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

        // All software assets for the tenant
        var assetsQuery = dbContext.Assets.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Software);

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            assetsQuery = assetsQuery.Where(a => a.Name.ToLower().Contains(term));
        }

        // Apply criticality filter
        if (!string.IsNullOrWhiteSpace(filter.Criticality)
            && Enum.TryParse<Criticality>(filter.Criticality, true, out var crit))
        {
            assetsQuery = assetsQuery.Where(a => a.Criticality == crit);
        }

        // Latest active decision per asset
        var decisionsLookup = await dbContext.RemediationDecisions.AsNoTracking()
            .Where(d => d.TenantId == tenantId
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
            .GroupBy(d => d.SoftwareAssetId)
            .Select(g => g.OrderByDescending(d => d.CreatedAt).First())
            .ToDictionaryAsync(d => d.SoftwareAssetId, ct);

        // Apply outcome/status filters (post-query since we need the decision lookup)
        HashSet<Guid>? filteredAssetIds = null;
        if (!string.IsNullOrWhiteSpace(filter.Outcome) || !string.IsNullOrWhiteSpace(filter.ApprovalStatus))
        {
            filteredAssetIds = [];
            foreach (var (assetId, decision) in decisionsLookup)
            {
                var matchesOutcome = string.IsNullOrWhiteSpace(filter.Outcome)
                    || string.Equals(decision.Outcome.ToString(), filter.Outcome, StringComparison.OrdinalIgnoreCase);
                var matchesStatus = string.IsNullOrWhiteSpace(filter.ApprovalStatus)
                    || string.Equals(decision.ApprovalStatus.ToString(), filter.ApprovalStatus, StringComparison.OrdinalIgnoreCase);

                if (matchesOutcome && matchesStatus)
                    filteredAssetIds.Add(assetId);
            }
        }

        // Risk scores
        var riskScores = await dbContext.AssetRiskScores.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToDictionaryAsync(r => r.AssetId, ct);

        // Vuln counts per software asset from risk scores (already aggregated)
        // Device counts per software asset
        var deviceCounts = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.IsActive)
            .GroupBy(i => i.SoftwareAssetId)
            .Select(g => new { SoftwareAssetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SoftwareAssetId, x => x.Count, ct);

        // TenantSoftwareId lookup via installations
        var tenantSoftwareByAsset = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.IsActive)
            .GroupBy(i => i.SoftwareAssetId)
            .Select(g => new { SoftwareAssetId = g.Key, TenantSoftwareId = g.First().TenantSoftwareId })
            .ToDictionaryAsync(x => x.SoftwareAssetId, x => x.TenantSoftwareId, ct);

        // SLA configuration
        var tenantSla = await dbContext.TenantSlaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        // SLA due dates per asset — need earliest first-seen per asset
        Dictionary<Guid, (DateTimeOffset DueDate, string Status)>? slaByAsset = null;
        if (tenantSla is not null)
        {
            var vulnFirstSeen = await dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.ResolvedAt == null && m.SnapshotId == activeSnapshotId)
                .Join(
                    dbContext.VulnerabilityDefinitions.AsNoTracking(),
                    m => m.VulnerabilityDefinitionId,
                    v => v.Id,
                    (m, v) => new { m.SoftwareAssetId, m.FirstSeenAt, v.VendorSeverity }
                )
                .GroupBy(x => x.SoftwareAssetId)
                .Select(g => new
                {
                    AssetId = g.Key,
                    EarliestFirstSeen = g.Min(x => x.FirstSeenAt),
                    HighestSeverity = g.Max(x => x.VendorSeverity),
                })
                .ToListAsync(ct);

            slaByAsset = [];
            foreach (var item in vulnFirstSeen)
            {
                if (item.HighestSeverity == default) continue;
                var dueDate = slaService.CalculateDueDate(item.HighestSeverity, item.EarliestFirstSeen, tenantSla);
                var status = slaService.GetSlaStatus(item.EarliestFirstSeen, dueDate, DateTimeOffset.UtcNow);
                slaByAsset[item.AssetId] = (dueDate, status.ToString());
            }
        }

        // Materialize assets
        var assets = await assetsQuery.OrderBy(a => a.Name).ToListAsync(ct);

        // Build list items
        var items = new List<RemediationDecisionListItemDto>();
        foreach (var asset in assets)
        {
            if (filteredAssetIds is not null && !filteredAssetIds.Contains(asset.Id))
                continue;

            decisionsLookup.TryGetValue(asset.Id, out var decision);
            riskScores.TryGetValue(asset.Id, out var risk);
            deviceCounts.TryGetValue(asset.Id, out var devCount);
            tenantSoftwareByAsset.TryGetValue(asset.Id, out var tsId);

            string? riskBand = null;
            double? riskScore = null;
            if (risk is not null)
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
            if (slaByAsset is not null && slaByAsset.TryGetValue(asset.Id, out var sla))
            {
                slaStatus = sla.Status;
                slaDueDate = sla.DueDate;
            }

            items.Add(new RemediationDecisionListItemDto(
                asset.Id,
                asset.Name,
                asset.Criticality.ToString(),
                tsId != Guid.Empty ? tsId : null,
                decision?.Outcome.ToString(),
                decision?.ApprovalStatus.ToString(),
                decision?.DecidedAt,
                decision?.ExpiryDate,
                risk?.CriticalCount + risk?.HighCount + risk?.MediumCount + risk?.LowCount ?? 0,
                risk?.CriticalCount ?? 0,
                risk?.HighCount ?? 0,
                riskScore,
                riskBand,
                slaStatus,
                slaDueDate,
                devCount
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
        var asset = await dbContext.Assets.AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == assetId && a.TenantId == tenantId && a.AssetType == AssetType.Software,
                ct
            );
        if (asset is null)
            return null;

        var activeSnapshotId = await snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(tenantId, ct);

        // Load vulnerability matches
        var matches = await dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(m =>
                m.SoftwareAssetId == assetId
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
            .Where(a => a.AssetId == assetId && a.SnapshotId == activeSnapshotId && allTenantVulnIds.Contains(a.TenantVulnerabilityId))
            .ToDictionaryAsync(a => a.TenantVulnerabilityId, ct);

        // Episode risk scores
        var episodeRiskScores = await dbContext.VulnerabilityEpisodeRiskAssessments.AsNoTracking()
            .Where(r => r.AssetId == assetId && allTenantVulnIds.Contains(r.TenantVulnerabilityId) && r.ResolvedAt == null)
            .ToDictionaryAsync(r => r.TenantVulnerabilityId, ct);

        // Current decision
        var decision = await dbContext.RemediationDecisions.AsNoTracking()
            .Include(d => d.VulnerabilityOverrides)
            .Where(d => d.TenantId == tenantId && d.SoftwareAssetId == assetId
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var overridesByTenantVulnId = decision?.VulnerabilityOverrides
            .ToDictionary(vo => vo.TenantVulnerabilityId);

        // Analyst recommendations
        var recommendations = await dbContext.AnalystRecommendations.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.SoftwareAssetId == assetId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

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
        if (assetRiskScore is not null)
        {
            var riskBand = assetRiskScore.OverallScore switch
            {
                >= 900m => "Critical",
                >= 750m => "High",
                >= 500m => "Medium",
                > 0m => "Low",
                _ => "None",
            };
            riskDto = new DecisionRiskDto(
                (double)assetRiskScore.OverallScore,
                riskBand,
                assetRiskScore.CalculatedAt
            );
        }

        // AI narrative (non-blocking, swallow errors)
        string? aiNarrative = null;
        try
        {
            aiNarrative = await GenerateAiNarrativeAsync(tenantId, asset.Name, summary, topVulns.Take(5).ToList(), ct);
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
            r.CreatedAt
        )).ToList();

        return new DecisionContextDto(
            asset.Id,
            asset.Name,
            asset.Criticality.ToString(),
            summary,
            decisionDto,
            recommendationDtos,
            topVulns.Take(5).ToList(),
            riskDto,
            slaDto,
            aiNarrative
        );
    }

    private async Task<string?> GenerateAiNarrativeAsync(
        Guid tenantId,
        string assetName,
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
            "You are a PatchHound remediation analyst. Write one concise paragraph (2-3 sentences) summarizing the remediation context for a software asset. Focus on risk level, key vulnerabilities, and recommended action urgency. Do not use bullets, headings, or markdown.",
            $"Asset: {assetName}. Total vulnerabilities: {summary.TotalVulnerabilities} (Critical: {summary.CriticalCount}, High: {summary.HighCount}, Medium: {summary.MediumCount}, Low: {summary.LowCount}). Known exploits: {summary.WithKnownExploit}. Active alerts: {summary.WithActiveAlert}. Top vulnerabilities: {topVulnSummary}.",
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
