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

        // Phase-2: TenantVulnerability + related legacy entities deleted — stub vulnerability counts.
        var ageBucketDefinitions = new (string Label, int MinDays, int MaxDays)[]
        {
            ("0-7 days", 0, 7),
            ("8-30 days", 8, 30),
            ("31-90 days", 31, 90),
            ("91-180 days", 91, 180),
            ("180+ days", 181, int.MaxValue),
        };
        var vulnsBySeverity = Enum.GetValues<Severity>().ToDictionary(s => s.ToString(), _ => 0);
        var vulnsByStatus = new Dictionary<string, int>
        {
            [nameof(VulnerabilityStatus.Open)] = 0,
            [nameof(VulnerabilityStatus.Resolved)] = 0,
        };
        var topVulns = new List<TopVulnerabilityDto>();
        var latestUnhandled = new List<UnhandledVulnerabilityDto>();
        var avgRemediationDays = 0m;
        var exposureScore = 0m;
        var ageBuckets = ageBucketDefinitions.Select(b => new VulnerabilityAgeBucketDto(b.Label, 0, 0, 0, 0, 0)).ToList();
        var mttrBySeverity = Enum.GetValues<Severity>().Select(s => new MttrBySeverityDto(s.ToString(), 0m, null)).ToList();

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

        var now = DateTimeOffset.UtcNow;
        var overdueCount = patchingTasks.Count(t =>
            t.Status != PatchingTaskStatus.Completed && t.DueDate < now);
        var slaPercent = patchingTasks.Count == 0
            ? 100m
            : Math.Round(
                (patchingTasks.Count - overdueCount) / (decimal)patchingTasks.Count * 100m, 1);

        var recurrence = await _dashboardQueryService.GetRecurrenceDataAsync(tenantId, ct);

        // Phase-2: VulnerabilityAssetEpisode deleted — simplified vulnsByDeviceGroup from risk scores only.
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
                item.RemediationCaseId, // Phase 4 (#17): was TenantSoftwareId
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
                _dbContext.Assets.AsNoTracking().Where(_ => false));
        }

        var currentUserId = _tenantContext.CurrentUserId;
        if (currentUserId == Guid.Empty)
        {
            return new OwnerScopeResult(
                new UnauthorizedResult(),
                Guid.Empty,
                [],
                _dbContext.Assets.AsNoTracking().Where(_ => false));
        }

        var ownerTeamIds = await _dbContext.TeamMembers.AsNoTracking()
            .Where(item => item.UserId == currentUserId)
            .Select(item => item.TeamId)
            .ToListAsync(ct);

        var ownedAssetsQuery = _dbContext.Assets.AsNoTracking()
            .Where(asset =>
                asset.TenantId == tenantId
                && (
                    asset.OwnerUserId == currentUserId
                    || (asset.OwnerTeamId != null && ownerTeamIds.Contains(asset.OwnerTeamId.Value))
                    || (asset.FallbackTeamId != null && ownerTeamIds.Contains(asset.FallbackTeamId.Value))
                )
            );

        return new OwnerScopeResult(null, tenantId, ownerTeamIds, ownedAssetsQuery);
    }

    private async Task<List<OwnerAssetSummaryDto>> BuildOwnerAssetSummariesAsync(
        Guid tenantId,
        IQueryable<Asset> ownedAssetsQuery,
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
                AssetName = asset.AssetType == AssetType.Device
                    ? asset.DeviceComputerDnsName ?? asset.Name
                    : asset.Name,
                asset.DeviceGroupName,
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
    }

    private sealed record OwnerScopeResult(
        ActionResult? Result,
        Guid TenantId,
        List<Guid> OwnerTeamIds,
        IQueryable<Asset> OwnedAssetsQuery);

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
                    item.RemediationCaseId, // Phase 4 (#17): was TenantSoftwareId
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
                RemediationCaseId = task.RemediationCaseId, // Phase 4 (#17): was TenantSoftwareId
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
                    item.RemediationCaseId, // Phase 4 (#17): was TenantSoftwareId; DTO field kept for API compat
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

        // Phase-2: VulnerabilityAssetEpisode deleted — return empty heatmap.
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

        // Phase-2: VulnerabilityAssetEpisode + TenantVulnerability deleted — return empty trends.
        _ = tenantId;
        return Ok(new TrendDataDto(new List<TrendItem>()));
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

        // Phase-2: VulnerabilityAssetEpisode deleted — return empty burndown.
        _ = tenantId;
        return Ok(new BurndownTrendDto(new List<BurndownPointDto>()));
    }

    [HttpGet("filter-options")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
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
                RemediationCaseId = decision.RemediationCaseId, // Phase 4 (#17): was TenantSoftwareId
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
                item.RemediationCaseId, // Phase 4 (#17): was TenantSoftwareId; DTO field kept for API compat
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
