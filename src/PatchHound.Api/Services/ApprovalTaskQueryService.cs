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

        // Risk score — TODO Phase 5 (#17): re-implement via RemediationCase risk model
        double? riskScore = null;
        string? riskBand = null;

        // Decided-by name
        var decidedByUser = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == decision.DecidedBy, ct);

        // Vulnerabilities — Phase 4 debt (#17): SoftwareVulnerabilityMatch removed by canonical cleanup
        var vulnList = new PagedVulnerabilityList([], 0, vulnPagination.Page, vulnPagination.BoundedPageSize);

        // Devices in scope — Phase 4 debt (#17): still uses TenantSoftwareId on NormalizedSoftwareInstallations
        // We derive tenantSoftwareId by joining RemediationCase → SoftwareProduct → NormalizedSoftware via CanonicalProductKey
        var deviceVersionCohorts = new List<ApprovalDeviceVersionCohortDto>();
        PagedDeviceList? deviceList = null;

        // TODO Phase 5 (#17): restore device listing via new canonical device-software join path
        var installationRows = Array.Empty<object>()
            .Select(_ => new { DeviceAssetId = Guid.Empty, DetectedVersion = (string?)null, FirstSeenAt = DateTimeOffset.MinValue, LastSeenAt = DateTimeOffset.MinValue, SoftwareAssetId = Guid.Empty })
            .ToList();

        var openMatchRows = Array.Empty<object>()
            .Select(_ => new { SoftwareAssetId = Guid.Empty, VulnerabilityDefinitionId = Guid.Empty })
            .ToList();

        if (devicePagination is not null)
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

    private Task<Dictionary<Guid, Severity>> GetHighestSeveritiesAsync(
        Guid tenantId, List<Guid> remediationCaseIds, CancellationToken ct)
        // Phase 4 debt (#17): SoftwareVulnerabilityMatch + VulnerabilityDefinition removed by canonical cleanup
        => Task.FromResult(new Dictionary<Guid, Severity>());

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
        // TODO Phase 5 (#17): re-implement device criticality lookup via new canonical device-software join path
        return new Dictionary<Guid, Criticality>();
    }

    private Task<Dictionary<Guid, int>> GetVulnCountsAsync(
        Guid tenantId, List<Guid> remediationCaseIds, CancellationToken ct)
        // Phase 4 debt (#17): SoftwareVulnerabilityMatch removed by canonical cleanup
        => Task.FromResult(new Dictionary<Guid, int>());

    private Task<Dictionary<Guid, (string Status, DateTimeOffset DueDate)>> GetSlaInfoAsync(
        Guid tenantId, List<Guid> remediationCaseIds, CancellationToken ct)
        // Phase 4 debt (#17): SoftwareVulnerabilityMatch + VulnerabilityDefinition removed by canonical cleanup
        => Task.FromResult(new Dictionary<Guid, (string Status, DateTimeOffset DueDate)>());

    private static string NormalizeVersionKey(string? version)
        => string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim();

    private static string? RestoreVersion(string versionKey)
        => string.IsNullOrWhiteSpace(versionKey) ? null : versionKey;
}
