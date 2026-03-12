using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Worker;

public class EnrichmentWorker(IServiceScopeFactory scopeFactory, ILogger<EnrichmentWorker> logger)
    : BackgroundService
{
    private sealed record EnrichmentSourceSnapshot(
        Guid Id,
        string SourceKey,
        string DisplayName,
        bool HasConfiguredCredentials
    );

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);
    private const int MaxJobsPerCycle = 10;
    private static readonly string LeaseOwner =
        $"{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "EnrichmentWorker started with polling interval {Interval}",
            Interval
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunEnrichmentCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during enrichment cycle");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunEnrichmentCycleAsync(CancellationToken ct)
    {
        logger.LogInformation(
            "Starting enrichment polling cycle at {CycleStartedAt}",
            DateTimeOffset.UtcNow
        );

        var sources = await LoadEnabledSourcesAsync(ct);
        var startedRuns = 0;

        foreach (var source in sources)
        {
            if (!HasConfiguredCredentials(source))
            {
                continue;
            }

            var runStarted = await RunSourceCycleAsync(source, ct);
            if (runStarted)
            {
                startedRuns++;
            }
        }

        logger.LogInformation(
            "Completed enrichment polling cycle at {CycleCompletedAt}. Enabled sources: {SourceCount}. Started runs: {StartedRuns}.",
            DateTimeOffset.UtcNow,
            sources.Count,
            startedRuns
        );
    }

    private async Task<List<EnrichmentSourceSnapshot>> LoadEnabledSourcesAsync(
        CancellationToken ct
    )
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var secretStore = scope.ServiceProvider.GetRequiredService<ISecretStore>();

        var openBaoStatus = await secretStore.GetStatusAsync(ct);
        if (!openBaoStatus.IsAvailable || !openBaoStatus.IsInitialized || openBaoStatus.IsSealed)
        {
            logger.LogWarning(
                "Skipping enrichment cycle because OpenBao is not ready. Available: {IsAvailable}. Initialized: {IsInitialized}. Sealed: {IsSealed}",
                openBaoStatus.IsAvailable,
                openBaoStatus.IsInitialized,
                openBaoStatus.IsSealed
            );
            return [];
        }

        return await dbContext
            .EnrichmentSourceConfigurations.AsNoTracking()
            .Where(source => source.Enabled)
            .OrderBy(source => source.DisplayName)
            .Select(source => new EnrichmentSourceSnapshot(
                source.Id,
                source.SourceKey,
                source.DisplayName,
                !string.IsNullOrWhiteSpace(source.SecretRef)
            ))
            .ToListAsync(ct);
    }

    private async Task<bool> RunSourceCycleAsync(
        EnrichmentSourceSnapshot source,
        CancellationToken ct
    )
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var runners = scope.ServiceProvider.GetServices<IEnrichmentSourceRunner>().ToList();

        var runner = runners.FirstOrDefault(item =>
            string.Equals(item.SourceKey, source.SourceKey, StringComparison.OrdinalIgnoreCase)
        );
        if (runner is null)
        {
            return false;
        }

        var leaseAcquiredAt = DateTimeOffset.UtcNow;
        var leaseExpiresAt = leaseAcquiredAt.Add(LeaseDuration);
        var run = EnrichmentRun.Start(source.SourceKey, leaseAcquiredAt);

        var acquired = await TryAcquireLeaseAsync(
            dbContext,
            source.Id,
            run.Id,
            leaseAcquiredAt,
            leaseExpiresAt,
            ct
        );
        if (!acquired)
        {
            return false;
        }

        var trackedSource = await dbContext
            .EnrichmentSourceConfigurations.IgnoreQueryFilters()
            .FirstAsync(item => item.Id == source.Id, ct);

        var runCompleted = false;

        try
        {
            await ResetExpiredRunningJobsAsync(dbContext, source.SourceKey, ct);

            await dbContext.EnrichmentRuns.AddAsync(run, ct);
            await dbContext.SaveChangesAsync(ct);

            await UpdateRuntimeAsync(
                dbContext,
                trackedSource,
                runtime =>
                    runtime.UpdateRuntime(
                        leaseAcquiredAt,
                        null,
                        runtime.LastSucceededAt,
                        "Running",
                        string.Empty
                    ),
                ct
            );

            var jobs = await ClaimDueJobsAsync(dbContext, source.SourceKey, runner.TargetModel, ct);
            if (jobs.Count == 0)
            {
                var completedAt = DateTimeOffset.UtcNow;
                run.Complete(EnrichmentRunStatus.NoWork, 0, 0, 0, 0, 0, completedAt);
                await dbContext.SaveChangesAsync(ct);

                await UpdateRuntimeAsync(
                    dbContext,
                    trackedSource,
                    runtime =>
                        runtime.UpdateRuntime(
                            leaseAcquiredAt,
                            completedAt,
                            runtime.LastSucceededAt,
                            "NoWork",
                            string.Empty
                        ),
                    ct
                );

                runCompleted = true;
                return true;
            }

            var succeeded = 0;
            var noData = 0;
            var failed = 0;
            var retried = 0;

            foreach (var job in jobs)
            {
                var result = await runner.ExecuteAsync(job, ct);
                var completedAt = DateTimeOffset.UtcNow;

                switch (result.Outcome)
                {
                    case EnrichmentJobExecutionOutcome.Succeeded:
                        job.Complete(EnrichmentJobStatus.Succeeded, completedAt);
                        succeeded++;
                        break;
                    case EnrichmentJobExecutionOutcome.NoData:
                        job.Complete(EnrichmentJobStatus.Skipped, completedAt, result.Error);
                        noData++;
                        break;
                    case EnrichmentJobExecutionOutcome.Retry:
                        job.ScheduleRetry(
                            result.NextAttemptAt ?? completedAt.AddMinutes(5),
                            completedAt,
                            result.Error ?? "Retry requested."
                        );
                        retried++;
                        break;
                    default:
                        job.Complete(
                            EnrichmentJobStatus.Failed,
                            completedAt,
                            result.Error ?? "Enrichment failed."
                        );
                        failed++;
                        break;
                }

                await dbContext.SaveChangesAsync(ct);

                if (job != jobs[^1] && runner.MinimumDelay > TimeSpan.Zero)
                {
                    await Task.Delay(runner.MinimumDelay, ct);
                }
            }

            var runCompletedAt = DateTimeOffset.UtcNow;
            var runStatus = EnrichmentRunStatus.Succeeded;
            var runtimeStatus = "Succeeded";
            var runtimeError = string.Empty;

            if (failed > 0 && succeeded == 0 && noData == 0 && retried == 0)
            {
                runStatus = EnrichmentRunStatus.Failed;
                runtimeStatus = "Failed";
                runtimeError = $"{failed} enrichment job(s) failed.";
            }
            else if (retried > 0 && succeeded == 0 && noData == 0 && failed == 0)
            {
                runStatus = EnrichmentRunStatus.RetryScheduled;
                runtimeStatus = "RetryScheduled";
                runtimeError = $"{retried} enrichment job(s) scheduled for retry.";
            }
            else if (failed > 0)
            {
                runtimeStatus = "SucceededWithFailures";
                runtimeError = $"{failed} enrichment job(s) failed.";
            }
            else if (retried > 0)
            {
                runtimeStatus = "SucceededWithRetries";
                runtimeError = $"{retried} enrichment job(s) scheduled for retry.";
            }

            run.Complete(
                runStatus,
                jobs.Count,
                succeeded,
                noData,
                failed,
                retried,
                runCompletedAt,
                runtimeError
            );
            await dbContext.SaveChangesAsync(ct);

            await UpdateRuntimeAsync(
                dbContext,
                trackedSource,
                runtime =>
                    runtime.UpdateRuntime(
                        leaseAcquiredAt,
                        runCompletedAt,
                        succeeded > 0 ? runCompletedAt : runtime.LastSucceededAt,
                        runtimeStatus,
                        runtimeError
                    ),
                ct
            );

            runCompleted = true;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error during enrichment run for source {SourceKey}",
                source.SourceKey
            );

            var failedAt = DateTimeOffset.UtcNow;
            run.Complete(EnrichmentRunStatus.Failed, 0, 0, 0, 0, 0, failedAt, ex.GetType().Name);
            await dbContext.SaveChangesAsync(ct);

            await UpdateRuntimeAsync(
                dbContext,
                trackedSource,
                runtime =>
                    runtime.UpdateRuntime(
                        leaseAcquiredAt,
                        failedAt,
                        runtime.LastSucceededAt,
                        "Failed",
                        ex.GetType().Name
                    ),
                ct
            );

            runCompleted = true;
            return false;
        }
        finally
        {
            await ReleaseLeaseAsync(scopeFactory, source.Id, run.Id, ct);
            if (!runCompleted)
            {
                logger.LogWarning(
                    "Released enrichment lease for source {SourceKey} without a completed run record.",
                    source.SourceKey
                );
            }
        }
    }

    private async Task<List<EnrichmentJob>> ClaimDueJobsAsync(
        PatchHoundDbContext dbContext,
        string sourceKey,
        EnrichmentTargetModel targetModel,
        CancellationToken ct
    )
    {
        var now = DateTimeOffset.UtcNow;
        var leaseExpiresAt = now.Add(LeaseDuration);
        var jobs = await dbContext
            .EnrichmentJobs.IgnoreQueryFilters()
            .Where(job =>
                job.SourceKey == sourceKey
                && job.TargetModel == targetModel
                && (
                    job.Status == EnrichmentJobStatus.Pending
                    || job.Status == EnrichmentJobStatus.RetryScheduled
                )
                && job.NextAttemptAt <= now
            )
            .OrderByDescending(job => job.Priority)
            .ThenBy(job => job.NextAttemptAt)
            .Take(MaxJobsPerCycle)
            .ToListAsync(ct);

        foreach (var job in jobs)
        {
            job.Start(LeaseOwner, now, leaseExpiresAt);
        }

        if (jobs.Count > 0)
        {
            await dbContext.SaveChangesAsync(ct);
        }

        return jobs;
    }

    private async Task ResetExpiredRunningJobsAsync(
        PatchHoundDbContext dbContext,
        string sourceKey,
        CancellationToken ct
    )
    {
        var now = DateTimeOffset.UtcNow;
        var expiredJobs = await dbContext
            .EnrichmentJobs.IgnoreQueryFilters()
            .Where(job =>
                job.SourceKey == sourceKey
                && job.Status == EnrichmentJobStatus.Running
                && job.LeaseExpiresAt.HasValue
                && job.LeaseExpiresAt < now
            )
            .ToListAsync(ct);

        foreach (var job in expiredJobs)
        {
            job.ScheduleRetry(now, now, "Previous enrichment lease expired.");
        }

        if (expiredJobs.Count > 0)
        {
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private async Task<bool> TryAcquireLeaseAsync(
        PatchHoundDbContext dbContext,
        Guid sourceId,
        Guid runId,
        DateTimeOffset acquiredAt,
        DateTimeOffset leaseExpiresAt,
        CancellationToken ct
    )
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext
            .EnrichmentSourceConfigurations.IgnoreQueryFilters()
            .Where(source =>
                source.Id == sourceId
                && (
                    source.ActiveEnrichmentRunId == null
                    || source.LeaseExpiresAt == null
                    || source.LeaseExpiresAt < now
                )
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(source => source.ActiveEnrichmentRunId, runId)
                        .SetProperty(source => source.LeaseAcquiredAt, acquiredAt)
                        .SetProperty(source => source.LeaseExpiresAt, leaseExpiresAt),
                ct
            );

        return updated == 1;
    }

    private async Task UpdateRuntimeAsync(
        PatchHoundDbContext dbContext,
        EnrichmentSourceConfiguration source,
        Action<EnrichmentSourceConfiguration> update,
        CancellationToken ct
    )
    {
        update(source);
        await dbContext.SaveChangesAsync(ct);
    }

    private static async Task ReleaseLeaseAsync(
        IServiceScopeFactory scopeFactory,
        Guid sourceId,
        Guid runId,
        CancellationToken ct
    )
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        await dbContext
            .EnrichmentSourceConfigurations.IgnoreQueryFilters()
            .Where(source => source.Id == sourceId && source.ActiveEnrichmentRunId == runId)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(source => source.ActiveEnrichmentRunId, (Guid?)null)
                        .SetProperty(source => source.LeaseAcquiredAt, (DateTimeOffset?)null)
                        .SetProperty(source => source.LeaseExpiresAt, (DateTimeOffset?)null),
                ct
            );
    }

    private static bool HasConfiguredCredentials(EnrichmentSourceSnapshot source)
    {
        return source.HasConfiguredCredentials;
    }
}
