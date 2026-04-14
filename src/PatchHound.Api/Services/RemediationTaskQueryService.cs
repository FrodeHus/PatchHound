using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Remediation;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Services;

public class RemediationTaskQueryService(
    PatchHoundDbContext dbContext
)
{
    private sealed record LinkedTaskRow(
        Guid TaskId,
        Guid RemediationCaseId,
        string SoftwareName,
        string? SoftwareVendor,
        Guid OwnerTeamId,
        string OwnerTeamName,
        DateTimeOffset DueDate,
        DateTimeOffset? MaintenanceWindowDate,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string Status
    );

    public async Task<PagedResponse<RemediationTaskListItemDto>> ListOpenTasksAsync(
        Guid tenantId,
        RemediationTaskFilterQuery filter,
        PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var rows = await ApplyFilters(
                BuildLinkedTaskRowsQuery(
                    tenantId,
                    taskId: filter.TaskId,
                    remediationCaseId: filter.CaseId
                ),
                filter
            )
            .ToListAsync(ct);

        var groupedRows = rows
            .GroupBy(row => new
            {
                row.TaskId,
                row.RemediationCaseId,
                row.SoftwareName,
                row.SoftwareVendor,
                row.OwnerTeamId,
                row.OwnerTeamName,
                row.DueDate,
                row.MaintenanceWindowDate,
                row.CreatedAt,
                row.UpdatedAt,
                row.Status,
            })
            .Select(group => group.Key)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.SoftwareName)
            .ToList();

        var totalCount = groupedRows.Count;

        var pageRows = groupedRows
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToList();

        var items = pageRows
            .Select(item => new RemediationTaskListItemDto(
                item.TaskId,
                item.RemediationCaseId,
                item.SoftwareName,
                item.SoftwareVendor,
                item.OwnerTeamId,
                item.OwnerTeamName,
                0, // AffectedDeviceCount — Phase 5 debt (#17)
                0, // CriticalDeviceCount — Phase 5 debt (#17)
                0, // HighOrWorseDeviceCount — Phase 5 debt (#17)
                "Unknown", // HighestDeviceCriticality — Phase 5 debt (#17)
                item.DueDate,
                item.MaintenanceWindowDate,
                item.CreatedAt,
                item.UpdatedAt,
                item.Status,
                [], // DeviceNames — Phase 5 debt (#17)
                []  // AssetOwners — Phase 5 debt (#17)
            ))
            .ToList();

        return new PagedResponse<RemediationTaskListItemDto>(
            items,
            totalCount,
            pagination.Page,
            pagination.BoundedPageSize
        );
    }

    public async Task<RemediationTaskSummaryDto> BuildDeviceSummaryAsync(
        Guid tenantId,
        Guid assetId,
        CancellationToken ct
    )
    {
        // Phase 5 debt (#17): device-level summary requires NormalizedSoftwareInstallations join
        return new RemediationTaskSummaryDto(0, 0, null);
    }

    public async Task<RemediationTaskSummaryDto> BuildSoftwareSummaryAsync(
        Guid tenantId,
        Guid remediationCaseId,
        CancellationToken ct
    )
    {
        return await BuildSummaryAsync(
            BuildLinkedTaskRowsQuery(tenantId, remediationCaseId: remediationCaseId),
            ct
        );
    }

    public async Task<RemediationTaskCreateResultDto> CreateMissingTasksForSoftwareAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        Guid assignedBy,
        CancellationToken ct
    )
    {
        // TODO Phase 5 (#17): re-implement device-level task creation after canonical model stabilises
        return new RemediationTaskCreateResultDto(0, 0);
    }

    public async Task<List<RemediationTaskTeamStatusDto>> ListTeamStatusesForSoftwareAsync(
        Guid tenantId,
        Guid remediationCaseId,
        CancellationToken ct
    )
    {
        var tasks = await dbContext.PatchingTasks.AsNoTracking()
            .Include(task => task.RemediationDecision)
            .Where(task => task.TenantId == tenantId && task.RemediationCaseId == remediationCaseId)
            .ToListAsync(ct);

        var latestTasksByTeamId = tasks
            .GroupBy(task => task.OwnerTeamId)
            .Select(group => group
                .OrderByDescending(task => task.RemediationDecision.MaintenanceWindowDate)
                .ThenByDescending(task => task.UpdatedAt)
                .ThenByDescending(task => task.CreatedAt)
                .First())
            .ToList();

        var ownerTeamIds = latestTasksByTeamId
            .Select(task => task.OwnerTeamId)
            .Distinct()
            .ToList();

        var teamNamesById = await dbContext.Teams.AsNoTracking()
            .Where(team => ownerTeamIds.Contains(team.Id))
            .Select(team => new { team.Id, team.Name })
            .ToDictionaryAsync(team => team.Id, team => team.Name, ct);

        var latestTasks = latestTasksByTeamId
            .Select(task => new RemediationTaskTeamStatusDto(
                task.OwnerTeamId,
                teamNamesById.GetValueOrDefault(task.OwnerTeamId) ?? "Unknown team",
                task.Status.ToString(),
                task.DueDate,
                task.RemediationDecision.MaintenanceWindowDate,
                task.UpdatedAt
            ))
            .OrderBy(item => item.OwnerTeamName)
            .ToList();

        return latestTasks;
    }

    private IQueryable<LinkedTaskRow> BuildLinkedTaskRowsQuery(
        Guid tenantId,
        Guid? taskId = null,
        Guid? deviceAssetId = null,
        Guid? remediationCaseId = null
    )
    {
        // Phase 5 debt (#17): deviceAssetId filter not applied — device-level joins removed in Phase 4.
        var query =
            from task in dbContext.PatchingTasks.AsNoTracking()
            join team in dbContext.Teams.AsNoTracking() on task.OwnerTeamId equals team.Id
            join decision in dbContext.RemediationDecisions.AsNoTracking()
                on task.RemediationDecisionId equals decision.Id
            join rc in dbContext.RemediationCases.AsNoTracking()
                on task.RemediationCaseId equals rc.Id
            join sp in dbContext.SoftwareProducts.AsNoTracking()
                on rc.SoftwareProductId equals sp.Id
            where task.TenantId == tenantId
                && task.Status != PatchingTaskStatus.Completed
                && (!taskId.HasValue || task.Id == taskId.Value)
                && (!remediationCaseId.HasValue || task.RemediationCaseId == remediationCaseId.Value)
            select new LinkedTaskRow(
                task.Id,
                task.RemediationCaseId,
                sp.Name,
                sp.Vendor,
                task.OwnerTeamId,
                team.Name,
                task.DueDate,
                decision.MaintenanceWindowDate,
                task.CreatedAt,
                task.UpdatedAt,
                task.Status.ToString()
            );

        return query;
    }

    private static IQueryable<LinkedTaskRow> ApplyFilters(
        IQueryable<LinkedTaskRow> query,
        RemediationTaskFilterQuery filter
    )
    {
        if (!string.IsNullOrWhiteSpace(filter.Vendor))
        {
            var vendor = filter.Vendor.Trim();
            query = query.Where(item =>
                item.SoftwareVendor != null && item.SoftwareVendor.Contains(vendor));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(item =>
                item.SoftwareName.Contains(search)
                || (item.SoftwareVendor != null && item.SoftwareVendor.Contains(search))
                || item.OwnerTeamName.Contains(search));
        }

        return query;
    }

    private static async Task<RemediationTaskSummaryDto> BuildSummaryAsync(
        IQueryable<LinkedTaskRow> query,
        CancellationToken ct
    )
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await query
            .Select(item => new { item.TaskId, item.DueDate })
            .Distinct()
            .ToListAsync(ct);

        return new RemediationTaskSummaryDto(
            rows.Count,
            rows.Count(item => item.DueDate < now),
            rows.OrderBy(item => item.DueDate).Select(item => (DateTimeOffset?)item.DueDate).FirstOrDefault()
        );
    }

    private async Task<int> CountOpenTasksForDecisionAsync(Guid decisionId, CancellationToken ct)
    {
        return await dbContext.PatchingTasks.AsNoTracking()
            .CountAsync(task => task.RemediationDecisionId == decisionId && task.Status != PatchingTaskStatus.Completed, ct);
    }
}
