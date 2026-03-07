using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public interface IAiReportProvider
{
    string ProviderName { get; }
    Task<string> GenerateReportAsync(
        Vulnerability vulnerability,
        IReadOnlyList<Asset> affectedAssets,
        CancellationToken ct
    );
}
