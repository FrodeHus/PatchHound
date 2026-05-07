using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Decisions;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Services;

public class RemediationDecisionQueryService(
    PatchHoundDbContext dbContext,
    SlaService slaService,
    TenantAiTextGenerationService aiTextGenerationService,
    ITenantAiConfigurationResolver aiConfigurationResolver,
    ITenantContext tenantContext
)
{
    private const string MissingAiProfileMessage =
        "Set up and enable a default AI profile for this tenant to get a plain-language risk summary here.";

    private sealed record OpenEpisodeRow(
        Guid AssetId,
        Guid VulnerabilityDefinitionId,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset? ResolvedAt
    );

    private sealed record RemediationDeviceContextRow(
        Guid AssetId,
        Criticality Criticality,
        string? DeviceValue,
        string? DeviceExposureLevel,
        EnvironmentClass? EnvironmentClass,
        InternetReachability? InternetReachability
    );

    private sealed record RemediationAiContext(
        int AffectedDeviceCount,
        int AffectedOwnerTeamCount,
        IReadOnlyList<string> AffectedOwnerTeamNames,
        int ServerCount,
        int WorkstationCount,
        int OtherEnvironmentCount,
        int InternetReachableDeviceCount,
        int HighOrCriticalDeviceCount,
        int CriticalDeviceCount,
        int HighDeviceCount,
        int MediumDeviceCount,
        int LowDeviceCount,
        string HighestAssetCriticality,
        string? BusinessValueSummary,
        bool HasApprovedPatchingPath,
        int OpenPatchingTaskCount,
        int CompletedPatchingTaskCount,
        DateTimeOffset? EarliestOpenPatchingDueDate,
        string? SlaStatus,
        DateTimeOffset? SlaDueDate,
        int TopFiveKnownExploitedCount,
        int TopFivePublicExploitCount,
        int TopFiveActiveAlertCount
    );

    private sealed record RemediationAiDraftPayload(
        string ExecutiveSummary,
        string OwnerRecommendation,
        string AnalystAssessment,
        string ExceptionRecommendation,
        string RecommendedOutcome,
        string RecommendedPriority
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

        var caseRows = await casesQuery
            .Select(c => new
            {
                c.Id,
                Name = c.SoftwareProduct.Name,
                Vendor = c.SoftwareProduct.Vendor,
            })
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var caseIds = caseRows.Select(c => c.Id).ToList();

        var decisionsLookup = await dbContext.RemediationDecisions.AsNoTracking()
            .Where(d => d.TenantId == tenantId
                && caseIds.Contains(d.RemediationCaseId)
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
            .GroupBy(d => d.RemediationCaseId)
            .Select(g => g.OrderByDescending(d => d.CreatedAt).First())
            .ToDictionaryAsync(d => d.RemediationCaseId, ct);

        // Load SoftwareProductId for each RemediationCase to join with risk scores and exposures.
        var caseToProduct = await dbContext.RemediationCases.AsNoTracking()
            .Where(c => c.TenantId == tenantId && caseIds.Contains(c.Id))
            .Select(c => new { c.Id, c.SoftwareProductId })
            .ToDictionaryAsync(c => c.Id, c => c.SoftwareProductId, ct);

        var productIds = caseToProduct.Values.Distinct().ToList();

        // Vulnerability counts and risk scores from the computed SoftwareRiskScore table.
        var softwareRiskScores = await dbContext.SoftwareRiskScores.AsNoTracking()
            .Where(s => s.TenantId == tenantId && productIds.Contains(s.SoftwareProductId))
            .ToDictionaryAsync(s => s.SoftwareProductId, ct);

        // VendorSeverity and Device.Criticality are stored as strings (HasConversion<string>),
        // so SQL-side Max((int)column) is impossible — Postgres would try to cast 'High' to
        // integer. Materialize the open-exposure rows and compute the Max enum values in
        // memory, which uses the natural int ordering of the enum.
        var openExposureRows = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                && e.SoftwareProductId != null
                && productIds.Contains(e.SoftwareProductId!.Value)
                && e.Status == ExposureStatus.Open)
            .Select(e => new
            {
                SoftwareProductId = e.SoftwareProductId!.Value,
                e.FirstObservedAt,
                Severity = e.Vulnerability.VendorSeverity,
                Criticality = e.Device.Criticality,
                DeviceId = e.DeviceId,
                VulnerabilityId = e.VulnerabilityId,
            })
            .ToListAsync(ct);

        var exposureSummaryByProduct = openExposureRows
            .GroupBy(e => e.SoftwareProductId)
            .ToDictionary(g => g.Key, g => new
            {
                SoftwareProductId = g.Key,
                EarliestFirstSeen = g.Min(e => e.FirstObservedAt),
                HighestSeverity = g.Max(e => e.Severity),
            });

        var criticalityByProduct = openExposureRows
            .GroupBy(e => e.SoftwareProductId)
            .ToDictionary(g => g.Key, g => new
            {
                SoftwareProductId = g.Key,
                MaxCriticality = g.Max(e => e.Criticality),
            });

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

        var activeWorkflowRows = await dbContext.RemediationWorkflows.AsNoTracking()
            .Where(w => w.TenantId == tenantId
                && caseIds.Contains(w.RemediationCaseId)
                && w.Status == RemediationWorkflowStatus.Active)
            .Select(w => new { w.Id, w.RemediationCaseId, w.CurrentStage, w.UpdatedAt })
            .ToListAsync(ct);
        var activeWorkflowsByCase = activeWorkflowRows
            .GroupBy(w => w.RemediationCaseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(w => w.UpdatedAt).First());
        var activeWorkflowStages = activeWorkflowsByCase
            .ToDictionary(pair => pair.Key, pair => pair.Value.CurrentStage);
        var activeWorkflowIds = activeWorkflowsByCase.Values.Select(w => w.Id).ToHashSet();
        var workflowsWithRecommendations = activeWorkflowIds.Count == 0
            ? []
            : await dbContext.AnalystRecommendations.AsNoTracking()
                .Where(r => r.TenantId == tenantId
                    && r.RemediationWorkflowId != null
                    && activeWorkflowIds.Contains(r.RemediationWorkflowId.Value))
                .Select(r => r.RemediationWorkflowId!.Value)
                .Distinct()
                .ToListAsync(ct);
        var workflowRecommendationSet = workflowsWithRecommendations.ToHashSet();
        var activeWorkflowOwnerTeams = await dbContext.RemediationWorkflows.AsNoTracking()
            .Where(w => w.TenantId == tenantId
                && caseIds.Contains(w.RemediationCaseId)
                && w.Status == RemediationWorkflowStatus.Active)
            .Select(w => new { w.RemediationCaseId, w.SoftwareOwnerTeamId, w.UpdatedAt })
            .ToListAsync(ct);
        var activeWorkflowOwnerTeamIds = activeWorkflowOwnerTeams
            .GroupBy(w => w.RemediationCaseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(w => w.UpdatedAt).First().SoftwareOwnerTeamId);

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

        var ownerTeamIds = activeWorkflowOwnerTeamIds.Values.ToHashSet();
        var ownerTeamNames = ownerTeamIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Teams.AsNoTracking()
                .Where(team => ownerTeamIds.Contains(team.Id))
                .ToDictionaryAsync(team => team.Id, team => team.Name, ct);

        // Build vulnCounts and riskScoresByCaseId keyed by RemediationCaseId.
        var vulnCounts = new Dictionary<Guid, (int Total, int Critical, int High, DateTimeOffset EarliestFirstSeen, Severity HighestSeverity)>();
        var riskScoresByCaseId = new Dictionary<Guid, (decimal OverallScore, DateTimeOffset CalculatedAt)>();
        foreach (var (caseId, productId) in caseToProduct)
        {
            if (softwareRiskScores.TryGetValue(productId, out var srs))
            {
                riskScoresByCaseId[caseId] = (srs.OverallScore, srs.CalculatedAt);
            }
            if (exposureSummaryByProduct.TryGetValue(productId, out var expSummary))
            {
                var critical = softwareRiskScores.TryGetValue(productId, out var srs2) ? srs2.CriticalExposureCount : 0;
                var high = softwareRiskScores.TryGetValue(productId, out var srs3) ? srs3.HighExposureCount : 0;
                var total = softwareRiskScores.TryGetValue(productId, out var srs4) ? srs4.OpenExposureCount : 0;
                vulnCounts[caseId] = (total, critical, high, expSummary.EarliestFirstSeen, expSummary.HighestSeverity);
            }
        }

        var tenantSla = await dbContext.TenantSlaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        var items = new List<RemediationDecisionListItemDto>();
        foreach (var rc in caseRows)
        {
            vulnCounts.TryGetValue(rc.Id, out var vc);
            var activeVulnerabilityCount = vc.Total;

            var productId = caseToProduct.TryGetValue(rc.Id, out var pid) ? pid : Guid.Empty;
            criticalityByProduct.TryGetValue(productId, out var critEntry);
            var criticality = critEntry?.MaxCriticality ?? Criticality.Low;

            if (!string.IsNullOrWhiteSpace(filter.Criticality)
                && Enum.TryParse<Criticality>(filter.Criticality, true, out var crit)
                && criticality != crit)
            {
                continue;
            }

            decisionsLookup.TryGetValue(rc.Id, out var decision);
            if (string.Equals(filter.DecisionState, "WithDecision", StringComparison.OrdinalIgnoreCase)
                && decision is null)
            {
                continue;
            }

            if (string.Equals(filter.DecisionState, "NoDecision", StringComparison.OrdinalIgnoreCase)
                && decision is not null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(filter.Outcome)
                && !string.Equals(decision?.Outcome.ToString(), filter.Outcome, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(filter.ApprovalStatus)
                && !string.Equals(decision?.ApprovalStatus.ToString(), filter.ApprovalStatus, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (filter.MissedMaintenanceWindow == true)
            {
                if (decision?.MaintenanceWindowDate is not DateTimeOffset maintenanceWindowDate
                    || maintenanceWindowDate >= DateTimeOffset.UtcNow)
                {
                    continue;
                }
            }

            if (filter.NeedsAnalystRecommendation == true)
            {
                // A case needs analyst attention when:
                //   * it has no active workflow yet (workflows are bootstrapped lazily on first
                //     recommendation/decision, so brand-new cases would otherwise be invisible), OR
                //   * its active workflow is parked at SecurityAnalysis without a recommendation.
                // Any later stage (RemediationDecision, Approval, Execution, Closure) means analyst
                // input has already happened.
                if (activeWorkflowsByCase.TryGetValue(rc.Id, out var activeWorkflow))
                {
                    if (activeWorkflow.CurrentStage != RemediationWorkflowStage.SecurityAnalysis
                        || workflowRecommendationSet.Contains(activeWorkflow.Id))
                    {
                        continue;
                    }
                }
            }

            if (filter.NeedsRemediationDecision == true)
            {
                if (!activeWorkflowsByCase.TryGetValue(rc.Id, out var activeWorkflow)
                    || activeWorkflow.CurrentStage != RemediationWorkflowStage.RemediationDecision
                    || decision is not null)
                {
                    continue;
                }
            }

            if (filter.NeedsApproval == true)
            {
                if (!activeWorkflowsByCase.TryGetValue(rc.Id, out var activeWorkflow)
                    || activeWorkflow.CurrentStage != RemediationWorkflowStage.Approval
                    || decision?.ApprovalStatus != DecisionApprovalStatus.PendingApproval)
                {
                    continue;
                }
            }

            string? riskBand = null;
            double? riskScore = null;
            if (riskScoresByCaseId.TryGetValue(rc.Id, out var risk))
            {
                riskScore = (double)risk.OverallScore;
                riskBand = risk.OverallScore switch
                {
                    >= 900m => "Critical",
                    >= 750m => "High",
                    >= 500m => "Medium",
                    > 0m => "Low",
                    _ => "None",
                };
            }

            string? slaStatus = null;
            DateTimeOffset? slaDueDate = null;
            if (tenantSla is not null && vulnCounts.TryGetValue(rc.Id, out var vcForSla) && vcForSla.HighestSeverity != default)
            {
                slaDueDate = slaService.CalculateDueDate(vcForSla.HighestSeverity, vcForSla.EarliestFirstSeen, tenantSla);
                slaStatus = slaService.GetSlaStatus(vcForSla.EarliestFirstSeen, slaDueDate.Value, DateTimeOffset.UtcNow).ToString();
            }

            var workflowStage = activeWorkflowStages.TryGetValue(rc.Id, out var stage) ? stage.ToString() : null;
            var hasWorkflowOwnerTeam = activeWorkflowOwnerTeamIds.TryGetValue(rc.Id, out var softwareOwnerTeamId);
            tenantSoftwareByProduct.TryGetValue(productId, out var tenantSoftware);
            var softwareOwnerAssignmentSource = !hasWorkflowOwnerTeam
                ? "Default"
                : tenantSoftware?.OwnerTeamRuleId != null
                    ? "Rule"
                    : "Manual";
            var softwareOwnerTeamName = hasWorkflowOwnerTeam
                && ownerTeamNames.TryGetValue(softwareOwnerTeamId, out var resolvedSoftwareOwnerTeamName)
                    ? resolvedSoftwareOwnerTeamName
                    : null;

            items.Add(new RemediationDecisionListItemDto(
                rc.Id,
                ResolveDisplaySoftwareName(rc.Name, null),
                softwareOwnerTeamName,
                softwareOwnerAssignmentSource,
                criticality.ToString(),
                decision?.Outcome.ToString(),
                decision?.ApprovalStatus.ToString(),
                decision?.DecidedAt,
                decision?.MaintenanceWindowDate,
                decision?.ExpiryDate,
                activeVulnerabilityCount,
                vulnCounts.GetValueOrDefault(rc.Id).Critical,
                vulnCounts.GetValueOrDefault(rc.Id).High,
                riskScore,
                riskBand,
                slaStatus,
                slaDueDate,
                softwareRiskScores.TryGetValue(productId, out var srsForCount) ? srsForCount.AffectedDeviceCount : 0,
                trendsByProduct.TryGetValue(productId, out var trend) ? trend : BuildEmptyTrend(),
                workflowStage
            ));
        }

        var totalCount = items.Count;
        var summary = new RemediationDecisionListSummaryDto(
            totalCount,
            items.Count(item => item.Outcome is not null),
            items.Count(item => string.Equals(item.ApprovalStatus, DecisionApprovalStatus.PendingApproval.ToString(), StringComparison.OrdinalIgnoreCase)),
            items.Count(item => item.Outcome is null)
        );
        var paged = items
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToList();

        var boundedPage = Math.Max(pagination.Page, 1);
        var boundedPageSize = Math.Max(pagination.BoundedPageSize, 1);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)boundedPageSize);

        return new RemediationDecisionListPageDto(
            paged,
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
        bool forceAiSummaryRefresh,
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

        var scopedDeviceCriticalityValues = new List<Criticality>();
        var assetCriticality = scopedDeviceCriticalityValues.Count > 0
            ? scopedDeviceCriticalityValues.Max()
            : Criticality.Low;
        var scopedDeviceDetails = scopedDeviceAssetIds.Count == 0
            ? []
            : await dbContext.Devices.AsNoTracking()
                .Where(device => device.TenantId == tenantId && scopedDeviceAssetIds.Contains(device.Id))
                .GroupJoin(
                    dbContext.SecurityProfiles.AsNoTracking(),
                    device => device.SecurityProfileId,
                    profile => profile.Id,
                    (device, profiles) => new { device, profile = profiles.FirstOrDefault() }
                )
                .Select(item => new RemediationDeviceContextRow(
                    item.device.Id,
                    item.device.Criticality,
                    item.device.DeviceValue,
                    item.device.ExposureLevel,
                    item.profile != null ? item.profile.EnvironmentClass : null,
                    item.profile != null ? item.profile.InternetReachability : null
                ))
                .ToListAsync(ct);
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
        var affectedOwnerTeamNames = affectedOwnerTeamIds.Count == 0
            ? []
            : await dbContext.Teams.AsNoTracking()
                .Where(team => affectedOwnerTeamIds.Contains(team.Id))
                .OrderBy(team => team.Name)
                .Select(team => team.Name)
                .Take(5)
                .ToListAsync(ct);
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
        var businessLabelSummary = businessLabelDtos.Count == 0
            ? []
            : businessLabelDtos
                .Take(3)
                .Select(label => $"{label.Name} ({label.AffectedDeviceCount})")
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

        var remediationAiContext = BuildRemediationAiContext(
            scopedDeviceDetails,
            assetCriticality,
            affectedOwnerTeamCount,
            affectedOwnerTeamNames,
            businessLabelSummary,
            decision,
            patchingTaskCounts?.Open ?? 0,
            patchingTaskCounts?.Completed ?? 0,
            patchingTaskCounts?.EarliestOpenDueDate,
            slaDto,
            topVulns.Take(5).ToList()
        );

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

        var aiSummary = await ResolveAiSummaryAsync(ct);

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
            aiSummary,
            threatIntel
        );
    }

    public async Task<DecisionAiSummaryDto?> RefreshAiSummaryAsync(
        Guid tenantId,
        Guid remediationCaseId,
        CancellationToken ct
    )
    {
        return (await BuildByCaseIdAsync(tenantId, remediationCaseId, true, ct))?.AiSummary;
    }

    public async Task<bool> GenerateAndStoreAiDraftsAsync(
        Guid tenantId,
        Guid remediationCaseId,
        CancellationToken ct
    )
    {
        var summary = await RefreshAiSummaryAsync(tenantId, remediationCaseId, ct);
        return summary is not null
            && (
                !string.IsNullOrWhiteSpace(summary.Content)
                || !string.IsNullOrWhiteSpace(summary.AnalystAssessment)
                || !string.IsNullOrWhiteSpace(summary.OwnerRecommendation)
                || !string.IsNullOrWhiteSpace(summary.ExceptionRecommendation)
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

    // TODO Phase 5: restore AI summary from RemediationCase → SoftwareProduct once AI fields are migrated off TenantSoftware.
    private static Task<DecisionAiSummaryDto> ResolveAiSummaryAsync(CancellationToken ct)
    {
        _ = ct;
        return Task.FromResult(new DecisionAiSummaryDto(
            null,
            null,
            null,
            null,
            null,
            null,
            "Unavailable",
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            false,
            null,
            "AI summary not available in Phase 4. TODO Phase 5."
        ));
    }

    private DecisionAiSummaryDto BuildAiSummaryDto(
        SoftwareTenantRecord tenantSoftware,
        RemediationAiJob? latestJob,
        bool canGenerate,
        bool isStale,
        string? unavailableMessage,
        string? reviewedByDisplayName
    )
    {
        var isGenerating = latestJob?.Status is RemediationAiJobStatus.Pending or RemediationAiJobStatus.Running;
        var status = ResolveAiSummaryStatus(tenantSoftware, latestJob, canGenerate, isStale);

        return new DecisionAiSummaryDto(
            NullIfWhiteSpace(tenantSoftware.RemediationAiSummaryContent),
            NullIfWhiteSpace(tenantSoftware.RemediationAiOwnerRecommendationContent),
            NullIfWhiteSpace(tenantSoftware.RemediationAiAnalystAssessmentContent),
            NullIfWhiteSpace(tenantSoftware.RemediationAiExceptionRecommendationContent),
            NullIfWhiteSpace(tenantSoftware.RemediationAiRecommendedOutcome),
            NullIfWhiteSpace(tenantSoftware.RemediationAiRecommendedPriority),
            status,
            isStale,
            NullIfWhiteSpace(tenantSoftware.RemediationAiReviewStatus),
            tenantSoftware.RemediationAiReviewedAt,
            string.IsNullOrWhiteSpace(reviewedByDisplayName) ? null : reviewedByDisplayName,
            tenantSoftware.RemediationAiSummaryGeneratedAt,
            latestJob?.RequestedAt,
            latestJob?.CompletedAt,
            NullIfWhiteSpace(tenantSoftware.RemediationAiSummaryProviderType),
            NullIfWhiteSpace(tenantSoftware.RemediationAiSummaryProfileName),
            NullIfWhiteSpace(tenantSoftware.RemediationAiSummaryModel),
            canGenerate,
            isGenerating,
            latestJob?.Status == RemediationAiJobStatus.Failed ? NullIfWhiteSpace(latestJob.Error) : null,
            unavailableMessage
        );
    }

    private static bool HasStoredAiGuidance(SoftwareTenantRecord tenantSoftware) =>
        !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiSummaryContent)
        || !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiOwnerRecommendationContent)
        || !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiAnalystAssessmentContent)
        || !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiExceptionRecommendationContent)
        || !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiRecommendedOutcome)
        || !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiRecommendedPriority);

    private static string ResolveAiSummaryStatus(
        SoftwareTenantRecord tenantSoftware,
        RemediationAiJob? latestJob,
        bool canGenerate,
        bool isStale
    )
    {
        if (latestJob?.Status == RemediationAiJobStatus.Pending)
        {
            return "Queued";
        }

        if (latestJob?.Status == RemediationAiJobStatus.Running)
        {
            return "Generating";
        }

        if (latestJob?.Status == RemediationAiJobStatus.Failed)
        {
            return "Failed";
        }

        if (isStale)
        {
            return "Stale";
        }

        if (HasStoredAiGuidance(tenantSoftware))
        {
            return "Ready";
        }

        return canGenerate ? "Missing" : "Unavailable";
    }

    private async Task<Result<(RemediationAiDraftPayload Value, AiTextGenerationResult Metadata)>> GenerateAiNarrativeAsync(
        Guid tenantId,
        string softwareName,
        DecisionSummaryDto summary,
        List<DecisionVulnDto> topVulns,
        RemediationAiContext remediationAiContext,
        CancellationToken ct
    )
    {
        var topVulnSummary = topVulns.Select(v => new
        {
            v.ExternalId,
            v.Title,
            Severity = v.EffectiveSeverity ?? v.VendorSeverity,
            RiskScore = v.EpisodeRiskScore ?? v.EffectiveScore,
            v.KnownExploited,
            v.PublicExploit,
            v.ActiveAlert,
        }).ToList();

        var request = new AiTextGenerationRequest(
            "You produce concise remediation guidance for three audiences. Return only JSON with these string fields: executiveSummary, ownerRecommendation, analystAssessment, exceptionRecommendation, recommendedOutcome, recommendedPriority. executiveSummary must be plain language for non-security readers. ownerRecommendation must give practical next steps. analystAssessment should explain the triage view and include a recommended remediation posture. exceptionRecommendation should say whether an exception looks justified and under what conditions. recommendedOutcome must be one of ApprovedForPatching, RiskAcceptance, AlternateMitigation, or PatchingDeferred. recommendedPriority must be one of Critical, High, Medium, or Low. Avoid markdown and do not invent facts.",
            JsonSerializer.Serialize(
                new
                {
                    softwareName,
                    vulnerabilitySummary = new
                    {
                        summary.TotalVulnerabilities,
                        summary.CriticalCount,
                        summary.HighCount,
                        summary.MediumCount,
                        summary.LowCount,
                        summary.WithKnownExploit,
                        summary.WithActiveAlert,
                    },
                    remediationContext = new
                    {
                        remediationAiContext.AffectedDeviceCount,
                        remediationAiContext.AffectedOwnerTeamCount,
                        remediationAiContext.AffectedOwnerTeamNames,
                        remediationAiContext.ServerCount,
                        remediationAiContext.WorkstationCount,
                        remediationAiContext.OtherEnvironmentCount,
                        remediationAiContext.InternetReachableDeviceCount,
                        remediationAiContext.HighestAssetCriticality,
                        assetCriticalityDistribution = new
                        {
                            remediationAiContext.CriticalDeviceCount,
                            remediationAiContext.HighDeviceCount,
                            remediationAiContext.MediumDeviceCount,
                            remediationAiContext.LowDeviceCount,
                            remediationAiContext.HighOrCriticalDeviceCount,
                        },
                        remediationAiContext.BusinessValueSummary,
                        patching = new
                        {
                            remediationAiContext.HasApprovedPatchingPath,
                            remediationAiContext.OpenPatchingTaskCount,
                            remediationAiContext.CompletedPatchingTaskCount,
                            remediationAiContext.EarliestOpenPatchingDueDate,
                        },
                        sla = new
                        {
                            remediationAiContext.SlaStatus,
                            remediationAiContext.SlaDueDate,
                        },
                        topFiveThreatSignals = new
                        {
                            remediationAiContext.TopFiveKnownExploitedCount,
                            remediationAiContext.TopFivePublicExploitCount,
                            remediationAiContext.TopFiveActiveAlertCount,
                        },
                    },
                    topRiskVulnerabilities = topVulnSummary,
                }
            ),
            ExternalContext: null,
            UseProviderNativeWebResearch: false
        );

        var generated = await aiTextGenerationService.GenerateAsync(tenantId, null, request, ct);
        if (!generated.IsSuccess)
        {
            return Result<(RemediationAiDraftPayload Value, AiTextGenerationResult Metadata)>.Failure(
                generated.Error ?? "AI generation failed."
            );
        }

        if (!TryParseAiDraftPayload(generated.Value.Content, out var payload))
        {
            return Result<(RemediationAiDraftPayload Value, AiTextGenerationResult Metadata)>.Failure(
                "The AI response did not contain a valid remediation draft payload."
            );
        }

        return Result<(RemediationAiDraftPayload Value, AiTextGenerationResult Metadata)>.Success((payload, generated.Value));
    }

    private static bool TryParseAiDraftPayload(
        string content,
        out RemediationAiDraftPayload payload
    )
    {
        payload = new RemediationAiDraftPayload(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                trimmed = trimmed[firstBrace..(lastBrace + 1)];
            }
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            payload = new RemediationAiDraftPayload(
                root.GetProperty("executiveSummary").GetString()?.Trim() ?? string.Empty,
                root.GetProperty("ownerRecommendation").GetString()?.Trim() ?? string.Empty,
                root.GetProperty("analystAssessment").GetString()?.Trim() ?? string.Empty,
                root.GetProperty("exceptionRecommendation").GetString()?.Trim() ?? string.Empty,
                NormalizeRecommendedOutcome(root.GetProperty("recommendedOutcome").GetString()),
                NormalizeRecommendedPriority(root.GetProperty("recommendedPriority").GetString())
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeRecommendedPriority(string? value)
    {
        return value?.Trim() switch
        {
            "Critical" => "Critical",
            "High" => "High",
            "Medium" => "Medium",
            "Low" => "Low",
            _ => string.Empty,
        };
    }

    private static string NormalizeRecommendedOutcome(string? value)
    {
        return value?.Trim() switch
        {
            nameof(RemediationOutcome.ApprovedForPatching) => nameof(RemediationOutcome.ApprovedForPatching),
            nameof(RemediationOutcome.RiskAcceptance) => nameof(RemediationOutcome.RiskAcceptance),
            nameof(RemediationOutcome.AlternateMitigation) => nameof(RemediationOutcome.AlternateMitigation),
            nameof(RemediationOutcome.PatchingDeferred) => nameof(RemediationOutcome.PatchingDeferred),
            _ => string.Empty,
        };
    }

    private static string HashAiSummaryInput(
        string softwareName,
        DecisionWorkflowStateDto workflowState,
        RemediationDecisionDto? decisionDto,
        List<AnalystRecommendationDto> recommendations,
        List<DecisionVulnDto> topVulns,
        DecisionSummaryDto summary,
        RemediationAiContext remediationAiContext
    )
    {
        var serialized = string.Join(
            "|",
            softwareName.Trim(),
            workflowState.CurrentStage,
            workflowState.CurrentStageDescription,
            decisionDto?.Outcome ?? string.Empty,
            decisionDto?.ApprovalStatus ?? string.Empty,
            decisionDto?.Justification ?? string.Empty,
            string.Join(";", recommendations.Select(item =>
                $"{item.Id}:{item.RecommendedOutcome}:{item.Rationale}:{item.PriorityOverride}:{item.CreatedAt:O}"
            )),
            string.Join(";", topVulns.Select(item =>
                $"{item.VulnerabilityId}:{item.ExternalId}:{item.EffectiveSeverity}:{item.EpisodeRiskScore}:{item.KnownExploited}:{item.PublicExploit}:{item.ActiveAlert}:{item.OverrideOutcome}"
            )),
            summary.TotalVulnerabilities,
            summary.CriticalCount,
            summary.HighCount,
            summary.MediumCount,
            summary.LowCount,
            summary.WithKnownExploit,
            summary.WithActiveAlert,
            remediationAiContext.AffectedDeviceCount,
            remediationAiContext.AffectedOwnerTeamCount,
            string.Join(",", remediationAiContext.AffectedOwnerTeamNames),
            remediationAiContext.ServerCount,
            remediationAiContext.WorkstationCount,
            remediationAiContext.OtherEnvironmentCount,
            remediationAiContext.InternetReachableDeviceCount,
            remediationAiContext.HighOrCriticalDeviceCount,
            remediationAiContext.CriticalDeviceCount,
            remediationAiContext.HighDeviceCount,
            remediationAiContext.MediumDeviceCount,
            remediationAiContext.LowDeviceCount,
            remediationAiContext.HighestAssetCriticality,
            remediationAiContext.BusinessValueSummary ?? string.Empty,
            remediationAiContext.HasApprovedPatchingPath,
            remediationAiContext.OpenPatchingTaskCount,
            remediationAiContext.CompletedPatchingTaskCount,
            remediationAiContext.EarliestOpenPatchingDueDate?.ToString("O") ?? string.Empty,
            remediationAiContext.SlaStatus ?? string.Empty,
            remediationAiContext.SlaDueDate?.ToString("O") ?? string.Empty,
            remediationAiContext.TopFiveKnownExploitedCount,
            remediationAiContext.TopFivePublicExploitCount,
            remediationAiContext.TopFiveActiveAlertCount
        );

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(serialized));
        return Convert.ToHexString(bytes);
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static RemediationAiContext BuildRemediationAiContext(
        IReadOnlyList<RemediationDeviceContextRow> scopedDeviceDetails,
        Criticality assetCriticality,
        int affectedOwnerTeamCount,
        IReadOnlyList<string> affectedOwnerTeamNames,
        IReadOnlyList<string> businessLabelSummary,
        RemediationDecision? decision,
        int openPatchingTaskCount,
        int completedPatchingTaskCount,
        DateTimeOffset? earliestOpenPatchingDueDate,
        DecisionSlaDto? slaDto,
        List<DecisionVulnDto> topVulns
    )
    {
        var details = scopedDeviceDetails.ToList();
        var businessValueSummary = details
            .Select(item => item.DeviceValue)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .GroupBy(item => item!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key} ({group.Count()})")
            .Take(3)
            .ToList();
        var businessContextSummary = new List<string>();
        if (businessLabelSummary.Count > 0)
        {
            businessContextSummary.Add($"Business labels: {string.Join(", ", businessLabelSummary)}");
        }
        if (businessValueSummary.Count > 0)
        {
            businessContextSummary.Add($"Asset values: {string.Join(", ", businessValueSummary)}");
        }

        var serverCount = details.Count(item => item.EnvironmentClass == EnvironmentClass.Server);
        var workstationCount = details.Count(item => item.EnvironmentClass == EnvironmentClass.Workstation);
        var otherEnvironmentCount = details.Count(item =>
            item.EnvironmentClass is EnvironmentClass.JumpHost or EnvironmentClass.Lab or EnvironmentClass.Kiosk or EnvironmentClass.OT
        );
        var internetReachableDeviceCount = details.Count(item =>
            item.InternetReachability == InternetReachability.Internet
        );

        return new RemediationAiContext(
            details.Count,
            affectedOwnerTeamCount,
            affectedOwnerTeamNames,
            serverCount,
            workstationCount,
            otherEnvironmentCount,
            internetReachableDeviceCount,
            details.Count(item => item.Criticality is Criticality.High or Criticality.Critical),
            details.Count(item => item.Criticality == Criticality.Critical),
            details.Count(item => item.Criticality == Criticality.High),
            details.Count(item => item.Criticality == Criticality.Medium),
            details.Count(item => item.Criticality == Criticality.Low),
            assetCriticality.ToString(),
            businessContextSummary.Count > 0 ? string.Join("; ", businessContextSummary) : null,
            decision?.Outcome == RemediationOutcome.ApprovedForPatching || openPatchingTaskCount > 0 || completedPatchingTaskCount > 0,
            openPatchingTaskCount,
            completedPatchingTaskCount,
            earliestOpenPatchingDueDate,
            slaDto?.SlaStatus,
            slaDto?.DueDate,
            topVulns.Count(item => item.KnownExploited),
            topVulns.Count(item => item.PublicExploit),
            topVulns.Count(item => item.ActiveAlert)
        );
    }
}
