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
    TenantSnapshotResolver snapshotResolver,
    SlaService slaService,
    TenantAiTextGenerationService aiTextGenerationService,
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
        var activeSnapshotId = await snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(tenantId, ct);
        var tenantSoftwareQuery = dbContext.TenantSoftware.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.SnapshotId == activeSnapshotId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            tenantSoftwareQuery = tenantSoftwareQuery.Where(item =>
                item.NormalizedSoftware.CanonicalName.ToLower().Contains(term)
                || (item.NormalizedSoftware.CanonicalVendor != null
                    && item.NormalizedSoftware.CanonicalVendor.ToLower().Contains(term)));
        }

        var tenantSoftwareRows = await tenantSoftwareQuery
            .Select(item => new
            {
                item.Id,
                Name = item.NormalizedSoftware.CanonicalName,
                item.NormalizedSoftware.CanonicalVendor,
            })
            .OrderBy(item => item.Name)
            .ToListAsync(ct);

        var tenantSoftwareIds = tenantSoftwareRows.Select(item => item.Id).ToList();
        var activeInstallations = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.IsActive && tenantSoftwareIds.Contains(item.TenantSoftwareId))
            .Select(item => new { item.TenantSoftwareId, item.SoftwareAssetId, item.DeviceAssetId })
            .ToListAsync(ct);
        var softwareAssetIds = activeInstallations.Select(item => item.SoftwareAssetId).Distinct().ToList();
        var deviceAssetIds = activeInstallations.Select(item => item.DeviceAssetId).Distinct().ToList();
        var softwareAssetNamesById = await dbContext.Assets.AsNoTracking()
            .Where(item => item.TenantId == tenantId && softwareAssetIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Name })
            .ToDictionaryAsync(item => item.Id, item => item.Name, ct);
        var deviceAssets = await dbContext.Assets.AsNoTracking()
            .Where(item => item.TenantId == tenantId && deviceAssetIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Criticality })
            .ToListAsync(ct);
        var deviceAssetsById = deviceAssets.ToDictionary(item => item.Id);
        var representativeAssetByTenantSoftwareId = activeInstallations
            .GroupBy(item => item.TenantSoftwareId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.SoftwareAssetId).Distinct().OrderBy(id => id).FirstOrDefault()
            );
        var highestCriticalityByTenantSoftwareId = activeInstallations
            .GroupBy(item => item.TenantSoftwareId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => deviceAssetsById.GetValueOrDefault(item.DeviceAssetId)?.Criticality ?? Criticality.Low)
                    .DefaultIfEmpty(Criticality.Low)
                    .Max()
            );
        var deviceCountByTenantSoftwareId = activeInstallations
            .GroupBy(item => item.TenantSoftwareId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.DeviceAssetId).Distinct().Count());
        var deviceIdsByTenantSoftwareId = activeInstallations
            .GroupBy(item => item.TenantSoftwareId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.DeviceAssetId).Distinct().ToHashSet()
            );

        var decisionsLookup = await dbContext.RemediationDecisions.AsNoTracking()
            .Where(d => d.TenantId == tenantId
                && tenantSoftwareIds.Contains(d.TenantSoftwareId)
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
            .GroupBy(d => d.TenantSoftwareId)
            .Select(g => g.OrderByDescending(d => d.CreatedAt).First())
            .ToDictionaryAsync(d => d.TenantSoftwareId, ct);

        var openMatchRows = await dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(m =>
                m.TenantId == tenantId
                && m.ResolvedAt == null
                && m.SnapshotId == activeSnapshotId
                && softwareAssetIds.Contains(m.SoftwareAssetId))
            .Join(
                dbContext.VulnerabilityDefinitions.AsNoTracking(),
                m => m.VulnerabilityDefinitionId,
                v => v.Id,
                (m, v) => new { m.SoftwareAssetId, m.FirstSeenAt, m.VulnerabilityDefinitionId, v.VendorSeverity }
            )
            .ToListAsync(ct);

        var tenantSoftwareIdBySoftwareAssetId = activeInstallations
            .GroupBy(item => item.SoftwareAssetId)
            .ToDictionary(group => group.Key, group => group.First().TenantSoftwareId);
        var vulnerabilityDefinitionIdsByTenantSoftwareId = openMatchRows
            .Where(item => tenantSoftwareIdBySoftwareAssetId.ContainsKey(item.SoftwareAssetId))
            .GroupBy(item => tenantSoftwareIdBySoftwareAssetId[item.SoftwareAssetId])
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.VulnerabilityDefinitionId).Distinct().ToHashSet()
            );

        var vulnCounts = openMatchRows
            .Where(item => tenantSoftwareIdBySoftwareAssetId.ContainsKey(item.SoftwareAssetId))
            .GroupBy(item => tenantSoftwareIdBySoftwareAssetId[item.SoftwareAssetId])
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Total = group.Select(item => item.VulnerabilityDefinitionId).Distinct().Count(),
                    Critical = group.Where(item => item.VendorSeverity == Severity.Critical).Select(item => item.VulnerabilityDefinitionId).Distinct().Count(),
                    High = group.Where(item => item.VendorSeverity == Severity.High).Select(item => item.VulnerabilityDefinitionId).Distinct().Count(),
                    EarliestFirstSeen = group.Min(item => item.FirstSeenAt),
                    HighestSeverity = group.Max(item => item.VendorSeverity),
                });

        var riskScoresByTsId = await dbContext.TenantSoftwareRiskScores.AsNoTracking()
            .Where(r => r.TenantId == tenantId && tenantSoftwareIds.Contains(r.TenantSoftwareId))
            .ToDictionaryAsync(r => r.TenantSoftwareId, ct);

        var tenantSla = await dbContext.TenantSlaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        var items = new List<RemediationDecisionListItemDto>();
        foreach (var software in tenantSoftwareRows)
        {
            var activeVulnerabilityCount = vulnCounts.GetValueOrDefault(software.Id)?.Total ?? 0;
            if (activeVulnerabilityCount == 0)
            {
                continue;
            }

            if (!highestCriticalityByTenantSoftwareId.TryGetValue(software.Id, out var criticality))
                criticality = Criticality.Low;

            if (!string.IsNullOrWhiteSpace(filter.Criticality)
                && Enum.TryParse<Criticality>(filter.Criticality, true, out var crit)
                && criticality != crit)
            {
                continue;
            }

            decisionsLookup.TryGetValue(software.Id, out var decision);
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

            string? riskBand = null;
            double? riskScore = null;
            if (riskScoresByTsId.TryGetValue(software.Id, out var risk))
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
            if (tenantSla is not null && vulnCounts.TryGetValue(software.Id, out var vcForSla) && vcForSla.HighestSeverity != default)
            {
                slaDueDate = slaService.CalculateDueDate(vcForSla.HighestSeverity, vcForSla.EarliestFirstSeen, tenantSla);
                slaStatus = slaService.GetSlaStatus(vcForSla.EarliestFirstSeen, slaDueDate.Value, DateTimeOffset.UtcNow).ToString();
            }

            items.Add(new RemediationDecisionListItemDto(
                software.Id,
                ResolveDisplaySoftwareName(
                    software.Name,
                    representativeAssetByTenantSoftwareId.TryGetValue(software.Id, out var representativeSoftwareAssetId)
                        && representativeSoftwareAssetId != Guid.Empty
                        && softwareAssetNamesById.TryGetValue(representativeSoftwareAssetId, out var representativeSoftwareName)
                        ? representativeSoftwareName
                        : null
                ),
                criticality.ToString(),
                decision?.Outcome.ToString(),
                decision?.ApprovalStatus.ToString(),
                decision?.DecidedAt,
                decision?.MaintenanceWindowDate,
                decision?.ExpiryDate,
                activeVulnerabilityCount,
                vulnCounts.GetValueOrDefault(software.Id)?.Critical ?? 0,
                vulnCounts.GetValueOrDefault(software.Id)?.High ?? 0,
                riskScore,
                riskBand,
                slaStatus,
                slaDueDate,
                deviceCountByTenantSoftwareId.GetValueOrDefault(software.Id),
                BuildEmptyTrend()
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

        var pagedEpisodeTrendBySoftwareId = await BuildOpenEpisodeTrendsBySoftwareAsync(
            tenantId,
            paged.Select(item => item.TenantSoftwareId).ToList(),
            deviceIdsByTenantSoftwareId,
            vulnerabilityDefinitionIdsByTenantSoftwareId,
            ct
        );

        paged = paged
            .Select(item => item with
            {
                OpenEpisodeTrend = pagedEpisodeTrendBySoftwareId.GetValueOrDefault(item.TenantSoftwareId) ?? BuildEmptyTrend()
            })
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

    public async Task<DecisionContextDto?> BuildAsync(
        Guid tenantId,
        Guid assetId,
        CancellationToken ct
    )
    {
        var tenantSoftwareId = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.SoftwareAssetId == assetId && item.IsActive)
            .Select(item => item.TenantSoftwareId)
            .FirstOrDefaultAsync(ct);
        if (tenantSoftwareId == Guid.Empty)
            return null;

        return await BuildByTenantSoftwareAsync(tenantId, tenantSoftwareId, false, ct);
    }

    public async Task<DecisionContextDto?> BuildByTenantSoftwareAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        bool forceAiSummaryRefresh,
        CancellationToken ct
    )
    {
        var tenantSoftwareMeta = await dbContext.TenantSoftware.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Id == tenantSoftwareId)
            .Select(item => new
            {
                Name = item.NormalizedSoftware.CanonicalName,
                Vendor = item.NormalizedSoftware.CanonicalVendor,
            })
            .FirstOrDefaultAsync(ct);

        var representativeAsset = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.TenantSoftwareId == tenantSoftwareId
                && item.IsActive)
            .Join(
                dbContext.Assets.AsNoTracking(),
                item => item.SoftwareAssetId,
                asset => asset.Id,
                (item, asset) => new { asset.Id, asset.Name, asset.Criticality }
            )
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(ct);

        if (representativeAsset is null && tenantSoftwareMeta is null)
            return null;

        var assetId = representativeAsset?.Id ?? tenantSoftwareId;
        var softwareName = ResolveDisplaySoftwareName(tenantSoftwareMeta?.Name, representativeAsset?.Name);

        var scopedSoftwareAssetIds = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.TenantSoftwareId == tenantSoftwareId && item.IsActive)
            .Select(item => item.SoftwareAssetId)
            .Distinct()
            .ToListAsync(ct);
        var scopedInstallations = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.TenantSoftwareId == tenantSoftwareId && item.IsActive)
            .Select(item => new { item.DeviceAssetId })
            .ToListAsync(ct);
        var scopedDeviceAssetIds = scopedInstallations.Select(item => item.DeviceAssetId).Distinct().ToList();
        var scopedDeviceCriticalityValues = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.TenantSoftwareId == tenantSoftwareId && item.IsActive)
            .Join(
                dbContext.Assets.AsNoTracking(),
                item => item.DeviceAssetId,
                asset => asset.Id,
                (item, asset) => asset.Criticality
            )
            .ToListAsync(ct);
        var assetCriticality = scopedDeviceCriticalityValues.Count > 0
            ? scopedDeviceCriticalityValues.Max()
            : Criticality.Low;
        var scopedDeviceDetails = scopedDeviceAssetIds.Count == 0
            ? []
            : await dbContext.Assets.AsNoTracking()
                .Where(asset => asset.TenantId == tenantId && scopedDeviceAssetIds.Contains(asset.Id))
                .GroupJoin(
                    dbContext.AssetSecurityProfiles.AsNoTracking(),
                    asset => asset.SecurityProfileId,
                    profile => profile.Id,
                    (asset, profiles) => new { asset, profile = profiles.FirstOrDefault() }
                )
                .Select(item => new RemediationDeviceContextRow(
                    item.asset.Id,
                    item.asset.Criticality,
                    item.asset.DeviceValue,
                    item.asset.DeviceExposureLevel,
                    item.profile != null ? item.profile.EnvironmentClass : null,
                    item.profile != null ? item.profile.InternetReachability : null
                ))
                .ToListAsync(ct);
        var affectedOwnerTeamIds = scopedDeviceAssetIds.Count > 0
            ? await dbContext.Assets.AsNoTracking()
                .Where(asset => asset.TenantId == tenantId && scopedDeviceAssetIds.Contains(asset.Id))
                .Select(asset => asset.OwnerTeamId ?? asset.FallbackTeamId)
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
        var businessLabelSummary = scopedDeviceAssetIds.Count == 0
            ? []
            : await dbContext.AssetBusinessLabels.AsNoTracking()
                .Where(link => scopedDeviceAssetIds.Contains(link.AssetId))
                .Select(link => new { link.AssetId, link.BusinessLabel.Name })
                .Distinct()
                .GroupBy(item => item.Name)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => $"{group.Key} ({group.Count()})")
                .Take(3)
                .ToListAsync(ct);
        var patchingTaskCounts = await dbContext.PatchingTasks.AsNoTracking()
            .Where(task => task.TenantId == tenantId && task.TenantSoftwareId == tenantSoftwareId)
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

        var activeSnapshotId = await snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(tenantId, ct);

        // Load vulnerability matches
        var matches = await dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(m =>
                scopedSoftwareAssetIds.Contains(m.SoftwareAssetId)
                && m.TenantId == tenantId
                && m.ResolvedAt == null
                && m.SnapshotId == activeSnapshotId
            )
            .Join(
                dbContext.VulnerabilityDefinitions.AsNoTracking(),
                m => m.VulnerabilityDefinitionId,
                v => v.Id,
                (m, v) => new
                {
                    v.Id,
                    v.ExternalId,
                    v.Title,
                    v.VendorSeverity,
                    VendorScore = v.CvssScore.HasValue ? (double?)((double)v.CvssScore.Value) : null,
                    v.CvssVector,
                    m.FirstSeenAt,
                }
            )
            .ToListAsync(ct);

        var vulnDefIds = matches.Select(m => m.Id).Distinct().ToList();

        // Threat assessments
        var threats = await dbContext.VulnerabilityThreatAssessments.AsNoTracking()
            .Where(t => vulnDefIds.Contains(t.VulnerabilityDefinitionId))
            .ToDictionaryAsync(t => t.VulnerabilityDefinitionId, ct);

        // TenantVulnerability lookup
        var tenantVulnLookup = await dbContext.TenantVulnerabilities.AsNoTracking()
            .Where(tv => tv.TenantId == tenantId && vulnDefIds.Contains(tv.VulnerabilityDefinitionId))
            .Select(tv => new { tv.Id, tv.VulnerabilityDefinitionId })
            .ToListAsync(ct);

        var tenantVulnIdByDefId = tenantVulnLookup
            .GroupBy(x => x.VulnerabilityDefinitionId)
            .ToDictionary(g => g.Key, g => g.First().Id);
        var allTenantVulnIds = tenantVulnLookup.Select(x => x.Id).ToList();
        var openEpisodeTrend = await BuildOpenEpisodeTrendForScopeAsync(
            tenantId,
            scopedDeviceAssetIds,
            vulnDefIds,
            ct
        );

        // Episode risk assessments for effective severity/score
        var assessmentsByTenantVulnId = await dbContext.VulnerabilityAssetAssessments.AsNoTracking()
            .Where(a =>
                representativeAsset != null
                && a.AssetId == assetId
                && a.SnapshotId == activeSnapshotId
                && allTenantVulnIds.Contains(a.TenantVulnerabilityId))
            .ToDictionaryAsync(a => a.TenantVulnerabilityId, ct);

        // Episode risk scores
        var episodeRiskScores = await dbContext.VulnerabilityEpisodeRiskAssessments.AsNoTracking()
            .Where(r =>
                representativeAsset != null
                && r.AssetId == assetId
                && allTenantVulnIds.Contains(r.TenantVulnerabilityId)
                && r.ResolvedAt == null)
            .ToDictionaryAsync(r => r.TenantVulnerabilityId, ct);

        var activeWorkflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .Where(workflow =>
                workflow.TenantId == tenantId
                && workflow.TenantSoftwareId == tenantSoftwareId)
            .OrderByDescending(workflow => workflow.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // Current decision
        var decisionQuery = dbContext.RemediationDecisions.AsNoTracking()
            .Include(d => d.VulnerabilityOverrides)
            .Where(d => d.TenantId == tenantId && d.TenantSoftwareId == tenantSoftwareId);

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

        var overridesByTenantVulnId = decision?.VulnerabilityOverrides
            .ToDictionary(vo => vo.TenantVulnerabilityId);

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

        // Asset risk score
        var assetRiskScore = await dbContext.AssetRiskScores.AsNoTracking()
            .FirstOrDefaultAsync(r => r.AssetId == assetId && r.TenantId == tenantId, ct);

        // SLA configuration
        var tenantSla = await dbContext.TenantSlaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        // Build top vulnerabilities
        var topVulns = matches
            .Select(m =>
            {
                threats.TryGetValue(m.Id, out var threat);
                tenantVulnIdByDefId.TryGetValue(m.Id, out var tvId);

                string? effectiveSeverity = m.VendorSeverity.ToString();
                double? effectiveScore = m.VendorScore;
                double? episodeScore = null;

                if (tvId != Guid.Empty)
                {
                    if (assessmentsByTenantVulnId.TryGetValue(tvId, out var assessment))
                    {
                        effectiveSeverity = assessment.EffectiveSeverity.ToString();
                        effectiveScore = (double?)assessment.EffectiveScore;
                    }
                    if (episodeRiskScores.TryGetValue(tvId, out var riskAssessment))
                    {
                        episodeScore = (double?)riskAssessment.EpisodeRiskScore;
                    }
                }

                string? overrideOutcome = null;
                if (overridesByTenantVulnId is not null && tvId != Guid.Empty
                    && overridesByTenantVulnId.TryGetValue(tvId, out var vo))
                {
                    overrideOutcome = vo.Outcome.ToString();
                }

                return new DecisionVulnDto(
                    tvId,
                    m.Id,
                    m.ExternalId,
                    m.Title,
                    m.VendorSeverity.ToString(),
                    m.VendorScore,
                    effectiveSeverity,
                    effectiveScore,
                    m.CvssVector,
                    threat?.KnownExploited ?? false,
                    threat?.PublicExploit ?? false,
                    threat?.ActiveAlert ?? false,
                    threat is not null ? (double?)threat.EpssScore : null,
                    episodeScore,
                    overrideOutcome
                );
            })
            .OrderByDescending(v => v.EpisodeRiskScore ?? v.EffectiveScore ?? 0)
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

        // Risk score
        DecisionRiskDto? riskDto = null;
        var tenantSoftwareRisk = await dbContext.TenantSoftwareRiskScores.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantSoftwareId == tenantSoftwareId && r.TenantId == tenantId, ct);
        if (tenantSoftwareRisk is not null)
        {
            var riskBand = tenantSoftwareRisk.OverallScore switch
            {
                >= 900m => "Critical",
                >= 750m => "High",
                >= 500m => "Medium",
                > 0m => "Low",
                _ => "None",
            };
            riskDto = new DecisionRiskDto(
                (double)tenantSoftwareRisk.OverallScore,
                riskBand,
                tenantSoftwareRisk.CalculatedAt
            );
        }

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
                    vo.TenantVulnerabilityId,
                    vo.Outcome.ToString(),
                    vo.Justification,
                    vo.CreatedAt
                )).ToList()
            );
        }

        // Recommendation DTOs
        var recommendationDtos = recommendations.Select(r => new AnalystRecommendationDto(
            r.Id,
            r.TenantVulnerabilityId,
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
                    vo.TenantVulnerabilityId,
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
        var softwareOwnerTeamName = softwareOwnerTeamId is Guid resolvedSoftwareOwnerTeamId
            && resolvedSoftwareOwnerTeamId != Guid.Empty
            ? await dbContext.Teams.AsNoTracking()
                .Where(team => team.Id == resolvedSoftwareOwnerTeamId)
                .Select(team => team.Name)
                .FirstOrDefaultAsync(ct)
            : null;

        var executionTeamIds = scopedDeviceAssetIds.Count > 0
            ? await dbContext.Assets.AsNoTracking()
                .Where(asset => asset.TenantId == tenantId && scopedDeviceAssetIds.Contains(asset.Id))
                .Select(asset => asset.OwnerTeamId ?? asset.FallbackTeamId)
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

        var aiSummary = await ResolveAiSummaryAsync(
            tenantId,
            tenantSoftwareId,
            softwareName,
            workflowState,
            decisionDto,
            recommendationDtos,
            topVulns.Take(5).ToList(),
            summary,
            remediationAiContext,
            forceRefresh: forceAiSummaryRefresh,
            ct
        );

        return new DecisionContextDto(
            tenantSoftwareId,
            softwareName,
            assetCriticality.ToString(),
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
            recommendationDtos,
            topVulns.Take(5).ToList(),
            riskDto,
            slaDto,
            aiSummary
        );
    }

    public async Task<DecisionAiSummaryDto?> RefreshAiSummaryAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        return (await BuildByTenantSoftwareAsync(tenantId, tenantSoftwareId, true, ct))?.AiSummary;
    }

    public async Task<bool> GenerateAndStoreAiDraftsAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        var summary = await RefreshAiSummaryAsync(tenantId, tenantSoftwareId, ct);
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
        var start = StartOfUtcDay(DateTimeOffset.UtcNow).AddDays(-29);
        return await dbContext.VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(episode =>
                episode.TenantId == tenantId
                && deviceAssetIds.Contains(episode.AssetId)
                && episode.FirstSeenAt < start.AddDays(30)
                && (episode.ResolvedAt == null || episode.ResolvedAt >= start))
            .Join(
                dbContext.TenantVulnerabilities.AsNoTracking()
                    .Where(tv => tv.TenantId == tenantId && vulnerabilityDefinitionIds.Contains(tv.VulnerabilityDefinitionId)),
                episode => episode.TenantVulnerabilityId,
                tenantVulnerability => tenantVulnerability.Id,
                (episode, tenantVulnerability) => new OpenEpisodeRow(
                    episode.AssetId,
                    tenantVulnerability.VulnerabilityDefinitionId,
                    episode.FirstSeenAt,
                    episode.ResolvedAt
                )
            )
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

    private async Task<DecisionAiSummaryDto> ResolveAiSummaryAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        string softwareName,
        DecisionWorkflowStateDto workflowState,
        RemediationDecisionDto? decisionDto,
        List<AnalystRecommendationDto> recommendations,
        List<DecisionVulnDto> topVulns,
        DecisionSummaryDto summary,
        RemediationAiContext remediationAiContext,
        bool forceRefresh,
        CancellationToken ct
    )
    {
        var tenantSoftware = await dbContext.TenantSoftware
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == tenantSoftwareId, ct);
        if (tenantSoftware is null)
        {
            return new DecisionAiSummaryDto(
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
                MissingAiProfileMessage
            );
        }

        var latestJob = await dbContext.RemediationAiJobs.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.TenantSoftwareId == tenantSoftwareId)
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync(ct);
        var reviewedByDisplayName = tenantSoftware.RemediationAiReviewedBy is Guid reviewedByUserId
            ? await dbContext.Users.AsNoTracking()
                .Where(user => user.Id == reviewedByUserId)
                .Select(user => user.DisplayName)
                .FirstOrDefaultAsync(ct)
            : null;

        var inputHash = HashAiSummaryInput(
            softwareName,
            workflowState,
            decisionDto,
            recommendations,
            topVulns,
            summary,
            remediationAiContext
        );

        var hasEnabledDefaultProfile = await dbContext.TenantAiProfiles.AsNoTracking()
            .AnyAsync(item => item.TenantId == tenantId && item.IsDefault && item.IsEnabled, ct);
        var hasStoredGuidance = HasStoredAiGuidance(tenantSoftware);
        var hasFreshGuidance = hasStoredGuidance
            && string.Equals(tenantSoftware.RemediationAiSummaryInputHash, inputHash, StringComparison.Ordinal);

        if (!forceRefresh && hasFreshGuidance)
        {
            return BuildAiSummaryDto(tenantSoftware, latestJob, hasEnabledDefaultProfile, false, null, reviewedByDisplayName);
        }

        if (!hasEnabledDefaultProfile)
        {
            return BuildAiSummaryDto(tenantSoftware, latestJob, false, hasStoredGuidance && !hasFreshGuidance, MissingAiProfileMessage, reviewedByDisplayName);
        }

        if (!forceRefresh)
        {
            return BuildAiSummaryDto(tenantSoftware, latestJob, true, hasStoredGuidance && !hasFreshGuidance, null, reviewedByDisplayName);
        }

        var generatedSummary = await GenerateAiNarrativeAsync(
            tenantId,
            softwareName,
            summary,
            topVulns,
            remediationAiContext,
            ct
        );
        if (!generatedSummary.IsSuccess)
        {
            return new DecisionAiSummaryDto(
                null,
                null,
                null,
                null,
                null,
                null,
                "Failed",
                false,
                NullIfWhiteSpace(tenantSoftware.RemediationAiReviewStatus),
                tenantSoftware.RemediationAiReviewedAt,
                string.IsNullOrWhiteSpace(reviewedByDisplayName) ? null : reviewedByDisplayName,
                null,
                latestJob?.RequestedAt,
                latestJob?.CompletedAt,
                null,
                null,
                null,
                true,
                false,
                generatedSummary.Error,
                "AI input is available, but the summary could not be generated right now. Try Ask AI again."
            );
        }

        tenantSoftware.StoreRemediationAiSummary(
            generatedSummary.Value.Value.ExecutiveSummary,
            generatedSummary.Value.Value.OwnerRecommendation,
            generatedSummary.Value.Value.AnalystAssessment,
            generatedSummary.Value.Value.ExceptionRecommendation,
            generatedSummary.Value.Value.RecommendedOutcome,
            generatedSummary.Value.Value.RecommendedPriority,
            inputHash,
            generatedSummary.Value.Metadata.ProviderType,
            generatedSummary.Value.Metadata.ProfileName,
            generatedSummary.Value.Metadata.Model
        );
        await dbContext.SaveChangesAsync(ct);

        return new DecisionAiSummaryDto(
            generatedSummary.Value.Value.ExecutiveSummary,
            generatedSummary.Value.Value.OwnerRecommendation,
            generatedSummary.Value.Value.AnalystAssessment,
            generatedSummary.Value.Value.ExceptionRecommendation,
            generatedSummary.Value.Value.RecommendedOutcome,
            generatedSummary.Value.Value.RecommendedPriority,
            "Ready",
            false,
            null,
            null,
            null,
            generatedSummary.Value.Metadata.GeneratedAt,
            latestJob?.RequestedAt,
            latestJob?.CompletedAt,
            generatedSummary.Value.Metadata.ProviderType,
            generatedSummary.Value.Metadata.ProfileName,
            generatedSummary.Value.Metadata.Model,
            true,
            false,
            null,
            null
        );
    }

    private DecisionAiSummaryDto BuildAiSummaryDto(
        TenantSoftware tenantSoftware,
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

    private static bool HasStoredAiGuidance(TenantSoftware tenantSoftware) =>
        !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiSummaryContent)
        || !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiOwnerRecommendationContent)
        || !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiAnalystAssessmentContent)
        || !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiExceptionRecommendationContent)
        || !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiRecommendedOutcome)
        || !string.IsNullOrWhiteSpace(tenantSoftware.RemediationAiRecommendedPriority);

    private static string ResolveAiSummaryStatus(
        TenantSoftware tenantSoftware,
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
                $"{item.TenantVulnerabilityId}:{item.ExternalId}:{item.EffectiveSeverity}:{item.EpisodeRiskScore}:{item.KnownExploited}:{item.PublicExploit}:{item.ActiveAlert}:{item.OverrideOutcome}"
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
