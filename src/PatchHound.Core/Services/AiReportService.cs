using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;

namespace PatchHound.Core.Services;

public class AiReportService
{
    private readonly IEnumerable<IAiReportProvider> _providers;

    public AiReportService(IEnumerable<IAiReportProvider> providers)
    {
        _providers = providers;
    }

    public async Task<Result<AIReport>> GenerateReportAsync(
        VulnerabilityDefinition vulnerabilityDefinition,
        Guid tenantVulnerabilityId,
        IReadOnlyList<Asset> affectedAssets,
        Guid tenantId,
        Guid userId,
        string providerName,
        CancellationToken ct
    )
    {
        var provider = _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase)
        );

        if (provider is null)
            return Result<AIReport>.Failure($"Unknown AI provider: {providerName}");

        var content = await provider.GenerateReportAsync(
            vulnerabilityDefinition,
            affectedAssets,
            ct
        );
        var report = AIReport.Create(
            tenantVulnerabilityId,
            tenantId,
            content,
            providerName,
            userId
        );

        return Result<AIReport>.Success(report);
    }
}
