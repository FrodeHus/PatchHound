using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Core.Entities;

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
}
