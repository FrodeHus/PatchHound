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
        var hasFilters = filteredAssetIds is not null || minPublishedDate.HasValue;

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

        // Age buckets for open exposures — age measured from vulnerability published date
        var openEpisodeAges = await exposureBaseQuery
            .Where(e => e.Status == ExposureStatus.Open && e.Vulnerability.PublishedDate != null)
            .Select(e => new
            {
                e.VulnerabilityId,
                AgeDays = (int)((now - e.Vulnerability.PublishedDate!.Value).TotalDays),
                e.Vulnerability.VendorSeverity,
            })
            .ToListAsync(ct);

        var ageBuckets = ageBucketDefinitions.Select(b =>
        {
            var inBucket = openEpisodeAges.Where(e =>
                e.AgeDays >= b.MinDays && (b.MaxDays == int.MaxValue || e.AgeDays <= b.MaxDays)).ToList();
            var uniqueVulnerabilities = inBucket
                .GroupBy(e => new { e.VulnerabilityId, e.VendorSeverity })
                .Select(group => group.Key)
                .ToList();

            return new VulnerabilityAgeBucketDto(
                b.Label,
                uniqueVulnerabilities.Count,
                uniqueVulnerabilities.Count(e => e.VendorSeverity == Severity.Critical),
                uniqueVulnerabilities.Count(e => e.VendorSeverity == Severity.High),
                uniqueVulnerabilities.Count(e => e.VendorSeverity == Severity.Medium),
                uniqueVulnerabilities.Count(e => e.VendorSeverity == Severity.Low)
            );
        }).ToList();

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
            .Where(score => string.IsNullOrEmpty(filter.DeviceGroup) || score.DeviceGroupName == filter.DeviceGroup)
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

        var executiveExposure = await BuildExecutiveExposureSummaryAsync(
            tenantId,
            filter,
            filteredAssetIds,
            minPublishedDate,
            vulnsByDeviceGroup,
            hasFilters,
            ct);
        var accountability = await BuildExecutiveAccountabilitySummaryAsync(tenantId, now, ct);
        var exposureScore = executiveExposure.Score;

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
                mttrBySeverity,
                executiveExposure,
                accountability
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
            : await BuildOwnerAssetSummariesAsync(tenantId, ownedAssetsQuery, take: null, attentionOnly: false, ct);

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
                PatchingTaskId = (Guid?)pt.Id,
                DueDate = (DateTimeOffset?)pt.DueDate,
                ActionState = pt.Status.ToString(),
            }
        )
            .OrderBy(item => item.DueDate)
            .ToListAsync(ct);

        // Workflows at the RemediationDecision stage where the owner team must make a decision.
        // These have no PatchingTask yet and would otherwise be invisible to the asset owner.
        var ownerDecisionRows = await (
            from wf in _dbContext.RemediationWorkflows.AsNoTracking()
            join rc in _dbContext.RemediationCases.AsNoTracking()
                on wf.RemediationCaseId equals rc.Id
            join sp in _dbContext.SoftwareProducts.AsNoTracking()
                on rc.SoftwareProductId equals sp.Id
            join ownerTeam in _dbContext.Teams.AsNoTracking()
                on wf.SoftwareOwnerTeamId equals ownerTeam.Id
            where wf.TenantId == tenantId
                  && ownerScope.OwnerTeamIds.Contains(wf.SoftwareOwnerTeamId)
                  && wf.CurrentStage == RemediationWorkflowStage.RemediationDecision
                  && wf.Status == RemediationWorkflowStatus.Active
            select new
            {
                RemediationCaseId = rc.Id,
                SoftwareProductId = rc.SoftwareProductId,
                SoftwareProductName = sp.Name,
                OwnerTeamName = ownerTeam.Name,
                PatchingTaskId = (Guid?)null,
                DueDate = (DateTimeOffset?)null,
                ActionState = "AwaitingDecision",
            }
        ).ToListAsync(ct);

        // Merge patching tasks + awaiting-decision workflows, deduplicate by RemediationCaseId
        // (a case shouldn't appear twice even if a workflow and a task both match).
        var existingCaseIds = ownerPatchingRows.Select(r => r.RemediationCaseId).ToHashSet();
        var combinedRows = ownerPatchingRows
            .Concat(ownerDecisionRows.Where(r => !existingCaseIds.Contains(r.RemediationCaseId)))
            .ToList();

        // Build owner actions from patching tasks — use the top vulnerability per software asset
        var patchingSoftwareProductIds = combinedRows
            .Select(p => p.SoftwareProductId)
            .Distinct()
            .ToList();

        var topVulnPerSoftwareProduct = patchingSoftwareProductIds.Count == 0
            ? []
            : await (
                from assessment in _dbContext.ExposureAssessments.AsNoTracking()
                where assessment.TenantId == tenantId
                      && assessment.Exposure.SoftwareProductId != null
                      && patchingSoftwareProductIds.Contains(assessment.Exposure.SoftwareProductId!.Value)
                      && assessment.Exposure.Status == ExposureStatus.Open
                orderby assessment.EnvironmentalCvss descending
                select new OwnerTopVulnRow(
                    assessment.Exposure.SoftwareProductId!.Value,
                    assessment.Exposure.VulnerabilityId,
                    assessment.Exposure.Vulnerability.ExternalId,
                    assessment.Exposure.Vulnerability.Title,
                    assessment.Exposure.Vulnerability.Description,
                    assessment.EnvironmentalCvss >= 9.0m ? Severity.Critical
                        : assessment.EnvironmentalCvss >= 7.0m ? Severity.High
                        : assessment.EnvironmentalCvss >= 4.0m ? Severity.Medium : Severity.Low,
                    assessment.EnvironmentalCvss
                )
            ).ToArrayAsync(ct);

        var topVulnBySoftware = topVulnPerSoftwareProduct
            .GroupBy(v => v.SoftwareProductId)
            .ToDictionary(
                g => g.Key,
                g => g.First()
            );

        var ownerActionRows = combinedRows.Select(p =>
        {
            var topVuln = topVulnBySoftware.GetValueOrDefault(p.SoftwareProductId);
            var episodeRiskScore = topVuln?.EnvironmentalCvss;
            var episodeRiskBand = episodeRiskScore.HasValue
                ? episodeRiskScore >= 9.0m ? "Critical"
                    : episodeRiskScore >= 7.0m ? "High"
                    : episodeRiskScore >= 4.0m ? "Medium" : "Low"
                : (string?)null;
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
                EpisodeRiskScore = episodeRiskScore,
                EpisodeRiskBand = episodeRiskBand,
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

        // Cloud application credential actions — apps owned by the user's teams
        // with credentials expiring within 7 days or already expired.
        var credentialSoonThreshold = DateTimeOffset.UtcNow.AddDays(7);
        var now = DateTimeOffset.UtcNow;

        var cloudAppActions = ownerScope.OwnerTeamIds.Count == 0
            ? []
            : await (
                from app in _dbContext.CloudApplications.AsNoTracking().IgnoreQueryFilters()
                join team in _dbContext.Teams.AsNoTracking() on app.OwnerTeamId equals team.Id
                where app.TenantId == tenantId
                      && app.ActiveInTenant
                      && app.OwnerTeamId != null
                      && ownerScope.OwnerTeamIds.Contains(app.OwnerTeamId!.Value)
                      && app.Credentials.Any(c => c.ExpiresAt <= credentialSoonThreshold)
                select new OwnerCloudAppActionDto(
                    app.Id,
                    app.Name,
                    app.AppId,
                    team.Name,
                    app.OwnerTeamRuleId != null ? "Rule" : "Manual",
                    app.Credentials.Count(c => c.ExpiresAt < now),
                    app.Credentials.Count(c => c.ExpiresAt >= now && c.ExpiresAt <= credentialSoonThreshold),
                    app.Credentials
                        .Where(c => c.ExpiresAt <= credentialSoonThreshold)
                        .OrderBy(c => c.ExpiresAt)
                        .Select(c => (DateTimeOffset?)c.ExpiresAt)
                        .FirstOrDefault()
                )
            ).ToListAsync(ct);

        var totalOpenCount = ownerActions.Count + cloudAppActions.Count;
        var totalOverdueCount = ownerActions.Count(item => item.DueDate.HasValue && item.DueDate.Value < now)
            + cloudAppActions.Count(item => item.ExpiredCredentialCount > 0);

        return Ok(new OwnerDashboardSummaryDto(
            ownedAssetIds.Count,
            assetsNeedingAttention,
            totalOpenCount,
            totalOverdueCount,
            topOwnedAssets,
            ownerActions,
            cloudAppActions
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
                HighCount = score != null ? score.HighCount : 0,
                MediumCount = score != null ? score.MediumCount : 0,
                LowCount = score != null ? score.LowCount : 0,
                asset.LastSeenAt,
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
                        ),
                    item.LastSeenAt,
                    item.CriticalCount,
                    item.HighCount,
                    item.MediumCount,
                    item.LowCount
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
                rc.SoftwareProductId,
                SoftwareName = sp.Name,
                task.OwnerTeamId,
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

        var remediationCaseIds = approvedPatchingTasks.Select(item => item.RemediationCaseId).Distinct().ToList();
        var activeWorkflowOwnerTeams = remediationCaseIds.Count == 0
            ? []
            : await _dbContext.RemediationWorkflows.AsNoTracking()
                .Where(workflow => workflow.TenantId == tenantId
                    && remediationCaseIds.Contains(workflow.RemediationCaseId)
                    && workflow.Status == RemediationWorkflowStatus.Active)
                .Select(workflow => new
                {
                    workflow.RemediationCaseId,
                    workflow.SoftwareOwnerTeamId,
                    workflow.UpdatedAt,
                })
                .ToListAsync(ct);
        var activeWorkflowOwnerTeamIds = activeWorkflowOwnerTeams
            .GroupBy(item => item.RemediationCaseId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.UpdatedAt)
                    .First()
                    .SoftwareOwnerTeamId);

        var softwareProductIds = approvedPatchingTasks.Select(item => item.SoftwareProductId).Distinct().ToList();
        var tenantSoftwareRows = softwareProductIds.Count == 0
            ? []
            : await _dbContext.SoftwareTenantRecords.AsNoTracking()
                .Where(item => item.TenantId == tenantId && softwareProductIds.Contains(item.SoftwareProductId))
                .Select(item => new
                {
                    item.SoftwareProductId,
                    item.OwnerTeamId,
                    item.OwnerTeamRuleId,
                    item.LastSeenAt,
                    item.UpdatedAt,
                })
                .ToListAsync(ct);
        var tenantSoftwareByProductId = tenantSoftwareRows
            .GroupBy(item => item.SoftwareProductId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.LastSeenAt)
                    .ThenByDescending(item => item.UpdatedAt)
                    .First());

        var effectiveOwnerTeamIds = approvedPatchingTasks
            .Select(item => activeWorkflowOwnerTeamIds.GetValueOrDefault(item.RemediationCaseId, item.OwnerTeamId))
            .Where(teamId => teamId != Guid.Empty)
            .Distinct()
            .ToList();
        var ownerTeamNames = effectiveOwnerTeamIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Teams.AsNoTracking()
                .Where(team => effectiveOwnerTeamIds.Contains(team.Id))
                .ToDictionaryAsync(team => team.Id, team => team.Name, ct);

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
                var hasWorkflowOwnerTeam = activeWorkflowOwnerTeamIds.TryGetValue(item.RemediationCaseId, out var workflowOwnerTeamId);
                var effectiveOwnerTeamId = hasWorkflowOwnerTeam ? workflowOwnerTeamId : item.OwnerTeamId;
                tenantSoftwareByProductId.TryGetValue(item.SoftwareProductId, out var tenantSoftware);
                var ownerAssignmentSource = !hasWorkflowOwnerTeam || tenantSoftware?.OwnerTeamId == null
                    ? "Default"
                    : tenantSoftware.OwnerTeamRuleId != null
                        ? "Rule"
                        : "Manual";
                var ownerTeamName = effectiveOwnerTeamId != Guid.Empty
                    && ownerTeamNames.TryGetValue(effectiveOwnerTeamId, out var resolvedOwnerTeamName)
                        ? resolvedOwnerTeamName
                        : "Default";
                return new ApprovedPatchingTaskDto(
                    item.Id,
                    item.RemediationDecisionId,
                    item.RemediationCaseId,
                    item.SoftwareName,
                    ownerTeamName,
                    ownerAssignmentSource,
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

        var normalizedGroupBy = NormalizeHeatmapGroupBy(groupBy);
        if (normalizedGroupBy is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Unsupported heatmap grouping.",
                Detail = "Supported groupBy values are deviceGroup, ownerTeam, businessLabel, businessService, platform, and severity.",
            });
        }

        var (filteredAssetIds, minPublishedDate) = BuildFilterContext(tenantId, filter);
        var exposureQuery = _dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(exposure => exposure.TenantId == tenantId && exposure.Status == ExposureStatus.Open);

        if (filteredAssetIds != null)
        {
            exposureQuery = exposureQuery.Where(exposure => filteredAssetIds.Contains(exposure.DeviceId));
        }

        if (minPublishedDate.HasValue)
        {
            exposureQuery = exposureQuery.Where(exposure => exposure.Vulnerability.PublishedDate >= minPublishedDate.Value);
        }

        var rows = normalizedGroupBy switch
        {
            "deviceGroup" => await BuildDeviceGroupHeatmapAsync(exposureQuery, ct),
            "ownerTeam" => await BuildOwnerTeamHeatmapAsync(exposureQuery, tenantId, ct),
            "businessLabel" => await BuildBusinessLabelHeatmapAsync(exposureQuery, tenantId, ct),
            "platform" => await BuildPlatformHeatmapAsync(exposureQuery, ct),
            "severity" => await BuildSeverityHeatmapAsync(exposureQuery, ct),
            _ => [],
        };

        return Ok(rows);
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
                ep.Exposure.VulnerabilityId,
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
            var asOfInstant = dayEndInstant <= trendWindowEnd ? dayEndInstant : trendWindowEnd;

            foreach (var severity in Enum.GetValues<Severity>())
            {
                var openOnDay = episodeRows
                    .Where(ep =>
                        ep.VendorSeverity == severity
                        && ep.FirstSeenAt <= asOfInstant
                        && (ep.ClosedAt == null || ep.ClosedAt > asOfInstant))
                    .Select(ep => ep.VulnerabilityId)
                    .Distinct()
                    .Count();

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

    private async Task<ExecutiveAccountabilitySummaryDto> BuildExecutiveAccountabilitySummaryAsync(
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var teams = await _dbContext.Teams.AsNoTracking()
            .Where(team => team.TenantId == tenantId)
            .Select(team => new { team.Id, team.Name, team.IsDefault })
            .ToListAsync(ct);
        var teamsById = teams.ToDictionary(team => team.Id);
        var defaultTeamIds = teams.Where(team => team.IsDefault).Select(team => team.Id).ToHashSet();

        var teamRiskRows = await _dbContext.TeamRiskScores.AsNoTracking()
            .Where(score => score.TenantId == tenantId)
            .Select(score => new
            {
                score.TeamId,
                score.OverallScore,
                score.CriticalEpisodeCount,
                score.HighEpisodeCount,
                score.AssetCount,
                score.OpenEpisodeCount,
            })
            .ToListAsync(ct);
        var teamRiskByTeamId = teamRiskRows.ToDictionary(row => row.TeamId);

        var deviceOwnershipRows = await _dbContext.Devices.AsNoTracking()
            .Where(device => device.TenantId == tenantId)
            .Select(device => new
            {
                device.Id,
                device.OwnerTeamId,
                device.OwnerTeamRuleId,
                device.FallbackTeamId,
            })
            .ToListAsync(ct);

        var softwareOwnershipRows = await _dbContext.SoftwareTenantRecords.AsNoTracking()
            .Where(record => record.TenantId == tenantId)
            .Select(record => new
            {
                record.SoftwareProductId,
                record.OwnerTeamId,
                record.OwnerTeamRuleId,
            })
            .ToListAsync(ct);

        var workflowRows = await _dbContext.RemediationWorkflows.AsNoTracking()
            .Where(workflow => workflow.TenantId == tenantId)
            .Select(workflow => new
            {
                workflow.Id,
                workflow.RemediationCaseId,
                workflow.SoftwareOwnerTeamId,
                workflow.CurrentStage,
                workflow.Status,
            })
            .ToListAsync(ct);
        var workflowsById = workflowRows.ToDictionary(workflow => workflow.Id);

        var caseProductRows = await _dbContext.RemediationCases.AsNoTracking()
            .Where(remediationCase => remediationCase.TenantId == tenantId)
            .Select(remediationCase => new
            {
                remediationCase.Id,
                remediationCase.SoftwareProductId,
            })
            .ToListAsync(ct);
        var caseProductByCaseId = caseProductRows.ToDictionary(row => row.Id, row => row.SoftwareProductId);
        var softwareOwnerByProductId = softwareOwnershipRows
            .Where(row => row.OwnerTeamId.HasValue)
            .GroupBy(row => row.SoftwareProductId)
            .ToDictionary(g => g.Key, g => g.First().OwnerTeamId!.Value);

        Guid? ResolveRemediationOwner(Guid? workflowId, Guid remediationCaseId)
        {
            if (workflowId.HasValue && workflowsById.TryGetValue(workflowId.Value, out var workflow))
            {
                return workflow.SoftwareOwnerTeamId;
            }

            return caseProductByCaseId.TryGetValue(remediationCaseId, out var productId)
                && softwareOwnerByProductId.TryGetValue(productId, out var ownerTeamId)
                    ? ownerTeamId
                    : null;
        }

        var overduePatchingRows = await _dbContext.PatchingTasks.AsNoTracking()
            .Where(task => task.TenantId == tenantId
                && task.Status != PatchingTaskStatus.Completed
                && task.DueDate < now)
            .Select(task => new
            {
                task.OwnerTeamId,
            })
            .ToListAsync(ct);
        var overduePatchingByTeamId = overduePatchingRows
            .GroupBy(task => task.OwnerTeamId)
            .ToDictionary(group => group.Key, group => group.Count());

        var overdueApprovalRows = await _dbContext.ApprovalTasks.AsNoTracking()
            .Where(task => task.TenantId == tenantId
                && task.Status == ApprovalTaskStatus.Pending
                && task.ExpiresAt < now)
            .Select(task => new
            {
                task.RemediationWorkflowId,
                task.RemediationCaseId,
            })
            .ToListAsync(ct);
        var overdueApprovalByTeamId = overdueApprovalRows
            .Select(row => ResolveRemediationOwner(row.RemediationWorkflowId, row.RemediationCaseId))
            .Where(teamId => teamId.HasValue)
            .GroupBy(teamId => teamId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        var awaitingDecisionByTeamId = workflowRows
            .Where(workflow => workflow.Status == RemediationWorkflowStatus.Active
                && workflow.CurrentStage == RemediationWorkflowStage.RemediationDecision)
            .GroupBy(workflow => workflow.SoftwareOwnerTeamId)
            .ToDictionary(group => group.Key, group => group.Count());

        var acceptedRiskRows = await _dbContext.RemediationDecisions.AsNoTracking()
            .Where(decision => decision.TenantId == tenantId
                && decision.Outcome == RemediationOutcome.RiskAcceptance
                && decision.ApprovalStatus == DecisionApprovalStatus.Approved)
            .Select(decision => new
            {
                decision.RemediationWorkflowId,
                decision.RemediationCaseId,
            })
            .ToListAsync(ct);
        var acceptedRiskByTeamId = acceptedRiskRows
            .Select(row => ResolveRemediationOwner(row.RemediationWorkflowId, row.RemediationCaseId))
            .Where(teamId => teamId.HasValue)
            .GroupBy(teamId => teamId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        var manualAssetsByTeamId = deviceOwnershipRows
            .Where(row => row.OwnerTeamId.HasValue
                && row.OwnerTeamRuleId is null
                && !defaultTeamIds.Contains(row.OwnerTeamId.Value))
            .GroupBy(row => row.OwnerTeamId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var ruleAssetsByTeamId = deviceOwnershipRows
            .Where(row => row.OwnerTeamId.HasValue && row.OwnerTeamRuleId is not null)
            .GroupBy(row => row.OwnerTeamId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var defaultAssetsByTeamId = deviceOwnershipRows
            .Select(row => row.OwnerTeamId ?? row.FallbackTeamId)
            .Where(teamId => teamId.HasValue && defaultTeamIds.Contains(teamId.Value))
            .GroupBy(teamId => teamId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        var manualSoftwareByTeamId = softwareOwnershipRows
            .Where(row => row.OwnerTeamId.HasValue
                && row.OwnerTeamRuleId is null
                && !defaultTeamIds.Contains(row.OwnerTeamId.Value))
            .GroupBy(row => row.OwnerTeamId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var ruleSoftwareByTeamId = softwareOwnershipRows
            .Where(row => row.OwnerTeamId.HasValue && row.OwnerTeamRuleId is not null)
            .GroupBy(row => row.OwnerTeamId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var defaultSoftwareByTeamId = softwareOwnershipRows
            .Where(row => row.OwnerTeamId.HasValue && defaultTeamIds.Contains(row.OwnerTeamId.Value))
            .GroupBy(row => row.OwnerTeamId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        var unownedAssetCount = deviceOwnershipRows.Count(row =>
            !row.OwnerTeamId.HasValue && !row.FallbackTeamId.HasValue);
        var unownedSoftwareCount = softwareOwnershipRows.Count(row => !row.OwnerTeamId.HasValue);
        var defaultRoutedAssetCount = defaultAssetsByTeamId.Values.Sum();
        var defaultRoutedSoftwareCount = defaultSoftwareByTeamId.Values.Sum();

        var teamIds = teamRiskByTeamId.Keys
            .Concat(overduePatchingByTeamId.Keys)
            .Concat(overdueApprovalByTeamId.Keys)
            .Concat(awaitingDecisionByTeamId.Keys)
            .Concat(acceptedRiskByTeamId.Keys)
            .Concat(manualAssetsByTeamId.Keys)
            .Concat(ruleAssetsByTeamId.Keys)
            .Concat(defaultAssetsByTeamId.Keys)
            .Concat(manualSoftwareByTeamId.Keys)
            .Concat(ruleSoftwareByTeamId.Keys)
            .Concat(defaultSoftwareByTeamId.Keys)
            .Distinct()
            .ToList();

        var rows = teamIds.Select(teamId =>
        {
            teamRiskByTeamId.TryGetValue(teamId, out var risk);
            var manualAssetCount = manualAssetsByTeamId.GetValueOrDefault(teamId);
            var ruleAssetCount = ruleAssetsByTeamId.GetValueOrDefault(teamId);
            var defaultAssetCount = defaultAssetsByTeamId.GetValueOrDefault(teamId);
            var manualSoftwareCount = manualSoftwareByTeamId.GetValueOrDefault(teamId);
            var ruleSoftwareCount = ruleSoftwareByTeamId.GetValueOrDefault(teamId);
            var defaultSoftwareCount = defaultSoftwareByTeamId.GetValueOrDefault(teamId);
            var ownerName = teamsById.TryGetValue(teamId, out var team) ? team.Name : "Unknown team";

            return new ExecutiveAccountabilityRowDto(
                teamId,
                ownerName,
                DescribeOwnerAssignmentSource(
                    manualAssetCount + manualSoftwareCount,
                    ruleAssetCount + ruleSoftwareCount,
                    defaultAssetCount + defaultSoftwareCount),
                risk?.OverallScore ?? 0m,
                risk?.CriticalEpisodeCount ?? 0,
                risk?.HighEpisodeCount ?? 0,
                risk?.AssetCount ?? 0,
                risk?.OpenEpisodeCount ?? 0,
                overduePatchingByTeamId.GetValueOrDefault(teamId),
                overdueApprovalByTeamId.GetValueOrDefault(teamId),
                awaitingDecisionByTeamId.GetValueOrDefault(teamId),
                acceptedRiskByTeamId.GetValueOrDefault(teamId),
                manualAssetCount,
                ruleAssetCount,
                defaultAssetCount,
                manualSoftwareCount,
                ruleSoftwareCount,
                defaultSoftwareCount,
                0,
                0);
        }).ToList();

        if (unownedAssetCount + unownedSoftwareCount > 0)
        {
            rows.Add(new ExecutiveAccountabilityRowDto(
                null,
                "Unowned",
                "Unowned",
                0m,
                0,
                0,
                unownedAssetCount,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                unownedAssetCount,
                unownedSoftwareCount));
        }

        var topOwners = rows
            .OrderByDescending(row => row.RiskScore)
            .ThenByDescending(row => row.OverdueApprovalCount + row.OverduePatchingTaskCount)
            .ThenByDescending(row => row.AwaitingDecisionCount)
            .ThenByDescending(row => row.AcceptedRiskCount)
            .ThenByDescending(row => row.UnownedAssetCount + row.UnownedSoftwareCount)
            .Take(8)
            .ToList();

        return new ExecutiveAccountabilitySummaryDto(
            unownedAssetCount,
            unownedSoftwareCount,
            defaultRoutedAssetCount,
            defaultRoutedSoftwareCount,
            awaitingDecisionByTeamId.Values.Sum(),
            overdueApprovalByTeamId.Values.Sum(),
            overduePatchingByTeamId.Values.Sum(),
            acceptedRiskByTeamId.Values.Sum(),
            topOwners);
    }

    private async Task<ExecutiveExposureSummaryDto> BuildExecutiveExposureSummaryAsync(
        Guid tenantId,
        DashboardFilterQuery filter,
        IQueryable<Guid>? filteredAssetIds,
        DateTimeOffset? minPublishedDate,
        List<DeviceGroupVulnerabilityDto> deviceGroups,
        bool hasFilters,
        CancellationToken ct)
    {
        var query = _dbContext.DeviceRiskScores.AsNoTracking()
            .Where(score => score.TenantId == tenantId);

        if (filteredAssetIds is not null)
        {
            query = query.Where(score => filteredAssetIds.Contains(score.DeviceId));
        }

        if (minPublishedDate.HasValue)
        {
            query = query.Where(score => _dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Any(exposure =>
                    exposure.TenantId == tenantId
                    && exposure.DeviceId == score.DeviceId
                    && exposure.Status == ExposureStatus.Open
                    && exposure.Vulnerability.PublishedDate >= minPublishedDate.Value));
        }

        var assetScores = await query
            .OrderByDescending(score => score.OverallScore)
            .Select(score => new RiskScoreService.AssetRiskResult(
                score.DeviceId,
                score.OverallScore,
                score.MaxEpisodeRiskScore,
                score.CriticalCount,
                score.HighCount,
                score.MediumCount,
                score.LowCount,
                score.OpenEpisodeCount,
                score.FactorsJson
            ))
            .ToListAsync(ct);

        var tenantRisk = RiskScoreService.CalculateTenantRisk(assetScores);
        var scoreDelta = hasFilters
            ? null
            : await CalculateTenantRiskDeltaAsync(tenantId, tenantRisk.OverallScore, ct);
        var trend = hasFilters ? "Filtered" : DescribeScoreTrend(scoreDelta);
        var topDriver = await FindExecutiveTopDriverAsync(
            tenantId,
            filter,
            filteredAssetIds,
            minPublishedDate,
            deviceGroups,
            ct);

        return new ExecutiveExposureSummaryDto(
            tenantRisk.OverallScore,
            DescribeRiskLevel(tenantRisk.OverallScore),
            scoreDelta,
            trend,
            hasFilters ? "Filtered" : "Tenant",
            tenantRisk.AssetCount,
            tenantRisk.CriticalAssetCount,
            tenantRisk.HighAssetCount,
            topDriver?.Title,
            topDriver?.Detail
        );
    }

    private async Task<decimal?> CalculateTenantRiskDeltaAsync(
        Guid tenantId,
        decimal currentScore,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var baseline = await _dbContext.TenantRiskScoreSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.TenantId == tenantId && snapshot.Date < today)
            .OrderByDescending(snapshot => snapshot.Date)
            .FirstOrDefaultAsync(ct);

        return baseline is null
            ? null
            : Math.Round(currentScore - baseline.OverallScore, 2);
    }

    private async Task<ExecutiveTopDriver?> FindExecutiveTopDriverAsync(
        Guid tenantId,
        DashboardFilterQuery filter,
        IQueryable<Guid>? filteredAssetIds,
        DateTimeOffset? minPublishedDate,
        List<DeviceGroupVulnerabilityDto> deviceGroups,
        CancellationToken ct)
    {
        var candidates = new List<ExecutiveTopDriver>();

        IEnumerable<DeviceGroupVulnerabilityDto> groupDriverCandidates = string.IsNullOrEmpty(filter.DeviceGroup) && filteredAssetIds is not null
            ? []
            : deviceGroups;
        var topGroup = groupDriverCandidates
            .Where(group => group.CurrentRiskScore.HasValue)
            .OrderByDescending(group => group.CurrentRiskScore)
            .FirstOrDefault();
        if (topGroup is not null)
        {
            candidates.Add(new ExecutiveTopDriver(
                $"Device group: {topGroup.DeviceGroupName}",
                $"{topGroup.OpenEpisodeCount ?? 0} open episodes across {topGroup.AssetCount ?? 0} assets.",
                topGroup.CurrentRiskScore ?? 0m));
        }

        var assetQuery =
            from score in _dbContext.DeviceRiskScores.AsNoTracking()
            join device in _dbContext.Devices.AsNoTracking()
                on score.DeviceId equals device.Id
            where score.TenantId == tenantId && device.TenantId == tenantId
            select new { score, device };

        if (filteredAssetIds is not null)
        {
            assetQuery = assetQuery.Where(item => filteredAssetIds.Contains(item.score.DeviceId));
        }

        if (minPublishedDate.HasValue)
        {
            assetQuery = assetQuery.Where(item => _dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Any(exposure =>
                    exposure.TenantId == tenantId
                    && exposure.DeviceId == item.score.DeviceId
                    && exposure.Status == ExposureStatus.Open
                    && exposure.Vulnerability.PublishedDate >= minPublishedDate.Value));
        }

        var topAsset = await assetQuery
            .OrderByDescending(item => item.score.OverallScore)
            .Select(item => new
            {
                item.device.Name,
                item.score.OverallScore,
                item.score.OpenEpisodeCount,
                item.score.CriticalCount,
                item.score.HighCount,
            })
            .FirstOrDefaultAsync(ct);
        if (topAsset is not null)
        {
            candidates.Add(new ExecutiveTopDriver(
                $"Asset: {topAsset.Name}",
                $"{topAsset.CriticalCount} critical and {topAsset.HighCount} high exposures among {topAsset.OpenEpisodeCount} open episodes.",
                topAsset.OverallScore));
        }

        var softwareQuery =
            from score in _dbContext.SoftwareRiskScores.AsNoTracking()
            join product in _dbContext.SoftwareProducts.AsNoTracking()
                on score.SoftwareProductId equals product.Id
            where score.TenantId == tenantId
            select new { score, product };

        if (filteredAssetIds is not null)
        {
            softwareQuery = softwareQuery.Where(item => _dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Any(exposure =>
                    exposure.TenantId == tenantId
                    && exposure.SoftwareProductId == item.score.SoftwareProductId
                    && exposure.Status == ExposureStatus.Open
                    && filteredAssetIds.Contains(exposure.DeviceId)));
        }

        if (minPublishedDate.HasValue)
        {
            softwareQuery = softwareQuery.Where(item => _dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Any(exposure =>
                    exposure.TenantId == tenantId
                    && exposure.SoftwareProductId == item.score.SoftwareProductId
                    && exposure.Status == ExposureStatus.Open
                    && exposure.Vulnerability.PublishedDate >= minPublishedDate.Value));
        }

        var topSoftware = await softwareQuery
            .OrderByDescending(item => item.score.OverallScore)
            .Select(item => new
            {
                item.product.Name,
                item.product.Vendor,
                item.score.OverallScore,
                item.score.OpenExposureCount,
                item.score.AffectedDeviceCount,
            })
            .FirstOrDefaultAsync(ct);
        if (topSoftware is not null)
        {
            candidates.Add(new ExecutiveTopDriver(
                $"Software: {topSoftware.Vendor} {topSoftware.Name}",
                $"{topSoftware.OpenExposureCount} open exposures across {topSoftware.AffectedDeviceCount} devices.",
                topSoftware.OverallScore));
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
    }

    private static string DescribeRiskLevel(decimal score) =>
        score switch
        {
            >= 900m => "Critical",
            >= 750m => "High",
            >= 500m => "Elevated",
            _ => "Contained",
        };

    private static string DescribeScoreTrend(decimal? delta)
    {
        if (!delta.HasValue)
        {
            return "NoBaseline";
        }

        return delta.Value switch
        {
            > 0m => "Worsening",
            < 0m => "Improving",
            _ => "Stable",
        };
    }

    private static string DescribeOwnerAssignmentSource(
        int manualCount,
        int ruleCount,
        int defaultCount)
    {
        if (defaultCount > 0 && defaultCount >= manualCount && defaultCount >= ruleCount)
        {
            return "Default";
        }

        return ruleCount > manualCount ? "Rule" : "Manual";
    }

    private static string? NormalizeHeatmapGroupBy(string? groupBy)
    {
        return groupBy?.Trim().ToLowerInvariant() switch
        {
            null or "" or "devicegroup" or "device-group" or "device_group" => "deviceGroup",
            "ownerteam" or "owner-team" or "owner_team" or "owner" or "team" => "ownerTeam",
            "businesslabel" or "business-label" or "business_label" or "businessservice" or "business-service" or "business_service" => "businessLabel",
            "platform" or "os" or "osplatform" or "os-platform" or "os_platform" => "platform",
            "severity" => "severity",
            _ => null,
        };
    }

    private static List<HeatmapRowDto> SortHeatmapRows(IEnumerable<HeatmapRowDto> rows)
    {
        return rows
            .OrderByDescending(row => row.Critical)
            .ThenByDescending(row => row.High)
            .ThenByDescending(row => row.Medium)
            .ThenByDescending(row => row.Low)
            .ThenBy(row => row.Label)
            .Take(20)
            .ToList();
    }

    private static HeatmapRowDto BuildHeatmapRow(string label, IEnumerable<HeatmapVulnerabilityCountItem> vulnerabilities)
    {
        var counts = vulnerabilities
            .GroupBy(vulnerability => new { vulnerability.VulnerabilityId, vulnerability.Severity })
            .Select(group => group.Key)
            .GroupBy(vulnerability => vulnerability.Severity)
            .ToDictionary(group => group.Key, group => group.Count());

        return new HeatmapRowDto(
            label,
            counts.GetValueOrDefault(Severity.Critical),
            counts.GetValueOrDefault(Severity.High),
            counts.GetValueOrDefault(Severity.Medium),
            counts.GetValueOrDefault(Severity.Low)
        );
    }

    private static async Task<List<HeatmapRowDto>> BuildDeviceGroupHeatmapAsync(
        IQueryable<DeviceVulnerabilityExposure> exposureQuery,
        CancellationToken ct)
    {
        var rows = await exposureQuery
            .Select(exposure => new
            {
                Label = exposure.Device.GroupName ?? "No device group",
                exposure.VulnerabilityId,
                Severity = exposure.Vulnerability.VendorSeverity,
            })
            .ToListAsync(ct);

        return SortHeatmapRows(rows
            .GroupBy(row => row.Label)
            .Select(group => BuildHeatmapRow(
                group.Key,
                group.Select(row => new HeatmapVulnerabilityCountItem(row.VulnerabilityId, row.Severity)))));
    }

    private static async Task<List<HeatmapRowDto>> BuildPlatformHeatmapAsync(
        IQueryable<DeviceVulnerabilityExposure> exposureQuery,
        CancellationToken ct)
    {
        var rows = await exposureQuery
            .Select(exposure => new
            {
                Label = exposure.Device.OsPlatform ?? "Unknown platform",
                exposure.VulnerabilityId,
                Severity = exposure.Vulnerability.VendorSeverity,
            })
            .ToListAsync(ct);

        return SortHeatmapRows(rows
            .GroupBy(row => row.Label)
            .Select(group => BuildHeatmapRow(
                group.Key,
                group.Select(row => new HeatmapVulnerabilityCountItem(row.VulnerabilityId, row.Severity)))));
    }

    private async Task<List<HeatmapRowDto>> BuildOwnerTeamHeatmapAsync(
        IQueryable<DeviceVulnerabilityExposure> exposureQuery,
        Guid tenantId,
        CancellationToken ct)
    {
        var rows = await exposureQuery
            .Select(exposure => new
            {
                TeamId = exposure.Device.OwnerTeamId ?? exposure.Device.FallbackTeamId,
                exposure.VulnerabilityId,
                Severity = exposure.Vulnerability.VendorSeverity,
            })
            .ToListAsync(ct);

        var teamIds = rows
            .Select(row => row.TeamId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var teamNamesById = teamIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Teams.AsNoTracking()
                .Where(team => team.TenantId == tenantId && teamIds.Contains(team.Id))
                .ToDictionaryAsync(team => team.Id, team => team.Name, ct);

        return SortHeatmapRows(rows
            .GroupBy(row => row.TeamId)
            .Select(group =>
            {
                var label = group.Key.HasValue && teamNamesById.TryGetValue(group.Key.Value, out var teamName)
                    ? teamName
                    : "Unowned";

                return BuildHeatmapRow(
                    label,
                    group.Select(row => new HeatmapVulnerabilityCountItem(row.VulnerabilityId, row.Severity)));
            }));
    }

    private async Task<List<HeatmapRowDto>> BuildBusinessLabelHeatmapAsync(
        IQueryable<DeviceVulnerabilityExposure> exposureQuery,
        Guid tenantId,
        CancellationToken ct)
    {
        var exposureRows = await exposureQuery
            .Select(exposure => new
            {
                exposure.DeviceId,
                exposure.VulnerabilityId,
                Severity = exposure.Vulnerability.VendorSeverity,
            })
            .ToListAsync(ct);

        if (exposureRows.Count == 0)
        {
            return [];
        }

        var deviceIds = exposureRows.Select(row => row.DeviceId).Distinct().ToList();
        var labelRows = await (
            from deviceLabel in _dbContext.DeviceBusinessLabels.AsNoTracking()
            join label in _dbContext.BusinessLabels.AsNoTracking()
                on deviceLabel.BusinessLabelId equals label.Id
            where deviceLabel.TenantId == tenantId
                  && label.TenantId == tenantId
                  && label.IsActive
                  && deviceIds.Contains(deviceLabel.DeviceId)
            select new
            {
                deviceLabel.DeviceId,
                label.Name,
            }
        ).ToListAsync(ct);

        var labelsByDeviceId = labelRows
            .GroupBy(row => row.DeviceId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Name).Distinct().ToList());

        var rows = exposureRows.SelectMany(row =>
        {
            if (!labelsByDeviceId.TryGetValue(row.DeviceId, out var labels) || labels.Count == 0)
            {
                labels = ["Unlabeled"];
            }

            return labels.Select(label => new { Label = label, row.VulnerabilityId, row.Severity });
        });

        return SortHeatmapRows(rows
            .GroupBy(row => row.Label)
            .Select(group => BuildHeatmapRow(
                group.Key,
                group.Select(row => new HeatmapVulnerabilityCountItem(row.VulnerabilityId, row.Severity)))));
    }

    private static async Task<List<HeatmapRowDto>> BuildSeverityHeatmapAsync(
        IQueryable<DeviceVulnerabilityExposure> exposureQuery,
        CancellationToken ct)
    {
        var vulnerabilities = await exposureQuery
            .Select(exposure => new HeatmapVulnerabilityCountItem(
                exposure.VulnerabilityId,
                exposure.Vulnerability.VendorSeverity))
            .ToListAsync(ct);

        var severityOrder = new[] { Severity.Critical, Severity.High, Severity.Medium, Severity.Low };
        return severityOrder
            .Select(severity => BuildHeatmapRow(
                severity.ToString(),
                vulnerabilities.Where(item => item.Severity == severity)))
            .Where(row => row.Critical + row.High + row.Medium + row.Low > 0)
            .ToList();
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

    private sealed record ExecutiveTopDriver(
        string Title,
        string Detail,
        decimal Score
    );

    private sealed record HeatmapVulnerabilityCountItem(
        Guid VulnerabilityId,
        Severity Severity
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
        Severity Severity,
        decimal EnvironmentalCvss
    );

}
