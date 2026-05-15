using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RiskRefreshService(
    PatchHoundDbContext dbContext,
    ExposureAssessmentService exposureAssessmentService,
    RiskScoreService riskScoreService,
    MaterializedViewRefreshService materializedViewRefreshService
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

        if (recalculateAssessments)
        {
            await exposureAssessmentService.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, ct);
        }

        await materializedViewRefreshService.RefreshExposureLatestAssessmentAsync(ct);
        await materializedViewRefreshService.RefreshAlternateMitigationVulnIdsAsync(ct);
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
            await exposureAssessmentService.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, ct);
        }

        await materializedViewRefreshService.RefreshExposureLatestAssessmentAsync(ct);
        await materializedViewRefreshService.RefreshAlternateMitigationVulnIdsAsync(ct);
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
        if (recalculateAssessments)
        {
            await exposureAssessmentService.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, ct);
        }

        await materializedViewRefreshService.RefreshExposureLatestAssessmentAsync(ct);
        await materializedViewRefreshService.RefreshAlternateMitigationVulnIdsAsync(ct);
        await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RefreshForTenantAsync(
        Guid tenantId,
        bool recalculateAssessments,
        CancellationToken ct
    )
    {
        if (recalculateAssessments)
        {
            await exposureAssessmentService.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, ct);
        }

        await materializedViewRefreshService.RefreshExposureLatestAssessmentAsync(ct);
        await materializedViewRefreshService.RefreshAlternateMitigationVulnIdsAsync(ct);
        await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
        await dbContext.SaveChangesAsync(ct);
    }
}
