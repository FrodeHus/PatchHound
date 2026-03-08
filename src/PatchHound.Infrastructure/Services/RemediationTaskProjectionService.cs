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
        Vulnerability vulnerability,
        Asset asset,
        CancellationToken ct
    )
    {
        var existingTask = await dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                task =>
                    task.TenantId == tenantId
                    && task.VulnerabilityId == vulnerability.Id
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
            vulnerability.Id,
            asset.Id,
            tenantId,
            assigneeId.Value,
            SystemUserId,
            slaService.CalculateDueDate(
                vulnerability.VendorSeverity,
                DateTimeOffset.UtcNow,
                tenantSla
            )
        );

        await dbContext.RemediationTasks.AddAsync(task, ct);
    }

    public async Task EnsureOpenTasksAsync(
        Guid tenantId,
        IReadOnlyList<(Vulnerability Vulnerability, Asset Asset)> openedProjectionPairs,
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

        foreach (var (vulnerability, asset) in openedProjectionPairs)
        {
            var pairKey = BuildPairKey(vulnerability.Id, asset.Id);
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
                    vulnerability.Id,
                    asset.Id,
                    tenantId,
                    assigneeId.Value,
                    SystemUserId,
                    slaService.CalculateDueDate(
                        vulnerability.VendorSeverity,
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
        IReadOnlyList<(Guid VulnerabilityId, Guid AssetId)> pairs,
        CancellationToken ct
    )
    {
        if (pairs.Count == 0)
        {
            return;
        }

        var pairKeys = pairs
            .Select(pair => BuildPairKey(pair.VulnerabilityId, pair.AssetId))
            .ToHashSet(StringComparer.Ordinal);
        var vulnerabilityIds = pairs.Select(pair => pair.VulnerabilityId).Distinct().ToList();
        var assetIds = pairs.Select(pair => pair.AssetId).Distinct().ToList();

        var tasks = await dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .Where(task =>
                vulnerabilityIds.Contains(task.VulnerabilityId)
                && assetIds.Contains(task.AssetId)
                && task.Status != RemediationTaskStatus.Completed
            )
            .ToListAsync(ct);

        foreach (var task in tasks)
        {
            if (!pairKeys.Contains(BuildPairKey(task.VulnerabilityId, task.AssetId)))
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

    private static string BuildPairKey(Guid vulnerabilityId, Guid assetId)
    {
        return $"{vulnerabilityId:N}:{assetId:N}";
    }
}
