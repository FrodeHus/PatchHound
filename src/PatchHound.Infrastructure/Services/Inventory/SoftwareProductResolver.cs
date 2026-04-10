using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Inventory;

public class SoftwareProductResolver(PatchHoundDbContext db) : ISoftwareProductResolver
{
    public async Task<SoftwareProduct> ResolveAsync(SoftwareObservation observation, CancellationToken ct)
    {
        var alias = await db.SoftwareAliases
            .FirstOrDefaultAsync(a =>
                a.SourceSystemId == observation.SourceSystemId
                && a.ExternalId == observation.ExternalId, ct);

        if (alias is not null)
        {
            var existing = await db.SoftwareProducts.FirstAsync(p => p.Id == alias.SoftwareProductId, ct);
            return existing;
        }

        var canonicalKey = $"{observation.Vendor.Trim().ToLowerInvariant()}::{observation.Name.Trim().ToLowerInvariant()}";
        var product = await db.SoftwareProducts.FirstOrDefaultAsync(p => p.CanonicalProductKey == canonicalKey, ct);
        if (product is null)
        {
            product = SoftwareProduct.Create(observation.Vendor, observation.Name, primaryCpe23Uri: null);
            db.SoftwareProducts.Add(product);
        }

        var newAlias = SoftwareAlias.Create(
            softwareProductId: product.Id,
            sourceSystemId: observation.SourceSystemId,
            externalId: observation.ExternalId,
            observedVendor: observation.Vendor,
            observedName: observation.Name);
        db.SoftwareAliases.Add(newAlias);
        await db.SaveChangesAsync(ct);
        return product;
    }
}
