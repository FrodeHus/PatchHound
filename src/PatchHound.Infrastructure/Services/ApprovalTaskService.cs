using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class ApprovalTaskService(
    PatchHoundDbContext dbContext,
    INotificationService notificationService,
    IRealTimeNotifier realTimeNotifier,
    RemediationWorkflowService remediationWorkflowService,
    PatchingTaskService patchingTaskService
)
{
    private static string BuildDecisionNotificationDetail(RemediationDecision decision)
    {
        var maintenanceWindowText = decision.MaintenanceWindowDate is DateTimeOffset maintenanceWindowDate
            ? $" Maintenance window: {maintenanceWindowDate:yyyy-MM-dd}."
            : string.Empty;

        return $"A remediation decision ({decision.Outcome}) requires your attention.{maintenanceWindowText}";
    }

    public async Task<ApprovalTask> CreateForDecisionAsync(
        RemediationDecision decision,
        int expiryHours,
        CancellationToken ct
    )
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(expiryHours);
        var initialStatus = decision.ApprovalStatus == DecisionApprovalStatus.PendingApproval
            ? ApprovalTaskStatus.Pending
            : ApprovalTaskStatus.AutoApproved;
        var task = ApprovalTask.Create(decision.TenantId, decision.RemediationCaseId, decision.Id, decision.Outcome, initialStatus, expiresAt);
        await remediationWorkflowService.AttachApprovalTaskAsync(task, decision, ct);

        await dbContext.ApprovalTasks.AddAsync(task, ct);
        await dbContext.SaveChangesAsync(ct);

        // Notify users who hold the required roles
        var roleStrings = task.VisibleRoles.Select(r => r.Role.ToString()).ToList();
        var userIds = await dbContext.UserTenantRoles
            .IgnoreQueryFilters()
            .Where(utr => utr.TenantId == decision.TenantId && roleStrings.Contains(utr.Role.ToString()))
            .Select(utr => utr.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            await notificationService.SendAsync(
                userId,
                decision.TenantId,
                NotificationType.ApprovalTaskCreated,
                task.Status == ApprovalTaskStatus.AutoApproved
                    ? "Informational: Remediation decision created"
                    : "Approval required: Remediation decision",
                BuildDecisionNotificationDetail(decision),
                "ApprovalTask",
                task.Id,
                ct
            );
        }

        await realTimeNotifier.NotifyApprovalTaskCreatedAsync(decision.TenantId, task.Id, ct);

        return task;
    }

    public async Task<ApprovalTask> ApproveAsync(
        Guid approvalTaskId,
        Guid userId,
        string? justification,
        DateTimeOffset? maintenanceWindowDate,
        CancellationToken ct
    )
    {
        var task = await dbContext.ApprovalTasks
            .Include(at => at.RemediationDecision)
            .FirstOrDefaultAsync(at => at.Id == approvalTaskId, ct)
            ?? throw new InvalidOperationException("Approval task not found.");

        if (task.RemediationDecision.Outcome == RemediationOutcome.ApprovedForPatching)
        {
            var effectiveMaintenanceWindowDate = maintenanceWindowDate ?? task.RemediationDecision.MaintenanceWindowDate;
            if (!effectiveMaintenanceWindowDate.HasValue)
            {
                throw new ArgumentException("Maintenance window date is required before approving patching.");
            }

            task.RemediationDecision.SetMaintenanceWindowDate(effectiveMaintenanceWindowDate);
        }

        task.Approve(userId, justification);
        task.RemediationDecision.Approve(userId);
        await remediationWorkflowService.HandleApprovalOutcomeAsync(
            task,
            task.RemediationDecision,
            true,
            userId,
            ct
        );

        if (task.RemediationDecision.Outcome == RemediationOutcome.ApprovedForPatching)
        {
            await patchingTaskService.EnsurePatchingTasksAsync(task.RemediationDecision, ct);
        }

        await NotifyApprovalAwarenessRoleAsync(task, task.RemediationDecision, ct);

        await dbContext.SaveChangesAsync(ct);
        return task;
    }

    public async Task<ApprovalTask> DenyAsync(
        Guid approvalTaskId,
        Guid userId,
        string? justification,
        CancellationToken ct
    )
    {
        var task = await dbContext.ApprovalTasks
            .Include(at => at.RemediationDecision)
            .FirstOrDefaultAsync(at => at.Id == approvalTaskId, ct)
            ?? throw new InvalidOperationException("Approval task not found.");

        task.Deny(userId, justification);
        task.RemediationDecision.Reject(userId);
        await remediationWorkflowService.HandleApprovalOutcomeAsync(
            task,
            task.RemediationDecision,
            false,
            userId,
            ct
        );

        // Notify the analyst who created the decision
        await notificationService.SendAsync(
            task.RemediationDecision.DecidedBy,
            task.TenantId,
            NotificationType.ApprovalTaskDenied,
            "Remediation decision denied",
            $"Your remediation decision ({task.RemediationDecision.Outcome}) was denied. Please cancel the decision and create a new one.{(task.RemediationDecision.MaintenanceWindowDate is DateTimeOffset maintenanceWindowDate ? $" Maintenance window had been set to {maintenanceWindowDate:yyyy-MM-dd}." : string.Empty)}",
            "ApprovalTask",
            task.Id,
            ct
        );

        await dbContext.SaveChangesAsync(ct);
        return task;
    }

    public async Task<int> AutoDenyExpiredAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredTasks = await dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .Include(at => at.RemediationDecision)
            .Where(at => at.Status == ApprovalTaskStatus.Pending && at.ExpiresAt <= now)
            .ToListAsync(ct);

        foreach (var task in expiredTasks)
        {
            task.AutoDeny();
            task.RemediationDecision.Reject(Guid.Empty);
            await remediationWorkflowService.HandleApprovalOutcomeAsync(
                task,
                task.RemediationDecision,
                false,
                null,
                ct
            );

            await notificationService.SendAsync(
                task.RemediationDecision.DecidedBy,
                task.TenantId,
                NotificationType.ApprovalTaskAutoExpired,
                "Approval task expired",
                $"Your remediation decision ({task.RemediationDecision.Outcome}) was auto-denied because the approval task expired.{(task.RemediationDecision.MaintenanceWindowDate is DateTimeOffset maintenanceWindowDate ? $" Maintenance window had been set to {maintenanceWindowDate:yyyy-MM-dd}." : string.Empty)}",
                "ApprovalTask",
                task.Id,
                ct
            );
        }

        if (expiredTasks.Count > 0)
            await dbContext.SaveChangesAsync(ct);

        return expiredTasks.Count;
    }

    public async Task MarkAsReadAsync(Guid approvalTaskId, CancellationToken ct)
    {
        var task = await dbContext.ApprovalTasks
            .FirstOrDefaultAsync(at => at.Id == approvalTaskId, ct)
            ?? throw new InvalidOperationException("Approval task not found.");

        task.MarkAsRead();
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task NotifyApprovalAwarenessRoleAsync(
        ApprovalTask task,
        RemediationDecision decision,
        CancellationToken ct
    )
    {
        var targetRole = decision.Outcome == RemediationOutcome.ApprovedForPatching
            ? RoleName.SecurityManager
            : RoleName.TechnicalManager;

        var userIds = await dbContext.UserTenantRoles
            .IgnoreQueryFilters()
            .Where(utr => utr.TenantId == decision.TenantId && utr.Role == targetRole)
            .Select(utr => utr.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            await notificationService.SendAsync(
                userId,
                decision.TenantId,
                NotificationType.ApprovalTaskApproved,
                "Remediation decision approved",
                $"A remediation decision ({decision.Outcome}) was approved.",
                "ApprovalTask",
                task.Id,
                ct
            );
        }
    }
}
