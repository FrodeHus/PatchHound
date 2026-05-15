using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Decisions;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Core.Services.RiskScoring;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Services;

public class RemediationDecisionQueryService(
    PatchHoundDbContext dbContext,
    SlaService slaService,
    ITenantAiConfigurationResolver aiConfigurationResolver,
    ITenantContext tenantContext
)
{
    private sealed record OpenEpisodeRow(
        Guid AssetId,
        Guid VulnerabilityDefinitionId,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset? ResolvedAt
    );

    private sealed record DecisionListCaseRow(
        Guid Id,
        Guid SoftwareProductId,
        string Name,
        Guid? LatestDecisionId,
        RemediationOutcome? LatestOutcome,
        DecisionApprovalStatus? LatestApprovalStatus,
        DateTimeOffset? LatestDecidedAt,
        DateTimeOffset? LatestMaintenanceWindowDate,
        DateTimeOffset? LatestExpiryDate,
        Guid? ActiveWorkflowId,
        RemediationWorkflowStage? ActiveWorkflowStage,
        Guid? ActiveWorkflowOwnerTeamId,
        bool HasAnalystRecommendation,
        int CriticalityRank,
        int HighestSeverityRank,
        DateTimeOffset? EarliestFirstSeen,
        int OpenAffectedDeviceCount,
        int InstalledDeviceCount,
        decimal? RiskScore,
        int RiskAffectedDeviceCount,
        int OpenExposureCount,
        int CriticalExposureCount,
        int HighExposureCount
    );

    public async Task<RemediationDecisionListPageDto> ListAsync(
        Guid tenantId,
        RemediationDecisionFilterQuery filter,
        PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var casesQuery = dbContext.RemediationCases.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .Where(c =>
                dbContext.DeviceVulnerabilityExposures.Any(e =>
                    e.TenantId == tenantId
                    && e.SoftwareProductId == c.SoftwareProductId
                    && e.Status == ExposureStatus.Open)
                || dbContext.InstalledSoftware.Any(i =>
                    i.TenantId == tenantId
                    && i.SoftwareProductId == c.SoftwareProductId));

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            casesQuery = casesQuery.Where(c =>
                c.SoftwareProduct.Name.ToLower().Contains(term)
                || (c.SoftwareProduct.Vendor != null && c.SoftwareProduct.Vendor.ToLower().Contains(term)));
        }

        var now = DateTimeOffset.UtcNow;
        var caseMetricsQuery = casesQuery
            .Select(c => new DecisionListCaseRow(
                c.Id,
                c.SoftwareProductId,
                c.SoftwareProduct.Name,
                dbContext.RemediationDecisions.AsNoTracking()
                    .Where(d => d.TenantId == tenantId
                        && d.RemediationCaseId == c.Id
                        && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                        && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => (Guid?)d.Id)
                    .FirstOrDefault(),
                dbContext.RemediationDecisions.AsNoTracking()
                    .Where(d => d.TenantId == tenantId
                        && d.RemediationCaseId == c.Id
                        && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                        && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => (RemediationOutcome?)d.Outcome)
                    .FirstOrDefault(),
                dbContext.RemediationDecisions.AsNoTracking()
                    .Where(d => d.TenantId == tenantId
                        && d.RemediationCaseId == c.Id
                        && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                        && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => (DecisionApprovalStatus?)d.ApprovalStatus)
                    .FirstOrDefault(),
                dbContext.RemediationDecisions.AsNoTracking()
                    .Where(d => d.TenantId == tenantId
                        && d.RemediationCaseId == c.Id
                        && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                        && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => (DateTimeOffset?)d.DecidedAt)
                    .FirstOrDefault(),
                dbContext.RemediationDecisions.AsNoTracking()
                    .Where(d => d.TenantId == tenantId
                        && d.RemediationCaseId == c.Id
                        && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                        && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => d.MaintenanceWindowDate)
                    .FirstOrDefault(),
                dbContext.RemediationDecisions.AsNoTracking()
                    .Where(d => d.TenantId == tenantId
                        && d.RemediationCaseId == c.Id
                        && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                        && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => d.ExpiryDate)
                    .FirstOrDefault(),
                dbContext.RemediationWorkflows.AsNoTracking()
                    .Where(w => w.TenantId == tenantId
                        && w.RemediationCaseId == c.Id
                        && w.Status == RemediationWorkflowStatus.Active)
                    .OrderByDescending(w => w.UpdatedAt)
                    .Select(w => (Guid?)w.Id)
                    .FirstOrDefault(),
                dbContext.RemediationWorkflows.AsNoTracking()
                    .Where(w => w.TenantId == tenantId
                        && w.RemediationCaseId == c.Id
                        && w.Status == RemediationWorkflowStatus.Active)
                    .OrderByDescending(w => w.UpdatedAt)
                    .Select(w => (RemediationWorkflowStage?)w.CurrentStage)
                    .FirstOrDefault(),
                dbContext.RemediationWorkflows.AsNoTracking()
                    .Where(w => w.TenantId == tenantId
                        && w.RemediationCaseId == c.Id
                        && w.Status == RemediationWorkflowStatus.Active)
                    .OrderByDescending(w => w.UpdatedAt)
                    .Select(w => w.SoftwareOwnerTeamId)
                    .FirstOrDefault(),
                dbContext.RemediationWorkflows.AsNoTracking()
                    .Where(w => w.TenantId == tenantId
                        && w.RemediationCaseId == c.Id
                        && w.Status == RemediationWorkflowStatus.Active)
                    .OrderByDescending(w => w.UpdatedAt)
                    .Take(1)
                    .Any(w => dbContext.AnalystRecommendations.AsNoTracking()
                        .Any(r => r.TenantId == tenantId && r.RemediationWorkflowId == w.Id)),
                dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                    .Where(e => e.TenantId == tenantId
                        && e.SoftwareProductId == c.SoftwareProductId
                        && e.Status == ExposureStatus.Open)
                    .Select(e =>
                        (int?)(e.Device.Criticality == Criticality.Critical ? 4 :
                        e.Device.Criticality == Criticality.High ? 3 :
                        e.Device.Criticality == Criticality.Medium ? 2 :
                        e.Device.Criticality == Criticality.Low ? 1 : 0))
                    .Max() ?? 0,
                dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                    .Where(e => e.TenantId == tenantId
                        && e.SoftwareProductId == c.SoftwareProductId
                        && e.Status == ExposureStatus.Open)
                    .Select(e =>
                        (int?)(e.Vulnerability.VendorSeverity == Severity.Critical ? 4 :
                        e.Vulnerability.VendorSeverity == Severity.High ? 3 :
                        e.Vulnerability.VendorSeverity == Severity.Medium ? 2 :
                        e.Vulnerability.VendorSeverity == Severity.Low ? 1 : 0))
                    .Max() ?? 0,
                dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                    .Where(e => e.TenantId == tenantId
                        && e.SoftwareProductId == c.SoftwareProductId
                        && e.Status == ExposureStatus.Open)
                    .Select(e => (DateTimeOffset?)e.FirstObservedAt)
                    .Min(),
                dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                    .Where(e => e.TenantId == tenantId
                        && e.SoftwareProductId == c.SoftwareProductId
                        && e.Status == ExposureStatus.Open)
                    .Select(e => e.DeviceId)
                    .Distinct()
                    .Count(),
                dbContext.InstalledSoftware.AsNoTracking()
                    .Where(i => i.TenantId == tenantId && i.SoftwareProductId == c.SoftwareProductId)
                    .Select(i => i.DeviceId)
                    .Distinct()
                    .Count(),
                dbContext.SoftwareRiskScores.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                    .Select(s => (decimal?)s.OverallScore)
                    .FirstOrDefault(),
                dbContext.SoftwareRiskScores.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                    .Select(s => s.AffectedDeviceCount)
                    .FirstOrDefault(),
                dbContext.SoftwareRiskScores.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                    .Select(s => s.OpenExposureCount)
                    .FirstOrDefault(),
                dbContext.SoftwareRiskScores.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                    .Select(s => s.CriticalExposureCount)
                    .FirstOrDefault(),
                dbContext.SoftwareRiskScores.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                    .Select(s => s.HighExposureCount)
                    .FirstOrDefault()
            ));

        if (!string.IsNullOrWhiteSpace(filter.Criticality)
            && TryGetCriticalityRank(filter.Criticality, out var criticalityRank))
        {
            caseMetricsQuery = caseMetricsQuery.Where(c => c.CriticalityRank == criticalityRank);
        }

        if (string.Equals(filter.DecisionState, "WithDecision", StringComparison.OrdinalIgnoreCase))
        {
            caseMetricsQuery = caseMetricsQuery.Where(c => c.LatestDecisionId != null);
        }

        if (string.Equals(filter.DecisionState, "NoDecision", StringComparison.OrdinalIgnoreCase))
        {
            caseMetricsQuery = caseMetricsQuery.Where(c => c.LatestDecisionId == null);
        }

        if (!string.IsNullOrWhiteSpace(filter.Outcome)
            && Enum.TryParse<RemediationOutcome>(filter.Outcome, true, out var outcome))
        {
            caseMetricsQuery = caseMetricsQuery.Where(c => c.LatestOutcome == outcome);
        }

        if (!string.IsNullOrWhiteSpace(filter.ApprovalStatus)
            && Enum.TryParse<DecisionApprovalStatus>(filter.ApprovalStatus, true, out var approvalStatus))
        {
            caseMetricsQuery = caseMetricsQuery.Where(c => c.LatestApprovalStatus == approvalStatus);
        }

        if (filter.MissedMaintenanceWindow == true)
        {
            caseMetricsQuery = caseMetricsQuery.Where(c =>
                c.LatestMaintenanceWindowDate != null && c.LatestMaintenanceWindowDate < now);
        }

        if (filter.NeedsAnalystRecommendation == true)
        {
            caseMetricsQuery = caseMetricsQuery.Where(c =>
                c.ActiveWorkflowId == null
                || (c.ActiveWorkflowStage == RemediationWorkflowStage.SecurityAnalysis && !c.HasAnalystRecommendation));
        }

        if (filter.NeedsRemediationDecision == true)
        {
            caseMetricsQuery = caseMetricsQuery.Where(c =>
                c.ActiveWorkflowStage == RemediationWorkflowStage.RemediationDecision
                && c.LatestDecisionId == null);
        }

        if (filter.NeedsApproval == true)
        {
            caseMetricsQuery = caseMetricsQuery.Where(c =>
                c.ActiveWorkflowStage == RemediationWorkflowStage.Approval
                && c.LatestApprovalStatus == DecisionApprovalStatus.PendingApproval);
        }

        var orderedQuery = caseMetricsQuery
            .OrderByDescending(c => c.CriticalityRank)
            .ThenByDescending(c => c.RiskAffectedDeviceCount > 0
                ? c.RiskAffectedDeviceCount
                : c.OpenAffectedDeviceCount > 0
                    ? c.OpenAffectedDeviceCount
                    : c.InstalledDeviceCount)
            .ThenByDescending(c => c.CriticalExposureCount)
            .ThenByDescending(c => c.HighestSeverityRank)
            .ThenBy(c => c.Name);

        var totalCount = await orderedQuery.CountAsync(ct);
        var summary = new RemediationDecisionListSummaryDto(
            totalCount,
            await orderedQuery.CountAsync(item => item.LatestDecisionId != null, ct),
            await orderedQuery.CountAsync(item => item.LatestApprovalStatus == DecisionApprovalStatus.PendingApproval, ct),
            await orderedQuery.CountAsync(item => item.LatestDecisionId == null, ct)
        );

        var boundedPage = Math.Max(pagination.Page, 1);
        var boundedPageSize = Math.Max(pagination.BoundedPageSize, 1);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)boundedPageSize);
        var caseRows = await orderedQuery
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var productIds = caseRows.Select(c => c.SoftwareProductId).Distinct().ToList();

        var openExposureRows = productIds.Count == 0
            ? []
            : await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Where(e => e.TenantId == tenantId
                    && e.SoftwareProductId != null
                    && productIds.Contains(e.SoftwareProductId!.Value)
                    && e.Status == ExposureStatus.Open)
                .Select(e => new
                {
                    SoftwareProductId = e.SoftwareProductId!.Value,
                    DeviceId = e.DeviceId,
                    VulnerabilityId = e.VulnerabilityId,
                })
                .ToListAsync(ct);

        var deviceIdsByProduct = openExposureRows
            .GroupBy(e => e.SoftwareProductId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.DeviceId).ToHashSet());
        var vulnerabilityIdsByProduct = openExposureRows
            .GroupBy(e => e.SoftwareProductId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.VulnerabilityId).ToHashSet());
        var trendsByProduct = await BuildOpenEpisodeTrendsBySoftwareAsync(
            tenantId,
            productIds,
            deviceIdsByProduct,
            vulnerabilityIdsByProduct,
            ct
        );

        var tenantSoftwareRows = await dbContext.SoftwareTenantRecords.AsNoTracking()
            .Where(item => item.TenantId == tenantId && productIds.Contains(item.SoftwareProductId))
            .Select(item => new
            {
                item.SoftwareProductId,
                item.OwnerTeamId,
                item.OwnerTeamRuleId,
                item.LastSeenAt,
                item.UpdatedAt,
            })
            .ToListAsync(ct);
        var tenantSoftwareByProduct = tenantSoftwareRows
            .GroupBy(item => item.SoftwareProductId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(item => item.LastSeenAt)
                    .ThenByDescending(item => item.UpdatedAt)
                    .First());

        var ownerTeamIds = caseRows
            .Select(row => row.ActiveWorkflowOwnerTeamId)
            .Where(teamId => teamId.HasValue)
            .Select(teamId => teamId!.Value)
            .ToHashSet();
        var ownerTeamNames = ownerTeamIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Teams.AsNoTracking()
                .Where(team => ownerTeamIds.Contains(team.Id))
                .ToDictionaryAsync(team => team.Id, team => team.Name, ct);

        var tenantSla = await dbContext.TenantSlaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        var items = new List<RemediationDecisionListItemDto>();
        foreach (var rc in caseRows)
        {
            string? riskBand = null;
            double? riskScore = null;
            if (rc.RiskScore is decimal risk)
            {
                riskScore = (double)risk;
                riskBand = RiskBand.FromScore(risk);
            }

            string? slaStatus = null;
            DateTimeOffset? slaDueDate = null;
            if (tenantSla is not null
                && rc.EarliestFirstSeen is DateTimeOffset earliestFirstSeen
                && rc.HighestSeverityRank > 0)
            {
                var highestSeverity = SeverityFromRank(rc.HighestSeverityRank);
                slaDueDate = slaService.CalculateDueDate(highestSeverity, earliestFirstSeen, tenantSla);
                slaStatus = slaService.GetSlaStatus(earliestFirstSeen, slaDueDate.Value, DateTimeOffset.UtcNow).ToString();
            }

            var workflowStage = rc.ActiveWorkflowStage?.ToString();
            var softwareOwnerTeamId = rc.ActiveWorkflowOwnerTeamId;
            var hasWorkflowOwnerTeam = softwareOwnerTeamId.HasValue;
            tenantSoftwareByProduct.TryGetValue(rc.SoftwareProductId, out var tenantSoftware);
            var softwareOwnerAssignmentSource = !hasWorkflowOwnerTeam
                ? "Default"
                : tenantSoftware?.OwnerTeamRuleId != null
                    ? "Rule"
                    : "Manual";
            var softwareOwnerTeamName = hasWorkflowOwnerTeam
                && ownerTeamNames.TryGetValue(softwareOwnerTeamId!.Value, out var resolvedSoftwareOwnerTeamName)
                    ? resolvedSoftwareOwnerTeamName
                    : null;
            var affectedDeviceCount = rc.RiskAffectedDeviceCount > 0
                ? rc.RiskAffectedDeviceCount
                : rc.OpenAffectedDeviceCount > 0
                    ? rc.OpenAffectedDeviceCount
                    : rc.InstalledDeviceCount;

            items.Add(new RemediationDecisionListItemDto(
                rc.Id,
                ResolveDisplaySoftwareName(rc.Name, null),
                softwareOwnerTeamName,
                softwareOwnerAssignmentSource,
                CriticalityFromRank(rc.CriticalityRank).ToString(),
                rc.LatestOutcome?.ToString(),
                rc.LatestApprovalStatus?.ToString(),
                rc.LatestDecidedAt,
                rc.LatestMaintenanceWindowDate,
                rc.LatestExpiryDate,
                rc.OpenExposureCount,
                rc.CriticalExposureCount,
                rc.HighExposureCount,
                riskScore,
                riskBand,
                slaStatus,
                slaDueDate,
                affectedDeviceCount,
                trendsByProduct.TryGetValue(rc.SoftwareProductId, out var trend) ? trend : BuildEmptyTrend(),
                workflowStage
            ));
        }

        return new RemediationDecisionListPageDto(
            items,
            totalCount,
            boundedPage,
            boundedPageSize,
            totalPages,
            summary
        );
    }

    public async Task<DecisionContextDto?> BuildByCaseIdAsync(
        Guid tenantId,
        Guid remediationCaseId,
        CancellationToken ct
    )
    {
        var caseMeta = await dbContext.RemediationCases.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == remediationCaseId)
            .Select(c => new
            {
                c.SoftwareProductId,
                Name = c.SoftwareProduct.Name,
                Vendor = c.SoftwareProduct.Vendor,
                Category = c.SoftwareProduct.Category,
                ProductDescription = c.SoftwareProduct.Description,
                c.ThreatIntelSummary,
                c.ThreatIntelGeneratedAt,
                c.ThreatIntelProfileName,
            })
            .FirstOrDefaultAsync(ct);

        if (caseMeta is null)
            return null;

        var softwareName = ResolveDisplaySoftwareName(caseMeta.Name, null);
        var softwareProductId = caseMeta.SoftwareProductId;

        var tenantSoftwareInsight = await dbContext.TenantSoftwareProductInsights.AsNoTracking()
            .Where(insight => insight.TenantId == tenantId && insight.SoftwareProductId == softwareProductId)
            .Select(insight => new { insight.Description })
            .FirstOrDefaultAsync(ct);
        var softwareDescription = !string.IsNullOrWhiteSpace(tenantSoftwareInsight?.Description)
            ? tenantSoftwareInsight.Description
            : caseMeta.ProductDescription;

        var scopedDeviceAssetIds = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                && e.SoftwareProductId == softwareProductId
                && e.Status == ExposureStatus.Open)
            .Select(e => e.DeviceId)
            .Distinct()
            .ToListAsync(ct);

        var assetCriticality = Criticality.Low;
        var affectedOwnerTeamIds = scopedDeviceAssetIds.Count > 0
            ? await dbContext.Devices.AsNoTracking()
                .Where(device => device.TenantId == tenantId && scopedDeviceAssetIds.Contains(device.Id))
                .Select(device => device.OwnerTeamId ?? device.FallbackTeamId)
                .Where(teamId => teamId != null)
                .Distinct()
                .Select(teamId => teamId!.Value)
                .ToListAsync(ct)
            : [];
        var affectedOwnerTeamCount = affectedOwnerTeamIds.Count;
        var businessLabelRows = scopedDeviceAssetIds.Count == 0
            ? []
            : await dbContext.DeviceBusinessLabels.AsNoTracking()
                .Where(link => scopedDeviceAssetIds.Contains(link.DeviceId) && link.BusinessLabel.IsActive)
                .Select(link => new
                {
                    link.DeviceId,
                    link.BusinessLabel.Id,
                    link.BusinessLabel.Name,
                    link.BusinessLabel.Color,
                    link.BusinessLabel.WeightCategory,
                })
                .Distinct()
                .ToListAsync(ct);
        var businessLabelDtos = businessLabelRows
            .GroupBy(item => new { item.Id, item.Name, item.Color, item.WeightCategory })
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Name)
            .Select(group => new DecisionBusinessLabelDto(
                group.Key.Id,
                group.Key.Name,
                group.Key.Color,
                group.Key.WeightCategory.ToString(),
                (double)BusinessLabel.CategoryWeights[group.Key.WeightCategory],
                group.Count()
            ))
            .ToList();
        var patchingTaskCounts = await dbContext.PatchingTasks.AsNoTracking()
            .Where(task => task.TenantId == tenantId && task.RemediationCaseId == remediationCaseId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Open = group.Count(task => task.Status != PatchingTaskStatus.Completed),
                Completed = group.Count(task => task.Status == PatchingTaskStatus.Completed),
                EarliestOpenDueDate = group
                    .Where(task => task.Status != PatchingTaskStatus.Completed)
                    .Select(task => (DateTimeOffset?)task.DueDate)
                    .OrderBy(dueDate => dueDate)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);

        var matches = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                && e.SoftwareProductId == softwareProductId
                && e.Status == ExposureStatus.Open)
            .GroupBy(e => e.VulnerabilityId)
            .Select(g => new
            {
                Id = g.Key,
                g.First().Vulnerability.ExternalId,
                g.First().Vulnerability.Title,
                g.First().Vulnerability.Description,
                VendorSeverity = g.First().Vulnerability.VendorSeverity,
                VendorScore = g.First().Vulnerability.CvssScore,
                g.First().Vulnerability.CvssVector,
                FirstSeenAt = g.Min(e => e.FirstObservedAt),
                AffectedDeviceCount = g.Select(e => e.DeviceId).Distinct().Count(),
                AffectedVersionCount = g
                    .Select(e => e.MatchedVersion == null ? null : e.MatchedVersion.Trim().ToLower())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Distinct()
                    .Count(),
            })
            .ToListAsync(ct);

        var vulnDefIds = matches.Select(m => m.Id).ToList();

        var threats = vulnDefIds.Count == 0
            ? new Dictionary<Guid, ThreatAssessment>()
            : await dbContext.ThreatAssessments.AsNoTracking()
                .Where(t => vulnDefIds.Contains(t.VulnerabilityId))
                .ToDictionaryAsync(t => t.VulnerabilityId, ct);

        var environmentalScoresByVulnId = vulnDefIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await dbContext.ExposureAssessments.AsNoTracking()
                .Where(a => a.TenantId == tenantId
                    && a.Exposure.SoftwareProductId == softwareProductId
                    && a.Exposure.Status == ExposureStatus.Open)
                .GroupBy(a => a.Exposure.VulnerabilityId)
                .Select(g => new { VulnerabilityId = g.Key, MaxEnv = g.Max(a => a.EnvironmentalCvss) })
                .ToDictionaryAsync(x => x.VulnerabilityId, x => x.MaxEnv, ct);

        var openEpisodeTrend = await BuildOpenEpisodeTrendForScopeAsync(
            tenantId,
            scopedDeviceAssetIds,
            vulnDefIds,
            ct
        );

        var activeWorkflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .Where(workflow =>
                workflow.TenantId == tenantId
                && workflow.RemediationCaseId == remediationCaseId)
            .OrderByDescending(workflow => workflow.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var tenantSoftware = await dbContext.SoftwareTenantRecords.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.SoftwareProductId == softwareProductId)
            .OrderByDescending(item => item.LastSeenAt)
            .ThenByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        // Current decision
        var decisionQuery = dbContext.RemediationDecisions.AsNoTracking()
            .Include(d => d.VulnerabilityOverrides)
            .Where(d => d.TenantId == tenantId && d.RemediationCaseId == remediationCaseId);

        var decision = activeWorkflow is not null
            ? await decisionQuery
                .Where(d => d.RemediationWorkflowId == activeWorkflow.Id)
                .OrderByDescending(d => d.CreatedAt)
                .FirstOrDefaultAsync(ct)
            : await decisionQuery
                .Where(d =>
                    d.ApprovalStatus != DecisionApprovalStatus.Rejected
                    && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                .OrderByDescending(d => d.CreatedAt)
                .FirstOrDefaultAsync(ct);

        RemediationDecision? previousDecision = null;

        var overridesByVulnerabilityId = decision?.VulnerabilityOverrides
            .ToDictionary(vo => vo.VulnerabilityId);

        // Analyst recommendations
        var activeWorkflowId = activeWorkflow?.Id;
        var recommendations = activeWorkflowId is Guid resolvedActiveWorkflowId
            ? await dbContext.AnalystRecommendations.AsNoTracking()
                .Where(r =>
                    r.TenantId == tenantId
                    && r.RemediationWorkflowId == resolvedActiveWorkflowId)
                .OrderByDescending(r => r.CreatedAt)
                .Take(1)
                .ToListAsync(ct)
            : [];
        var recommendationAnalystIds = recommendations.Select(r => r.AnalystId).Distinct().ToList();
        var recommendationAnalystNames = recommendationAnalystIds.Count > 0
            ? await dbContext.Users.AsNoTracking()
                .Where(user => recommendationAnalystIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct)
            : new Dictionary<Guid, string>();

        var latestRejectedApproval = decision is not null
            ? await dbContext.ApprovalTasks.AsNoTracking()
                .Where(task =>
                    task.TenantId == tenantId
                    && task.RemediationDecisionId == decision.Id
                    && (task.Status == ApprovalTaskStatus.Denied || task.Status == ApprovalTaskStatus.AutoDenied))
                .OrderByDescending(task => task.ResolvedAt ?? task.UpdatedAt)
                .FirstOrDefaultAsync(ct)
            : null;
        var latestApprovalResolution = decision is not null
            ? await dbContext.ApprovalTasks.AsNoTracking()
                .Where(task =>
                    task.TenantId == tenantId
                    && task.RemediationDecisionId == decision.Id
                    && (task.Status == ApprovalTaskStatus.Approved
                        || task.Status == ApprovalTaskStatus.Denied
                        || task.Status == ApprovalTaskStatus.AutoDenied))
                .OrderByDescending(task => task.ResolvedAt ?? task.UpdatedAt)
                .Select(task => new
                {
                    Status = task.Status.ToString(),
                    task.ResolutionJustification,
                    task.ResolvedAt,
                    ResolvedByDisplayName = task.ResolvedBy == null
                        ? null
                        : dbContext.Users.AsNoTracking()
                            .Where(user => user.Id == task.ResolvedBy.Value)
                            .Select(user => user.DisplayName)
                            .FirstOrDefault(),
                })
                .FirstOrDefaultAsync(ct)
            : null;

        // Asset risk score — TODO Phase 5: restore from canonical risk scores.
        _ = await dbContext.DeviceRiskScores.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);

        // SLA configuration
        var tenantSla = await dbContext.TenantSlaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        var topVulns = matches
            .Select(m =>
            {
                threats.TryGetValue(m.Id, out var threat);
                environmentalScoresByVulnId.TryGetValue(m.Id, out var envScore);

                double? effectiveScore = envScore > 0m
                    ? (double)envScore
                    : (m.VendorScore.HasValue ? (double?)((double)m.VendorScore.Value) : null);
                string effectiveSeverity = m.VendorSeverity.ToString();

                string? overrideOutcome = null;
                if (overridesByVulnerabilityId is not null
                    && overridesByVulnerabilityId.TryGetValue(m.Id, out var vo))
                {
                    overrideOutcome = vo.Outcome.ToString();
                }

                return new DecisionVulnDto(
                    m.Id,
                    m.Id,
                    m.ExternalId,
                    m.Title,
                    m.Description,
                    m.VendorSeverity.ToString(),
                    m.VendorScore.HasValue ? (double?)((double)m.VendorScore.Value) : null,
                    effectiveSeverity,
                    effectiveScore,
                    m.CvssVector,
                    m.FirstSeenAt,
                    m.AffectedDeviceCount,
                    m.AffectedVersionCount,
                    threat?.KnownExploited ?? false,
                    threat?.PublicExploit ?? false,
                    threat?.ActiveAlert ?? false,
                    threat?.EpssScore is decimal epss ? (double?)((double)epss) : null,
                    null,
                    overrideOutcome
                );
            })
            .OrderByDescending(v => v.EffectiveScore ?? 0)
            .ToList();

        // Summary
        var summary = new DecisionSummaryDto(
            TotalVulnerabilities: topVulns.Count,
            CriticalCount: topVulns.Count(v => v.EffectiveSeverity == "Critical"),
            HighCount: topVulns.Count(v => v.EffectiveSeverity == "High"),
            MediumCount: topVulns.Count(v => v.EffectiveSeverity == "Medium"),
            LowCount: topVulns.Count(v => v.EffectiveSeverity == "Low"),
            WithKnownExploit: topVulns.Count(v => v.KnownExploited),
            WithActiveAlert: topVulns.Count(v => v.ActiveAlert)
        );

        // SLA status
        DecisionSlaDto? slaDto = null;
        if (tenantSla is not null)
        {
            var highestSeverity = matches
                .OrderByDescending(m => m.VendorSeverity)
                .Select(m => m.VendorSeverity)
                .FirstOrDefault();

            if (highestSeverity != default)
            {
                var dueDate = slaService.CalculateDueDate(
                    highestSeverity,
                    matches.Min(m => m.FirstSeenAt),
                    tenantSla
                );
                var slaStatus = slaService.GetSlaStatus(matches.Min(m => m.FirstSeenAt), dueDate, DateTimeOffset.UtcNow);

                slaDto = new DecisionSlaDto(
                    tenantSla.CriticalDays,
                    tenantSla.HighDays,
                    tenantSla.MediumDays,
                    tenantSla.LowDays,
                    slaStatus.ToString(),
                    dueDate
                );
            }
        }

        // Risk score — TODO Phase 5: restore from canonical risk model keyed by RemediationCaseId.
        DecisionRiskDto? riskDto = null;


        // Decision DTO
        RemediationDecisionDto? decisionDto = null;
        if (decision is not null)
        {
            decisionDto = new RemediationDecisionDto(
                decision.Id,
                decision.Outcome.ToString(),
                decision.ApprovalStatus.ToString(),
                decision.Justification,
                decision.DecidedBy,
                decision.DecidedAt,
                decision.ApprovedBy,
                decision.ApprovedAt,
                decision.MaintenanceWindowDate,
                decision.ExpiryDate,
                decision.ReEvaluationDate,
                latestRejectedApproval is not null
                    ? new DecisionRejectionDto(
                        latestRejectedApproval.ResolutionJustification,
                        latestRejectedApproval.ResolvedAt
                    )
                    : null,
                decision.VulnerabilityOverrides.Select(vo => new VulnerabilityOverrideDto(
                    vo.Id,
                    vo.VulnerabilityId,
                    vo.Outcome.ToString(),
                    vo.Justification,
                    vo.CreatedAt
                )).ToList()
            );
        }

        // Recommendation DTOs
        var recommendationDtos = recommendations.Select(r => new AnalystRecommendationDto(
            r.Id,
            r.VulnerabilityId,
            r.RecommendedOutcome.ToString(),
            r.Rationale,
            r.PriorityOverride,
            r.AnalystId,
            recommendationAnalystNames.GetValueOrDefault(r.AnalystId),
            r.CreatedAt
        )).ToList();

        if (activeWorkflow?.RecurrenceSourceWorkflowId is Guid recurrenceSourceWorkflowId)
        {
            previousDecision = await dbContext.RemediationDecisions.AsNoTracking()
                .Include(d => d.VulnerabilityOverrides)
                .Where(d =>
                    d.TenantId == tenantId
                    && d.RemediationWorkflowId == recurrenceSourceWorkflowId
                    && d.ApprovalStatus == DecisionApprovalStatus.Approved)
                .OrderByDescending(d => d.DecidedAt)
                .FirstOrDefaultAsync(ct);
        }

        RemediationDecisionDto? previousDecisionDto = null;
        if (previousDecision is not null)
        {
            previousDecisionDto = new RemediationDecisionDto(
                previousDecision.Id,
                previousDecision.Outcome.ToString(),
                previousDecision.ApprovalStatus.ToString(),
                previousDecision.Justification,
                previousDecision.DecidedBy,
                previousDecision.DecidedAt,
                previousDecision.ApprovedBy,
                previousDecision.ApprovedAt,
                previousDecision.MaintenanceWindowDate,
                previousDecision.ExpiryDate,
                previousDecision.ReEvaluationDate,
                null,
                previousDecision.VulnerabilityOverrides.Select(vo => new VulnerabilityOverrideDto(
                    vo.Id,
                    vo.VulnerabilityId,
                    vo.Outcome.ToString(),
                    vo.Justification,
                    vo.CreatedAt
                )).ToList()
            );
        }

        var stageRecords = activeWorkflow is not null
            ? await dbContext.RemediationWorkflowStageRecords.AsNoTracking()
                .Where(record => record.RemediationWorkflowId == activeWorkflow.Id)
                .OrderBy(record => record.StartedAt)
                .ToListAsync(ct)
            : [];

        var currentUserRoles = tenantContext
            .GetRolesForTenant(tenantId)
            .Select(role => Enum.TryParse<RoleName>(role, true, out var parsed) ? parsed : (RoleName?)null)
            .OfType<RoleName>()
            .ToHashSet();

        var currentUserTeams = await dbContext.TeamMembers.AsNoTracking()
            .Where(member => member.UserId == tenantContext.CurrentUserId && member.Team.TenantId == tenantId)
            .Select(member => new { member.TeamId, member.Team.Name })
            .ToListAsync(ct);
        var currentUserTeamIds = currentUserTeams.Select(item => item.TeamId).ToList();

        var softwareOwnerTeamId = activeWorkflow?.SoftwareOwnerTeamId;
        var softwareOwnerAssignmentSource = activeWorkflow is null
            ? "Default"
            : tenantSoftware?.OwnerTeamId == null
                ? "Default"
                : tenantSoftware.OwnerTeamRuleId != null
                    ? "Rule"
                    : "Manual";
        var softwareOwnerTeamName = softwareOwnerTeamId is Guid resolvedSoftwareOwnerTeamId
            && resolvedSoftwareOwnerTeamId != Guid.Empty
            ? await dbContext.Teams.AsNoTracking()
                .Where(team => team.Id == resolvedSoftwareOwnerTeamId)
                .Select(team => team.Name)
                .FirstOrDefaultAsync(ct)
            : null;

        var executionTeamIds = scopedDeviceAssetIds.Count > 0
            ? await dbContext.Devices.AsNoTracking()
                .Where(device => device.TenantId == tenantId && scopedDeviceAssetIds.Contains(device.Id))
                .Select(device => device.OwnerTeamId ?? device.FallbackTeamId)
                .Where(teamId => teamId != null)
                .Select(teamId => teamId!.Value)
                .Distinct()
                .ToListAsync(ct)
            : [];

        var workflowState = BuildWorkflowState(
            activeWorkflow,
            stageRecords,
            currentUserRoles,
            currentUserTeamIds,
            currentUserTeams.Select(item => item.Name).Distinct().OrderBy(name => name).ToList(),
            softwareOwnerTeamName,
            executionTeamIds,
            decision
        );

        var aiProfileAvailable = (await aiConfigurationResolver.ResolveDefaultAsync(tenantId, ct)).IsSuccess;

        var patchAssessments = await ResolvePatchAssessmentsAsync(tenantId, remediationCaseId, ct);
        var patchAssessment = patchAssessments.FirstOrDefault(assessment => assessment.Recommendation is not null)
            ?? patchAssessments.FirstOrDefault()
            ?? PatchAssessmentDtoMapper.Empty(null);

        var threatIntel = new ThreatIntelDto(
            caseMeta.ThreatIntelSummary,
            caseMeta.ThreatIntelGeneratedAt,
            caseMeta.ThreatIntelProfileName,
            CanGenerate: aiProfileAvailable,
            UnavailableMessage: aiProfileAvailable
                ? null
                : "Set up and enable a default AI profile for this tenant to retrieve threat intelligence."
        );

        return new DecisionContextDto(
            remediationCaseId,
            tenantSoftware?.Id,
            softwareName,
            caseMeta.Vendor,
            caseMeta.Category,
            softwareDescription,
            softwareOwnerTeamId,
            softwareOwnerTeamName,
            softwareOwnerAssignmentSource,
            assetCriticality.ToString(),
            businessLabelDtos,
            summary,
            new DecisionWorkflowSummaryDto(
                scopedDeviceAssetIds.Count,
                affectedOwnerTeamCount,
                patchingTaskCounts?.Open ?? 0,
                patchingTaskCounts?.Completed ?? 0,
                openEpisodeTrend
            ),
            workflowState,
            decisionDto,
            previousDecisionDto,
            latestApprovalResolution is null
                ? null
                : new DecisionApprovalResolutionDto(
                    latestApprovalResolution.Status,
                    latestApprovalResolution.ResolutionJustification,
                    latestApprovalResolution.ResolvedAt,
                    latestApprovalResolution.ResolvedByDisplayName
                ),
            recommendationDtos,
            topVulns.Take(5).ToList(),
            topVulns,
            riskDto,
            slaDto,
            patchAssessment,
            patchAssessments,
            threatIntel
        );
    }


    private static DecisionWorkflowStateDto BuildWorkflowState(
        RemediationWorkflow? workflow,
        List<RemediationWorkflowStageRecord> stageRecords,
        HashSet<RoleName> currentUserRoles,
        List<Guid> currentUserTeamIds,
        List<string> currentUserTeamNames,
        string? softwareOwnerTeamName,
        List<Guid> executionTeamIds,
        RemediationDecision? decision
    )
    {
        var isRecurrence = workflow?.RecurrenceSourceWorkflowId != null;
        var currentStage = workflow?.Status == RemediationWorkflowStatus.Completed
            ? RemediationWorkflowStage.Closure
            : workflow?.CurrentStage ?? RemediationWorkflowStage.SecurityAnalysis;

        var stageOrder = new List<RemediationWorkflowStage>();
        if (isRecurrence)
        {
            stageOrder.Add(RemediationWorkflowStage.Verification);
        }

        stageOrder.AddRange(
        [
            RemediationWorkflowStage.SecurityAnalysis,
            RemediationWorkflowStage.RemediationDecision,
            RemediationWorkflowStage.Approval,
            RemediationWorkflowStage.Execution,
            RemediationWorkflowStage.Closure,
        ]);

        var currentIndex = stageOrder.IndexOf(currentStage);
        var stageDtos = stageOrder.Select((stage, index) =>
        {
            var latestRecord = stageRecords
                .Where(record => record.Stage == stage)
                .OrderByDescending(record => record.StartedAt)
                .FirstOrDefault();

            var state = workflow?.Status == RemediationWorkflowStatus.Completed && stage == RemediationWorkflowStage.Closure
                ? "closed"
                : stage == RemediationWorkflowStage.Approval
                    && decision?.ApprovalStatus == DecisionApprovalStatus.Rejected
                    ? "rejected"
                : latestRecord?.Status switch
                {
                    RemediationWorkflowStageStatus.Skipped => "skipped",
                    RemediationWorkflowStageStatus.Completed or RemediationWorkflowStageStatus.AutoCompleted => "complete",
                    RemediationWorkflowStageStatus.InProgress when stage == currentStage => "current",
                    RemediationWorkflowStageStatus.Pending => "pending",
                    _ => index < currentIndex ? "complete" : index == currentIndex ? "current" : "pending",
                };

            return new DecisionWorkflowStageDto(
                StageId(stage),
                StageLabel(stage),
                state,
                StageDescription(stage)
            );
        }).ToList();

        return new DecisionWorkflowStateDto(
            workflow?.Id,
            StageId(currentStage),
            StageLabel(currentStage),
            StageDescription(currentStage),
            CurrentActorSummary(currentStage, workflow, softwareOwnerTeamName),
            CanActOnCurrentStage(currentStage, workflow, currentUserRoles, currentUserTeamIds, executionTeamIds),
            currentUserRoles.Select(role => role.ToString()).OrderBy(role => role).ToList(),
            currentUserTeamNames,
            ExpectedRoles(currentStage, workflow),
            ExpectedTeamName(currentStage, workflow, softwareOwnerTeamName),
            IsInExpectedTeam(currentStage, workflow, currentUserTeamIds, executionTeamIds),
            isRecurrence,
            workflow?.Status == RemediationWorkflowStatus.Active,
            stageDtos
        );
    }

    private static bool CanActOnCurrentStage(
        RemediationWorkflowStage stage,
        RemediationWorkflow? workflow,
        HashSet<RoleName> currentUserRoles,
        List<Guid> currentUserTeamIds,
        List<Guid> executionTeamIds
    )
    {
        if (currentUserRoles.Contains(RoleName.GlobalAdmin))
            return stage != RemediationWorkflowStage.Closure;

        return stage switch
        {
            RemediationWorkflowStage.Verification =>
                workflow?.ProposedOutcome switch
                {
                    RemediationOutcome.RiskAcceptance or RemediationOutcome.AlternateMitigation =>
                        currentUserRoles.Contains(RoleName.SecurityManager),
                    _ =>
                        workflow is not null && currentUserTeamIds.Contains(workflow.SoftwareOwnerTeamId),
                },
            RemediationWorkflowStage.SecurityAnalysis =>
                currentUserRoles.Contains(RoleName.SecurityManager)
                || currentUserRoles.Contains(RoleName.SecurityAnalyst),
            RemediationWorkflowStage.RemediationDecision =>
                workflow is not null && currentUserTeamIds.Contains(workflow.SoftwareOwnerTeamId),
            RemediationWorkflowStage.Approval =>
                workflow?.ApprovalMode switch
                {
                    RemediationWorkflowApprovalMode.SecurityApproval =>
                        currentUserRoles.Contains(RoleName.SecurityManager),
                    RemediationWorkflowApprovalMode.TechnicalApproval =>
                        currentUserRoles.Contains(RoleName.TechnicalManager),
                    _ => false,
                },
            RemediationWorkflowStage.Execution =>
                currentUserRoles.Contains(RoleName.TechnicalManager)
                || executionTeamIds.Any(currentUserTeamIds.Contains),
            _ => false,
        };
    }

    private static string CurrentActorSummary(
        RemediationWorkflowStage stage,
        RemediationWorkflow? workflow,
        string? softwareOwnerTeamName
    ) =>
        stage switch
        {
            RemediationWorkflowStage.Verification when workflow?.ProposedOutcome is RemediationOutcome.RiskAcceptance or RemediationOutcome.AlternateMitigation =>
                "Waiting for Security Manager or Global Admin to verify whether the previous exception still applies.",
            RemediationWorkflowStage.Verification =>
                $"Waiting for the software owner team to verify whether the previous remediation should be kept. Current owner: {softwareOwnerTeamName ?? DefaultTeamHelper.DefaultTeamName}.",
            RemediationWorkflowStage.SecurityAnalysis =>
                "Security analysis can be completed by Global Admin, Security Manager, or Security Analyst.",
            RemediationWorkflowStage.RemediationDecision =>
                $"Waiting for the software owner team to decide. Current owner: {softwareOwnerTeamName ?? DefaultTeamHelper.DefaultTeamName}.",
            RemediationWorkflowStage.Approval when workflow?.ApprovalMode == RemediationWorkflowApprovalMode.SecurityApproval =>
                "Waiting for Security Manager or Global Admin approval.",
            RemediationWorkflowStage.Approval when workflow?.ApprovalMode == RemediationWorkflowApprovalMode.TechnicalApproval =>
                "Waiting for Technical Manager or Global Admin approval.",
            RemediationWorkflowStage.Execution =>
                "Device owner teams execute patching tasks, with Technical Manager or Global Admin oversight.",
            RemediationWorkflowStage.Closure =>
                workflow?.Status == RemediationWorkflowStatus.Active
                    && workflow.ProposedOutcome is RemediationOutcome.RiskAcceptance or RemediationOutcome.AlternateMitigation
                    ? "The approved exception or alternate mitigation is the active remediation posture for this software. Execution is not applicable."
                    : "Closure is completed by the system when execution is finished and exposure is resolved.",
            _ => "This stage is ready for action.",
        };

    private static List<string> ExpectedRoles(
        RemediationWorkflowStage stage,
        RemediationWorkflow? workflow
    ) =>
        stage switch
        {
            RemediationWorkflowStage.Verification when workflow?.ProposedOutcome is RemediationOutcome.RiskAcceptance or RemediationOutcome.AlternateMitigation =>
                [RoleName.GlobalAdmin.ToString(), RoleName.SecurityManager.ToString()],
            RemediationWorkflowStage.Verification =>
                [RoleName.GlobalAdmin.ToString()],
            RemediationWorkflowStage.SecurityAnalysis =>
                [RoleName.GlobalAdmin.ToString(), RoleName.SecurityManager.ToString(), RoleName.SecurityAnalyst.ToString()],
            RemediationWorkflowStage.RemediationDecision =>
                [RoleName.GlobalAdmin.ToString()],
            RemediationWorkflowStage.Approval when workflow?.ApprovalMode == RemediationWorkflowApprovalMode.SecurityApproval =>
                [RoleName.GlobalAdmin.ToString(), RoleName.SecurityManager.ToString()],
            RemediationWorkflowStage.Approval when workflow?.ApprovalMode == RemediationWorkflowApprovalMode.TechnicalApproval =>
                [RoleName.GlobalAdmin.ToString(), RoleName.TechnicalManager.ToString()],
            RemediationWorkflowStage.Execution =>
                [RoleName.GlobalAdmin.ToString(), RoleName.TechnicalManager.ToString()],
            _ => [],
        };

    private static string? ExpectedTeamName(
        RemediationWorkflowStage stage,
        RemediationWorkflow? workflow,
        string? softwareOwnerTeamName
    ) =>
        stage switch
        {
            RemediationWorkflowStage.Verification when workflow?.ProposedOutcome is not RemediationOutcome.RiskAcceptance and not RemediationOutcome.AlternateMitigation =>
                softwareOwnerTeamName ?? DefaultTeamHelper.DefaultTeamName,
            RemediationWorkflowStage.RemediationDecision =>
                softwareOwnerTeamName ?? DefaultTeamHelper.DefaultTeamName,
            _ => null,
        };

    private static bool? IsInExpectedTeam(
        RemediationWorkflowStage stage,
        RemediationWorkflow? workflow,
        List<Guid> currentUserTeamIds,
        List<Guid> executionTeamIds
    ) =>
        stage switch
        {
            RemediationWorkflowStage.Verification when workflow?.ProposedOutcome is not RemediationOutcome.RiskAcceptance and not RemediationOutcome.AlternateMitigation =>
                workflow is not null ? currentUserTeamIds.Contains(workflow.SoftwareOwnerTeamId) : null,
            RemediationWorkflowStage.RemediationDecision =>
                workflow is not null ? currentUserTeamIds.Contains(workflow.SoftwareOwnerTeamId) : null,
            RemediationWorkflowStage.Execution =>
                executionTeamIds.Count > 0 ? executionTeamIds.Any(currentUserTeamIds.Contains) : null,
            _ => null,
        };

    private static string StageId(RemediationWorkflowStage stage) =>
        stage switch
        {
            RemediationWorkflowStage.Verification => "verification",
            RemediationWorkflowStage.SecurityAnalysis => "securityAnalysis",
            RemediationWorkflowStage.RemediationDecision => "remediationDecision",
            RemediationWorkflowStage.Approval => "approval",
            RemediationWorkflowStage.Execution => "execution",
            RemediationWorkflowStage.Closure => "closure",
            _ => "securityAnalysis",
        };

    private static string StageLabel(RemediationWorkflowStage stage) =>
        stage switch
        {
            RemediationWorkflowStage.Verification => "Verification",
            RemediationWorkflowStage.SecurityAnalysis => "Security Analysis",
            RemediationWorkflowStage.RemediationDecision => "Remediation Decision",
            RemediationWorkflowStage.Approval => "Approval",
            RemediationWorkflowStage.Execution => "Execution",
            RemediationWorkflowStage.Closure => "Closure",
            _ => "Security Analysis",
        };

    private static string StageDescription(RemediationWorkflowStage stage) =>
        stage switch
        {
            RemediationWorkflowStage.Verification => "Recurring exposure must be verified before the workflow reuses or replaces the previous remediation posture.",
            RemediationWorkflowStage.SecurityAnalysis => "Security roles review shared exposure and record a recommendation and priority.",
            RemediationWorkflowStage.RemediationDecision => "The software owner team chooses how the organization should handle the software exposure.",
            RemediationWorkflowStage.Approval => "Approvers validate the chosen posture when the decision branch requires approval.",
            RemediationWorkflowStage.Execution => "Device owner teams execute approved patching work across affected devices.",
            RemediationWorkflowStage.Closure => "Closure records the active end state of the remediation, whether patching resolved the exposure or an approved exception remains in effect.",
            _ => "The workflow is active.",
        };

    private static string ResolveDisplaySoftwareName(string? canonicalName, string? fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(canonicalName))
        {
            return canonicalName;
        }

        if (!string.IsNullOrWhiteSpace(fallbackName))
        {
            return fallbackName;
        }

        return "Unknown software";
    }

    private static bool TryGetCriticalityRank(string value, out int rank)
    {
        if (Enum.TryParse<Criticality>(value, true, out var criticality))
        {
            rank = criticality switch
            {
                Criticality.Critical => 4,
                Criticality.High => 3,
                Criticality.Medium => 2,
                Criticality.Low => 1,
                _ => 0,
            };
            return true;
        }

        rank = 0;
        return false;
    }

    private static Criticality CriticalityFromRank(int rank) =>
        rank switch
        {
            >= 4 => Criticality.Critical,
            3 => Criticality.High,
            2 => Criticality.Medium,
            _ => Criticality.Low,
        };

    private static Severity SeverityFromRank(int rank) =>
        rank switch
        {
            >= 4 => Severity.Critical,
            3 => Severity.High,
            2 => Severity.Medium,
            _ => Severity.Low,
        };

    private async Task<Dictionary<Guid, List<OpenEpisodeTrendPointDto>>> BuildOpenEpisodeTrendsBySoftwareAsync(
        Guid tenantId,
        List<Guid> tenantSoftwareIds,
        Dictionary<Guid, HashSet<Guid>> deviceIdsByTenantSoftwareId,
        Dictionary<Guid, HashSet<Guid>> vulnerabilityDefinitionIdsByTenantSoftwareId,
        CancellationToken ct
    )
    {
        if (tenantSoftwareIds.Count == 0)
        {
            return [];
        }

        var relevantDeviceIds = tenantSoftwareIds
            .SelectMany(id => deviceIdsByTenantSoftwareId.GetValueOrDefault(id) ?? [])
            .Distinct()
            .ToList();
        var relevantDefinitionIds = tenantSoftwareIds
            .SelectMany(id => vulnerabilityDefinitionIdsByTenantSoftwareId.GetValueOrDefault(id) ?? [])
            .Distinct()
            .ToList();

        if (relevantDeviceIds.Count == 0 || relevantDefinitionIds.Count == 0)
        {
            return tenantSoftwareIds.ToDictionary(id => id, _ => BuildEmptyTrend());
        }

        var episodeRows = await LoadOpenEpisodeRowsAsync(
            tenantId,
            relevantDeviceIds,
            relevantDefinitionIds,
            ct
        );

        return tenantSoftwareIds.ToDictionary(
            id => id,
            id => BuildOpenEpisodeTrend(
                episodeRows,
                deviceIdsByTenantSoftwareId.GetValueOrDefault(id) ?? [],
                vulnerabilityDefinitionIdsByTenantSoftwareId.GetValueOrDefault(id) ?? []
            )
        );
    }

    private async Task<List<OpenEpisodeTrendPointDto>> BuildOpenEpisodeTrendForScopeAsync(
        Guid tenantId,
        List<Guid> deviceAssetIds,
        List<Guid> vulnerabilityDefinitionIds,
        CancellationToken ct
    )
    {
        if (deviceAssetIds.Count == 0 || vulnerabilityDefinitionIds.Count == 0)
        {
            return BuildEmptyTrend();
        }

        var episodeRows = await LoadOpenEpisodeRowsAsync(
            tenantId,
            deviceAssetIds,
            vulnerabilityDefinitionIds,
            ct
        );

        return BuildOpenEpisodeTrend(episodeRows, deviceAssetIds, vulnerabilityDefinitionIds);
    }

    private async Task<List<OpenEpisodeRow>> LoadOpenEpisodeRowsAsync(
        Guid tenantId,
        List<Guid> deviceAssetIds,
        List<Guid> vulnerabilityDefinitionIds,
        CancellationToken ct
    )
    {
        if (deviceAssetIds.Count == 0 || vulnerabilityDefinitionIds.Count == 0)
        {
            return [];
        }

        return await dbContext.ExposureEpisodes.AsNoTracking()
            .Where(ep => ep.TenantId == tenantId
                && deviceAssetIds.Contains(ep.Exposure.DeviceId)
                && vulnerabilityDefinitionIds.Contains(ep.Exposure.VulnerabilityId))
            .Select(ep => new OpenEpisodeRow(
                ep.Exposure.DeviceId,
                ep.Exposure.VulnerabilityId,
                ep.FirstSeenAt,
                ep.ClosedAt
            ))
            .ToListAsync(ct);
    }

    private static List<OpenEpisodeTrendPointDto> BuildOpenEpisodeTrend(
        IReadOnlyList<OpenEpisodeRow> episodeRows,
        IEnumerable<Guid> deviceAssetIds,
        IEnumerable<Guid> vulnerabilityDefinitionIds
    )
    {
        var deviceAssetIdSet = deviceAssetIds.ToHashSet();
        var vulnerabilityDefinitionIdSet = vulnerabilityDefinitionIds.ToHashSet();
        if (deviceAssetIdSet.Count == 0 || vulnerabilityDefinitionIdSet.Count == 0)
        {
            return BuildEmptyTrend();
        }

        var scopedRows = episodeRows
            .Where(row =>
                deviceAssetIdSet.Contains(row.AssetId)
                && vulnerabilityDefinitionIdSet.Contains(row.VulnerabilityDefinitionId))
            .ToList();

        if (scopedRows.Count == 0)
        {
            return BuildEmptyTrend();
        }

        var start = StartOfUtcDay(DateTimeOffset.UtcNow).AddDays(-29);
        var points = new List<OpenEpisodeTrendPointDto>(30);
        for (var offset = 0; offset < 30; offset++)
        {
            var day = start.AddDays(offset);
            var nextDay = day.AddDays(1);
            var openCount = scopedRows
                .Where(row =>
                    row.FirstSeenAt < nextDay
                    && (row.ResolvedAt == null || row.ResolvedAt >= day))
                .Select(row => row.AssetId)
                .Distinct()
                .Count();
            points.Add(new OpenEpisodeTrendPointDto(day, openCount));
        }

        return points;
    }

    private static List<OpenEpisodeTrendPointDto> BuildEmptyTrend()
    {
        var start = StartOfUtcDay(DateTimeOffset.UtcNow).AddDays(-29);
        return Enumerable.Range(0, 30)
            .Select(offset => new OpenEpisodeTrendPointDto(start.AddDays(offset), 0))
            .ToList();
    }

    private static DateTimeOffset StartOfUtcDay(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private async Task<List<PatchAssessmentDto>> ResolvePatchAssessmentsAsync(
        Guid tenantId,
        Guid caseId,
        CancellationToken ct)
    {
        var candidates = await dbContext.RemediationCases.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == caseId)
            .Join(dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.Status == ExposureStatus.Open),
                c => c.SoftwareProductId,
                e => e.SoftwareProductId,
                (_, e) => e.VulnerabilityId)
            .Join(dbContext.Vulnerabilities.AsNoTracking(),
                id => id,
                v => v.Id,
                (_, v) => new { v.Id, v.VendorSeverity })
            .ToListAsync(ct);

        var uniqueCandidates = candidates
            .GroupBy(c => c.Id)
            .Select(group => group.First())
            .ToList();
        var candidateIds = uniqueCandidates.Select(c => c.Id).ToList();
        if (candidateIds.Count == 0)
            return [];

        var assessments = await dbContext.VulnerabilityPatchAssessments.AsNoTracking()
            .Where(a => candidateIds.Contains(a.VulnerabilityId))
            .ToListAsync(ct);
        var assessmentsByVulnerabilityId = assessments.ToDictionary(a => a.VulnerabilityId);

        var jobs = await dbContext.VulnerabilityAssessmentJobs.AsNoTracking()
            .Where(j => candidateIds.Contains(j.VulnerabilityId))
            .ToListAsync(ct);
        var latestJobs = jobs
            .GroupBy(j => j.VulnerabilityId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(j => j.UpdatedAt)
                    .ThenByDescending(j => j.RequestedAt)
                    .First());

        return uniqueCandidates
            .OrderByDescending(x => x.VendorSeverity)
            .ThenBy(x => x.Id)
            .Select(candidate => assessmentsByVulnerabilityId.TryGetValue(candidate.Id, out var assessment)
                ? PatchAssessmentDtoMapper.FromAssessment(assessment)
                : PatchAssessmentDtoMapper.FromJob(
                    candidate.Id,
                    latestJobs.GetValueOrDefault(candidate.Id)))
            .ToList();
    }
}
