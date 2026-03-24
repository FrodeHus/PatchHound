using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models;
using PatchHound.Api.Models.ApprovalTasks;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Services;

public class ApprovalTaskQueryService(
    PatchHoundDbContext dbContext,
    SlaService slaService
)
{
    public async Task<PagedResponse<ApprovalTaskListItemDto>> ListAsync(
        Guid tenantId,
        IReadOnlyList<string> userRoles,
        ApprovalTaskFilterQuery filter,
        PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var roleNames = ParseUserRoles(userRoles);
        var query = dbContext.ApprovalTasks.AsNoTracking()
            .Include(at => at.VisibleRoles)
            .Include(at => at.RemediationDecision)
                .ThenInclude(rd => rd.SoftwareAsset)
            .Where(at => at.TenantId == tenantId)
            .Where(at => at.VisibleRoles.Any(role => roleNames.Contains(role.Role)));

        // Hide read informational tasks by default
        if (!filter.ShowRead)
        {
            query = query.Where(at =>
                at.Status == ApprovalTaskStatus.Pending || at.ReadAt == null);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status)
            && Enum.TryParse<ApprovalTaskStatus>(filter.Status, true, out var statusFilter))
        {
            query = query.Where(at => at.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(filter.Type)
            && Enum.TryParse<ApprovalTaskType>(filter.Type, true, out var typeFilter))
        {
            query = query.Where(at => at.Type == typeFilter);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(at =>
                at.RemediationDecision.SoftwareAsset.Name.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(ct);

        var tasks = await query
            .OrderByDescending(at => at.Status == ApprovalTaskStatus.Pending ? 1 : 0)
            .ThenByDescending(at => at.CreatedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        // Resolve highest severity per decision's software asset
        var softwareAssetIds = tasks.Select(t => t.RemediationDecision.SoftwareAssetId).Distinct().ToList();
        var highestSeverities = await GetHighestSeveritiesAsync(tenantId, softwareAssetIds, ct);

        // Vulnerability counts
        var vulnCounts = await GetVulnCountsAsync(tenantId, softwareAssetIds, ct);

        // SLA info
        var slaInfo = await GetSlaInfoAsync(tenantId, softwareAssetIds, ct);

        // Decided-by user names
        var decidedByIds = tasks.Select(t => t.RemediationDecision.DecidedBy).Distinct().ToList();
        var userNames = await dbContext.Users.AsNoTracking()
            .Where(u => decidedByIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = tasks.Select(t =>
        {
            var assetId = t.RemediationDecision.SoftwareAssetId;
            var hasSeverity = highestSeverities.TryGetValue(assetId, out var severity);
            vulnCounts.TryGetValue(assetId, out var vc);
            var hasSla = slaInfo.TryGetValue(assetId, out var sla);
            userNames.TryGetValue(t.RemediationDecision.DecidedBy, out var decidedByName);

            return new ApprovalTaskListItemDto(
                t.Id,
                t.Type.ToString(),
                t.Status.ToString(),
                t.RemediationDecision.SoftwareAsset.Name,
                t.RemediationDecision.SoftwareAsset.Criticality.ToString(),
                t.RemediationDecision.Outcome.ToString(),
                hasSeverity ? severity.ToString() : "Unknown",
                vc,
                t.ExpiresAt,
                t.CreatedAt,
                t.ReadAt,
                decidedByName ?? "Unknown",
                hasSla ? sla.Status : null,
                hasSla ? sla.DueDate : null
            );
        }).ToList();

        return new PagedResponse<ApprovalTaskListItemDto>(items, totalCount, pagination.Page, pagination.BoundedPageSize);
    }

    public async Task<ApprovalTaskDetailDto?> GetDetailAsync(
        Guid tenantId,
        Guid approvalTaskId,
        IReadOnlyList<string> userRoles,
        PaginationQuery vulnPagination,
        PaginationQuery? devicePagination,
        CancellationToken ct
    )
    {
        var roleNames = ParseUserRoles(userRoles);
        var task = await dbContext.ApprovalTasks.AsNoTracking()
            .Include(at => at.VisibleRoles)
            .Include(at => at.RemediationDecision)
                .ThenInclude(rd => rd.SoftwareAsset)
            .FirstOrDefaultAsync(at =>
                at.Id == approvalTaskId
                && at.TenantId == tenantId
                && at.VisibleRoles.Any(role => roleNames.Contains(role.Role)),
                ct);

        if (task is null) return null;

        var decision = task.RemediationDecision;
        var assetId = decision.SoftwareAssetId;

        // Highest severity
        var highestSeverities = await GetHighestSeveritiesAsync(tenantId, [assetId], ct);
        var hasHighestSeverity = highestSeverities.TryGetValue(assetId, out var highestSeverity);

        // SLA
        var slaInfo = await GetSlaInfoAsync(tenantId, [assetId], ct);
        var hasSla = slaInfo.TryGetValue(assetId, out var sla);

        // Risk score
        var tenantSoftwareId = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.SoftwareAssetId == assetId && i.IsActive)
            .Select(i => i.TenantSoftwareId)
            .FirstOrDefaultAsync(ct);

        double? riskScore = null;
        string? riskBand = null;
        if (tenantSoftwareId != Guid.Empty)
        {
            var risk = await dbContext.TenantSoftwareRiskScores.AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantSoftwareId == tenantSoftwareId && r.TenantId == tenantId, ct);
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
        }

        // Decided-by name
        var decidedByUser = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == decision.DecidedBy, ct);

        // Vulnerabilities (paginated, sorted by severity)
        var vulnQuery = dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(m => m.SoftwareAssetId == assetId && m.TenantId == tenantId && m.ResolvedAt == null)
            .Join(
                dbContext.VulnerabilityDefinitions.AsNoTracking(),
                m => m.VulnerabilityDefinitionId,
                v => v.Id,
                (m, v) => new { m, v }
            )
            .Join(
                dbContext.TenantVulnerabilities.AsNoTracking().Where(tv => tv.TenantId == tenantId),
                mv => mv.v.Id,
                tv => tv.VulnerabilityDefinitionId,
                (mv, tv) => new { mv.v, TenantVulnId = tv.Id }
            );

        var vulnTotalCount = await vulnQuery.CountAsync(ct);

        // Load threats for KnownExploited and EpssScore
        var vulnDefIds = await vulnQuery.Select(x => x.v.Id).Distinct().ToListAsync(ct);
        var threats = await dbContext.VulnerabilityThreatAssessments.AsNoTracking()
            .Where(t => vulnDefIds.Contains(t.VulnerabilityDefinitionId))
            .ToDictionaryAsync(t => t.VulnerabilityDefinitionId, ct);

        var vulns = await vulnQuery
            .OrderByDescending(x => x.v.VendorSeverity)
            .ThenByDescending(x => x.v.CvssScore)
            .Skip(vulnPagination.Skip)
            .Take(vulnPagination.BoundedPageSize)
            .Select(x => new
            {
                x.TenantVulnId,
                x.v.Id,
                x.v.ExternalId,
                x.v.Title,
                x.v.VendorSeverity,
                VendorScore = x.v.CvssScore.HasValue ? (double?)((double)x.v.CvssScore.Value) : null,
            })
            .ToListAsync(ct);

        var vulnDtos = vulns.Select(v =>
        {
            threats.TryGetValue(v.Id, out var threat);
            return new ApprovalVulnDto(
                v.TenantVulnId,
                v.ExternalId,
                v.Title,
                v.VendorSeverity.ToString(),
                v.VendorScore,
                v.VendorSeverity.ToString(),
                threat?.KnownExploited ?? false,
                threat is not null ? (double?)threat.EpssScore : null
            );
        }).ToList();

        var vulnList = new PagedVulnerabilityList(vulnDtos, vulnTotalCount, vulnPagination.Page, vulnPagination.BoundedPageSize);

        // Devices (only for patching-type tasks)
        PagedDeviceList? deviceList = null;
        if (task.Type is ApprovalTaskType.PatchingApproved or ApprovalTaskType.PatchingDeferred && devicePagination is not null)
        {
            var deviceQuery = dbContext.NormalizedSoftwareInstallations.AsNoTracking()
                .Where(nsi =>
                    nsi.TenantId == tenantId
                    && nsi.SoftwareAssetId == assetId
                    && nsi.IsActive)
                .Join(
                    dbContext.Assets.AsNoTracking().Where(a => a.TenantId == tenantId),
                    nsi => nsi.DeviceAssetId,
                    a => a.Id,
                    (dsi, a) => new { a.Id, a.Name, a.Criticality }
                )
                .Distinct();

            var deviceTotalCount = await deviceQuery.CountAsync(ct);
            var devices = await deviceQuery
                .OrderByDescending(d => d.Criticality)
                .ThenBy(d => d.Name)
                .Skip(devicePagination.Skip)
                .Take(devicePagination.BoundedPageSize)
                .Select(d => new ApprovalDeviceDto(d.Id, d.Name, d.Criticality.ToString()))
                .ToListAsync(ct);

            deviceList = new PagedDeviceList(devices, deviceTotalCount, devicePagination.Page, devicePagination.BoundedPageSize);
        }

        // Recommendations
        var recommendations = await dbContext.AnalystRecommendations.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.SoftwareAssetId == assetId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ApprovalRecommendationDto(
                r.Id, r.RecommendedOutcome.ToString(), r.Rationale,
                r.PriorityOverride, r.AnalystId, r.CreatedAt))
            .ToListAsync(ct);

        // Audit trail
        var auditEntries = await dbContext.AuditLogEntries.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId
                && a.EntityType == "RemediationDecision"
                && a.EntityId == decision.Id)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(ct);

        var auditUserIds = auditEntries.Select(a => a.UserId).Distinct().ToList();
        var auditUserNames = await dbContext.Users.AsNoTracking()
            .Where(u => auditUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var auditDtos = auditEntries.Select(a =>
        {
            auditUserNames.TryGetValue(a.UserId, out var name);
            // Extract justification from NewValues JSON
            string? justification = null;
            if (a.NewValues is not null)
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(a.NewValues);
                    if (doc.RootElement.TryGetProperty("Justification", out var j))
                        justification = j.GetString();
                }
                catch { }
            }
            return new ApprovalAuditEntryDto(a.Action.ToString(), name, justification, a.Timestamp);
        }).ToList();

        return new ApprovalTaskDetailDto(
            task.Id,
            task.Type.ToString(),
            task.Status.ToString(),
            task.RemediationDecisionId,
            decision.SoftwareAsset.Name,
            decision.SoftwareAsset.Criticality.ToString(),
            decision.Outcome.ToString(),
            decision.Justification,
            hasHighestSeverity ? highestSeverity.ToString() : "Unknown",
            task.RequiresJustification,
            task.ExpiresAt,
            task.CreatedAt,
            task.ReadAt,
            decidedByUser?.DisplayName ?? "Unknown",
            hasSla ? sla.Status : null,
            hasSla ? sla.DueDate : null,
            riskScore,
            riskBand,
            vulnList,
            deviceList,
            recommendations,
            auditDtos
        );
    }

    public async Task<int> GetPendingCountAsync(
        Guid tenantId,
        IReadOnlyList<string> userRoles,
        CancellationToken ct
    )
    {
        var roleNames = ParseUserRoles(userRoles);

        return await dbContext.ApprovalTasks.AsNoTracking()
            .Where(at => at.TenantId == tenantId
                && at.Status == ApprovalTaskStatus.Pending
                && at.VisibleRoles.Any(role => roleNames.Contains(role.Role)))
            .CountAsync(ct);
    }

    private static List<RoleName> ParseUserRoles(IReadOnlyList<string> userRoles)
    {
        return userRoles
            .Where(role => Enum.TryParse<RoleName>(role, true, out _))
            .Select(role => Enum.Parse<RoleName>(role, true))
            .Distinct()
            .ToList();
    }

    private async Task<Dictionary<Guid, Severity>> GetHighestSeveritiesAsync(
        Guid tenantId, List<Guid> softwareAssetIds, CancellationToken ct)
    {
        return await dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(m => m.TenantId == tenantId && softwareAssetIds.Contains(m.SoftwareAssetId) && m.ResolvedAt == null)
            .Join(
                dbContext.VulnerabilityDefinitions.AsNoTracking(),
                m => m.VulnerabilityDefinitionId,
                v => v.Id,
                (m, v) => new { m.SoftwareAssetId, v.VendorSeverity }
            )
            .GroupBy(x => x.SoftwareAssetId)
            .Select(g => new { AssetId = g.Key, HighestSeverity = g.Max(x => x.VendorSeverity) })
            .ToDictionaryAsync(x => x.AssetId, x => x.HighestSeverity, ct);
    }

    private async Task<Dictionary<Guid, int>> GetVulnCountsAsync(
        Guid tenantId, List<Guid> softwareAssetIds, CancellationToken ct)
    {
        return await dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(m => m.TenantId == tenantId && softwareAssetIds.Contains(m.SoftwareAssetId) && m.ResolvedAt == null)
            .GroupBy(m => m.SoftwareAssetId)
            .Select(g => new { AssetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AssetId, x => x.Count, ct);
    }

    private async Task<Dictionary<Guid, (string Status, DateTimeOffset DueDate)>> GetSlaInfoAsync(
        Guid tenantId, List<Guid> softwareAssetIds, CancellationToken ct)
    {
        var tenantSla = await dbContext.TenantSlaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        if (tenantSla is null)
            return [];

        var vulnData = await dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(m => m.TenantId == tenantId && softwareAssetIds.Contains(m.SoftwareAssetId) && m.ResolvedAt == null)
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

        var result = new Dictionary<Guid, (string Status, DateTimeOffset DueDate)>();
        foreach (var item in vulnData)
        {
            if (item.HighestSeverity == default) continue;
            var dueDate = slaService.CalculateDueDate(item.HighestSeverity, item.EarliestFirstSeen, tenantSla);
            var status = slaService.GetSlaStatus(item.EarliestFirstSeen, dueDate, DateTimeOffset.UtcNow);
            result[item.AssetId] = (status.ToString(), dueDate);
        }

        return result;
    }
}
