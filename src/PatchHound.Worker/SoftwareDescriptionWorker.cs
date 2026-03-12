using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Worker;

public class SoftwareDescriptionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SoftwareDescriptionWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "SoftwareDescriptionWorker started with polling interval {Interval}",
            Interval
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during software description polling cycle");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessPendingJobsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var generationService =
            scope.ServiceProvider.GetRequiredService<SoftwareDescriptionGenerationService>();

        var job = await dbContext
            .SoftwareDescriptionJobs.IgnoreQueryFilters()
            .Where(item => item.Status == SoftwareDescriptionJobStatus.Pending)
            .OrderBy(item => item.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (job is null)
        {
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        job.Start(startedAt);
        await dbContext.SaveChangesAsync(ct);

        try
        {
            var result = await generationService.GenerateAsync(
                job.TenantId,
                job.TenantSoftwareId,
                job.TenantAiProfileId,
                ct
            );

            var trackedJob = await dbContext
                .SoftwareDescriptionJobs.IgnoreQueryFilters()
                .FirstAsync(item => item.Id == job.Id, ct);

            if (result.IsSuccess)
            {
                trackedJob.CompleteSucceeded(DateTimeOffset.UtcNow);
                logger.LogInformation(
                    "Completed software description job {JobId} for tenant software {TenantSoftwareId}",
                    trackedJob.Id,
                    trackedJob.TenantSoftwareId
                );
            }
            else
            {
                trackedJob.CompleteFailed(
                    DateTimeOffset.UtcNow,
                    result.Error ?? "Software description generation failed."
                );
                logger.LogWarning(
                    "Software description job {JobId} failed: {Error}",
                    trackedJob.Id,
                    trackedJob.Error
                );
            }

            await dbContext.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var trackedJob = await dbContext
                .SoftwareDescriptionJobs.IgnoreQueryFilters()
                .FirstAsync(item => item.Id == job.Id, ct);
            trackedJob.CompleteFailed(DateTimeOffset.UtcNow, ex.Message);
            await dbContext.SaveChangesAsync(ct);
            logger.LogError(ex, "Software description job {JobId} failed", trackedJob.Id);
        }
    }
}
