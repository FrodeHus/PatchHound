using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models;
using PatchHound.Api.Models.ApprovalTasks;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Services;

public class ApprovalTaskQueryService(
    PatchHoundDbContext dbContext
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
                ResolveDisplaySoftwareName(softwareName, t.RemediationDecision.SoftwareAsset.Name),
                hasCriticality ? criticality.ToString() : t.RemediationDecision.SoftwareAsset.Criticality.ToString(),
                t.RemediationDecision.Outcome.ToString(),
                hasSeverity ? severity.ToString() : "Unknown",
                vc,
                t.ExpiresAt,
                t.RemediationDecision.MaintenanceWindowDate,
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
        // Phase 4 debt (#17): SoftwareVulnerabilityMatch + VulnerabilityDefinition + TenantVulnerability removed by canonical cleanup; remediation-surface rewrite restores this.
        var vulnList = new PagedVulnerabilityList([], 0, vulnPagination.Page, vulnPagination.BoundedPageSize);

        // Devices in scope for the approval's software scope
        var deviceVersionCohorts = new List<ApprovalDeviceVersionCohortDto>();
        PagedDeviceList? deviceList = null;
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
        // Phase 4 debt (#17): SoftwareVulnerabilityMatch removed by canonical cleanup; remediation-surface rewrite restores this.
        var openMatchRows = Array.Empty<object>().Select(_ => new { SoftwareAssetId = Guid.Empty, VulnerabilityDefinitionId = Guid.Empty }).ToList();

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

        if (devicePagination is not null)
        {
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
                    // Phase 4 debt (#17): VulnerabilityAsset removed by canonical cleanup; remediation-surface rewrite restores this.
                    OpenVulnerabilityCount = 0,
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
        var decisionWorkflowId = decision.RemediationWorkflowId;
        var recommendations = decisionWorkflowId is Guid resolvedDecisionWorkflowId
            ? await dbContext.AnalystRecommendations.AsNoTracking()
                .Where(r =>
                    r.TenantId == tenantId
                    && r.RemediationWorkflowId == resolvedDecisionWorkflowId)
                .OrderByDescending(r => r.CreatedAt)
                .Take(1)
                .Select(r => new ApprovalRecommendationDto(
                    r.Id, r.RecommendedOutcome.ToString(), r.Rationale,
                    r.PriorityOverride, r.AnalystId, r.CreatedAt))
                .ToListAsync(ct)
            : [];

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
            return AuditTimelineMapper.ToDto(a, name);
        }).ToList();

        return new ApprovalTaskDetailDto(
            task.Id,
            task.Type.ToString(),
            task.Status.ToString(),
            task.RemediationDecisionId,
            ResolveDisplaySoftwareName(softwareName, decision.SoftwareAsset.Name),
            hasHighestCriticality ? highestCriticality.ToString() : decision.SoftwareAsset.Criticality.ToString(),
            decision.Outcome.ToString(),
            decision.Justification,
            hasHighestSeverity ? highestSeverity.ToString() : "Unknown",
            task.RequiresJustification,
            task.ExpiresAt,
            decision.MaintenanceWindowDate,
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

    private Task<Dictionary<Guid, Severity>> GetHighestSeveritiesAsync(
        Guid tenantId, List<Guid> tenantSoftwareIds, CancellationToken ct)
        // Phase 4 debt (#17): SoftwareVulnerabilityMatch + VulnerabilityDefinition removed by canonical cleanup; remediation-surface rewrite restores this.
        => Task.FromResult(new Dictionary<Guid, Severity>());

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

    private Task<Dictionary<Guid, int>> GetVulnCountsAsync(
        Guid tenantId, List<Guid> tenantSoftwareIds, CancellationToken ct)
        // Phase 4 debt (#17): SoftwareVulnerabilityMatch removed by canonical cleanup; remediation-surface rewrite restores this.
        => Task.FromResult(new Dictionary<Guid, int>());

    private Task<Dictionary<Guid, (string Status, DateTimeOffset DueDate)>> GetSlaInfoAsync(
        Guid tenantId, List<Guid> tenantSoftwareIds, CancellationToken ct)
        // Phase 4 debt (#17): SoftwareVulnerabilityMatch + VulnerabilityDefinition removed by canonical cleanup; remediation-surface rewrite restores this.
        => Task.FromResult(new Dictionary<Guid, (string Status, DateTimeOffset DueDate)>());

    private static string NormalizeVersionKey(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim();
    }

    private static string? RestoreVersion(string versionKey)
    {
        return string.IsNullOrWhiteSpace(versionKey) ? null : versionKey;
    }
}
