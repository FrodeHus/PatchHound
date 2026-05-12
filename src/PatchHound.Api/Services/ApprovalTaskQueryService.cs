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
            // Search via RemediationCase → SoftwareProduct
            var matchingCaseIds = await dbContext.RemediationCases.AsNoTracking()
                .Where(rc => rc.TenantId == tenantId)
                .Join(dbContext.SoftwareProducts.AsNoTracking(),
                    rc => rc.SoftwareProductId,
                    sp => sp.Id,
                    (rc, sp) => new { rc.Id, sp.Name, sp.Vendor })
                .Where(x =>
                    x.Name.ToLower().Contains(term)
                    || (x.Vendor != null && x.Vendor.ToLower().Contains(term)))
                .Select(x => x.Id)
                .ToListAsync(ct);

            query = query.Where(at =>
                matchingCaseIds.Contains(at.RemediationDecision.RemediationCaseId));
        }

        var totalCount = await query.CountAsync(ct);

        var tasks = await query
            .OrderByDescending(at => at.Status == ApprovalTaskStatus.Pending ? 1 : 0)
            .ThenByDescending(at => at.CreatedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var caseIds = tasks.Select(t => t.RemediationDecision.RemediationCaseId).Distinct().ToList();
        var softwareNames = await GetSoftwareNamesAsync(tenantId, caseIds, ct);
        var highestSeverities = await GetHighestSeveritiesAsync(tenantId, caseIds, ct);
        var highestCriticalities = await GetHighestCriticalitiesAsync(tenantId, caseIds, ct);
        var vulnCounts = await GetVulnCountsAsync(tenantId, caseIds, ct);
        var slaInfo = await GetSlaInfoAsync(tenantId, caseIds, ct);

        // Decided-by user names
        var decidedByIds = tasks.Select(t => t.RemediationDecision.DecidedBy).Distinct().ToList();
        var userNames = await dbContext.Users.AsNoTracking()
            .Where(u => decidedByIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = tasks.Select(t =>
        {
            var caseId = t.RemediationDecision.RemediationCaseId;
            softwareNames.TryGetValue(caseId, out var softwareName);
            var hasSeverity = highestSeverities.TryGetValue(caseId, out var severity);
            var hasCriticality = highestCriticalities.TryGetValue(caseId, out var criticality);
            vulnCounts.TryGetValue(caseId, out var vc);
            var hasSla = slaInfo.TryGetValue(caseId, out var sla);
            userNames.TryGetValue(t.RemediationDecision.DecidedBy, out var decidedByName);

            return new ApprovalTaskListItemDto(
                t.Id,
                t.RemediationDecision.RemediationCaseId,
                t.Type.ToString(),
                t.Status.ToString(),
                softwareName ?? "Unknown software",
                hasCriticality ? criticality.ToString() : "Unknown",
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
            .FirstOrDefaultAsync(at =>
                at.Id == approvalTaskId
                && at.TenantId == tenantId
                && at.VisibleRoles.Any(role => roleNames.Contains(role.Role)),
                ct);

        if (task is null) return null;

        var decision = task.RemediationDecision;
        var remediationCaseId = decision.RemediationCaseId;

        var softwareNames = await GetSoftwareNamesAsync(tenantId, [remediationCaseId], ct);
        softwareNames.TryGetValue(remediationCaseId, out var softwareName);

        // Highest severity
        var highestSeverities = await GetHighestSeveritiesAsync(tenantId, [remediationCaseId], ct);
        var hasHighestSeverity = highestSeverities.TryGetValue(remediationCaseId, out var highestSeverity);
        var highestCriticalities = await GetHighestCriticalitiesAsync(tenantId, [remediationCaseId], ct);
        var hasHighestCriticality = highestCriticalities.TryGetValue(remediationCaseId, out var highestCriticality);

        // SLA
        var slaInfo = await GetSlaInfoAsync(tenantId, [remediationCaseId], ct);
        var hasSla = slaInfo.TryGetValue(remediationCaseId, out var sla);

        // Risk score — read from SoftwareRiskScores via RemediationCase.SoftwareProductId
        var remCase = await dbContext.RemediationCases.AsNoTracking()
            .FirstOrDefaultAsync(rc => rc.Id == remediationCaseId && rc.TenantId == tenantId, ct);
        double? riskScore = null;
        string? riskBand = null;
        if (remCase is not null)
        {
            var softwareScore = await dbContext.SoftwareRiskScores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SoftwareProductId == remCase.SoftwareProductId, ct);
            if (softwareScore is not null)
            {
                riskScore = (double)softwareScore.OverallScore;
                riskBand = ResolveRiskBand(softwareScore.OverallScore);
            }
        }

        // Decided-by name
        var decidedByUser = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == decision.DecidedBy, ct);

        // Vulnerabilities — query DVE → Vulnerability for this remediation case's software product
        PagedVulnerabilityList vulnList;
        if (remCase is not null)
        {
            var vulnQuery = dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Where(e => e.TenantId == tenantId
                    && e.SoftwareProductId == remCase.SoftwareProductId
                    && e.Status == ExposureStatus.Open)
                .Select(e => e.VulnerabilityId)
                .Distinct();

            var vulnTotalCount = await vulnQuery.CountAsync(ct);
            var vulnIds = await vulnQuery
                .Skip(vulnPagination.Skip)
                .Take(vulnPagination.BoundedPageSize)
                .ToListAsync(ct);

            var vulnItems = await dbContext.Vulnerabilities.AsNoTracking()
                .Where(v => vulnIds.Contains(v.Id))
                .Select(v => new ApprovalVulnDto(
                    v.Id, v.ExternalId, v.Title, v.VendorSeverity.ToString(),
                    (double?)v.CvssScore, null, false, null))
                .ToListAsync(ct);

            vulnList = new PagedVulnerabilityList(vulnItems, vulnTotalCount, vulnPagination.Page, vulnPagination.BoundedPageSize);
        }
        else
        {
            vulnList = new PagedVulnerabilityList([], 0, vulnPagination.Page, vulnPagination.BoundedPageSize);
        }

        // Devices in scope — query InstalledSoftware → Device for this remediation case's software product
        var deviceVersionCohorts = new List<ApprovalDeviceVersionCohortDto>();
        PagedDeviceList? deviceList = null;

        if (remCase is not null)
        {
            // Version cohorts (all pages)
            var installRows = await dbContext.InstalledSoftware.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.SoftwareProductId == remCase.SoftwareProductId)
                .Select(i => new { i.Version, i.FirstSeenAt, i.LastSeenAt, i.DeviceId })
                .ToListAsync(ct);

            deviceVersionCohorts = installRows
                .GroupBy(r => NormalizeVersionKey(r.Version))
                .Select(g => new ApprovalDeviceVersionCohortDto(
                    RestoreVersion(g.Key),
                    g.Count(),
                    g.Select(r => r.DeviceId).Distinct().Count(),
                    0, // open vuln count per-cohort not needed for this surface
                    g.Min(r => r.FirstSeenAt),
                    g.Max(r => r.LastSeenAt)
                ))
                .OrderBy(c => c.Version)
                .ToList();

            if (devicePagination is not null)
            {
                var deviceQuery = dbContext.InstalledSoftware.AsNoTracking()
                    .Where(i => i.TenantId == tenantId && i.SoftwareProductId == remCase.SoftwareProductId);

                if (!string.IsNullOrWhiteSpace(deviceVersion))
                {
                    deviceQuery = deviceQuery.Where(i => i.Version == deviceVersion);
                }

                var deviceTotalCount = await deviceQuery.Select(i => i.DeviceId).Distinct().CountAsync(ct);
                var deviceIds = await deviceQuery
                    .Select(i => i.DeviceId)
                    .Distinct()
                    .Skip(devicePagination.Skip)
                    .Take(devicePagination.BoundedPageSize)
                    .ToListAsync(ct);

                var deviceMap = await dbContext.Devices.AsNoTracking()
                    .Where(d => deviceIds.Contains(d.Id))
                    .Select(d => new { d.Id, Name = d.ComputerDnsName ?? d.Name, d.Criticality })
                    .ToDictionaryAsync(d => d.Id, ct);

                var openCountPerDevice = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                    .Where(e => e.TenantId == tenantId
                        && e.SoftwareProductId == remCase.SoftwareProductId
                        && e.Status == ExposureStatus.Open
                        && deviceIds.Contains(e.DeviceId))
                    .GroupBy(e => e.DeviceId)
                    .Select(g => new { DeviceId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(r => r.DeviceId, r => r.Count, ct);

                var latestSeenPerDevice = await dbContext.InstalledSoftware.AsNoTracking()
                    .Where(i => i.TenantId == tenantId
                        && i.SoftwareProductId == remCase.SoftwareProductId
                        && deviceIds.Contains(i.DeviceId))
                    .GroupBy(i => i.DeviceId)
                    .Select(g => new { DeviceId = g.Key, LastSeen = g.Max(i => i.LastSeenAt), Version = g.Max(i => i.Version) })
                    .ToDictionaryAsync(r => r.DeviceId, ct);

                var deviceItems = deviceIds
                    .Where(id => deviceMap.ContainsKey(id))
                    .Select(id =>
                    {
                        deviceMap.TryGetValue(id, out var dev);
                        latestSeenPerDevice.TryGetValue(id, out var seen);
                        openCountPerDevice.TryGetValue(id, out var openCount);
                        return new ApprovalDeviceDto(
                            id,
                            dev?.Name ?? "Unknown",
                            dev?.Criticality.ToString() ?? "Unknown",
                            seen?.Version,
                            seen?.LastSeen ?? DateTimeOffset.MinValue,
                            openCount
                        );
                    })
                    .ToList();

                deviceList = new PagedDeviceList(deviceItems, deviceTotalCount, devicePagination.Page, devicePagination.BoundedPageSize);
            }
        }
        else if (devicePagination is not null)
        {
            deviceList = new PagedDeviceList([], 0, devicePagination.Page, devicePagination.BoundedPageSize);
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
                && (
                    (a.EntityType == nameof(RemediationDecision) && a.EntityId == decision.Id)
                    || (a.EntityType == nameof(ApprovalTask) && a.EntityId == task.Id)
                ))
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
            softwareName ?? "Unknown software",
            hasHighestCriticality ? highestCriticality.ToString() : "Unknown",
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
            return canonicalName;
        if (!string.IsNullOrWhiteSpace(fallbackName))
            return fallbackName;
        return "Unknown software";
    }

    private async Task<Dictionary<Guid, Severity>> GetHighestSeveritiesAsync(
        Guid tenantId, List<Guid> remediationCaseIds, CancellationToken ct)
    {
        if (remediationCaseIds.Count == 0) return [];

        // Join RemediationCase → DVE via SoftwareProductId, get max VendorSeverity per case
        var rows = await dbContext.RemediationCases.AsNoTracking()
            .Where(rc => rc.TenantId == tenantId && remediationCaseIds.Contains(rc.Id))
            .Join(dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                    .Where(e => e.TenantId == tenantId && e.Status == ExposureStatus.Open),
                rc => rc.SoftwareProductId,
                e => e.SoftwareProductId,
                (rc, e) => new { CaseId = rc.Id, e.Vulnerability.VendorSeverity })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.CaseId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.VendorSeverity));
    }

    private async Task<Dictionary<Guid, string>> GetSoftwareNamesAsync(
        Guid tenantId, List<Guid> remediationCaseIds, CancellationToken ct)
    {
        if (remediationCaseIds.Count == 0)
            return [];

        return await dbContext.RemediationCases.AsNoTracking()
            .Where(rc => rc.TenantId == tenantId && remediationCaseIds.Contains(rc.Id))
            .Join(dbContext.SoftwareProducts.AsNoTracking(),
                rc => rc.SoftwareProductId,
                sp => sp.Id,
                (rc, sp) => new { CaseId = rc.Id, sp.Name })
            .ToDictionaryAsync(x => x.CaseId, x => x.Name, ct);
    }

    private async Task<Dictionary<Guid, Criticality>> GetHighestCriticalitiesAsync(
        Guid tenantId, List<Guid> remediationCaseIds, CancellationToken ct)
    {
        if (remediationCaseIds.Count == 0) return [];

        // RC → InstalledSoftware → Device to get max Criticality per case
        var rows = await dbContext.RemediationCases.AsNoTracking()
            .Where(rc => rc.TenantId == tenantId && remediationCaseIds.Contains(rc.Id))
            .Join(dbContext.InstalledSoftware.AsNoTracking()
                    .Where(i => i.TenantId == tenantId),
                rc => rc.SoftwareProductId,
                i => i.SoftwareProductId,
                (rc, i) => new { CaseId = rc.Id, i.DeviceId })
            .Join(dbContext.Devices.AsNoTracking()
                    .Where(d => d.TenantId == tenantId),
                x => x.DeviceId,
                d => d.Id,
                (x, d) => new { x.CaseId, d.Criticality })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.CaseId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Criticality));
    }

    private async Task<Dictionary<Guid, int>> GetVulnCountsAsync(
        Guid tenantId, List<Guid> remediationCaseIds, CancellationToken ct)
    {
        if (remediationCaseIds.Count == 0) return [];

        // Count distinct open VulnerabilityIds per case via DVE
        var rows = await dbContext.RemediationCases.AsNoTracking()
            .Where(rc => rc.TenantId == tenantId && remediationCaseIds.Contains(rc.Id))
            .Join(dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                    .Where(e => e.TenantId == tenantId && e.Status == ExposureStatus.Open),
                rc => rc.SoftwareProductId,
                e => e.SoftwareProductId,
                (rc, e) => new { CaseId = rc.Id, e.VulnerabilityId })
            .Distinct()
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.CaseId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.VulnerabilityId).Distinct().Count());
    }

    private async Task<Dictionary<Guid, (string Status, DateTimeOffset DueDate)>> GetSlaInfoAsync(
        Guid tenantId, List<Guid> remediationCaseIds, CancellationToken ct)
    {
        if (remediationCaseIds.Count == 0) return [];

        var highestSeverities = await GetHighestSeveritiesAsync(tenantId, remediationCaseIds, ct);
        if (highestSeverities.Count == 0) return [];

        var caseDates = await dbContext.RemediationCases.AsNoTracking()
            .Where(rc => rc.TenantId == tenantId && remediationCaseIds.Contains(rc.Id))
            .Select(rc => new { rc.Id, rc.CreatedAt })
            .ToDictionaryAsync(rc => rc.Id, rc => rc.CreatedAt, ct);

        var tenantSla = await dbContext.TenantSlaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<Guid, (string Status, DateTimeOffset DueDate)>();
        foreach (var (caseId, severity) in highestSeverities)
        {
            if (!caseDates.TryGetValue(caseId, out var createdAt)) continue;
            var dueDate = slaService.CalculateDueDate(severity, createdAt, tenantSla);
            var status = slaService.GetSlaStatus(createdAt, dueDate, now);
            result[caseId] = (status.ToString(), dueDate);
        }
        return result;
    }

    private static string ResolveRiskBand(decimal score) =>
        score >= 900m ? "Critical" : score >= 750m ? "High" : score >= 500m ? "Medium" : "Low";

    private static string NormalizeVersionKey(string? version)        => string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim();

    private static string? RestoreVersion(string versionKey)
        => string.IsNullOrWhiteSpace(versionKey) ? null : versionKey;
}
