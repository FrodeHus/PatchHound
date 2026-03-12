using PatchHound.Core.Common;
using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface ITenantAiResearchService
{
    Task<Result<AiWebResearchBundle>> ResearchAsync(
        TenantAiProfileResolved profile,
        AiWebResearchRequest request,
        CancellationToken ct
    );
}
