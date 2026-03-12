using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class StagedAssetMergeService(PatchHoundDbContext dbContext)
{
    private const int AssetChunkSize = 500;
    private static readonly JsonSerializerOptions StagingJsonOptions = new(
        JsonSerializerDefaults.Web
    );

    public async Task<StagedAssetMergeSummary> ProcessAsync(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var stagedAssets = await dbContext
            .StagedAssets.IgnoreQueryFilters()
            .Where(item =>
                item.IngestionRunId == ingestionRunId
                && item.TenantId == tenantId
                && item.SourceKey == normalizedSourceKey
            )
            .OrderBy(item => item.ExternalId)
            .ToListAsync(ct);
        var stagedLinks = await dbContext
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item =>
                item.IngestionRunId == ingestionRunId
                && item.TenantId == tenantId
                && item.SourceKey == normalizedSourceKey
            )
            .OrderBy(item => item.DeviceExternalId)
            .ThenBy(item => item.SoftwareExternalId)
            .ToListAsync(ct);

        var mergedAssetCount = 0;

        foreach (var chunk in Chunk(stagedAssets, AssetChunkSize))
        {
            var chunkExternalIds = chunk.Select(item => item.ExternalId).Distinct().ToList();
            var existingAssetsByExternalId = await dbContext
                .Assets.IgnoreQueryFilters()
                .Where(current =>
                    current.TenantId == tenantId && chunkExternalIds.Contains(current.ExternalId)
                )
                .ToDictionaryAsync(current => current.ExternalId, StringComparer.Ordinal, ct);

            foreach (var staged in chunk)
            {
                var asset = JsonSerializer.Deserialize<IngestionAsset>(
                    staged.PayloadJson,
                    StagingJsonOptions
                );
                if (asset is null)
                {
                    continue;
                }

                mergedAssetCount++;

                existingAssetsByExternalId.TryGetValue(asset.ExternalId, out var existing);

                if (existing is null)
                {
                    existing = Asset.Create(
                        tenantId,
                        asset.ExternalId,
                        asset.AssetType,
                        asset.Name,
                        Criticality.Medium,
                        asset.Description
                    );
                    if (asset.AssetType == AssetType.Device)
                    {
                        existing.UpdateDeviceDetails(
                            asset.DeviceComputerDnsName,
                            asset.DeviceHealthStatus,
                            asset.DeviceOsPlatform,
                            asset.DeviceOsVersion,
                            asset.DeviceRiskScore,
                            asset.DeviceLastSeenAt,
                            asset.DeviceLastIpAddress,
                            asset.DeviceAadDeviceId
                        );
                    }

                    existing.UpdateMetadata(asset.Metadata);
                    await dbContext.Assets.AddAsync(existing, ct);
                    existingAssetsByExternalId[asset.ExternalId] = existing;
                    continue;
                }

                existing.UpdateDeviceDetails(
                    asset.AssetType == AssetType.Device
                        ? asset.DeviceComputerDnsName
                        : existing.DeviceComputerDnsName,
                    asset.AssetType == AssetType.Device
                        ? asset.DeviceHealthStatus
                        : existing.DeviceHealthStatus,
                    asset.AssetType == AssetType.Device
                        ? asset.DeviceOsPlatform
                        : existing.DeviceOsPlatform,
                    asset.AssetType == AssetType.Device
                        ? asset.DeviceOsVersion
                        : existing.DeviceOsVersion,
                    asset.AssetType == AssetType.Device
                        ? asset.DeviceRiskScore
                        : existing.DeviceRiskScore,
                    asset.AssetType == AssetType.Device
                        ? asset.DeviceLastSeenAt
                        : existing.DeviceLastSeenAt,
                    asset.AssetType == AssetType.Device
                        ? asset.DeviceLastIpAddress
                        : existing.DeviceLastIpAddress,
                    asset.AssetType == AssetType.Device
                        ? asset.DeviceAadDeviceId
                        : existing.DeviceAadDeviceId
                );
                existing.UpdateDetails(asset.Name, asset.Description);
                existing.UpdateMetadata(asset.Metadata);
            }

            await dbContext.SaveChangesAsync(ct);
            dbContext.ChangeTracker.Clear();
        }

        var links = stagedLinks
            .Select(item =>
                JsonSerializer.Deserialize<IngestionDeviceSoftwareLink>(
                    item.PayloadJson,
                    StagingJsonOptions
                )
            )
            .Where(item => item is not null)
            .Cast<IngestionDeviceSoftwareLink>()
            .ToList();

        var softwareLinkSummary = await ProcessDeviceSoftwareLinksAsync(
            tenantId,
            links,
            new Dictionary<string, Guid>(StringComparer.Ordinal),
            ct
        );

        await dbContext.SaveChangesAsync(ct);
        dbContext.ChangeTracker.Clear();

        return new StagedAssetMergeSummary(
            stagedAssets.Count,
            mergedAssetCount,
            stagedLinks.Count,
            softwareLinkSummary.ResolvedLinkCount,
            softwareLinkSummary.InstallationsCreated,
            softwareLinkSummary.InstallationsTouched,
            softwareLinkSummary.EpisodesOpened,
            softwareLinkSummary.EpisodesSeen,
            softwareLinkSummary.StaleInstallationsMarked,
            softwareLinkSummary.InstallationsRemoved
        );
    }

    private async Task<StagedAssetSoftwareLinkSummary> ProcessDeviceSoftwareLinksAsync(
        Guid tenantId,
        IReadOnlyList<IngestionDeviceSoftwareLink> links,
        Dictionary<string, Guid> assetIdsByExternalId,
        CancellationToken ct
    )
    {
        if (links.Count == 0)
        {
            return new StagedAssetSoftwareLinkSummary(0, 0, 0, 0, 0, 0, 0);
        }

        var persistedAssetIds = await dbContext
            .Assets.IgnoreQueryFilters()
            .Where(asset => asset.TenantId == tenantId)
            .Select(asset => new { asset.Id, asset.ExternalId })
            .ToListAsync(ct);

        foreach (var asset in persistedAssetIds)
        {
            assetIdsByExternalId.TryAdd(asset.ExternalId, asset.Id);
        }

        var resolvedLinks = links
            .Select(link =>
            {
                if (
                    assetIdsByExternalId.TryGetValue(link.DeviceExternalId, out var deviceAssetId)
                    && assetIdsByExternalId.TryGetValue(
                        link.SoftwareExternalId,
                        out var softwareAssetId
                    )
                )
                {
                    return new StagedAssetResolvedDeviceSoftwareLink(
                        link.DeviceExternalId,
                        link.SoftwareExternalId,
                        deviceAssetId,
                        softwareAssetId,
                        link.ObservedAt
                    );
                }

                return null;
            })
            .Where(link => link is not null)
            .Cast<StagedAssetResolvedDeviceSoftwareLink>()
            .ToList();

        if (resolvedLinks.Count == 0)
        {
            return new StagedAssetSoftwareLinkSummary(0, 0, 0, 0, 0, 0, 0);
        }

        var installationsCreated = 0;
        var installationsTouched = 0;
        var episodesOpened = 0;
        var episodesSeen = 0;

        var deviceAssetIds = resolvedLinks.Select(link => link.DeviceAssetId).Distinct().ToList();
        var softwareAssetIds = resolvedLinks
            .Select(link => link.SoftwareAssetId)
            .Distinct()
            .ToList();

        var existingInstallations = await dbContext
            .DeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(current =>
                current.TenantId == tenantId
                && deviceAssetIds.Contains(current.DeviceAssetId)
                && softwareAssetIds.Contains(current.SoftwareAssetId)
            )
            .ToListAsync(ct);
        var installationsByPair = existingInstallations.ToDictionary(current =>
            BuildPairKey(current.DeviceAssetId, current.SoftwareAssetId)
        );

        var openEpisodes = await dbContext
            .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
            .Where(current =>
                current.TenantId == tenantId
                && current.RemovedAt == null
                && deviceAssetIds.Contains(current.DeviceAssetId)
                && softwareAssetIds.Contains(current.SoftwareAssetId)
            )
            .ToListAsync(ct);
        var openEpisodesByPair = openEpisodes.ToDictionary(current =>
            BuildPairKey(current.DeviceAssetId, current.SoftwareAssetId)
        );

        var latestEpisodeNumbers = await dbContext
            .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
            .Where(current =>
                current.TenantId == tenantId
                && deviceAssetIds.Contains(current.DeviceAssetId)
                && softwareAssetIds.Contains(current.SoftwareAssetId)
            )
            .GroupBy(current => new { current.DeviceAssetId, current.SoftwareAssetId })
            .Select(group => new
            {
                group.Key.DeviceAssetId,
                group.Key.SoftwareAssetId,
                EpisodeNumber = group.Max(current => current.EpisodeNumber),
            })
            .ToListAsync(ct);
        var latestEpisodeNumbersByPair = latestEpisodeNumbers.ToDictionary(
            current => BuildPairKey(current.DeviceAssetId, current.SoftwareAssetId),
            current => current.EpisodeNumber
        );

        foreach (var link in resolvedLinks)
        {
            var pairKey = BuildPairKey(link.DeviceAssetId, link.SoftwareAssetId);
            if (!installationsByPair.TryGetValue(pairKey, out var existing))
            {
                existing = DeviceSoftwareInstallation.Create(
                    tenantId,
                    link.DeviceAssetId,
                    link.SoftwareAssetId,
                    link.ObservedAt
                );
                await dbContext.DeviceSoftwareInstallations.AddAsync(existing, ct);
                installationsByPair[pairKey] = existing;
                installationsCreated++;
            }
            else
            {
                existing.Touch(link.ObservedAt);
                installationsTouched++;
            }

            if (openEpisodesByPair.TryGetValue(pairKey, out var openEpisode))
            {
                openEpisode.Seen(link.ObservedAt);
                episodesSeen++;
                continue;
            }

            var nextEpisodeNumber = latestEpisodeNumbersByPair.GetValueOrDefault(pairKey, 0) + 1;
            var episode = DeviceSoftwareInstallationEpisode.Create(
                tenantId,
                link.DeviceAssetId,
                link.SoftwareAssetId,
                nextEpisodeNumber,
                link.ObservedAt
            );
            await dbContext.DeviceSoftwareInstallationEpisodes.AddAsync(episode, ct);
            openEpisodesByPair[pairKey] = episode;
            latestEpisodeNumbersByPair[pairKey] = nextEpisodeNumber;
            episodesOpened++;
        }

        var seenKeys = resolvedLinks
            .Select(link => $"{link.DeviceExternalId}:{link.SoftwareExternalId}")
            .ToHashSet(StringComparer.Ordinal);
        var currentInstallations = await dbContext
            .DeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(current => current.TenantId == tenantId)
            .ToListAsync(ct);

        if (currentInstallations.Count == 0)
        {
            return new StagedAssetSoftwareLinkSummary(
                resolvedLinks.Count,
                installationsCreated,
                installationsTouched,
                episodesOpened,
                episodesSeen,
                0,
                0
            );
        }

        var externalIdsByAssetId = assetIdsByExternalId.ToDictionary(
            pair => pair.Value,
            pair => pair.Key
        );
        var staleInstallations = new List<DeviceSoftwareInstallation>();

        foreach (var installation in currentInstallations)
        {
            if (
                !externalIdsByAssetId.TryGetValue(
                    installation.DeviceAssetId,
                    out var deviceExternalId
                )
                || !externalIdsByAssetId.TryGetValue(
                    installation.SoftwareAssetId,
                    out var softwareExternalId
                )
            )
            {
                continue;
            }

            if (seenKeys.Contains($"{deviceExternalId}:{softwareExternalId}"))
            {
                continue;
            }

            staleInstallations.Add(installation);
        }

        if (staleInstallations.Count == 0)
        {
            return new StagedAssetSoftwareLinkSummary(
                resolvedLinks.Count,
                installationsCreated,
                installationsTouched,
                episodesOpened,
                episodesSeen,
                0,
                0
            );
        }

        var staleOpenEpisodes = await dbContext
            .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
            .Where(current =>
                current.TenantId == tenantId
                && current.RemovedAt == null
                && staleInstallations
                    .Select(item => item.DeviceAssetId)
                    .Contains(current.DeviceAssetId)
                && staleInstallations
                    .Select(item => item.SoftwareAssetId)
                    .Contains(current.SoftwareAssetId)
            )
            .ToListAsync(ct);
        var staleOpenEpisodeByPair = staleOpenEpisodes.ToDictionary(current =>
            BuildPairKey(current.DeviceAssetId, current.SoftwareAssetId)
        );
        var now = DateTimeOffset.UtcNow;

        var installationsRemoved = 0;
        foreach (var staleInstallation in staleInstallations)
        {
            staleInstallation.MarkMissing();
            var pairKey = BuildPairKey(
                staleInstallation.DeviceAssetId,
                staleInstallation.SoftwareAssetId
            );
            if (staleOpenEpisodeByPair.TryGetValue(pairKey, out var openEpisode))
            {
                openEpisode.MarkMissing();
                if (openEpisode.MissingSyncCount >= 2)
                {
                    openEpisode.Remove(now);
                }
            }

            if (staleInstallation.MissingSyncCount >= 2)
            {
                dbContext.DeviceSoftwareInstallations.Remove(staleInstallation);
                installationsRemoved++;
            }
        }

        return new StagedAssetSoftwareLinkSummary(
            resolvedLinks.Count,
            installationsCreated,
            installationsTouched,
            episodesOpened,
            episodesSeen,
            staleInstallations.Count,
            installationsRemoved
        );
    }

    private static string BuildPairKey(Guid leftId, Guid rightId)
    {
        return $"{leftId:N}:{rightId:N}";
    }

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        for (var index = 0; index < items.Count; index += size)
        {
            var count = Math.Min(size, items.Count - index);
            var chunk = new List<T>(count);
            for (var offset = 0; offset < count; offset++)
            {
                chunk.Add(items[index + offset]);
            }

            yield return chunk;
        }
    }
}

public sealed record StagedAssetMergeSummary(
    int StagedAssetCount,
    int MergedAssetCount,
    int StagedSoftwareLinkCount,
    int ResolvedSoftwareLinkCount,
    int InstallationsCreated,
    int InstallationsTouched,
    int EpisodesOpened,
    int EpisodesSeen,
    int StaleInstallationsMarked,
    int InstallationsRemoved
);

internal sealed record StagedAssetSoftwareLinkSummary(
    int ResolvedLinkCount,
    int InstallationsCreated,
    int InstallationsTouched,
    int EpisodesOpened,
    int EpisodesSeen,
    int StaleInstallationsMarked,
    int InstallationsRemoved
);

internal sealed record StagedAssetResolvedDeviceSoftwareLink(
    string DeviceExternalId,
    string SoftwareExternalId,
    Guid DeviceAssetId,
    Guid SoftwareAssetId,
    DateTimeOffset ObservedAt
);
