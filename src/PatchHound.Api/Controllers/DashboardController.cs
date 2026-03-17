using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Policies.ViewVulnerabilities)]
public class DashboardController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly IRiskChangeBriefAiSummaryService _riskChangeBriefAiSummaryService;
    private readonly ITenantContext _tenantContext;

    public DashboardController(
        PatchHoundDbContext dbContext,
        IRiskChangeBriefAiSummaryService riskChangeBriefAiSummaryService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _riskChangeBriefAiSummaryService = riskChangeBriefAiSummaryService;
        _tenantContext = tenantContext;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }
        var activeSnapshotId = await ResolveActiveVulnerabilitySnapshotIdAsync(tenantId, ct);

        var riskChangeBrief = await BuildRiskChangeBriefAsync(
            tenantId,
            limit: 3,
            highCriticalOnly: true,
            ct
        );

        // Vulnerability counts by severity
        var bySeverity = await _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v =>
                v.TenantId == tenantId
                && _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open
                )
            )
            .GroupBy(v => v.VulnerabilityDefinition.VendorSeverity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var vulnsBySeverity = Enum.GetValues<Severity>()
            .ToDictionary(
                s => s.ToString(),
                s => bySeverity.FirstOrDefault(x => x.Severity == s)?.Count ?? 0
            );

        // Vulnerability counts by status (derived from episodes)
        var totalTenantVulnerabilities = await _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v => v.TenantId == tenantId)
            .CountAsync(ct);

        var openVulnerabilityCount = await _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v =>
                v.TenantId == tenantId
                && _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open
                )
            )
            .CountAsync(ct);

        var vulnsByStatus = new Dictionary<string, int>
        {
            [nameof(VulnerabilityStatus.Open)] = openVulnerabilityCount,
            [nameof(VulnerabilityStatus.Resolved)] = totalTenantVulnerabilities - openVulnerabilityCount,
        };

        // SLA compliance
        var tasks = await _dbContext
            .RemediationTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new
            {
                t.Status,
                t.DueDate,
                t.CreatedAt,
                t.UpdatedAt,
            })
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var taskTuples = tasks.Select(t => (t.Status, t.DueDate)).ToList();
        var (slaPercent, overdueCount) = DashboardService.CalculateSlaCompliance(taskTuples, now);

        // Average remediation days
        var completedTasks = tasks
            .Where(t => t.Status == RemediationTaskStatus.Completed)
            .Select(t => (t.CreatedAt, t.UpdatedAt))
            .ToList();
        var avgRemediationDays = DashboardService.CalculateAverageRemediationDays(completedTasks);

        // Exposure score: vulnerability severity × asset criticality for open vulnerability-asset pairs
        var vulnerabilityAssetPairs = await _dbContext
            .VulnerabilityAssets.AsNoTracking()
            .Where(va =>
                va.Status == VulnerabilityStatus.Open && va.SnapshotId == activeSnapshotId
            )
            .Join(
                _dbContext.TenantVulnerabilities.AsNoTracking(),
                va => va.TenantVulnerabilityId,
                v => v.Id,
                (va, v) => new { v.TenantId, v.VulnerabilityDefinition.VendorSeverity, va.AssetId }
            )
            .Where(x => x.TenantId == tenantId)
            .Join(
                _dbContext.Assets,
                x => x.AssetId,
                a => a.Id,
                (x, a) => new { x.VendorSeverity, a.Criticality }
            )
            .ToListAsync(ct);

        var exposurePairs = vulnerabilityAssetPairs
            .Select(p => (p.VendorSeverity, p.Criticality))
            .ToList();
        var exposureScore = DashboardService.CalculateExposureScore(exposurePairs);

        // Top 10 most urgent vulnerabilities (highest severity × oldest)
        var topVulns = await _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v =>
                v.TenantId == tenantId
                && _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open
                )
            )
            .OrderByDescending(v => v.VulnerabilityDefinition.VendorSeverity)
            .ThenBy(v => v.VulnerabilityDefinition.PublishedDate)
            .Take(10)
            .Select(v => new TopVulnerabilityDto(
                v.Id,
                v.VulnerabilityDefinition.ExternalId,
                v.VulnerabilityDefinition.Title,
                v.VulnerabilityDefinition.VendorSeverity.ToString(),
                v.VulnerabilityDefinition.CvssScore,
                _dbContext.VulnerabilityAssets.Count(va =>
                    va.TenantVulnerabilityId == v.Id
                    && va.TenantVulnerability.TenantId == tenantId
                    && va.SnapshotId == activeSnapshotId
                ),
                v.VulnerabilityDefinition.PublishedDate.HasValue
                    ? (int)(now - v.VulnerabilityDefinition.PublishedDate.Value).TotalDays
                    : 0
            ))
            .ToListAsync(ct);

        var recurrenceRows = await _dbContext
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

        var recurringVulnerabilities = await _dbContext
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
        var recurringAssets = await _dbContext
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

        return Ok(
            new DashboardSummaryDto(
                exposureScore,
                vulnsBySeverity,
                vulnsByStatus,
                slaPercent,
                overdueCount,
                tasks.Count,
                avgRemediationDays,
                topVulns,
                riskChangeBrief,
                recurringVulnerabilityIds.Count,
                recurrenceRatePercent,
                topRecurringVulnerabilities,
                topRecurringAssets
            )
        );
    }

    private async Task<Guid?> ResolveActiveVulnerabilitySnapshotIdAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        return await _dbContext
            .TenantSourceConfigurations.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId && item.SourceKey == TenantSourceCatalog.DefenderSourceKey
            )
            .Select(item => item.ActiveSnapshotId)
            .FirstOrDefaultAsync(ct);
    }

    [HttpGet("risk-changes")]
    public async Task<ActionResult<DashboardRiskChangeBriefDto>> GetRiskChanges(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        return Ok(await BuildRiskChangeBriefAsync(tenantId, limit: null, highCriticalOnly: false, ct));
    }

    [HttpGet("trends")]
    public async Task<ActionResult<TrendDataDto>> GetTrends(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-89);

        var episodeRows = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Join(
                _dbContext.TenantVulnerabilities.AsNoTracking(),
                episode => episode.TenantVulnerabilityId,
                vulnerability => vulnerability.Id,
                (episode, vulnerability) =>
                    new
                    {
                        vulnerability.TenantId,
                        VulnerabilityId = episode.TenantVulnerabilityId,
                        vulnerability.VulnerabilityDefinition.VendorSeverity,
                        episode.FirstSeenAt,
                        episode.ResolvedAt,
                    }
            )
            .Where(row =>
                row.TenantId == tenantId
                && DateOnly.FromDateTime(row.FirstSeenAt.UtcDateTime) <= today
                && DateOnly.FromDateTime((row.ResolvedAt ?? DateTimeOffset.UtcNow).UtcDateTime)
                    >= startDate
            )
            .ToListAsync(ct);

        var counts = new Dictionary<(DateOnly Date, Severity Severity), HashSet<Guid>>();

        foreach (var row in episodeRows)
        {
            var firstSeenDate = DateOnly.FromDateTime(row.FirstSeenAt.UtcDateTime);
            var resolvedDate = row.ResolvedAt.HasValue
                ? DateOnly.FromDateTime(row.ResolvedAt.Value.UtcDateTime).AddDays(-1)
                : today;
            var effectiveStart = firstSeenDate < startDate ? startDate : firstSeenDate;
            var effectiveEnd = resolvedDate > today ? today : resolvedDate;

            if (effectiveEnd < effectiveStart)
            {
                continue;
            }

            for (var date = effectiveStart; date <= effectiveEnd; date = date.AddDays(1))
            {
                var key = (date, row.VendorSeverity);
                if (!counts.TryGetValue(key, out var vulnerabilityIds))
                {
                    vulnerabilityIds = [];
                    counts[key] = vulnerabilityIds;
                }

                vulnerabilityIds.Add(row.VulnerabilityId);
            }
        }

        var items = new List<TrendItem>();
        foreach (var date in EachDay(startDate, today))
        {
            foreach (var severity in Enum.GetValues<Severity>())
            {
                counts.TryGetValue((date, severity), out var vulnerabilityIds);
                items.Add(new TrendItem(date, severity.ToString(), vulnerabilityIds?.Count ?? 0));
            }
        }

        return Ok(new TrendDataDto(items));
    }

    private static IEnumerable<DateOnly> EachDay(DateOnly startDate, DateOnly endDate)
    {
        for (var current = startDate; current <= endDate; current = current.AddDays(1))
        {
            yield return current;
        }
    }

    private async Task<DashboardRiskChangeBriefDto> BuildRiskChangeBriefAsync(
        Guid tenantId,
        int? limit,
        bool highCriticalOnly,
        CancellationToken ct
    )
    {
        var activeSnapshotId = await ResolveActiveVulnerabilitySnapshotIdAsync(tenantId, ct);
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);

        var candidateRows = await _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v =>
                v.TenantId == tenantId
                && (v.CreatedAt >= cutoff || v.UpdatedAt >= cutoff)
            )
            .Select(v => new
            {
                v.Id,
                HasOpenEpisodes = _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open
                ),
                v.CreatedAt,
                v.UpdatedAt,
                v.VulnerabilityDefinition.ExternalId,
                v.VulnerabilityDefinition.Title,
                VendorSeverity = v.VulnerabilityDefinition.VendorSeverity,
                AdjustedSeverity = _dbContext
                    .OrganizationalSeverities
                    .Where(os => os.TenantVulnerabilityId == v.Id)
                    .OrderByDescending(os => os.AdjustedAt)
                    .Select(os => (Severity?)os.AdjustedSeverity)
                    .FirstOrDefault(),
                AffectedAssetCount = _dbContext.VulnerabilityAssets.Count(va =>
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
            var aiSummary = await _riskChangeBriefAiSummaryService.GenerateAsync(
                _tenantContext.CurrentTenantId ?? Guid.Empty,
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
