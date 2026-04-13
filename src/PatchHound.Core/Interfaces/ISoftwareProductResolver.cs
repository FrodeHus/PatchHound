using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public record SoftwareObservation(Guid SourceSystemId, string ExternalId, string Vendor, string Name);

public interface ISoftwareProductResolver
{
    Task<SoftwareProduct> ResolveAsync(SoftwareObservation observation, CancellationToken ct);
}
