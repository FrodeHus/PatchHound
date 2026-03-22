using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class RiskScoreService(
    PatchHoundDbContext dbContext,
    ILogger<RiskScoreService> logger
)
{
    public const string CalculationVersion = "1";
    private readonly record struct RiskFactor(string Name, string Description, decimal Impact);

    public record AssetRiskResult(
        Guid AssetId,
        decimal OverallScore,
        decimal MaxEpisodeRiskScore,
        int CriticalCount,
        int HighCount,
        int MediumCount,
        int LowCount,
        int OpenEpisodeCount,
        string FactorsJson
    );

    public record TenantRiskResult(
        decimal OverallScore,
        int AssetCount,
        int CriticalAssetCount,
        int HighAssetCount,
        List<AssetRiskResult> AssetScores
    );

    public record SoftwareRiskResult(
        Guid TenantSoftwareId,
        Guid? SnapshotId,
        decimal OverallScore,
        decimal MaxEpisodeRiskScore,
        int CriticalEpisodeCount,
        int HighEpisodeCount,
        int MediumEpisodeCount,
        int LowEpisodeCount,
        int AffectedDeviceCount,
        int OpenEpisodeCount,
        string FactorsJson
    );

    public record DeviceGroupRiskResult(
        string GroupKey,
        string? DeviceGroupId,
        string DeviceGroupName,
        decimal OverallScore,
        decimal MaxAssetRiskScore,
        int CriticalEpisodeCount,
        int HighEpisodeCount,
        int MediumEpisodeCount,
        int LowEpisodeCount,
        int AssetCount,
        int OpenEpisodeCount,
        string FactorsJson
    );

    public record TeamRiskResult(
        Guid TeamId,
        decimal OverallScore,
        decimal MaxAssetRiskScore,
        int CriticalEpisodeCount,
        int HighEpisodeCount,
        int MediumEpisodeCount,
        int LowEpisodeCount,
        int AssetCount,
        int OpenEpisodeCount,
        string FactorsJson
    );

    public async Task RecalculateForTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var assetResults = await CalculateAssetScoresAsync(tenantId, ct);
        var softwareResults = await CalculateSoftwareScoresAsync(tenantId, ct);
        var deviceGroupResults = await CalculateDeviceGroupScoresAsync(tenantId, assetResults, ct);
        var teamResults = await CalculateTeamScoresAsync(tenantId, assetResults, ct);
        var existingScores = await dbContext.AssetRiskScores
            .Where(item => item.TenantId == tenantId)
            .ToDictionaryAsync(item => item.AssetId, ct);
        var existingDeviceGroupScores = await dbContext.DeviceGroupRiskScores
            .Where(item => item.TenantId == tenantId)
            .ToDictionaryAsync(item => item.GroupKey, ct);
        var existingSoftwareScores = await dbContext.TenantSoftwareRiskScores
            .Where(item => item.TenantId == tenantId)
            .ToDictionaryAsync(item => item.TenantSoftwareId, ct);
        var existingTeamScores = await dbContext.TeamRiskScores
            .Where(item => item.TenantId == tenantId)
            .ToDictionaryAsync(item => item.TeamId, ct);

        foreach (var result in assetResults)
        {
            if (existingScores.TryGetValue(result.AssetId, out var existing))
            {
                existing.Update(
                    result.OverallScore,
                    result.MaxEpisodeRiskScore,
                    result.CriticalCount,
                    result.HighCount,
                    result.MediumCount,
                    result.LowCount,
                    result.OpenEpisodeCount,
                    result.FactorsJson,
                    CalculationVersion
                );
            }
            else
            {
                dbContext.AssetRiskScores.Add(
                    AssetRiskScore.Create(
                        tenantId,
                        result.AssetId,
                        result.OverallScore,
                        result.MaxEpisodeRiskScore,
                        result.CriticalCount,
                        result.HighCount,
                        result.MediumCount,
                        result.LowCount,
                        result.OpenEpisodeCount,
                        result.FactorsJson,
                        CalculationVersion
                    )
                );
            }
        }

        var activeAssetIds = assetResults.Select(item => item.AssetId).ToHashSet();
        var staleScores = existingScores.Values
            .Where(item => !activeAssetIds.Contains(item.AssetId))
            .ToList();
        if (staleScores.Count > 0)
        {
            dbContext.AssetRiskScores.RemoveRange(staleScores);
        }

        foreach (var result in deviceGroupResults)
        {
            if (existingDeviceGroupScores.TryGetValue(result.GroupKey, out var existing))
            {
                existing.Update(
                    result.DeviceGroupId,
                    result.DeviceGroupName,
                    result.OverallScore,
                    result.MaxAssetRiskScore,
                    result.CriticalEpisodeCount,
                    result.HighEpisodeCount,
                    result.MediumEpisodeCount,
                    result.LowEpisodeCount,
                    result.AssetCount,
                    result.OpenEpisodeCount,
                    result.FactorsJson,
                    CalculationVersion
                );
            }
            else
            {
                dbContext.DeviceGroupRiskScores.Add(
                    DeviceGroupRiskScore.Create(
                        tenantId,
                        result.GroupKey,
                        result.DeviceGroupId,
                        result.DeviceGroupName,
                        result.OverallScore,
                        result.MaxAssetRiskScore,
                        result.CriticalEpisodeCount,
                        result.HighEpisodeCount,
                        result.MediumEpisodeCount,
                        result.LowEpisodeCount,
                        result.AssetCount,
                        result.OpenEpisodeCount,
                        result.FactorsJson,
                        CalculationVersion
                    )
                );
            }
        }

        var activeGroupKeys = deviceGroupResults.Select(item => item.GroupKey).ToHashSet();
        var staleGroupScores = existingDeviceGroupScores.Values
            .Where(item => !activeGroupKeys.Contains(item.GroupKey))
            .ToList();
        if (staleGroupScores.Count > 0)
        {
            dbContext.DeviceGroupRiskScores.RemoveRange(staleGroupScores);
        }

        foreach (var result in softwareResults)
        {
            if (existingSoftwareScores.TryGetValue(result.TenantSoftwareId, out var existing))
            {
                existing.Update(
                    result.SnapshotId,
                    result.OverallScore,
                    result.MaxEpisodeRiskScore,
                    result.CriticalEpisodeCount,
                    result.HighEpisodeCount,
                    result.MediumEpisodeCount,
                    result.LowEpisodeCount,
                    result.AffectedDeviceCount,
                    result.OpenEpisodeCount,
                    result.FactorsJson,
                    CalculationVersion
                );
            }
            else
            {
                dbContext.TenantSoftwareRiskScores.Add(
                    TenantSoftwareRiskScore.Create(
                        tenantId,
                        result.TenantSoftwareId,
                        result.SnapshotId,
                        result.OverallScore,
                        result.MaxEpisodeRiskScore,
                        result.CriticalEpisodeCount,
                        result.HighEpisodeCount,
                        result.MediumEpisodeCount,
                        result.LowEpisodeCount,
                        result.AffectedDeviceCount,
                        result.OpenEpisodeCount,
                        result.FactorsJson,
                        CalculationVersion
                    )
                );
            }
        }

        var activeTenantSoftwareIds = softwareResults.Select(item => item.TenantSoftwareId).ToHashSet();
        var staleSoftwareScores = existingSoftwareScores.Values
            .Where(item => !activeTenantSoftwareIds.Contains(item.TenantSoftwareId))
            .ToList();
        if (staleSoftwareScores.Count > 0)
        {
            dbContext.TenantSoftwareRiskScores.RemoveRange(staleSoftwareScores);
        }

        foreach (var result in teamResults)
        {
            if (existingTeamScores.TryGetValue(result.TeamId, out var existing))
            {
                existing.Update(
                    result.OverallScore,
                    result.MaxAssetRiskScore,
                    result.CriticalEpisodeCount,
                    result.HighEpisodeCount,
                    result.MediumEpisodeCount,
                    result.LowEpisodeCount,
                    result.AssetCount,
                    result.OpenEpisodeCount,
                    result.FactorsJson,
                    CalculationVersion
                );
            }
            else
            {
                dbContext.TeamRiskScores.Add(
                    TeamRiskScore.Create(
                        tenantId,
                        result.TeamId,
                        result.OverallScore,
                        result.MaxAssetRiskScore,
                        result.CriticalEpisodeCount,
                        result.HighEpisodeCount,
                        result.MediumEpisodeCount,
                        result.LowEpisodeCount,
                        result.AssetCount,
                        result.OpenEpisodeCount,
                        result.FactorsJson,
                        CalculationVersion
                    )
                );
            }
        }

        var activeTeamIds = teamResults.Select(item => item.TeamId).ToHashSet();
        var staleTeamScores = existingTeamScores.Values
            .Where(item => !activeTeamIds.Contains(item.TeamId))
            .ToList();
        if (staleTeamScores.Count > 0)
        {
            dbContext.TeamRiskScores.RemoveRange(staleTeamScores);
        }

        await dbContext.SaveChangesAsync(ct);
        await RecordDailySnapshotAsync(tenantId, assetResults, ct);

        logger.LogInformation(
            "Risk scores recalculated for tenant {TenantId}. Asset scores: {AssetCount}. Software scores: {SoftwareCount}. Device groups: {DeviceGroupCount}. Teams: {TeamCount}.",
            tenantId,
            assetResults.Count,
            softwareResults.Count,
            deviceGroupResults.Count,
            teamResults.Count
        );
    }

    public async Task<TenantRiskResult> GetTenantRiskAsync(Guid tenantId, CancellationToken ct)
    {
        var assetScores = await dbContext.AssetRiskScores
            .Where(item => item.TenantId == tenantId)
            .OrderByDescending(item => item.OverallScore)
            .ToListAsync(ct);

        return CalculateTenantRisk(assetScores.Select(item => new AssetRiskResult(
            item.AssetId,
            item.OverallScore,
            item.MaxEpisodeRiskScore,
            item.CriticalCount,
            item.HighCount,
            item.MediumCount,
            item.LowCount,
            item.OpenEpisodeCount,
            item.FactorsJson
        )).ToList());
    }

    public async Task<TenantRiskResult> GetFilteredTenantRiskAsync(
        Guid tenantId,
        int? minAgeDays,
        string? platform,
        string? deviceGroup,
        CancellationToken ct
    )
    {
        var publishedBefore = minAgeDays.HasValue
            ? DateTime.UtcNow.Date.AddDays(-minAgeDays.Value)
            : (DateTime?)null;

        var episodeScores = await (
            from episodeRisk in dbContext.VulnerabilityEpisodeRiskAssessments.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking()
                on episodeRisk.AssetId equals asset.Id
            join tenantVulnerability in dbContext.TenantVulnerabilities.AsNoTracking()
                on episodeRisk.TenantVulnerabilityId equals tenantVulnerability.Id
            join definition in dbContext.VulnerabilityDefinitions.AsNoTracking()
                on tenantVulnerability.VulnerabilityDefinitionId equals definition.Id
            where episodeRisk.TenantId == tenantId
                  && episodeRisk.ResolvedAt == null
                  && asset.TenantId == tenantId
                  && (string.IsNullOrWhiteSpace(platform) || asset.DeviceOsPlatform == platform)
                  && (string.IsNullOrWhiteSpace(deviceGroup) || asset.DeviceGroupName == deviceGroup)
                  && (!publishedBefore.HasValue
                      || (definition.PublishedDate.HasValue && definition.PublishedDate.Value <= publishedBefore.Value))
            select new
            {
                episodeRisk.AssetId,
                episodeRisk.EpisodeRiskScore,
                episodeRisk.RiskBand,
            }
        ).ToListAsync(ct);

        return CalculateTenantRisk(BuildAssetRiskResults(episodeScores.Select(item => (
            item.AssetId,
            item.EpisodeRiskScore,
            item.RiskBand
        ))));
    }

    public async Task<List<TenantRiskScoreSnapshot>> GetRiskHistoryAsync(Guid tenantId, CancellationToken ct)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-29));
        return await dbContext.TenantRiskScoreSnapshots
            .Where(item => item.TenantId == tenantId && item.Date >= cutoff)
            .OrderBy(item => item.Date)
            .ToListAsync(ct);
    }

    public static TenantRiskResult CalculateTenantRisk(IReadOnlyList<AssetRiskResult> assetScores)
    {
        if (assetScores.Count == 0)
        {
            return new TenantRiskResult(0m, 0, 0, 0, []);
        }

        var ordered = assetScores
            .OrderByDescending(item => item.OverallScore)
            .ToList();
        var maxAsset = ordered[0].OverallScore;
        var topFiveAverage = ordered.Take(5).Average(item => item.OverallScore);
        var criticalAssetCount = ordered.Count(item => item.OverallScore >= 900m);
        var highAssetCount = ordered.Count(item => item.OverallScore >= 750m && item.OverallScore < 900m);
        var mediumAssetCount = ordered.Count(item => item.OverallScore >= 500m && item.OverallScore < 750m);
        var lowAssetCount = ordered.Count(item => item.OverallScore > 0m && item.OverallScore < 500m);

        var score = Math.Clamp(
            Math.Round(
                (0.55m * maxAsset)
                + (0.30m * topFiveAverage)
                + Math.Min(criticalAssetCount * 18m, 90m)
                + Math.Min(highAssetCount * 8m, 40m)
                + Math.Min(mediumAssetCount * 2m, 10m)
                + Math.Min(lowAssetCount * 0.5m, 5m),
                2),
            0m,
            1000m);

        return new TenantRiskResult(
            score,
            ordered.Count,
            criticalAssetCount,
            highAssetCount,
            ordered
        );
    }

    private async Task<List<AssetRiskResult>> CalculateAssetScoresAsync(Guid tenantId, CancellationToken ct)
    {
        var episodeScores = await dbContext.VulnerabilityEpisodeRiskAssessments
            .Where(item => item.TenantId == tenantId && item.ResolvedAt == null)
            .Select(item => new
            {
                item.AssetId,
                item.EpisodeRiskScore,
                item.RiskBand,
            })
            .ToListAsync(ct);

        return BuildAssetRiskResults(episodeScores.Select(item => (
            item.AssetId,
            item.EpisodeRiskScore,
            item.RiskBand
        )));
    }

    private static List<AssetRiskResult> BuildAssetRiskResults(
        IEnumerable<(Guid AssetId, decimal EpisodeRiskScore, string RiskBand)> episodeScores
    )
    {
        return episodeScores
            .GroupBy(item => item.AssetId)
            .Select(group =>
            {
                var orderedScores = group
                    .Select(item => item.EpisodeRiskScore)
                    .OrderByDescending(score => score)
                    .ToList();
                var maxEpisodeRisk = orderedScores.FirstOrDefault();
                var topThreeAverage = orderedScores.Take(3).DefaultIfEmpty(0m).Average();
                var criticalCount = group.Count(item => item.RiskBand == "Critical");
                var highCount = group.Count(item => item.RiskBand == "High");
                var mediumCount = group.Count(item => item.RiskBand == "Medium");
                var lowCount = group.Count(item => item.RiskBand == "Low");

                var overallScore = Math.Clamp(
                    Math.Round(
                        (0.7m * maxEpisodeRisk)
                        + (0.2m * topThreeAverage)
                        + Math.Min(criticalCount * 35m, 120m)
                        + Math.Min(highCount * 15m, 60m)
                        + Math.Min(mediumCount * 5m, 20m)
                        + Math.Min(lowCount * 1m, 5m),
                        2),
                    0m,
                    1000m);

                var factorsJson = JsonSerializer.Serialize(new List<RiskFactor>
                {
                    new("MaxEpisodeRisk", "Highest unresolved episode risk on the asset.", maxEpisodeRisk),
                    new("TopThreeAverage", "Average of the top three unresolved episode risks.", Math.Round(topThreeAverage, 2)),
                    new("CriticalEpisodes", $"{criticalCount} critical-risk episodes.", Math.Min(criticalCount * 35m, 120m)),
                    new("HighEpisodes", $"{highCount} high-risk episodes.", Math.Min(highCount * 15m, 60m)),
                    new("MediumEpisodes", $"{mediumCount} medium-risk episodes.", Math.Min(mediumCount * 5m, 20m)),
                    new("LowEpisodes", $"{lowCount} low-risk episodes.", Math.Min(lowCount * 1m, 5m)),
                });

                return new AssetRiskResult(
                    group.Key,
                    overallScore,
                    maxEpisodeRisk,
                    criticalCount,
                    highCount,
                    mediumCount,
                    lowCount,
                    group.Count(),
                    factorsJson
                );
            })
            .OrderByDescending(item => item.OverallScore)
            .ToList();
    }

    private async Task<List<SoftwareRiskResult>> CalculateSoftwareScoresAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        var activeSnapshotId = await dbContext.TenantSourceConfigurations
            .AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.SourceKey == TenantSourceCatalog.DefenderSourceKey
            )
            .Select(item => item.ActiveSnapshotId)
            .FirstOrDefaultAsync(ct);

        if (activeSnapshotId is null)
        {
            return [];
        }

        var episodeRows = await (
            from installation in dbContext.NormalizedSoftwareInstallations.AsNoTracking()
            join match in dbContext.SoftwareVulnerabilityMatches.AsNoTracking()
                on installation.SoftwareAssetId equals match.SoftwareAssetId
            join tenantVulnerability in dbContext.TenantVulnerabilities.AsNoTracking()
                on new { DefinitionId = match.VulnerabilityDefinitionId, TenantId = installation.TenantId }
                equals new { DefinitionId = tenantVulnerability.VulnerabilityDefinitionId, TenantId = tenantVulnerability.TenantId }
            join episodeRisk in dbContext.VulnerabilityEpisodeRiskAssessments.AsNoTracking()
                on new { AssetId = installation.DeviceAssetId, TenantVulnerabilityId = tenantVulnerability.Id }
                equals new { episodeRisk.AssetId, episodeRisk.TenantVulnerabilityId }
            where installation.TenantId == tenantId
                  && installation.SnapshotId == activeSnapshotId
                  && installation.IsActive
                  && match.SnapshotId == activeSnapshotId
                  && match.ResolvedAt == null
                  && episodeRisk.ResolvedAt == null
            select new
            {
                installation.TenantSoftwareId,
                installation.DeviceAssetId,
                SnapshotId = installation.SnapshotId,
                episodeRisk.EpisodeRiskScore,
                episodeRisk.RiskBand,
            }
        ).Distinct().ToListAsync(ct);

        return episodeRows
            .GroupBy(item => item.TenantSoftwareId)
            .Select(group =>
            {
                var orderedScores = group
                    .Select(item => item.EpisodeRiskScore)
                    .OrderByDescending(score => score)
                    .ToList();
                var maxEpisodeRisk = orderedScores.FirstOrDefault();
                var topThreeAverage = orderedScores.Take(3).DefaultIfEmpty(0m).Average();
                var criticalCount = group.Count(item => item.RiskBand == "Critical");
                var highCount = group.Count(item => item.RiskBand == "High");
                var mediumCount = group.Count(item => item.RiskBand == "Medium");
                var lowCount = group.Count(item => item.RiskBand == "Low");
                var affectedDeviceCount = group.Select(item => item.DeviceAssetId).Distinct().Count();

                var overallScore = Math.Clamp(
                    Math.Round(
                        (0.65m * maxEpisodeRisk)
                        + (0.20m * topThreeAverage)
                        + Math.Min(criticalCount * 30m, 120m)
                        + Math.Min(highCount * 12m, 48m)
                        + Math.Min(mediumCount * 4m, 16m)
                        + Math.Min(lowCount * 1m, 6m),
                        2),
                    0m,
                    1000m);

                var factorsJson = JsonSerializer.Serialize(new List<RiskFactor>
                {
                    new("MaxEpisodeRisk", "Highest unresolved episode risk linked to the software.", maxEpisodeRisk),
                    new("TopThreeAverage", "Average of the top three unresolved episode risks linked to the software.", Math.Round(topThreeAverage, 2)),
                    new("CriticalEpisodes", $"{criticalCount} critical-risk software-linked episodes.", Math.Min(criticalCount * 30m, 120m)),
                    new("HighEpisodes", $"{highCount} high-risk software-linked episodes.", Math.Min(highCount * 12m, 48m)),
                    new("MediumEpisodes", $"{mediumCount} medium-risk software-linked episodes.", Math.Min(mediumCount * 4m, 16m)),
                    new("LowEpisodes", $"{lowCount} low-risk software-linked episodes.", Math.Min(lowCount * 1m, 6m)),
                });

                return new SoftwareRiskResult(
                    group.Key,
                    group.Select(item => item.SnapshotId).FirstOrDefault(),
                    overallScore,
                    maxEpisodeRisk,
                    criticalCount,
                    highCount,
                    mediumCount,
                    lowCount,
                    affectedDeviceCount,
                    group.Count(),
                    factorsJson
                );
            })
            .OrderByDescending(item => item.OverallScore)
            .ToList();
    }

    private async Task<List<DeviceGroupRiskResult>> CalculateDeviceGroupScoresAsync(
        Guid tenantId,
        IReadOnlyList<AssetRiskResult> assetResults,
        CancellationToken ct
    )
    {
        var assets = await dbContext.Assets.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.AssetType == AssetType.Device)
            .Select(item => new
            {
                item.Id,
                item.DeviceGroupId,
                item.DeviceGroupName,
            })
            .ToListAsync(ct);

        return (
            from asset in assets
            join risk in assetResults on asset.Id equals risk.AssetId
            let groupName = string.IsNullOrWhiteSpace(asset.DeviceGroupName) ? "Ungrouped" : asset.DeviceGroupName!
            let groupKey = !string.IsNullOrWhiteSpace(asset.DeviceGroupId)
                ? $"id:{asset.DeviceGroupId}"
                : $"name:{groupName.Trim().ToLowerInvariant()}"
            group new { asset, risk } by new { groupKey, asset.DeviceGroupId, groupName } into grouped
            let orderedScores = grouped.Select(item => item.risk.OverallScore).OrderByDescending(score => score).ToList()
            let maxAssetRisk = orderedScores.FirstOrDefault()
            let topThreeAverage = orderedScores.Take(3).DefaultIfEmpty(0m).Average()
            let criticalCount = grouped.Sum(item => item.risk.CriticalCount)
            let highCount = grouped.Sum(item => item.risk.HighCount)
            let mediumCount = grouped.Sum(item => item.risk.MediumCount)
            let lowCount = grouped.Sum(item => item.risk.LowCount)
            let openEpisodeCount = grouped.Sum(item => item.risk.OpenEpisodeCount)
            let overallScore = Math.Clamp(
                Math.Round(
                    (0.55m * maxAssetRisk)
                    + (0.25m * topThreeAverage)
                    + Math.Min(criticalCount * 8m, 120m)
                    + Math.Min(highCount * 3m, 60m)
                    + Math.Min(mediumCount * 1m, 20m)
                    + Math.Min(lowCount * 0.25m, 8m),
                    2),
                0m,
                1000m)
            select new DeviceGroupRiskResult(
                grouped.Key.groupKey,
                grouped.Key.DeviceGroupId,
                grouped.Key.groupName,
                overallScore,
                maxAssetRisk,
                criticalCount,
                highCount,
                mediumCount,
                lowCount,
                grouped.Count(),
                openEpisodeCount,
                JsonSerializer.Serialize(new List<RiskFactor>
                {
                    new("MaxAssetRisk", "Highest current asset risk inside the device group.", maxAssetRisk),
                    new("TopThreeAverage", "Average of the top three asset risk scores in the device group.", Math.Round(topThreeAverage, 2)),
                    new("CriticalEpisodes", $"{criticalCount} critical-risk episodes in the device group.", Math.Min(criticalCount * 8m, 120m)),
                    new("HighEpisodes", $"{highCount} high-risk episodes in the device group.", Math.Min(highCount * 3m, 60m)),
                    new("MediumEpisodes", $"{mediumCount} medium-risk episodes in the device group.", Math.Min(mediumCount * 1m, 20m)),
                    new("LowEpisodes", $"{lowCount} low-risk episodes in the device group.", Math.Min(lowCount * 0.25m, 8m)),
                })
            )
        )
            .OrderByDescending(item => item.OverallScore)
            .ToList();
    }

    private async Task<List<TeamRiskResult>> CalculateTeamScoresAsync(
        Guid tenantId,
        IReadOnlyList<AssetRiskResult> assetResults,
        CancellationToken ct
    )
    {
        var assets = await dbContext.Assets.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Select(item => new
            {
                item.Id,
                EffectiveTeamId = item.OwnerTeamId ?? item.FallbackTeamId,
            })
            .Where(item => item.EffectiveTeamId != null)
            .ToListAsync(ct);

        return (
            from asset in assets
            join risk in assetResults on asset.Id equals risk.AssetId
            group risk by asset.EffectiveTeamId!.Value into grouped
            let orderedScores = grouped.Select(item => item.OverallScore).OrderByDescending(score => score).ToList()
            let maxAssetRisk = orderedScores.FirstOrDefault()
            let topThreeAverage = orderedScores.Take(3).DefaultIfEmpty(0m).Average()
            let criticalCount = grouped.Sum(item => item.CriticalCount)
            let highCount = grouped.Sum(item => item.HighCount)
            let mediumCount = grouped.Sum(item => item.MediumCount)
            let lowCount = grouped.Sum(item => item.LowCount)
            let openEpisodeCount = grouped.Sum(item => item.OpenEpisodeCount)
            let overallScore = Math.Clamp(
                Math.Round(
                    (0.60m * maxAssetRisk)
                    + (0.25m * topThreeAverage)
                    + Math.Min(criticalCount * 10m, 150m)
                    + Math.Min(highCount * 4m, 72m)
                    + Math.Min(mediumCount * 1m, 20m)
                    + Math.Min(lowCount * 0.25m, 8m),
                    2),
                0m,
                1000m)
            select new TeamRiskResult(
                grouped.Key,
                overallScore,
                maxAssetRisk,
                criticalCount,
                highCount,
                mediumCount,
                lowCount,
                grouped.Count(),
                openEpisodeCount,
                JsonSerializer.Serialize(new List<RiskFactor>
                {
                    new("MaxAssetRisk", "Highest current asset risk owned by the team.", maxAssetRisk),
                    new("TopThreeAverage", "Average of the top three asset risk scores owned by the team.", Math.Round(topThreeAverage, 2)),
                    new("CriticalEpisodes", $"{criticalCount} critical-risk episodes owned by the team.", Math.Min(criticalCount * 10m, 150m)),
                    new("HighEpisodes", $"{highCount} high-risk episodes owned by the team.", Math.Min(highCount * 4m, 72m)),
                    new("MediumEpisodes", $"{mediumCount} medium-risk episodes owned by the team.", Math.Min(mediumCount * 1m, 20m)),
                    new("LowEpisodes", $"{lowCount} low-risk episodes owned by the team.", Math.Min(lowCount * 0.25m, 8m)),
                })
            )
        )
            .OrderByDescending(item => item.OverallScore)
            .ToList();
    }

    private async Task RecordDailySnapshotAsync(
        Guid tenantId,
        IReadOnlyList<AssetRiskResult> assetScores,
        CancellationToken ct
    )
    {
        var tenantRisk = CalculateTenantRisk(assetScores);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var existing = await dbContext.TenantRiskScoreSnapshots
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Date == today, ct);

        if (existing is null)
        {
            dbContext.TenantRiskScoreSnapshots.Add(
                TenantRiskScoreSnapshot.Create(
                    tenantId,
                    today,
                    tenantRisk.OverallScore,
                    tenantRisk.AssetCount,
                    tenantRisk.CriticalAssetCount,
                    tenantRisk.HighAssetCount
                )
            );
        }
        else
        {
            existing.Update(
                tenantRisk.OverallScore,
                tenantRisk.AssetCount,
                tenantRisk.CriticalAssetCount,
                tenantRisk.HighAssetCount
            );
        }

        var cutoff = today.AddDays(-30);
        var stale = await dbContext.TenantRiskScoreSnapshots
            .Where(item => item.TenantId == tenantId && item.Date < cutoff)
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            dbContext.TenantRiskScoreSnapshots.RemoveRange(stale);
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
