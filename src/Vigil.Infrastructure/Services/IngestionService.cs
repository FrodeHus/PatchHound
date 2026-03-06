using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Core.Models;
using Vigil.Infrastructure.Data;

namespace Vigil.Infrastructure.Services;

public class IngestionService
{
    private readonly VigilDbContext _dbContext;
    private readonly IEnumerable<IVulnerabilitySource> _sources;
    private readonly ILogger<IngestionService> _logger;

    // System user ID used as "assignedBy" for auto-created tasks
    private static readonly Guid SystemUserId = Guid.Empty;

    public IngestionService(
        VigilDbContext dbContext,
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
        foreach (var source in _sources)
        {
            try
            {
                _logger.LogInformation(
                    "Starting ingestion from {Source} for tenant {TenantId}",
                    source.SourceName,
                    tenantId
                );

                var results = await source.FetchVulnerabilitiesAsync(tenantId, ct);
                await ProcessResultsAsync(tenantId, source.SourceName, results, ct);

                _logger.LogInformation(
                    "Completed ingestion from {Source} for tenant {TenantId}: {Count} vulnerabilities",
                    source.SourceName,
                    tenantId,
                    results.Count
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
            }
        }
    }

    internal async Task ProcessResultsAsync(
        Guid tenantId,
        string sourceName,
        IReadOnlyList<IngestionResult> results,
        CancellationToken ct
    )
    {
        // Track which external IDs were seen in this ingestion run
        var seenExternalIds = new HashSet<string>();

        foreach (var result in results)
        {
            seenExternalIds.Add(result.ExternalId);
            await UpsertVulnerabilityAsync(tenantId, sourceName, result, ct);
        }

        // Resolve vulnerabilities from this source that were not in the results
        await ResolveAbsentVulnerabilitiesAsync(tenantId, sourceName, seenExternalIds, ct);

        await _dbContext.SaveChangesAsync(ct);
    }

    private async Task UpsertVulnerabilityAsync(
        Guid tenantId,
        string sourceName,
        IngestionResult result,
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
            await UpsertVulnerabilityAssetAsync(tenantId, existing!, affectedAsset, isNew, ct);
        }
    }

    private async Task UpsertVulnerabilityAssetAsync(
        Guid tenantId,
        Vulnerability vulnerability,
        IngestionAffectedAsset affectedAsset,
        bool isNewVulnerability,
        CancellationToken ct
    )
    {
        // Upsert the Asset
        var asset = await _dbContext
            .Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                a => a.ExternalId == affectedAsset.ExternalAssetId && a.TenantId == tenantId,
                ct
            );

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
}
