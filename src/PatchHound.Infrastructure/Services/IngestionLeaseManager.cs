using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

internal sealed record AcquiredIngestionRun(IngestionRun Run, bool Resumed);

public class IngestionLeaseManager
{
    private const int MaxPersistenceAttempts = 2;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IngestionArtifactRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan FailedIngestionRetention = TimeSpan.FromHours(24);

    private readonly PatchHoundDbContext _dbContext;
    private readonly ILogger<IngestionLeaseManager> _logger;

    public IngestionLeaseManager(PatchHoundDbContext dbContext, ILogger<IngestionLeaseManager> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    internal async Task<AcquiredIngestionRun?> TryAcquireIngestionRunAsync(
        Guid tenantId,
        string sourceKey,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            if (IsInMemoryProvider())
            {
                var sourceConfiguration = await _dbContext
                    .TenantSourceConfigurations.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                        ct
                    );

                if (sourceConfiguration is null)
                {
                    return null;
                }

                if (sourceConfiguration.ActiveIngestionRunId.HasValue)
                {
                    await FinalizeAbortedRunIfPendingAsync(
                        sourceConfiguration.ActiveIngestionRunId.Value,
                        tenantId,
                        normalizedSourceKey,
                        now,
                        ct
                    );
                    await _dbContext.Entry(sourceConfiguration).ReloadAsync(ct);
                }

                if (
                    sourceConfiguration.ActiveIngestionRunId.HasValue
                    && sourceConfiguration.LeaseExpiresAt >= now
                )
                {
                    return null;
                }

                IngestionRun? resumableRun = null;
                if (sourceConfiguration.ActiveIngestionRunId.HasValue)
                {
                    resumableRun = await _dbContext
                        .IngestionRuns.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(
                            item =>
                                item.Id == sourceConfiguration.ActiveIngestionRunId.Value
                                && item.AbortRequestedAt == null
                                && (
                                    item.Status == IngestionRunStatuses.Staging
                                    || item.Status == IngestionRunStatuses.MergePending
                                    || item.Status == IngestionRunStatuses.Merging
                                )
                                && !item.CompletedAt.HasValue,
                            ct
                        );
                }

                var resumed = resumableRun is not null;
                var run = resumableRun ?? IngestionRun.Start(tenantId, normalizedSourceKey, now);
                sourceConfiguration.AcquireLease(run.Id, now, now.Add(LeaseDuration));
                if (!resumed)
                {
                    await _dbContext.IngestionRuns.AddAsync(run, ct);
                }

                await _dbContext.SaveChangesAsync(ct);
                return new AcquiredIngestionRun(run, resumed);
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

            var persistedSourceConfiguration = await _dbContext
                .TenantSourceConfigurations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                    ct
                );

            if (persistedSourceConfiguration is null)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            if (persistedSourceConfiguration.ActiveIngestionRunId.HasValue)
            {
                await FinalizeAbortedRunIfPendingAsync(
                    persistedSourceConfiguration.ActiveIngestionRunId.Value,
                    tenantId,
                    normalizedSourceKey,
                    now,
                    ct
                );
                persistedSourceConfiguration = await _dbContext
                    .TenantSourceConfigurations.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                        ct
                    );

                if (persistedSourceConfiguration is null)
                {
                    await transaction.RollbackAsync(ct);
                    return null;
                }
            }

