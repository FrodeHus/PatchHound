using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class ApprovalTaskService(
    PatchHoundDbContext dbContext,
    INotificationService notificationService,
    AuditLogWriter auditLogWriter,
    IRealTimeNotifier realTimeNotifier
)
{
    public async Task<ApprovalTask> CreateForDecisionAsync(
        RemediationDecision decision,
        int expiryHours,
        CancellationToken ct
    )
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(expiryHours);
        var task = ApprovalTask.Create(decision.TenantId, decision.Id, decision.Outcome, expiresAt);

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
                $"A remediation decision ({decision.Outcome}) requires your attention.",
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
        CancellationToken ct
    )
    {
        var task = await dbContext.ApprovalTasks
            .Include(at => at.RemediationDecision)
            .FirstOrDefaultAsync(at => at.Id == approvalTaskId, ct)
            ?? throw new InvalidOperationException("Approval task not found.");

        task.Approve(userId, justification);
        task.RemediationDecision.Approve(userId);

        await auditLogWriter.WriteAsync(
            task.TenantId,
            "RemediationDecision",
            task.RemediationDecisionId,
            AuditAction.Approved,
            null,
            new { ApprovalTaskId = task.Id, Justification = justification },
            ct
        );

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

        await auditLogWriter.WriteAsync(
            task.TenantId,
            "RemediationDecision",
            task.RemediationDecisionId,
            AuditAction.Denied,
            null,
            new { ApprovalTaskId = task.Id, Justification = justification },
            ct
        );

        // Notify the analyst who created the decision
        await notificationService.SendAsync(
            task.RemediationDecision.DecidedBy,
            task.TenantId,
            NotificationType.ApprovalTaskDenied,
            "Remediation decision denied",
            $"Your remediation decision ({task.RemediationDecision.Outcome}) was denied. Please cancel the decision and create a new one.",
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

            await notificationService.SendAsync(
                task.RemediationDecision.DecidedBy,
                task.TenantId,
                NotificationType.ApprovalTaskAutoExpired,
                "Approval task expired",
                $"Your remediation decision ({task.RemediationDecision.Outcome}) was auto-denied because the approval task expired.",
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
}
