using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Core.Services;
using Vigil.Infrastructure.Data;

namespace Vigil.Worker;

public class SlaCheckWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SlaCheckWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SlaCheckWorker started");

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
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VigilDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var slaService = scope.ServiceProvider.GetRequiredService<SlaService>();

        var now = DateTimeOffset.UtcNow;

        // Get all open/in-progress tasks across all tenants
        var activeTasks = await dbContext.RemediationTasks
            .IgnoreQueryFilters()
            .Where(t => t.Status != RemediationTaskStatus.Completed
                        && t.Status != RemediationTaskStatus.RiskAccepted)
            .ToListAsync(ct);

        foreach (var task in activeTasks)
        {
            var status = slaService.GetSlaStatus(task.CreatedAt, task.DueDate, now);

            switch (status)
            {
                case SlaStatus.Overdue:
                    logger.LogWarning(
                        "Task {TaskId} for tenant {TenantId} is overdue (due: {DueDate})",
                        task.Id, task.TenantId, task.DueDate);

                    await notificationService.SendAsync(
                        task.AssigneeId,
                        task.TenantId,
                        NotificationType.SLAWarning,
                        "Remediation task overdue",
                        $"Task {task.Id} is past its SLA due date of {task.DueDate:yyyy-MM-dd}.",
                        "RemediationTask",
                        task.Id,
                        ct);
                    break;

                case SlaStatus.NearDue:
                    logger.LogInformation(
                        "Task {TaskId} for tenant {TenantId} is near SLA due date ({DueDate})",
                        task.Id, task.TenantId, task.DueDate);

                    await notificationService.SendAsync(
                        task.AssigneeId,
                        task.TenantId,
                        NotificationType.SLAWarning,
                        "Remediation task nearing SLA deadline",
                        $"Task {task.Id} is approaching its SLA due date of {task.DueDate:yyyy-MM-dd}.",
                        "RemediationTask",
                        task.Id,
                        ct);
                    break;
            }
        }
    }
}
