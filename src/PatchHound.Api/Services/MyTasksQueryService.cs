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
        Guid? LatestDecisionId,
        RemediationOutcome? LatestOutcome,
        DecisionApprovalStatus? LatestApprovalStatus,
        Guid? ActiveWorkflowId,
        RemediationWorkflowStage? ActiveWorkflowStage,
        Guid? ActiveWorkflowOwnerTeamId,
        bool HasAnalystRecommendation,
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
        var rows = await ApplyBucketFilter(BuildBaseQuery(tenantId), bucket, roles)
            .OrderByDescending(c => c.CriticalityRank)
            .ThenByDescending(c => c.RiskAffectedDeviceCount > 0
                ? c.RiskAffectedDeviceCount
                : c.OpenAffectedDeviceCount > 0
                    ? c.OpenAffectedDeviceCount
                    : c.InstalledDeviceCount)
            .ThenByDescending(c => c.CriticalExposureCount)
            .ThenByDescending(c => c.HighestSeverityRank)
            .ThenBy(c => c.Name)
            .Skip(skip)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var pageRows = rows.Take(pageSize).ToList();
        var productIds = pageRows.Select(row => row.SoftwareProductId).Distinct().ToList();

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

            return new MyTaskListItemDto(
                row.Id,
                row.Name,
                CriticalityFromRank(row.CriticalityRank).ToString(),
                row.LatestOutcome?.ToString(),
                row.LatestApprovalStatus?.ToString(),
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

    private IQueryable<MyTaskCaseRow> BuildBaseQuery(Guid tenantId)
    {
        var casesQuery = dbContext.RemediationCases.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .Where(c =>
                dbContext.DeviceVulnerabilityExposures.Any(e =>
                    e.TenantId == tenantId
                    && e.SoftwareProductId == c.SoftwareProductId
                    && e.Status == ExposureStatus.Open)
                || dbContext.InstalledSoftware.Any(i =>
                    i.TenantId == tenantId
                    && i.SoftwareProductId == c.SoftwareProductId));

        return casesQuery.Select(c => new MyTaskCaseRow(
            c.Id,
            c.SoftwareProductId,
            c.SoftwareProduct.Name,
            dbContext.RemediationDecisions.AsNoTracking()
                .Where(d => d.TenantId == tenantId
                    && d.RemediationCaseId == c.Id
                    && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                    && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => (Guid?)d.Id)
                .FirstOrDefault(),
            dbContext.RemediationDecisions.AsNoTracking()
                .Where(d => d.TenantId == tenantId
                    && d.RemediationCaseId == c.Id
                    && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                    && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => (RemediationOutcome?)d.Outcome)
                .FirstOrDefault(),
            dbContext.RemediationDecisions.AsNoTracking()
                .Where(d => d.TenantId == tenantId
                    && d.RemediationCaseId == c.Id
                    && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                    && d.ApprovalStatus != DecisionApprovalStatus.Expired)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => (DecisionApprovalStatus?)d.ApprovalStatus)
                .FirstOrDefault(),
            dbContext.RemediationWorkflows.AsNoTracking()
                .Where(w => w.TenantId == tenantId
                    && w.RemediationCaseId == c.Id
                    && w.Status == RemediationWorkflowStatus.Active)
                .OrderByDescending(w => w.UpdatedAt)
                .Select(w => (Guid?)w.Id)
                .FirstOrDefault(),
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
            dbContext.RemediationWorkflows.AsNoTracking()
                .Where(w => w.TenantId == tenantId
                    && w.RemediationCaseId == c.Id
                    && w.Status == RemediationWorkflowStatus.Active)
                .OrderByDescending(w => w.UpdatedAt)
                .Take(1)
                .Any(w => dbContext.AnalystRecommendations.AsNoTracking()
                    .Any(r => r.TenantId == tenantId && r.RemediationWorkflowId == w.Id)),
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
    }

    private static IQueryable<MyTaskCaseRow> ApplyBucketFilter(
        IQueryable<MyTaskCaseRow> query,
        string bucket,
        IReadOnlySet<RoleName> roles
    ) =>
        bucket switch
        {
            MyTaskBuckets.Recommendation => query.Where(c =>
                c.ActiveWorkflowId == null
                || (c.ActiveWorkflowStage == RemediationWorkflowStage.SecurityAnalysis && !c.HasAnalystRecommendation)),
            MyTaskBuckets.Decision => query.Where(c =>
                c.ActiveWorkflowStage == RemediationWorkflowStage.RemediationDecision
                && c.LatestDecisionId == null),
            MyTaskBuckets.Approval => ApplyApprovalRoleFilter(query.Where(c =>
                c.ActiveWorkflowStage == RemediationWorkflowStage.Approval
                && c.LatestApprovalStatus == DecisionApprovalStatus.PendingApproval), roles),
            _ => query.Where(_ => false),
        };

    private static IQueryable<MyTaskCaseRow> ApplyApprovalRoleFilter(
        IQueryable<MyTaskCaseRow> query,
        IReadOnlySet<RoleName> roles
    )
    {
        if (roles.Contains(RoleName.GlobalAdmin))
        {
            return query;
        }

        var canSecurityApprove = roles.Contains(RoleName.SecurityManager);
        var canTechnicalApprove = roles.Contains(RoleName.TechnicalManager);

        return query.Where(c =>
            (canSecurityApprove
                && (c.LatestOutcome == RemediationOutcome.RiskAcceptance
                    || c.LatestOutcome == RemediationOutcome.AlternateMitigation))
            || (canTechnicalApprove
                && (c.LatestOutcome == RemediationOutcome.ApprovedForPatching
                    || c.LatestOutcome == RemediationOutcome.PatchingDeferred)));
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