            if (
                persistedSourceConfiguration.ActiveIngestionRunId.HasValue
                && persistedSourceConfiguration.LeaseExpiresAt >= now
            )
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            IngestionRun? resumable = null;
            if (persistedSourceConfiguration.ActiveIngestionRunId.HasValue)
            {
                resumable = await _dbContext
                    .IngestionRuns.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        item =>
                            item.Id == persistedSourceConfiguration.ActiveIngestionRunId.Value
                            && item.AbortRequestedAt == null
                            && (
                                item.Status == IngestionRunStatuses.Staging
                                || item.Status == IngestionRunStatuses.MergePending
                                || item.Status == IngestionRunStatuses.Merging
                            )
                            && !item.CompletedAt.HasValue,
                        ct
                    );
            }

            var resumedRun = resumable is not null;
            var acquiredRun = resumable ?? IngestionRun.Start(tenantId, normalizedSourceKey, now);

            var updatedRows = await _dbContext
                .TenantSourceConfigurations.IgnoreQueryFilters()
                .Where(item => item.Id == persistedSourceConfiguration.Id)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(item => item.ActiveIngestionRunId, acquiredRun.Id)
                            .SetProperty(item => item.LeaseAcquiredAt, now)
                            .SetProperty(item => item.LeaseExpiresAt, now.Add(LeaseDuration)),
                    ct
                );

            if (updatedRows == 0)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            if (!resumedRun)
            {
                await _dbContext.IngestionRuns.AddAsync(acquiredRun, ct);
            }

            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new AcquiredIngestionRun(acquiredRun, resumedRun);
        });
    }

    public async Task FinalizeAbortedRunIfPendingAsync(
        Guid runId,
        Guid tenantId,
        string sourceKey,
        DateTimeOffset completedAt,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();

        if (IsInMemoryProvider())
        {
            var run = await _dbContext
                .IngestionRuns.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == runId, ct);

            if (
                run is null
                || run.CompletedAt.HasValue
                || !run.AbortRequestedAt.HasValue
                || !IngestionRunStatePolicy.IsActive(run.Status)
            )
            {
                return;
            }

            _dbContext.Entry(run).Property(nameof(IngestionRun.CompletedAt)).CurrentValue =
                completedAt;
            _dbContext.Entry(run).Property(nameof(IngestionRun.Status)).CurrentValue =
                IngestionRunStatuses.FailedTerminal;
            _dbContext.Entry(run).Property(nameof(IngestionRun.Error)).CurrentValue =
                IngestionFailurePolicy.Describe(new IngestionAbortedException());
            var source = await _dbContext
                .TenantSourceConfigurations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    item =>
                        item.TenantId == tenantId
                        && item.SourceKey == normalizedSourceKey
                        && item.ActiveIngestionRunId == runId,
                    ct
                );
            source?.ReleaseLease(runId);
            await _dbContext.SaveChangesAsync(ct);
            return;
        }

        await _dbContext
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item =>
                item.Id == runId
                && item.AbortRequestedAt != null
                && item.CompletedAt == null
                && (
                    item.Status == IngestionRunStatuses.Staging
                    || item.Status == IngestionRunStatuses.MergePending
                    || item.Status == IngestionRunStatuses.Merging
                ))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.CompletedAt, completedAt)
                    .SetProperty(item => item.Status, IngestionRunStatuses.FailedTerminal)
                    .SetProperty(
                        item => item.Error,
                        IngestionFailurePolicy.Describe(new IngestionAbortedException())
                    ),
                ct
            );

        await _dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.SourceKey == normalizedSourceKey
                && item.ActiveIngestionRunId == runId)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(item => item.ActiveIngestionRunId, (Guid?)null)
                        .SetProperty(item => item.LeaseAcquiredAt, (DateTimeOffset?)null)
                        .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null),
                ct
            );
    }

    public async Task ThrowIfAbortRequestedAsync(Guid runId, CancellationToken ct)
    {
        var abortRequestedAt = await _dbContext
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item => item.Id == runId)
            .Select(item => item.AbortRequestedAt)
            .FirstOrDefaultAsync(ct);

        if (abortRequestedAt.HasValue)
        {
            _dbContext.ChangeTracker.Clear();
            throw new IngestionAbortedException();
        }
    }

    internal async Task UpdateRuntimeStateAsync(
        Guid tenantId,
        string sourceKey,
        Action<TenantIngestionRuntimeState> update,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var tenant = await _dbContext
            .Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return;
        }

        if (IsInMemoryProvider())
        {
            var trackedSource = await _dbContext
                .TenantSourceConfigurations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                    ct
                );

            if (trackedSource is null)
            {
                return;
            }

            var runtime = new TenantIngestionRuntimeState(
                trackedSource.ManualRequestedAt,
                trackedSource.LastStartedAt,
                trackedSource.LastCompletedAt,
                trackedSource.LastSucceededAt,
                trackedSource.LastStatus,
                trackedSource.LastError
            );
            update(runtime);

            trackedSource.UpdateRuntime(
                runtime.ManualRequestedAt,
                runtime.LastStartedAt,
                runtime.LastCompletedAt,
                runtime.LastSucceededAt,
                runtime.LastStatus,
                runtime.LastError
            );
            await _dbContext.SaveChangesAsync(ct);
            return;
        }

        var source = await _dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                ct
            );

        if (source is null)
        {
            return;
        }

        var detachedRuntime = new TenantIngestionRuntimeState(
            source.ManualRequestedAt,
            source.LastStartedAt,
            source.LastCompletedAt,
            source.LastSucceededAt,
            source.LastStatus,
            source.LastError
        );
        update(detachedRuntime);

        await _dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(item => item.ManualRequestedAt, detachedRuntime.ManualRequestedAt)
                        .SetProperty(item => item.LastStartedAt, detachedRuntime.LastStartedAt)
                        .SetProperty(item => item.LastCompletedAt, detachedRuntime.LastCompletedAt)
                        .SetProperty(item => item.LastSucceededAt, detachedRuntime.LastSucceededAt)
                        .SetProperty(item => item.LastStatus, detachedRuntime.LastStatus)
                        .SetProperty(item => item.LastError, detachedRuntime.LastError),
                ct
            );
    }

    public async Task UpdateIngestionRunStatusAsync(Guid runId, string status, CancellationToken ct)
    {
        if (IsInMemoryProvider())
        {
            var run = await _dbContext
                .IngestionRuns.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == runId, ct);
            run?.UpdateStatus(status);
            if (run is not null)
            {
                _dbContext.IngestionRuns.Update(run);
                await _dbContext.SaveChangesAsync(ct);
            }

            return;
        }

        await _dbContext
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item => item.Id == runId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, status), ct);
    }

    public async Task UpdateActiveRunStatusAsync(
        Guid runId,
        Guid tenantId,
        string sourceKey,
        string status,
        CancellationToken ct
    )
    {
        await UpdateIngestionRunStatusAsync(runId, status, ct);
        await UpdateRuntimeStateAsync(
            tenantId,
            sourceKey,
            runtime => runtime.LastStatus = status,
            ct
        );
    }

    internal async Task CompleteIngestionRunAsync(
        Guid runId,
        Guid tenantId,
        string sourceKey,
        bool succeeded,
        string? error,
        StagedVulnerabilityMergeSummary vulnerabilityMergeSummary,
        StagedAssetMergeSummary assetMergeSummary,
        int deactivatedMachineCount,
        string? failureStatus,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var completedAt = DateTimeOffset.UtcNow;
        if (IsInMemoryProvider())
        {
            var run = await _dbContext
                .IngestionRuns.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == runId, ct);
            if (run is null)
            {
                return;
            }

            if (succeeded)
            {
                run.CompleteSucceeded(
                    completedAt,
                    assetMergeSummary.StagedMachineCount,
                    assetMergeSummary.StagedSoftwareCount,
                    vulnerabilityMergeSummary.StagedVulnerabilityCount,
                    assetMergeSummary.PersistedMachineCount,
                    deactivatedMachineCount,
                    assetMergeSummary.PersistedSoftwareCount,
                    vulnerabilityMergeSummary.PersistedVulnerabilityCount
                );
            }
            else
            {
                run.CompleteFailed(
                    completedAt,
                    error ?? "Unknown ingestion failure",
                    failureStatus ?? IngestionRunStatuses.FailedRecoverable,
                    assetMergeSummary.StagedMachineCount,
                    assetMergeSummary.StagedSoftwareCount,
                    vulnerabilityMergeSummary.StagedVulnerabilityCount,
                    assetMergeSummary.PersistedMachineCount,
                    deactivatedMachineCount,
                    assetMergeSummary.PersistedSoftwareCount,
                    vulnerabilityMergeSummary.PersistedVulnerabilityCount
                );
            }

            var source = await _dbContext
                .TenantSourceConfigurations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    item =>
                        item.TenantId == tenantId
                        && item.SourceKey == normalizedSourceKey
                        && item.ActiveIngestionRunId == runId,
                    ct
                );
            if (source is not null)
            {
                source.ReleaseLease(runId);
                source.UpdateRuntime(
                    source.ManualRequestedAt,
                    source.LastStartedAt,
                    completedAt,
                    succeeded ? completedAt : source.LastSucceededAt,
                    succeeded
                        ? IngestionRunStatuses.Succeeded
                        : failureStatus ?? IngestionRunStatuses.FailedRecoverable,
                    succeeded ? string.Empty : error ?? "Unknown ingestion failure"
                );
            }
            await _dbContext.SaveChangesAsync(ct);

            if (succeeded)
            {
                await ClearStagedDataForRunAsync(runId, ct);
            }

            var inMemoryCleanupSummary = await CleanupExpiredIngestionArtifactsAsync(
                completedAt,
                ct
            );
            if (inMemoryCleanupSummary.PrunedRunCount > 0)
            {
                _logger.LogInformation(
                    "Pruned ingestion artifacts: runs={PrunedRunCount} stagedVulnerabilities={PrunedVulnerabilityCount} stagedExposures={PrunedExposureCount} stagedAssets={PrunedAssetCount} stagedSoftwareLinks={PrunedSoftwareLinkCount}",
                    inMemoryCleanupSummary.PrunedRunCount,
                    inMemoryCleanupSummary.PrunedVulnerabilityCount,
                    inMemoryCleanupSummary.PrunedExposureCount,
                    inMemoryCleanupSummary.PrunedAssetCount,
                    inMemoryCleanupSummary.PrunedSoftwareLinkCount
                );
            }
            return;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

            var updatedRunRows = succeeded
                ? await _dbContext
                    .IngestionRuns.IgnoreQueryFilters()
                    .Where(item => item.Id == runId)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(item => item.CompletedAt, completedAt)
                                .SetProperty(item => item.Status, IngestionRunStatuses.Succeeded)
                                .SetProperty(item => item.StagedMachineCount, assetMergeSummary.StagedMachineCount)
                                .SetProperty(item => item.StagedSoftwareCount, assetMergeSummary.StagedSoftwareCount)
                                .SetProperty(item => item.StagedVulnerabilityCount, vulnerabilityMergeSummary.StagedVulnerabilityCount)
                                .SetProperty(item => item.PersistedMachineCount, assetMergeSummary.PersistedMachineCount)
                                .SetProperty(item => item.DeactivatedMachineCount, deactivatedMachineCount)
                                .SetProperty(item => item.PersistedSoftwareCount, assetMergeSummary.PersistedSoftwareCount)
                                .SetProperty(item => item.PersistedVulnerabilityCount, vulnerabilityMergeSummary.PersistedVulnerabilityCount)
                                .SetProperty(item => item.Error, string.Empty),
                        ct
                    )
                : await _dbContext
                    .IngestionRuns.IgnoreQueryFilters()
                    .Where(item => item.Id == runId)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(item => item.CompletedAt, completedAt)
                                .SetProperty(item => item.Status, failureStatus ?? IngestionRunStatuses.FailedRecoverable)
                                .SetProperty(item => item.StagedMachineCount, assetMergeSummary.StagedMachineCount)
                                .SetProperty(item => item.StagedSoftwareCount, assetMergeSummary.StagedSoftwareCount)
                                .SetProperty(item => item.StagedVulnerabilityCount, vulnerabilityMergeSummary.StagedVulnerabilityCount)
                                .SetProperty(item => item.PersistedMachineCount, assetMergeSummary.PersistedMachineCount)
                                .SetProperty(item => item.DeactivatedMachineCount, deactivatedMachineCount)
                                .SetProperty(item => item.PersistedSoftwareCount, assetMergeSummary.PersistedSoftwareCount)
                                .SetProperty(item => item.PersistedVulnerabilityCount, vulnerabilityMergeSummary.PersistedVulnerabilityCount)
                                .SetProperty(
                                    item => item.Error,
                                    error ?? "Unknown ingestion failure"
                                ),
                        ct
                    );

            if (updatedRunRows == 0)
            {
                _logger.LogWarning(
                    "Expected to complete ingestion run {RunId} for tenant {TenantId} and source {SourceKey}, but no run row was updated.",
                    runId,
                    tenantId,
                    normalizedSourceKey
                );
            }

            await _dbContext
                .TenantSourceConfigurations.IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.SourceKey == normalizedSourceKey
                    && item.ActiveIngestionRunId == runId
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(item => item.ActiveIngestionRunId, (Guid?)null)
                            .SetProperty(item => item.LeaseAcquiredAt, (DateTimeOffset?)null)
                            .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null),
                    ct
                );

            await _dbContext
                .TenantSourceConfigurations.IgnoreQueryFilters()
                .Where(item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(item => item.LastCompletedAt, completedAt)
                            .SetProperty(
                                item => item.LastSucceededAt,
                                item => succeeded ? completedAt : item.LastSucceededAt
                            )
                            .SetProperty(
                                item => item.LastStatus,
                                succeeded
                                    ? IngestionRunStatuses.Succeeded
                                    : failureStatus ?? IngestionRunStatuses.FailedRecoverable
                            )
                            .SetProperty(
                                item => item.LastError,
                                succeeded ? string.Empty : error ?? "Unknown ingestion failure"
                            ),
                    ct
                );

            await transaction.CommitAsync(ct);
        });

        if (succeeded)
        {
            await ClearStagedDataForRunAsync(runId, ct);
        }

        var cleanupSummary = await CleanupExpiredIngestionArtifactsAsync(completedAt, ct);
        if (cleanupSummary.PrunedRunCount > 0)
        {
            _logger.LogInformation(
                "Pruned ingestion artifacts: runs={PrunedRunCount} stagedVulnerabilities={PrunedVulnerabilityCount} stagedExposures={PrunedExposureCount} stagedAssets={PrunedAssetCount} stagedSoftwareLinks={PrunedSoftwareLinkCount}",
                cleanupSummary.PrunedRunCount,
                cleanupSummary.PrunedVulnerabilityCount,
                cleanupSummary.PrunedExposureCount,
                cleanupSummary.PrunedAssetCount,
                cleanupSummary.PrunedSoftwareLinkCount
            );
        }
    }

    internal async Task<IngestionArtifactCleanupSummary> CleanupExpiredIngestionArtifactsAsync(
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var completedCutoff = now.Subtract(IngestionArtifactRetention);
        var failedCutoff = now.Subtract(FailedIngestionRetention);
        var expiredRunIds = await _dbContext
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item =>
                item.CompletedAt.HasValue
                && (
                    ((item.Status == IngestionRunStatuses.FailedRecoverable
                        || item.Status == IngestionRunStatuses.FailedTerminal)
                        && item.CompletedAt.Value < failedCutoff)
                    || ((item.Status != IngestionRunStatuses.FailedRecoverable
                            && item.Status != IngestionRunStatuses.FailedTerminal)
                        && item.CompletedAt.Value < completedCutoff)
                )
            )
            .Select(item => item.Id)
            .ToListAsync(ct);

        if (expiredRunIds.Count == 0)
        {
            return new IngestionArtifactCleanupSummary(0, 0, 0, 0, 0);
        }

        if (IsInMemoryProvider())
        {
            var stagedExposures = await _dbContext
                .StagedVulnerabilityExposures.IgnoreQueryFilters()
                .Where(item => expiredRunIds.Contains(item.IngestionRunId))
                .ToListAsync(ct);
            var stagedVulnerabilities = await _dbContext
                .StagedVulnerabilities.IgnoreQueryFilters()
                .Where(item => expiredRunIds.Contains(item.IngestionRunId))
                .ToListAsync(ct);
            var stagedSoftwareLinks = await _dbContext
                .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
                .Where(item => expiredRunIds.Contains(item.IngestionRunId))
                .ToListAsync(ct);
            var stagedDeviceRows = await _dbContext
                .StagedDevices.IgnoreQueryFilters()
                .Where(item => expiredRunIds.Contains(item.IngestionRunId))
                .ToListAsync(ct);
            var checkpoints = await _dbContext
                .IngestionCheckpoints.IgnoreQueryFilters()
                .Where(item => expiredRunIds.Contains(item.IngestionRunId))
                .ToListAsync(ct);
            var runs = await _dbContext
                .IngestionRuns.IgnoreQueryFilters()
                .Where(item => expiredRunIds.Contains(item.Id))
                .ToListAsync(ct);

            _dbContext.StagedVulnerabilityExposures.RemoveRange(stagedExposures);
            _dbContext.StagedVulnerabilities.RemoveRange(stagedVulnerabilities);
            _dbContext.StagedDeviceSoftwareInstallations.RemoveRange(stagedSoftwareLinks);
            _dbContext.StagedDevices.RemoveRange(stagedDeviceRows);
            _dbContext.IngestionCheckpoints.RemoveRange(checkpoints);
            _dbContext.IngestionRuns.RemoveRange(runs);
            await _dbContext.SaveChangesAsync(ct);

            return new IngestionArtifactCleanupSummary(
                runs.Count,
                stagedVulnerabilities.Count,
                stagedExposures.Count,
                stagedDeviceRows.Count,
                stagedSoftwareLinks.Count
            );
        }

        var prunedExposureCount = await _dbContext
            .StagedVulnerabilityExposures.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ExecuteDeleteAsync(ct);
        var prunedVulnerabilityCount = await _dbContext
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ExecuteDeleteAsync(ct);
        var prunedSoftwareLinkCount = await _dbContext
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ExecuteDeleteAsync(ct);
        var prunedAssetCount = await _dbContext
            .StagedDevices.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ExecuteDeleteAsync(ct);
        await _dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ExecuteDeleteAsync(ct);
        var prunedRunCount = await _dbContext
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.Id))
            .ExecuteDeleteAsync(ct);

        return new IngestionArtifactCleanupSummary(
            prunedRunCount,
            prunedVulnerabilityCount,
            prunedExposureCount,
            prunedAssetCount,
            prunedSoftwareLinkCount
        );
    }

    public async Task ReleaseIngestionLeaseAsync(
        Guid tenantId,
        string sourceKey,
        Guid runId,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        if (IsInMemoryProvider())
        {
            var source = await _dbContext
                .TenantSourceConfigurations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                    ct
                );
            if (source is null)
            {
                return;
            }

            source.ReleaseLease(runId);
            await _dbContext.SaveChangesAsync(ct);
            return;
        }

        await _dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.SourceKey == normalizedSourceKey
                && item.ActiveIngestionRunId == runId
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(item => item.ActiveIngestionRunId, (Guid?)null)
                        .SetProperty(item => item.LeaseAcquiredAt, (DateTimeOffset?)null)
                        .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null),
                ct
            );
    }

    private async Task ClearStagedDataForRunAsync(Guid ingestionRunId, CancellationToken ct)
    {
        if (IsInMemoryProvider())
        {
            var stagedExposures = await _dbContext
                .StagedVulnerabilityExposures.IgnoreQueryFilters()
                .Where(item => item.IngestionRunId == ingestionRunId)
                .ToListAsync(ct);
            var stagedVulnerabilities = await _dbContext
                .StagedVulnerabilities.IgnoreQueryFilters()
                .Where(item => item.IngestionRunId == ingestionRunId)
                .ToListAsync(ct);
            var stagedSoftwareLinks = await _dbContext
                .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
                .Where(item => item.IngestionRunId == ingestionRunId)
                .ToListAsync(ct);
            var stagedDevices = await _dbContext
                .StagedDevices.IgnoreQueryFilters()
                .Where(item => item.IngestionRunId == ingestionRunId)
                .ToListAsync(ct);

            _dbContext.StagedVulnerabilityExposures.RemoveRange(stagedExposures);
            _dbContext.StagedVulnerabilities.RemoveRange(stagedVulnerabilities);
            _dbContext.StagedDeviceSoftwareInstallations.RemoveRange(stagedSoftwareLinks);
            _dbContext.StagedDevices.RemoveRange(stagedDevices);
            await _dbContext.SaveChangesAsync(ct);
            return;
        }

        await _dbContext
            .StagedVulnerabilityExposures.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ExecuteDeleteAsync(ct);
        await _dbContext
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ExecuteDeleteAsync(ct);
        await _dbContext
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ExecuteDeleteAsync(ct);
        await _dbContext
            .StagedDevices.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ExecuteDeleteAsync(ct);
    }

    private bool IsInMemoryProvider()
    {
        return string.Equals(
            _dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal
        );
    }
}
