using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Worker;

public class SlaCheckWorker(IServiceScopeFactory scopeFactory, ILogger<SlaCheckWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan NotificationCooldown = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SlaCheckWorker started with polling interval {Interval}", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckSlaStatusAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during SLA check cycle");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CheckSlaStatusAsync(CancellationToken ct)
    {
        var cycleStartedAt = DateTimeOffset.UtcNow;
        logger.LogInformation("Starting SLA check cycle at {CycleStartedAt}", cycleStartedAt);

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var slaService = scope.ServiceProvider.GetRequiredService<SlaService>();

        var now = DateTimeOffset.UtcNow;
        var cooldownThreshold = now - NotificationCooldown;
        var notificationsSent = 0;

        // --- Remediation decisions: PatchingDeferred approaching re-evaluation ---
        var reEvalThreshold = now.AddDays(3);
        var deferredDecisions = await dbContext
            .RemediationDecisions.IgnoreQueryFilters()
            .Where(d =>
                d.Outcome == RemediationOutcome.PatchingDeferred
                && d.ApprovalStatus == DecisionApprovalStatus.Approved
                && d.ReEvaluationDate.HasValue
                && d.ReEvaluationDate.Value <= reEvalThreshold
                && (d.LastSlaNotifiedAt == null || d.LastSlaNotifiedAt < cooldownThreshold)
            )
            .ToListAsync(ct);

        foreach (var decision in deferredDecisions)
        {
            await notificationService.SendAsync(
                decision.DecidedBy,
                decision.TenantId,
                NotificationType.SLAWarning,
                "Deferred patching decision approaching re-evaluation",
                $"Remediation decision {decision.Id} for remediation case {decision.RemediationCaseId} has a re-evaluation date of {decision.ReEvaluationDate!.Value:yyyy-MM-dd}.",
                "RemediationDecision",
                decision.Id,
                ct
            );
            decision.MarkSlaNotified();
            await dbContext.SaveChangesAsync(ct);
            notificationsSent++;
        }

        // --- Remediation decisions: approaching expiry ---
        var expiryThreshold = now.AddDays(7);
        var expiringDecisions = await dbContext
            .RemediationDecisions.IgnoreQueryFilters()
            .Where(d =>
                d.ApprovalStatus == DecisionApprovalStatus.Approved
                && d.ExpiryDate.HasValue
                && d.ExpiryDate.Value <= expiryThreshold
                && (d.LastSlaNotifiedAt == null || d.LastSlaNotifiedAt < cooldownThreshold)
            )
            .ToListAsync(ct);

        foreach (var decision in expiringDecisions)
        {
            await notificationService.SendAsync(
                decision.DecidedBy,
                decision.TenantId,
                NotificationType.SLAWarning,
                "Remediation decision approaching expiry",
                $"Remediation decision {decision.Id} for remediation case {decision.RemediationCaseId} expires on {decision.ExpiryDate!.Value:yyyy-MM-dd}.",
                "RemediationDecision",
                decision.Id,
                ct
            );
            decision.MarkSlaNotified();
            await dbContext.SaveChangesAsync(ct);
            notificationsSent++;
        }

        // --- Patching tasks approaching SLA ---
        var activePatchingTasks = await dbContext
            .PatchingTasks.IgnoreQueryFilters()
            .Where(pt =>
                pt.Status != PatchingTaskStatus.Completed
            )
            .ToListAsync(ct);

        foreach (var patchingTask in activePatchingTasks)
        {
            var status = slaService.GetSlaStatus(patchingTask.CreatedAt, patchingTask.DueDate, now);

            if (status is SlaStatus.Overdue or SlaStatus.NearDue)
            {
                var title = status == SlaStatus.Overdue
                    ? "Patching task overdue"
                    : "Patching task nearing SLA deadline";
                var body = status == SlaStatus.Overdue
                    ? $"Patching task {patchingTask.Id} for remediation case {patchingTask.RemediationCaseId} is past its SLA due date of {patchingTask.DueDate:yyyy-MM-dd}."
                    : $"Patching task {patchingTask.Id} for remediation case {patchingTask.RemediationCaseId} is approaching its SLA due date of {patchingTask.DueDate:yyyy-MM-dd}.";

                // Notify team members
                var teamMembers = await dbContext.TeamMembers.IgnoreQueryFilters()
                    .Where(tm => tm.TeamId == patchingTask.OwnerTeamId)
                    .Select(tm => tm.UserId)
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var userId in teamMembers)
                {
                    await notificationService.SendAsync(
                        userId,
                        patchingTask.TenantId,
                        NotificationType.SLAWarning,
                        title,
                        body,
                        "PatchingTask",
                        patchingTask.Id,
                        ct
                    );
                    notificationsSent++;
                }
            }
        }

        logger.LogInformation(
            "Completed SLA check cycle at {CycleCompletedAt}. Decisions checked: {DecisionsCount}. Patching tasks: {PatchingTaskCount}. Notifications sent: {NotificationsSent}.",
            DateTimeOffset.UtcNow,
            deferredDecisions.Count + expiringDecisions.Count,
            activePatchingTasks.Count,
            notificationsSent
        );
    }
}
