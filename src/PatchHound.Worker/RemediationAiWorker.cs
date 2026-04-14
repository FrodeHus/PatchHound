using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Worker;

public class RemediationAiWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RemediationAiWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RemediationAiWorker started with polling interval {Interval}", Interval);

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
                logger.LogError(ex, "Error during remediation AI polling cycle");
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
        var queryService = scope.ServiceProvider.GetRequiredService<PatchHound.Api.Services.RemediationDecisionQueryService>();

        var job = await dbContext.RemediationAiJobs.IgnoreQueryFilters()
            .Where(item => item.Status == RemediationAiJobStatus.Pending)
            .OrderBy(item => item.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (job is null)
        {
            return;
        }

        job.Start(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(ct);

        try
        {
            var generated = await queryService.GenerateAndStoreAiDraftsAsync(job.TenantId, job.RemediationCaseId, ct);
            var trackedJob = await dbContext.RemediationAiJobs.IgnoreQueryFilters()
                .FirstAsync(item => item.Id == job.Id, ct);

            if (generated)
            {
                trackedJob.CompleteSucceeded(DateTimeOffset.UtcNow);
                logger.LogInformation(
                    "Completed remediation AI job {JobId} for remediation case {RemediationCaseId}",
                    trackedJob.Id,
                    trackedJob.RemediationCaseId
                );
            }
            else
            {
                trackedJob.CompleteFailed(DateTimeOffset.UtcNow, "AI drafts could not be generated.");
                logger.LogWarning(
                    "Remediation AI job {JobId} did not produce drafts",
                    trackedJob.Id
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
            var trackedJob = await dbContext.RemediationAiJobs.IgnoreQueryFilters()
                .FirstAsync(item => item.Id == job.Id, ct);
            trackedJob.CompleteFailed(DateTimeOffset.UtcNow, ex.Message);
            await dbContext.SaveChangesAsync(ct);
            logger.LogError(ex, "Remediation AI job {JobId} failed", trackedJob.Id);
        }
    }
}
