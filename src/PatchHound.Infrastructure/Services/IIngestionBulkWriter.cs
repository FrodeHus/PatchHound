namespace PatchHound.Infrastructure.Services;

/// <summary>
/// Abstracts the two EF Core execution paths that differ between the InMemory provider
/// (used in tests) and PostgreSQL (production): bulk deletes via
/// <c>ExecuteDeleteAsync</c>/<c>ExecuteUpdateAsync</c> vs. load + mutate + <c>SaveChangesAsync</c>.
/// </summary>
internal interface IIngestionBulkWriter
{
    // ── Staged data cleanup ──────────────────────────────────────────────────

    /// <summary>Deletes all staged rows (devices, software, vulnerabilities, exposures) for the given run.</summary>
    Task ClearStagedDataForRunAsync(Guid ingestionRunId, CancellationToken ct);

    /// <summary>
    /// Deletes all staged rows for the given set of expired run IDs and returns
    /// per-entity deleted counts.
    /// </summary>
    Task<IngestionArtifactCleanupSummary> CleanupExpiredArtifactsAsync(
        IReadOnlyList<Guid> expiredRunIds,
        CancellationToken ct
    );

    // ── Snapshot data cleanup ────────────────────────────────────────────────

    /// <summary>Deletes <c>SoftwareProductInstallations</c> and <c>SoftwareTenantRecords</c> for the given snapshot.</summary>
    Task CleanupSnapshotDataAsync(Guid snapshotId, CancellationToken ct);

    // ── Source lease updates ─────────────────────────────────────────────────

    /// <summary>Clears the active run / lease fields on the matching <c>TenantSourceConfiguration</c>.</summary>
    Task ReleaseIngestionLeaseAsync(
        Guid tenantId,
        string normalizedSourceKey,
        Guid runId,
        CancellationToken ct
    );

    // ── Run status updates ───────────────────────────────────────────────────

    /// <summary>Updates <c>IngestionRun.Status</c> for the given run.</summary>
    Task UpdateIngestionRunStatusAsync(Guid runId, string status, CancellationToken ct);

    /// <summary>Updates staged / persisted vulnerability count fields on the run.</summary>
    Task UpdateVulnerabilityMergeProgressAsync(
        Guid ingestionRunId,
        int stagedVulnerabilityCount,
        int persistedVulnerabilityCount,
        CancellationToken ct
    );

    /// <summary>Updates staged / persisted asset count fields on the run.</summary>
    Task UpdateAssetMergeProgressAsync(
        Guid ingestionRunId,
        int stagedMachineCount,
        int stagedSoftwareCount,
        int persistedMachineCount,
        int persistedSoftwareCount,
        CancellationToken ct
    );

    // ── Source runtime state updates ─────────────────────────────────────────

    /// <summary>Reads the current runtime state, applies <paramref name="update"/>, then persists it.</summary>
    Task UpdateRuntimeStateAsync(
        Guid tenantId,
        string normalizedSourceKey,
        Action<TenantIngestionRuntimeState> update,
        CancellationToken ct
    );

    // ── Compound operations ──────────────────────────────────────────────────

    /// <summary>
    /// If the run exists, is not yet completed, has an abort request, and is still in an
    /// active status, marks it <c>FailedTerminal</c> and releases the source lease.
    /// </summary>
    Task FinalizeAbortedRunAsync(
        Guid runId,
        Guid tenantId,
        string normalizedSourceKey,
        DateTimeOffset completedAt,
        CancellationToken ct
    );

    /// <summary>
    /// Writes the final run outcome (run row + source lease release + source runtime state)
    /// atomically where the provider supports transactions.
    /// </summary>
    Task CompleteIngestionRunAsync(
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
    );
}
