using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Core.Interfaces;

public record DeviceObservation(
    Guid TenantId,
    Guid SourceSystemId,
    string ExternalId,
    string Name,
    Criticality BaselineCriticality);

public interface IDeviceResolver
{
    Task<Device> ResolveAsync(DeviceObservation observation, CancellationToken ct);
}
