using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

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
        Guid SoftwareProductId,
        decimal OverallScore,
        decimal MaxExposureScore,
        int CriticalExposureCount,
        int HighExposureCount,
        int MediumExposureCount,
        int LowExposureCount,
        int AffectedDeviceCount,
        int OpenExposureCount,
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
        var existingScores = await dbContext.DeviceRiskScores
            .Where(item => item.TenantId == tenantId)
            .ToDictionaryAsync(item => item.DeviceId, ct);
        var existingDeviceGroupScores = await dbContext.DeviceGroupRiskScores
            .Where(item => item.TenantId == tenantId)
            .ToDictionaryAsync(item => item.GroupKey, ct);
        var existingSoftwareScores = await dbContext.SoftwareRiskScores
            .Where(item => item.TenantId == tenantId)
            .ToDictionaryAsync(item => item.SoftwareProductId, ct);
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
                dbContext.DeviceRiskScores.Add(
                    DeviceRiskScore.Create(
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
            .Where(item => !activeAssetIds.Contains(item.DeviceId))
            .ToList();
        if (staleScores.Count > 0)
        {
            dbContext.DeviceRiskScores.RemoveRange(staleScores);
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
            if (existingSoftwareScores.TryGetValue(result.SoftwareProductId, out var existing))
            {
                existing.Update(
                    result.OverallScore,
                    result.MaxExposureScore,
                    result.CriticalExposureCount,
                    result.HighExposureCount,
                    result.MediumExposureCount,
                    result.LowExposureCount,
                    result.AffectedDeviceCount,
                    result.OpenExposureCount,
                    result.FactorsJson,
                    CalculationVersion
                );
            }
            else
            {
                dbContext.SoftwareRiskScores.Add(
                    SoftwareRiskScore.Create(
                        tenantId,
                        result.SoftwareProductId,
                        result.OverallScore,
                        result.MaxExposureScore,
                        result.CriticalExposureCount,
                        result.HighExposureCount,
                        result.MediumExposureCount,
                        result.LowExposureCount,
                        result.AffectedDeviceCount,
                        result.OpenExposureCount,
                        result.FactorsJson,
                        CalculationVersion
                    )
                );
            }
        }

        var activeSoftwareProductIds = softwareResults.Select(item => item.SoftwareProductId).ToHashSet();
        var staleSoftwareScores = existingSoftwareScores.Values
            .Where(item => !activeSoftwareProductIds.Contains(item.SoftwareProductId))
            .ToList();
        if (staleSoftwareScores.Count > 0)
        {
            dbContext.SoftwareRiskScores.RemoveRange(staleSoftwareScores);
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
        var deviceScores = await dbContext.DeviceRiskScores
            .Where(item => item.TenantId == tenantId)
            .OrderByDescending(item => item.OverallScore)
            .ToListAsync(ct);

        return CalculateTenantRisk(deviceScores.Select(item => new AssetRiskResult(
            item.DeviceId,
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
        var query = dbContext.DeviceRiskScores.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Join(
                dbContext.Devices.AsNoTracking().Where(item => item.TenantId == tenantId),
                score => score.DeviceId,
                device => device.Id,
                (score, device) => new { score, device }
            );

        if (minAgeDays.HasValue)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-minAgeDays.Value);
            query = query.Where(item => item.device.LastSeenAt == null || item.device.LastSeenAt <= cutoff);
        }

        if (!string.IsNullOrWhiteSpace(platform))
        {
            query = query.Where(item => item.device.OsPlatform != null && item.device.OsPlatform.Contains(platform));
        }

        if (!string.IsNullOrWhiteSpace(deviceGroup))
        {
            query = query.Where(item => item.device.GroupName != null && item.device.GroupName == deviceGroup);
        }

        var filtered = await query
            .OrderByDescending(item => item.score.OverallScore)
            .Select(item => new AssetRiskResult(
                item.score.DeviceId,
                item.score.OverallScore,
                item.score.MaxEpisodeRiskScore,
                item.score.CriticalCount,
                item.score.HighCount,
                item.score.MediumCount,
                item.score.LowCount,
                item.score.OpenEpisodeCount,
                item.score.FactorsJson
            ))
            .ToListAsync(ct);

        return CalculateTenantRisk(filtered);
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
        var episodeScores = await dbContext.ExposureAssessments.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Select(item => new
            {
                AssetId = item.Exposure.DeviceId,
                EpisodeRiskScore = item.EnvironmentalCvss,
                RiskBand = item.EnvironmentalCvss >= 9.0m
                    ? "Critical"
                    : item.EnvironmentalCvss >= 7.0m
                        ? "High"
                        : item.EnvironmentalCvss >= 4.0m
                            ? "Medium"
                            : "Low",
            })
            .ToListAsync(ct);

        return BuildAssetRiskResults(
            episodeScores.Select(item => (item.AssetId, item.EpisodeRiskScore, item.RiskBand))
        );
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
        var exposures = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.SoftwareProductId != null)
            .Select(item => new
            {
                item.Id,
                item.DeviceId,
                SoftwareProductId = item.SoftwareProductId!.Value,
                item.Status,
                Score = dbContext.ExposureAssessments
                    .Where(a => a.DeviceVulnerabilityExposureId == item.Id)
                    .Select(a => (decimal?)a.EnvironmentalCvss)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return exposures
            .GroupBy(item => item.SoftwareProductId)
            .Select(group =>
            {
                var openExposures = group.Where(e => e.Status == Core.Enums.ExposureStatus.Open).ToList();
                var scores = openExposures
                    .Select(e => e.Score ?? 0m)
                    .OrderByDescending(s => s)
                    .ToList();
                var maxScore = scores.FirstOrDefault();
                var topThreeAvg = scores.Take(3).DefaultIfEmpty(0m).Average();
                var criticalCount = openExposures.Count(e => (e.Score ?? 0m) >= 9.0m);
                var highCount = openExposures.Count(e => (e.Score ?? 0m) >= 7.0m && (e.Score ?? 0m) < 9.0m);
                var mediumCount = openExposures.Count(e => (e.Score ?? 0m) >= 4.0m && (e.Score ?? 0m) < 7.0m);
                var lowCount = openExposures.Count(e => (e.Score ?? 0m) > 0m && (e.Score ?? 0m) < 4.0m);
                var affectedDeviceCount = openExposures.Select(e => e.DeviceId).Distinct().Count();

                var overallScore = Math.Clamp(
                    Math.Round(
                        (0.7m * maxScore)
                        + (0.2m * topThreeAvg)
                        + Math.Min(criticalCount * 35m, 120m)
                        + Math.Min(highCount * 15m, 60m)
                        + Math.Min(mediumCount * 5m, 20m)
                        + Math.Min(lowCount * 1m, 5m),
                        2),
                    0m,
                    1000m);

                var factorsJson = JsonSerializer.Serialize(new List<RiskFactor>
                {
                    new("MaxExposureScore", "Highest open exposure score for this software product.", maxScore),
                    new("TopThreeAverage", "Average of the top three open exposure scores.", Math.Round(topThreeAvg, 2)),
                    new("CriticalExposures", $"{criticalCount} critical-risk open exposures.", Math.Min(criticalCount * 35m, 120m)),
                    new("HighExposures", $"{highCount} high-risk open exposures.", Math.Min(highCount * 15m, 60m)),
                    new("MediumExposures", $"{mediumCount} medium-risk open exposures.", Math.Min(mediumCount * 5m, 20m)),
                    new("LowExposures", $"{lowCount} low-risk open exposures.", Math.Min(lowCount * 1m, 5m)),
                });

                return new SoftwareRiskResult(
                    group.Key,
                    overallScore,
                    maxScore,
                    criticalCount,
                    highCount,
                    mediumCount,
                    lowCount,
                    affectedDeviceCount,
                    openExposures.Count,
                    factorsJson
                );
            })
            .Where(r => r.OpenExposureCount > 0)
            .OrderByDescending(r => r.OverallScore)
            .ToList();
    }

    private async Task<List<DeviceGroupRiskResult>> CalculateDeviceGroupScoresAsync(
        Guid tenantId,
        IReadOnlyList<AssetRiskResult> assetResults,
        CancellationToken ct
    )
    {
        var devices = await dbContext.Devices.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Select(item => new
            {
                item.Id,
                DeviceGroupId = item.GroupId,
                DeviceGroupName = item.GroupName,
            })
            .ToListAsync(ct);

        return (
            from device in devices
            join risk in assetResults on device.Id equals risk.AssetId
            let groupName = string.IsNullOrWhiteSpace(device.DeviceGroupName) ? "Ungrouped" : device.DeviceGroupName!
            let groupKey = !string.IsNullOrWhiteSpace(device.DeviceGroupId)
                ? $"id:{device.DeviceGroupId}"
                : $"name:{groupName.Trim().ToLowerInvariant()}"
            group new { device, risk } by new { groupKey, device.DeviceGroupId, groupName } into grouped
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
        var devices = await dbContext.Devices.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Select(item => new
            {
                item.Id,
                EffectiveTeamId = item.OwnerTeamId ?? item.FallbackTeamId,
            })
            .Where(item => item.EffectiveTeamId != null)
            .ToListAsync(ct);

        return (
            from device in devices
            join risk in assetResults on device.Id equals risk.AssetId
            group risk by device.EffectiveTeamId!.Value into grouped
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
