using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RemediationTaskProjectionService(PatchHoundDbContext dbContext, SlaService slaService)
{
    private static readonly Guid SystemUserId = Guid.Empty;

    public async Task EnsureOpenTaskAsync(
        Guid tenantId,
        TenantVulnerability tenantVulnerability,
        VulnerabilityDefinition definition,
        Asset asset,
        CancellationToken ct
    )
    {
        var existingTask = await dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                task =>
                    task.TenantId == tenantId
                    && task.TenantVulnerabilityId == tenantVulnerability.Id
                    && task.AssetId == asset.Id
                    && task.Status != RemediationTaskStatus.Completed,
                ct
            );

        if (existingTask is not null)
        {
            return;
        }

        var assigneeId = ResolveAssignee(asset);
        if (!assigneeId.HasValue)
        {
            return;
        }

        var tenantSla = await dbContext
            .TenantSlaConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(config => config.TenantId == tenantId, ct);

        var task = RemediationTask.Create(
            tenantVulnerability.Id,
            asset.Id,
            tenantId,
            assigneeId.Value,
            SystemUserId,
            slaService.CalculateDueDate(
                definition.VendorSeverity,
                DateTimeOffset.UtcNow,
                tenantSla
            )
        );

        await dbContext.RemediationTasks.AddAsync(task, ct);
    }

    public async Task EnsureOpenTasksAsync(
        Guid tenantId,
        IReadOnlyList<(
            TenantVulnerability TenantVulnerability,
            VulnerabilityDefinition Definition,
            Asset Asset
        )> openedProjectionPairs,
        HashSet<string> openTaskPairKeys,
        CancellationToken ct
    )
    {
        if (openedProjectionPairs.Count == 0)
        {
            return;
        }

        var tenantSla = await dbContext
            .TenantSlaConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(config => config.TenantId == tenantId, ct);
        var tasksToCreate = new List<RemediationTask>();

        foreach (var (tenantVulnerability, definition, asset) in openedProjectionPairs)
        {
            var pairKey = BuildPairKey(tenantVulnerability.Id, asset.Id);
            if (openTaskPairKeys.Contains(pairKey))
            {
                continue;
            }

            var assigneeId = ResolveAssignee(asset);
            if (!assigneeId.HasValue)
            {
                continue;
            }

            tasksToCreate.Add(
                RemediationTask.Create(
                    tenantVulnerability.Id,
                    asset.Id,
                    tenantId,
                    assigneeId.Value,
                    SystemUserId,
                    slaService.CalculateDueDate(
                        definition.VendorSeverity,
                        DateTimeOffset.UtcNow,
                        tenantSla
                    )
                )
            );
            openTaskPairKeys.Add(pairKey);
        }

        if (tasksToCreate.Count > 0)
        {
            await dbContext.RemediationTasks.AddRangeAsync(tasksToCreate, ct);
        }
    }

    public async Task CloseOpenTasksAsync(
        IReadOnlyList<(Guid TenantVulnerabilityId, Guid AssetId)> pairs,
        CancellationToken ct
    )
    {
        if (pairs.Count == 0)
        {
            return;
        }

        var pairKeys = pairs
            .Select(pair => BuildPairKey(pair.TenantVulnerabilityId, pair.AssetId))
            .ToHashSet(StringComparer.Ordinal);
        var tenantVulnerabilityIds = pairs
            .Select(pair => pair.TenantVulnerabilityId)
            .Distinct()
            .ToList();
        var assetIds = pairs.Select(pair => pair.AssetId).Distinct().ToList();

        var tasks = await dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .Where(task =>
                tenantVulnerabilityIds.Contains(task.TenantVulnerabilityId)
                && assetIds.Contains(task.AssetId)
                && task.Status != RemediationTaskStatus.Completed
            )
            .ToListAsync(ct);

        foreach (var task in tasks)
        {
            if (!pairKeys.Contains(BuildPairKey(task.TenantVulnerabilityId, task.AssetId)))
            {
                continue;
            }

            task.UpdateStatus(
                RemediationTaskStatus.Completed,
                "Auto-closed: vulnerability resolved in source"
            );
        }
    }

    private static Guid? ResolveAssignee(Asset asset)
    {
        if (asset.OwnerUserId.HasValue)
        {
            return asset.OwnerUserId.Value;
        }

        if (asset.FallbackTeamId.HasValue)
        {
            return asset.FallbackTeamId.Value;
        }

        return null;
    }

    private static string BuildPairKey(Guid tenantVulnerabilityId, Guid assetId)
    {
        return $"{tenantVulnerabilityId:N}:{assetId:N}";
    }
}
