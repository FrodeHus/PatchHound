using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IRiskChangeBriefAiSummaryService
{
    Task<string?> GenerateAsync(
        Guid tenantId,
        RiskChangeBriefSummaryInput brief,
        CancellationToken ct
    );
}
