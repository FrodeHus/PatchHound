using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class IngestionService
{
    private const int MaxPersistenceAttempts = 2;
    private const int MaxSourceAttempts = 2;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IngestionArtifactRetention = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions StagingJsonOptions = new(
        JsonSerializerDefaults.Web
    );
    private readonly PatchHoundDbContext _dbContext;
    private readonly IEnumerable<IVulnerabilitySource> _sources;
    private readonly EnrichmentJobEnqueuer _enrichmentJobEnqueuer;
    private readonly VulnerabilityAssessmentService _assessmentService;
    private readonly SoftwareVulnerabilityMatchService _softwareVulnerabilityMatchService;
    private readonly RemediationTaskProjectionService _remediationTaskProjectionService;
    private readonly StagedVulnerabilityMergeService _stagedVulnerabilityMergeService;
    private readonly StagedAssetMergeService _stagedAssetMergeService;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IVulnerabilitySource> sources,
        EnrichmentJobEnqueuer enrichmentJobEnqueuer,
        VulnerabilityAssessmentService assessmentService,
        SoftwareVulnerabilityMatchService softwareVulnerabilityMatchService,
        RemediationTaskProjectionService remediationTaskProjectionService,
        StagedVulnerabilityMergeService stagedVulnerabilityMergeService,
        StagedAssetMergeService stagedAssetMergeService,
        ILogger<IngestionService> logger
    )
    {
        _dbContext = dbContext;
        _sources = sources;
        _enrichmentJobEnqueuer = enrichmentJobEnqueuer;
        _assessmentService = assessmentService;
        _softwareVulnerabilityMatchService = softwareVulnerabilityMatchService;
        _remediationTaskProjectionService = remediationTaskProjectionService;
        _stagedVulnerabilityMergeService = stagedVulnerabilityMergeService;
        _stagedAssetMergeService = stagedAssetMergeService;
        _logger = logger;
    }

    public async Task<bool> RunIngestionAsync(Guid tenantId, CancellationToken ct)
    {
        return await RunIngestionAsync(tenantId, null, ct);
    }

    public async Task<bool> RunIngestionAsync(
        Guid tenantId,
        string? sourceKey,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey?.Trim().ToLowerInvariant();
        var sources = string.IsNullOrWhiteSpace(sourceKey)
            ? _sources
            : _sources.Where(source => source.SourceKey == normalizedSourceKey);
        var startedAny = false;

        foreach (var source in sources)
        {
            var run = await TryAcquireIngestionRunAsync(tenantId, source.SourceKey, ct);
            if (run is null)
            {
                _logger.LogInformation(
                    "Skipping ingestion from {Source} for tenant {TenantId} because another run already holds the lease.",
                    source.SourceName,
                    tenantId
                );
                continue;
            }

            startedAny = true;
            var runCompleted = false;
            var fetchedVulnerabilityCount = 0;
            var fetchedAssetCount = 0;
            var fetchedSoftwareInstallationCount = 0;
            var vulnerabilityMergeSummary = new StagedVulnerabilityMergeSummary(0, 0, 0, 0, 0);
            var assetMergeSummary = new StagedAssetMergeSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

            try
            {
                for (var attempt = 1; attempt <= MaxSourceAttempts; attempt++)
                {
                    await ClearStagedDataForRunAsync(run.Id, ct);

                    await UpdateRuntimeStateAsync(
                        tenantId,
                        source.SourceKey,
                        runtime =>
                        {
                            runtime.ManualRequestedAt = null;
                            runtime.LastStartedAt = DateTimeOffset.UtcNow;
                            runtime.LastStatus = "Running";
                            runtime.LastError = string.Empty;
                        },
                        ct
                    );

                    try
                    {
                        _logger.LogInformation(
                            "Starting ingestion from {Source} for tenant {TenantId}",
                            source.SourceName,
                            tenantId
                        );

                        if (source is IAssetInventorySource assetInventorySource)
                        {
                            var assetSnapshot = await assetInventorySource.FetchAssetsAsync(
                                tenantId,
                                ct
                            );
                            var normalizedAssetSnapshot = NormalizeAssetSnapshot(assetSnapshot);
                            fetchedAssetCount = assetSnapshot.Assets.Count;
                            fetchedSoftwareInstallationCount = assetSnapshot
                                .DeviceSoftwareLinks
                                .Count;
                            await StageAssetInventorySnapshotAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                normalizedAssetSnapshot,
                                ct
                            );
                            assetMergeSummary = await ExecuteWithConcurrencyRetryAsync(
                                () =>
                                    ProcessStagedAssetsAsync(
                                        run.Id,
                                        tenantId,
                                        source.SourceKey,
                                        ct
                                    ),
                                source.SourceName,
                                tenantId,
                                ct
                            );
                            _logger.LogInformation(
                                "Asset inventory merge for {Source} tenant {TenantId}: stagedAssets={StagedAssetCount} mergedAssets={MergedAssetCount} stagedSoftwareLinks={StagedSoftwareLinkCount} resolvedSoftwareLinks={ResolvedSoftwareLinkCount} createdInstallations={InstallationsCreated} touchedInstallations={InstallationsTouched} openedEpisodes={EpisodesOpened} seenEpisodes={EpisodesSeen} staleInstallations={StaleInstallationsMarked} removedInstallations={InstallationsRemoved}",
                                source.SourceName,
                                tenantId,
                                assetMergeSummary.StagedAssetCount,
                                assetMergeSummary.MergedAssetCount,
                                assetMergeSummary.StagedSoftwareLinkCount,
                                assetMergeSummary.ResolvedSoftwareLinkCount,
                                assetMergeSummary.InstallationsCreated,
                                assetMergeSummary.InstallationsTouched,
                                assetMergeSummary.EpisodesOpened,
                                assetMergeSummary.EpisodesSeen,
                                assetMergeSummary.StaleInstallationsMarked,
                                assetMergeSummary.InstallationsRemoved
                            );
                        }

                        var results = await source.FetchVulnerabilitiesAsync(tenantId, ct);
                        fetchedVulnerabilityCount = results.Count;
                        var normalizedResults = NormalizeResults(results);
                        await StageVulnerabilitiesAsync(
                            run.Id,
                            tenantId,
                            source.SourceKey,
                            normalizedResults,
                            ct
                        );
                        vulnerabilityMergeSummary = await ExecuteWithConcurrencyRetryAsync(
                            () =>
                                ProcessStagedResultsAsync(
                                    run.Id,
                                    tenantId,
                                    source.SourceKey,
                                    source.SourceName,
                                    ct
                                ),
                            source.SourceName,
                            tenantId,
                            ct
                        );
                        await EnqueueEnrichmentJobsForRunAsync(run.Id, tenantId, ct);
                        await ExecuteWithConcurrencyRetryAsync(
                            async () =>
                            {
                                await _softwareVulnerabilityMatchService.SyncForTenantAsync(
                                    tenantId,
                                    ct
                                );
                                return true;
                            },
                            source.SourceName,
                            tenantId,
                            ct
                        );
                        _logger.LogInformation(
                            "Vulnerability merge for {Source} tenant {TenantId}: stagedVulnerabilities={StagedVulnerabilityCount} stagedExposures={StagedExposureCount} mergedExposures={MergedExposureCount} openedProjections={OpenedProjectionCount} resolvedProjections={ResolvedProjectionCount}",
                            source.SourceName,
                            tenantId,
                            vulnerabilityMergeSummary.StagedVulnerabilityCount,
                            vulnerabilityMergeSummary.StagedExposureCount,
                            vulnerabilityMergeSummary.MergedExposureCount,
                            vulnerabilityMergeSummary.OpenedProjectionCount,
                            vulnerabilityMergeSummary.ResolvedProjectionCount
                        );

                        _logger.LogInformation(
                            "Completed ingestion from {Source} for tenant {TenantId}: {Count} vulnerabilities",
                            source.SourceName,
                            tenantId,
                            results.Count
                        );

                        await UpdateRuntimeStateAsync(
                            tenantId,
                            source.SourceKey,
                            runtime =>
                            {
                                var now = DateTimeOffset.UtcNow;
                                runtime.LastCompletedAt = now;
                                runtime.LastSucceededAt = now;
                                runtime.LastStatus = "Succeeded";
                                runtime.LastError = string.Empty;
                            },
                            ct
                        );
                        await CompleteIngestionRunAsync(
                            run.Id,
                            tenantId,
                            source.SourceKey,
                            succeeded: true,
                            error: null,
                            fetchedVulnerabilityCount,
                            fetchedAssetCount,
                            fetchedSoftwareInstallationCount,
                            vulnerabilityMergeSummary,
                            assetMergeSummary,
                            ct
                        );
                        runCompleted = true;

                        break;
                    }
                    catch (DbUpdateConcurrencyException ex) when (attempt < MaxSourceAttempts)
                    {
                        _logger.LogWarning(
                            ex,
                            "Retrying ingestion from {Source} for tenant {TenantId} after concurrency conflict affecting entities [{Entities}]. Attempt {Attempt}/{MaxAttempts}.",
                            source.SourceName,
                            tenantId,
                            DescribeConcurrencyEntries(ex),
                            attempt + 1,
                            MaxSourceAttempts
                        );

                        _dbContext.ChangeTracker.Clear();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error during ingestion from {Source} for tenant {TenantId}",
                            source.SourceName,
                            tenantId
                        );

                        await UpdateRuntimeStateAsync(
                            tenantId,
                            source.SourceKey,
                            runtime =>
                            {
                                runtime.LastCompletedAt = DateTimeOffset.UtcNow;
                                runtime.LastStatus = "Failed";
                                runtime.LastError = $"Ingestion failed: {ex.GetType().Name}";
                            },
                            ct
                        );
                        await CompleteIngestionRunAsync(
                            run.Id,
                            tenantId,
                            source.SourceKey,
                            succeeded: false,
                            error: $"Ingestion failed: {ex.GetType().Name}",
                            fetchedVulnerabilityCount,
                            fetchedAssetCount,
                            fetchedSoftwareInstallationCount,
                            vulnerabilityMergeSummary,
                            assetMergeSummary,
                            ct
                        );
                        runCompleted = true;

                        break;
                    }
                }
            }
            finally
            {
                if (!runCompleted)
                {
                    await ReleaseIngestionLeaseAsync(tenantId, source.SourceKey, run.Id, ct);
                }
            }
        }

        return startedAny;
    }

    private async Task<T> ExecuteWithConcurrencyRetryAsync<T>(
        Func<Task<T>> operation,
        string sourceName,
        Guid tenantId,
        CancellationToken ct
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxPersistenceAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Retrying ingestion persistence after concurrency conflict for {Source} and tenant {TenantId} affecting entities [{Entities}]. Attempt {Attempt}/{MaxAttempts}.",
                    sourceName,
                    tenantId,
                    DescribeConcurrencyEntries(ex),
                    attempt + 1,
                    MaxPersistenceAttempts
                );

                _dbContext.ChangeTracker.Clear();
            }
        }
    }

    private async Task UpdateRuntimeStateAsync(
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

        var runtime = new TenantIngestionRuntimeState(
            source.ManualRequestedAt,
            source.LastStartedAt,
            source.LastCompletedAt,
            source.LastSucceededAt,
            source.LastStatus,
            source.LastError
        );
        update(runtime);
        await _dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(item => item.ManualRequestedAt, runtime.ManualRequestedAt)
                        .SetProperty(item => item.LastStartedAt, runtime.LastStartedAt)
                        .SetProperty(item => item.LastCompletedAt, runtime.LastCompletedAt)
                        .SetProperty(item => item.LastSucceededAt, runtime.LastSucceededAt)
                        .SetProperty(item => item.LastStatus, runtime.LastStatus)
                        .SetProperty(item => item.LastError, runtime.LastError),
                ct
            );
    }

    private async Task<IngestionRun?> TryAcquireIngestionRunAsync(
        Guid tenantId,
        string sourceKey,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var run = IngestionRun.Start(tenantId, normalizedSourceKey, now);
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

            var updatedRows = await _dbContext
                .TenantSourceConfigurations.IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.SourceKey == normalizedSourceKey
                    && (!item.ActiveIngestionRunId.HasValue || item.LeaseExpiresAt < now)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(item => item.ActiveIngestionRunId, run.Id)
                            .SetProperty(item => item.LeaseAcquiredAt, now)
                            .SetProperty(item => item.LeaseExpiresAt, now.Add(LeaseDuration)),
                    ct
                );

            if (updatedRows == 0)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            await _dbContext.IngestionRuns.AddAsync(run, ct);
            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return run;
        });
    }

    private async Task CompleteIngestionRunAsync(
        Guid runId,
        Guid tenantId,
        string sourceKey,
        bool succeeded,
        string? error,
        int fetchedVulnerabilityCount,
        int fetchedAssetCount,
        int fetchedSoftwareInstallationCount,
        StagedVulnerabilityMergeSummary vulnerabilityMergeSummary,
        StagedAssetMergeSummary assetMergeSummary,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var completedAt = DateTimeOffset.UtcNow;
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
                                .SetProperty(item => item.Status, "Succeeded")
                                .SetProperty(
                                    item => item.FetchedVulnerabilityCount,
                                    fetchedVulnerabilityCount
                                )
                                .SetProperty(item => item.FetchedAssetCount, fetchedAssetCount)
                                .SetProperty(
                                    item => item.FetchedSoftwareInstallationCount,
                                    fetchedSoftwareInstallationCount
                                )
                                .SetProperty(
                                    item => item.StagedVulnerabilityCount,
                                    vulnerabilityMergeSummary.StagedVulnerabilityCount
                                )
                                .SetProperty(
                                    item => item.StagedExposureCount,
                                    vulnerabilityMergeSummary.StagedExposureCount
                                )
                                .SetProperty(
                                    item => item.MergedExposureCount,
                                    vulnerabilityMergeSummary.MergedExposureCount
                                )
                                .SetProperty(
                                    item => item.OpenedProjectionCount,
                                    vulnerabilityMergeSummary.OpenedProjectionCount
                                )
                                .SetProperty(
                                    item => item.ResolvedProjectionCount,
                                    vulnerabilityMergeSummary.ResolvedProjectionCount
                                )
                                .SetProperty(
                                    item => item.StagedAssetCount,
                                    assetMergeSummary.StagedAssetCount
                                )
                                .SetProperty(
                                    item => item.MergedAssetCount,
                                    assetMergeSummary.MergedAssetCount
                                )
                                .SetProperty(
                                    item => item.StagedSoftwareLinkCount,
                                    assetMergeSummary.StagedSoftwareLinkCount
                                )
                                .SetProperty(
                                    item => item.ResolvedSoftwareLinkCount,
                                    assetMergeSummary.ResolvedSoftwareLinkCount
                                )
                                .SetProperty(
                                    item => item.InstallationsCreated,
                                    assetMergeSummary.InstallationsCreated
                                )
                                .SetProperty(
                                    item => item.InstallationsTouched,
                                    assetMergeSummary.InstallationsTouched
                                )
                                .SetProperty(
                                    item => item.InstallationEpisodesOpened,
                                    assetMergeSummary.EpisodesOpened
                                )
                                .SetProperty(
                                    item => item.InstallationEpisodesSeen,
                                    assetMergeSummary.EpisodesSeen
                                )
                                .SetProperty(
                                    item => item.StaleInstallationsMarked,
                                    assetMergeSummary.StaleInstallationsMarked
                                )
                                .SetProperty(
                                    item => item.InstallationsRemoved,
                                    assetMergeSummary.InstallationsRemoved
                                )
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
                                .SetProperty(item => item.Status, "Failed")
                                .SetProperty(
                                    item => item.FetchedVulnerabilityCount,
                                    fetchedVulnerabilityCount
                                )
                                .SetProperty(item => item.FetchedAssetCount, fetchedAssetCount)
                                .SetProperty(
                                    item => item.FetchedSoftwareInstallationCount,
                                    fetchedSoftwareInstallationCount
                                )
                                .SetProperty(
                                    item => item.StagedVulnerabilityCount,
                                    vulnerabilityMergeSummary.StagedVulnerabilityCount
                                )
                                .SetProperty(
                                    item => item.StagedExposureCount,
                                    vulnerabilityMergeSummary.StagedExposureCount
                                )
                                .SetProperty(
                                    item => item.MergedExposureCount,
                                    vulnerabilityMergeSummary.MergedExposureCount
                                )
                                .SetProperty(
                                    item => item.OpenedProjectionCount,
                                    vulnerabilityMergeSummary.OpenedProjectionCount
                                )
                                .SetProperty(
                                    item => item.ResolvedProjectionCount,
                                    vulnerabilityMergeSummary.ResolvedProjectionCount
                                )
                                .SetProperty(
                                    item => item.StagedAssetCount,
                                    assetMergeSummary.StagedAssetCount
                                )
                                .SetProperty(
                                    item => item.MergedAssetCount,
                                    assetMergeSummary.MergedAssetCount
                                )
                                .SetProperty(
                                    item => item.StagedSoftwareLinkCount,
                                    assetMergeSummary.StagedSoftwareLinkCount
                                )
                                .SetProperty(
                                    item => item.ResolvedSoftwareLinkCount,
                                    assetMergeSummary.ResolvedSoftwareLinkCount
                                )
                                .SetProperty(
                                    item => item.InstallationsCreated,
                                    assetMergeSummary.InstallationsCreated
                                )
                                .SetProperty(
                                    item => item.InstallationsTouched,
                                    assetMergeSummary.InstallationsTouched
                                )
                                .SetProperty(
                                    item => item.InstallationEpisodesOpened,
                                    assetMergeSummary.EpisodesOpened
                                )
                                .SetProperty(
                                    item => item.InstallationEpisodesSeen,
                                    assetMergeSummary.EpisodesSeen
                                )
                                .SetProperty(
                                    item => item.StaleInstallationsMarked,
                                    assetMergeSummary.StaleInstallationsMarked
                                )
                                .SetProperty(
                                    item => item.InstallationsRemoved,
                                    assetMergeSummary.InstallationsRemoved
                                )
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

            await transaction.CommitAsync(ct);
        });
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

    private async Task<IngestionArtifactCleanupSummary> CleanupExpiredIngestionArtifactsAsync(
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var cutoff = now.Subtract(IngestionArtifactRetention);
        var expiredRunIds = await _dbContext
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item => item.CompletedAt.HasValue && item.CompletedAt.Value < cutoff)
            .Select(item => item.Id)
            .ToListAsync(ct);

        if (expiredRunIds.Count == 0)
        {
            return new IngestionArtifactCleanupSummary(0, 0, 0, 0, 0);
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
            .StagedAssets.IgnoreQueryFilters()
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

    private async Task ReleaseIngestionLeaseAsync(
        Guid tenantId,
        string sourceKey,
        Guid runId,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
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
            .StagedAssets.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ExecuteDeleteAsync(ct);
    }

    private async Task StageVulnerabilitiesAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        IReadOnlyList<IngestionResult> results,
        CancellationToken ct
    )
    {
        if (results.Count == 0)
        {
            return;
        }

        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var stagedAt = DateTimeOffset.UtcNow;
        var rows = results.Select(result =>
            StagedVulnerability.Create(
                ingestionRunId,
                tenantId,
                normalizedSourceKey,
                result.ExternalId,
                result.Title,
                result.VendorSeverity,
                JsonSerializer.Serialize(result with { AffectedAssets = [] }, StagingJsonOptions),
                stagedAt
            )
        );
        var exposures = results.SelectMany(result =>
            result.AffectedAssets.Select(affectedAsset =>
                StagedVulnerabilityExposure.Create(
                    ingestionRunId,
                    tenantId,
                    normalizedSourceKey,
                    result.ExternalId,
                    affectedAsset.ExternalAssetId,
                    affectedAsset.AssetName,
                    affectedAsset.AssetType,
                    JsonSerializer.Serialize(affectedAsset, StagingJsonOptions),
                    stagedAt
                )
            )
        );

        await _dbContext.StagedVulnerabilities.AddRangeAsync(rows, ct);
        await _dbContext.StagedVulnerabilityExposures.AddRangeAsync(exposures, ct);
        await _dbContext.SaveChangesAsync(ct);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task StageAssetInventorySnapshotAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        IngestionAssetInventorySnapshot snapshot,
        CancellationToken ct
    )
    {
        if (snapshot.Assets.Count == 0 && snapshot.DeviceSoftwareLinks.Count == 0)
        {
            return;
        }

        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var stagedAt = DateTimeOffset.UtcNow;

        if (snapshot.Assets.Count > 0)
        {
            var stagedAssets = snapshot.Assets.Select(asset =>
                StagedAsset.Create(
                    ingestionRunId,
                    tenantId,
                    normalizedSourceKey,
                    asset.ExternalId,
                    asset.Name,
                    asset.AssetType,
                    JsonSerializer.Serialize(asset, StagingJsonOptions),
                    stagedAt
                )
            );
            await _dbContext.StagedAssets.AddRangeAsync(stagedAssets, ct);
        }

        if (snapshot.DeviceSoftwareLinks.Count > 0)
        {
            var stagedLinks = snapshot.DeviceSoftwareLinks.Select(link =>
                StagedDeviceSoftwareInstallation.Create(
                    ingestionRunId,
                    tenantId,
                    normalizedSourceKey,
                    link.DeviceExternalId,
                    link.SoftwareExternalId,
                    link.ObservedAt,
                    JsonSerializer.Serialize(link, StagingJsonOptions),
                    stagedAt
                )
            );
            await _dbContext.StagedDeviceSoftwareInstallations.AddRangeAsync(stagedLinks, ct);
        }

        await _dbContext.SaveChangesAsync(ct);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task EnqueueEnrichmentJobsForRunAsync(
        Guid ingestionRunId,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var externalIds = await _dbContext
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .Select(item => item.ExternalId)
            .Distinct()
            .ToListAsync(ct);

        if (externalIds.Count == 0)
        {
            return;
        }

        var vulnerabilityIds = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && externalIds.Contains(item.ExternalId))
            .Select(item => item.Id)
            .ToListAsync(ct);

        await _enrichmentJobEnqueuer.EnqueueVulnerabilityJobsAsync(tenantId, vulnerabilityIds, ct);
    }

    internal async Task ProcessResultsAsync(
        Guid tenantId,
        string sourceName,
        IReadOnlyList<IngestionResult> results,
        CancellationToken ct
    )
    {
        var run = IngestionRun.Start(
            tenantId,
            sourceName.Trim().ToLowerInvariant(),
            DateTimeOffset.UtcNow
        );
        await _dbContext.IngestionRuns.AddAsync(run, ct);
        await _dbContext.SaveChangesAsync(ct);

        var normalizedResults = NormalizeResults(results);
        await StageVulnerabilitiesAsync(run.Id, tenantId, run.SourceKey, normalizedResults, ct);
        await ProcessStagedResultsAsync(run.Id, tenantId, run.SourceKey, sourceName, ct);
    }

    internal async Task<StagedVulnerabilityMergeSummary> ProcessStagedResultsAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        string sourceName,
        CancellationToken ct
    )
    {
        return await _stagedVulnerabilityMergeService.ProcessAsync(
            ingestionRunId,
            tenantId,
            sourceKey,
            sourceName,
            ct
        );
    }

    internal async Task ProcessAssetsAsync(
        Guid tenantId,
        IngestionAssetInventorySnapshot snapshot,
        CancellationToken ct
    )
    {
        var run = IngestionRun.Start(tenantId, "manual-assets", DateTimeOffset.UtcNow);
        await _dbContext.IngestionRuns.AddAsync(run, ct);
        await _dbContext.SaveChangesAsync(ct);

        var normalizedSnapshot = NormalizeAssetSnapshot(snapshot);
        await StageAssetInventorySnapshotAsync(
            run.Id,
            tenantId,
            run.SourceKey,
            normalizedSnapshot,
            ct
        );
        await ProcessStagedAssetsAsync(run.Id, tenantId, run.SourceKey, ct);
    }

    internal async Task<StagedAssetMergeSummary> ProcessStagedAssetsAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        CancellationToken ct
    )
    {
        return await _stagedAssetMergeService.ProcessAsync(ingestionRunId, tenantId, sourceKey, ct);
    }

    private static IReadOnlyList<IngestionResult> NormalizeResults(
        IReadOnlyList<IngestionResult> results
    )
    {
        return results
            .GroupBy(result => result.ExternalId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var affectedAssets = group
                    .SelectMany(item => item.AffectedAssets)
                    .GroupBy(asset => asset.ExternalAssetId, StringComparer.OrdinalIgnoreCase)
                    .Select(assetGroup => assetGroup.First())
                    .ToList();
                var references = group
                    .SelectMany(item => item.References ?? [])
                    .GroupBy(reference => reference.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(referenceGroup => referenceGroup.First())
                    .ToList();
                var affectedSoftware = group
                    .SelectMany(item => item.AffectedSoftware ?? [])
                    .GroupBy(
                        item =>
                            $"{item.Criteria}|{item.VersionStartIncluding}|{item.VersionStartExcluding}|{item.VersionEndIncluding}|{item.VersionEndExcluding}|{item.Vulnerable}",
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Select(softwareGroup => softwareGroup.First())
                    .ToList();
                var sources = group
                    .SelectMany(item => item.Sources ?? [])
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return first with
                {
                    AffectedAssets = affectedAssets,
                    References = references,
                    AffectedSoftware = affectedSoftware,
                    Sources = sources,
                };
            })
            .ToList();
    }

    private static IngestionAssetInventorySnapshot NormalizeAssetSnapshot(
        IngestionAssetInventorySnapshot snapshot
    )
    {
        var assets = snapshot
            .Assets.GroupBy(asset => asset.ExternalId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        var deviceSoftwareLinks = snapshot
            .DeviceSoftwareLinks.GroupBy(
                link => $"{link.DeviceExternalId}:{link.SoftwareExternalId}",
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group => group.OrderByDescending(link => link.ObservedAt).First())
            .ToList();

        return new IngestionAssetInventorySnapshot(assets, deviceSoftwareLinks);
    }

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        for (var index = 0; index < items.Count; index += size)
        {
            var count = Math.Min(size, items.Count - index);
            var chunk = new List<T>(count);
            for (var offset = 0; offset < count; offset++)
            {
                chunk.Add(items[index + offset]);
            }

            yield return chunk;
        }
    }

    private static string DescribeConcurrencyEntries(DbUpdateConcurrencyException ex)
    {
        return string.Join(
            ", ",
            ex.Entries.Select(entry => entry.Metadata.Name).Distinct(StringComparer.Ordinal)
        );
    }
}

