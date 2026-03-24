using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Decisions;
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
    ITenantAiConfigurationResolver aiConfigResolver,
    TenantAiTextGenerationService aiTextGenerationService
)
{
    public async Task<PagedResponse<RemediationDecisionListItemDto>> ListAsync(
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
            if (!highestCriticalityByTenantSoftwareId.TryGetValue(software.Id, out var criticality))
                criticality = Criticality.Low;

            if (!string.IsNullOrWhiteSpace(filter.Criticality)
                && Enum.TryParse<Criticality>(filter.Criticality, true, out var crit)
                && criticality != crit)
            {
                continue;
            }

            decisionsLookup.TryGetValue(software.Id, out var decision);
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
                decision?.ExpiryDate,
                vulnCounts.GetValueOrDefault(software.Id)?.Total ?? 0,
                vulnCounts.GetValueOrDefault(software.Id)?.Critical ?? 0,
                vulnCounts.GetValueOrDefault(software.Id)?.High ?? 0,
                riskScore,
                riskBand,
                slaStatus,
                slaDueDate,
                deviceCountByTenantSoftwareId.GetValueOrDefault(software.Id)
            ));
        }

        var totalCount = items.Count;
        var paged = items
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToList();

        return new PagedResponse<RemediationDecisionListItemDto>(paged, totalCount, pagination.Page, pagination.BoundedPageSize);
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

        return await BuildByTenantSoftwareAsync(tenantId, tenantSoftwareId, ct);
    }

    public async Task<DecisionContextDto?> BuildByTenantSoftwareAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
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

        // Current decision
        var decision = await dbContext.RemediationDecisions.AsNoTracking()
            .Include(d => d.VulnerabilityOverrides)
            .Where(d => d.TenantId == tenantId && d.TenantSoftwareId == tenantSoftwareId
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var overridesByTenantVulnId = decision?.VulnerabilityOverrides
            .ToDictionary(vo => vo.TenantVulnerabilityId);

        // Analyst recommendations
        var recommendations = await dbContext.AnalystRecommendations.AsNoTracking()
            .Where(r => r.TenantId == tenantId && scopedSoftwareAssetIds.Contains(r.SoftwareAssetId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        var recommendationAnalystIds = recommendations.Select(r => r.AnalystId).Distinct().ToList();
        var recommendationAnalystNames = recommendationAnalystIds.Count > 0
            ? await dbContext.Users.AsNoTracking()
                .Where(user => recommendationAnalystIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct)
            : new Dictionary<Guid, string>();

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

        // AI narrative (non-blocking, swallow errors)
        string? aiNarrative = null;
        try
        {
            aiNarrative = await GenerateAiNarrativeAsync(tenantId, softwareName, summary, topVulns.Take(5).ToList(), ct);
        }
        catch
        {
            // AI narrative is optional
        }

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
                decision.ExpiryDate,
                decision.ReEvaluationDate,
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

        return new DecisionContextDto(
            tenantSoftwareId,
            softwareName,
            assetCriticality.ToString(),
            summary,
            decisionDto,
            recommendationDtos,
            topVulns.Take(5).ToList(),
            riskDto,
            slaDto,
            aiNarrative
        );
    }

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

    private async Task<string?> GenerateAiNarrativeAsync(
        Guid tenantId,
        string softwareName,
        DecisionSummaryDto summary,
        List<DecisionVulnDto> topVulns,
        CancellationToken ct
    )
    {
        var resolvedProfileResult = await aiConfigResolver.ResolveDefaultAsync(tenantId, ct);
        if (!resolvedProfileResult.IsSuccess)
            return null;

        var resolvedProfile = resolvedProfileResult.Value;

        var topVulnSummary = string.Join("; ", topVulns.Select(v =>
            $"{v.ExternalId} ({v.VendorSeverity}, score={v.EpisodeRiskScore?.ToString("F0") ?? "N/A"}, exploited={v.KnownExploited})"
        ));

        var request = new AiTextGenerationRequest(
            "You are a PatchHound remediation analyst. Write one concise paragraph (2-3 sentences) summarizing the remediation context for a software title across its active versions. Focus on risk level, key vulnerabilities, and recommended action urgency. Do not use bullets, headings, or markdown.",
            $"Software: {softwareName}. Total vulnerabilities: {summary.TotalVulnerabilities} (Critical: {summary.CriticalCount}, High: {summary.HighCount}, Medium: {summary.MediumCount}, Low: {summary.LowCount}). Known exploits: {summary.WithKnownExploit}. Active alerts: {summary.WithActiveAlert}. Top vulnerabilities: {topVulnSummary}.",
            ExternalContext: null,
            UseProviderNativeWebResearch: false
        );

        var result = await aiTextGenerationService.GenerateAsync(
            tenantId,
            resolvedProfile.Profile.Id,
            request,
            ct
        );

        return result.IsSuccess ? result.Value.Content : null;
    }
}
