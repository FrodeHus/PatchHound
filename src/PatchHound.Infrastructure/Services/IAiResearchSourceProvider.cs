using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services;

public interface IAiResearchSourceProvider
{
    string SourceKey { get; }

    Task<Result<AiWebResearchBundle>> ResearchAsync(
        EnrichmentSourceConfiguration source,
        AiWebResearchRequest request,
        CancellationToken ct
    );
}
