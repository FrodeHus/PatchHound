using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface ICloudApplicationSource : IIngestionSource
{
    Task<IngestionCloudApplicationSnapshot> FetchCloudApplicationsAsync(Guid tenantId, CancellationToken ct);
}
