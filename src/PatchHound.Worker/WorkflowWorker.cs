using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Worker;

public class WorkflowWorker(IServiceScopeFactory scopeFactory, ILogger<WorkflowWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("WorkflowWorker started with polling interval {Interval}", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTimeoutsAsync(stoppingToken);
                await ResumeReadyMergeNodesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during workflow processing cycle");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>
    /// Time out any pending workflow actions that have exceeded their DueAt deadline.
    /// </summary>
    private async Task ProcessTimeoutsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        var now = DateTimeOffset.UtcNow;

        var overdueActions = await dbContext.WorkflowActions
            .IgnoreQueryFilters()
            .Where(a =>
                a.Status == WorkflowActionStatus.Pending
                && a.DueAt != null
                && a.DueAt < now
            )
            .Include(a => a.NodeExecution)
            .ToListAsync(ct);

        if (overdueActions.Count == 0)
            return;

        logger.LogInformation("Found {Count} overdue workflow actions", overdueActions.Count);

        var affectedInstanceIds = new HashSet<Guid>();

        foreach (var action in overdueActions)
        {
            action.TimeOut();
            action.NodeExecution.Fail("Action timed out");
            affectedInstanceIds.Add(action.WorkflowInstanceId);
        }

        // Fail affected instances
        var instances = await dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .Where(i => affectedInstanceIds.Contains(i.Id)
                && i.Status != WorkflowInstanceStatus.Failed
                && i.Status != WorkflowInstanceStatus.Completed
                && i.Status != WorkflowInstanceStatus.Cancelled)
            .ToListAsync(ct);

        foreach (var instance in instances)
        {
            instance.Fail("Action timed out");
            logger.LogWarning("Workflow instance {InstanceId} failed due to action timeout", instance.Id);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Check waiting merge nodes to see if all incoming branches have now completed.
    /// </summary>
    private async Task ResumeReadyMergeNodesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();

        // Find instances that are waiting and might have merge nodes ready
        var waitingInstances = await dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .Where(i => i.Status == WorkflowInstanceStatus.WaitingForAction)
            .Select(i => i.Id)
            .ToListAsync(ct);

        foreach (var instanceId in waitingInstances)
        {
            try
            {
                await engine.ResumeWorkflowAsync(instanceId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error resuming workflow instance {InstanceId}", instanceId);
            }
        }
    }
}
