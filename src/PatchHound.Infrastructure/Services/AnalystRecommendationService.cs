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
    public async Task<Result<AnalystRecommendation>> AddRecommendationForCaseAsync(
        Guid tenantId,
        Guid remediationCaseId,
        RemediationOutcome recommendedOutcome,
        string rationale,
        Guid analystId,
        Guid? vulnerabilityId = null,
        string? priorityOverride = null,
        CancellationToken ct = default
    )
    {
        var activeWorkflow = await remediationWorkflowService.GetOrCreateActiveWorkflowAsync(
            tenantId,
            remediationCaseId,
            ct
        );

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
                remediationCaseId,
                recommendedOutcome,
                rationale,
                analystId,
                vulnerabilityId,
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
                vulnerabilityId,
                priorityOverride
            );
        }

        await remediationWorkflowService.AttachRecommendationAsync(
            tenantId,
            remediationCaseId,
            recommendation,
            ct
        );
        await dbContext.SaveChangesAsync(ct);

        return Result<AnalystRecommendation>.Success(recommendation);
    }
}