internal sealed class TenantIngestionRuntimeState
{
    public TenantIngestionRuntimeState(
        DateTimeOffset? manualRequestedAt,
        DateTimeOffset? lastStartedAt,
        DateTimeOffset? lastCompletedAt,
        DateTimeOffset? lastSucceededAt,
        string lastStatus,
        string lastError
    )
    {
        ManualRequestedAt = manualRequestedAt;
        LastStartedAt = lastStartedAt;
        LastCompletedAt = lastCompletedAt;
        LastSucceededAt = lastSucceededAt;
        LastStatus = lastStatus;
        LastError = lastError;
    }

    public DateTimeOffset? ManualRequestedAt { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public string LastStatus { get; set; }
    public string LastError { get; set; }
}

internal sealed class GlobalEnrichmentRuntimeState
{
    public GlobalEnrichmentRuntimeState(
        DateTimeOffset? lastStartedAt,
        DateTimeOffset? lastCompletedAt,
        DateTimeOffset? lastSucceededAt,
        string lastStatus,
        string lastError
    )
    {
        LastStartedAt = lastStartedAt;
        LastCompletedAt = lastCompletedAt;
        LastSucceededAt = lastSucceededAt;
        LastStatus = lastStatus;
        LastError = lastError;
    }

    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public string LastStatus { get; set; }
    public string LastError { get; set; }
}

internal sealed record IngestionArtifactCleanupSummary(
    int PrunedRunCount,
    int PrunedVulnerabilityCount,
    int PrunedExposureCount,
    int PrunedAssetCount,
    int PrunedSoftwareLinkCount
);
