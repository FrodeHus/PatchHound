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

        var (filteredAssetIds, minPublishedDate) = BuildFilterContext(tenantId, filter);

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
        var totalTenantVulnQuery = _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v =>
                v.TenantId == tenantId
                && _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && (filteredAssetIds == null || filteredAssetIds.Contains(e.AssetId))
                )
            );

        if (minPublishedDate.HasValue)
        {
            totalTenantVulnQuery = totalTenantVulnQuery.Where(v =>
                v.VulnerabilityDefinition.PublishedDate <= minPublishedDate.Value
            );
        }

        var totalTenantVulnerabilities = await totalTenantVulnQuery.CountAsync(ct);

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

        // SLA compliance and remediation metrics — tenant-wide, NOT affected by dashboard filters
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
        var deviceGroupQuery = _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(e =>
                e.Status == VulnerabilityStatus.Open
                && e.TenantVulnerability.TenantId == tenantId
                && (filteredAssetIds == null || filteredAssetIds.Contains(e.AssetId))
            );

        if (minPublishedDate.HasValue)
        {
            deviceGroupQuery = deviceGroupQuery.Where(e =>
                e.TenantVulnerability.VulnerabilityDefinition.PublishedDate <= minPublishedDate.Value
            );
        }

        var deviceGroupRows = await deviceGroupQuery
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

        // Device onboarding status breakdown — NOT affected by vulnerability filters
        var deviceOnboardingRows = await _dbContext
            .Assets.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device && a.DeviceOnboardingStatus != null)
            .GroupBy(a => a.DeviceOnboardingStatus!)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var deviceOnboardingBreakdown = deviceOnboardingRows.ToDictionary(r => r.Status, r => r.Count);

        // ────────────────────────────────────────────────────
        // SLA compliance trend — 30 daily data points
        // ────────────────────────────────────────────────────
        var slaComplianceTrend = new List<SlaComplianceTrendPointDto>();
        var todayDate = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var dayOffset = 29; dayOffset >= 0; dayOffset--)
        {
            var snapshotDate = todayDate.AddDays(-dayOffset);
            var snapshotInstant = new DateTimeOffset(snapshotDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            // Tasks that existed on this date: created on or before snapshotInstant
            var tasksOnDate = tasks.Where(t => t.CreatedAt <= snapshotInstant).ToList();
            if (tasksOnDate.Count == 0)
            {
                slaComplianceTrend.Add(new SlaComplianceTrendPointDto(snapshotDate, 100m));
                continue;
            }
            var taskTuplesOnDate = tasksOnDate.Select(t => (t.Status, t.DueDate)).ToList();
            var (dailySlaPercent, _) = DashboardService.CalculateSlaCompliance(taskTuplesOnDate, snapshotInstant);
            slaComplianceTrend.Add(new SlaComplianceTrendPointDto(snapshotDate, dailySlaPercent));
        }

        // ────────────────────────────────────────────────────
        // Metric sparklines — 30 daily data points for 4 metrics
        // ────────────────────────────────────────────────────
        var sparkCriticalBacklog = new List<int>();
        var sparkOverdueActions = new List<int>();
        var sparkHealthyTasks = new List<int>();
        var sparkOpenStatuses = new List<int>();

        // Pre-fetch open episode data for sparklines
        var sparkEpisodes = await _dbContext.VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(e => e.TenantVulnerability.TenantId == tenantId)
            .Select(e => new
            {
                e.TenantVulnerabilityId,
                e.FirstSeenAt,
                e.ResolvedAt,
                e.TenantVulnerability.VulnerabilityDefinition.VendorSeverity,
            })
            .ToListAsync(ct);

        for (var dayOffset = 29; dayOffset >= 0; dayOffset--)
        {
            var snapshotDate = todayDate.AddDays(-dayOffset);
            var snapshotInstant = new DateTimeOffset(snapshotDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

            // Critical backlog on this date
            var criticalOnDate = sparkEpisodes.Count(e =>
                e.VendorSeverity == Severity.Critical
                && DateOnly.FromDateTime(e.FirstSeenAt.UtcDateTime) <= snapshotDate
                && (e.ResolvedAt == null || DateOnly.FromDateTime(e.ResolvedAt.Value.UtcDateTime) > snapshotDate));
            sparkCriticalBacklog.Add(criticalOnDate);

            // Open episodes on this date (all severities, distinct by TenantVulnerabilityId)
            var openOnDate = sparkEpisodes
                .Where(e =>
                    DateOnly.FromDateTime(e.FirstSeenAt.UtcDateTime) <= snapshotDate
                    && (e.ResolvedAt == null || DateOnly.FromDateTime(e.ResolvedAt.Value.UtcDateTime) > snapshotDate))
                .Select(e => e.TenantVulnerabilityId)
                .Distinct()
                .Count();
            sparkOpenStatuses.Add(openOnDate);

            // Task-based sparklines
            var tasksOnDate = tasks.Where(t => t.CreatedAt <= snapshotInstant).ToList();
            var overdueOnDate = tasksOnDate.Count(t =>
                t.Status != RemediationTaskStatus.Completed
                && t.Status != RemediationTaskStatus.RiskAccepted
                && t.DueDate < snapshotInstant);
            sparkOverdueActions.Add(overdueOnDate);
            sparkHealthyTasks.Add(Math.Max(tasksOnDate.Count - overdueOnDate, 0));
        }

        var metricSparklines = new MetricSparklinesDto(
            sparkCriticalBacklog, sparkOverdueActions, sparkHealthyTasks, sparkOpenStatuses);

        // ────────────────────────────────────────────────────
        // Vulnerability age buckets — open vulnerabilities by dwell time
        // ────────────────────────────────────────────────────
        var ageRows = await _dbContext.TenantVulnerabilities.AsNoTracking()
            .Where(v =>
                v.TenantId == tenantId
                && _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open))
            .Select(v => new
            {
                v.VulnerabilityDefinition.VendorSeverity,
                v.VulnerabilityDefinition.PublishedDate,
            })
            .ToListAsync(ct);

        var ageBucketDefinitions = new (string Label, int MinDays, int MaxDays)[]
        {
            ("0-7 days", 0, 7),
            ("8-30 days", 8, 30),
            ("31-90 days", 31, 90),
            ("91-180 days", 91, 180),
            ("180+ days", 181, int.MaxValue),
        };

        var ageBuckets = ageBucketDefinitions.Select(bucket =>
        {
            var inBucket = ageRows.Where(r =>
            {
                if (!r.PublishedDate.HasValue) return bucket.Label == "180+ days";
                var days = (int)(now - r.PublishedDate.Value).TotalDays;
                return days >= bucket.MinDays && days <= bucket.MaxDays;
            }).ToList();

            return new VulnerabilityAgeBucketDto(
                bucket.Label,
                inBucket.Count,
                inBucket.Count(r => r.VendorSeverity == Severity.Critical),
                inBucket.Count(r => r.VendorSeverity == Severity.High),
                inBucket.Count(r => r.VendorSeverity == Severity.Medium),
                inBucket.Count(r => r.VendorSeverity == Severity.Low));
        }).ToList();

        // ────────────────────────────────────────────────────
        // MTTR by severity — current 30d vs prior 30d
        // ────────────────────────────────────────────────────
        var completedTasksWithSeverity = await _dbContext.RemediationTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Status == RemediationTaskStatus.Completed)
            .Join(
                _dbContext.TenantVulnerabilities.AsNoTracking(),
                t => t.TenantVulnerabilityId,
                v => v.Id,
                (t, v) => new
                {
                    v.VulnerabilityDefinition.VendorSeverity,
                    t.CreatedAt,
                    t.UpdatedAt,
                })
            .ToListAsync(ct);

        var currentPeriodStart = now.AddDays(-30);
        var priorPeriodStart = now.AddDays(-60);

        var mttrBySeverity = Enum.GetValues<Severity>().Select(severity =>
        {
            var currentPeriod = completedTasksWithSeverity
                .Where(t => t.VendorSeverity == severity && t.UpdatedAt >= currentPeriodStart)
                .ToList();
            var priorPeriod = completedTasksWithSeverity
                .Where(t => t.VendorSeverity == severity && t.UpdatedAt >= priorPeriodStart && t.UpdatedAt < currentPeriodStart)
                .ToList();

            var currentMttr = currentPeriod.Count > 0
                ? Math.Round((decimal)(currentPeriod.Sum(t => (t.UpdatedAt - t.CreatedAt).TotalDays) / currentPeriod.Count), 1)
                : 0m;
            decimal? priorMttr = priorPeriod.Count > 0
                ? Math.Round((decimal)(priorPeriod.Sum(t => (t.UpdatedAt - t.CreatedAt).TotalDays) / priorPeriod.Count), 1)
                : null;

            return new MttrBySeverityDto(severity.ToString(), currentMttr, priorMttr);
        }).ToList();

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
                deviceHealthBreakdown,
                deviceOnboardingBreakdown,
                slaComplianceTrend,
                metricSparklines,
                ageBuckets,
                mttrBySeverity
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

        var (filteredAssetIds, minPublishedDate) = BuildFilterContext(tenantId, filter);

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

    [HttpGet("burndown")]
    public async Task<ActionResult<BurndownTrendDto>> GetBurndown(
        [FromQuery] DashboardFilterQuery filter,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var (filteredAssetIds, minPublishedDate) = BuildFilterContext(tenantId, filter);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-89);

        // Fetch all episodes that overlap the 90-day window
        var episodeQuery = _dbContext.VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(e =>
                e.TenantVulnerability.TenantId == tenantId
                && DateOnly.FromDateTime(e.FirstSeenAt.UtcDateTime) <= today
                && (e.ResolvedAt == null || DateOnly.FromDateTime(e.ResolvedAt.Value.UtcDateTime) >= startDate));

        if (filteredAssetIds != null)
            episodeQuery = episodeQuery.Where(e => filteredAssetIds.Contains(e.AssetId));

        if (minPublishedDate.HasValue)
            episodeQuery = episodeQuery.Where(e =>
                e.TenantVulnerability.VulnerabilityDefinition.PublishedDate <= minPublishedDate.Value);

        var episodes = await episodeQuery
            .Select(e => new
            {
                e.TenantVulnerabilityId,
                e.FirstSeenAt,
                e.ResolvedAt,
            })
            .ToListAsync(ct);

        var items = new List<BurndownPointDto>();
        var runningNetOpen = 0;

        // Count episodes that were already open before the window
        var preWindowOpen = episodes.Count(e =>
            DateOnly.FromDateTime(e.FirstSeenAt.UtcDateTime) < startDate
            && (e.ResolvedAt == null || DateOnly.FromDateTime(e.ResolvedAt.Value.UtcDateTime) >= startDate));
        runningNetOpen = preWindowOpen;

        foreach (var date in EachDay(startDate, today))
        {
            var discovered = episodes.Count(e =>
                DateOnly.FromDateTime(e.FirstSeenAt.UtcDateTime) == date);
            var resolved = episodes.Count(e =>
                e.ResolvedAt.HasValue && DateOnly.FromDateTime(e.ResolvedAt.Value.UtcDateTime) == date);

            runningNetOpen += discovered - resolved;
            items.Add(new BurndownPointDto(date, discovered, resolved, runningNetOpen));
        }

        return Ok(new BurndownTrendDto(items));
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

    private (IQueryable<Guid>? FilteredAssetIdQuery, DateTimeOffset? MinPublishedDate) BuildFilterContext(
        Guid tenantId, DashboardFilterQuery filter)
    {
        IQueryable<Guid>? filteredAssetIdQuery = null;
        if (!string.IsNullOrEmpty(filter.Platform) || !string.IsNullOrEmpty(filter.DeviceGroup))
        {
            var assetQuery = _dbContext.Assets.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device);
            if (!string.IsNullOrEmpty(filter.Platform))
                assetQuery = assetQuery.Where(a => a.DeviceOsPlatform == filter.Platform);
            if (!string.IsNullOrEmpty(filter.DeviceGroup))
                assetQuery = assetQuery.Where(a => a.DeviceGroupName == filter.DeviceGroup);
            filteredAssetIdQuery = assetQuery.Select(a => a.Id);
        }

        DateTimeOffset? minPublishedDate = filter.MinAgeDays.HasValue
            ? DateTimeOffset.UtcNow.AddDays(-filter.MinAgeDays.Value)
            : null;

        return (filteredAssetIdQuery, minPublishedDate);
    }

    private static IEnumerable<DateOnly> EachDay(DateOnly startDate, DateOnly endDate)
    {
        for (var current = startDate; current <= endDate; current = current.AddDays(1))
        {
            yield return current;
        }
    }

}
