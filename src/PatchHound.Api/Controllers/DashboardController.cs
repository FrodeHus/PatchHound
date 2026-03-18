using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Api.Services;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Policies.ViewVulnerabilities)]
public class DashboardController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly DashboardQueryService _dashboardQueryService;
    private readonly ITenantContext _tenantContext;
    private readonly TenantSnapshotResolver _snapshotResolver;

    public DashboardController(
        PatchHoundDbContext dbContext,
        DashboardQueryService dashboardQueryService,
        ITenantContext tenantContext,
        TenantSnapshotResolver snapshotResolver
    )
    {
        _dbContext = dbContext;
        _dashboardQueryService = dashboardQueryService;
        _tenantContext = tenantContext;
        _snapshotResolver = snapshotResolver;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(
        [FromQuery] DashboardFilterQuery filter,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }
        var activeSnapshotId = await _snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(tenantId, ct);

        // Build filtered asset ID set based on platform/device group filters
        HashSet<Guid>? filteredAssetIds = null;
        if (!string.IsNullOrEmpty(filter.Platform) || !string.IsNullOrEmpty(filter.DeviceGroup))
        {
            var assetQuery = _dbContext.Assets.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device);

            if (!string.IsNullOrEmpty(filter.Platform))
            {
                assetQuery = assetQuery.Where(a => a.DeviceOsPlatform == filter.Platform);
            }

            if (!string.IsNullOrEmpty(filter.DeviceGroup))
            {
                assetQuery = assetQuery.Where(a => a.DeviceGroupName == filter.DeviceGroup);
            }

            filteredAssetIds = (await assetQuery.Select(a => a.Id).ToListAsync(ct)).ToHashSet();
        }

        // Compute minPublishedDate from MinAgeDays
        DateTimeOffset? minPublishedDate = filter.MinAgeDays.HasValue
            ? DateTimeOffset.UtcNow.AddDays(-filter.MinAgeDays.Value)
            : null;

        var riskChangeBrief = await _dashboardQueryService.BuildRiskChangeBriefAsync(
            tenantId,
            _tenantContext.CurrentTenantId ?? Guid.Empty,
            limit: 3,
            highCriticalOnly: true,
            ct
        );

        // Vulnerability counts by severity
        var bySeverityQuery = _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v =>
                v.TenantId == tenantId
                && _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open
                    && (filteredAssetIds == null || filteredAssetIds.Contains(e.AssetId))
                )
            );

        if (minPublishedDate.HasValue)
        {
            bySeverityQuery = bySeverityQuery.Where(v =>
                v.VulnerabilityDefinition.PublishedDate <= minPublishedDate.Value
            );
        }

        var bySeverity = await bySeverityQuery
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

        var openVulnQuery = _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v =>
                v.TenantId == tenantId
                && _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open
                    && (filteredAssetIds == null || filteredAssetIds.Contains(e.AssetId))
                )
            );

        if (minPublishedDate.HasValue)
        {
            openVulnQuery = openVulnQuery.Where(v =>
                v.VulnerabilityDefinition.PublishedDate <= minPublishedDate.Value
            );
        }

        var openVulnerabilityCount = await openVulnQuery.CountAsync(ct);

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
        var vulnerabilityAssetPairsQuery = _dbContext
            .VulnerabilityAssets.AsNoTracking()
            .Where(va =>
                va.Status == VulnerabilityStatus.Open && va.SnapshotId == activeSnapshotId
                && (filteredAssetIds == null || filteredAssetIds.Contains(va.AssetId))
            )
            .Join(
                _dbContext.TenantVulnerabilities.AsNoTracking(),
                va => va.TenantVulnerabilityId,
                v => v.Id,
                (va, v) => new { v.TenantId, v.VulnerabilityDefinitionId, v.VulnerabilityDefinition.VendorSeverity, v.VulnerabilityDefinition.PublishedDate, va.AssetId }
            )
            .Where(x => x.TenantId == tenantId);

        if (minPublishedDate.HasValue)
        {
            vulnerabilityAssetPairsQuery = vulnerabilityAssetPairsQuery.Where(x =>
                x.PublishedDate <= minPublishedDate.Value
            );
        }

        var vulnerabilityAssetPairs = await vulnerabilityAssetPairsQuery
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
        var topVulnsQuery = _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v =>
                v.TenantId == tenantId
                && _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open
                    && (filteredAssetIds == null || filteredAssetIds.Contains(e.AssetId))
                )
            );

        if (minPublishedDate.HasValue)
        {
            topVulnsQuery = topVulnsQuery.Where(v =>
                v.VulnerabilityDefinition.PublishedDate <= minPublishedDate.Value
            );
        }

        var topVulns = await topVulnsQuery
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

        var recurrence = await _dashboardQueryService.GetRecurrenceDataAsync(tenantId, ct);

        // Vulnerabilities by device group — top 10 by total count descending
        var deviceGroupRows = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(e => e.Status == VulnerabilityStatus.Open && e.TenantVulnerability.TenantId == tenantId)
            .Join(
                _dbContext.Assets.AsNoTracking(),
                e => e.AssetId,
                a => a.Id,
                (e, a) => new { e.TenantVulnerabilityId, a.DeviceGroupName, e.TenantVulnerability.VulnerabilityDefinition.VendorSeverity }
            )
            .GroupBy(x => new { GroupName = x.DeviceGroupName ?? "Ungrouped", x.VendorSeverity })
            .Select(g => new { g.Key.GroupName, g.Key.VendorSeverity, Count = g.Select(x => x.TenantVulnerabilityId).Distinct().Count() })
            .ToListAsync(ct);

        var vulnsByDeviceGroup = deviceGroupRows
            .GroupBy(r => r.GroupName)
            .Select(g => new
            {
                GroupName = g.Key,
                Critical = g.Where(x => x.VendorSeverity == Severity.Critical).Sum(x => x.Count),
                High = g.Where(x => x.VendorSeverity == Severity.High).Sum(x => x.Count),
                Medium = g.Where(x => x.VendorSeverity == Severity.Medium).Sum(x => x.Count),
                Low = g.Where(x => x.VendorSeverity == Severity.Low).Sum(x => x.Count),
            })
            .OrderByDescending(g => g.Critical + g.High + g.Medium + g.Low)
            .Take(10)
            .Select(g => new DeviceGroupVulnerabilityDto(g.GroupName, g.Critical, g.High, g.Medium, g.Low))
            .ToList();

        // Device health breakdown — NOT affected by vulnerability filters
        var deviceHealthRows = await _dbContext
            .Assets.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device && a.DeviceHealthStatus != null)
            .GroupBy(a => a.DeviceHealthStatus!)
            .Select(g => new { HealthStatus = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var deviceHealthBreakdown = deviceHealthRows.ToDictionary(r => r.HealthStatus, r => r.Count);

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
                recurrence.RecurringVulnerabilityCount,
                recurrence.RecurrenceRatePercent,
                recurrence.TopRecurringVulnerabilities,
                recurrence.TopRecurringAssets,
                vulnsByDeviceGroup,
                deviceHealthBreakdown
            )
        );
    }

    [HttpGet("risk-changes")]
    public async Task<ActionResult<DashboardRiskChangeBriefDto>> GetRiskChanges(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        return Ok(await _dashboardQueryService.BuildRiskChangeBriefAsync(tenantId, _tenantContext.CurrentTenantId ?? Guid.Empty, limit: null, highCriticalOnly: false, ct));
    }

    [HttpGet("trends")]
    public async Task<ActionResult<TrendDataDto>> GetTrends(
        [FromQuery] DashboardFilterQuery filter,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        // Build filtered asset ID set based on platform/device group filters
        HashSet<Guid>? filteredAssetIds = null;
        if (!string.IsNullOrEmpty(filter.Platform) || !string.IsNullOrEmpty(filter.DeviceGroup))
        {
            var assetQuery = _dbContext.Assets.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device);

            if (!string.IsNullOrEmpty(filter.Platform))
            {
                assetQuery = assetQuery.Where(a => a.DeviceOsPlatform == filter.Platform);
            }

            if (!string.IsNullOrEmpty(filter.DeviceGroup))
            {
                assetQuery = assetQuery.Where(a => a.DeviceGroupName == filter.DeviceGroup);
            }

            filteredAssetIds = (await assetQuery.Select(a => a.Id).ToListAsync(ct)).ToHashSet();
        }

        // Compute minPublishedDate from MinAgeDays
        DateTimeOffset? minPublishedDate = filter.MinAgeDays.HasValue
            ? DateTimeOffset.UtcNow.AddDays(-filter.MinAgeDays.Value)
            : null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-89);

        var episodeQuery = _dbContext
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
                        vulnerability.VulnerabilityDefinition.PublishedDate,
                        episode.AssetId,
                        episode.FirstSeenAt,
                        episode.ResolvedAt,
                    }
            )
            .Where(row =>
                row.TenantId == tenantId
                && DateOnly.FromDateTime(row.FirstSeenAt.UtcDateTime) <= today
                && DateOnly.FromDateTime((row.ResolvedAt ?? DateTimeOffset.UtcNow).UtcDateTime)
                    >= startDate
            );

        if (minPublishedDate.HasValue)
        {
            episodeQuery = episodeQuery.Where(row => row.PublishedDate <= minPublishedDate.Value);
        }

        if (filteredAssetIds != null)
        {
            episodeQuery = episodeQuery.Where(row => filteredAssetIds.Contains(row.AssetId));
        }

        var episodeRows = await episodeQuery.ToListAsync(ct);

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

    [HttpGet("filter-options")]
    public async Task<ActionResult<DashboardFilterOptionsDto>> GetFilterOptions(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var platforms = await _dbContext.Assets.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device && a.DeviceOsPlatform != null)
            .Select(a => a.DeviceOsPlatform!)
            .Distinct()
            .OrderBy(p => p)
            .ToArrayAsync(ct);

        var deviceGroups = await _dbContext.Assets.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device && a.DeviceGroupName != null)
            .Select(a => a.DeviceGroupName!)
            .Distinct()
            .OrderBy(g => g)
            .ToArrayAsync(ct);

        return Ok(new DashboardFilterOptionsDto(platforms, deviceGroups));
    }

    private static IEnumerable<DateOnly> EachDay(DateOnly startDate, DateOnly endDate)
    {
        for (var current = startDate; current <= endDate; current = current.AddDays(1))
        {
            yield return current;
        }
    }

}
