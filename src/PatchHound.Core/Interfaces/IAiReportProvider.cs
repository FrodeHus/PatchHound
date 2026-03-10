using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public interface IAiReportProvider
{
    string ProviderName { get; }
    Task<string> GenerateReportAsync(
        VulnerabilityDefinition vulnerabilityDefinition,
        IReadOnlyList<Asset> affectedAssets,
        CancellationToken ct
    );
}
