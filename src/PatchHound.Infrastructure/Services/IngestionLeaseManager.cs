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
    private readonly IIngestionBulkWriter _bulkWriter;
    private readonly ILogger<IngestionLeaseManager> _logger;

    internal IngestionLeaseManager(
        PatchHoundDbContext dbContext,
        IIngestionBulkWriter bulkWriter,
        ILogger<IngestionLeaseManager> logger
    )
    {
        _dbContext = dbContext;
        _bulkWriter = bulkWriter;
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
            if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
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
        await _bulkWriter.FinalizeAbortedRunAsync(runId, tenantId, normalizedSourceKey, completedAt, ct);
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

        await _bulkWriter.UpdateRuntimeStateAsync(tenantId, normalizedSourceKey, update, ct);
    }

    public async Task UpdateIngestionRunStatusAsync(Guid runId, string status, CancellationToken ct)
    {
        await _bulkWriter.UpdateIngestionRunStatusAsync(runId, status, ct);
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

        await _bulkWriter.CompleteIngestionRunAsync(
            runId,
            tenantId,
            normalizedSourceKey,
            succeeded,
            error,
            vulnerabilityMergeSummary,
            assetMergeSummary,
            deactivatedMachineCount,
            failureStatus,
            completedAt,
            ct
        );

        await _bulkWriter.ClearStagedDataForRunAsync(runId, ct);

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

        return await _bulkWriter.CleanupExpiredArtifactsAsync(expiredRunIds, ct);
    }

    public async Task ReleaseIngestionLeaseAsync(
        Guid tenantId,
        string sourceKey,
        Guid runId,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        await _bulkWriter.ReleaseIngestionLeaseAsync(tenantId, normalizedSourceKey, runId, ct);
    }
}
