using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

/// <summary>
/// Production implementation of <see cref="IIngestionBulkWriter"/> that uses
/// <c>ExecuteDeleteAsync</c> / <c>ExecuteUpdateAsync</c> for bulk operations.
/// </summary>
internal sealed class PostgresIngestionBulkWriter(PatchHoundDbContext db) : IIngestionBulkWriter
{
    public async Task ClearStagedDataForRunAsync(Guid ingestionRunId, CancellationToken ct)
    {
        await db
            .StagedVulnerabilityExposures.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ExecuteDeleteAsync(ct);
        await db
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ExecuteDeleteAsync(ct);
        await db
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ExecuteDeleteAsync(ct);
        await db
            .StagedDevices.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<IngestionArtifactCleanupSummary> CleanupExpiredArtifactsAsync(
        IReadOnlyList<Guid> expiredRunIds,
        CancellationToken ct
    )
    {
        var prunedExposureCount = await db
            .StagedVulnerabilityExposures.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ExecuteDeleteAsync(ct);
        var prunedVulnerabilityCount = await db
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ExecuteDeleteAsync(ct);
        var prunedSoftwareLinkCount = await db
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ExecuteDeleteAsync(ct);
        var prunedAssetCount = await db
            .StagedDevices.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ExecuteDeleteAsync(ct);
        await db
            .IngestionCheckpoints.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ExecuteDeleteAsync(ct);
        var prunedRunCount = await db
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

    public async Task CleanupSnapshotDataAsync(Guid snapshotId, CancellationToken ct)
    {
        await db
            .SoftwareProductInstallations.IgnoreQueryFilters()
            .Where(item => item.SnapshotId == snapshotId)
            .ExecuteDeleteAsync(ct);
        await db
            .SoftwareTenantRecords.IgnoreQueryFilters()
            .Where(item => item.SnapshotId == snapshotId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task ReleaseIngestionLeaseAsync(
        Guid tenantId,
        string normalizedSourceKey,
        Guid runId,
        CancellationToken ct
    )
    {
        await db
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

    public async Task UpdateIngestionRunStatusAsync(Guid runId, string status, CancellationToken ct)
    {
        await db
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item => item.Id == runId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, status), ct);
    }

    public async Task UpdateVulnerabilityMergeProgressAsync(
        Guid ingestionRunId,
        int stagedVulnerabilityCount,
        int persistedVulnerabilityCount,
        CancellationToken ct
    )
    {
        await db
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item => item.Id == ingestionRunId && !item.CompletedAt.HasValue)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(item => item.StagedVulnerabilityCount, stagedVulnerabilityCount)
                        .SetProperty(item => item.PersistedVulnerabilityCount, persistedVulnerabilityCount),
                ct
            );
    }

    public async Task UpdateAssetMergeProgressAsync(
        Guid ingestionRunId,
        int stagedMachineCount,
        int stagedSoftwareCount,
        int persistedMachineCount,
        int persistedSoftwareCount,
        CancellationToken ct
    )
    {
        await db
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item => item.Id == ingestionRunId && !item.CompletedAt.HasValue)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(item => item.StagedMachineCount, stagedMachineCount)
                        .SetProperty(item => item.StagedSoftwareCount, stagedSoftwareCount)
                        .SetProperty(item => item.PersistedMachineCount, persistedMachineCount)
                        .SetProperty(item => item.DeactivatedMachineCount, 0)
                        .SetProperty(item => item.PersistedSoftwareCount, persistedSoftwareCount),
                ct
            );
    }

    public async Task UpdateRuntimeStateAsync(
        Guid tenantId,
        string normalizedSourceKey,
        Action<TenantIngestionRuntimeState> update,
        CancellationToken ct
    )
    {
        var source = await db
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

        await db
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

    public async Task FinalizeAbortedRunAsync(
        Guid runId,
        Guid tenantId,
        string normalizedSourceKey,
        DateTimeOffset completedAt,
        CancellationToken ct
    )
    {
        await db
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

        await db
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

    public async Task CompleteIngestionRunAsync(
        Guid runId,
        Guid tenantId,
        string normalizedSourceKey,
        bool succeeded,
        string? error,
        StagedVulnerabilityMergeSummary vulnerabilityMergeSummary,
        StagedAssetMergeSummary assetMergeSummary,
        int deactivatedMachineCount,
        string? failureStatus,
        DateTimeOffset completedAt,
        CancellationToken ct
    )
    {
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            _ = succeeded
                ? await db
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
                : await db
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
                                .SetProperty(item => item.Error, error ?? "Unknown ingestion failure"),
                        ct
                    );

            await db
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

            await db
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
    }
}
