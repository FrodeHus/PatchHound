using PatchHound.Core.Common;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services;

public class TenantAiResearchService : ITenantAiResearchService
{
    public Task<Result<AiWebResearchBundle>> ResearchAsync(
        TenantAiProfileResolved profile,
        AiWebResearchRequest request,
        CancellationToken ct
    )
    {
        return Task.FromResult(
            Result<AiWebResearchBundle>.Failure(
                $"PatchHound-managed web research is not configured for {profile.Profile.ProviderType}."
            )
        );
    }
}
