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
    private readonly PatchHoundDbContext _dbContext;
    private readonly IEnumerable<IVulnerabilitySource> _sources;
    private readonly IEnumerable<IVulnerabilityEnricher> _enrichers;
    private readonly SlaService _slaService;
    private readonly VulnerabilityAssessmentService _assessmentService;
    private readonly ILogger<IngestionService> _logger;

    // System user ID used as "assignedBy" for auto-created tasks
    private static readonly Guid SystemUserId = Guid.Empty;

    public IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IVulnerabilitySource> sources,
        IEnumerable<IVulnerabilityEnricher> enrichers,
        SlaService slaService,
        VulnerabilityAssessmentService assessmentService,
        ILogger<IngestionService> logger
    )
    {
        _dbContext = dbContext;
        _sources = sources;
        _enrichers = enrichers;
        _slaService = slaService;
        _assessmentService = assessmentService;
        _logger = logger;
    }

    public async Task RunIngestionAsync(Guid tenantId, CancellationToken ct)
    {
        await RunIngestionAsync(tenantId, null, ct);
    }

    public async Task RunIngestionAsync(Guid tenantId, string? sourceKey, CancellationToken ct)
    {
        var normalizedSourceKey = sourceKey?.Trim().ToLowerInvariant();
        var sources = string.IsNullOrWhiteSpace(sourceKey)
            ? _sources
            : _sources.Where(source => source.SourceKey == normalizedSourceKey);

        foreach (var source in sources)
        {
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
                    var assetSnapshot = await assetInventorySource.FetchAssetsAsync(tenantId, ct);
                    await ProcessAssetsAsync(tenantId, assetSnapshot, ct);
                }

                var results = await source.FetchVulnerabilitiesAsync(tenantId, ct);
                var enrichedResults = await EnrichResultsAsync(tenantId, results, ct);
                await ProcessResultsAsync(tenantId, source.SourceName, enrichedResults, ct);

                _logger.LogInformation(
                    "Completed ingestion from {Source} for tenant {TenantId}: {Count} vulnerabilities",
                    source.SourceName,
                    tenantId,
                    enrichedResults.Count
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
        source.UpdateRuntime(
            runtime.ManualRequestedAt,
            runtime.LastStartedAt,
            runtime.LastCompletedAt,
            runtime.LastSucceededAt,
            runtime.LastStatus,
            runtime.LastError
        );
        await _dbContext.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<IngestionResult>> EnrichResultsAsync(
        Guid tenantId,
        IReadOnlyList<IngestionResult> results,
        CancellationToken ct
    )
    {
        var current = results;
        foreach (var enricher in _enrichers)
        {
            await UpdateEnrichmentRuntimeStateAsync(
                enricher.SourceKey,
                runtime =>
                {
                    runtime.LastStartedAt = DateTimeOffset.UtcNow;
                    runtime.LastStatus = "Running";
                    runtime.LastError = string.Empty;
                },
                ct
            );

            try
            {
                current = await enricher.EnrichAsync(tenantId, current, ct);
                await UpdateEnrichmentRuntimeStateAsync(
                    enricher.SourceKey,
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
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error during enrichment from {Source} for tenant {TenantId}",
                    enricher.SourceKey,
                    tenantId
                );
                await UpdateEnrichmentRuntimeStateAsync(
                    enricher.SourceKey,
                    runtime =>
                    {
                        runtime.LastCompletedAt = DateTimeOffset.UtcNow;
                        runtime.LastStatus = "Failed";
                        runtime.LastError = $"Enrichment failed: {ex.GetType().Name}";
                    },
                    ct
                );
            }
        }

        return current;
    }

    private async Task UpdateEnrichmentRuntimeStateAsync(
        string sourceKey,
        Action<GlobalEnrichmentRuntimeState> update,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var source = await _dbContext
            .EnrichmentSourceConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.SourceKey == normalizedSourceKey, ct);

        if (source is null)
        {
            return;
        }

        var runtime = new GlobalEnrichmentRuntimeState(
            source.LastStartedAt,
            source.LastCompletedAt,
            source.LastSucceededAt,
            source.LastStatus,
            source.LastError
        );
        update(runtime);
        source.UpdateRuntime(
            runtime.LastStartedAt,
            runtime.LastCompletedAt,
            runtime.LastSucceededAt,
            runtime.LastStatus,
            runtime.LastError
        );
        await _dbContext.SaveChangesAsync(ct);
    }

    internal async Task ProcessResultsAsync(
        Guid tenantId,
        string sourceName,
        IReadOnlyList<IngestionResult> results,
        CancellationToken ct
    )
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        var seenPairKeys = new HashSet<string>(StringComparer.Ordinal);
        // Track assets created in this batch to avoid duplicate inserts
        var pendingAssets = new Dictionary<string, Asset>();

        foreach (var result in results)
        {
            await UpsertVulnerabilityAsync(
                tenantId,
                sourceName,
                result,
                pendingAssets,
                seenPairKeys,
                ct
            );
        }

        await ProcessMissingAssetEpisodesAsync(tenantId, sourceName, seenPairKeys, ct);
        await _dbContext.SaveChangesAsync(ct);
        await UpdateSourceVulnerabilityStatusesAsync(tenantId, sourceName, ct);

        await _dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task UpsertVulnerabilityAsync(
        Guid tenantId,
        string sourceName,
        IngestionResult result,
        Dictionary<string, Asset> pendingAssets,
        HashSet<string> seenPairKeys,
        CancellationToken ct
    )
    {
        var existing = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .Include(v => v.AffectedAssets)
            .FirstOrDefaultAsync(
                v => v.ExternalId == result.ExternalId && v.TenantId == tenantId,
                ct
            );

        bool isNew = existing is null;

        if (isNew)
        {
            existing = Vulnerability.Create(
                tenantId,
                result.ExternalId,
                result.Title,
                result.Description,
                result.VendorSeverity,
                BuildSourceSummary(sourceName, result.Sources),
                result.CvssScore,
                result.CvssVector,
                result.PublishedDate,
                result.ProductVendor,
                result.ProductName,
                result.ProductVersion,
                MapReferences(result.References),
                MapAffectedSoftware(result.AffectedSoftware)
            );

            await _dbContext.Vulnerabilities.AddAsync(existing, ct);
        }
        else
        {
            existing!.Update(
                result.Title,
                result.Description,
                result.VendorSeverity,
                BuildSourceSummary(sourceName, result.Sources),
                result.CvssScore,
                result.CvssVector,
                result.PublishedDate,
                result.ProductVendor,
                result.ProductName,
                result.ProductVersion,
                MapReferences(result.References),
                MapAffectedSoftware(result.AffectedSoftware)
            );
        }

        // Process affected assets
        foreach (var affectedAsset in result.AffectedAssets)
        {
            await UpsertVulnerabilityAssetAsync(
                tenantId,
                existing!,
                affectedAsset,
                pendingAssets,
                seenPairKeys,
                ct
            );
        }
    }

    private static IReadOnlyList<(
        string Url,
        string Source,
        IReadOnlyList<string> Tags
    )> MapReferences(IReadOnlyList<IngestionReference>? references)
    {
        return references
                ?.Select(reference =>
                    (reference.Url, reference.Source, (IReadOnlyList<string>)reference.Tags)
                )
                .ToList() ?? [];
    }

    private static IReadOnlyList<(
        bool Vulnerable,
        string Criteria,
        string? VersionStartIncluding,
        string? VersionStartExcluding,
        string? VersionEndIncluding,
        string? VersionEndExcluding
    )> MapAffectedSoftware(IReadOnlyList<IngestionAffectedSoftware>? affectedSoftware)
    {
        return affectedSoftware
                ?.Select(item =>
                    (
                        item.Vulnerable,
                        item.Criteria,
                        item.VersionStartIncluding,
                        item.VersionStartExcluding,
                        item.VersionEndIncluding,
                        item.VersionEndExcluding
                    )
                )
                .ToList() ?? [];
    }

    private static string BuildSourceSummary(string sourceName, IReadOnlyList<string>? sources)
    {
        return string.Join(
            "|",
            (sources ?? [])
                .Append(sourceName)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
        );
    }

    private async Task UpsertVulnerabilityAssetAsync(
        Guid tenantId,
        Vulnerability vulnerability,
        IngestionAffectedAsset affectedAsset,
        Dictionary<string, Asset> pendingAssets,
        HashSet<string> seenPairKeys,
        CancellationToken ct
    )
    {
        var now = DateTimeOffset.UtcNow;

        // Upsert the Asset — check pending batch first, then database
        var assetKey = $"{tenantId}:{affectedAsset.ExternalAssetId}";
        if (!pendingAssets.TryGetValue(assetKey, out var asset))
        {
            asset = await _dbContext
                .Assets.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    a => a.ExternalId == affectedAsset.ExternalAssetId && a.TenantId == tenantId,
                    ct
                );
        }

        if (asset is null)
        {
            asset = Asset.Create(
                tenantId,
                affectedAsset.ExternalAssetId,
                affectedAsset.AssetType,
                affectedAsset.AssetName,
                Criticality.Medium
            );

            await _dbContext.Assets.AddAsync(asset, ct);
            pendingAssets[assetKey] = asset;
        }
        else if (!string.Equals(asset.Name, affectedAsset.AssetName, StringComparison.Ordinal))
        {
            asset.UpdateDetails(affectedAsset.AssetName, asset.Description);
        }

        seenPairKeys.Add(BuildPairKey(vulnerability.Id, asset.Id));

        var existingVa = await _dbContext
            .VulnerabilityAssets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                va => va.VulnerabilityId == vulnerability.Id && va.AssetId == asset.Id,
                ct
            );

        var openEpisode = await _dbContext
            .VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                episode =>
                    episode.TenantId == tenantId
                    && episode.VulnerabilityId == vulnerability.Id
                    && episode.AssetId == asset.Id
                    && episode.Status == VulnerabilityStatus.Open,
                ct
            );

        var projectionOpened = false;

        if (openEpisode is not null)
        {
            openEpisode.Seen(now);

            if (existingVa is null)
            {
                existingVa = VulnerabilityAsset.Create(
                    vulnerability.Id,
                    asset.Id,
                    openEpisode.FirstSeenAt
                );
                await _dbContext.VulnerabilityAssets.AddAsync(existingVa, ct);
                projectionOpened = true;
            }
            else if (existingVa.Status != VulnerabilityStatus.Open)
            {
                existingVa.Reopen(openEpisode.FirstSeenAt);
                projectionOpened = true;
            }
        }
        else
        {
            var latestEpisode = await _dbContext
                .VulnerabilityAssetEpisodes.IgnoreQueryFilters()
                .Where(episode =>
                    episode.TenantId == tenantId
                    && episode.VulnerabilityId == vulnerability.Id
                    && episode.AssetId == asset.Id
                )
                .OrderByDescending(episode => episode.EpisodeNumber)
                .FirstOrDefaultAsync(ct);

            var nextEpisodeNumber = (latestEpisode?.EpisodeNumber ?? 0) + 1;
            var episode = VulnerabilityAssetEpisode.Create(
                tenantId,
                vulnerability.Id,
                asset.Id,
                nextEpisodeNumber,
                now
            );
            await _dbContext.VulnerabilityAssetEpisodes.AddAsync(episode, ct);

            if (existingVa is null)
            {
                existingVa = VulnerabilityAsset.Create(vulnerability.Id, asset.Id, now);
                await _dbContext.VulnerabilityAssets.AddAsync(existingVa, ct);
                projectionOpened = true;
            }
            else if (existingVa.Status != VulnerabilityStatus.Open)
            {
                existingVa.Reopen(now);
                projectionOpened = true;
            }
        }

        if (projectionOpened)
        {
            await EnsureOpenRemediationTaskAsync(tenantId, vulnerability, asset, ct);
        }

        _assessmentService.UpsertAssessment(
            tenantId,
            vulnerability,
            asset,
            await ResolveSecurityProfileAsync(asset, tenantId, ct)
        );
    }

    private async Task<AssetSecurityProfile?> ResolveSecurityProfileAsync(
        Asset asset,
        Guid tenantId,
        CancellationToken ct
    )
    {
        if (!asset.SecurityProfileId.HasValue)
        {
            return null;
        }

        return await _dbContext
            .AssetSecurityProfiles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                profile =>
                    profile.Id == asset.SecurityProfileId.Value && profile.TenantId == tenantId,
                ct
            );
    }

    private static Guid? ResolveAssignee(Asset asset)
    {
        // Fallback chain: Asset Owner (User) → Fallback Team (first member would be used,
        // but we use the team ID as assignee for now) → skip
        if (asset.OwnerUserId.HasValue)
            return asset.OwnerUserId.Value;

        if (asset.FallbackTeamId.HasValue)
            return asset.FallbackTeamId.Value;

        return null;
    }

    private async Task ProcessMissingAssetEpisodesAsync(
        Guid tenantId,
        string sourceName,
        HashSet<string> seenPairKeys,
        CancellationToken ct
    )
    {
        var now = DateTimeOffset.UtcNow;
        var openEpisodes = await _dbContext
            .VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .Where(episode =>
                episode.TenantId == tenantId
                && episode.Status == VulnerabilityStatus.Open
                && episode.Vulnerability.Source.Contains(sourceName)
            )
            .ToListAsync(ct);

        if (openEpisodes.Count == 0)
        {
            return;
        }

        var episodeVulnIds = openEpisodes.Select(e => e.VulnerabilityId).Distinct().ToList();
        var episodeAssetIds = openEpisodes.Select(e => e.AssetId).Distinct().ToList();

        var candidateProjections = await _dbContext
            .VulnerabilityAssets.IgnoreQueryFilters()
            .Where(va =>
                episodeVulnIds.Contains(va.VulnerabilityId) && episodeAssetIds.Contains(va.AssetId)
            )
            .ToListAsync(ct);

        var episodePairs = openEpisodes
            .Select(episode => BuildPairKey(episode.VulnerabilityId, episode.AssetId))
            .ToHashSet(StringComparer.Ordinal);

        var currentProjections = candidateProjections
            .Where(va => episodePairs.Contains(BuildPairKey(va.VulnerabilityId, va.AssetId)))
            .ToDictionary(va => BuildPairKey(va.VulnerabilityId, va.AssetId));

        foreach (var episode in openEpisodes)
        {
            var pairKey = BuildPairKey(episode.VulnerabilityId, episode.AssetId);
            if (seenPairKeys.Contains(pairKey))
            {
                continue;
            }

            episode.MarkMissing();
            if (episode.MissingSyncCount < 2)
            {
                continue;
            }

            episode.Resolve(now);

            if (
                currentProjections.TryGetValue(pairKey, out var projection)
                && projection.Status == VulnerabilityStatus.Open
            )
            {
                projection.Resolve(now);
                await CloseOpenRemediationTasksAsync(episode.VulnerabilityId, episode.AssetId, ct);
            }
        }
    }

    private async Task UpdateSourceVulnerabilityStatusesAsync(
        Guid tenantId,
        string sourceName,
        CancellationToken ct
    )
    {
        var vulnerabilities = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId && v.Source.Contains(sourceName))
            .ToListAsync(ct);

        if (vulnerabilities.Count == 0)
        {
            return;
        }

        var vulnerabilityIds = vulnerabilities.Select(v => v.Id).ToList();
        var openVulnerabilityIds = await _dbContext
            .VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .Where(episode =>
                episode.TenantId == tenantId
                && episode.Status == VulnerabilityStatus.Open
                && vulnerabilityIds.Contains(episode.VulnerabilityId)
            )
            .Select(episode => episode.VulnerabilityId)
            .Distinct()
            .ToListAsync(ct);

        var openVulnerabilitySet = openVulnerabilityIds.ToHashSet();

        foreach (var vulnerability in vulnerabilities)
        {
            vulnerability.UpdateStatus(
                openVulnerabilitySet.Contains(vulnerability.Id)
                    ? VulnerabilityStatus.Open
                    : VulnerabilityStatus.Resolved
            );
        }
    }

    private async Task EnsureOpenRemediationTaskAsync(
        Guid tenantId,
        Vulnerability vulnerability,
        Asset asset,
        CancellationToken ct
    )
    {
        var existingTask = await _dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                task =>
                    task.TenantId == tenantId
                    && task.VulnerabilityId == vulnerability.Id
                    && task.AssetId == asset.Id
                    && task.Status != RemediationTaskStatus.Completed,
                ct
            );

        if (existingTask is not null)
        {
            return;
        }

        var assigneeId = ResolveAssignee(asset);
        if (!assigneeId.HasValue)
        {
            return;
        }

        var tenantSla = await _dbContext
            .TenantSlaConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(config => config.TenantId == tenantId, ct);

        var task = RemediationTask.Create(
            vulnerability.Id,
            asset.Id,
            tenantId,
            assigneeId.Value,
            SystemUserId,
            _slaService.CalculateDueDate(
                vulnerability.VendorSeverity,
                DateTimeOffset.UtcNow,
                tenantSla
            )
        );

        await _dbContext.RemediationTasks.AddAsync(task, ct);
    }

    private async Task CloseOpenRemediationTasksAsync(
        Guid vulnerabilityId,
        Guid assetId,
        CancellationToken ct
    )
    {
        var openTasks = await _dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .Where(task =>
                task.VulnerabilityId == vulnerabilityId
                && task.AssetId == assetId
                && task.Status != RemediationTaskStatus.Completed
            )
            .ToListAsync(ct);

        foreach (var task in openTasks)
        {
            task.UpdateStatus(
                RemediationTaskStatus.Completed,
                "Auto-closed: vulnerability resolved in source"
            );
        }
    }

    private static string BuildPairKey(Guid vulnerabilityId, Guid assetId)
    {
        return $"{vulnerabilityId:N}:{assetId:N}";
    }

    internal async Task ProcessAssetsAsync(
        Guid tenantId,
        IngestionAssetInventorySnapshot snapshot,
        CancellationToken ct
    )
    {
        var assetIdsByExternalId = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var asset in snapshot.Assets)
        {
            var existing = await _dbContext
                .Assets.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    current =>
                        current.ExternalId == asset.ExternalId && current.TenantId == tenantId,
                    ct
                );

            if (existing is null)
            {
                existing = Asset.Create(
                    tenantId,
                    asset.ExternalId,
                    asset.AssetType,
                    asset.Name,
                    Criticality.Medium,
                    asset.Description
                );
                if (asset.AssetType == AssetType.Device)
                {
                    existing.UpdateDeviceDetails(
                        asset.DeviceComputerDnsName,
                        asset.DeviceHealthStatus,
                        asset.DeviceOsPlatform,
                        asset.DeviceOsVersion,
                        asset.DeviceRiskScore,
                        asset.DeviceLastSeenAt,
                        asset.DeviceLastIpAddress,
                        asset.DeviceAadDeviceId
                    );
                }
                existing.UpdateMetadata(asset.Metadata);
                await _dbContext.Assets.AddAsync(existing, ct);
                assetIdsByExternalId[asset.ExternalId] = existing.Id;
                continue;
            }

            existing.UpdateDetails(asset.Name, asset.Description);
            if (asset.AssetType == AssetType.Device)
            {
                existing.UpdateDeviceDetails(
                    asset.DeviceComputerDnsName,
                    asset.DeviceHealthStatus,
                    asset.DeviceOsPlatform,
                    asset.DeviceOsVersion,
                    asset.DeviceRiskScore,
                    asset.DeviceLastSeenAt,
                    asset.DeviceLastIpAddress,
                    asset.DeviceAadDeviceId
                );
            }
            existing.UpdateMetadata(asset.Metadata);
            assetIdsByExternalId[asset.ExternalId] = existing.Id;
        }

        await ProcessDeviceSoftwareLinksAsync(
            tenantId,
            snapshot.DeviceSoftwareLinks,
            assetIdsByExternalId,
            ct
        );

        await _dbContext.SaveChangesAsync(ct);
    }

    private async Task ProcessDeviceSoftwareLinksAsync(
        Guid tenantId,
        IReadOnlyList<IngestionDeviceSoftwareLink> links,
        Dictionary<string, Guid> assetIdsByExternalId,
        CancellationToken ct
    )
    {
        if (links.Count == 0)
        {
            return;
        }

        var persistedAssetIds = await _dbContext
            .Assets.IgnoreQueryFilters()
            .Where(asset => asset.TenantId == tenantId)
            .Select(asset => new { asset.Id, asset.ExternalId })
            .ToListAsync(ct);

        foreach (var asset in persistedAssetIds)
        {
            assetIdsByExternalId.TryAdd(asset.ExternalId, asset.Id);
        }

        foreach (var link in links)
        {
            if (
                !assetIdsByExternalId.TryGetValue(link.DeviceExternalId, out var deviceAssetId)
                || !assetIdsByExternalId.TryGetValue(
                    link.SoftwareExternalId,
                    out var softwareAssetId
                )
            )
            {
                continue;
            }

            var installation = await _dbContext
                .DeviceSoftwareInstallations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    current =>
                        current.TenantId == tenantId
                        && current.DeviceAssetId == deviceAssetId
                        && current.SoftwareAssetId == softwareAssetId,
                    ct
                );

            var openEpisode = await _dbContext
                .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    current =>
                        current.TenantId == tenantId
                        && current.DeviceAssetId == deviceAssetId
                        && current.SoftwareAssetId == softwareAssetId
                        && current.RemovedAt == null,
                    ct
                );

            if (installation is null)
            {
                installation = DeviceSoftwareInstallation.Create(
                    tenantId,
                    deviceAssetId,
                    softwareAssetId,
                    link.ObservedAt
                );
                await _dbContext.DeviceSoftwareInstallations.AddAsync(installation, ct);
            }
            else
            {
                installation.Touch(link.ObservedAt);
            }

            if (openEpisode is null)
            {
                var latestEpisode = await _dbContext
                    .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
                    .Where(current =>
                        current.TenantId == tenantId
                        && current.DeviceAssetId == deviceAssetId
                        && current.SoftwareAssetId == softwareAssetId
                    )
                    .OrderByDescending(current => current.EpisodeNumber)
                    .FirstOrDefaultAsync(ct);

                openEpisode = DeviceSoftwareInstallationEpisode.Create(
                    tenantId,
                    deviceAssetId,
                    softwareAssetId,
                    (latestEpisode?.EpisodeNumber ?? 0) + 1,
                    link.ObservedAt
                );
                await _dbContext.DeviceSoftwareInstallationEpisodes.AddAsync(openEpisode, ct);
            }
            else
            {
                openEpisode.Seen(link.ObservedAt);
            }
        }

        var seenKeys = links
            .Select(link => $"{link.DeviceExternalId}:{link.SoftwareExternalId}")
            .ToHashSet(StringComparer.Ordinal);

        var currentInstallations = await _dbContext
            .DeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(current => current.TenantId == tenantId)
            .ToListAsync(ct);

        if (currentInstallations.Count == 0)
        {
            return;
        }

        var externalIdsByAssetId = assetIdsByExternalId.ToDictionary(
            pair => pair.Value,
            pair => pair.Key
        );

        foreach (var installation in currentInstallations)
        {
            if (
                !externalIdsByAssetId.TryGetValue(
                    installation.DeviceAssetId,
                    out var deviceExternalId
                )
                || !externalIdsByAssetId.TryGetValue(
                    installation.SoftwareAssetId,
                    out var softwareExternalId
                )
            )
            {
                continue;
            }

            if (seenKeys.Contains($"{deviceExternalId}:{softwareExternalId}"))
            {
                continue;
            }

            installation.MarkMissing();
            if (installation.MissingSyncCount < 2)
            {
                continue;
            }

            var openEpisode = await _dbContext
                .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    current =>
                        current.TenantId == tenantId
                        && current.DeviceAssetId == installation.DeviceAssetId
                        && current.SoftwareAssetId == installation.SoftwareAssetId
                        && current.RemovedAt == null,
                    ct
                );

            openEpisode?.MarkMissing();
            if (openEpisode is not null && openEpisode.MissingSyncCount >= 2)
            {
                openEpisode.Remove(DateTimeOffset.UtcNow);
            }

            _dbContext.DeviceSoftwareInstallations.Remove(installation);
        }
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
