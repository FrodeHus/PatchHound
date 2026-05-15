using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models.MyTasks;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Core.Services.RiskScoring;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Services;

public class MyTasksQueryService(
    PatchHoundDbContext dbContext,
    SlaService slaService
)
{
    private sealed record MyTaskCaseRow(
        Guid Id,
        Guid SoftwareProductId,
        string Name,
        RemediationWorkflowStage? ActiveWorkflowStage,
        Guid? ActiveWorkflowOwnerTeamId,
        int CriticalityRank,
        int HighestSeverityRank,
        DateTimeOffset? EarliestFirstSeen,
        int OpenAffectedDeviceCount,
        int InstalledDeviceCount,
        decimal? RiskScore,
        int RiskAffectedDeviceCount,
        int OpenExposureCount,
        int CriticalExposureCount,
        int HighExposureCount
    );

    private sealed record MyTaskSortRow(
        Guid Id,
        Guid SoftwareProductId,
        string Name,
        decimal? RiskScore,
        int RiskAffectedDeviceCount,
        int OpenExposureCount,
        int CriticalExposureCount,
        int HighExposureCount
    );

    private sealed record MyTaskDecisionRow(
        Guid RemediationCaseId,
        RemediationOutcome Outcome,
        DecisionApprovalStatus ApprovalStatus,
        DateTimeOffset CreatedAt
    );

    public async Task<MyTasksPageDto> ListAsync(
        Guid tenantId,
        IReadOnlyList<string> userRoles,
        MyTasksQuery query,
        CancellationToken ct
    )
    {
        var roles = ParseUserRoles(userRoles);
        var buckets = BucketsForRoles(roles);
        var sections = new List<MyTaskBucketDto>();

        foreach (var bucket in buckets)
        {
            sections.Add(await BuildBucketAsync(tenantId, bucket, roles, query, ct));
        }

        return new MyTasksPageDto(sections);
    }

    private async Task<MyTaskBucketDto> BuildBucketAsync(
        Guid tenantId,
        string bucket,
        IReadOnlySet<RoleName> roles,
        MyTasksQuery query,
        CancellationToken ct
    )
    {
        var page = query.PageFor(bucket);
        var pageSize = query.BoundedPageSize;
        var skip = (page - 1) * pageSize;
        var sortedRows = await BuildSortedRowsQuery(
                ApplyBucketFilter(BuildBaseCasesQuery(tenantId), tenantId, bucket, roles),
                tenantId)
            .Skip(skip)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = sortedRows.Count > pageSize;
        var pageSortRows = sortedRows.Take(pageSize).ToList();
        var caseIds = pageSortRows.Select(row => row.Id).ToList();
        var rowLookup = caseIds.Count == 0
            ? new Dictionary<Guid, MyTaskCaseRow>()
            : await ProjectCaseRows(
                    BuildBaseCasesQuery(tenantId).Where(c => caseIds.Contains(c.Id)),
                    tenantId)
                .ToDictionaryAsync(row => row.Id, ct);
        var pageRows = pageSortRows
            .Select(row => rowLookup[row.Id])
            .ToList();
        var productIds = pageRows.Select(row => row.SoftwareProductId).Distinct().ToList();
        var latestDecisionsByCase = await LoadLatestDecisionsAsync(tenantId, caseIds, ct);

        var tenantSoftwareRows = productIds.Count == 0
            ? []
            : await dbContext.SoftwareTenantRecords.AsNoTracking()
                .Where(item => item.TenantId == tenantId && productIds.Contains(item.SoftwareProductId))
                .Select(item => new
                {
                    item.SoftwareProductId,
                    item.OwnerTeamRuleId,
                    item.LastSeenAt,
                    item.UpdatedAt,
                })
                .ToListAsync(ct);
        var tenantSoftwareByProduct = tenantSoftwareRows
            .GroupBy(item => item.SoftwareProductId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.LastSeenAt)
                    .ThenByDescending(item => item.UpdatedAt)
                    .First());

        var ownerTeamIds = pageRows
            .Select(row => row.ActiveWorkflowOwnerTeamId)
            .Where(teamId => teamId.HasValue)
            .Select(teamId => teamId!.Value)
            .ToHashSet();
        var ownerTeamNames = ownerTeamIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Teams.AsNoTracking()
                .Where(team => ownerTeamIds.Contains(team.Id))
                .ToDictionaryAsync(team => team.Id, team => team.Name, ct);

        var tenantSla = await dbContext.TenantSlaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(config => config.TenantId == tenantId, ct);
        var now = DateTimeOffset.UtcNow;

        var items = pageRows.Select(row =>
        {
            var riskScore = row.RiskScore is decimal risk ? (double?)risk : null;
            var riskBand = row.RiskScore is decimal riskForBand ? RiskBand.FromScore(riskForBand) : null;

            string? slaStatus = null;
            DateTimeOffset? slaDueDate = null;
            if (tenantSla is not null
                && row.EarliestFirstSeen is DateTimeOffset earliestFirstSeen
                && row.HighestSeverityRank > 0)
            {
                var highestSeverity = SeverityFromRank(row.HighestSeverityRank);
                slaDueDate = slaService.CalculateDueDate(highestSeverity, earliestFirstSeen, tenantSla);
                slaStatus = slaService.GetSlaStatus(earliestFirstSeen, slaDueDate.Value, now).ToString();
            }

            var softwareOwnerTeamId = row.ActiveWorkflowOwnerTeamId;
            var hasWorkflowOwnerTeam = softwareOwnerTeamId.HasValue;
            tenantSoftwareByProduct.TryGetValue(row.SoftwareProductId, out var tenantSoftware);
            var softwareOwnerAssignmentSource = !hasWorkflowOwnerTeam
                ? "Default"
                : tenantSoftware?.OwnerTeamRuleId != null
                    ? "Rule"
                    : "Manual";
            var softwareOwnerTeamName = hasWorkflowOwnerTeam
                && ownerTeamNames.TryGetValue(softwareOwnerTeamId!.Value, out var teamName)
                    ? teamName
                    : null;
            var affectedDeviceCount = row.RiskAffectedDeviceCount > 0
                ? row.RiskAffectedDeviceCount
                : row.OpenAffectedDeviceCount > 0
                    ? row.OpenAffectedDeviceCount
                    : row.InstalledDeviceCount;
            latestDecisionsByCase.TryGetValue(row.Id, out var latestDecision);

            return new MyTaskListItemDto(
                row.Id,
                row.Name,
                CriticalityFromRank(row.CriticalityRank).ToString(),
                latestDecision is not null
                    ? latestDecision.Outcome.ToString()
                    : null,
                latestDecision is not null
                    ? latestDecision.ApprovalStatus.ToString()
                    : null,
                row.OpenExposureCount,
                row.CriticalExposureCount,
                row.HighExposureCount,
                riskScore,
                riskBand,
                slaStatus,
                slaDueDate,
                affectedDeviceCount,
                softwareOwnerTeamName,
                softwareOwnerAssignmentSource,
                row.ActiveWorkflowStage?.ToString()
            );
        }).ToList();

        return new MyTaskBucketDto(bucket, items, page, pageSize, hasMore);
    }

    private IQueryable<RemediationCase> BuildBaseCasesQuery(Guid tenantId) =>
        dbContext.RemediationCases.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .Where(c =>
                dbContext.DeviceVulnerabilityExposures.Any(e =>
                    e.TenantId == tenantId
                    && e.SoftwareProductId == c.SoftwareProductId
                    && e.Status == ExposureStatus.Open)
                || dbContext.InstalledSoftware.Any(i =>
                    i.TenantId == tenantId
                    && i.SoftwareProductId == c.SoftwareProductId));

    private IQueryable<MyTaskSortRow> BuildSortedRowsQuery(IQueryable<RemediationCase> casesQuery, Guid tenantId) =>
        casesQuery
            .Select(c => new
            {
                c.Id,
                c.SoftwareProductId,
                Name = c.SoftwareProduct.Name,
                RiskScore = dbContext.SoftwareRiskScores.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                    .Select(s => (decimal?)s.OverallScore)
                    .FirstOrDefault(),
                RiskAffectedDeviceCount = dbContext.SoftwareRiskScores.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                    .Select(s => s.AffectedDeviceCount)
                    .FirstOrDefault(),
                OpenExposureCount = dbContext.SoftwareRiskScores.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                    .Select(s => s.OpenExposureCount)
                    .FirstOrDefault(),
                CriticalExposureCount = dbContext.SoftwareRiskScores.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                    .Select(s => s.CriticalExposureCount)
                    .FirstOrDefault(),
                HighExposureCount = dbContext.SoftwareRiskScores.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                    .Select(s => s.HighExposureCount)
                    .FirstOrDefault(),
            })
            .OrderByDescending(row => row.CriticalExposureCount > 0
                ? 4
                : row.HighExposureCount > 0
                    ? 3
                    : row.OpenExposureCount > 0
                        ? 2
                        : 0)
            .ThenByDescending(row => row.CriticalExposureCount)
            .ThenByDescending(row => row.HighExposureCount)
            .ThenByDescending(row => row.RiskAffectedDeviceCount)
            .ThenByDescending(row => row.RiskScore ?? 0)
            .ThenBy(row => row.Name)
            .Select(row => new MyTaskSortRow(
                row.Id,
                row.SoftwareProductId,
                row.Name,
                row.RiskScore,
                row.RiskAffectedDeviceCount,
                row.OpenExposureCount,
                row.CriticalExposureCount,
                row.HighExposureCount
            ));

    private IQueryable<MyTaskCaseRow> ProjectCaseRows(IQueryable<RemediationCase> casesQuery, Guid tenantId) =>
        casesQuery.Select(c => new MyTaskCaseRow(
            c.Id,
            c.SoftwareProductId,
            c.SoftwareProduct.Name,
            dbContext.RemediationWorkflows.AsNoTracking()
                .Where(w => w.TenantId == tenantId
                    && w.RemediationCaseId == c.Id
                    && w.Status == RemediationWorkflowStatus.Active)
                .OrderByDescending(w => w.UpdatedAt)
                .Select(w => (RemediationWorkflowStage?)w.CurrentStage)
                .FirstOrDefault(),
            dbContext.RemediationWorkflows.AsNoTracking()
                .Where(w => w.TenantId == tenantId
                    && w.RemediationCaseId == c.Id
                    && w.Status == RemediationWorkflowStatus.Active)
                .OrderByDescending(w => w.UpdatedAt)
                .Select(w => w.SoftwareOwnerTeamId)
                .FirstOrDefault(),
            dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Where(e => e.TenantId == tenantId
                    && e.SoftwareProductId == c.SoftwareProductId
                    && e.Status == ExposureStatus.Open)
                .Select(e =>
                    (int?)(e.Device.Criticality == Criticality.Critical ? 4 :
                    e.Device.Criticality == Criticality.High ? 3 :
                    e.Device.Criticality == Criticality.Medium ? 2 :
                    e.Device.Criticality == Criticality.Low ? 1 : 0))
                .Max() ?? 0,
            dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Where(e => e.TenantId == tenantId
                    && e.SoftwareProductId == c.SoftwareProductId
                    && e.Status == ExposureStatus.Open)
                .Select(e =>
                    (int?)(e.Vulnerability.VendorSeverity == Severity.Critical ? 4 :
                    e.Vulnerability.VendorSeverity == Severity.High ? 3 :
                    e.Vulnerability.VendorSeverity == Severity.Medium ? 2 :
                    e.Vulnerability.VendorSeverity == Severity.Low ? 1 : 0))
                .Max() ?? 0,
            dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Where(e => e.TenantId == tenantId
                    && e.SoftwareProductId == c.SoftwareProductId
                    && e.Status == ExposureStatus.Open)
                .Select(e => (DateTimeOffset?)e.FirstObservedAt)
                .Min(),
            dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                .Where(e => e.TenantId == tenantId
                    && e.SoftwareProductId == c.SoftwareProductId
                    && e.Status == ExposureStatus.Open)
                .Select(e => e.DeviceId)
                .Distinct()
                .Count(),
            dbContext.InstalledSoftware.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.SoftwareProductId == c.SoftwareProductId)
                .Select(i => i.DeviceId)
                .Distinct()
                .Count(),
            dbContext.SoftwareRiskScores.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                .Select(s => (decimal?)s.OverallScore)
                .FirstOrDefault(),
            dbContext.SoftwareRiskScores.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                .Select(s => s.AffectedDeviceCount)
                .FirstOrDefault(),
            dbContext.SoftwareRiskScores.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                .Select(s => s.OpenExposureCount)
                .FirstOrDefault(),
            dbContext.SoftwareRiskScores.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                .Select(s => s.CriticalExposureCount)
                .FirstOrDefault(),
            dbContext.SoftwareRiskScores.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.SoftwareProductId == c.SoftwareProductId)
                .Select(s => s.HighExposureCount)
                .FirstOrDefault()
        ));

    private async Task<Dictionary<Guid, MyTaskDecisionRow>> LoadLatestDecisionsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> caseIds,
        CancellationToken ct
    )
    {
        if (caseIds.Count == 0)
        {
            return [];
        }

        var decisions = await dbContext.RemediationDecisions.AsNoTracking()
            .Where(d => d.TenantId == tenantId
                && caseIds.Contains(d.RemediationCaseId)
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
            .Select(d => new MyTaskDecisionRow(
                d.RemediationCaseId,
                d.Outcome,
                d.ApprovalStatus,
                d.CreatedAt
            ))
            .ToListAsync(ct);

        return decisions
            .GroupBy(decision => decision.RemediationCaseId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(decision => decision.CreatedAt)
                    .First());
    }

    private IQueryable<RemediationCase> ApplyBucketFilter(
        IQueryable<RemediationCase> query,
        Guid tenantId,
        string bucket,
        IReadOnlySet<RoleName> roles
    ) =>
        bucket switch
        {
            MyTaskBuckets.Recommendation => query.Where(c =>
                !dbContext.RemediationWorkflows.Any(w =>
                    w.TenantId == tenantId
                    && w.RemediationCaseId == c.Id
                    && w.Status == RemediationWorkflowStatus.Active)
                || dbContext.RemediationWorkflows.Any(w =>
                    w.TenantId == tenantId
                    && w.RemediationCaseId == c.Id
                    && w.Status == RemediationWorkflowStatus.Active
                    && w.CurrentStage == RemediationWorkflowStage.SecurityAnalysis
                    && !dbContext.AnalystRecommendations.Any(r =>
                        r.TenantId == tenantId
                        && r.RemediationWorkflowId == w.Id))),
            MyTaskBuckets.Decision => query.Where(c =>
                dbContext.RemediationWorkflows.Any(w =>
                    w.TenantId == tenantId
                    && w.RemediationCaseId == c.Id
                    && w.Status == RemediationWorkflowStatus.Active
                    && w.CurrentStage == RemediationWorkflowStage.RemediationDecision)
                && !dbContext.RemediationDecisions.Any(d =>
                    d.TenantId == tenantId
                    && d.RemediationCaseId == c.Id
                    && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                    && d.ApprovalStatus != DecisionApprovalStatus.Expired)),
            MyTaskBuckets.Approval => ApplyApprovalRoleFilter(query.Where(c =>
                dbContext.RemediationWorkflows.Any(w =>
                    w.TenantId == tenantId
                    && w.RemediationCaseId == c.Id
                    && w.Status == RemediationWorkflowStatus.Active
                    && w.CurrentStage == RemediationWorkflowStage.Approval)
                && dbContext.RemediationDecisions
                    .Where(d => d.TenantId == tenantId
                        && d.RemediationCaseId == c.Id
                        && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                        && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => (DecisionApprovalStatus?)d.ApprovalStatus)
                    .FirstOrDefault() == DecisionApprovalStatus.PendingApproval), tenantId, roles),
            _ => query.Where(_ => false),
        };

    private IQueryable<RemediationCase> ApplyApprovalRoleFilter(
        IQueryable<RemediationCase> query,
        Guid tenantId,
        IReadOnlySet<RoleName> roles
    )
    {
        if (roles.Contains(RoleName.GlobalAdmin))
        {
            return query;
        }

        var canSecurityApprove = roles.Contains(RoleName.SecurityManager);
        var canTechnicalApprove = roles.Contains(RoleName.TechnicalManager);

        return query
            .Select(c => new
            {
                Case = c,
                LatestOutcome = dbContext.RemediationDecisions
                    .Where(d => d.TenantId == tenantId
                        && d.RemediationCaseId == c.Id
                        && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                        && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => (RemediationOutcome?)d.Outcome)
                    .FirstOrDefault(),
            })
            .Where(c =>
                (canSecurityApprove
                    && (c.LatestOutcome == RemediationOutcome.RiskAcceptance
                        || c.LatestOutcome == RemediationOutcome.AlternateMitigation))
                || (canTechnicalApprove
                    && (c.LatestOutcome == RemediationOutcome.ApprovedForPatching
                        || c.LatestOutcome == RemediationOutcome.PatchingDeferred)))
            .Select(c => c.Case);
    }

    private static List<string> BucketsForRoles(IReadOnlySet<RoleName> roles)
    {
        if (roles.Contains(RoleName.GlobalAdmin))
        {
            return [MyTaskBuckets.Recommendation, MyTaskBuckets.Decision, MyTaskBuckets.Approval];
        }

        var buckets = new List<string>();
        if (roles.Contains(RoleName.SecurityAnalyst) || roles.Contains(RoleName.SecurityManager))
        {
            buckets.Add(MyTaskBuckets.Recommendation);
        }

        if (roles.Contains(RoleName.AssetOwner))
        {
            buckets.Add(MyTaskBuckets.Decision);
        }

        if (roles.Contains(RoleName.SecurityManager) || roles.Contains(RoleName.TechnicalManager))
        {
            buckets.Add(MyTaskBuckets.Approval);
        }

        return buckets;
    }

    private static HashSet<RoleName> ParseUserRoles(IReadOnlyList<string> userRoles) =>
        userRoles
            .Where(role => Enum.TryParse<RoleName>(role, true, out _))
            .Select(role => Enum.Parse<RoleName>(role, true))
            .ToHashSet();

    private static Criticality CriticalityFromRank(int rank) =>
        rank switch
        {
            >= 4 => Criticality.Critical,
            3 => Criticality.High,
            2 => Criticality.Medium,
            _ => Criticality.Low,
        };

    private static Severity SeverityFromRank(int rank) =>
        rank switch
        {
            >= 4 => Severity.Critical,
            3 => Severity.High,
            2 => Severity.Medium,
            _ => Severity.Low,
        };
}
