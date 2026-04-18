using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Workers;

public class TenantDeletionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TenantDeletionWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await RecoverStaleJobsAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during tenant deletion processing cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private async Task RecoverStaleJobsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        var recovered = await dbContext.TenantDeletionJobs
            .IgnoreQueryFilters()
            .Where(j => j.Status == TenantDeletionJobStatus.Running)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, TenantDeletionJobStatus.Pending)
                      .SetProperty(j => j.StartedAt, (DateTimeOffset?)null),
                ct);

        if (recovered > 0)
            logger.LogWarning("Recovered {Count} stale Running deletion job(s) to Pending on startup", recovered);
    }

    private async Task ProcessNextJobAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        var pendingJob = await dbContext.TenantDeletionJobs
            .IgnoreQueryFilters()
            .Where(j => j.Status == TenantDeletionJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (pendingJob is null)
            return;

        var claimed = await dbContext.TenantDeletionJobs
            .IgnoreQueryFilters()
            .Where(j => j.Id == pendingJob.Id && j.Status == TenantDeletionJobStatus.Pending)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, TenantDeletionJobStatus.Running)
                      .SetProperty(j => j.StartedAt, DateTimeOffset.UtcNow),
                ct);

        if (claimed == 0)
            return;

        var tenantId = pendingJob.TenantId;
        var userId = pendingJob.RequestedByUserId.ToString();

        logger.LogInformation("Processing deletion job for tenant {TenantId}", tenantId);

        var eventPusher = scope.ServiceProvider.GetRequiredService<IEventPusher>();

        try
        {
            var deletionService = scope.ServiceProvider.GetRequiredService<TenantDeletionService>();
            await deletionService.DeleteAsync(tenantId, ct);

            await dbContext.TenantDeletionJobs
                .IgnoreQueryFilters()
                .Where(j => j.Id == pendingJob.Id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(j => j.Status, TenantDeletionJobStatus.Completed)
                          .SetProperty(j => j.CompletedAt, DateTimeOffset.UtcNow),
                    ct);

            logger.LogInformation("Tenant {TenantId} deleted successfully", tenantId);
            await eventPusher.PushAsync(
                "TenantDeleted",
                new { tenantId },
                userId: userId,
                ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to delete tenant {TenantId}", tenantId);

            await dbContext.TenantDeletionJobs
                .IgnoreQueryFilters()
                .Where(j => j.Id == pendingJob.Id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(j => j.Status, TenantDeletionJobStatus.Failed)
                          .SetProperty(j => j.Error, ex.Message)
                          .SetProperty(j => j.CompletedAt, DateTimeOffset.UtcNow),
                    ct);

            await eventPusher.PushAsync(
                "TenantDeletionFailed",
                new { tenantId },
                userId: userId,
                ct: ct);
        }
    }
}
