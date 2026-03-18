using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Services;

public class DashboardQueryService(
    PatchHoundDbContext dbContext,
    IRiskChangeBriefAiSummaryService riskChangeBriefAiSummaryService,
    TenantSnapshotResolver snapshotResolver
)
{
    public record RecurrenceData(
        int RecurringVulnerabilityCount,
        decimal RecurrenceRatePercent,
        List<RecurringVulnerabilityDto> TopRecurringVulnerabilities,
        List<RecurringAssetDto> TopRecurringAssets
    );

    public async Task<RecurrenceData> GetRecurrenceDataAsync(Guid tenantId, CancellationToken ct)
    {
        var recurrenceRows = await dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(episode => episode.TenantId == tenantId)
            .GroupBy(episode => new { episode.TenantVulnerabilityId, episode.AssetId })
            .Select(group => new
            {
                VulnerabilityId = group.Key.TenantVulnerabilityId,
                group.Key.AssetId,
                EpisodeCount = group.Count(),
            })
            .ToListAsync(ct);

        var recurringPairs = recurrenceRows.Where(row => row.EpisodeCount > 1).ToList();
        var recurringVulnerabilityIds = recurringPairs
            .Select(row => row.VulnerabilityId)
            .Distinct()
            .ToList();

        var recurrenceRatePercent =
            recurrenceRows.Count == 0
                ? 0
                : Math.Round((decimal)recurringPairs.Count / recurrenceRows.Count * 100m, 1);

        var topRecurringVulnerabilityCounts = recurringPairs
            .GroupBy(row => row.VulnerabilityId)
            .Select(group => new
            {
                VulnerabilityId = group.Key,
                EpisodeCount = group.Sum(row => row.EpisodeCount),
                ReappearanceCount = group.Sum(row => row.EpisodeCount - 1),
            })
            .OrderByDescending(row => row.ReappearanceCount)
            .ThenByDescending(row => row.EpisodeCount)
            .Take(5)
            .ToList();

        var topRecurringVulnerabilityIds = topRecurringVulnerabilityCounts
            .Select(row => row.VulnerabilityId)
            .ToList();

        var recurringVulnerabilities = await dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v => v.TenantId == tenantId && topRecurringVulnerabilityIds.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                v.VulnerabilityDefinition.ExternalId,
                v.VulnerabilityDefinition.Title,
            })
            .ToDictionaryAsync(v => v.Id, ct);

        var topRecurringVulnerabilities = topRecurringVulnerabilityCounts
            .Select(row =>
            {
                var vulnerability = recurringVulnerabilities[row.VulnerabilityId];
                return new RecurringVulnerabilityDto(
                    vulnerability.Id,
                    vulnerability.ExternalId,
                    vulnerability.Title,
                    row.EpisodeCount,
                    row.ReappearanceCount
                );
            })
            .ToList();

        var topRecurringAssetCounts = recurringPairs
            .GroupBy(row => row.AssetId)
            .Select(group => new
            {
                AssetId = group.Key,
                RecurringVulnerabilityCount = group.Count(),
            })
            .OrderByDescending(row => row.RecurringVulnerabilityCount)
            .Take(5)
            .ToList();

        var recurringAssetIds = topRecurringAssetCounts.Select(row => row.AssetId).ToList();
        var recurringAssets = await dbContext
            .Assets.AsNoTracking()
            .Where(asset => asset.TenantId == tenantId && recurringAssetIds.Contains(asset.Id))
            .ToDictionaryAsync(asset => asset.Id, ct);

        var topRecurringAssets = topRecurringAssetCounts
            .Select(row =>
            {
                var asset = recurringAssets[row.AssetId];
                return new RecurringAssetDto(
                    asset.Id,
                    asset.AssetType == AssetType.Device
                        ? asset.DeviceComputerDnsName ?? asset.Name
                        : asset.Name,
                    asset.AssetType.ToString(),
                    row.RecurringVulnerabilityCount
                );
            })
            .ToList();

        return new RecurrenceData(
            recurringVulnerabilityIds.Count,
            recurrenceRatePercent,
            topRecurringVulnerabilities,
            topRecurringAssets
        );
    }

    public async Task<DashboardRiskChangeBriefDto> BuildRiskChangeBriefAsync(
        Guid tenantId,
        Guid currentTenantId,
        int? limit,
        bool highCriticalOnly,
        CancellationToken ct,
        int cutoffHours = 24
    )
    {
        var activeSnapshotId = await snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(tenantId, ct);
        var cutoff = DateTimeOffset.UtcNow.AddHours(-cutoffHours);

        var candidateRows = await dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v =>
                v.TenantId == tenantId
                && (v.CreatedAt >= cutoff || v.UpdatedAt >= cutoff)
            )
            .Select(v => new
            {
                v.Id,
                HasOpenEpisodes = dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open
                ),
                v.CreatedAt,
                v.UpdatedAt,
                v.VulnerabilityDefinition.ExternalId,
                v.VulnerabilityDefinition.Title,
                VendorSeverity = v.VulnerabilityDefinition.VendorSeverity,
                AdjustedSeverity = dbContext
                    .OrganizationalSeverities
                    .Where(os => os.TenantVulnerabilityId == v.Id)
                    .OrderByDescending(os => os.AdjustedAt)
                    .Select(os => (Severity?)os.AdjustedSeverity)
                    .FirstOrDefault(),
                AffectedAssetCount = dbContext.VulnerabilityAssets.Count(va =>
                    va.TenantVulnerabilityId == v.Id && va.SnapshotId == activeSnapshotId
                ),
            })
            .ToListAsync(ct);

        var appearedRows = candidateRows
            .Where(row =>
                row.HasOpenEpisodes
                && (row.CreatedAt >= cutoff || row.UpdatedAt >= cutoff)
            )
            .Select(row => new
            {
                Item = new DashboardRiskChangeItemDto(
                    row.Id,
                    row.ExternalId,
                    row.Title,
                    (row.AdjustedSeverity ?? row.VendorSeverity).ToString(),
                    row.AffectedAssetCount,
                    row.CreatedAt >= cutoff ? row.CreatedAt : row.UpdatedAt
                ),
                EffectiveSeverity = row.AdjustedSeverity ?? row.VendorSeverity,
            })
            .Where(row => !highCriticalOnly
                || row.EffectiveSeverity == Severity.High
                || row.EffectiveSeverity == Severity.Critical)
            .OrderByDescending(row => row.Item.ChangedAt)
            .ThenByDescending(row => row.Item.AffectedAssetCount)
            .ToList();

        var resolvedRows = candidateRows
            .Where(row => !row.HasOpenEpisodes && row.UpdatedAt >= cutoff)
            .Select(row => new
            {
                Item = new DashboardRiskChangeItemDto(
                    row.Id,
                    row.ExternalId,
                    row.Title,
                    (row.AdjustedSeverity ?? row.VendorSeverity).ToString(),
                    row.AffectedAssetCount,
                    row.UpdatedAt
                ),
                EffectiveSeverity = row.AdjustedSeverity ?? row.VendorSeverity,
            })
            .Where(row => !highCriticalOnly
                || row.EffectiveSeverity == Severity.High
                || row.EffectiveSeverity == Severity.Critical)
            .OrderByDescending(row => row.Item.ChangedAt)
            .ThenByDescending(row => row.Item.AffectedAssetCount)
            .ToList();

        var deterministicBrief = new DashboardRiskChangeBriefDto(
            appearedRows.Count,
            resolvedRows.Count,
            (limit.HasValue ? appearedRows.Take(limit.Value) : appearedRows)
                .Select(row => row.Item)
                .ToList(),
            (limit.HasValue ? resolvedRows.Take(limit.Value) : resolvedRows)
                .Select(row => row.Item)
                .ToList(),
            null
        );

        if (limit != 3)
        {
            return deterministicBrief;
        }

        try
        {
            var aiSummary = await riskChangeBriefAiSummaryService.GenerateAsync(
                currentTenantId,
                new RiskChangeBriefSummaryInput(
                    deterministicBrief.AppearedCount,
                    deterministicBrief.ResolvedCount,
                    deterministicBrief.Appeared
                        .Select(item => new RiskChangeBriefSummaryItemInput(
                            item.ExternalId,
                            item.Title,
                            item.Severity,
                            item.AffectedAssetCount,
                            item.ChangedAt
                        ))
                        .ToList(),
                    deterministicBrief.Resolved
                        .Select(item => new RiskChangeBriefSummaryItemInput(
                            item.ExternalId,
                            item.Title,
                            item.Severity,
                            item.AffectedAssetCount,
                            item.ChangedAt
                        ))
                        .ToList()
                ),
                ct
            );

            return deterministicBrief with { AiSummary = aiSummary };
        }
        catch
        {
            return deterministicBrief;
        }
    }
}
