using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        // Get all open/in-progress tasks across all tenants
        var activeTasks = await dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .Where(t =>
                t.Status != RemediationTaskStatus.Completed
                && t.Status != RemediationTaskStatus.RiskAccepted
                && (t.LastSlaNotifiedAt == null || t.LastSlaNotifiedAt < cooldownThreshold)
            )
            .ToListAsync(ct);

        foreach (var task in activeTasks)
        {
            if (task.AssigneeId == Guid.Empty)
            {
                logger.LogWarning(
                    "Task {TaskId} for tenant {TenantId} has no assignee, skipping SLA notification",
                    task.Id,
                    task.TenantId
                );
                continue;
            }

            var status = slaService.GetSlaStatus(task.CreatedAt, task.DueDate, now);

            switch (status)
            {
                case SlaStatus.Overdue:
                    logger.LogWarning(
                        "Task {TaskId} for tenant {TenantId} is overdue (due: {DueDate})",
                        task.Id,
                        task.TenantId,
                        task.DueDate
                    );

                    await notificationService.SendAsync(
                        task.AssigneeId,
                        task.TenantId,
                        NotificationType.SLAWarning,
                        "Remediation task overdue",
                        $"Task {task.Id} is past its SLA due date of {task.DueDate:yyyy-MM-dd}.",
                        "RemediationTask",
                        task.Id,
                        ct
                    );
                    task.MarkSlaNotified();
                    await dbContext.SaveChangesAsync(ct);
                    notificationsSent++;
                    break;

                case SlaStatus.NearDue:
                    logger.LogInformation(
                        "Task {TaskId} for tenant {TenantId} is near SLA due date ({DueDate})",
                        task.Id,
                        task.TenantId,
                        task.DueDate
                    );

                    await notificationService.SendAsync(
                        task.AssigneeId,
                        task.TenantId,
                        NotificationType.SLAWarning,
                        "Remediation task nearing SLA deadline",
                        $"Task {task.Id} is approaching its SLA due date of {task.DueDate:yyyy-MM-dd}.",
                        "RemediationTask",
                        task.Id,
                        ct
                    );
                    task.MarkSlaNotified();
                    await dbContext.SaveChangesAsync(ct);
                    notificationsSent++;
                    break;
            }
        }

        logger.LogInformation(
            "Completed SLA check cycle at {CycleCompletedAt}. Active tasks evaluated: {ActiveTaskCount}. Notifications sent: {NotificationsSent}.",
            DateTimeOffset.UtcNow,
            activeTasks.Count,
            notificationsSent
        );
    }
}
