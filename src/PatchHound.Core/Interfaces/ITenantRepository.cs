using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public interface ITenantRepository : IRepository<Tenant>
{
    Task<bool> AnyExistUnfilteredAsync(CancellationToken ct = default);
}
