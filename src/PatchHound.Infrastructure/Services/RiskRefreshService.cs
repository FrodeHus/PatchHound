using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RiskRefreshService(
    PatchHoundDbContext dbContext,
    TenantSnapshotResolver snapshotResolver,
    VulnerabilityAssessmentService assessmentService,
    VulnerabilityEpisodeRiskAssessmentService episodeRiskAssessmentService,
    RiskScoreService riskScoreService
)
{
    public async Task RefreshForAssetAsync(
        Guid tenantId,
        Guid assetId,
        bool recalculateAssessments,
        CancellationToken ct
    )
    {
        await RefreshForAssetsAsync(tenantId, [assetId], recalculateAssessments, ct);
    }

    public async Task RefreshForAssetsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> assetIds,
        bool recalculateAssessments,
        CancellationToken ct
    )
    {
        var distinctAssetIds = assetIds.Distinct().ToList();
        if (distinctAssetIds.Count == 0)
        {
            return;
        }

        if (recalculateAssessments)
        {
            foreach (var assetId in distinctAssetIds)
            {
                await assessmentService.RecalculateForAssetAsync(assetId, ct);
            }
        }

        var activeSnapshotId = await snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(
            tenantId,
            ct
        );

        var assetsById = await dbContext.Assets.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && distinctAssetIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);

        var openEpisodes = await dbContext.VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.Status == VulnerabilityStatus.Open
                && distinctAssetIds.Contains(item.AssetId))
            .Include(item => item.TenantVulnerability)
            .ThenInclude(item => item.VulnerabilityDefinition)
            .ToListAsync(ct);

        foreach (var episode in openEpisodes)
        {
            if (!assetsById.TryGetValue(episode.AssetId, out var asset))
            {
                continue;
            }

            await episodeRiskAssessmentService.UpsertAssessmentAsync(
                tenantId,
                activeSnapshotId,
                episode,
                episode.TenantVulnerability,
                episode.TenantVulnerability.VulnerabilityDefinition,
                asset,
                ct
            );
        }

        await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RefreshForPairAsync(
        Guid tenantId,
        Guid tenantVulnerabilityId,
        Guid assetId,
        bool recalculateAssessments,
        CancellationToken ct
    )
    {
        if (recalculateAssessments)
        {
            await assessmentService.RecalculateAsync(tenantId, tenantVulnerabilityId, assetId, ct);
        }

        var activeSnapshotId = await snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(
            tenantId,
            ct
        );

        var asset = await dbContext.Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == assetId && item.TenantId == tenantId, ct);
        if (asset is not null)
        {
            var openEpisodes = await dbContext.VulnerabilityAssetEpisodes.IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.TenantVulnerabilityId == tenantVulnerabilityId
                    && item.AssetId == assetId
                    && item.Status == VulnerabilityStatus.Open)
                .Include(item => item.TenantVulnerability)
                .ThenInclude(item => item.VulnerabilityDefinition)
                .ToListAsync(ct);

            foreach (var episode in openEpisodes)
            {
                await episodeRiskAssessmentService.UpsertAssessmentAsync(
                    tenantId,
                    activeSnapshotId,
                    episode,
                    episode.TenantVulnerability,
                    episode.TenantVulnerability.VulnerabilityDefinition,
                    asset,
                    ct
                );
            }
        }

        await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RefreshForVulnerabilityAsync(
        Guid tenantId,
        Guid tenantVulnerabilityId,
        bool recalculateAssessments,
        CancellationToken ct
    )
    {
        var openEpisodes = await dbContext.VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.TenantVulnerabilityId == tenantVulnerabilityId
                && item.Status == VulnerabilityStatus.Open)
            .Include(item => item.TenantVulnerability)
            .ThenInclude(item => item.VulnerabilityDefinition)
            .ToListAsync(ct);

        var assetIds = openEpisodes.Select(item => item.AssetId).Distinct().ToList();
        if (assetIds.Count == 0)
        {
            await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
            await dbContext.SaveChangesAsync(ct);
            return;
        }

        if (recalculateAssessments)
        {
            foreach (var assetId in assetIds)
            {
                await assessmentService.RecalculateAsync(tenantId, tenantVulnerabilityId, assetId, ct);
            }
        }

        var activeSnapshotId = await snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(
            tenantId,
            ct
        );
        var assetsById = await dbContext.Assets.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && assetIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);

        foreach (var episode in openEpisodes)
        {
            if (!assetsById.TryGetValue(episode.AssetId, out var asset))
            {
                continue;
            }

            await episodeRiskAssessmentService.UpsertAssessmentAsync(
                tenantId,
                activeSnapshotId,
                episode,
                episode.TenantVulnerability,
                episode.TenantVulnerability.VulnerabilityDefinition,
                asset,
                ct
            );
        }

        await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RefreshForTenantAsync(
        Guid tenantId,
        bool recalculateAssessments,
        CancellationToken ct
    )
    {
        var openAssetIds = await dbContext.VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.Status == VulnerabilityStatus.Open)
            .Select(item => item.AssetId)
            .Distinct()
            .ToListAsync(ct);

        if (openAssetIds.Count == 0)
        {
            await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
            await dbContext.SaveChangesAsync(ct);
            return;
        }

        await RefreshForAssetsAsync(tenantId, openAssetIds, recalculateAssessments, ct);
    }
}
