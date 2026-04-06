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
        Guid NormalizedSoftwareId,
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
            .NormalizedSoftwareAliases.IgnoreQueryFilters()
            .ToListAsync(ct);
        var aliasesBySourceKey = aliases.ToDictionary(
            alias => BuildAliasKey(alias.SourceSystem, alias.ExternalSoftwareId),
            StringComparer.Ordinal
        );

        var normalizedSoftware = await dbContext
            .NormalizedSoftware.IgnoreQueryFilters()
            .ToListAsync(ct);
        var normalizedById = normalizedSoftware.ToDictionary(item => item.Id);
        var normalizedByProductKey = normalizedSoftware.ToDictionary(
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

            NormalizedSoftware? normalized = null;
            if (existingAlias is not null)
            {
                normalizedById.TryGetValue(existingAlias.NormalizedSoftwareId, out normalized);
            }

            if (
                normalized is not null
                && !string.Equals(normalized.CanonicalProductKey, identity.CanonicalProductKey, StringComparison.Ordinal)
                && normalizedByProductKey.TryGetValue(identity.CanonicalProductKey, out var matchingNormalized)
                && matchingNormalized.Id != normalized.Id
            )
            {
                normalized = matchingNormalized;
            }

            if (normalized is null)
            {
                normalizedByProductKey.TryGetValue(identity.CanonicalProductKey, out normalized);
            }

            if (normalized is null)
            {
                normalized = NormalizedSoftware.Create(
                    identity.CanonicalName,
                    identity.CanonicalVendor,
                    identity.Category,
                    identity.CanonicalProductKey,
                    identity.PrimaryCpe23Uri,
                    identity.NormalizationMethod,
                    identity.Confidence,
                    now
                );
                await dbContext.NormalizedSoftware.AddAsync(normalized, ct);
                normalizedById[normalized.Id] = normalized;
                normalizedByProductKey[normalized.CanonicalProductKey] = normalized;
            }
            else
            {
                normalized.UpdateIdentity(
                    normalized.PrimaryCpe23Uri is null ? identity.CanonicalName : normalized.CanonicalName,
                    normalized.PrimaryCpe23Uri is null ? identity.CanonicalVendor : normalized.CanonicalVendor,
                    normalized.PrimaryCpe23Uri is null ? identity.Category : normalized.Category,
                    normalized.PrimaryCpe23Uri is null ? identity.CanonicalProductKey : normalized.CanonicalProductKey,
                    normalized.PrimaryCpe23Uri ?? identity.PrimaryCpe23Uri,
                    normalized.PrimaryCpe23Uri is null ? identity.NormalizationMethod : SoftwareNormalizationMethod.ExplicitCpe,
                    normalized.PrimaryCpe23Uri is null ? identity.Confidence : SoftwareNormalizationConfidence.High,
                    now
                );
                normalizedByProductKey[normalized.CanonicalProductKey] = normalized;
            }

            if (existingAlias is null)
            {
                existingAlias = NormalizedSoftwareAlias.Create(
                    normalized.Id,
                    identity.SourceSystem,
                    identity.ExternalSoftwareId,
                    identity.CanonicalName,
                    identity.CanonicalVendor,
                    identity.DetectedVersion,
                    identity.Confidence,
                    identity.MatchReason,
                    now
                );
                await dbContext.NormalizedSoftwareAliases.AddAsync(existingAlias, ct);
                aliasesBySourceKey[aliasKey] = existingAlias;
            }
            else
            {
                existingAlias.UpdateMatch(
                    normalized.Id,
                    identity.CanonicalName,
                    identity.CanonicalVendor,
                    identity.DetectedVersion,
                    identity.Confidence,
                    identity.MatchReason,
                    now
                );
            }

            resolutions[asset.Id] = new ResolutionResult(
                normalized.Id,
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
            dbContext.NormalizedSoftwareAliases.RemoveRange(staleAliases);
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
            return new SoftwareIdentitySnapshot(
                softwareAssetId,
                externalSoftwareId,
                sourceSystem,
                rawName.Trim(),
                string.IsNullOrWhiteSpace(rawVendor) ? null : rawVendor.Trim(),
                string.IsNullOrWhiteSpace(rawCategory) ? null : rawCategory.Trim(),
                BuildCanonicalProductKey(rawVendor, rawName, null),
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

    private static string BuildCanonicalProductKey(
        string? vendor,
        string product,
        string? cpe23Uri
    )
    {
        if (!string.IsNullOrWhiteSpace(cpe23Uri) && TryParseCpe23(cpe23Uri, out var cpe))
        {
            return $"cpe:{NormalizeToken(cpe.Vendor)}:{NormalizeToken(cpe.Product)}";
        }

        return $"{NormalizeToken(vendor)}|{NormalizeToken(product)}";
    }

    private static SoftwareNormalizationConfidence MapConfidence(MatchConfidence confidence)
    {
        return confidence switch
        {
            MatchConfidence.High => SoftwareNormalizationConfidence.High,
            MatchConfidence.Medium => SoftwareNormalizationConfidence.Medium,
            _ => SoftwareNormalizationConfidence.Low,
        };
    }

    private static string BuildAliasKey(
        SoftwareIdentitySourceSystem sourceSystem,
        string externalSoftwareId
    )
    {
        return $"{sourceSystem}:{externalSoftwareId.Trim()}";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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

    private static bool TryParseCpe23(
        string? cpe23Uri,
        out (string Vendor, string Product, string? Version) components
    )
    {
        components = default;
        if (string.IsNullOrWhiteSpace(cpe23Uri))
        {
            return false;
        }

        var segments = cpe23Uri.Split(':', StringSplitOptions.None);
        if (segments.Length < 6 || !string.Equals(segments[0], "cpe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        components = (
            segments[3],
            segments[4],
            string.Equals(segments[5], "*", StringComparison.Ordinal) ? null : segments[5]
        );
        return true;
    }
}
