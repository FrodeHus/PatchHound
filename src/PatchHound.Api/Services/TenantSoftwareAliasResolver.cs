using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Services;

public class TenantSoftwareAliasResolver(PatchHoundDbContext dbContext)
{
    public record ResolvedAlias(Guid TenantSoftwareId, Guid SoftwareProductId);

    public async Task<Dictionary<string, ResolvedAlias>> ResolveByExternalIdsAsync(
        Guid tenantId,
        IReadOnlyList<string> externalSoftwareIds,
        CancellationToken ct
    )
    {
        if (externalSoftwareIds.Count == 0)
        {
            return new Dictionary<string, ResolvedAlias>(StringComparer.Ordinal);
        }

        return await dbContext
            .SoftwareTenantRecords.AsNoTracking()
            .Join(
                dbContext.SoftwareProductAliases.AsNoTracking(),
                tenantSoftware => tenantSoftware.SoftwareProductId,
                alias => alias.SoftwareProductId,
                (tenantSoftware, alias) => new
                {
                    tenantSoftware.Id,
                    tenantSoftware.SoftwareProductId,
                    tenantSoftware.TenantId,
                    alias.SourceSystem,
                    alias.ExternalSoftwareId,
                }
            )
            .Where(item =>
                item.TenantId == tenantId
                && item.SourceSystem == SoftwareIdentitySourceSystem.Defender
                && externalSoftwareIds.Contains(item.ExternalSoftwareId)
            )
            .GroupBy(item => item.ExternalSoftwareId)
            .Select(group => new
            {
                ExternalSoftwareId = group.Key,
                TenantSoftwareId = group.Select(item => item.Id).First(),
                SoftwareProductId = group.Select(item => item.SoftwareProductId).First(),
            })
            .ToDictionaryAsync(
                item => item.ExternalSoftwareId,
                item => new ResolvedAlias(item.TenantSoftwareId, item.SoftwareProductId),
                ct
            );
    }
}
