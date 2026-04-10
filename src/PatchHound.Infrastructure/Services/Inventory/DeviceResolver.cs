using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Inventory;

public class DeviceResolver(PatchHoundDbContext db) : IDeviceResolver
{
    public async Task<Device> ResolveAsync(DeviceObservation observation, CancellationToken ct)
    {
        var existing = await db.Devices
            .IgnoreQueryFilters() // resolver runs under system context during ingestion
            .FirstOrDefaultAsync(d =>
                d.TenantId == observation.TenantId
                && d.SourceSystemId == observation.SourceSystemId
                && d.ExternalId == observation.ExternalId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var device = Device.Create(
            tenantId: observation.TenantId,
            sourceSystemId: observation.SourceSystemId,
            externalId: observation.ExternalId,
            name: observation.Name,
            baselineCriticality: observation.BaselineCriticality);
        db.Devices.Add(device);
        await db.SaveChangesAsync(ct);
        return device;
    }
}
