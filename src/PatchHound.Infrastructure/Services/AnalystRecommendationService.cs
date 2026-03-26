using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class AnalystRecommendationService(
    PatchHoundDbContext dbContext,
    RemediationWorkflowService remediationWorkflowService
)
{
    public async Task<Result<AnalystRecommendation>> AddRecommendationAsync(
        Guid tenantId,
        Guid softwareAssetId,
        RemediationOutcome recommendedOutcome,
        string rationale,
        Guid analystId,
        Guid? tenantVulnerabilityId = null,
        string? priorityOverride = null,
        CancellationToken ct = default
    )
    {
        var recommendation = AnalystRecommendation.Create(
            tenantId,
            softwareAssetId,
            recommendedOutcome,
            rationale,
            analystId,
            tenantVulnerabilityId,
            priorityOverride
        );

        await dbContext.AnalystRecommendations.AddAsync(recommendation, ct);
        await dbContext.SaveChangesAsync(ct);

        return Result<AnalystRecommendation>.Success(recommendation);
    }

    public async Task<Result<AnalystRecommendation>> AddRecommendationForTenantSoftwareAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        RemediationOutcome recommendedOutcome,
        string rationale,
        Guid analystId,
        Guid? tenantVulnerabilityId = null,
        string? priorityOverride = null,
        CancellationToken ct = default
    )
    {
        var activeWorkflow = await remediationWorkflowService.GetOrCreateActiveWorkflowAsync(
            tenantId,
            tenantSoftwareId,
            ct
        );

        var representativeSoftwareAssetId = await dbContext.NormalizedSoftwareInstallations
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.TenantSoftwareId == tenantSoftwareId
                && item.IsActive)
            .Select(item => item.SoftwareAssetId)
            .Distinct()
            .OrderBy(id => id)
            .FirstOrDefaultAsync(ct);

        if (representativeSoftwareAssetId == Guid.Empty)
            return Result<AnalystRecommendation>.Failure("No tenant software scope was found for this software.");

        var recommendation = await dbContext.AnalystRecommendations
            .FirstOrDefaultAsync(item =>
                item.TenantId == tenantId
                && item.RemediationWorkflowId == activeWorkflow.Id,
                ct
            );

        if (recommendation is null)
        {
            recommendation = AnalystRecommendation.Create(
                tenantId,
                representativeSoftwareAssetId,
                recommendedOutcome,
                rationale,
                analystId,
                tenantVulnerabilityId,
                priorityOverride
            );

            await dbContext.AnalystRecommendations.AddAsync(recommendation, ct);
        }
        else
        {
            recommendation.Update(
                recommendedOutcome,
                rationale,
                analystId,
                tenantVulnerabilityId,
                priorityOverride
            );
        }

        await remediationWorkflowService.AttachRecommendationAsync(
            tenantId,
            tenantSoftwareId,
            recommendation,
            ct
        );
        await dbContext.SaveChangesAsync(ct);

        return Result<AnalystRecommendation>.Success(recommendation);
    }
}
