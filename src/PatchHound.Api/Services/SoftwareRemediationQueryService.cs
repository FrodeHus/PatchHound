using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models.Assets;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Services;

public class SoftwareRemediationQueryService(
    PatchHoundDbContext dbContext,
    TenantSnapshotResolver snapshotResolver
)
{
    public async Task<SoftwareRemediationContextDto?> BuildAsync(
        Guid tenantId,
        Guid assetId,
        CancellationToken ct
    )
    {
        var asset = await dbContext
            .Assets.AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == assetId && a.TenantId == tenantId && a.AssetType == AssetType.Software,
                ct
            );
        if (asset is null)
            return null;

        var activeSnapshotId = await snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(tenantId, ct);

        var matches = await dbContext
            .SoftwareVulnerabilityMatches.AsNoTracking()
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
                    VendorSeverity = v.VendorSeverity.ToString(),
                    VendorScore = v.CvssScore.HasValue ? (double?)((double)v.CvssScore.Value) : null,
                    v.CvssVector,
                    MatchMethod = m.MatchMethod.ToString(),
                    Confidence = m.Confidence.ToString(),
                    m.Evidence,
                    m.FirstSeenAt,
                    m.ResolvedAt,
                }
            )
            .ToListAsync(ct);

        var vulnDefIds = matches.Select(m => m.Id).Distinct().ToList();

        var threats = await dbContext
            .VulnerabilityThreatAssessments.AsNoTracking()
            .Where(t => vulnDefIds.Contains(t.VulnerabilityDefinitionId))
            .ToDictionaryAsync(t => t.VulnerabilityDefinitionId, ct);

        // Remediation tasks are linked via TenantVulnerabilityId, so we need
        // to resolve VulnerabilityDefinitionId -> TenantVulnerabilityId first
        var tenantVulnLookup = await dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(tv => tv.TenantId == tenantId && vulnDefIds.Contains(tv.VulnerabilityDefinitionId))
            .Select(tv => new { tv.Id, tv.VulnerabilityDefinitionId })
            .ToListAsync(ct);

        var tenantVulnIdByDefId = tenantVulnLookup
            .GroupBy(x => x.VulnerabilityDefinitionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var allTenantVulnIds = tenantVulnLookup.Select(x => x.Id).ToList();

        var tasks = await dbContext
            .RemediationTasks.AsNoTracking()
            .Where(t => t.AssetId == assetId && t.TenantId == tenantId && allTenantVulnIds.Contains(t.TenantVulnerabilityId))
            .ToListAsync(ct);

        var tasksByTenantVulnId = tasks
            .GroupBy(t => t.TenantVulnerabilityId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.CreatedAt).First());

        var riskAcceptances = await dbContext
            .RiskAcceptances.AsNoTracking()
            .Where(r => r.AssetId == assetId && r.TenantId == tenantId && allTenantVulnIds.Contains(r.TenantVulnerabilityId))
            .ToListAsync(ct);

        var riskAcceptancesByTenantVulnId = riskAcceptances
            .GroupBy(r => r.TenantVulnerabilityId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RequestedAt).First());

        // Build VulnerabilityAssetAssessments for effective severity
        var assessmentsByTenantVulnId = await dbContext
            .VulnerabilityAssetAssessments.AsNoTracking()
            .Where(a => a.AssetId == assetId && a.SnapshotId == activeSnapshotId && allTenantVulnIds.Contains(a.TenantVulnerabilityId))
            .ToDictionaryAsync(a => a.TenantVulnerabilityId, ct);

        var vulnerabilities = matches
            .Select(m =>
            {
                threats.TryGetValue(m.Id, out var threat);

                var tenantVulnIds = tenantVulnIdByDefId.GetValueOrDefault(m.Id);
                Core.Entities.RemediationTask? task = null;
                Core.Entities.RiskAcceptance? riskAcceptance = null;
                string effectiveSeverity = m.VendorSeverity;
                double? effectiveScore = m.VendorScore;

                if (tenantVulnIds is not null)
                {
                    foreach (var tvId in tenantVulnIds)
                    {
                        if (task is null && tasksByTenantVulnId.TryGetValue(tvId, out var t))
                            task = t;
                        if (riskAcceptance is null && riskAcceptancesByTenantVulnId.TryGetValue(tvId, out var ra))
                            riskAcceptance = ra;
                        if (assessmentsByTenantVulnId.TryGetValue(tvId, out var assessment))
                        {
                            effectiveSeverity = assessment.EffectiveSeverity.ToString();
                            effectiveScore = (double?)assessment.EffectiveScore;
                        }
                    }
                }

                return new SoftwareRemediationVulnDto(
                    m.Id,
                    m.ExternalId,
                    m.Title,
                    m.VendorSeverity,
                    m.VendorScore,
                    effectiveSeverity,
                    effectiveScore,
                    m.CvssVector,
                    m.MatchMethod,
                    m.Confidence,
                    m.Evidence,
                    m.FirstSeenAt,
                    m.ResolvedAt,
                    threat is not null
                        ? new SoftwareRemediationThreatDto(
                            (double?)threat.EpssScore,
                            null,
                            threat.KnownExploited,
                            threat.PublicExploit,
                            threat.ActiveAlert,
                            threat.HasRansomwareAssociation
                        )
                        : null,
                    task is not null
                        ? new SoftwareRemediationTaskDto(
                            task.Id,
                            task.Status.ToString(),
                            task.Justification,
                            task.DueDate,
                            task.CreatedAt
                        )
                        : null,
                    riskAcceptance is not null
                        ? new SoftwareRemediationRiskAcceptanceDto(
                            riskAcceptance.Id,
                            riskAcceptance.Status.ToString(),
                            riskAcceptance.Justification,
                            riskAcceptance.Conditions,
                            riskAcceptance.ExpiryDate,
                            riskAcceptance.RequestedAt
                        )
                        : null
                );
            })
            .OrderByDescending(v => v.EffectiveScore ?? 0)
            .ThenByDescending(v => v.VendorScore ?? 0)
            .ToList();

        var summary = new SoftwareRemediationSummaryDto(
            TotalVulnerabilities: vulnerabilities.Count,
            CriticalCount: vulnerabilities.Count(v => v.EffectiveSeverity == "Critical"),
            HighCount: vulnerabilities.Count(v => v.EffectiveSeverity == "High"),
            MediumCount: vulnerabilities.Count(v => v.EffectiveSeverity == "Medium"),
            LowCount: vulnerabilities.Count(v => v.EffectiveSeverity == "Low"),
            WithKnownExploit: vulnerabilities.Count(v => v.Threat?.KnownExploited == true),
            WithActiveAlert: vulnerabilities.Count(v => v.Threat?.ActiveAlert == true),
            PendingRemediationTasks: vulnerabilities.Count(v => v.RemediationTask is { Status: "Pending" or "InProgress" }),
            RiskAcceptedCount: vulnerabilities.Count(v => v.RiskAcceptance is { Status: "Approved" })
        );

        return new SoftwareRemediationContextDto(
            asset.Id,
            asset.Name,
            asset.Criticality.ToString(),
            summary,
            vulnerabilities
        );
    }
}
