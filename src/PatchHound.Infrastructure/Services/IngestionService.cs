using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class IngestionService
{
    private const int MaxPersistenceAttempts = 2;
    private const int MaxSourceAttempts = 2;
    private const int AssetBatchSize = 200;
    private const int VulnerabilityBatchSize = 250;
    private static readonly TimeSpan DeviceInactiveThreshold = TimeSpan.FromDays(30);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IngestionArtifactRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan FailedIngestionRetention = TimeSpan.FromHours(24);
    private readonly PatchHoundDbContext _dbContext;
    private readonly IEnumerable<IVulnerabilitySource> _sources;
    private readonly EnrichmentJobEnqueuer _enrichmentJobEnqueuer;
    private readonly IStagedDeviceMergeService _stagedDeviceMergeService;
    private readonly IDeviceRuleEvaluationService _deviceRuleEvaluationService;
    private readonly ExposureDerivationService _exposureDerivationService;
    private readonly ExposureEpisodeService _exposureEpisodeService;
    private readonly ExposureAssessmentService _exposureAssessmentService;
    private readonly RiskScoreService _riskScoreService;
    private readonly VulnerabilityResolver _vulnerabilityResolver;
    private readonly RemediationDecisionService? _remediationDecisionService;
    private readonly ILogger<IngestionService> _logger;

    private sealed record AcquiredIngestionRun(IngestionRun Run, bool Resumed);

    public IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IVulnerabilitySource> sources,
        EnrichmentJobEnqueuer enrichmentJobEnqueuer,
        IStagedDeviceMergeService stagedDeviceMergeService,
        IDeviceRuleEvaluationService deviceRuleEvaluationService,
        ExposureDerivationService exposureDerivationService,
        ExposureEpisodeService exposureEpisodeService,
        ExposureAssessmentService exposureAssessmentService,
        RiskScoreService riskScoreService,
        ILogger<IngestionService> logger
    )
        : this(
            dbContext,
            sources,
            enrichmentJobEnqueuer,
            stagedDeviceMergeService,
            deviceRuleEvaluationService,
            exposureDerivationService,
            exposureEpisodeService,
            exposureAssessmentService,
            riskScoreService,
            new VulnerabilityResolver(dbContext),
            remediationDecisionService: null,
            logger
        ) { }

    public IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IVulnerabilitySource> sources,
        EnrichmentJobEnqueuer enrichmentJobEnqueuer,
        IStagedDeviceMergeService stagedDeviceMergeService,
        IDeviceRuleEvaluationService deviceRuleEvaluationService,
        ExposureDerivationService exposureDerivationService,
        ExposureEpisodeService exposureEpisodeService,
        ExposureAssessmentService exposureAssessmentService,
        RiskScoreService riskScoreService,
        VulnerabilityResolver vulnerabilityResolver,
        ILogger<IngestionService> logger
    )
        : this(
            dbContext,
            sources,
            enrichmentJobEnqueuer,
            stagedDeviceMergeService,
            deviceRuleEvaluationService,
            exposureDerivationService,
            exposureEpisodeService,
            exposureAssessmentService,
            riskScoreService,
            vulnerabilityResolver,
            remediationDecisionService: null,
            logger
        ) { }

    public IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IVulnerabilitySource> sources,
        EnrichmentJobEnqueuer enrichmentJobEnqueuer,
        IStagedDeviceMergeService stagedDeviceMergeService,
        IDeviceRuleEvaluationService deviceRuleEvaluationService,
        ExposureDerivationService exposureDerivationService,
        ExposureEpisodeService exposureEpisodeService,
        ExposureAssessmentService exposureAssessmentService,
        RiskScoreService riskScoreService,
        RemediationDecisionService? remediationDecisionService,
        ILogger<IngestionService> logger
    )
        : this(
            dbContext,
            sources,
            enrichmentJobEnqueuer,
            stagedDeviceMergeService,
            deviceRuleEvaluationService,
            exposureDerivationService,
            exposureEpisodeService,
            exposureAssessmentService,
            riskScoreService,
            new VulnerabilityResolver(dbContext),
            remediationDecisionService,
            logger
        ) { }

    public IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IVulnerabilitySource> sources,
        EnrichmentJobEnqueuer enrichmentJobEnqueuer,
        IStagedDeviceMergeService stagedDeviceMergeService,
        IDeviceRuleEvaluationService deviceRuleEvaluationService,
        ExposureDerivationService exposureDerivationService,
        ExposureEpisodeService exposureEpisodeService,
        ExposureAssessmentService exposureAssessmentService,
        RiskScoreService riskScoreService,
        VulnerabilityResolver vulnerabilityResolver,
        RemediationDecisionService? remediationDecisionService,
        ILogger<IngestionService> logger
    )
    {
        _dbContext = dbContext;
        _sources = sources;
        _enrichmentJobEnqueuer = enrichmentJobEnqueuer;
        _stagedDeviceMergeService = stagedDeviceMergeService;
        _deviceRuleEvaluationService = deviceRuleEvaluationService;
        _exposureDerivationService = exposureDerivationService;
        _exposureEpisodeService = exposureEpisodeService;
        _exposureAssessmentService = exposureAssessmentService;
        _riskScoreService = riskScoreService;
        _vulnerabilityResolver = vulnerabilityResolver;
        _remediationDecisionService = remediationDecisionService;
        _logger = logger;
    }

    public async Task RunExposureDerivationAsync(Guid tenantId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await _exposureDerivationService.DeriveForTenantAsync(tenantId, now, ct);
        await _dbContext.SaveChangesAsync(ct);
        await _exposureEpisodeService.SyncEpisodesForTenantAsync(tenantId, now, ct);
        await _dbContext.SaveChangesAsync(ct);
        await _exposureAssessmentService.AssessForTenantAsync(tenantId, now, ct);
        await _dbContext.SaveChangesAsync(ct);
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
            var acquiredRun = await TryAcquireIngestionRunAsync(tenantId, source.SourceKey, ct);
            if (acquiredRun is null)
            {
                _logger.LogInformation(
                    "Skipping ingestion from {Source} for tenant {TenantId} because another run already holds the lease.",
                    source.SourceName,
                    tenantId
                );
                continue;
            }

            var run = acquiredRun.Run;

            startedAny = true;
            var runCompleted = false;
            IngestionSnapshot? softwareSnapshot = null;
            var fetchedVulnerabilityCount = 0;
            var fetchedAssetCount = 0;
            var fetchedSoftwareCount = 0;
            var fetchedSoftwareInstallationCount = 0;
            var softwareWithoutMachineReferencesCount = 0;
            var vulnerabilityMergeSummary = new StagedVulnerabilityMergeSummary(0, 0, 0, 0, 0, 0);
            var assetMergeSummary = new StagedAssetMergeSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var deactivatedMachineCount = 0;

            try
            {
                for (var attempt = 1; attempt <= MaxSourceAttempts; attempt++)
                {
                    await UpdateRuntimeStateAsync(
                        tenantId,
                        source.SourceKey,
                        runtime =>
                        {
                            runtime.ManualRequestedAt = null;
                            runtime.LastStartedAt = DateTimeOffset.UtcNow;
                            runtime.LastStatus = IngestionRunStatuses.Staging;
                            runtime.LastError = string.Empty;
                        },
                        ct
                    );

                    try
                    {
                        await ThrowIfAbortRequestedAsync(run.Id, ct);

                        _logger.LogInformation(
                            "Starting ingestion from {Source} for tenant {TenantId}",
                            source.SourceName,
                            tenantId
                        );

                        var assetStagingCompleted = await IsCheckpointCompletedAsync(
                            run.Id,
                            "asset-staging",
                            ct
                        );
                        var assetMergeCompleted = await IsCheckpointCompletedAsync(
                            run.Id,
                            "asset-merge",
                            ct
                        );
                        var vulnerabilityStagingCompleted = await IsCheckpointCompletedAsync(
                            run.Id,
                            "vulnerability-staging",
                            ct
                        );
                        var vulnerabilityMergeCompleted = await IsCheckpointCompletedAsync(
                            run.Id,
                            "vulnerability-merge",
                            ct
                        );

                        deactivatedMachineCount = await RefreshDeviceActivityForTenantAsync(tenantId, ct);

                        if (SupportsSoftwareSnapshots(source.SourceKey))
                        {
                            softwareSnapshot ??= await GetOrCreateBuildingSoftwareSnapshotAsync(
                                tenantId,
                                source.SourceKey,
                                run.Id,
                                ct
                            );
                        }

                        if (!assetStagingCompleted && source is IAssetInventoryBatchSource assetInventoryBatchSource)
                        {
                            await ThrowIfAbortRequestedAsync(run.Id, ct);
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Staging,
                                ct
                            );
                            var assetBatchSummary = await StageAssetBatchesAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                assetInventoryBatchSource,
                                ct
                            );
                            fetchedAssetCount = assetBatchSummary.AssetCount;
                            fetchedSoftwareCount = assetBatchSummary.SoftwareCount;
                            fetchedSoftwareInstallationCount = assetBatchSummary.LinkCount;
                            softwareWithoutMachineReferencesCount =
                                assetBatchSummary.SoftwareWithoutMachineReferencesCount;
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.MergePending,
                                ct
                            );
                            await ThrowIfAbortRequestedAsync(run.Id, ct);
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Merging,
                                ct
                            );
                            var assetMergeStartedAt = DateTimeOffset.UtcNow;
                            assetMergeSummary = await ExecuteWithConcurrencyRetryAsync(
                                () =>
                                    ProcessStagedAssetsAsync(
                                        run.Id,
                                        tenantId,
                                        source.SourceKey,
                                        softwareSnapshot?.Id,
                                        ct
                                    ),
                                source.SourceName,
                                tenantId,
                                ct
                            );
                            await CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                "asset-merge",
                                assetBatchSummary.BatchNumber,
                                null,
                                assetMergeSummary.MergedAssetCount,
                                "Completed",
                                ct
                            );
                            assetStagingCompleted = true;
                            assetMergeCompleted = true;
                            _logger.LogInformation(
                                "Asset inventory merge for {Source} tenant {TenantId}: stagedMachines={StagedMachineCount} stagedSoftware={StagedSoftwareCount} persistedMachines={PersistedMachineCount} persistedSoftware={PersistedSoftwareCount}",
                                source.SourceName,
                                tenantId,
                                assetMergeSummary.StagedMachineCount,
                                assetMergeSummary.StagedSoftwareCount,
                                assetMergeSummary.PersistedMachineCount,
                                assetMergeSummary.PersistedSoftwareCount
                            );
                            _logger.LogInformation(
                                "Asset merge phase completed for ingestion run {IngestionRunId}. Duration: {DurationMs} ms.",
                                run.Id,
                                (DateTimeOffset.UtcNow - assetMergeStartedAt).TotalMilliseconds
                            );
                        }
                        else if (!assetStagingCompleted && source is IAssetInventorySource assetInventorySource)
                        {
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Staging,
                                ct
                            );
                            var assetSnapshot = await assetInventorySource.FetchAssetsAsync(
                                tenantId,
                                ct
                            );
                            var normalizedAssetSnapshot = NormalizeAssetSnapshot(assetSnapshot);
                            fetchedAssetCount = assetSnapshot.Assets.Count;
                            fetchedSoftwareCount = assetSnapshot.RetrievedSoftwareCount;
                            fetchedSoftwareInstallationCount = assetSnapshot
                                .DeviceSoftwareLinks
                                .Count;
                            softwareWithoutMachineReferencesCount =
                                assetSnapshot.SoftwareWithoutMachineReferencesCount;
                            await StageAssetInventorySnapshotAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                normalizedAssetSnapshot,
                                0,
                                ct
                            );
                            await CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                "asset-staging",
                                0,
                                null,
                                normalizedAssetSnapshot.Assets.Count + normalizedAssetSnapshot.DeviceSoftwareLinks.Count,
                                "Staged",
                                ct
                            );
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.MergePending,
                                ct
                            );
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Merging,
                                ct
                            );
                            var assetMergeStartedAt = DateTimeOffset.UtcNow;
                            assetMergeSummary = await ExecuteWithConcurrencyRetryAsync(
                                () =>
                                    ProcessStagedAssetsAsync(
                                        run.Id,
                                        tenantId,
                                        source.SourceKey,
                                        softwareSnapshot?.Id,
                                        ct
                                    ),
                                source.SourceName,
                                tenantId,
                                ct
                            );
                            await CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                "asset-merge",
                                0,
                                null,
                                assetMergeSummary.MergedAssetCount,
                                "Completed",
                                ct
                            );
                            assetStagingCompleted = true;
                            assetMergeCompleted = true;
                            _logger.LogInformation(
                                "Asset inventory merge for {Source} tenant {TenantId}: stagedMachines={StagedMachineCount} stagedSoftware={StagedSoftwareCount} persistedMachines={PersistedMachineCount} persistedSoftware={PersistedSoftwareCount}",
                                source.SourceName,
                                tenantId,
                                assetMergeSummary.StagedMachineCount,
                                assetMergeSummary.StagedSoftwareCount,
                                assetMergeSummary.PersistedMachineCount,
                                assetMergeSummary.PersistedSoftwareCount
                            );
                            _logger.LogInformation(
                                "Asset merge phase completed for ingestion run {IngestionRunId}. Duration: {DurationMs} ms.",
                                run.Id,
                                (DateTimeOffset.UtcNow - assetMergeStartedAt).TotalMilliseconds
                            );
                        }
                        else if (assetStagingCompleted)
                        {
                            fetchedAssetCount = await _dbContext
                                .StagedDevices.IgnoreQueryFilters()
                                .CountAsync(item => item.IngestionRunId == run.Id, ct);
                            fetchedSoftwareInstallationCount = await _dbContext
                                .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
                                .CountAsync(item => item.IngestionRunId == run.Id, ct);
                        }

                        if (!assetMergeCompleted && assetStagingCompleted)
                        {
                            await ThrowIfAbortRequestedAsync(run.Id, ct);
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.MergePending,
                                ct
                            );
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Merging,
                                ct
                            );
                            var assetMergeStartedAt = DateTimeOffset.UtcNow;
                            assetMergeSummary = await ExecuteWithConcurrencyRetryAsync(
                                () =>
                                    ProcessStagedAssetsAsync(
                                        run.Id,
                                        tenantId,
                                        source.SourceKey,
                                        softwareSnapshot?.Id,
                                        ct
                                    ),
                                source.SourceName,
                                tenantId,
                                ct
                            );
                            var assetMergeBatchNumber = await GetCheckpointBatchNumberAsync(
                                run.Id,
                                "asset-staging",
                                ct
                            );
                            await CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                "asset-merge",
                                assetMergeBatchNumber,
                                null,
                                assetMergeSummary.MergedAssetCount,
                                "Completed",
                                ct
                            );
                            assetMergeCompleted = true;
                            _logger.LogInformation(
                                "Asset merge phase completed for ingestion run {IngestionRunId}. Duration: {DurationMs} ms.",
                                run.Id,
                                (DateTimeOffset.UtcNow - assetMergeStartedAt).TotalMilliseconds
                            );
                        }

                        if (!vulnerabilityStagingCompleted && source is IVulnerabilityBatchSource batchSource)
                        {
                            await ThrowIfAbortRequestedAsync(run.Id, ct);
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Staging,
                                ct
                            );
                            fetchedVulnerabilityCount = await StageVulnerabilityBatchesAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                batchSource,
                                ct
                            );
                            vulnerabilityStagingCompleted = true;
                        }
                        else if (!vulnerabilityStagingCompleted)
                        {
                            await ThrowIfAbortRequestedAsync(run.Id, ct);
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Staging,
                                ct
                            );
                            var results = await source.FetchVulnerabilitiesAsync(tenantId, ct);
                            fetchedVulnerabilityCount = results.Count;
                            var normalizedResults = NormalizeResults(results);
                            await StageVulnerabilitiesAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                normalizedResults,
                                0,
                                ct
                            );
                            await CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                "vulnerability-staging",
                                0,
                                null,
                                normalizedResults.Count,
                                "Staged",
                                ct
                            );
                            vulnerabilityStagingCompleted = true;
                        }
                        else
                        {
                            fetchedVulnerabilityCount = await _dbContext
                                .StagedVulnerabilities.IgnoreQueryFilters()
                                .CountAsync(item => item.IngestionRunId == run.Id, ct);
                        }

                        if (!vulnerabilityMergeCompleted && vulnerabilityStagingCompleted)
                        {
                            await ThrowIfAbortRequestedAsync(run.Id, ct);
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.MergePending,
                                ct
                            );
                            await UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Merging,
                                ct
                            );
                            var vulnerabilityMergeStartedAt = DateTimeOffset.UtcNow;
                            vulnerabilityMergeSummary = await ExecuteWithConcurrencyRetryAsync(
                                () =>
                                    ProcessStagedResultsAsync(
                                        run.Id,
                                        tenantId,
                                        source.SourceKey,
                                        softwareSnapshot?.Id,
                                        source.SourceName,
                                        ct
                                    ),
                                source.SourceName,
                                tenantId,
                                ct
                            );
                            var vulnerabilityMergeBatchNumber = await GetCheckpointBatchNumberAsync(
                                run.Id,
                                "vulnerability-staging",
                                ct
                            );
                            await CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                "vulnerability-merge",
                                vulnerabilityMergeBatchNumber,
                                null,
                                vulnerabilityMergeSummary.MergedExposureCount,
                                "Completed",
                                ct
                            );
                            vulnerabilityMergeCompleted = true;
                            _logger.LogInformation(
                                "Vulnerability merge phase completed for ingestion run {IngestionRunId}. Duration: {DurationMs} ms. Staged vulnerabilities: {StagedVulnerabilityCount}. Persisted vulnerabilities: {PersistedVulnerabilityCount}. Opened projections: {OpenedProjectionCount}. Resolved projections: {ResolvedProjectionCount}.",
                                run.Id,
                                (DateTimeOffset.UtcNow - vulnerabilityMergeStartedAt).TotalMilliseconds,
                                vulnerabilityMergeSummary.StagedVulnerabilityCount,
                                vulnerabilityMergeSummary.PersistedVulnerabilityCount,
                                vulnerabilityMergeSummary.OpenedProjectionCount,
                                vulnerabilityMergeSummary.ResolvedProjectionCount
                            );
                        }

                        await EnqueueEnrichmentJobsForRunAsync(run.Id, tenantId, ct);

                        await _deviceRuleEvaluationService.EvaluateRulesAsync(tenantId, ct);

                        if (SupportsSoftwareSnapshots(source.SourceKey))
                        {
                            softwareSnapshot ??= await GetOrCreateBuildingSoftwareSnapshotAsync(
                                tenantId,
                                source.SourceKey,
                                run.Id,
                                ct
                            );
                            var softwareMatchStartedAt = DateTimeOffset.UtcNow;
                            await ExecuteWithConcurrencyRetryAsync(
                                async () =>
                                {
                                    await RunExposureDerivationAsync(tenantId, ct);
                                    return true;
                                },
                                source.SourceName,
                                tenantId,
                                ct
                            );
                            _logger.LogInformation(
                                "Software vulnerability match sync completed for ingestion run {IngestionRunId}. Snapshot: {SnapshotId}. Duration: {DurationMs} ms.",
                                run.Id,
                                softwareSnapshot.Id,
                                (DateTimeOffset.UtcNow - softwareMatchStartedAt).TotalMilliseconds
                            );
                            var snapshotPublishStartedAt = DateTimeOffset.UtcNow;
                            await PublishSnapshotAsync(
                                tenantId,
                                source.SourceKey,
                                softwareSnapshot.Id,
                                ct
                            );
                            if (_remediationDecisionService is not null)
                            {
                                await _remediationDecisionService.ReconcileResolvedSoftwareRemediationsAsync(
                                    tenantId,
                                    softwareSnapshot.Id,
                                    ct
                                );
                            }
                            _logger.LogInformation(
                                "Snapshot publish completed for ingestion run {IngestionRunId}. Snapshot: {SnapshotId}. Duration: {DurationMs} ms.",
                                run.Id,
                                softwareSnapshot.Id,
                                (DateTimeOffset.UtcNow - snapshotPublishStartedAt).TotalMilliseconds
                            );
                        }
                        else
                        {
                            var softwareMatchStartedAt = DateTimeOffset.UtcNow;
                            await ExecuteWithConcurrencyRetryAsync(
                                async () =>
                                {
                                    await RunExposureDerivationAsync(tenantId, ct);
                                    return true;
                                },
                                source.SourceName,
                                tenantId,
                                ct
                            );
                            if (_remediationDecisionService is not null)
                            {
                                await _remediationDecisionService.ReconcileResolvedSoftwareRemediationsAsync(
                                    tenantId,
                                    null,
                                    ct
                                );
                            }
                            _logger.LogInformation(
                                "Exposure derivation sync completed for ingestion run {IngestionRunId}. Duration: {DurationMs} ms.",
                                run.Id,
                                (DateTimeOffset.UtcNow - softwareMatchStartedAt).TotalMilliseconds
                            );
                        }

                        await _exposureAssessmentService.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, ct);
                        await _dbContext.SaveChangesAsync(ct);
                        await _riskScoreService.RecalculateForTenantAsync(tenantId, ct);
                        _logger.LogInformation(
                            "Vulnerability merge for {Source} tenant {TenantId}: stagedVulnerabilities={StagedVulnerabilityCount} persistedVulnerabilities={PersistedVulnerabilityCount}",
                            source.SourceName,
                            tenantId,
                            vulnerabilityMergeSummary.StagedVulnerabilityCount,
                            vulnerabilityMergeSummary.PersistedVulnerabilityCount
                        );

                        _logger.LogInformation(
                            "Completed ingestion from {Source} for tenant {TenantId}: {Count} vulnerabilities",
                            source.SourceName,
                            tenantId,
                            fetchedVulnerabilityCount
                        );

                        await UpdateRuntimeStateAsync(
                            tenantId,
                            source.SourceKey,
                            runtime =>
                            {
                                var now = DateTimeOffset.UtcNow;
                                runtime.LastCompletedAt = now;
                                runtime.LastSucceededAt = now;
                                runtime.LastStatus = IngestionRunStatuses.Succeeded;
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
                            vulnerabilityMergeSummary,
                            assetMergeSummary,
                            deactivatedMachineCount,
                            null,
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
                        var failureStatus = IngestionFailurePolicy.IsTerminal(ex)
                            ? IngestionRunStatuses.FailedTerminal
                            : IngestionRunStatuses.FailedRecoverable;
                        var failureReason = IngestionFailurePolicy.Describe(ex);
                        _logger.LogError(
                            ex,
                            "Error during ingestion from {Source} for tenant {TenantId}",
                            source.SourceName,
                            tenantId
                        );

                        if (
                            softwareSnapshot is not null
                            && failureStatus == IngestionRunStatuses.FailedTerminal
                        )
                        {
                            await DiscardBuildingSnapshotAsync(
                                tenantId,
                                source.SourceKey,
                                softwareSnapshot.Id,
                                ct
                            );
                        }

                        await UpdateRuntimeStateAsync(
                            tenantId,
                            source.SourceKey,
                            runtime =>
                            {
                                runtime.LastCompletedAt = DateTimeOffset.UtcNow;
                                runtime.LastStatus = failureStatus;
                                runtime.LastError = failureReason;
                            },
                            ct
                        );
                        await CompleteIngestionRunAsync(
                            run.Id,
                            tenantId,
                            source.SourceKey,
                            succeeded: false,
                            error: failureReason,
                            vulnerabilityMergeSummary,
                            assetMergeSummary,
                            deactivatedMachineCount,
                            failureStatus,
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

    private async Task<int> RefreshDeviceActivityForTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(DeviceInactiveThreshold);
        var devices = await _dbContext.Devices
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

        await _dbContext.SaveChangesAsync(ct);
        return deactivatedCount;
    }

    private sealed record AssetBatchStageSummary(
        int AssetCount,
        int SoftwareCount,
        int LinkCount,
        int SoftwareWithoutMachineReferencesCount,
        int BatchNumber
    );

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

    private async Task UpdateActiveRunStatusAsync(
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

    private async Task UpdateIngestionRunStatusAsync(Guid runId, string status, CancellationToken ct)
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

    private async Task<AcquiredIngestionRun?> TryAcquireIngestionRunAsync(
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

    private async Task FinalizeAbortedRunIfPendingAsync(
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

    private async Task ThrowIfAbortRequestedAsync(Guid runId, CancellationToken ct)
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

    private async Task CompleteIngestionRunAsync(
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

    private async Task<IngestionArtifactCleanupSummary> CleanupExpiredIngestionArtifactsAsync(
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

    private async Task ReleaseIngestionLeaseAsync(
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

    private async Task<bool> IsCheckpointCompletedAsync(
        Guid ingestionRunId,
        string phase,
        CancellationToken ct
    )
    {
        return await _dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .AnyAsync(
                item =>
                    item.IngestionRunId == ingestionRunId
                    && item.Phase == phase
                    && item.Status == "Completed",
                ct
            );
    }

    private async Task<int> GetCheckpointBatchNumberAsync(
        Guid ingestionRunId,
        string phase,
        CancellationToken ct
    )
    {
        return await _dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId && item.Phase == phase)
            .Select(item => item.BatchNumber)
            .FirstOrDefaultAsync(ct);
    }

    private async Task CommitCheckpointAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        string phase,
        int batchNumber,
        string? cursorJson,
        int recordsCommitted,
        string status,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var checkpoint = await _dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.IngestionRunId == ingestionRunId && item.Phase == phase,
                ct
            );

        if (checkpoint is null)
        {
            checkpoint = IngestionCheckpoint.Start(
                ingestionRunId,
                tenantId,
                normalizedSourceKey,
                phase,
                DateTimeOffset.UtcNow
            );
            await _dbContext.IngestionCheckpoints.AddAsync(checkpoint, ct);
        }

        checkpoint.CommitBatch(
            batchNumber,
            cursorJson,
            recordsCommitted,
            status,
            DateTimeOffset.UtcNow
        );

        await _dbContext.SaveChangesAsync(ct);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task StageVulnerabilitiesAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        IReadOnlyList<IngestionResult> results,
        int batchNumber,
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
                JsonSerializer.Serialize(result with { AffectedAssets = [] }, StagingSerializerOptions.Instance),
                stagedAt,
                batchNumber
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
                    JsonSerializer.Serialize(affectedAsset, StagingSerializerOptions.Instance),
                    stagedAt,
                    batchNumber
                )
            )
        );

        await _dbContext.StagedVulnerabilities.AddRangeAsync(rows, ct);
        await _dbContext.StagedVulnerabilityExposures.AddRangeAsync(exposures, ct);
        await _dbContext.SaveChangesAsync(ct);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<int> StageVulnerabilityBatchesAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        IVulnerabilityBatchSource batchSource,
        CancellationToken ct
    )
    {
        var checkpoint = await _dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item =>
                    item.IngestionRunId == ingestionRunId
                    && item.Phase == "vulnerability-staging",
                ct
            );
        var batchNumber = checkpoint?.BatchNumber ?? 0;
        var cursorJson =
            string.IsNullOrWhiteSpace(checkpoint?.CursorJson) ? null : checkpoint.CursorJson;
        var totalResults = 0;

        while (true)
        {
            await ThrowIfAbortRequestedAsync(ingestionRunId, ct);
            batchNumber++;
            var batch = await batchSource.FetchVulnerabilityBatchAsync(
                tenantId,
                cursorJson,
                VulnerabilityBatchSize,
                ct
            );
            var normalizedResults = NormalizeResults(batch.Items);

            if (normalizedResults.Count > 0)
            {
                totalResults += normalizedResults.Count;
                await StageVulnerabilitiesAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    normalizedResults,
                    batchNumber,
                    ct
                );
                await CommitCheckpointAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    "vulnerability-staging",
                    batchNumber,
                    batch.NextCursorJson,
                    normalizedResults.Count,
                    batch.IsComplete ? "Completed" : "Running",
                    ct
                );
            }
            else
            {
                await CommitCheckpointAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    "vulnerability-staging",
                    batchNumber,
                    batch.NextCursorJson,
                    0,
                    batch.IsComplete ? "Completed" : "Running",
                    ct
                );
            }

            if (batch.IsComplete)
            {
                break;
            }

            cursorJson = batch.NextCursorJson;
        }

        return totalResults;
    }

    private async Task StageAssetInventorySnapshotAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        IngestionAssetInventorySnapshot snapshot,
        int batchNumber,
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
            var stagedDeviceRecords = snapshot.Assets.Select(asset =>
                StagedDevice.Create(
                    ingestionRunId,
                    tenantId,
                    normalizedSourceKey,
                    asset.ExternalId,
                    asset.Name,
                    asset.AssetType,
                    JsonSerializer.Serialize(asset, StagingSerializerOptions.Instance),
                    stagedAt,
                    batchNumber
                )
            );
            await _dbContext.StagedDevices.AddRangeAsync(stagedDeviceRecords, ct);
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
                    JsonSerializer.Serialize(link, StagingSerializerOptions.Instance),
                    stagedAt,
                    batchNumber
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
            .Where(v => externalIds.Contains(v.ExternalId))
            .Select(v => v.Id)
            .Distinct()
            .ToListAsync(ct);

        await _enrichmentJobEnqueuer.EnqueueVulnerabilityJobsAsync(
            tenantId,
            vulnerabilityIds,
            ct
        );

        var normalizedSoftwareIds = await _dbContext
            .SoftwareTenantRecords.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(ts => ts.TenantId == tenantId)
            .Select(ts => ts.SoftwareProductId)
            .Distinct()
            .ToListAsync(ct);

        if (normalizedSoftwareIds.Count > 0)
        {
            await _enrichmentJobEnqueuer.EnqueueSoftwareEndOfLifeJobsAsync(
                tenantId,
                normalizedSoftwareIds,
                ct
            );
            await _enrichmentJobEnqueuer.EnqueueSoftwareSupplyChainJobsAsync(
                tenantId,
                normalizedSoftwareIds,
                ct
            );
        }
    }

    internal async Task<StagedVulnerabilityMergeSummary> ProcessStagedResultsAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        Guid? snapshotId,
        string sourceName,
        CancellationToken ct
    )
    {
        var now = DateTimeOffset.UtcNow;

        // ── Step 0: Load staged exposures up front so we can derive applicabilities
        //            per vulnerability before resolving vulnerabilities in Step 1.
        //            Without this, VulnerabilityApplicability rows would stay empty
        //            and ExposureDerivationService would resolve every exposure the
        //            downstream Step 4 creates.
        var stagedExposures = await _dbContext
            .StagedVulnerabilityExposures.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ToListAsync(ct);

        var applicabilitiesByVulnExternalId = BuildApplicabilityInputsFromStagedExposures(stagedExposures);

        // ── Step 1: Load and upsert staged vulnerabilities into Vulnerability table ──
        var stagedVulns = await _dbContext
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .ToListAsync(ct);

        var stagedVulnCount = stagedVulns.Count;
        var persistedVulnCount = 0;

        // Resolve (upsert) in batches of VulnerabilityBatchSize
        var vulnExternalIdToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < stagedVulns.Count; i += VulnerabilityBatchSize)
        {
            var batch = stagedVulns.Skip(i).Take(VulnerabilityBatchSize);
            foreach (var staged in batch)
            {
                IngestionResult? payload = null;
                if (!string.IsNullOrWhiteSpace(staged.PayloadJson))
                {
                    try
                    {
                        payload = JsonSerializer.Deserialize<IngestionResult>(staged.PayloadJson);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize PayloadJson for staged vulnerability {Id}", staged.Id);
                    }
                }

                var applicabilities = applicabilitiesByVulnExternalId.TryGetValue(
                    staged.ExternalId, out var apps)
                    ? apps
                    : (IReadOnlyList<VulnerabilityApplicabilityInput>)[];

                var input = new VulnerabilityResolveInput(
                    Source: staged.SourceKey,
                    ExternalId: staged.ExternalId,
                    Title: payload?.Title ?? staged.Title,
                    Description: payload?.Description ?? string.Empty,
                    VendorSeverity: staged.VendorSeverity,
                    CvssScore: payload?.CvssScore,
                    CvssVector: payload?.CvssVector,
                    PublishedDate: payload?.PublishedDate,
                    References: payload?.References?
                        .Select(r => new VulnerabilityReferenceInput(r.Url, r.Source, r.Tags))
                        .ToList() ?? [],
                    Applicabilities: applicabilities
                );

                var vuln = await _vulnerabilityResolver.ResolveAsync(input, ct);
                vulnExternalIdToId[staged.ExternalId] = vuln.Id;
                persistedVulnCount++;
            }

            await _dbContext.SaveChangesAsync(ct);
            await UpdateVulnerabilityMergeProgressAsync(stagedVulnCount, persistedVulnCount, ct);
        }

        // ── Step 2: Build device ExternalId → Id map from the staged exposures loaded in Step 0 ──
        var stagedExposureCount = stagedExposures.Count;

        var assetExternalIds = stagedExposures
            .Select(e => e.AssetExternalId)
            .Distinct()
            .ToList();

        var deviceIdByExternalId = await _dbContext.Devices
            .Where(d => d.TenantId == tenantId && assetExternalIds.Contains(d.ExternalId))
            .Select(d => new { d.ExternalId, d.Id })
            .ToDictionaryAsync(d => d.ExternalId, d => d.Id, ct);

        // ── Step 2b: Build a (DeviceId, CanonicalProductKey) → InstalledSoftware lookup ──
        // Used below to populate SoftwareProductId/InstalledSoftwareId on new exposures.
        var deviceIds = deviceIdByExternalId.Values.ToList();
        var installedSoftwareLookup = await _dbContext.InstalledSoftware
            .Where(i => i.TenantId == tenantId && deviceIds.Contains(i.DeviceId))
            .Select(i => new
            {
                i.DeviceId,
                i.SoftwareProductId,
                i.Id,
                ProductKey = _dbContext.SoftwareProducts
                    .Where(p => p.Id == i.SoftwareProductId)
                    .Select(p => p.CanonicalProductKey)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);
        var installedByDeviceAndProduct = installedSoftwareLookup
            .Where(i => i.ProductKey != null)
            .GroupBy(i => (i.DeviceId, i.ProductKey!))
            .ToDictionary(
                g => g.Key,
                g => (g.First().SoftwareProductId, g.First().Id));

        // ── Step 3: Load existing DeviceVulnerabilityExposures for this tenant (for upsert) ──
        var existing = await _dbContext.DeviceVulnerabilityExposures
            .Where(e => e.TenantId == tenantId)
            .ToListAsync(ct);
        var existingByPair = existing.ToDictionary(e => (e.DeviceId, e.VulnerabilityId));

        // Track active pairs from this ingestion run (for resolving stale exposures)
        var activePairs = new HashSet<(Guid DeviceId, Guid VulnerabilityId)>();

        var mergedExposureCount = 0;
        var openedCount = 0;
        var resolvedCount = 0;

        // ── Step 4: Upsert exposures in batches ──
        for (var i = 0; i < stagedExposures.Count; i += VulnerabilityBatchSize)
        {
            var batch = stagedExposures.Skip(i).Take(VulnerabilityBatchSize);
            foreach (var exposure in batch)
            {
                if (!vulnExternalIdToId.TryGetValue(exposure.VulnerabilityExternalId, out var vulnerabilityId))
                {
                    // Vulnerability wasn't staged in this run — try to look it up directly
                    var directLookup = await _dbContext.Vulnerabilities
                        .Where(v => v.Source == sourceKey && v.ExternalId == exposure.VulnerabilityExternalId)
                        .Select(v => v.Id)
                        .FirstOrDefaultAsync(ct);

                    if (directLookup == Guid.Empty)
                    {
                        _logger.LogDebug(
                            "Skipping exposure for unknown vulnerability {ExternalId}",
                            exposure.VulnerabilityExternalId);
                        continue;
                    }

                    vulnerabilityId = directLookup;
                }

                if (!deviceIdByExternalId.TryGetValue(exposure.AssetExternalId, out var deviceId))
                {
                    _logger.LogDebug(
                        "Skipping exposure for unknown device external id {ExternalId}",
                        exposure.AssetExternalId);
                    continue;
                }

                var pair = (deviceId, vulnerabilityId);
                activePairs.Add(pair);

                if (existingByPair.TryGetValue(pair, out var existingExposure))
                {
                    if (existingExposure.Status == ExposureStatus.Resolved)
                    {
                        existingExposure.Reopen(now);
                        openedCount++;
                    }
                    else
                    {
                        existingExposure.Reobserve(now);
                    }
                }
                else
                {
                    // Attempt to resolve the software product for this exposure from the staged payload.
                    Guid? softwareProductId = null;
                    Guid? installedSoftwareId = null;
                    IngestionAffectedAsset? affectedAssetPayload = null;
                    if (!string.IsNullOrWhiteSpace(exposure.PayloadJson))
                    {
                        try
                        {
                            affectedAssetPayload = JsonSerializer.Deserialize<IngestionAffectedAsset>(
                                exposure.PayloadJson, StagingSerializerOptions.Instance);
                        }
                        catch (JsonException) { /* best-effort */ }
                    }

                    if (affectedAssetPayload is { ProductVendor: not null, ProductName: not null })
                    {
                        var canonicalKey =
                            $"{affectedAssetPayload.ProductVendor.Trim().ToLowerInvariant()}::{affectedAssetPayload.ProductName.Trim().ToLowerInvariant()}";
                        if (installedByDeviceAndProduct.TryGetValue((deviceId, canonicalKey), out var swInfo))
                        {
                            softwareProductId = swInfo.SoftwareProductId;
                            installedSoftwareId = swInfo.Id;
                        }
                    }

                    var fresh = DeviceVulnerabilityExposure.Observe(
                        tenantId,
                        deviceId,
                        vulnerabilityId,
                        softwareProductId: softwareProductId,
                        installedSoftwareId: installedSoftwareId,
                        matchedVersion: string.Empty,
                        ExposureMatchSource.Product,
                        now);

                    _dbContext.DeviceVulnerabilityExposures.Add(fresh);
                    existingByPair[pair] = fresh;
                    openedCount++;
                }

                mergedExposureCount++;
            }

            await _dbContext.SaveChangesAsync(ct);
        }

        // ── Step 5: Resolve exposures absent from this ingestion run ──
        foreach (var exp in existing)
        {
            var pair = (exp.DeviceId, exp.VulnerabilityId);
            if (activePairs.Contains(pair) || exp.Status == ExposureStatus.Resolved)
                continue;

            exp.Resolve(now);
            resolvedCount++;
        }

        if (resolvedCount > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
        }

        await UpdateVulnerabilityMergeProgressAsync(stagedVulnCount, persistedVulnCount, ct);

        return new StagedVulnerabilityMergeSummary(
            stagedVulnCount,
            persistedVulnCount,
            stagedExposureCount,
            mergedExposureCount,
            openedCount,
            resolvedCount);

        async Task UpdateVulnerabilityMergeProgressAsync(
            int stagedVulnerabilityCount,
            int persistedVulnerabilityCount,
            CancellationToken callbackCt
        )
        {
            if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                var run = await _dbContext
                    .IngestionRuns.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(item => item.Id == ingestionRunId, callbackCt);
                if (run is null)
                {
                    return;
                }

                run.UpdateVulnerabilityMergeProgress(
                    stagedVulnerabilityCount,
                    persistedVulnerabilityCount
                );
                await _dbContext.SaveChangesAsync(callbackCt);
                return;
            }

            await _dbContext
                .IngestionRuns.IgnoreQueryFilters()
                .Where(item => item.Id == ingestionRunId && !item.CompletedAt.HasValue)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                item => item.StagedVulnerabilityCount,
                                stagedVulnerabilityCount
                            )
                            .SetProperty(
                                item => item.PersistedVulnerabilityCount,
                                persistedVulnerabilityCount
                            ),
                    callbackCt
                );
        }
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
            0,
            ct
        );
        await CommitCheckpointAsync(
            run.Id,
            tenantId,
            run.SourceKey,
            "asset-staging",
            0,
            null,
            normalizedSnapshot.Assets.Count + normalizedSnapshot.DeviceSoftwareLinks.Count,
            "Staged",
            ct
        );
        await ProcessStagedAssetsAsync(run.Id, tenantId, run.SourceKey, null, ct);
        await CommitCheckpointAsync(
            run.Id,
            tenantId,
            run.SourceKey,
            "asset-merge",
            0,
            null,
            normalizedSnapshot.Assets.Count + normalizedSnapshot.DeviceSoftwareLinks.Count,
            "Completed",
            ct
        );
    }

    private async Task<AssetBatchStageSummary> StageAssetBatchesAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        IAssetInventoryBatchSource batchSource,
        CancellationToken ct
    )
    {
        var checkpoint = await _dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item =>
                item.IngestionRunId == ingestionRunId
                && item.TenantId == tenantId
                && item.SourceKey == sourceKey
                && item.Phase == "asset-staging",
                ct
            );

        var batchNumber = checkpoint?.BatchNumber ?? 0;
        var cursorJson = string.IsNullOrWhiteSpace(checkpoint?.CursorJson)
            ? null
            : checkpoint!.CursorJson;
        var totalAssets = 0;
        var totalSoftware = 0;
        var totalLinks = 0;
        var totalSoftwareWithoutMachineReferences = 0;

        while (true)
        {
            await ThrowIfAbortRequestedAsync(ingestionRunId, ct);
            var batch = await batchSource.FetchAssetBatchAsync(
                tenantId,
                cursorJson,
                AssetBatchSize,
                ct
            );
            batchNumber++;

            var normalizedBatch = NormalizeAssetSnapshots(batch.Items);
            if (
                normalizedBatch.Assets.Count > 0
                || normalizedBatch.DeviceSoftwareLinks.Count > 0
                || normalizedBatch.RetrievedSoftwareCount > 0
                || normalizedBatch.SoftwareWithoutMachineReferencesCount > 0
            )
            {
                totalAssets += normalizedBatch.Assets.Count;
                totalSoftwareWithoutMachineReferences +=
                    normalizedBatch.SoftwareWithoutMachineReferencesCount;

                await StageAssetInventorySnapshotAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    normalizedBatch,
                    batchNumber,
                    ct
                );
                await CommitCheckpointAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    "asset-staging",
                    batchNumber,
                    batch.NextCursorJson,
                    normalizedBatch.Assets.Count + normalizedBatch.DeviceSoftwareLinks.Count,
                    batch.IsComplete ? "Completed" : "Running",
                    ct
                );
            }
            else if (batch.IsComplete)
            {
                await CommitCheckpointAsync(
                    ingestionRunId,
                    tenantId,
                    sourceKey,
                    "asset-staging",
                    batchNumber,
                    batch.NextCursorJson,
                    0,
                    "Completed",
                    ct
                );
            }

            if (batch.IsComplete)
            {
                break;
            }

            cursorJson = batch.NextCursorJson;
        }

        totalSoftware = await _dbContext
            .StagedDevices.IgnoreQueryFilters()
            .Where(
                item =>
                    item.IngestionRunId == ingestionRunId
                    && EF.Functions.Like(item.ExternalId, "defender-sw::%")
            )
            .Select(item => item.ExternalId)
            .Distinct()
            .CountAsync(
                ct
            );
        totalLinks = await _dbContext
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == ingestionRunId)
            .Select(item => new { item.DeviceExternalId, item.SoftwareExternalId })
            .Distinct()
            .CountAsync(ct);

        return new AssetBatchStageSummary(
            totalAssets,
            totalSoftware,
            totalLinks,
            totalSoftwareWithoutMachineReferences,
            batchNumber
        );
    }

    internal async Task<StagedAssetMergeSummary> ProcessStagedAssetsAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        Guid? snapshotId,
        CancellationToken ct
    )
    {
        // Canonical merge: writes Device + InstalledSoftware directly via resolvers.
        // The legacy NormalizedSoftwareProjectionService path is invoked after merge
        // for software projection.
        var canonicalSummary = await _stagedDeviceMergeService.MergeAsync(
            ingestionRunId,
            tenantId,
            ct
        );

        var persistedMachineCount =
            canonicalSummary.DevicesCreated + canonicalSummary.DevicesTouched;
        var persistedSoftwareCount =
            canonicalSummary.InstalledSoftwareCreated + canonicalSummary.InstalledSoftwareTouched;

        // The canonical merge service does not expose mid-run progress, so emit a
        // single final-state progress update after the merge completes.
        await UpdateAssetMergeProgressAsync(
            stagedMachineCount: persistedMachineCount,
            stagedSoftwareCount: persistedSoftwareCount,
            persistedMachineCount: persistedMachineCount,
            persistedSoftwareCount: persistedSoftwareCount,
            ct
        );

        // Adapt the 4-field canonical summary into the 13-field legacy shape so the
        // ~30 downstream call sites in IngestionService keep working unchanged.
        // Fields with no canonical equivalent are set to zero; they tracked concerns
        // (episodes, stale installations) that no longer exist after Phase 1.
        return new StagedAssetMergeSummary(
            StagedMachineCount: persistedMachineCount,
            StagedSoftwareCount: persistedSoftwareCount,
            MergedAssetCount: persistedMachineCount + persistedSoftwareCount,
            PersistedMachineCount: persistedMachineCount,
            PersistedSoftwareCount: persistedSoftwareCount,
            StagedSoftwareLinkCount: 0,
            ResolvedSoftwareLinkCount: 0,
            InstallationsCreated: canonicalSummary.InstalledSoftwareCreated,
            InstallationsTouched: canonicalSummary.InstalledSoftwareTouched,
            EpisodesOpened: 0,
            EpisodesSeen: 0,
            StaleInstallationsMarked: 0,
            InstallationsRemoved: 0
        );

        async Task UpdateAssetMergeProgressAsync(
            int stagedMachineCount,
            int stagedSoftwareCount,
            int persistedMachineCount,
            int persistedSoftwareCount,
            CancellationToken callbackCt
        )
        {
            if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                var run = await _dbContext
                    .IngestionRuns.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(item => item.Id == ingestionRunId, callbackCt);
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
                await _dbContext.SaveChangesAsync(callbackCt);
                return;
            }

            await _dbContext
                .IngestionRuns.IgnoreQueryFilters()
                .Where(item => item.Id == ingestionRunId && !item.CompletedAt.HasValue)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                item => item.StagedMachineCount,
                                stagedMachineCount
                            )
                            .SetProperty(
                                item => item.StagedSoftwareCount,
                                stagedSoftwareCount
                            )
                            .SetProperty(
                                item => item.PersistedMachineCount,
                                persistedMachineCount
                            )
                            .SetProperty(
                                item => item.DeactivatedMachineCount,
                                0
                            )
                            .SetProperty(
                                item => item.PersistedSoftwareCount,
                                persistedSoftwareCount
                            ),
                    callbackCt
                );
        }
    }

    private static bool SupportsSoftwareSnapshots(string sourceKey)
    {
        return sourceKey.Trim().ToLowerInvariant() == TenantSourceCatalog.DefenderSourceKey;
    }

    private async Task<IngestionSnapshot> GetOrCreateBuildingSoftwareSnapshotAsync(
        Guid tenantId,
        string sourceKey,
        Guid ingestionRunId,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var source = await _dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .FirstAsync(
                item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                ct
            );

        if (source.BuildingSnapshotId is Guid buildingSnapshotId)
        {
            var existing = await _dbContext
                .IngestionSnapshots.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == buildingSnapshotId, ct);
            if (
                existing is not null
                && existing.IngestionRunId == ingestionRunId
                && existing.Status == IngestionSnapshotStatuses.Building
            )
            {
                return existing;
            }

            if (existing is not null && existing.Status == IngestionSnapshotStatuses.Building)
            {
                existing.Discard();
                await CleanupSnapshotDataAsync(existing.Id, ct);
            }
        }

        var snapshot = IngestionSnapshot.Create(
            tenantId,
            normalizedSourceKey,
            ingestionRunId,
            DateTimeOffset.UtcNow
        );
        source.SetSnapshotPointers(source.ActiveSnapshotId, snapshot.Id);
        await _dbContext.IngestionSnapshots.AddAsync(snapshot, ct);
        await _dbContext.SaveChangesAsync(ct);
        return snapshot;
    }

    private async Task PublishSnapshotAsync(
        Guid tenantId,
        string sourceKey,
        Guid snapshotId,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var source = await _dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .FirstAsync(
                item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                ct
            );
        var snapshot = await _dbContext
            .IngestionSnapshots.IgnoreQueryFilters()
            .FirstAsync(item => item.Id == snapshotId, ct);

        Guid? retiredSnapshotId = null;
        if (source.ActiveSnapshotId is Guid previousActiveSnapshotId && previousActiveSnapshotId != snapshotId)
        {
            var previous = await _dbContext
                .IngestionSnapshots.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == previousActiveSnapshotId, ct);
            if (previous is not null)
            {
                previous.Discard();
                retiredSnapshotId = previous.Id;
            }
        }

        snapshot.MarkPublished();
        source.SetSnapshotPointers(snapshot.Id, null);
        await _dbContext.SaveChangesAsync(ct);

        if (retiredSnapshotId.HasValue)
        {
            await RekeyTenantSoftwareReferencesAsync(retiredSnapshotId.Value, snapshotId, ct);
            await CleanupSnapshotDataAsync(retiredSnapshotId.Value, ct);
        }
    }

    private async Task RekeyTenantSoftwareReferencesAsync(
        Guid oldSnapshotId,
        Guid newSnapshotId,
        CancellationToken ct
    )
    {
        var oldRows = await _dbContext
            .SoftwareTenantRecords.IgnoreQueryFilters()
            .Where(ts => ts.SnapshotId == oldSnapshotId)
            .Select(ts => new { ts.Id, ts.SoftwareProductId })
            .ToListAsync(ct);

        var newRows = await _dbContext
            .SoftwareTenantRecords.IgnoreQueryFilters()
            .Where(ts => ts.SnapshotId == newSnapshotId)
            .Select(ts => new { ts.Id, ts.SoftwareProductId })
            .ToListAsync(ct);

        var newByNormalized = newRows.ToDictionary(r => r.SoftwareProductId, r => r.Id);
        var oldToNew = oldRows
            .Where(old => newByNormalized.ContainsKey(old.SoftwareProductId))
            .ToDictionary(old => old.Id, old => newByNormalized[old.SoftwareProductId]);

        if (oldToNew.Count == 0)
            return;

        // Phase 4: Remediation entities (RemediationDecision, RemediationWorkflow, PatchingTask)
        // are now anchored on RemediationCase, which is keyed by (TenantId, SoftwareProductId).
        // RemediationCase IDs are stable across snapshot rotations — no re-keying required.
        await Task.CompletedTask;
    }

    private async Task DiscardBuildingSnapshotAsync(
        Guid tenantId,
        string sourceKey,
        Guid snapshotId,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var source = await _dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.SourceKey == normalizedSourceKey,
                ct
            );
        var snapshot = await _dbContext
            .IngestionSnapshots.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == snapshotId, ct);

        if (snapshot is not null && snapshot.Status == IngestionSnapshotStatuses.Building)
        {
            snapshot.Discard();
        }

        if (source is not null && source.BuildingSnapshotId == snapshotId)
        {
            source.SetSnapshotPointers(source.ActiveSnapshotId, null);
        }

        await _dbContext.SaveChangesAsync(ct);
        await CleanupSnapshotDataAsync(snapshotId, ct);
    }

    private async Task CleanupSnapshotDataAsync(Guid snapshotId, CancellationToken ct)
    {
        if (_dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            var tenantSoftware = await _dbContext
                .SoftwareTenantRecords.IgnoreQueryFilters()
                .Where(item => item.SnapshotId == snapshotId)
                .ToListAsync(ct);
            var installations = await _dbContext
                .SoftwareProductInstallations.IgnoreQueryFilters()
                .Where(item => item.SnapshotId == snapshotId)
                .ToListAsync(ct);

            _dbContext.SoftwareTenantRecords.RemoveRange(tenantSoftware);
            _dbContext.SoftwareProductInstallations.RemoveRange(installations);
            await _dbContext.SaveChangesAsync(ct);
            return;
        }

        await _dbContext
            .SoftwareProductInstallations.IgnoreQueryFilters()
            .Where(item => item.SnapshotId == snapshotId)
            .ExecuteDeleteAsync(ct);
        await _dbContext
            .SoftwareTenantRecords.IgnoreQueryFilters()
            .Where(item => item.SnapshotId == snapshotId)
            .ExecuteDeleteAsync(ct);
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

        return new IngestionAssetInventorySnapshot(
            assets,
            deviceSoftwareLinks,
            snapshot.RetrievedSoftwareCount,
            snapshot.SoftwareWithoutMachineReferencesCount
        );
    }

    private static IngestionAssetInventorySnapshot NormalizeAssetSnapshots(
        IReadOnlyList<IngestionAssetInventorySnapshot> snapshots
    )
    {
        if (snapshots.Count == 0)
        {
            return new IngestionAssetInventorySnapshot([], []);
        }

        return NormalizeAssetSnapshot(
            new IngestionAssetInventorySnapshot(
                snapshots.SelectMany(item => item.Assets).ToList(),
                snapshots.SelectMany(item => item.DeviceSoftwareLinks).ToList(),
                snapshots.Sum(item => item.RetrievedSoftwareCount),
                snapshots.Sum(item => item.SoftwareWithoutMachineReferencesCount)
            )
        );
    }

    private bool IsInMemoryProvider()
    {
        return string.Equals(
            _dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal
        );
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

    /// <summary>
    /// Derives one <see cref="VulnerabilityApplicabilityInput"/> per unique
    /// (vendor, product, version) triple present in the staged exposure payloads,
    /// keyed by vulnerability external id. CPE is built with
    /// <see cref="SoftwareProductResolver.DeriveCpe"/> so it matches the CPE assigned
    /// to <see cref="SoftwareProduct"/> rows created from observed software, letting
    /// <see cref="ExposureDerivationService"/> join installs ↔ applicabilities.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<VulnerabilityApplicabilityInput>>
        BuildApplicabilityInputsFromStagedExposures(IReadOnlyList<StagedVulnerabilityExposure> stagedExposures)
    {
        var byVuln = new Dictionary<string, Dictionary<(string Cpe, string? Version), VulnerabilityApplicabilityInput>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var staged in stagedExposures)
        {
            if (string.IsNullOrWhiteSpace(staged.PayloadJson))
            {
                continue;
            }

            IngestionAffectedAsset? asset;
            try
            {
                asset = JsonSerializer.Deserialize<IngestionAffectedAsset>(staged.PayloadJson, StagingSerializerOptions.Instance);
            }
            catch (JsonException)
            {
                continue;
            }

            if (asset is null || string.IsNullOrWhiteSpace(asset.ProductName))
            {
                continue;
            }

            var cpe = SoftwareProductResolver.DeriveCpe(asset.ProductVendor ?? string.Empty, asset.ProductName);
            var version = string.IsNullOrWhiteSpace(asset.ProductVersion) ? null : asset.ProductVersion;
            var key = (cpe, version);

            if (!byVuln.TryGetValue(staged.VulnerabilityExternalId, out var perVuln))
            {
                perVuln = new Dictionary<(string, string?), VulnerabilityApplicabilityInput>();
                byVuln[staged.VulnerabilityExternalId] = perVuln;
            }

            if (!perVuln.ContainsKey(key))
            {
                perVuln[key] = new VulnerabilityApplicabilityInput(
                    SoftwareProductId: null,
                    CpeCriteria: cpe,
                    Vulnerable: true,
                    VersionStartIncluding: null,
                    VersionStartExcluding: null,
                    VersionEndIncluding: version,
                    VersionEndExcluding: null);
            }
        }

        return byVuln.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<VulnerabilityApplicabilityInput>)kvp.Value.Values.ToList(),
            StringComparer.OrdinalIgnoreCase);
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

// Defined here after StagedVulnerabilityMergeService was deleted in Phase 2.
// Phase 3 will replace this with a proper merge summary from DeviceVulnerabilityExposure processing.
internal sealed record StagedVulnerabilityMergeSummary(
    int StagedVulnerabilityCount,
    int PersistedVulnerabilityCount,
    int StagedExposureCount,
    int MergedExposureCount,
    int OpenedProjectionCount,
    int ResolvedProjectionCount
);

// Moved here after StagedAssetMergeService was deleted in Phase 7c.
public sealed record StagedAssetMergeSummary(
    int StagedMachineCount,
    int StagedSoftwareCount,
    int MergedAssetCount,
    int PersistedMachineCount,
    int PersistedSoftwareCount,
    int StagedSoftwareLinkCount,
    int ResolvedSoftwareLinkCount,
    int InstallationsCreated,
    int InstallationsTouched,
    int EpisodesOpened,
    int EpisodesSeen,
    int StaleInstallationsMarked,
    int InstallationsRemoved
);
