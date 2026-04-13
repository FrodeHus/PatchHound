using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RiskRefreshService(
    PatchHoundDbContext dbContext,
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

    // Phase 1 canonical cleanup (Task 13): Device-keyed entry point for the
    // risk refresh pipeline. Until Phase 5 rewires the vulnerability-episode
    // tables off the Asset navigation, this method delegates to the asset
    // path using the device id as the semantic asset id. This works for test
    // seeds that pair Asset + Device rows with synchronized ids (via the
    // `ForceId` reflection helper) and is a documented no-op for canonical
    // devices with no paired Asset row — the full rewire is Phase 5's scope.
    public async Task RefreshForDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        bool recalculateAssessments,
        CancellationToken ct
    )
    {
        await RefreshForAssetsAsync(tenantId, [deviceId], recalculateAssessments, ct);
    }

    public async Task RefreshForDevicesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> deviceIds,
        bool recalculateAssessments,
        CancellationToken ct
    )
    {
        await RefreshForAssetsAsync(tenantId, deviceIds, recalculateAssessments, ct);
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

        // phase-5: re-introduce per-asset episode risk assessment using DeviceVulnerabilityExposure
        // (VulnerabilityAssessmentService and VulnerabilityEpisodeRiskAssessmentService removed in Phase 2)

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
        // phase-5: re-introduce per-pair episode risk assessment using DeviceVulnerabilityExposure
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
        // phase-5: re-introduce per-vulnerability episode risk assessment using DeviceVulnerabilityExposure
        await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RefreshForTenantAsync(
        Guid tenantId,
        bool recalculateAssessments,
        CancellationToken ct
    )
    {
        // phase-5: re-introduce asset-keyed episode risk assessment using DeviceVulnerabilityExposure
        await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
        await dbContext.SaveChangesAsync(ct);
    }
}
