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
        var stagedMachineCount = await dbContext
            .StagedAssets.IgnoreQueryFilters()
            .CountAsync(
                item =>
                    item.IngestionRunId == ingestionRunId
                    && item.TenantId == tenantId
                    && item.SourceKey == normalizedSourceKey
                    && item.AssetType == AssetType.Device,
                ct
            );
        var stagedSoftwareCount = await dbContext
            .StagedAssets.IgnoreQueryFilters()
            .CountAsync(
                item =>
                    item.IngestionRunId == ingestionRunId
                    && item.TenantId == tenantId
                    && item.SourceKey == normalizedSourceKey
                    && item.AssetType == AssetType.Software,
                ct
            );
        var stagedLinkCount = await dbContext
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .CountAsync(
                item =>
                    item.IngestionRunId == ingestionRunId
                    && item.TenantId == tenantId
                    && item.SourceKey == normalizedSourceKey,
                ct
            );

        var mergedAssetCount = 0;
        var persistedMachineCount = 0;
        var persistedSoftwareCount = 0;
        Guid? lastProcessedAssetId = null;

        while (true)
        {
            var chunk = await dbContext
                .StagedAssets.IgnoreQueryFilters()
                .Where(item =>
                    item.IngestionRunId == ingestionRunId
                    && item.TenantId == tenantId
                    && item.SourceKey == normalizedSourceKey
                    && (!lastProcessedAssetId.HasValue || item.Id.CompareTo(lastProcessedAssetId.Value) > 0)
                )
                .OrderBy(item => item.Id)
                .Take(AssetChunkSize)
                .ToListAsync(ct);

            if (chunk.Count == 0)
            {
                break;
            }

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
                if (asset.AssetType == AssetType.Device)
                {
                    persistedMachineCount++;
                }
                else if (asset.AssetType == AssetType.Software)
                {
                    persistedSoftwareCount++;
                }

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
            lastProcessedAssetId = chunk[^1].Id;
        }

        var softwareLinkSummary = await ProcessDeviceSoftwareLinksAsync(
            ingestionRunId,
            tenantId,
            normalizedSourceKey,
            ct
        );

        await dbContext.SaveChangesAsync(ct);
        dbContext.ChangeTracker.Clear();

        return new StagedAssetMergeSummary(
            stagedMachineCount,
            stagedSoftwareCount,
            mergedAssetCount,
            persistedMachineCount,
            persistedSoftwareCount,
            stagedLinkCount,
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
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        CancellationToken ct
    )
    {
        var assetIdsByExternalId = await dbContext
            .Assets.IgnoreQueryFilters()
            .Where(asset => asset.TenantId == tenantId)
            .Select(asset => new { asset.Id, asset.ExternalId })
            .ToDictionaryAsync(asset => asset.ExternalId, asset => asset.Id, StringComparer.Ordinal, ct);

        var resolvedLinkCount = 0;
        var installationsCreated = 0;
        var installationsTouched = 0;
        var episodesOpened = 0;
        var episodesSeen = 0;
        var stagedPairKeys = new HashSet<string>(StringComparer.Ordinal);
        Guid? lastProcessedLinkId = null;

        while (true)
        {
            var linkChunk = await dbContext
                .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
                .Where(item =>
                    item.IngestionRunId == ingestionRunId
                    && item.TenantId == tenantId
                    && item.SourceKey == sourceKey
                    && (!lastProcessedLinkId.HasValue || item.Id.CompareTo(lastProcessedLinkId.Value) > 0)
                )
                .OrderBy(item => item.Id)
                .Take(AssetChunkSize)
                .ToListAsync(ct);

            if (linkChunk.Count == 0)
            {
                break;
            }

            var links = linkChunk
                .Select(item =>
                    JsonSerializer.Deserialize<IngestionDeviceSoftwareLink>(
                        item.PayloadJson,
                        StagingJsonOptions
                    )
                )
                .Where(item => item is not null)
                .Cast<IngestionDeviceSoftwareLink>()
                .ToList();

            var chunkSummary = await ProcessDeviceSoftwareLinkChunkAsync(
                tenantId,
                links,
                assetIdsByExternalId,
                ct
            );

            foreach (var pairKey in chunkSummary.SeenPairKeys)
            {
                stagedPairKeys.Add(pairKey);
            }

            resolvedLinkCount += chunkSummary.ResolvedLinkCount;
            installationsCreated += chunkSummary.InstallationsCreated;
            installationsTouched += chunkSummary.InstallationsTouched;
            episodesOpened += chunkSummary.EpisodesOpened;
            episodesSeen += chunkSummary.EpisodesSeen;
            await dbContext.SaveChangesAsync(ct);
            dbContext.ChangeTracker.Clear();
            lastProcessedLinkId = linkChunk[^1].Id;
        }

        var staleSummary = await ReconcileMissingDeviceSoftwareLinksAsync(
            tenantId,
            assetIdsByExternalId,
            stagedPairKeys,
            ct
        );

        return new StagedAssetSoftwareLinkSummary(
            resolvedLinkCount,
            installationsCreated,
            installationsTouched,
            episodesOpened,
            episodesSeen,
            staleSummary.StaleInstallationsMarked,
            staleSummary.InstallationsRemoved
        );
    }

    private async Task<StagedAssetSoftwareLinkChunkSummary> ProcessDeviceSoftwareLinkChunkAsync(
        Guid tenantId,
        IReadOnlyList<IngestionDeviceSoftwareLink> links,
        IReadOnlyDictionary<string, Guid> assetIdsByExternalId,
        CancellationToken ct
    )
    {
        if (links.Count == 0)
        {
            return new StagedAssetSoftwareLinkChunkSummary(
                0,
                0,
                0,
                0,
                0,
                new HashSet<string>(StringComparer.Ordinal)
            );
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
            return new StagedAssetSoftwareLinkChunkSummary(
                0,
                0,
                0,
                0,
                0,
                new HashSet<string>(StringComparer.Ordinal)
            );
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

        return new StagedAssetSoftwareLinkChunkSummary(
            resolvedLinks.Count,
            installationsCreated,
            installationsTouched,
            episodesOpened,
            episodesSeen,
            seenKeys
        );
    }

    private async Task<StagedAssetStaleLinkSummary> ReconcileMissingDeviceSoftwareLinksAsync(
        Guid tenantId,
        IReadOnlyDictionary<string, Guid> assetIdsByExternalId,
        IReadOnlySet<string> stagedPairKeys,
        CancellationToken ct
    )
    {
        var currentInstallations = await dbContext
            .DeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(current => current.TenantId == tenantId)
            .ToListAsync(ct);

        if (currentInstallations.Count == 0)
        {
            return new StagedAssetStaleLinkSummary(0, 0);
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

            if (stagedPairKeys.Contains($"{deviceExternalId}:{softwareExternalId}"))
            {
                continue;
            }

            staleInstallations.Add(installation);
        }

        if (staleInstallations.Count == 0)
        {
            return new StagedAssetStaleLinkSummary(0, 0);
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

        return new StagedAssetStaleLinkSummary(staleInstallations.Count, installationsRemoved);
    }

    private static string BuildPairKey(Guid leftId, Guid rightId)
    {
        return $"{leftId:N}:{rightId:N}";
    }
}

public sealed record StagedAssetMergeSummary(
    int StagedMachineCount,
    int StagedSoftwareCount,
    int MergedAssetCount,
    int PersistedMachineCount,
    int PersistedSoftwareCount,
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

internal sealed record StagedAssetSoftwareLinkChunkSummary(
    int ResolvedLinkCount,
    int InstallationsCreated,
    int InstallationsTouched,
    int EpisodesOpened,
    int EpisodesSeen,
    IReadOnlySet<string> SeenPairKeys
);

internal sealed record StagedAssetStaleLinkSummary(
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
