using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Core.Entities;
using PatchHound.Core.Common;

namespace PatchHound.Core.Interfaces;

public interface IAiReportProvider
{
    TenantAiProviderType ProviderType { get; }
    Task<string> GenerateReportAsync(
        AiReportGenerationRequest request,
        TenantAiProfileResolved profile,
        CancellationToken ct
    );

    Task<AiProviderValidationResult> ValidateAsync(
        TenantAiProfileResolved profile,
        CancellationToken ct
    );

    Task<Result<IReadOnlyList<string>>> ListAvailableModelsAsync(
        TenantAiProfileResolved profile,
        CancellationToken ct
    );
}
