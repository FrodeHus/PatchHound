using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models.Assets;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Services;

public class AssetDetailQueryService(
    PatchHoundDbContext dbContext,
    TenantSnapshotResolver snapshotResolver,
    TenantSoftwareAliasResolver aliasResolver,
    RemediationTaskQueryService remediationTaskQueryService
)
{
    private sealed record SoftwareCorrelationRow(
        Guid SoftwareAssetId,
        int EpisodeNumber,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt,
        DateTimeOffset? RemovedAt
    );

    public async Task<AssetDetailDto?> BuildAsync(
        Guid tenantId,
        Guid assetId,
        CancellationToken ct
    )
    {
        var activeSnapshotId = await snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(tenantId, ct);

        var asset = await dbContext
            .Assets.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assetId && a.TenantId == tenantId, ct);
        if (asset is null)
            return null;

        var securityProfile = asset.SecurityProfileId is Guid securityProfileId
            ? await dbContext
                .AssetSecurityProfiles.AsNoTracking()
                .Where(profile => profile.Id == securityProfileId)
                .Select(profile => new AssetSecurityProfileSummaryDto(
                    profile.Id,
                    profile.Name,
                    profile.EnvironmentClass.ToString(),
                    profile.InternetReachability.ToString(),
                    profile.ConfidentialityRequirement.ToString(),
                    profile.IntegrityRequirement.ToString(),
                    profile.AvailabilityRequirement.ToString()
                ))
                .FirstOrDefaultAsync(ct)
            : null;
        var businessLabels = await dbContext.AssetBusinessLabels.AsNoTracking()
            .Where(link => link.AssetId == assetId)
            .OrderBy(link => link.BusinessLabel.Name)
            .Select(link => new BusinessLabelSummaryDto(
                link.BusinessLabel.Id,
                link.BusinessLabel.Name,
                link.BusinessLabel.Description,
                link.BusinessLabel.Color
            ))
            .ToListAsync(ct);
        businessLabels = businessLabels
            .GroupBy(label => label.Id)
            .Select(group => group.First())
            .OrderBy(label => label.Name)
            .ToList();

        var ownerUserName = asset.OwnerUserId is Guid ownerUserId
            ? await dbContext.Users.AsNoTracking()
                .Where(user => user.Id == ownerUserId)
                .Select(user => user.DisplayName)
                .FirstOrDefaultAsync(ct)
            : null;

        var relevantTeamIds = new[] { asset.OwnerTeamId, asset.FallbackTeamId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        var teamNamesById = relevantTeamIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Teams.AsNoTracking()
                .Where(team => relevantTeamIds.Contains(team.Id))
                .ToDictionaryAsync(team => team.Id, team => team.Name, ct);

        var episodeRows = await dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(episode => episode.AssetId == assetId)
            .OrderBy(episode => episode.TenantVulnerabilityId)
            .ThenBy(episode => episode.EpisodeNumber)
            .Select(episode => new
            {
                VulnerabilityId = episode.TenantVulnerabilityId,
                episode.EpisodeNumber,
                episode.Status,
                episode.FirstSeenAt,
                episode.LastSeenAt,
                episode.ResolvedAt,
            })
            .ToListAsync(ct);

        var episodesByVulnerabilityId = episodeRows
            .GroupBy(row => row.VulnerabilityId)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .Select(row => new AssetVulnerabilityEpisodeDto(
                            row.EpisodeNumber,
                            row.Status.ToString(),
                            row.FirstSeenAt,
                            row.LastSeenAt,
                            row.ResolvedAt
                        ))
                        .ToList() as IReadOnlyList<AssetVulnerabilityEpisodeDto>
            );

        var softwareEpisodeRows = await dbContext
            .DeviceSoftwareInstallationEpisodes.AsNoTracking()
            .Where(episode => episode.DeviceAssetId == assetId)
            .OrderBy(episode => episode.SoftwareAssetId)
            .ThenBy(episode => episode.EpisodeNumber)
            .Select(episode => new SoftwareCorrelationRow(
                episode.SoftwareAssetId,
                episode.EpisodeNumber,
                episode.FirstSeenAt,
                episode.LastSeenAt,
                episode.RemovedAt
            ))
            .ToListAsync(ct);

        var softwareEpisodesByAssetId = softwareEpisodeRows
            .GroupBy(row => row.SoftwareAssetId)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .Select(row => new AssetSoftwareInstallationEpisodeDto(
                            row.EpisodeNumber,
                            row.FirstSeenAt,
                            row.LastSeenAt,
                            row.RemovedAt
                        ))
                        .ToList() as IReadOnlyList<AssetSoftwareInstallationEpisodeDto>
            );

        var softwareRows = await dbContext
            .DeviceSoftwareInstallations.AsNoTracking()
            .Where(link => link.DeviceAssetId == assetId)
            .Join(
                dbContext.Assets,
                link => link.SoftwareAssetId,
                software => software.Id,
                (link, software) =>
                    new
                    {
                        software.Id,
                        software.Name,
                        software.ExternalId,
                        link.LastSeenAt,
                    }
            )
            .ToListAsync(ct);

        var relevantSoftwareAssets = softwareRows
            .Select(row => new { row.Id, row.ExternalId })
            .ToList();
        if (asset.AssetType == AssetType.Software)
        {
            relevantSoftwareAssets.Add(new { Id = asset.Id, asset.ExternalId });
        }

        var relevantSoftwareExternalIds = relevantSoftwareAssets
            .Select(item => item.ExternalId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var resolvedAliases = await aliasResolver.ResolveByExternalIdsAsync(
            asset.TenantId,
            relevantSoftwareExternalIds,
            ct
        );
        var tenantSoftwareIdsByExternalId = resolvedAliases.ToDictionary(
            kvp => kvp.Key,
            kvp => (Guid?)kvp.Value.TenantSoftwareId,
            StringComparer.Ordinal
        );
        var normalizedSoftwareIdsByExternalId = resolvedAliases.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.NormalizedSoftwareId,
            StringComparer.Ordinal
        );

        var normalizedSoftwareIds = normalizedSoftwareIdsByExternalId.Values.Distinct().ToList();
        var cpeBindingsByNormalizedSoftwareId = await dbContext
            .SoftwareCpeBindings.AsNoTracking()
            .Where(binding => normalizedSoftwareIds.Contains(binding.NormalizedSoftwareId))
            .ToDictionaryAsync(
                binding => binding.NormalizedSoftwareId,
                binding => new SoftwareCpeBindingDto(
                    binding.Id,
                    binding.Cpe23Uri,
                    binding.BindingMethod.ToString(),
                    binding.Confidence.ToString(),
                    binding.MatchedVendor,
                    binding.MatchedProduct,
                    binding.MatchedVersion,
                    binding.LastValidatedAt
                ),
                ct
            );

        var softwareNamesByAssetId = softwareRows.ToDictionary(row => row.Id, row => row.Name);

        var assessmentsByVulnerabilityId = await dbContext
            .VulnerabilityAssetAssessments.AsNoTracking()
            .Where(assessment =>
                assessment.AssetId == assetId && assessment.SnapshotId == activeSnapshotId
            )
            .ToDictionaryAsync(assessment => assessment.TenantVulnerabilityId, ct);

        var possibleCorrelationsByVulnerabilityId = episodeRows
            .GroupBy(row => row.VulnerabilityId)
            .ToDictionary(
                group => group.Key,
                group =>
                    RankPossibleCorrelatedSoftware(
                        softwareEpisodeRows,
                        softwareNamesByAssetId,
                        group
                            .Select(row => new AssetVulnerabilityEpisodeDto(
                                row.EpisodeNumber,
                                row.Status.ToString(),
                                row.FirstSeenAt,
                                row.LastSeenAt,
                                row.ResolvedAt
                            ))
                            .ToList()
                    )
            );

        var vulnerabilityRows = await dbContext
            .VulnerabilityAssets.AsNoTracking()
            .Where(va => va.AssetId == assetId && va.SnapshotId == activeSnapshotId)
            .Join(
                dbContext.TenantVulnerabilities.AsNoTracking(),
                va => va.TenantVulnerabilityId,
                tv => tv.Id,
                (va, tv) =>
                    new
                    {
                        Id = tv.Id,
                        ExternalId = tv.VulnerabilityDefinition.ExternalId,
                        Title = tv.VulnerabilityDefinition.Title,
                        Description = tv.VulnerabilityDefinition.Description,
                        VendorSeverity = tv.VulnerabilityDefinition.VendorSeverity.ToString(),
                        CvssVector = tv.VulnerabilityDefinition.CvssVector,
                        PublishedDate = tv.VulnerabilityDefinition.PublishedDate,
                        Status = va.Status.ToString(),
                        va.DetectedDate,
                        va.ResolvedDate,
                    }
            )
            .ToListAsync(ct);

        IReadOnlyList<AssetKnownSoftwareVulnerabilityDto> softwareVulnerabilityRows = [];
        if (asset.AssetType == AssetType.Software)
        {
            var softwareVulnerabilityItems = await dbContext
                .SoftwareVulnerabilityMatches.AsNoTracking()
                .Where(match => match.SoftwareAssetId == assetId && match.ResolvedAt == null)
                .Join(
                    dbContext.VulnerabilityDefinitions.AsNoTracking(),
                    match => match.VulnerabilityDefinitionId,
                    vulnerability => vulnerability.Id,
                    (match, vulnerability) =>
                        new
                        {
                            vulnerability.Id,
                            vulnerability.ExternalId,
                            vulnerability.Title,
                            VendorSeverity = vulnerability.VendorSeverity.ToString(),
                            vulnerability.CvssScore,
                            vulnerability.CvssVector,
                            MatchMethod = match.MatchMethod.ToString(),
                            Confidence = match.Confidence.ToString(),
                            match.Evidence,
                            match.FirstSeenAt,
                            match.LastSeenAt,
                            match.ResolvedAt,
                        }
                )
                .OrderBy(item => item.ExternalId)
                .ToListAsync(ct);

            softwareVulnerabilityRows = softwareVulnerabilityItems
                .Select(item => new AssetKnownSoftwareVulnerabilityDto(
                    item.Id,
                    item.ExternalId,
                    item.Title,
                    item.VendorSeverity,
                    item.CvssScore,
                    item.CvssVector,
                    item.MatchMethod,
                    item.Confidence,
                    item.Evidence,
                    item.FirstSeenAt,
                    item.LastSeenAt,
                    item.ResolvedAt
                ))
                .ToList();
        }

        var vulnerabilities = vulnerabilityRows
            .Select(row =>
            {
                assessmentsByVulnerabilityId.TryGetValue(row.Id, out var assessment);
                episodesByVulnerabilityId.TryGetValue(row.Id, out var episodeHistory);
                possibleCorrelationsByVulnerabilityId.TryGetValue(
                    row.Id,
                    out var correlatedSoftware
                );

                return new AssetVulnerabilityDto(
                    row.Id,
                    row.ExternalId,
                    row.Title,
                    row.Description,
                    row.VendorSeverity,
                    assessment?.BaseScore,
                    assessment?.BaseVector ?? row.CvssVector,
                    row.PublishedDate,
                    assessment?.EffectiveSeverity.ToString() ?? row.VendorSeverity,
                    assessment?.EffectiveScore,
                    assessment?.ReasonSummary,
                    row.Status,
                    row.DetectedDate,
                    row.ResolvedDate,
                    episodeHistory?.Count ?? 0,
                    episodeHistory ?? [],
                    correlatedSoftware ?? []
                );
            })
            .ToList();

        var softwareInventory = softwareRows
            .Select(row => new AssetSoftwareInstallationDto(
                row.Id,
                tenantSoftwareIdsByExternalId.TryGetValue(row.ExternalId, out var tenantSoftwareId)
                    ? tenantSoftwareId
                    : null,
                row.Name,
                row.ExternalId,
                row.LastSeenAt,
                normalizedSoftwareIdsByExternalId.TryGetValue(row.ExternalId, out var normalizedSoftwareId)
                    ? cpeBindingsByNormalizedSoftwareId.GetValueOrDefault(normalizedSoftwareId)
                    : null,
                softwareEpisodesByAssetId.TryGetValue(row.Id, out var episodes)
                    ? episodes.Count
                    : 0,
                softwareEpisodesByAssetId.TryGetValue(row.Id, out var episodeHistory)
                    ? episodeHistory
                    : []
            ))
            .ToList();

        var tags = await dbContext.AssetTags
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .Select(t => t.Tag)
            .ToArrayAsync(ct);

        var assetRiskScore = await dbContext.AssetRiskScores
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.AssetId == assetId)
            .Select(item => new
            {
                item.OverallScore,
                item.MaxEpisodeRiskScore,
                item.CriticalCount,
                item.HighCount,
                item.MediumCount,
                item.LowCount,
                item.OpenEpisodeCount,
                item.FactorsJson,
                item.CalculationVersion,
                item.CalculatedAt,
            })
            .FirstOrDefaultAsync(ct);

        AssetRiskDetailDto? risk = null;
        if (assetRiskScore is not null)
        {
            var topDrivers = await dbContext.VulnerabilityEpisodeRiskAssessments
                .AsNoTracking()
                .Where(item => item.AssetId == assetId && item.TenantId == tenantId && item.ResolvedAt == null)
                .OrderByDescending(item => item.EpisodeRiskScore)
                .Take(5)
                .Select(item => new AssetRiskDriverDto(
                    item.TenantVulnerabilityId,
                    item.TenantVulnerability.VulnerabilityDefinition.ExternalId,
                    item.TenantVulnerability.VulnerabilityDefinition.Title,
                    item.RiskBand,
                    item.EpisodeRiskScore,
                    item.ThreatScore,
                    item.ContextScore,
                    item.OperationalScore
                ))
                .ToListAsync(ct);

            risk = new AssetRiskDetailDto(
                assetRiskScore.OverallScore,
                assetRiskScore.MaxEpisodeRiskScore,
                ResolveRiskBand(assetRiskScore.OverallScore),
                assetRiskScore.OpenEpisodeCount,
                assetRiskScore.CriticalCount,
                assetRiskScore.HighCount,
                assetRiskScore.MediumCount,
                assetRiskScore.LowCount,
                assetRiskScore.CalculatedAt,
                ToAssetRiskExplanationDto(
                    assetRiskScore.OverallScore,
                    assetRiskScore.MaxEpisodeRiskScore,
                    assetRiskScore.OpenEpisodeCount,
                    assetRiskScore.CriticalCount,
                    assetRiskScore.HighCount,
                    assetRiskScore.MediumCount,
                    assetRiskScore.LowCount,
                    assetRiskScore.FactorsJson,
                    assetRiskScore.CalculationVersion
                ),
                topDrivers
            );
        }

        var remediation = asset.AssetType == AssetType.Device
            ? await remediationTaskQueryService.BuildDeviceSummaryAsync(tenantId, assetId, ct)
            : null;

        return new AssetDetailDto(
            asset.Id,
            asset.AssetType == AssetType.Software
                && tenantSoftwareIdsByExternalId.TryGetValue(asset.ExternalId, out var assetTenantSoftwareId)
                ? assetTenantSoftwareId
                : null,
            asset.ExternalId,
            asset.Name,
            asset.Description,
            asset.AssetType.ToString(),
            asset.Criticality.ToString(),
            asset.CriticalitySource is { Length: > 0 }
                ? new AssetCriticalityDetailDto(
                    asset.CriticalitySource,
                    asset.CriticalityReason,
                    asset.CriticalityRuleId,
                    asset.CriticalityUpdatedAt
                )
                : null,
            asset.OwnerType.ToString(),
            ownerUserName,
            asset.OwnerUserId,
            asset.OwnerTeamId is Guid ownerTeamId
                ? teamNamesById.GetValueOrDefault(ownerTeamId)
                : null,
            asset.OwnerTeamId,
            asset.FallbackTeamId is Guid fallbackTeamId
                ? teamNamesById.GetValueOrDefault(fallbackTeamId)
                : null,
            asset.FallbackTeamId,
            securityProfile,
            asset.DeviceComputerDnsName,
            asset.DeviceHealthStatus,
            asset.DeviceOsPlatform,
            asset.DeviceOsVersion,
            asset.DeviceRiskScore,
            asset.DeviceLastSeenAt,
            asset.DeviceLastIpAddress,
            asset.DeviceAadDeviceId,
            asset.DeviceGroupId,
            asset.DeviceGroupName,
            asset.DeviceExposureLevel,
            asset.DeviceIsAadJoined,
            asset.DeviceOnboardingStatus,
            asset.DeviceValue,
            businessLabels,
            risk,
            remediation,
            tags,
            asset.AssetType == AssetType.Software
                && normalizedSoftwareIdsByExternalId.TryGetValue(asset.ExternalId, out var assetNormalizedSoftwareId)
                ? cpeBindingsByNormalizedSoftwareId.GetValueOrDefault(assetNormalizedSoftwareId)
                : null,
            asset.Metadata,
            vulnerabilities,
            softwareInventory,
            softwareVulnerabilityRows
        );
    }

    private static string ResolveRiskBand(decimal score)
    {
        if (score >= 900m)
        {
            return "Critical";
        }

        if (score >= 750m)
        {
            return "High";
        }

        if (score >= 500m)
        {
            return "Medium";
        }

        return "Low";
    }

    private static AssetRiskExplanationDto ToAssetRiskExplanationDto(
        decimal overallScore,
        decimal maxEpisodeRiskScore,
        int openEpisodeCount,
        int criticalCount,
        int highCount,
        int mediumCount,
        int lowCount,
        string factorsJson,
        string calculationVersion
    )
    {
        var factors = ParseRiskFactors(factorsJson);
        var topThreeAverage = factors.FirstOrDefault(item => item.Name == "TopThreeAverage")?.Impact ?? 0m;
        var criticalContribution = factors.FirstOrDefault(item => item.Name == "CriticalEpisodes")?.Impact ?? 0m;
        var highContribution = factors.FirstOrDefault(item => item.Name == "HighEpisodes")?.Impact ?? 0m;
        var mediumContribution = factors.FirstOrDefault(item => item.Name == "MediumEpisodes")?.Impact ?? 0m;
        var lowContribution = factors.FirstOrDefault(item => item.Name == "LowEpisodes")?.Impact ?? 0m;

        return new AssetRiskExplanationDto(
            overallScore,
            calculationVersion,
            maxEpisodeRiskScore,
            topThreeAverage,
            Math.Round(0.7m * maxEpisodeRiskScore, 2),
            Math.Round(0.2m * topThreeAverage, 2),
            openEpisodeCount,
            criticalCount,
            highCount,
            mediumCount,
            lowCount,
            criticalContribution,
            highContribution,
            mediumContribution,
            lowContribution,
            factors.Select(item => new AssetRiskExplanationFactorDto(
                item.Name,
                item.Description,
                item.Impact
            )).ToList()
        );
    }

    private static IReadOnlyList<ParsedRiskFactor> ParseRiskFactors(string factorsJson)
    {
        if (string.IsNullOrWhiteSpace(factorsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ParsedRiskFactor>>(factorsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> RankPossibleCorrelatedSoftware(
        IReadOnlyList<SoftwareCorrelationRow> softwareRows,
        IReadOnlyDictionary<Guid, string> softwareNamesByAssetId,
        IReadOnlyList<AssetVulnerabilityEpisodeDto> vulnerabilityEpisodes
    )
    {
        return SoftwareCorrelationRanker.Rank(
            softwareRows
                .Where(row => softwareNamesByAssetId.ContainsKey(row.SoftwareAssetId))
                .Select(row => new SoftwareCorrelationRanker.SoftwareInstallationInput(
                    softwareNamesByAssetId[row.SoftwareAssetId],
                    row.EpisodeNumber,
                    row.FirstSeenAt,
                    row.RemovedAt
                )),
            vulnerabilityEpisodes
                .Select(e => new SoftwareCorrelationRanker.VulnerabilityEpisodeInput(
                    e.EpisodeNumber,
                    e.FirstSeenAt
                ))
                .ToList()
        );
    }

    private sealed record ParsedRiskFactor(
        string Name,
        string Description,
        decimal Impact
    );
}
