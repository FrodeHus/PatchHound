using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Remediation;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Services;

public class RemediationTaskQueryService(
    PatchHoundDbContext dbContext,
    RemediationDecisionService remediationDecisionService
)
{
    private sealed record LinkedTaskRow(
        Guid TaskId,
        Guid SoftwareAssetId,
        Guid TenantSoftwareId,
        string SoftwareName,
        string? SoftwareVendor,
        Guid OwnerTeamId,
        string OwnerTeamName,
        DateTimeOffset DueDate,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string Status,
        Guid DeviceAssetId,
        string DeviceName,
        Criticality DeviceCriticality,
        string? DeviceOwnerName
    );

    private sealed record LinkedTaskDbRow(
        Guid TaskId,
        Guid SoftwareAssetId,
        Guid TenantSoftwareId,
        string SoftwareName,
        string? SoftwareVendor,
        Guid OwnerTeamId,
        string OwnerTeamName,
        DateTimeOffset DueDate,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string Status,
        Guid DeviceAssetId,
        string DeviceName,
        Criticality DeviceCriticality,
        Guid? DeviceOwnerUserId,
        Guid? DeviceOwnerTeamId,
        Guid? DeviceFallbackTeamId
    );

    public async Task<PagedResponse<RemediationTaskListItemDto>> ListOpenTasksAsync(
        Guid tenantId,
        RemediationTaskFilterQuery filter,
        PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var linkedRows = await ApplyFilters(
                BuildLinkedTaskRowsQuery(
                    tenantId,
                    deviceAssetId: filter.DeviceAssetId,
                    tenantSoftwareId: filter.TenantSoftwareId
                ),
                filter
            )
            .ToListAsync(ct);
        var ownerNames = await LoadOwnerNamesAsync(linkedRows, ct);
        var hydratedRows = linkedRows
            .Select(row => new LinkedTaskRow(
                row.TaskId,
                row.SoftwareAssetId,
                row.TenantSoftwareId,
                row.SoftwareName,
                row.SoftwareVendor,
                row.OwnerTeamId,
                row.OwnerTeamName,
                row.DueDate,
                row.CreatedAt,
                row.UpdatedAt,
                row.Status,
                row.DeviceAssetId,
                row.DeviceName,
                row.DeviceCriticality,
                ResolveOwnerName(row, ownerNames)
            ))
            .ToList();
        hydratedRows = ApplyHydratedFilters(hydratedRows, filter);

        var groupedRows = hydratedRows
            .GroupBy(row => new
            {
                row.TaskId,
                row.SoftwareAssetId,
                row.TenantSoftwareId,
                row.SoftwareName,
                row.SoftwareVendor,
                row.OwnerTeamId,
                row.OwnerTeamName,
                row.DueDate,
                row.CreatedAt,
                row.UpdatedAt,
                row.Status,
            })
            .Select(group => new
            {
                group.Key.TaskId,
                group.Key.SoftwareAssetId,
                group.Key.TenantSoftwareId,
                group.Key.SoftwareName,
                group.Key.SoftwareVendor,
                group.Key.OwnerTeamId,
                group.Key.OwnerTeamName,
                group.Key.DueDate,
                group.Key.CreatedAt,
                group.Key.UpdatedAt,
                group.Key.Status,
                AffectedDeviceCount = group.Select(item => item.DeviceAssetId).Distinct().Count(),
                CriticalDeviceCount = group
                    .Where(item => item.DeviceCriticality == Criticality.Critical)
                    .Select(item => item.DeviceAssetId)
                    .Distinct()
                    .Count(),
                HighOrWorseDeviceCount = group
                    .Where(item =>
                        item.DeviceCriticality == Criticality.Critical
                        || item.DeviceCriticality == Criticality.High)
                    .Select(item => item.DeviceAssetId)
                    .Distinct()
                    .Count(),
                HighestDeviceCriticality = group.Max(item => item.DeviceCriticality),
            })
            .OrderBy(item => item.DueDate)
            .ThenByDescending(item => item.CriticalDeviceCount)
            .ThenByDescending(item => item.AffectedDeviceCount)
            .ThenBy(item => item.SoftwareName)
            .ToList();

        var totalCount = groupedRows.Count;

        var pageRows = groupedRows
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToList();

        var pageTaskIds = pageRows.Select(item => item.TaskId).ToList();
        var pageDetails = hydratedRows
            .Where(item => pageTaskIds.Contains(item.TaskId))
            .ToList();

        var pageDetailsByTaskId = pageDetails
            .GroupBy(item => item.TaskId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var items = pageRows
            .Select(item =>
            {
                var detailRows = pageDetailsByTaskId.GetValueOrDefault(item.TaskId) ?? [];
                var deviceNames = detailRows
                    .Select(row => row.DeviceName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();
                var assetOwners = detailRows
                    .Select(row => row.DeviceOwnerName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();

                return new RemediationTaskListItemDto(
                    item.TaskId,
                    item.SoftwareAssetId,
                    item.TenantSoftwareId,
                    item.SoftwareName,
                    item.SoftwareVendor,
                    item.OwnerTeamId,
                    item.OwnerTeamName,
                    item.AffectedDeviceCount,
                    item.CriticalDeviceCount,
                    item.HighOrWorseDeviceCount,
                    item.HighestDeviceCriticality.ToString(),
                    item.DueDate,
                    item.CreatedAt,
                    item.UpdatedAt,
                    item.Status,
                    deviceNames,
                    assetOwners
                );
            })
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
        return await BuildSummaryAsync(BuildLinkedTaskRowsQuery(tenantId, deviceAssetId: assetId), ct);
    }

    public async Task<RemediationTaskSummaryDto> BuildSoftwareSummaryAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        return await BuildSummaryAsync(
            BuildLinkedTaskRowsQuery(tenantId, tenantSoftwareId: tenantSoftwareId),
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
        var activeSoftwareAssetIds = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.TenantSoftwareId == tenantSoftwareId
                && item.IsActive)
            .Select(item => item.SoftwareAssetId)
            .Distinct()
            .ToListAsync(ct);

        var createdCount = 0;

        foreach (var softwareAssetId in activeSoftwareAssetIds)
        {
            var decision = await dbContext.RemediationDecisions
                .Where(item =>
                    item.TenantId == tenantId
                    && item.TenantSoftwareId == tenantSoftwareId
                    && item.Outcome == RemediationOutcome.ApprovedForPatching
                    && item.ApprovalStatus == DecisionApprovalStatus.Approved)
                .OrderByDescending(item => item.DecidedAt)
                .FirstOrDefaultAsync(ct);

            if (decision is null)
            {
                var createdDecision = await remediationDecisionService.CreateDecisionAsync(
                    tenantId,
                    softwareAssetId,
                    RemediationOutcome.ApprovedForPatching,
                    "Created from software remediation workbench.",
                    assignedBy,
                    expiryDate: null,
                    reEvaluationDate: null,
                    ct
                );

                if (createdDecision.IsSuccess)
                {
                    createdCount += await CountOpenTasksForDecisionAsync(createdDecision.Value.Id, ct);
                }

                continue;
            }

            createdCount += await remediationDecisionService.EnsurePatchingTasksAsync(decision.Id, ct);
            await dbContext.SaveChangesAsync(ct);
        }

        return new RemediationTaskCreateResultDto(
            createdCount,
            activeSoftwareAssetIds.Count
        );
    }

    public async Task<List<RemediationTaskTeamStatusDto>> ListTeamStatusesForSoftwareAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        var tasks = await dbContext.PatchingTasks.AsNoTracking()
            .Where(task => task.TenantId == tenantId && task.TenantSoftwareId == tenantSoftwareId)
            .ToListAsync(ct);

        var latestTasksByTeamId = tasks
            .GroupBy(task => task.OwnerTeamId)
            .Select(group => group
                .OrderByDescending(task => task.UpdatedAt)
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
                task.UpdatedAt
            ))
            .OrderBy(item => item.OwnerTeamName)
            .ToList();

        return latestTasks;
    }

    private IQueryable<LinkedTaskDbRow> BuildLinkedTaskRowsQuery(
        Guid tenantId,
        Guid? deviceAssetId = null,
        Guid? tenantSoftwareId = null
    )
    {
        var query =
            from task in dbContext.PatchingTasks.AsNoTracking()
            join team in dbContext.Teams.AsNoTracking() on task.OwnerTeamId equals team.Id
            join installation in dbContext.NormalizedSoftwareInstallations.AsNoTracking()
                on task.TenantSoftwareId equals installation.TenantSoftwareId
            join tenantSoftware in dbContext.TenantSoftware.AsNoTracking()
                on task.TenantSoftwareId equals tenantSoftware.Id
            join normalized in dbContext.NormalizedSoftware.AsNoTracking()
                on tenantSoftware.NormalizedSoftwareId equals normalized.Id
            join device in dbContext.Assets.AsNoTracking()
                on installation.DeviceAssetId equals device.Id
            where task.TenantId == tenantId
                && task.Status != PatchingTaskStatus.Completed
                && installation.TenantId == tenantId
                && installation.IsActive
                && (!tenantSoftwareId.HasValue || installation.TenantSoftwareId == tenantSoftwareId.Value)
                && device.TenantId == tenantId
                && (!deviceAssetId.HasValue || device.Id == deviceAssetId.Value)
                && (
                    device.OwnerTeamId == task.OwnerTeamId
                    || (device.OwnerTeamId == null && device.FallbackTeamId == task.OwnerTeamId)
                )
            select new LinkedTaskDbRow(
                task.Id,
                task.SoftwareAssetId,
                task.TenantSoftwareId,
                normalized.CanonicalName,
                normalized.CanonicalVendor,
                task.OwnerTeamId,
                team.Name,
                task.DueDate,
                task.CreatedAt,
                task.UpdatedAt,
                task.Status.ToString(),
                device.Id,
                device.AssetType == AssetType.Device
                    ? device.DeviceComputerDnsName ?? device.Name
                    : device.Name,
                device.Criticality,
                device.OwnerUserId,
                device.OwnerTeamId,
                device.FallbackTeamId
            );

        return query;
    }

    private static IQueryable<LinkedTaskDbRow> ApplyFilters(
        IQueryable<LinkedTaskDbRow> query,
        RemediationTaskFilterQuery filter
    )
    {
        if (!string.IsNullOrWhiteSpace(filter.Vendor))
        {
            var vendor = filter.Vendor.Trim();
            query = query.Where(item =>
                item.SoftwareVendor != null && item.SoftwareVendor.Contains(vendor));
        }

        if (!string.IsNullOrWhiteSpace(filter.Criticality)
            && Enum.TryParse<Criticality>(filter.Criticality, true, out var criticality))
        {
            query = query.Where(item => item.DeviceCriticality == criticality);
        }

        if (!string.IsNullOrWhiteSpace(filter.AssetOwner))
        {
            // Asset owner names are resolved after the DB fetch because EF cannot translate
            // the fallback user/team display-name logic cleanly for this grouped query.
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(item =>
                item.SoftwareName.Contains(search)
                || (item.SoftwareVendor != null && item.SoftwareVendor.Contains(search))
                || item.OwnerTeamName.Contains(search)
                || item.DeviceName.Contains(search));
        }

        return query;
    }

    private static List<LinkedTaskRow> ApplyHydratedFilters(
        List<LinkedTaskRow> rows,
        RemediationTaskFilterQuery filter
    )
    {
        if (string.IsNullOrWhiteSpace(filter.AssetOwner))
        {
            return rows;
        }

        var assetOwner = filter.AssetOwner.Trim();
        return rows
            .Where(item =>
                item.OwnerTeamName.Contains(assetOwner, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(item.DeviceOwnerName)
                    && item.DeviceOwnerName.Contains(assetOwner, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private async Task<Dictionary<Guid, string>> LoadOwnerNamesAsync(
        IReadOnlyCollection<LinkedTaskDbRow> rows,
        CancellationToken ct
    )
    {
        var userIds = rows
            .Select(row => row.DeviceOwnerUserId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var teamIds = rows
            .SelectMany(row => new[] { row.DeviceOwnerTeamId, row.DeviceFallbackTeamId })
            .OfType<Guid>()
            .Distinct()
            .ToList();

        var userNames = await dbContext.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.DisplayName })
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct);
        var teamNames = await dbContext.Teams.AsNoTracking()
            .Where(team => teamIds.Contains(team.Id))
            .Select(team => new { team.Id, team.Name })
            .ToDictionaryAsync(team => team.Id, team => team.Name, ct);

        return userNames.Concat(teamNames).ToDictionary(item => item.Key, item => item.Value);
    }

    private static string? ResolveOwnerName(
        LinkedTaskDbRow row,
        IReadOnlyDictionary<Guid, string> ownerNames
    )
    {
        if (row.DeviceOwnerUserId is Guid ownerUserId
            && ownerNames.TryGetValue(ownerUserId, out var ownerUserName))
        {
            return ownerUserName;
        }

        if (row.DeviceOwnerTeamId is Guid ownerTeamId
            && ownerNames.TryGetValue(ownerTeamId, out var ownerTeamName))
        {
            return ownerTeamName;
        }

        if (row.DeviceFallbackTeamId is Guid fallbackTeamId
            && ownerNames.TryGetValue(fallbackTeamId, out var fallbackTeamName))
        {
            return fallbackTeamName;
        }

        return null;
    }

    private static async Task<RemediationTaskSummaryDto> BuildSummaryAsync(
        IQueryable<LinkedTaskDbRow> query,
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
