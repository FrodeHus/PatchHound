using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

/// <summary>
/// Test/InMemory implementation of <see cref="IIngestionBulkWriter"/> that uses
/// load + mutate + <c>SaveChangesAsync</c> because the InMemory provider does not
/// support <c>ExecuteDeleteAsync</c> / <c>ExecuteUpdateAsync</c>.
/// </summary>
internal sealed class InMemoryIngestionBulkWriter(PatchHoundDbContext db) : IIngestionBulkWriter
{
    public async Task ClearStagedDataForRunAsync(Guid ingestionRunId, CancellationToken ct)
    {
        var stagedExposures = await db
            .StagedVulnerabilityExposures.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ToListAsync(ct);
        var stagedVulnerabilities = await db
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ToListAsync(ct);
        var stagedSoftwareLinks = await db
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ToListAsync(ct);
        var stagedDevices = await db
            .StagedDevices.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ToListAsync(ct);

        db.StagedVulnerabilityExposures.RemoveRange(stagedExposures);
        db.StagedVulnerabilities.RemoveRange(stagedVulnerabilities);
        db.StagedDeviceSoftwareInstallations.RemoveRange(stagedSoftwareLinks);
        db.StagedDevices.RemoveRange(stagedDevices);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IngestionArtifactCleanupSummary> CleanupExpiredArtifactsAsync(
        IReadOnlyList<Guid> expiredRunIds,
        CancellationToken ct
    )
    {
        var stagedExposures = await db
            .StagedVulnerabilityExposures.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ToListAsync(ct);
        var stagedVulnerabilities = await db
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ToListAsync(ct);
        var stagedSoftwareLinks = await db
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ToListAsync(ct);
        var stagedDeviceRows = await db
            .StagedDevices.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ToListAsync(ct);
        var checkpoints = await db
            .IngestionCheckpoints.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.IngestionRunId))
            .ToListAsync(ct);
        var runs = await db
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item => expiredRunIds.Contains(item.Id))
            .ToListAsync(ct);

        db.StagedVulnerabilityExposures.RemoveRange(stagedExposures);
        db.StagedVulnerabilities.RemoveRange(stagedVulnerabilities);
        db.StagedDeviceSoftwareInstallations.RemoveRange(stagedSoftwareLinks);
        db.StagedDevices.RemoveRange(stagedDeviceRows);
        db.IngestionCheckpoints.RemoveRange(checkpoints);
        db.IngestionRuns.RemoveRange(runs);
        await db.SaveChangesAsync(ct);

        return new IngestionArtifactCleanupSummary(
            runs.Count,
            stagedVulnerabilities.Count,
            stagedExposures.Count,
            stagedDeviceRows.Count,
            stagedSoftwareLinks.Count
        );
    }

    public async Task CleanupSnapshotDataAsync(Guid snapshotId, CancellationToken ct)
    {
        var tenantSoftware = await db
            .SoftwareTenantRecords.IgnoreQueryFilters()
            .Where(item => item.SnapshotId == snapshotId)
            .ToListAsync(ct);
        var installations = await db
            .SoftwareProductInstallations.IgnoreQueryFilters()
            .Where(item => item.SnapshotId == snapshotId)
            .ToListAsync(ct);

        db.SoftwareTenantRecords.RemoveRange(tenantSoftware);
        db.SoftwareProductInstallations.RemoveRange(installations);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReleaseIngestionLeaseAsync(
        Guid tenantId,
        string normalizedSourceKey,
        Guid runId,
        CancellationToken ct
    )
    {
        var source = await db
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
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateIngestionRunStatusAsync(Guid runId, string status, CancellationToken ct)
    {
        var run = await db
            .IngestionRuns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == runId, ct);
        run?.UpdateStatus(status);
        if (run is not null)
        {
            db.IngestionRuns.Update(run);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateVulnerabilityMergeProgressAsync(
        Guid ingestionRunId,
        int stagedVulnerabilityCount,
        int persistedVulnerabilityCount,
        CancellationToken ct
    )
    {
        var run = await db
            .IngestionRuns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == ingestionRunId, ct);
        if (run is null)
        {
            return;
        }

        run.UpdateVulnerabilityMergeProgress(stagedVulnerabilityCount, persistedVulnerabilityCount);
        await db.SaveChangesAsync(ct);
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
        var run = await db
            .IngestionRuns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == ingestionRunId, ct);
        if (run is null)
        {
            return;
        }

        run.UpdateAssetMergeProgress(
            stagedMachineCount,
            stagedSoftwareCount,
            persistedMachineCount,
            0,
            persistedSoftwareCount
        );
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateRuntimeStateAsync(
        Guid tenantId,
        string normalizedSourceKey,
        Action<TenantIngestionRuntimeState> update,
        CancellationToken ct
    )
    {
        var trackedSource = await db
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
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> RefreshDeviceActivityAsync(Guid tenantId, TimeSpan inactiveThreshold, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(inactiveThreshold);
        var devices = await db.Devices
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId)
            .ToListAsync(ct);

        var deactivatedCount = 0;
        foreach (var device in devices)
        {
            var isActive = device.LastSeenAt.HasValue && device.LastSeenAt.Value >= cutoff;
            device.SetActiveInTenant(isActive);
            if (!isActive)
            {
                deactivatedCount++;
            }
        }

        await db.SaveChangesAsync(ct);
        return deactivatedCount;
    }

    public async Task FinalizeAbortedRunAsync(
        Guid runId,
        Guid tenantId,
        string normalizedSourceKey,
        DateTimeOffset completedAt,
        CancellationToken ct
    )
    {
        var run = await db
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

        db.Entry(run).Property(nameof(IngestionRun.CompletedAt)).CurrentValue = completedAt;
        db.Entry(run).Property(nameof(IngestionRun.Status)).CurrentValue = IngestionRunStatuses.FailedTerminal;
        db.Entry(run).Property(nameof(IngestionRun.Error)).CurrentValue =
            IngestionFailurePolicy.Describe(new IngestionAbortedException());

        var source = await db
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item =>
                    item.TenantId == tenantId
                    && item.SourceKey == normalizedSourceKey
                    && item.ActiveIngestionRunId == runId,
                ct
            );
        source?.ReleaseLease(runId);
        await db.SaveChangesAsync(ct);
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
        var run = await db
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

        var source = await db
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
        await db.SaveChangesAsync(ct);
    }
}
