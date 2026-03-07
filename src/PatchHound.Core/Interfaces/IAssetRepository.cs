using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Core.Interfaces;

public interface IAssetRepository : IRepository<Asset>
{
    Task<Asset?> GetByExternalIdAsync(
        string externalId,
        Guid tenantId,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<Asset>> GetByOwnerAsync(
        Guid ownerId,
        OwnerType ownerType,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<Asset>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
