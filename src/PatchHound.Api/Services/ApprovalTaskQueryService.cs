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
            var matchingTenantSoftwareIds = await dbContext.TenantSoftware.AsNoTracking()
                .Where(item =>
                    item.TenantId == tenantId
                    && (
                        item.NormalizedSoftware.CanonicalName.ToLower().Contains(term)
                        || (item.NormalizedSoftware.CanonicalVendor != null
                            && item.NormalizedSoftware.CanonicalVendor.ToLower().Contains(term))
                    ))
                .Select(item => item.Id)
                .ToListAsync(ct);

            query = query.Where(at =>
                matchingTenantSoftwareIds.Contains(at.RemediationDecision.TenantSoftwareId));
        }

        var totalCount = await query.CountAsync(ct);

        var tasks = await query
            .OrderByDescending(at => at.Status == ApprovalTaskStatus.Pending ? 1 : 0)
            .ThenByDescending(at => at.CreatedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        // Resolve highest severity per decision's software asset
        var tenantSoftwareIds = tasks.Select(t => t.RemediationDecision.TenantSoftwareId).Distinct().ToList();
        var softwareNames = await GetSoftwareNamesAsync(tenantId, tenantSoftwareIds, ct);
        var highestSeverities = await GetHighestSeveritiesAsync(tenantId, tenantSoftwareIds, ct);
        var highestCriticalities = await GetHighestCriticalitiesAsync(tenantId, tenantSoftwareIds, ct);

        // Vulnerability counts
        var vulnCounts = await GetVulnCountsAsync(tenantId, tenantSoftwareIds, ct);

        // SLA info
        var slaInfo = await GetSlaInfoAsync(tenantId, tenantSoftwareIds, ct);

        // Decided-by user names
        var decidedByIds = tasks.Select(t => t.RemediationDecision.DecidedBy).Distinct().ToList();
        var userNames = await dbContext.Users.AsNoTracking()
            .Where(u => decidedByIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = tasks.Select(t =>
        {
            var tenantSoftwareId = t.RemediationDecision.TenantSoftwareId;
            softwareNames.TryGetValue(tenantSoftwareId, out var softwareName);
            var hasSeverity = highestSeverities.TryGetValue(tenantSoftwareId, out var severity);
            var hasCriticality = highestCriticalities.TryGetValue(tenantSoftwareId, out var criticality);
            vulnCounts.TryGetValue(tenantSoftwareId, out var vc);
            var hasSla = slaInfo.TryGetValue(tenantSoftwareId, out var sla);
            userNames.TryGetValue(t.RemediationDecision.DecidedBy, out var decidedByName);

            return new ApprovalTaskListItemDto(
                t.Id,
                t.Type.ToString(),
                t.Status.ToString(),
                softwareName ?? t.RemediationDecision.SoftwareAsset.Name,
                hasCriticality ? criticality.ToString() : t.RemediationDecision.SoftwareAsset.Criticality.ToString(),
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
        string? deviceVersion,
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
        var tenantSoftwareId = decision.TenantSoftwareId;
        var softwareNames = await GetSoftwareNamesAsync(tenantId, [tenantSoftwareId], ct);
        softwareNames.TryGetValue(tenantSoftwareId, out var softwareName);

        // Highest severity
        var highestSeverities = await GetHighestSeveritiesAsync(tenantId, [tenantSoftwareId], ct);
        var hasHighestSeverity = highestSeverities.TryGetValue(tenantSoftwareId, out var highestSeverity);
        var highestCriticalities = await GetHighestCriticalitiesAsync(tenantId, [tenantSoftwareId], ct);
        var hasHighestCriticality = highestCriticalities.TryGetValue(tenantSoftwareId, out var highestCriticality);

        // SLA
        var slaInfo = await GetSlaInfoAsync(tenantId, [tenantSoftwareId], ct);
        var hasSla = slaInfo.TryGetValue(tenantSoftwareId, out var sla);

        // Risk score
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
        var scopedSoftwareAssetIds = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.TenantSoftwareId == tenantSoftwareId && i.IsActive)
            .Select(i => i.SoftwareAssetId)
            .Distinct()
            .ToListAsync(ct);
        var vulnQuery = dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(m =>
                m.TenantId == tenantId
                && m.ResolvedAt == null
                && scopedSoftwareAssetIds.Contains(m.SoftwareAssetId))
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
        var deviceVersionCohorts = new List<ApprovalDeviceVersionCohortDto>();
        PagedDeviceList? deviceList = null;
        if (task.Type is ApprovalTaskType.PatchingApproved or ApprovalTaskType.PatchingDeferred && devicePagination is not null)
        {
            var activeInstallations = dbContext.NormalizedSoftwareInstallations.AsNoTracking()
                .Where(nsi =>
                    nsi.TenantId == tenantId
                    && nsi.TenantSoftwareId == tenantSoftwareId
                    && nsi.IsActive)
                .Select(nsi => new
                {
                    nsi.DeviceAssetId,
                    nsi.DetectedVersion,
                    nsi.FirstSeenAt,
                    nsi.LastSeenAt,
                    nsi.SoftwareAssetId,
                });

            var installationRows = await activeInstallations.ToListAsync(ct);
            var softwareAssetIdsForInstalls = installationRows
                .Select(item => item.SoftwareAssetId)
                .Distinct()
                .ToList();
            var openMatchRows = await dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
                .Where(match =>
                    match.TenantId == tenantId
                    && match.ResolvedAt == null
                    && softwareAssetIdsForInstalls.Contains(match.SoftwareAssetId))
                .Select(match => new { match.SoftwareAssetId, match.VulnerabilityDefinitionId })
                .Distinct()
                .ToListAsync(ct);

            deviceVersionCohorts = installationRows
                .GroupBy(item => NormalizeVersionKey(item.DetectedVersion))
                .Select(group =>
                {
                    var cohortSoftwareAssetIds = group.Select(item => item.SoftwareAssetId).ToHashSet();
                    return new ApprovalDeviceVersionCohortDto(
                        RestoreVersion(group.Key),
                        group.Count(),
                        group.Select(item => item.DeviceAssetId).Distinct().Count(),
                        openMatchRows.Count(item => cohortSoftwareAssetIds.Contains(item.SoftwareAssetId)),
                        group.Min(item => item.FirstSeenAt),
                        group.Max(item => item.LastSeenAt)
                    );
                })
                .OrderByDescending(item => item.ActiveInstallCount)
                .ThenByDescending(item => item.ActiveVulnerabilityCount)
                .ThenBy(item => item.Version ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selectedVersionKey = deviceVersion is null
                ? NormalizeVersionKey(deviceVersionCohorts.FirstOrDefault()?.Version)
                : NormalizeVersionKey(deviceVersion);

            var deviceQuery = dbContext.NormalizedSoftwareInstallations.AsNoTracking()
                .Where(nsi =>
                    nsi.TenantId == tenantId
                    && nsi.TenantSoftwareId == tenantSoftwareId
                    && nsi.IsActive
                    && (selectedVersionKey == string.Empty
                        ? string.IsNullOrWhiteSpace(nsi.DetectedVersion)
                        : nsi.DetectedVersion == selectedVersionKey))
                .Select(nsi => new
                {
                    nsi.DeviceAssetId,
                    DeviceName = nsi.DeviceAsset.AssetType == AssetType.Device
                        ? nsi.DeviceAsset.DeviceComputerDnsName ?? nsi.DeviceAsset.Name
                        : nsi.DeviceAsset.Name,
                    Criticality = nsi.DeviceAsset.Criticality,
                    nsi.DetectedVersion,
                    nsi.LastSeenAt,
                    OpenVulnerabilityCount = dbContext.VulnerabilityAssets
                        .Where(link =>
                            link.AssetId == nsi.DeviceAssetId
                            && link.Status == Core.Enums.VulnerabilityStatus.Open)
                        .Count(),
                })
                .Distinct();

            var deviceTotalCount = await deviceQuery.CountAsync(ct);
            var devices = await deviceQuery
                .OrderByDescending(d => d.Criticality)
                .ThenBy(d => d.DeviceName)
                .Skip(devicePagination.Skip)
                .Take(devicePagination.BoundedPageSize)
                .Select(d => new ApprovalDeviceDto(
                    d.DeviceAssetId,
                    d.DeviceName,
                    d.Criticality.ToString(),
                    d.DetectedVersion,
                    d.LastSeenAt,
                    d.OpenVulnerabilityCount))
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
            softwareName ?? decision.SoftwareAsset.Name,
            hasHighestCriticality ? highestCriticality.ToString() : decision.SoftwareAsset.Criticality.ToString(),
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
            deviceVersionCohorts,
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
        Guid tenantId, List<Guid> tenantSoftwareIds, CancellationToken ct)
    {
        return await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.IsActive && tenantSoftwareIds.Contains(i.TenantSoftwareId))
            .Join(
                dbContext.SoftwareVulnerabilityMatches.AsNoTracking().Where(m => m.TenantId == tenantId && m.ResolvedAt == null),
                i => i.SoftwareAssetId,
                m => m.SoftwareAssetId,
                (i, m) => new { i.TenantSoftwareId, m.VulnerabilityDefinitionId }
            )
            .Join(
                dbContext.VulnerabilityDefinitions.AsNoTracking(),
                x => x.VulnerabilityDefinitionId,
                v => v.Id,
                (x, v) => new { x.TenantSoftwareId, v.VendorSeverity }
            )
            .GroupBy(x => x.TenantSoftwareId)
            .Select(g => new { TenantSoftwareId = g.Key, HighestSeverity = g.Max(x => x.VendorSeverity) })
            .ToDictionaryAsync(x => x.TenantSoftwareId, x => x.HighestSeverity, ct);
    }

    private async Task<Dictionary<Guid, string>> GetSoftwareNamesAsync(
        Guid tenantId, List<Guid> tenantSoftwareIds, CancellationToken ct)
    {
        if (tenantSoftwareIds.Count == 0)
        {
            return [];
        }

        return await dbContext.TenantSoftware.AsNoTracking()
            .Where(item => item.TenantId == tenantId && tenantSoftwareIds.Contains(item.Id))
            .Select(item => new { item.Id, Name = item.NormalizedSoftware.CanonicalName })
            .ToDictionaryAsync(item => item.Id, item => item.Name, ct);
    }

    private async Task<Dictionary<Guid, Criticality>> GetHighestCriticalitiesAsync(
        Guid tenantId, List<Guid> tenantSoftwareIds, CancellationToken ct)
    {
        if (tenantSoftwareIds.Count == 0)
        {
            return [];
        }

        return await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.IsActive && tenantSoftwareIds.Contains(item.TenantSoftwareId))
            .Join(
                dbContext.Assets.AsNoTracking().Where(asset => asset.TenantId == tenantId),
                item => item.DeviceAssetId,
                asset => asset.Id,
                (item, asset) => new { item.TenantSoftwareId, asset.Criticality }
            )
            .GroupBy(item => item.TenantSoftwareId)
            .Select(group => new
            {
                TenantSoftwareId = group.Key,
                HighestCriticality = group.Max(item => item.Criticality),
            })
            .ToDictionaryAsync(item => item.TenantSoftwareId, item => item.HighestCriticality, ct);
    }

    private async Task<Dictionary<Guid, int>> GetVulnCountsAsync(
        Guid tenantId, List<Guid> tenantSoftwareIds, CancellationToken ct)
    {
        return await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.IsActive && tenantSoftwareIds.Contains(i.TenantSoftwareId))
            .Join(
                dbContext.SoftwareVulnerabilityMatches.AsNoTracking().Where(m => m.TenantId == tenantId && m.ResolvedAt == null),
                i => i.SoftwareAssetId,
                m => m.SoftwareAssetId,
                (i, m) => new { i.TenantSoftwareId, m.VulnerabilityDefinitionId }
            )
            .Distinct()
            .GroupBy(x => x.TenantSoftwareId)
            .Select(g => new { TenantSoftwareId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantSoftwareId, x => x.Count, ct);
    }

    private async Task<Dictionary<Guid, (string Status, DateTimeOffset DueDate)>> GetSlaInfoAsync(
        Guid tenantId, List<Guid> tenantSoftwareIds, CancellationToken ct)
    {
        var tenantSla = await dbContext.TenantSlaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        if (tenantSla is null)
            return [];

        var vulnData = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.IsActive && tenantSoftwareIds.Contains(i.TenantSoftwareId))
            .Join(
                dbContext.SoftwareVulnerabilityMatches.AsNoTracking().Where(m => m.TenantId == tenantId && m.ResolvedAt == null),
                i => i.SoftwareAssetId,
                m => m.SoftwareAssetId,
                (i, m) => new { i.TenantSoftwareId, m.FirstSeenAt, m.VulnerabilityDefinitionId }
            )
            .Join(
                dbContext.VulnerabilityDefinitions.AsNoTracking(),
                x => x.VulnerabilityDefinitionId,
                v => v.Id,
                (x, v) => new { x.TenantSoftwareId, x.FirstSeenAt, v.VendorSeverity }
            )
            .GroupBy(x => x.TenantSoftwareId)
            .Select(g => new
            {
                TenantSoftwareId = g.Key,
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
            result[item.TenantSoftwareId] = (status.ToString(), dueDate);
        }

        return result;
    }

    private static string NormalizeVersionKey(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim();
    }

    private static string? RestoreVersion(string versionKey)
    {
        return string.IsNullOrWhiteSpace(versionKey) ? null : versionKey;
    }
}
