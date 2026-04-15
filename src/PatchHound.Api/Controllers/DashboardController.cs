using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
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
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(
        [FromQuery] DashboardFilterQuery filter,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }
        var (filteredAssetIds, minPublishedDate) = BuildFilterContext(tenantId, filter);

        var riskChangeBrief = await _dashboardQueryService.BuildRiskChangeBriefAsync(
            tenantId,
            _tenantContext.CurrentTenantId ?? Guid.Empty,
            limit: 3,
            highCriticalOnly: true,
            ct
        );

        var ageBucketDefinitions = new (string Label, int MinDays, int MaxDays)[]
        {
            ("0-7 days", 0, 7),
            ("8-30 days", 8, 30),
            ("31-90 days", 31, 90),
            ("91-180 days", 91, 180),
            ("180+ days", 181, int.MaxValue),
        };

        // Vulnerability counts from canonical DeviceVulnerabilityExposures
        var now = DateTimeOffset.UtcNow;
        var exposureBaseQuery = _dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == tenantId);
        if (filteredAssetIds != null)
            exposureBaseQuery = exposureBaseQuery.Where(e => filteredAssetIds.Contains(e.DeviceId));
        if (minPublishedDate.HasValue)
            exposureBaseQuery = exposureBaseQuery.Where(e => e.Vulnerability.PublishedDate >= minPublishedDate.Value);

        var exposureSeverityCounts = await exposureBaseQuery
            .GroupBy(e => new { e.Status, e.Vulnerability.VendorSeverity })
            .Select(g => new { g.Key.Status, g.Key.VendorSeverity, Count = g.Select(e => e.VulnerabilityId).Distinct().Count() })
            .ToListAsync(ct);

        var vulnsBySeverity = Enum.GetValues<Severity>().ToDictionary(
            s => s.ToString(),
            s => exposureSeverityCounts.Where(r => r.Status == ExposureStatus.Open && r.VendorSeverity == s).Sum(r => r.Count));
        var openCount = exposureSeverityCounts.Where(r => r.Status == ExposureStatus.Open).Sum(r => r.Count);
        var resolvedCount = exposureSeverityCounts.Where(r => r.Status == ExposureStatus.Resolved).Sum(r => r.Count);
        var vulnsByStatus = new Dictionary<string, int>
        {
            [nameof(VulnerabilityStatus.Open)] = openCount,
            [nameof(VulnerabilityStatus.Resolved)] = resolvedCount,
        };

        // Top critical vulnerabilities
        var topVulnRows = await exposureBaseQuery
            .Where(e => e.Status == ExposureStatus.Open
                && (e.Vulnerability.VendorSeverity == Severity.Critical || e.Vulnerability.VendorSeverity == Severity.High))
            .GroupBy(e => e.VulnerabilityId)
            .Select(g => new
            {
                VulnerabilityId = g.Key,
                ExternalId = g.First().Vulnerability.ExternalId,
                Title = g.First().Vulnerability.Title,
                VendorSeverity = g.First().Vulnerability.VendorSeverity,
                CvssScore = g.First().Vulnerability.CvssScore,
                PublishedDate = g.First().Vulnerability.PublishedDate,
                AffectedAssetCount = g.Select(e => e.DeviceId).Distinct().Count(),
            })
            .OrderByDescending(r => r.VendorSeverity)
            .ThenByDescending(r => r.AffectedAssetCount)
            .Take(10)
            .ToListAsync(ct);

        var topVulns = topVulnRows.Select(r => new TopVulnerabilityDto(
            r.VulnerabilityId, r.ExternalId, r.Title,
            r.VendorSeverity.ToString(), r.CvssScore,
            r.AffectedAssetCount,
            r.PublishedDate.HasValue ? (int)(now - r.PublishedDate.Value).TotalDays : 0
        )).ToList();

        // Latest unhandled (open, no remediation case)
        var latestUnhandledRows = await (
            from e in exposureBaseQuery
            where e.Status == ExposureStatus.Open
            join rc in _dbContext.RemediationCases.AsNoTracking()
                on e.SoftwareProductId equals rc.SoftwareProductId into rcJoin
            from rc in rcJoin.DefaultIfEmpty()
            where rc == null || rc.TenantId != tenantId
            group e by e.VulnerabilityId into g
            select new
            {
                VulnerabilityId = g.Key,
                ExternalId = g.First().Vulnerability.ExternalId,
                Title = g.First().Vulnerability.Title,
                VendorSeverity = g.First().Vulnerability.VendorSeverity,
                CvssScore = g.First().Vulnerability.CvssScore,
                PublishedDate = g.First().Vulnerability.PublishedDate,
                AffectedAssetCount = g.Select(e => e.DeviceId).Distinct().Count(),
                LatestSeenAt = g.Max(e => e.LastObservedAt),
            }
        ).OrderByDescending(r => r.LatestSeenAt).Take(10).ToListAsync(ct);

        var latestUnhandled = latestUnhandledRows.Select(r => new UnhandledVulnerabilityDto(
            r.VulnerabilityId, r.ExternalId, r.Title,
            r.VendorSeverity.ToString(), r.CvssScore,
            r.AffectedAssetCount,
            r.PublishedDate.HasValue ? (int)(now - r.PublishedDate.Value).TotalDays : 0,
            r.LatestSeenAt
        )).ToList();

        // MTTR: average days from first episode open to close for resolved exposures
        var closedEpisodes = await _dbContext.ExposureEpisodes.AsNoTracking()
            .Where(ep => ep.TenantId == tenantId && ep.ClosedAt != null
                && exposureBaseQuery.Select(e => e.Id).Contains(ep.DeviceVulnerabilityExposureId))
            .Select(ep => new
            {
                ep.FirstSeenAt,
                ep.ClosedAt,
                ep.Exposure.Vulnerability.VendorSeverity,
            })
            .ToListAsync(ct);

        var avgRemediationDays = closedEpisodes.Count > 0
            ? Math.Round((decimal)closedEpisodes.Average(ep => (ep.ClosedAt!.Value - ep.FirstSeenAt).TotalDays), 1)
            : 0m;
        var mttrBySeverity = Enum.GetValues<Severity>().Select(s =>
        {
            var episodes = closedEpisodes.Where(ep => ep.VendorSeverity == s).ToList();
            return new MttrBySeverityDto(
                s.ToString(),
                episodes.Count > 0 ? Math.Round((decimal)episodes.Average(ep => (ep.ClosedAt!.Value - ep.FirstSeenAt).TotalDays), 1) : 0m,
                null);
        }).ToList();

        // Age buckets for open exposures
        var openEpisodeAges = await _dbContext.ExposureEpisodes.AsNoTracking()
            .Where(ep => ep.TenantId == tenantId && ep.ClosedAt == null
                && exposureBaseQuery.Select(e => e.Id).Contains(ep.DeviceVulnerabilityExposureId))
            .Select(ep => new
            {
                AgeDays = (int)((now - ep.FirstSeenAt).TotalDays),
                ep.Exposure.Vulnerability.VendorSeverity,
            })
            .ToListAsync(ct);

        var ageBuckets = ageBucketDefinitions.Select(b =>
        {
            var inBucket = openEpisodeAges.Where(e =>
                e.AgeDays >= b.MinDays && (b.MaxDays == int.MaxValue || e.AgeDays <= b.MaxDays)).ToList();
            return new VulnerabilityAgeBucketDto(
                b.Label,
                inBucket.Count,
                inBucket.Count(e => e.VendorSeverity == Severity.Critical),
                inBucket.Count(e => e.VendorSeverity == Severity.High),
                inBucket.Count(e => e.VendorSeverity == Severity.Medium),
                inBucket.Count(e => e.VendorSeverity == Severity.Low)
            );
        }).ToList();

        var exposureScore = 0m;

        // SLA compliance and remediation metrics — tenant-wide, NOT affected by dashboard filters
        var patchingTasks = await _dbContext
            .PatchingTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new
            {
                t.Status,
                t.DueDate,
                t.CreatedAt,
                t.UpdatedAt,
            })
            .ToListAsync(ct);

        var overdueCount = patchingTasks.Count(t =>
            t.Status != PatchingTaskStatus.Completed && t.DueDate < now);
        var slaPercent = patchingTasks.Count == 0
            ? 100m
            : Math.Round(
                (patchingTasks.Count - overdueCount) / (decimal)patchingTasks.Count * 100m, 1);

        var recurrence = await _dashboardQueryService.GetRecurrenceDataAsync(tenantId, ct);

        // Vulnerability exposure by device group — derived from DeviceGroupRiskScores (canonical).
        var vulnsByDeviceGroup = await _dbContext.DeviceGroupRiskScores.AsNoTracking()
            .Where(score => score.TenantId == tenantId)
            .OrderByDescending(score => score.OverallScore)
            .ThenByDescending(score => score.OpenEpisodeCount)
            .Take(10)
            .Select(score => new DeviceGroupVulnerabilityDto(
                score.DeviceGroupName,
                score.CriticalEpisodeCount,
                score.HighEpisodeCount,
                score.MediumEpisodeCount,
                score.LowEpisodeCount,
                score.OverallScore,
                score.AssetCount,
                score.OpenEpisodeCount
            ))
            .ToListAsync(ct);

        // Device health breakdown — NOT affected by vulnerability filters
        var deviceHealthRows = await _dbContext
            .Devices.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.HealthStatus != null)
            .GroupBy(a => a.HealthStatus!)
            .Select(g => new { HealthStatus = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var deviceHealthBreakdown = deviceHealthRows.ToDictionary(r => r.HealthStatus, r => r.Count);

        // Device onboarding status breakdown — NOT affected by vulnerability filters
        var deviceOnboardingRows = await _dbContext
            .Devices.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.OnboardingStatus != null)
            .GroupBy(a => a.OnboardingStatus!)
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
            // Patching tasks that existed on this date
            var tasksOnDate = patchingTasks.Where(t => t.CreatedAt <= snapshotInstant).ToList();
            if (tasksOnDate.Count == 0)
            {
                slaComplianceTrend.Add(new SlaComplianceTrendPointDto(snapshotDate, 100m));
                continue;
            }
            var overdueOnDateSla = tasksOnDate.Count(t =>
                t.Status != PatchingTaskStatus.Completed && t.DueDate < snapshotInstant);
            var dailySlaPercent = Math.Round(
                (tasksOnDate.Count - overdueOnDateSla) / (decimal)tasksOnDate.Count * 100m, 1);
            slaComplianceTrend.Add(new SlaComplianceTrendPointDto(snapshotDate, dailySlaPercent));
        }

        // ────────────────────────────────────────────────────
        // Metric sparklines — 30 daily data points (task-based only; episode data removed in Phase 2)
        // ────────────────────────────────────────────────────
        var sparkCriticalBacklog = Enumerable.Repeat(0, 30).ToList();
        var sparkOpenStatuses = Enumerable.Repeat(0, 30).ToList();
        var sparkOverdueActions = new List<int>();
        var sparkHealthyTasks = new List<int>();

        for (var dayOffset = 29; dayOffset >= 0; dayOffset--)
        {
            var snapshotDate = todayDate.AddDays(-dayOffset);
            var snapshotInstant = new DateTimeOffset(snapshotDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

            var tasksOnDateSpk = patchingTasks.Where(t => t.CreatedAt <= snapshotInstant).ToList();
            var overdueOnDate = tasksOnDateSpk.Count(t =>
                t.Status != PatchingTaskStatus.Completed
                && t.DueDate < snapshotInstant);
            sparkOverdueActions.Add(overdueOnDate);
            sparkHealthyTasks.Add(Math.Max(tasksOnDateSpk.Count - overdueOnDate, 0));
        }

        var metricSparklines = new MetricSparklinesDto(
            sparkCriticalBacklog, sparkOverdueActions, sparkHealthyTasks, sparkOpenStatuses);

        return Ok(
            new DashboardSummaryDto(
                exposureScore,
                vulnsBySeverity,
                vulnsByStatus,
            slaPercent,
            overdueCount,
            patchingTasks.Count,
            avgRemediationDays,
            topVulns,
            latestUnhandled,
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

    [HttpGet("owner-summary")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<OwnerDashboardSummaryDto>> GetOwnerSummary(CancellationToken ct)
    {
        var ownerScope = await ResolveOwnerScopeAsync(ct);
        if (ownerScope.Result is not null)
        {
            return ownerScope.Result;
        }

        var tenantId = ownerScope.TenantId;
        var ownedAssetsQuery = ownerScope.OwnedAssetsQuery;

        var ownedAssetIds = await ownedAssetsQuery
            .Select(asset => asset.Id)
            .ToListAsync(ct);

        var assetsNeedingAttention = ownedAssetIds.Count == 0
            ? 0
            : await (
                from asset in ownedAssetsQuery
                join score in _dbContext.DeviceRiskScores.AsNoTracking()
                    on asset.Id equals score.DeviceId
                where score.OverallScore >= 500m
                select asset.Id
            )
                .Distinct()
                .CountAsync(ct);

        var topOwnedAssets = ownedAssetIds.Count == 0
            ? []
            : await BuildOwnerAssetSummariesAsync(tenantId, ownedAssetsQuery, take: 6, attentionOnly: false, ct);

        var topAssetIds = topOwnedAssets.Select(item => item.AssetId).ToList();
        var topDriverRows = topAssetIds.Count == 0
            ? []
            : await (
                from assessment in _dbContext.ExposureAssessments.AsNoTracking()
                where assessment.TenantId == tenantId
                      && topAssetIds.Contains(assessment.Exposure.DeviceId)
                      && assessment.Exposure.Status == ExposureStatus.Open
                orderby assessment.EnvironmentalCvss descending
                select new OwnerTopDriverRow(
                    assessment.Exposure.DeviceId,
                    assessment.EnvironmentalCvss,
                    assessment.EnvironmentalCvss >= 9.0m ? "Critical"
                        : assessment.EnvironmentalCvss >= 7.0m ? "High"
                        : assessment.EnvironmentalCvss >= 4.0m ? "Medium" : "Low",
                    assessment.Exposure.Vulnerability.ExternalId,
                    assessment.Exposure.Vulnerability.Title,
                    assessment.Exposure.Vulnerability.Description,
                    assessment.EnvironmentalCvss >= 9.0m ? Severity.Critical
                        : assessment.EnvironmentalCvss >= 7.0m ? Severity.High
                        : assessment.EnvironmentalCvss >= 4.0m ? Severity.Medium : Severity.Low
                )
            ).ToArrayAsync(ct);

        var ownerAssets = topOwnedAssets
            .Select(item =>
            {
                var topDriver = topDriverRows
                    .Where(driver => driver.AssetId == item.AssetId)
                    .OrderByDescending(driver => driver.EpisodeRiskScore)
                    .FirstOrDefault();

                return new OwnerAssetSummaryDto(
                    item.AssetId,
                    item.AssetName,
                    item.DeviceGroupName,
                    item.Criticality,
                    item.CurrentRiskScore,
                    topDriver?.RiskBand,
                    item.OpenEpisodeCount,
                    topDriver?.Title,
                    topDriver is null
                        ? null
                        : OwnerFacingIssueSummaryFormatter.BuildIssueSummary(
                            null,
                            topDriver.Title,
                            topDriver.Description,
                            topDriver.Severity
                        )
                );
            })
            .ToList();

        // Patching tasks targeting teams that own the user's devices
        var ownerPatchingRows = await (
            from pt in _dbContext.PatchingTasks.AsNoTracking()
            join decision in _dbContext.RemediationDecisions.AsNoTracking()
                on pt.RemediationDecisionId equals decision.Id
            join rc in _dbContext.RemediationCases.AsNoTracking()
                on pt.RemediationCaseId equals rc.Id
            join sp in _dbContext.SoftwareProducts.AsNoTracking()
                on rc.SoftwareProductId equals sp.Id
            join ownerTeam in _dbContext.Teams.AsNoTracking()
                on pt.OwnerTeamId equals ownerTeam.Id
            where pt.TenantId == tenantId
                  && ownerScope.OwnerTeamIds.Contains(pt.OwnerTeamId)
                  && pt.Status != PatchingTaskStatus.Completed
            select new
            {
                RemediationCaseId = pt.RemediationCaseId,
                SoftwareProductId = rc.SoftwareProductId,
                SoftwareProductName = sp.Name,
                OwnerTeamName = ownerTeam.Name,
                PatchingTaskId = pt.Id,
                pt.DueDate,
                ActionState = pt.Status.ToString(),
            }
        )
            .OrderBy(item => item.DueDate)
            .Take(10)
            .ToListAsync(ct);

        // Build owner actions from patching tasks — use the top vulnerability per software asset
        var patchingSoftwareProductIds = ownerPatchingRows
            .Select(p => p.SoftwareProductId)
            .Distinct()
            .ToList();

        var topVulnPerSoftwareProduct = Array.Empty<OwnerTopVulnRow>();

        var topVulnBySoftware = topVulnPerSoftwareProduct
            .GroupBy(v => v.SoftwareProductId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(v => v.Severity).First()
            );

        var ownerActionRows = ownerPatchingRows.Select(p =>
        {
            var topVuln = topVulnBySoftware.GetValueOrDefault(p.SoftwareProductId);
            return new
            {
                RemediationCaseId = p.RemediationCaseId,
                AssetId = p.SoftwareProductId,
                VulnerabilityId = topVuln?.Id ?? Guid.Empty,
                TaskId = (Guid?)p.PatchingTaskId,
                AssetName = p.SoftwareProductName,
                p.OwnerTeamName,
                ExternalId = topVuln?.ExternalId ?? "-",
                Title = topVuln?.Title ?? "Patching required",
                Description = topVuln?.Description ?? "",
                Severity = topVuln?.Severity ?? Severity.Medium,
                EpisodeRiskScore = (decimal?)null,
                EpisodeRiskBand = (string?)null,
                DueDate = p.DueDate,
                ActionState = p.ActionState,
            };
        }).ToList();

        var ownerActionAssetIds = ownerActionRows
            .Select(item => item.AssetId)
            .Distinct()
            .ToList();

        var ownerActions = ownerActionRows
            .Select(item => new OwnerActionDto(
                item.RemediationCaseId,
                item.VulnerabilityId,
                item.TaskId,
                item.AssetName,
                item.OwnerTeamName,
                item.ExternalId,
                item.Title ?? "",
                [],
                OwnerFacingIssueSummaryFormatter.BuildIssueSummary(
                    null,
                    item.Title ?? "",
                    item.Description ?? "",
                    item.Severity
                ),
                item.Severity.ToString(),
                item.EpisodeRiskScore,
                item.EpisodeRiskBand,
                item.DueDate,
                item.ActionState
            ))
            .ToList();

        return Ok(new OwnerDashboardSummaryDto(
            ownedAssetIds.Count,
            assetsNeedingAttention,
            ownerActions.Count,
            ownerActions.Count(item => item.DueDate.HasValue && item.DueDate.Value < DateTimeOffset.UtcNow),
            ownerAssets,
            ownerActions
        ));
    }

    [HttpGet("owner-assets-needing-attention")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<List<OwnerAssetSummaryDto>>> GetOwnerAssetsNeedingAttention(CancellationToken ct)
    {
        var ownerScope = await ResolveOwnerScopeAsync(ct);
        if (ownerScope.Result is not null)
        {
            return ownerScope.Result;
        }

        var assets = await BuildOwnerAssetSummariesAsync(
            ownerScope.TenantId,
            ownerScope.OwnedAssetsQuery,
            take: null,
            attentionOnly: true,
            ct);
        return Ok(assets);
    }

    private async Task<OwnerScopeResult> ResolveOwnerScopeAsync(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return new OwnerScopeResult(
                new BadRequestObjectResult(new ProblemDetails { Title = "No active tenant is selected." }),
                Guid.Empty,
                [],
                _dbContext.Devices.AsNoTracking().Where(_ => false));
        }

        var currentUserId = _tenantContext.CurrentUserId;
        if (currentUserId == Guid.Empty)
        {
            return new OwnerScopeResult(
                new UnauthorizedResult(),
                Guid.Empty,
                [],
                _dbContext.Devices.AsNoTracking().Where(_ => false));
        }

        var ownerTeamIds = await _dbContext.TeamMembers.AsNoTracking()
            .Where(item => item.UserId == currentUserId)
            .Select(item => item.TeamId)
            .ToListAsync(ct);

        var ownedAssetsQuery = _dbContext.Devices.AsNoTracking()
            .Where(device =>
                device.TenantId == tenantId
                && (
                    device.OwnerUserId == currentUserId
                    || (device.OwnerTeamId != null && ownerTeamIds.Contains(device.OwnerTeamId.Value))
                    || (device.FallbackTeamId != null && ownerTeamIds.Contains(device.FallbackTeamId.Value))
                )
            );

        return new OwnerScopeResult(null, tenantId, ownerTeamIds, ownedAssetsQuery);
    }

    private async Task<List<OwnerAssetSummaryDto>> BuildOwnerAssetSummariesAsync(
        Guid tenantId,
        IQueryable<Device> ownedAssetsQuery,
        int? take,
        bool attentionOnly,
        CancellationToken ct)
    {
        var query =
            from asset in ownedAssetsQuery
            join score in _dbContext.DeviceRiskScores.AsNoTracking()
                on asset.Id equals score.DeviceId into scoreJoin
            from score in scoreJoin.DefaultIfEmpty()
            select new
            {
                asset.Id,
                AssetName = asset.ComputerDnsName ?? asset.Name,
                asset.GroupName,
                Criticality = asset.Criticality.ToString(),
                CurrentRiskScore = score != null ? (decimal?)score.OverallScore : null,
                OpenEpisodeCount = score != null ? score.OpenEpisodeCount : 0,
                CriticalCount = score != null ? score.CriticalCount : 0,
                HighCount = score != null ? score.HighCount : 0
            };

        if (attentionOnly)
        {
            query = query.Where(item => (item.CurrentRiskScore ?? 0m) >= 500m);
        }

        query = query
            .OrderByDescending(item => item.CurrentRiskScore ?? 0m)
            .ThenByDescending(item => item.CriticalCount)
            .ThenByDescending(item => item.HighCount);

        var rows = take.HasValue
            ? await query.Take(take.Value).ToListAsync(ct)
            : await query.ToListAsync(ct);

        var assetIds = rows.Select(item => item.Id).ToList();
        var topDriverRows = assetIds.Count == 0
            ? []
            : await (
                from assessment in _dbContext.ExposureAssessments.AsNoTracking()
                where assessment.TenantId == tenantId
                      && assetIds.Contains(assessment.Exposure.DeviceId)
                      && assessment.Exposure.Status == ExposureStatus.Open
                orderby assessment.EnvironmentalCvss descending
                select new OwnerTopDriverRow(
                    assessment.Exposure.DeviceId,
                    assessment.EnvironmentalCvss,
                    assessment.EnvironmentalCvss >= 9.0m ? "Critical"
                        : assessment.EnvironmentalCvss >= 7.0m ? "High"
                        : assessment.EnvironmentalCvss >= 4.0m ? "Medium" : "Low",
                    assessment.Exposure.Vulnerability.ExternalId,
                    assessment.Exposure.Vulnerability.Title,
                    assessment.Exposure.Vulnerability.Description,
                    assessment.EnvironmentalCvss >= 9.0m ? Severity.Critical
                        : assessment.EnvironmentalCvss >= 7.0m ? Severity.High
                        : assessment.EnvironmentalCvss >= 4.0m ? Severity.Medium : Severity.Low
                )
            ).ToArrayAsync(ct);

        return rows
            .Select(item =>
            {
                var topDriver = topDriverRows
                    .Where(driver => driver.AssetId == item.Id)
                    .OrderByDescending(driver => driver.EpisodeRiskScore)
                    .FirstOrDefault();

                return new OwnerAssetSummaryDto(
                    item.Id,
                    item.AssetName,
                    item.GroupName,
                    item.Criticality,
                    item.CurrentRiskScore,
                    topDriver?.RiskBand,
                    item.OpenEpisodeCount,
                    topDriver?.Title,
                    topDriver is null
                        ? null
                        : OwnerFacingIssueSummaryFormatter.BuildIssueSummary(
                            null,
                            topDriver.Title,
                            topDriver.Description,
                            topDriver.Severity
                        )
                );
            })
            .ToList();
    }

    private sealed record OwnerScopeResult(
        ActionResult? Result,
        Guid TenantId,
        List<Guid> OwnerTeamIds,
        IQueryable<Device> OwnedAssetsQuery);

    [HttpGet("security-manager-summary")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<SecurityManagerDashboardSummaryDto>> GetSecurityManagerSummary(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var recentApprovedDecisions = await (
            from decision in _dbContext.RemediationDecisions.AsNoTracking()
            join rc in _dbContext.RemediationCases.AsNoTracking()
                on decision.RemediationCaseId equals rc.Id
            join sp in _dbContext.SoftwareProducts.AsNoTracking()
                on rc.SoftwareProductId equals sp.Id
            where decision.TenantId == tenantId
                  && decision.ApprovalStatus == DecisionApprovalStatus.Approved
                  && (decision.Outcome == RemediationOutcome.RiskAcceptance
                      || decision.Outcome == RemediationOutcome.AlternateMitigation)
            orderby decision.ApprovedAt descending, decision.DecidedAt descending
            select new
            {
                decision.Id,
                RemediationCaseId = decision.RemediationCaseId,
                SoftwareName = sp.Name,
                Outcome = decision.Outcome.ToString(),
                decision.Justification,
                decision.DecidedAt,
                decision.ApprovedAt,
                decision.ExpiryDate,
            }
        )
            .Take(10)
            .ToListAsync(ct);

        var policySoftwareStats = await BuildSoftwareScopeStatsAsync(
            tenantId,
            recentApprovedDecisions.Select(item => item.RemediationCaseId).Distinct().ToList(),
            ct);

        var approvalTasks = await BuildApprovalAttentionTasksAsync(
            tenantId,
            ParseRoleNames(_tenantContext.GetRolesForTenant(tenantId)),
            ct);

        return Ok(new SecurityManagerDashboardSummaryDto(
            recentApprovedDecisions.Select(item =>
            {
                var stats = policySoftwareStats.GetValueOrDefault(item.RemediationCaseId);
                return new ApprovedPolicyDecisionDto(
                    item.Id,
                    item.RemediationCaseId,
                    item.SoftwareName,
                    item.Outcome,
                    string.IsNullOrWhiteSpace(item.Justification) ? null : item.Justification,
                    stats?.HighestSeverity ?? "Unknown",
                    stats?.VulnerabilityCount ?? 0,
                    item.ApprovedAt ?? item.DecidedAt,
                    item.ExpiryDate
                );
            }).ToList(),
            approvalTasks
        ));
    }

    [HttpGet("technical-manager-summary")]
    [Authorize(Policy = Policies.ViewApprovalTasks)]
    public async Task<ActionResult<TechnicalManagerDashboardSummaryDto>> GetTechnicalManagerSummary(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var approvedPatchingTasks = await (
            from task in _dbContext.PatchingTasks.AsNoTracking()
            join decision in _dbContext.RemediationDecisions.AsNoTracking()
                on task.RemediationDecisionId equals decision.Id
            join rc in _dbContext.RemediationCases.AsNoTracking()
                on task.RemediationCaseId equals rc.Id
            join sp in _dbContext.SoftwareProducts.AsNoTracking()
                on rc.SoftwareProductId equals sp.Id
            join ownerTeam in _dbContext.Teams.AsNoTracking()
                on task.OwnerTeamId equals ownerTeam.Id
            where task.TenantId == tenantId
                  && task.Status != PatchingTaskStatus.Completed
                  && decision.Outcome == RemediationOutcome.ApprovedForPatching
                  && decision.ApprovalStatus == DecisionApprovalStatus.Approved
            orderby decision.ApprovedAt descending, task.CreatedAt descending
            select new
            {
                task.Id,
                task.RemediationDecisionId,
                RemediationCaseId = task.RemediationCaseId,
                SoftwareName = sp.Name,
                OwnerTeamName = ownerTeam.Name,
                task.DueDate,
                task.Status,
                task.CreatedAt,
                decision.ApprovedAt,
                decision.MaintenanceWindowDate,
            }
        )
            .Take(10)
            .ToListAsync(ct);

        var patchingSoftwareStats = await BuildSoftwareScopeStatsAsync(
            tenantId,
            approvedPatchingTasks.Select(item => item.RemediationCaseId).Distinct().ToList(),
            ct);

        var now = DateTimeOffset.UtcNow;
        var missedMaintenanceWindowCount = await (
            from task in _dbContext.PatchingTasks.AsNoTracking()
            join decision in _dbContext.RemediationDecisions.AsNoTracking()
                on task.RemediationDecisionId equals decision.Id
            where task.TenantId == tenantId
                  && task.Status != PatchingTaskStatus.Completed
                  && decision.MaintenanceWindowDate != null
                  && decision.MaintenanceWindowDate < now
            select task.Id
        ).CountAsync(ct);

        // Devices with open exposures older than the Low SLA (180 days)
        const int agedThresholdDays = 180;
        var agedCutoff = now.AddDays(-agedThresholdDays);
        var devicesWithAgedVulnerabilities = await (
            from exposure in _dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            where exposure.TenantId == tenantId
                  && exposure.Status == ExposureStatus.Open
                  && exposure.FirstObservedAt < agedCutoff
            join assessment in _dbContext.ExposureAssessments.AsNoTracking()
                on exposure.Id equals assessment.DeviceVulnerabilityExposureId into assessmentJoin
            from assessment in assessmentJoin.DefaultIfEmpty()
            group new { exposure, assessment } by exposure.DeviceId into g
            select new
            {
                DeviceId = g.Key,
                OldVulnerabilityCount = g.Select(x => x.exposure.VulnerabilityId).Distinct().Count(),
                OldestPublishedDate = g.Min(x => x.exposure.Vulnerability.PublishedDate),
                MaxCvss = g.Max(x => x.assessment != null ? (decimal?)x.assessment.EnvironmentalCvss : null),
                DeviceName = g.First().exposure.Device.ComputerDnsName ?? g.First().exposure.Device.Name,
                Criticality = g.First().exposure.Device.Criticality,
            }
        )
            .OrderByDescending(x => x.MaxCvss)
            .Take(20)
            .ToListAsync(ct);

        var devicesWithAgedVulnerabilitiesDtos = devicesWithAgedVulnerabilities
            .Select(x => new DevicePatchDriftDto(
                x.DeviceId,
                x.DeviceName,
                x.Criticality.ToString(),
                x.MaxCvss switch
                {
                    >= 9.0m => "Critical",
                    >= 7.0m => "High",
                    >= 4.0m => "Medium",
                    not null => "Low",
                    _ => "Unknown",
                },
                x.OldVulnerabilityCount,
                x.OldestPublishedDate ?? now
            ))
            .ToList();

        var approvalTasks = await BuildApprovalAttentionTasksAsync(
            tenantId,
            ParseRoleNames(_tenantContext.GetRolesForTenant(tenantId)),
            ct);

        return Ok(new TechnicalManagerDashboardSummaryDto(
            missedMaintenanceWindowCount,
            approvedPatchingTasks.Select(item =>
            {
                var stats = patchingSoftwareStats.GetValueOrDefault(item.RemediationCaseId);
                return new ApprovedPatchingTaskDto(
                    item.Id,
                    item.RemediationDecisionId,
                    item.RemediationCaseId,
                    item.SoftwareName,
                    item.OwnerTeamName,
                    stats?.HighestSeverity ?? "Unknown",
                    stats?.AffectedDeviceCount ?? 0,
                    item.ApprovedAt ?? item.CreatedAt,
                    item.DueDate,
                    item.MaintenanceWindowDate,
                    item.Status.ToString()
                );
            }).ToList(),
            devicesWithAgedVulnerabilitiesDtos,
            approvalTasks
        ));
    }

    [HttpGet("risk-changes")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<DashboardRiskChangeBriefDto>> GetRiskChanges(
        [FromQuery] int days = 1,
        CancellationToken ct = default
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var cutoffHours = Math.Clamp(days, 1, 30) * 24;
        return Ok(await _dashboardQueryService.BuildRiskChangeBriefAsync(
            tenantId, _tenantContext.CurrentTenantId ?? Guid.Empty,
            limit: null, highCriticalOnly: false, ct, cutoffHours));
    }

    [HttpGet("heatmap")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<List<HeatmapRowDto>>> GetHeatmap(
        [FromQuery] DashboardFilterQuery filter,
        [FromQuery] string groupBy = "deviceGroup",
        CancellationToken ct = default
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        // Heatmap not yet implemented — returns empty list.
        _ = tenantId;
        return Ok(new List<HeatmapRowDto>());
    }

    [HttpGet("trends")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<TrendDataDto>> GetTrends(
        [FromQuery] DashboardFilterQuery filter,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var (trendFilteredAssetIds, trendMinPublishedDate) = BuildFilterContext(tenantId, filter);
        var trendWindowEnd = DateTimeOffset.UtcNow;
        var trendWindowStart = trendWindowEnd.AddDays(-89); // 90-day rolling window

        var episodeRows = await _dbContext.ExposureEpisodes.AsNoTracking()
            .Where(ep => ep.TenantId == tenantId
                && ep.FirstSeenAt <= trendWindowEnd
                && (ep.ClosedAt == null || ep.ClosedAt >= trendWindowStart))
            .Select(ep => new
            {
                ep.FirstSeenAt,
                ep.ClosedAt,
                ep.Exposure.Vulnerability.VendorSeverity,
                DeviceId = ep.Exposure.DeviceId,
                PublishedDate = ep.Exposure.Vulnerability.PublishedDate,
            })
            .ToListAsync(ct);

        // Apply filters in-memory after projection
        if (trendFilteredAssetIds != null)
        {
            var filteredDeviceIds = await trendFilteredAssetIds.ToListAsync(ct);
            var filteredSet = filteredDeviceIds.ToHashSet();
            episodeRows = episodeRows.Where(r => filteredSet.Contains(r.DeviceId)).ToList();
        }
        if (trendMinPublishedDate.HasValue)
            episodeRows = episodeRows.Where(r => r.PublishedDate >= trendMinPublishedDate.Value).ToList();

        var trendItems = new List<TrendItem>();
        for (var dayOffset = 89; dayOffset >= 0; dayOffset--)
        {
            var day = DateOnly.FromDateTime(trendWindowEnd.AddDays(-dayOffset).DateTime);
            var dayInstant = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var dayEndInstant = dayInstant.AddDays(1);

            foreach (var severity in Enum.GetValues<Severity>())
            {
                var openOnDay = episodeRows.Count(ep =>
                    ep.VendorSeverity == severity
                    && ep.FirstSeenAt < dayEndInstant
                    && (ep.ClosedAt == null || ep.ClosedAt >= dayInstant));
                if (openOnDay > 0)
                    trendItems.Add(new TrendItem(day, severity.ToString(), openOnDay));
            }
        }

        return Ok(new TrendDataDto(trendItems));
    }

    [HttpGet("burndown")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<BurndownTrendDto>> GetBurndown(
        [FromQuery] DashboardFilterQuery filter,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var (burnFilteredAssetIds, burnMinPublishedDate) = BuildFilterContext(tenantId, filter);
        var burnEnd = DateTimeOffset.UtcNow;
        var burnStart = burnEnd.AddDays(-89);

        var burnEpisodeRows = await _dbContext.ExposureEpisodes.AsNoTracking()
            .Where(ep => ep.TenantId == tenantId
                && ep.FirstSeenAt <= burnEnd
                && (ep.ClosedAt == null || ep.ClosedAt >= burnStart))
            .Select(ep => new
            {
                ep.FirstSeenAt,
                ep.ClosedAt,
                DeviceId = ep.Exposure.DeviceId,
                PublishedDate = ep.Exposure.Vulnerability.PublishedDate,
            })
            .ToListAsync(ct);

        if (burnFilteredAssetIds != null)
        {
            var filteredDeviceIds = await burnFilteredAssetIds.ToListAsync(ct);
            var filteredSet = filteredDeviceIds.ToHashSet();
            burnEpisodeRows = burnEpisodeRows.Where(r => filteredSet.Contains(r.DeviceId)).ToList();
        }
        if (burnMinPublishedDate.HasValue)
            burnEpisodeRows = burnEpisodeRows.Where(r => r.PublishedDate >= burnMinPublishedDate.Value).ToList();

        var burndownItems = new List<BurndownPointDto>();
        for (var dayOffset = 89; dayOffset >= 0; dayOffset--)
        {
            var day = DateOnly.FromDateTime(burnEnd.AddDays(-dayOffset).DateTime);
            var dayInstant = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var dayEndInstant = dayInstant.AddDays(1);

            var discovered = burnEpisodeRows.Count(ep =>
                ep.FirstSeenAt >= dayInstant && ep.FirstSeenAt < dayEndInstant);
            var resolved = burnEpisodeRows.Count(ep =>
                ep.ClosedAt >= dayInstant && ep.ClosedAt < dayEndInstant);
            var netOpen = burnEpisodeRows.Count(ep =>
                ep.FirstSeenAt < dayEndInstant && (ep.ClosedAt == null || ep.ClosedAt >= dayInstant));

            burndownItems.Add(new BurndownPointDto(day, discovered, resolved, netOpen));
        }

        return Ok(new BurndownTrendDto(burndownItems));
    }

    [HttpGet("filter-options")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<DashboardFilterOptionsDto>> GetFilterOptions(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var platforms = await _dbContext.Devices.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.OsPlatform != null)
            .Select(a => a.OsPlatform!)
            .Distinct()
            .OrderBy(p => p)
            .ToArrayAsync(ct);

        var deviceGroups = await _dbContext.Devices.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.GroupName != null)
            .Select(a => a.GroupName!)
            .Distinct()
            .OrderBy(g => g)
            .ToArrayAsync(ct);

        return Ok(new DashboardFilterOptionsDto(platforms, deviceGroups));
    }

    private async Task<List<ApprovalAttentionTaskDto>> BuildApprovalAttentionTasksAsync(
        Guid tenantId,
        HashSet<RoleName> visibleRoles,
        CancellationToken ct)
    {
        var pendingTasks = await (
            from task in _dbContext.ApprovalTasks.AsNoTracking()
            join decision in _dbContext.RemediationDecisions.AsNoTracking()
                on task.RemediationDecisionId equals decision.Id
            join rc in _dbContext.RemediationCases.AsNoTracking()
                on decision.RemediationCaseId equals rc.Id
            join sp in _dbContext.SoftwareProducts.AsNoTracking()
                on rc.SoftwareProductId equals sp.Id
            where task.TenantId == tenantId
                  && task.Status == ApprovalTaskStatus.Pending
                  && task.VisibleRoles.Any(role => visibleRoles.Contains(role.Role))
            orderby task.ExpiresAt ascending, task.CreatedAt descending
            select new
            {
                task.Id,
                task.RemediationDecisionId,
                RemediationCaseId = decision.RemediationCaseId,
                SoftwareName = sp.Name,
                ApprovalType = task.Type.ToString(),
                task.ExpiresAt,
                decision.MaintenanceWindowDate,
                task.CreatedAt,
            }
        )
            .Take(8)
            .ToListAsync(ct);

        var softwareStats = await BuildSoftwareScopeStatsAsync(
            tenantId,
            pendingTasks.Select(item => item.RemediationCaseId).Distinct().ToList(),
            ct);

        var now = DateTimeOffset.UtcNow;
        return pendingTasks.Select(item =>
        {
            var stats = softwareStats.GetValueOrDefault(item.RemediationCaseId);
            var attentionState = item.ExpiresAt <= now
                ? "Overdue"
                : item.ExpiresAt <= now.AddHours(24)
                    ? "NearExpiry"
                    : "Pending";

            return new ApprovalAttentionTaskDto(
                item.Id,
                item.RemediationDecisionId,
                item.RemediationCaseId,
                item.SoftwareName,
                item.ApprovalType,
                stats?.HighestSeverity ?? "Unknown",
                stats?.VulnerabilityCount ?? 0,
                item.ExpiresAt,
                item.MaintenanceWindowDate,
                item.CreatedAt,
                attentionState
            );
        }).ToList();
    }

    private async Task<Dictionary<Guid, SoftwareScopeStats>> BuildSoftwareScopeStatsAsync(
        Guid tenantId,
        List<Guid> remediationCaseIds,
        CancellationToken ct)
    {
        if (remediationCaseIds.Count == 0)
            return new Dictionary<Guid, SoftwareScopeStats>();

        // Map remediationCaseId -> softwareProductId
        var caseToProduct = await _dbContext.RemediationCases.AsNoTracking()
            .Where(rc => rc.TenantId == tenantId && remediationCaseIds.Contains(rc.Id))
            .Select(rc => new { rc.Id, rc.SoftwareProductId })
            .ToListAsync(ct);

        var softwareProductIds = caseToProduct.Select(x => x.SoftwareProductId).Distinct().ToList();

        // For each softwareProductId: max EnvironmentalCvss and distinct affected device count
        var exposureStats = await (
            from exposure in _dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            where exposure.TenantId == tenantId
                  && exposure.Status == ExposureStatus.Open
                  && exposure.SoftwareProductId != null
                  && softwareProductIds.Contains(exposure.SoftwareProductId!.Value)
            join assessment in _dbContext.ExposureAssessments.AsNoTracking()
                on exposure.Id equals assessment.DeviceVulnerabilityExposureId into assessmentJoin
            from assessment in assessmentJoin.DefaultIfEmpty()
            group new { exposure, assessment } by exposure.SoftwareProductId!.Value into g
            select new
            {
                SoftwareProductId = g.Key,
                MaxCvss = g.Max(x => x.assessment != null ? (decimal?)x.assessment.EnvironmentalCvss : null),
                VulnerabilityCount = g.Select(x => x.exposure.VulnerabilityId).Distinct().Count(),
                AffectedDeviceCount = g.Select(x => x.exposure.DeviceId).Distinct().Count(),
            }
        ).ToListAsync(ct);

        var statsByProduct = exposureStats.ToDictionary(x => x.SoftwareProductId);

        var result = new Dictionary<Guid, SoftwareScopeStats>();
        foreach (var c in caseToProduct)
        {
            if (!statsByProduct.TryGetValue(c.SoftwareProductId, out var stats))
                continue;

            var highestSeverity = stats.MaxCvss switch
            {
                >= 9.0m => "Critical",
                >= 7.0m => "High",
                >= 4.0m => "Medium",
                not null => "Low",
                _ => "Unknown",
            };

            result[c.Id] = new SoftwareScopeStats(
                highestSeverity,
                stats.VulnerabilityCount,
                stats.AffectedDeviceCount
            );
        }

        return result;
    }

    private (IQueryable<Guid>? FilteredAssetIdQuery, DateTimeOffset? MinPublishedDate) BuildFilterContext(
        Guid tenantId, DashboardFilterQuery filter)
    {
        IQueryable<Guid>? filteredAssetIdQuery = null;
        if (!string.IsNullOrEmpty(filter.Platform) || !string.IsNullOrEmpty(filter.DeviceGroup))
        {
            var assetQuery = _dbContext.Devices.AsNoTracking()
                .Where(a => a.TenantId == tenantId);
            if (!string.IsNullOrEmpty(filter.Platform))
                assetQuery = assetQuery.Where(a => a.OsPlatform == filter.Platform);
            if (!string.IsNullOrEmpty(filter.DeviceGroup))
                assetQuery = assetQuery.Where(a => a.GroupName == filter.DeviceGroup);
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

    private static HashSet<RoleName> ParseRoleNames(IReadOnlyList<string> userRoles)
    {
        return userRoles
            .Select(role => Enum.TryParse<RoleName>(role, true, out var parsedRole) ? parsedRole : (RoleName?)null)
            .Where(role => role.HasValue)
            .Select(role => role!.Value)
            .ToHashSet();
    }

    private sealed record SoftwareScopeStats(
        string HighestSeverity,
        int VulnerabilityCount,
        int AffectedDeviceCount
    );

    private sealed record OwnerTopDriverRow(
        Guid AssetId,
        decimal? EpisodeRiskScore,
        string? RiskBand,
        string ExternalId,
        string Title,
        string? Description,
        Severity Severity
    );

    private sealed record OwnerTopVulnRow(
        Guid SoftwareProductId,
        Guid Id,
        string ExternalId,
        string Title,
        string? Description,
        Severity Severity
    );

}
