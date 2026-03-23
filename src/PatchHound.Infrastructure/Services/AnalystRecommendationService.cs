using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class AnalystRecommendationService(PatchHoundDbContext dbContext)
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
}
