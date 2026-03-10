using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class NormalizedSoftwareProjectionService(
    PatchHoundDbContext dbContext,
    NormalizedSoftwareResolver resolver
)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SyncTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var resolutions = await resolver.SyncTenantAsync(tenantId, ct);
        await RebuildInstallationProjectionAsync(tenantId, resolutions, ct);
        await dbContext.SaveChangesAsync(ct);
        await RebuildVulnerabilityProjectionAsync(tenantId, resolutions, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task RebuildInstallationProjectionAsync(
        Guid tenantId,
        IReadOnlyDictionary<Guid, NormalizedSoftwareResolver.ResolutionResult> resolutions,
        CancellationToken ct
    )
    {
        var tenantSoftwareRows = await UpsertTenantSoftwareAsync(tenantId, resolutions, ct);

        var existingInstallations = await dbContext
            .NormalizedSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId)
            .ToListAsync(ct);
        if (existingInstallations.Count > 0)
        {
            dbContext.NormalizedSoftwareInstallations.RemoveRange(existingInstallations);
        }

        if (resolutions.Count == 0)
        {
            return;
        }

        var relevantSoftwareAssetIds = resolutions.Keys.ToList();

        var currentInstallations = await dbContext
            .DeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && relevantSoftwareAssetIds.Contains(item.SoftwareAssetId)
            )
            .ToListAsync(ct);
        var currentInstallationsByPair = currentInstallations.ToDictionary(
            item => BuildPairKey(item.DeviceAssetId, item.SoftwareAssetId),
            StringComparer.Ordinal
        );

        var latestEpisodes = await dbContext
            .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && relevantSoftwareAssetIds.Contains(item.SoftwareAssetId)
            )
            .GroupBy(item => new { item.DeviceAssetId, item.SoftwareAssetId })
            .Select(group => group
                .OrderByDescending(item => item.EpisodeNumber)
                .First())
            .ToListAsync(ct);

        var rows = latestEpisodes
            .Where(episode => resolutions.ContainsKey(episode.SoftwareAssetId))
            .Select(episode =>
            {
                var key = BuildPairKey(episode.DeviceAssetId, episode.SoftwareAssetId);
                currentInstallationsByPair.TryGetValue(key, out var currentInstallation);
                var resolution = resolutions[episode.SoftwareAssetId];
                var tenantSoftware = tenantSoftwareRows[resolution.NormalizedSoftwareId];

                return NormalizedSoftwareInstallation.Create(
                    tenantId,
                    tenantSoftware.Id,
                    episode.SoftwareAssetId,
                    episode.DeviceAssetId,
                    SoftwareIdentitySourceSystem.Defender,
                    resolution.DetectedVersion,
                    episode.FirstSeenAt,
                    currentInstallation?.LastSeenAt ?? episode.LastSeenAt,
                    currentInstallation is null ? episode.RemovedAt : null,
                    currentInstallation is not null,
                    episode.EpisodeNumber
                );
            })
            .ToList();

        if (rows.Count > 0)
        {
            await dbContext.NormalizedSoftwareInstallations.AddRangeAsync(rows, ct);
        }
    }

    private async Task RebuildVulnerabilityProjectionAsync(
        Guid tenantId,
        IReadOnlyDictionary<Guid, NormalizedSoftwareResolver.ResolutionResult> resolutions,
        CancellationToken ct
    )
    {
        var existingProjections = await dbContext
            .NormalizedSoftwareVulnerabilityProjections.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId)
            .ToListAsync(ct);
        if (existingProjections.Count > 0)
        {
            dbContext.NormalizedSoftwareVulnerabilityProjections.RemoveRange(existingProjections);
        }

        if (resolutions.Count == 0)
        {
            return;
        }

        var matches = await dbContext
            .SoftwareVulnerabilityMatches.IgnoreQueryFilters()
            .Where(match =>
                match.TenantId == tenantId
                && resolutions.Keys.Contains(match.SoftwareAssetId)
            )
            .ToListAsync(ct);

        if (matches.Count == 0)
        {
            return;
        }

        var activeInstallations = await dbContext
            .NormalizedSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.IsActive)
            .ToListAsync(ct);

        var grouped = matches
            .GroupBy(match => new
            {
                TenantSoftwareId = activeInstallations
                    .Where(item => item.SoftwareAssetId == match.SoftwareAssetId)
                    .Select(item => item.TenantSoftwareId)
                    .First(),
                match.VulnerabilityDefinitionId,
            })
            .ToList();

        var projections = new List<NormalizedSoftwareVulnerabilityProjection>(grouped.Count);

        foreach (var group in grouped)
        {
            var orderedMatches = group
                .OrderByDescending(match => GetMethodPriority(match.MatchMethod))
                .ThenByDescending(match => GetConfidencePriority(match.Confidence))
                .ThenByDescending(match => match.LastSeenAt)
                .ToList();

            var relatedSoftwareAssetIds = group
                .Select(item => item.SoftwareAssetId)
                .ToHashSet();
            var relatedInstallations = activeInstallations
                .Where(item =>
                    item.TenantSoftwareId == group.Key.TenantSoftwareId
                    && relatedSoftwareAssetIds.Contains(item.SoftwareAssetId)
                )
                .ToList();

            var allResolved = group.All(item => item.ResolvedAt.HasValue);
            var evidence = group
                .Select(item => new
                {
                    method = item.MatchMethod.ToString(),
                    confidence = item.Confidence.ToString(),
                    evidence = item.Evidence,
                    firstSeenAt = item.FirstSeenAt,
                    lastSeenAt = item.LastSeenAt,
                    resolvedAt = item.ResolvedAt,
                })
                .ToList();

            projections.Add(
                NormalizedSoftwareVulnerabilityProjection.Create(
                    tenantId,
                    group.Key.TenantSoftwareId,
                    group.Key.VulnerabilityDefinitionId,
                    orderedMatches[0].MatchMethod,
                    orderedMatches[0].Confidence,
                    relatedInstallations.Count,
                    relatedInstallations.Select(item => item.DeviceAssetId).Distinct().Count(),
                    relatedInstallations
                        .Select(item => item.DetectedVersion ?? string.Empty)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(version => !string.IsNullOrWhiteSpace(version)),
                    group.Min(item => item.FirstSeenAt),
                    group.Max(item => item.LastSeenAt),
                    allResolved ? group.Max(item => item.ResolvedAt) : null,
                    JsonSerializer.Serialize(evidence, JsonOptions)
                )
            );
        }

        if (projections.Count > 0)
        {
            await dbContext.NormalizedSoftwareVulnerabilityProjections.AddRangeAsync(
                projections,
                ct
            );
        }
    }

    private async Task<Dictionary<Guid, TenantSoftware>> UpsertTenantSoftwareAsync(
        Guid tenantId,
        IReadOnlyDictionary<Guid, NormalizedSoftwareResolver.ResolutionResult> resolutions,
        CancellationToken ct
    )
    {
        var normalizedSoftwareIds = resolutions
            .Values.Select(item => item.NormalizedSoftwareId)
            .Distinct()
            .ToList();

        var existingRows = await dbContext
            .TenantSoftware.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId)
            .ToListAsync(ct);

        var existingByNormalizedId = existingRows.ToDictionary(item => item.NormalizedSoftwareId);
        var rowsByNormalizedId = new Dictionary<Guid, TenantSoftware>();
        var now = DateTimeOffset.UtcNow;

        foreach (var normalizedSoftwareId in normalizedSoftwareIds)
        {
            if (!existingByNormalizedId.TryGetValue(normalizedSoftwareId, out var row))
            {
                row = TenantSoftware.Create(tenantId, normalizedSoftwareId, now, now);
                await dbContext.TenantSoftware.AddAsync(row, ct);
            }
            else
            {
                row.UpdateObservationWindow(row.FirstSeenAt, now);
            }

            rowsByNormalizedId[normalizedSoftwareId] = row;
        }

        var staleRows = existingRows
            .Where(item => !normalizedSoftwareIds.Contains(item.NormalizedSoftwareId))
            .ToList();
        if (staleRows.Count > 0)
        {
            dbContext.TenantSoftware.RemoveRange(staleRows);
        }

        await dbContext.SaveChangesAsync(ct);
        return rowsByNormalizedId;
    }

    private static string BuildPairKey(Guid deviceAssetId, Guid softwareAssetId)
    {
        return $"{deviceAssetId:N}:{softwareAssetId:N}";
    }

    private static int GetMethodPriority(SoftwareVulnerabilityMatchMethod method)
    {
        return method switch
        {
            SoftwareVulnerabilityMatchMethod.DefenderDirect => 200,
            SoftwareVulnerabilityMatchMethod.CpeBinding => 100,
            _ => 0,
        };
    }

    private static int GetConfidencePriority(MatchConfidence confidence)
    {
        return confidence switch
        {
            MatchConfidence.High => 30,
            MatchConfidence.Medium => 20,
            _ => 10,
        };
    }
}
