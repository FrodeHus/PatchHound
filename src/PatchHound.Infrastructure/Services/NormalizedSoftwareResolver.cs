using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class NormalizedSoftwareResolver(PatchHoundDbContext dbContext)
{
    internal sealed record SoftwareIdentitySnapshot(
        Guid SoftwareAssetId,
        string ExternalSoftwareId,
        SoftwareIdentitySourceSystem SourceSystem,
        string CanonicalName,
        string? CanonicalVendor,
        string? Category,
        string CanonicalProductKey,
        string? PrimaryCpe23Uri,
        string? DetectedVersion,
        SoftwareNormalizationMethod NormalizationMethod,
        SoftwareNormalizationConfidence Confidence,
        string MatchReason
    );

    public sealed record ResolutionResult(
        Guid SoftwareProductId,
        Guid SoftwareAssetId,
        string? DetectedVersion,
        SoftwareIdentitySourceSystem SourceSystem
    );

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<Guid, ResolutionResult>> SyncTenantAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        var now = DateTimeOffset.UtcNow;
        var softwareAssets = await dbContext
            .Assets.IgnoreQueryFilters()
            .Where(asset => asset.TenantId == tenantId && asset.AssetType == AssetType.Software)
            .Select(asset => new
            {
                asset.Id,
                asset.ExternalId,
                asset.Name,
                asset.Metadata,
                asset.SourceKey,
            })
            .ToListAsync(ct);

        var aliases = await dbContext
            .SoftwareProductAliases.IgnoreQueryFilters()
            .ToListAsync(ct);
        var aliasesBySourceKey = aliases.ToDictionary(
            alias => BuildAliasKey(alias.SourceSystem, alias.ExternalSoftwareId),
            StringComparer.Ordinal
        );

        var softwareProducts = await dbContext
            .SoftwareProducts.IgnoreQueryFilters()
            .ToListAsync(ct);
        var productsById = softwareProducts.ToDictionary(item => item.Id);
        var productsByKey = softwareProducts.ToDictionary(
            item => item.CanonicalProductKey,
            StringComparer.Ordinal
        );

        var resolutions = new Dictionary<Guid, ResolutionResult>();
        var activeAliasKeys = new HashSet<string>(StringComparer.Ordinal);
        var activeSourceSystems = new HashSet<SoftwareIdentitySourceSystem>();

        foreach (var asset in softwareAssets)
        {
            var sourceSystem = MapSourceSystem(asset.SourceKey);
            activeSourceSystems.Add(sourceSystem);
            var identity = BuildIdentity(asset.Id, asset.ExternalId, asset.Name, asset.Metadata, sourceSystem);
            if (identity is null)
            {
                continue;
            }

            var aliasKey = BuildAliasKey(identity.SourceSystem, identity.ExternalSoftwareId);
            activeAliasKeys.Add(aliasKey);
            aliasesBySourceKey.TryGetValue(aliasKey, out var existingAlias);

            SoftwareProduct? product = null;
            if (existingAlias is not null)
            {
                productsById.TryGetValue(existingAlias.SoftwareProductId, out product);
            }

            if (
                product is not null
                && !string.Equals(product.CanonicalProductKey, identity.CanonicalProductKey, StringComparison.Ordinal)
                && productsByKey.TryGetValue(identity.CanonicalProductKey, out var matchingProduct)
                && matchingProduct.Id != product.Id
            )
            {
                product = matchingProduct;
            }

            if (product is null)
            {
                productsByKey.TryGetValue(identity.CanonicalProductKey, out product);
            }

            if (product is null)
            {
                var vendor = identity.CanonicalVendor ?? string.Empty;
                product = SoftwareProduct.Create(
                    string.IsNullOrWhiteSpace(vendor) ? identity.CanonicalName : vendor,
                    identity.CanonicalName,
                    identity.PrimaryCpe23Uri
                );
                product.UpdateIdentity(
                    identity.Category,
                    identity.PrimaryCpe23Uri,
                    identity.NormalizationMethod,
                    identity.Confidence,
                    now
                );
                await dbContext.SoftwareProducts.AddAsync(product, ct);
                productsById[product.Id] = product;
                productsByKey[product.CanonicalProductKey] = product;
            }
            else
            {
                product.UpdateIdentity(
                    product.PrimaryCpe23Uri is null ? identity.Category : product.Category,
                    product.PrimaryCpe23Uri ?? identity.PrimaryCpe23Uri,
                    product.PrimaryCpe23Uri is null ? identity.NormalizationMethod : SoftwareNormalizationMethod.ExplicitCpe,
                    product.PrimaryCpe23Uri is null ? identity.Confidence : SoftwareNormalizationConfidence.High,
                    now
                );
                productsByKey[product.CanonicalProductKey] = product;
            }

            if (existingAlias is null)
            {
                existingAlias = SoftwareProductAlias.Create(
                    product.Id,
                    identity.SourceSystem,
                    identity.ExternalSoftwareId,
                    identity.CanonicalName,
                    identity.CanonicalVendor,
                    identity.DetectedVersion,
                    identity.Confidence,
                    identity.MatchReason,
                    now
                );
                await dbContext.SoftwareProductAliases.AddAsync(existingAlias, ct);
                aliasesBySourceKey[aliasKey] = existingAlias;
            }
            else
            {
                existingAlias.UpdateMatch(
                    product.Id,
                    identity.CanonicalName,
                    identity.CanonicalVendor,
                    identity.DetectedVersion,
                    identity.Confidence,
                    identity.MatchReason,
                    now
                );
            }

            resolutions[asset.Id] = new ResolutionResult(
                product.Id,
                asset.Id,
                identity.DetectedVersion,
                identity.SourceSystem
            );
        }

        var staleAliases = aliases
            .Where(alias =>
                activeSourceSystems.Contains(alias.SourceSystem)
                && !activeAliasKeys.Contains(BuildAliasKey(alias.SourceSystem, alias.ExternalSoftwareId))
            )
            .ToList();
        if (staleAliases.Count > 0)
        {
            dbContext.SoftwareProductAliases.RemoveRange(staleAliases);
        }

        await dbContext.SaveChangesAsync(ct);
        return resolutions;
    }

    private static SoftwareIdentitySourceSystem MapSourceSystem(string? sourceKey)
    {
        var normalized = sourceKey?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "authenticated-scan" => SoftwareIdentitySourceSystem.AuthenticatedScan,
            _ => SoftwareIdentitySourceSystem.Defender,
        };
    }

    private static SoftwareIdentitySnapshot? BuildIdentity(
        Guid softwareAssetId,
        string externalSoftwareId,
        string assetName,
        string metadataJson,
        SoftwareIdentitySourceSystem sourceSystem
    )
    {
        var metadata = ParseMetadata(metadataJson);

        var rawName = ReadMetadataValue(metadata, "name") ?? assetName;
        var rawVendor = ReadMetadataValue(metadata, "vendor");
        var rawVersion = ReadMetadataValue(metadata, "version");
        var rawCategory = ReadMetadataValue(metadata, "category");

        if (!string.IsNullOrWhiteSpace(rawName))
        {
            var vendor = string.IsNullOrWhiteSpace(rawVendor) ? null : rawVendor.Trim();
            return new SoftwareIdentitySnapshot(
                softwareAssetId,
                externalSoftwareId,
                sourceSystem,
                rawName.Trim(),
                vendor,
                string.IsNullOrWhiteSpace(rawCategory) ? null : rawCategory.Trim(),
                BuildCanonicalProductKey(vendor, rawName),
                null,
                string.IsNullOrWhiteSpace(rawVersion) ? null : rawVersion.Trim(),
                SoftwareNormalizationMethod.Heuristic,
                SoftwareNormalizationConfidence.Medium,
                "Resolved from software asset metadata."
            );
        }

        return null;
    }

    private static Dictionary<string, string?> ParseMetadata(string metadataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(metadataJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
            });
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.EnumerateObject().ToDictionary(
                    item => item.Name,
                    item => item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : item.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase
                )
                : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ReadMetadataValue(
        IReadOnlyDictionary<string, string?> metadata,
        string key
    )
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    /// <summary>
    /// Builds the canonical product key in the <c>vendor::name</c> format used by
    /// <see cref="SoftwareProduct"/>. Both tokens are lowercased and stripped of
    /// non-alphanumeric characters to match the key stored in the database.
    /// </summary>
    private static string BuildCanonicalProductKey(string? vendor, string product)
    {
        var vendorToken = NormalizeToken(vendor);
        var productToken = NormalizeToken(product);
        // Use the vendor token as vendor when present; fall back to the product token
        // so that SoftwareProduct.Create can always receive a non-empty vendor.
        return $"{(string.IsNullOrEmpty(vendorToken) ? productToken : vendorToken)}::{productToken}";
    }

    private static string BuildAliasKey(
        SoftwareIdentitySourceSystem sourceSystem,
        string externalSoftwareId
    )
    {
        return $"{sourceSystem}:{externalSoftwareId.Trim()}";
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }
}
