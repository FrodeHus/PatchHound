using Vigil.Core.Entities;

namespace Vigil.Core.Interfaces;

public interface IAiReportProvider
{
    string ProviderName { get; }
    Task<string> GenerateReportAsync(Vulnerability vulnerability, IReadOnlyList<Asset> affectedAssets, CancellationToken ct);
}
