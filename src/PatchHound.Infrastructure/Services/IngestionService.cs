using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private static readonly TimeSpan DeviceInactiveThreshold = TimeSpan.FromDays(30);
    private readonly PatchHoundDbContext _dbContext;
    private readonly IEnumerable<IIngestionSource> _sources;
    private readonly EnrichmentJobEnqueuer _enrichmentJobEnqueuer;
    private readonly IStagedDeviceMergeService _stagedDeviceMergeService;
    private readonly IStagedCloudApplicationMergeService _stagedCloudApplicationMergeService;
    private readonly IDeviceRuleEvaluationService _deviceRuleEvaluationService;
    private readonly ExposureDerivationService _exposureDerivationService;
    private readonly ExposureEpisodeService _exposureEpisodeService;
    private readonly ExposureAssessmentService _exposureAssessmentService;
    private readonly RiskScoreService _riskScoreService;
    private readonly VulnerabilityResolver _vulnerabilityResolver;
    private readonly NormalizedSoftwareProjectionService? _normalizedSoftwareProjectionService;
    private readonly RemediationDecisionService? _remediationDecisionService;
    private readonly IngestionLeaseManager _leaseManager;
    private readonly IngestionCheckpointWriter _checkpointWriter;
    private readonly IngestionStagingPipeline _stagingPipeline;
    private readonly IngestionSnapshotLifecycle _snapshotLifecycle;
    private readonly IIngestionBulkWriter _bulkWriter;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IIngestionSource> sources,
        EnrichmentJobEnqueuer enrichmentJobEnqueuer,
        IStagedDeviceMergeService stagedDeviceMergeService,
        IStagedCloudApplicationMergeService stagedCloudApplicationMergeService,
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
            stagedCloudApplicationMergeService,
            deviceRuleEvaluationService,
            exposureDerivationService,
            exposureEpisodeService,
            exposureAssessmentService,
            riskScoreService,
            new VulnerabilityResolver(dbContext, NullLogger<VulnerabilityResolver>.Instance),
            normalizedSoftwareProjectionService: null,
            remediationDecisionService: null,
            new IngestionLeaseManager(dbContext, NullLogger<IngestionLeaseManager>.Instance),
            new IngestionCheckpointWriter(dbContext),
            new IngestionStagingPipeline(dbContext, enrichmentJobEnqueuer, new IngestionLeaseManager(dbContext, NullLogger<IngestionLeaseManager>.Instance), new IngestionCheckpointWriter(dbContext)),
            new IngestionSnapshotLifecycle(dbContext),
            CreateBulkWriter(dbContext),
            logger
        ) { }

    public IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IIngestionSource> sources,
        EnrichmentJobEnqueuer enrichmentJobEnqueuer,
        IStagedDeviceMergeService stagedDeviceMergeService,
        IStagedCloudApplicationMergeService stagedCloudApplicationMergeService,
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
            stagedCloudApplicationMergeService,
            deviceRuleEvaluationService,
            exposureDerivationService,
            exposureEpisodeService,
            exposureAssessmentService,
            riskScoreService,
            vulnerabilityResolver,
            normalizedSoftwareProjectionService: null,
            remediationDecisionService: null,
            new IngestionLeaseManager(dbContext, NullLogger<IngestionLeaseManager>.Instance),
            new IngestionCheckpointWriter(dbContext),
            new IngestionStagingPipeline(dbContext, enrichmentJobEnqueuer, new IngestionLeaseManager(dbContext, NullLogger<IngestionLeaseManager>.Instance), new IngestionCheckpointWriter(dbContext)),
            new IngestionSnapshotLifecycle(dbContext),
            CreateBulkWriter(dbContext),
            logger
        ) { }

    public IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IIngestionSource> sources,
        EnrichmentJobEnqueuer enrichmentJobEnqueuer,
        IStagedDeviceMergeService stagedDeviceMergeService,
        IStagedCloudApplicationMergeService stagedCloudApplicationMergeService,
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
            stagedCloudApplicationMergeService,
            deviceRuleEvaluationService,
            exposureDerivationService,
            exposureEpisodeService,
            exposureAssessmentService,
            riskScoreService,
            new VulnerabilityResolver(dbContext, NullLogger<VulnerabilityResolver>.Instance),
            normalizedSoftwareProjectionService: null,
            remediationDecisionService,
            new IngestionLeaseManager(dbContext, NullLogger<IngestionLeaseManager>.Instance),
            new IngestionCheckpointWriter(dbContext),
            new IngestionStagingPipeline(dbContext, enrichmentJobEnqueuer, new IngestionLeaseManager(dbContext, NullLogger<IngestionLeaseManager>.Instance), new IngestionCheckpointWriter(dbContext)),
            new IngestionSnapshotLifecycle(dbContext),
            CreateBulkWriter(dbContext),
            logger
        ) { }

    private static IIngestionBulkWriter CreateBulkWriter(PatchHoundDbContext db) =>
        db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
            ? new InMemoryIngestionBulkWriter(db)
            : new PostgresIngestionBulkWriter(db);

    internal IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IIngestionSource> sources,
        EnrichmentJobEnqueuer enrichmentJobEnqueuer,
        IStagedDeviceMergeService stagedDeviceMergeService,
        IStagedCloudApplicationMergeService stagedCloudApplicationMergeService,
        IDeviceRuleEvaluationService deviceRuleEvaluationService,
        ExposureDerivationService exposureDerivationService,
        ExposureEpisodeService exposureEpisodeService,
        ExposureAssessmentService exposureAssessmentService,
        RiskScoreService riskScoreService,
        VulnerabilityResolver vulnerabilityResolver,
        NormalizedSoftwareProjectionService? normalizedSoftwareProjectionService,
        RemediationDecisionService? remediationDecisionService,
        IngestionLeaseManager leaseManager,
        IngestionCheckpointWriter checkpointWriter,
        IngestionStagingPipeline stagingPipeline,
        IngestionSnapshotLifecycle snapshotLifecycle,
        IIngestionBulkWriter bulkWriter,
        ILogger<IngestionService> logger
    )
    {
        _dbContext = dbContext;
        _sources = sources;
        _enrichmentJobEnqueuer = enrichmentJobEnqueuer;
        _stagedDeviceMergeService = stagedDeviceMergeService;
        _stagedCloudApplicationMergeService = stagedCloudApplicationMergeService;
        _deviceRuleEvaluationService = deviceRuleEvaluationService;
        _exposureDerivationService = exposureDerivationService;
        _exposureEpisodeService = exposureEpisodeService;
        _exposureAssessmentService = exposureAssessmentService;
        _riskScoreService = riskScoreService;
        _vulnerabilityResolver = vulnerabilityResolver;
        _normalizedSoftwareProjectionService = normalizedSoftwareProjectionService;
        _remediationDecisionService = remediationDecisionService;
        _leaseManager = leaseManager;
        _checkpointWriter = checkpointWriter;
        _stagingPipeline = stagingPipeline;
        _snapshotLifecycle = snapshotLifecycle;
        _bulkWriter = bulkWriter;
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
        await new RemediationCaseService(_dbContext).EnsureCasesForOpenExposuresAsync(tenantId, ct);
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
            var acquiredRun = await _leaseManager.TryAcquireIngestionRunAsync(tenantId, source.SourceKey, ct);
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
            var assetMergeSummary = new StagedAssetMergeSummary(0, 0, 0, 0, 0);
            var deactivatedMachineCount = 0;

            try
            {
                for (var attempt = 1; attempt <= MaxSourceAttempts; attempt++)
                {
                    await _leaseManager.UpdateRuntimeStateAsync(
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
                        await _leaseManager.ThrowIfAbortRequestedAsync(run.Id, ct);

                        _logger.LogInformation(
                            "Starting ingestion from {Source} for tenant {TenantId}",
                            source.SourceName,
                            tenantId
                        );

                        var assetStagingCompleted = await _checkpointWriter.IsCheckpointCompletedAsync(
                            run.Id,
                            CheckpointPhases.AssetStaging,
                            ct
                        );
                        var assetMergeCompleted = await _checkpointWriter.IsCheckpointCompletedAsync(
                            run.Id,
                            CheckpointPhases.AssetMerge,
                            ct
                        );
                        var vulnerabilityStagingCompleted = await _checkpointWriter.IsCheckpointCompletedAsync(
                            run.Id,
                            CheckpointPhases.VulnerabilityStaging,
                            ct
                        );
                        var vulnerabilityMergeCompleted = await _checkpointWriter.IsCheckpointCompletedAsync(
                            run.Id,
                            CheckpointPhases.VulnerabilityMerge,
                            ct
                        );

                        deactivatedMachineCount = await RefreshDeviceActivityForTenantAsync(tenantId, ct);

                        if (IngestionSnapshotLifecycle.SupportsSoftwareSnapshots(source.SourceKey))
                        {
                            softwareSnapshot ??= await _snapshotLifecycle.GetOrCreateBuildingSoftwareSnapshotAsync(
                                tenantId,
                                source.SourceKey,
                                run.Id,
                                ct
                            );
                        }

                        if (!assetStagingCompleted && source is IAssetInventoryBatchSource assetInventoryBatchSource)
                        {
                            await _leaseManager.ThrowIfAbortRequestedAsync(run.Id, ct);
                            await _leaseManager.UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Staging,
                                ct
                            );
                            var assetBatchSummary = await _stagingPipeline.StageAssetBatchesAsync(
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
                            await _leaseManager.UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.MergePending,
                                ct
                            );
                            await _leaseManager.ThrowIfAbortRequestedAsync(run.Id, ct);
                            await _leaseManager.UpdateActiveRunStatusAsync(
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
                            await _checkpointWriter.CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                CheckpointPhases.AssetMerge,
                                assetBatchSummary.BatchNumber,
                                null,
                                assetMergeSummary.MergedAssetCount,
                                CheckpointStatuses.Completed,
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
                            await _leaseManager.UpdateActiveRunStatusAsync(
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
                            var normalizedAssetSnapshot = IngestionStagingPipeline.NormalizeAssetSnapshot(assetSnapshot);
                            fetchedAssetCount = assetSnapshot.Assets.Count;
                            fetchedSoftwareCount = assetSnapshot.RetrievedSoftwareCount;
                            fetchedSoftwareInstallationCount = assetSnapshot
                                .DeviceSoftwareLinks
                                .Count;
                            softwareWithoutMachineReferencesCount =
                                assetSnapshot.SoftwareWithoutMachineReferencesCount;
                            await _stagingPipeline.StageAssetInventorySnapshotAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                normalizedAssetSnapshot,
                                0,
                                ct
                            );
                            await _checkpointWriter.CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                CheckpointPhases.AssetStaging,
                                0,
                                null,
                                normalizedAssetSnapshot.Assets.Count + normalizedAssetSnapshot.DeviceSoftwareLinks.Count,
                                CheckpointStatuses.Staged,
                                ct
                            );
                            await _leaseManager.UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.MergePending,
                                ct
                            );
                            await _leaseManager.UpdateActiveRunStatusAsync(
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
                            await _checkpointWriter.CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                CheckpointPhases.AssetMerge,
                                0,
                                null,
                                assetMergeSummary.MergedAssetCount,
                                CheckpointStatuses.Completed,
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
                            await _leaseManager.ThrowIfAbortRequestedAsync(run.Id, ct);
                            await _leaseManager.UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.MergePending,
                                ct
                            );
                            await _leaseManager.UpdateActiveRunStatusAsync(
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
                            var assetMergeBatchNumber = await _checkpointWriter.GetCheckpointBatchNumberAsync(
                                run.Id,
                                CheckpointPhases.AssetStaging,
                                ct
                            );
                            await _checkpointWriter.CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                CheckpointPhases.AssetMerge,
                                assetMergeBatchNumber,
                                null,
                                assetMergeSummary.MergedAssetCount,
                                CheckpointStatuses.Completed,
                                ct
                            );
                            assetMergeCompleted = true;
                            _logger.LogInformation(
                                "Asset merge phase completed for ingestion run {IngestionRunId}. Duration: {DurationMs} ms.",
                                run.Id,
                                (DateTimeOffset.UtcNow - assetMergeStartedAt).TotalMilliseconds
                            );
                        }

                        if (source is ICloudApplicationSource cloudAppSource)
                        {
                            await _leaseManager.ThrowIfAbortRequestedAsync(run.Id, ct);
                            await _leaseManager.UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Staging,
                                ct
                            );
                            var appSnapshot = await cloudAppSource.FetchCloudApplicationsAsync(tenantId, ct);
                            foreach (var app in appSnapshot.Applications)
                            {
                                var payloadJson = JsonSerializer.Serialize(app);
                                _dbContext.StagedCloudApplications.Add(
                                    StagedCloudApplication.Create(
                                        ingestionRunId: run.Id,
                                        tenantId: tenantId,
                                        sourceKey: source.SourceKey,
                                        externalId: app.ExternalId,
                                        name: app.Name,
                                        description: app.Description,
                                        payloadJson: payloadJson,
                                        stagedAt: DateTimeOffset.UtcNow
                                    )
                                );
                            }
                            await _dbContext.SaveChangesAsync(ct);

                            await _leaseManager.UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Merging,
                                ct
                            );
                            await _stagedCloudApplicationMergeService.MergeAsync(run.Id, tenantId, ct);
                            _logger.LogInformation(
                                "Cloud application merge completed for ingestion run {IngestionRunId}: {Count} applications staged.",
                                run.Id,
                                appSnapshot.Applications.Count
                            );
                        }

                        if (!vulnerabilityStagingCompleted && source is IVulnerabilityBatchSource batchSource)
                        {
                            await _leaseManager.ThrowIfAbortRequestedAsync(run.Id, ct);
                            await _leaseManager.UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Staging,
                                ct
                            );
                            fetchedVulnerabilityCount = await _stagingPipeline.StageVulnerabilityBatchesAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                batchSource,
                                ct
                            );
                            vulnerabilityStagingCompleted = true;
                        }
                        else if (!vulnerabilityStagingCompleted && source is IVulnerabilitySource vulnSource)
                        {
                            await _leaseManager.ThrowIfAbortRequestedAsync(run.Id, ct);
                            await _leaseManager.UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.Staging,
                                ct
                            );
                            var results = await vulnSource.FetchVulnerabilitiesAsync(tenantId, ct);
                            fetchedVulnerabilityCount = results.Count;
                            var normalizedResults = IngestionStagingPipeline.NormalizeResults(results);
                            await _stagingPipeline.StageVulnerabilitiesAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                normalizedResults,
                                0,
                                ct
                            );
                            await _checkpointWriter.CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                CheckpointPhases.VulnerabilityStaging,
                                0,
                                null,
                                normalizedResults.Count,
                                CheckpointStatuses.Staged,
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
                            await _leaseManager.ThrowIfAbortRequestedAsync(run.Id, ct);
                            await _leaseManager.UpdateActiveRunStatusAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                IngestionRunStatuses.MergePending,
                                ct
                            );
                            await _leaseManager.UpdateActiveRunStatusAsync(
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
                            var vulnerabilityMergeBatchNumber = await _checkpointWriter.GetCheckpointBatchNumberAsync(
                                run.Id,
                                CheckpointPhases.VulnerabilityStaging,
                                ct
                            );
                            await _checkpointWriter.CommitCheckpointAsync(
                                run.Id,
                                tenantId,
                                source.SourceKey,
                                CheckpointPhases.VulnerabilityMerge,
                                vulnerabilityMergeBatchNumber,
                                null,
                                vulnerabilityMergeSummary.MergedExposureCount,
                                CheckpointStatuses.Completed,
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

                        await _stagingPipeline.EnqueueEnrichmentJobsForRunAsync(run.Id, tenantId, ct);

                        if (_normalizedSoftwareProjectionService is not null)
                        {
                            await _normalizedSoftwareProjectionService.SyncTenantAsync(
                                tenantId,
                                softwareSnapshot?.Id,
                                ct
                            );
                        }

                        await _deviceRuleEvaluationService.EvaluateRulesAsync(tenantId, ct);

                        if (IngestionSnapshotLifecycle.SupportsSoftwareSnapshots(source.SourceKey))
                        {
                            softwareSnapshot ??= await _snapshotLifecycle.GetOrCreateBuildingSoftwareSnapshotAsync(
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
                            await _snapshotLifecycle.PublishSnapshotAsync(
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

                        await _leaseManager.UpdateRuntimeStateAsync(
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
                        await _leaseManager.CompleteIngestionRunAsync(
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
                            await _snapshotLifecycle.DiscardBuildingSnapshotAsync(
                                tenantId,
                                source.SourceKey,
                                softwareSnapshot.Id,
                                ct
                            );
                        }

                        await _leaseManager.UpdateRuntimeStateAsync(
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
                        await _leaseManager.CompleteIngestionRunAsync(
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
                    await _leaseManager.ReleaseIngestionLeaseAsync(tenantId, source.SourceKey, run.Id, ct);
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

        // Resolve (upsert) in batches of IngestionStagingPipeline.VulnerabilityBatchSize
        var vulnExternalIdToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < stagedVulns.Count; i += IngestionStagingPipeline.VulnerabilityBatchSize)
        {
            var batch = stagedVulns.Skip(i).Take(IngestionStagingPipeline.VulnerabilityBatchSize);
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
        for (var i = 0; i < stagedExposures.Count; i += IngestionStagingPipeline.VulnerabilityBatchSize)
        {
            var batch = stagedExposures.Skip(i).Take(IngestionStagingPipeline.VulnerabilityBatchSize);
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
            await _bulkWriter.UpdateVulnerabilityMergeProgressAsync(
                ingestionRunId,
                stagedVulnerabilityCount,
                persistedVulnerabilityCount,
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

        var normalizedSnapshot = IngestionStagingPipeline.NormalizeAssetSnapshot(snapshot);
        await _stagingPipeline.StageAssetInventorySnapshotAsync(
            run.Id,
            tenantId,
            run.SourceKey,
            normalizedSnapshot,
            0,
            ct
        );
        await _checkpointWriter.CommitCheckpointAsync(
            run.Id,
            tenantId,
            run.SourceKey,
            CheckpointPhases.AssetStaging,
            0,
            null,
            normalizedSnapshot.Assets.Count + normalizedSnapshot.DeviceSoftwareLinks.Count,
            CheckpointStatuses.Staged,
            ct
        );
        await ProcessStagedAssetsAsync(run.Id, tenantId, run.SourceKey, null, ct);
        await _checkpointWriter.CommitCheckpointAsync(
            run.Id,
            tenantId,
            run.SourceKey,
            CheckpointPhases.AssetMerge,
            0,
            null,
            normalizedSnapshot.Assets.Count + normalizedSnapshot.DeviceSoftwareLinks.Count,
            CheckpointStatuses.Completed,
            ct
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
            PersistedSoftwareCount: persistedSoftwareCount
        );

        async Task UpdateAssetMergeProgressAsync(
            int stagedMachineCount,
            int stagedSoftwareCount,
            int persistedMachineCount,
            int persistedSoftwareCount,
            CancellationToken callbackCt
        )
        {
            await _bulkWriter.UpdateAssetMergeProgressAsync(
                ingestionRunId,
                stagedMachineCount,
                stagedSoftwareCount,
                persistedMachineCount,
                persistedSoftwareCount,
                callbackCt
            );
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

public sealed record StagedAssetMergeSummary(
    int StagedMachineCount,
    int StagedSoftwareCount,
    int MergedAssetCount,
    int PersistedMachineCount,
    int PersistedSoftwareCount
);
