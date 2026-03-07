using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class IngestionService
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly IEnumerable<IVulnerabilitySource> _sources;
    private readonly ILogger<IngestionService> _logger;

    // System user ID used as "assignedBy" for auto-created tasks
    private static readonly Guid SystemUserId = Guid.Empty;

    public IngestionService(
        PatchHoundDbContext dbContext,
        IEnumerable<IVulnerabilitySource> sources,
        ILogger<IngestionService> logger
    )
    {
        _dbContext = dbContext;
        _sources = sources;
        _logger = logger;
    }

    public async Task RunIngestionAsync(Guid tenantId, CancellationToken ct)
    {
        await RunIngestionAsync(tenantId, null, ct);
    }

    public async Task RunIngestionAsync(Guid tenantId, string? sourceKey, CancellationToken ct)
    {
        var sources = string.IsNullOrWhiteSpace(sourceKey)
            ? _sources
            : _sources.Where(source =>
                string.Equals(source.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));

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

                var results = await source.FetchVulnerabilitiesAsync(tenantId, ct);
                await ProcessResultsAsync(tenantId, source.SourceName, results, ct);

                if (source is IAssetInventorySource assetInventorySource)
                {
                    var assets = await assetInventorySource.FetchAssetsAsync(tenantId, ct);
                    await ProcessAssetsAsync(tenantId, assets, ct);
                }

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
        Action<PersistedIngestionRuntimeState> update,
        CancellationToken ct
    )
    {
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return;
        }

        var sources = TenantSourceSettings.ReadSources(tenant.Settings);
        var source = sources.FirstOrDefault(item =>
            string.Equals(item.Key, sourceKey, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return;
        }

        source.Runtime ??= new PersistedIngestionRuntimeState();
        update(source.Runtime);
        tenant.UpdateSettings(TenantSourceSettings.WriteSources(tenant.Settings, sources));
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

        // Track which external IDs were seen in this ingestion run
        var seenExternalIds = new HashSet<string>();
        // Track assets created in this batch to avoid duplicate inserts
        var pendingAssets = new Dictionary<string, Asset>();

        foreach (var result in results)
        {
            seenExternalIds.Add(result.ExternalId);
            await UpsertVulnerabilityAsync(tenantId, sourceName, result, pendingAssets, ct);
        }

        // Resolve vulnerabilities from this source that were not in the results
        await ResolveAbsentVulnerabilitiesAsync(tenantId, sourceName, seenExternalIds, ct);

        await _dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task UpsertVulnerabilityAsync(
        Guid tenantId,
        string sourceName,
        IngestionResult result,
        Dictionary<string, Asset> pendingAssets,
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
                sourceName,
                result.CvssScore,
                result.CvssVector,
                result.PublishedDate
            );

            await _dbContext.Vulnerabilities.AddAsync(existing, ct);
        }
        else
        {
            existing!.Update(
                result.Title,
                result.Description,
                result.VendorSeverity,
                result.CvssScore,
                result.CvssVector,
                result.PublishedDate
            );
        }

        // Process affected assets
        foreach (var affectedAsset in result.AffectedAssets)
        {
            await UpsertVulnerabilityAssetAsync(
                tenantId,
                existing!,
                affectedAsset,
                isNew,
                pendingAssets,
                ct
            );
        }
    }

    private async Task UpsertVulnerabilityAssetAsync(
        Guid tenantId,
        Vulnerability vulnerability,
        IngestionAffectedAsset affectedAsset,
        bool isNewVulnerability,
        Dictionary<string, Asset> pendingAssets,
        CancellationToken ct
    )
    {
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

        // Check if VulnerabilityAsset already exists
        var existingVa = await _dbContext
            .VulnerabilityAssets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                va => va.VulnerabilityId == vulnerability.Id && va.AssetId == asset.Id,
                ct
            );

        if (existingVa is not null)
            return;

        // Create VulnerabilityAsset
        var vulnerabilityAsset = VulnerabilityAsset.Create(
            vulnerability.Id,
            asset.Id,
            DateTimeOffset.UtcNow
        );

        await _dbContext.VulnerabilityAssets.AddAsync(vulnerabilityAsset, ct);

        // Auto-create RemediationTask for new vulnerabilities
        if (isNewVulnerability)
        {
            var assigneeId = ResolveAssignee(asset);
            if (assigneeId.HasValue)
            {
                var dueDate = CalculateDefaultDueDate(vulnerability.VendorSeverity);
                var task = RemediationTask.Create(
                    vulnerability.Id,
                    asset.Id,
                    tenantId,
                    assigneeId.Value,
                    SystemUserId,
                    dueDate
                );

                await _dbContext.RemediationTasks.AddAsync(task, ct);
            }
        }
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

    private static DateTimeOffset CalculateDefaultDueDate(Severity severity)
    {
        var days = severity switch
        {
            Severity.Critical => 7,
            Severity.High => 30,
            Severity.Medium => 90,
            Severity.Low => 180,
            _ => 90,
        };
        return DateTimeOffset.UtcNow.AddDays(days);
    }

    private async Task ResolveAbsentVulnerabilitiesAsync(
        Guid tenantId,
        string sourceName,
        HashSet<string> seenExternalIds,
        CancellationToken ct
    )
    {
        // Find open vulnerabilities from this source that were not returned
        var openVulnerabilities = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .Where(v =>
                v.TenantId == tenantId
                && v.Source == sourceName
                && v.Status == VulnerabilityStatus.Open
            )
            .ToListAsync(ct);

        var absentVulns = openVulnerabilities
            .Where(v => !seenExternalIds.Contains(v.ExternalId))
            .ToList();

        foreach (var vuln in absentVulns)
        {
            vuln.UpdateStatus(VulnerabilityStatus.Resolved);

            // Resolve all VulnerabilityAsset entries
            var vulnAssets = await _dbContext
                .VulnerabilityAssets.IgnoreQueryFilters()
                .Where(va => va.VulnerabilityId == vuln.Id && va.Status == VulnerabilityStatus.Open)
                .ToListAsync(ct);

            foreach (var va in vulnAssets)
            {
                va.Resolve(DateTimeOffset.UtcNow);
            }

            // Auto-close open remediation tasks
            var openTasks = await _dbContext
                .RemediationTasks.IgnoreQueryFilters()
                .Where(t =>
                    t.VulnerabilityId == vuln.Id && t.Status != RemediationTaskStatus.Completed
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
    }

    internal async Task ProcessAssetsAsync(
        Guid tenantId,
        IReadOnlyList<IngestionAsset> assets,
        CancellationToken ct
    )
    {
        foreach (var asset in assets)
        {
            var existing = await _dbContext
                .Assets.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    current => current.ExternalId == asset.ExternalId && current.TenantId == tenantId,
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
                existing.UpdateMetadata(asset.Metadata);
                await _dbContext.Assets.AddAsync(existing, ct);
                continue;
            }

            existing.UpdateDetails(asset.Name, asset.Description);
            existing.UpdateMetadata(asset.Metadata);
        }

        await _dbContext.SaveChangesAsync(ct);
    }
}
