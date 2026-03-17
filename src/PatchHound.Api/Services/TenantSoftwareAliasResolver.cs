using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Services;

public class TenantSoftwareAliasResolver(PatchHoundDbContext dbContext)
{
    public record ResolvedAlias(Guid TenantSoftwareId, Guid NormalizedSoftwareId);

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
            .TenantSoftware.AsNoTracking()
            .Join(
                dbContext.NormalizedSoftwareAliases.AsNoTracking(),
                tenantSoftware => tenantSoftware.NormalizedSoftwareId,
                alias => alias.NormalizedSoftwareId,
                (tenantSoftware, alias) => new
                {
                    tenantSoftware.Id,
                    tenantSoftware.NormalizedSoftwareId,
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
                NormalizedSoftwareId = group.Select(item => item.NormalizedSoftwareId).First(),
            })
            .ToDictionaryAsync(
                item => item.ExternalSoftwareId,
                item => new ResolvedAlias(item.TenantSoftwareId, item.NormalizedSoftwareId),
                ct
            );
    }
}
