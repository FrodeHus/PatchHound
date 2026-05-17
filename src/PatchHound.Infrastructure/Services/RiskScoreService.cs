using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Constants;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services.RiskScoring;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RiskScoreService(
    PatchHoundDbContext dbContext,
    ILogger<RiskScoreService> logger
)
{
    public const string CalculationVersion = "2-trurisk-inspired";

    /// <summary>
    /// Multiplier applied to an exposure's environmental CVSS when an active, approved
    /// patching decision reduces its risk within the maintenance window.
    /// A value of 0.5 represents a 50 % score reduction.
    /// </summary>
    public const decimal RemediationAdjustmentFactor = 0.5m;

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
        var criticalAssetCount = ordered.Count(item => item.OverallScore >= RiskBand.CriticalThreshold);
        var highAssetCount = ordered.Count(item => item.OverallScore >= RiskBand.HighThreshold && item.OverallScore < RiskBand.CriticalThreshold);
        var mediumAssetCount = ordered.Count(item => item.OverallScore >= RiskBand.MediumThreshold && item.OverallScore < RiskBand.HighThreshold);
        var lowAssetCount = ordered.Count(item => item.OverallScore > 0m && item.OverallScore < RiskBand.MediumThreshold);

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
        var now = DateTimeOffset.UtcNow;

        // Load active (Approved) remediation decisions for this tenant and join to the
        // software products they cover.  We materialise only the columns we need.
        var activeDecisions = await dbContext.RemediationDecisions.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ApprovalStatus == DecisionApprovalStatus.Approved)
            .Select(d => new
            {
                SoftwareProductId = d.RemediationCase.SoftwareProductId,
                d.Outcome,
                d.MaintenanceWindowDate,
            })
            .ToListAsync(ct);

        // Build a set of software product ids for which remediation actively lowers risk.
        // Rules:
        //   ApprovedForPatching  → reduces only when maintenance window has NOT been missed
        //   RiskAcceptance       → no change (visibility only)
        //   AlternateMitigation  → considered fixed via approved vulnerability remediation coverage
        //   PatchingDeferred     → no change (administrative only)
        var reducedSoftwareProductIds = activeDecisions
            .Where(d =>
                d.Outcome == RemediationOutcome.ApprovedForPatching
                && !(d.MaintenanceWindowDate.HasValue && d.MaintenanceWindowDate.Value < now))
            .Select(d => d.SoftwareProductId)
            .ToHashSet();

        var mitigatedVulnerabilityIds = MitigatedVulnerabilityIds(tenantId);

        var exposureRows = await (
            from item in dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            join assessment in dbContext.ExposureLatestAssessments
                on item.Id equals assessment.DeviceVulnerabilityExposureId into assessmentJoin
            from assessment in assessmentJoin.DefaultIfEmpty()
            join mitigatedVulnerabilityId in mitigatedVulnerabilityIds
                on (Guid?)item.VulnerabilityId equals mitigatedVulnerabilityId into mitigatedJoin
            from mitigatedVulnerabilityId in mitigatedJoin.DefaultIfEmpty()
            where item.TenantId == tenantId
                && item.Status == ExposureStatus.Open
                && mitigatedVulnerabilityId == null
            select new
            {
                item.DeviceId,
                item.VulnerabilityId,
                item.SoftwareProductId,
                DeviceCriticality = item.Device.Criticality,
                VendorSeverity = item.Vulnerability.VendorSeverity,
                VulnerabilityCvss = item.Vulnerability.CvssScore,
                AssessmentScore = (decimal?)assessment.EnvironmentalCvss,
            }
        ).ToListAsync(ct);

        var vulnerabilityIds = exposureRows.Select(item => item.VulnerabilityId).Distinct().ToList();
        var threatAssessments = await LoadThreatAssessmentsAsync(vulnerabilityIds, ct);
        var emergencyPatchVulnerabilityIds = await LoadEmergencyPatchVulnerabilityIdsAsync(vulnerabilityIds, ct);

        // Load the highest active business-label weight per device. The CASE expression
        // translates to SQL so aggregation happens server-side — only one row per device
        // returns even when many labels are assigned.
        var deviceLabelWeights = await dbContext.DeviceBusinessLabels.AsNoTracking()
            .Where(dbl => dbl.TenantId == tenantId && dbl.BusinessLabel.IsActive)
            .GroupBy(dbl => dbl.DeviceId)
            .Select(g => new
            {
                DeviceId = g.Key,
                MaxWeight = g.Max(dbl =>
                    dbl.BusinessLabel.WeightCategory == BusinessLabelWeightCategory.Critical ? 2.0m
                    : dbl.BusinessLabel.WeightCategory == BusinessLabelWeightCategory.Sensitive ? 1.5m
                    : dbl.BusinessLabel.WeightCategory == BusinessLabelWeightCategory.Informational ? 0.5m
                    : 1.0m),
            })
            .ToDictionaryAsync(item => item.DeviceId, item => item.MaxWeight, ct);

        return exposureRows
            .Select(item => BuildExposureInput(
                item.DeviceId,
                item.VulnerabilityId,
                item.SoftwareProductId,
                item.AssessmentScore ?? item.VulnerabilityCvss ?? 0m,
                item.VendorSeverity,
                item.DeviceCriticality,
                threatAssessments,
                emergencyPatchVulnerabilityIds,
                reducedSoftwareProductIds))
            .GroupBy(item => item.AssetId)
            .Select(group =>
            {
                var labelWeight = deviceLabelWeights.GetValueOrDefault(group.Key, 1.0m);
                var result = PatchHoundRiskScoringEngine.CalculateAssetRisk(
                    group.Select(item => item.Input).ToList(),
                    labelWeight);

                return new AssetRiskResult(
                    group.Key,
                    result.OverallScore,
                    ScaleDetectionScoreToComposite(result.MaxDetectionScore),
                    result.CriticalCount,
                    result.HighCount,
                    result.MediumCount,
                    result.LowCount,
                    group.Count(),
                    result.FactorsJson
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
        var now = DateTimeOffset.UtcNow;
        var activeDecisions = await dbContext.RemediationDecisions.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ApprovalStatus == DecisionApprovalStatus.Approved)
            .Select(d => new
            {
                SoftwareProductId = d.RemediationCase.SoftwareProductId,
                d.Outcome,
                d.MaintenanceWindowDate,
            })
            .ToListAsync(ct);
        var reducedSoftwareProductIds = activeDecisions
            .Where(d =>
                d.Outcome == RemediationOutcome.ApprovedForPatching
                && !(d.MaintenanceWindowDate.HasValue && d.MaintenanceWindowDate.Value < now))
            .Select(d => d.SoftwareProductId)
            .ToHashSet();

        var mitigatedVulnerabilityIds = MitigatedVulnerabilityIds(tenantId);

        var exposures = await (
            from item in dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            join assessment in dbContext.ExposureLatestAssessments
                on item.Id equals assessment.DeviceVulnerabilityExposureId into assessmentJoin
            from assessment in assessmentJoin.DefaultIfEmpty()
            join mitigatedVulnerabilityId in mitigatedVulnerabilityIds
                on (Guid?)item.VulnerabilityId equals mitigatedVulnerabilityId into mitigatedJoin
            from mitigatedVulnerabilityId in mitigatedJoin.DefaultIfEmpty()
            where item.TenantId == tenantId
                && item.SoftwareProductId != null
                && item.Status == ExposureStatus.Open
                && mitigatedVulnerabilityId == null
            select new
            {
                item.Id,
                item.DeviceId,
                SoftwareProductId = item.SoftwareProductId!.Value,
                item.VulnerabilityId,
                DeviceCriticality = item.Device.Criticality,
                VendorSeverity = item.Vulnerability.VendorSeverity,
                VulnerabilityCvss = item.Vulnerability.CvssScore,
                AssessmentScore = (decimal?)assessment.EnvironmentalCvss,
            }
        ).ToListAsync(ct);

        var vulnerabilityIds = exposures.Select(item => item.VulnerabilityId).Distinct().ToList();
        var threatAssessments = await LoadThreatAssessmentsAsync(vulnerabilityIds, ct);
        var emergencyPatchVulnerabilityIds = await LoadEmergencyPatchVulnerabilityIdsAsync(vulnerabilityIds, ct);

        return exposures
            .GroupBy(item => item.SoftwareProductId)
            .Select(group =>
            {
                var openExposures = group.ToList();
                var inputs = openExposures
                    .Select(item => BuildExposureInput(
                        item.DeviceId,
                        item.VulnerabilityId,
                        item.SoftwareProductId,
                        item.AssessmentScore ?? item.VulnerabilityCvss ?? 0m,
                        item.VendorSeverity,
                        item.DeviceCriticality,
                        threatAssessments,
                        emergencyPatchVulnerabilityIds,
                        reducedSoftwareProductIds).Input)
                    .ToList();
                var affectedDeviceIds = openExposures.Select(item => item.DeviceId).Distinct().ToList();
                var affectedDeviceCount = affectedDeviceIds.Count;
                var highValueDeviceCount = openExposures
                    .GroupBy(item => item.DeviceId)
                    .Count(deviceGroup =>
                    {
                        var criticality = deviceGroup.First().DeviceCriticality;
                        return criticality == Criticality.High || criticality == Criticality.Critical;
                    });
                var result = PatchHoundRiskScoringEngine.CalculateSoftwareRisk(
                    inputs,
                    affectedDeviceCount,
                    highValueDeviceCount);

                return new SoftwareRiskResult(
                    group.Key,
                    result.OverallScore,
                    ScaleDetectionScoreToComposite(result.MaxDetectionScore),
                    result.CriticalCount,
                    result.HighCount,
                    result.MediumCount,
                    result.LowCount,
                    affectedDeviceCount,
                    openExposures.Count,
                    result.FactorsJson
                );
            })
            .Where(r => r.OpenExposureCount > 0)
            .OrderByDescending(r => r.OverallScore)
            .ToList();
    }

    private async Task<Dictionary<Guid, ThreatAssessment>> LoadThreatAssessmentsAsync(
        IReadOnlyCollection<Guid> vulnerabilityIds,
        CancellationToken ct)
    {
        if (vulnerabilityIds.Count == 0)
        {
            return [];
        }

        return await dbContext.ThreatAssessments.AsNoTracking()
            .Where(item => vulnerabilityIds.Contains(item.VulnerabilityId))
            .ToDictionaryAsync(item => item.VulnerabilityId, ct);
    }

    private async Task<HashSet<Guid>> LoadEmergencyPatchVulnerabilityIdsAsync(
        IReadOnlyCollection<Guid> vulnerabilityIds,
        CancellationToken ct)
    {
        if (vulnerabilityIds.Count == 0)
        {
            return [];
        }

        var ids = await dbContext.VulnerabilityPatchAssessments.AsNoTracking()
            .Where(item => vulnerabilityIds.Contains(item.VulnerabilityId)
                && item.UrgencyTier == PatchUrgencyTier.Emergency)
            .Select(item => item.VulnerabilityId)
            .Distinct()
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    private static (Guid AssetId, RiskExposureInput Input) BuildExposureInput(
        Guid deviceId,
        Guid vulnerabilityId,
        Guid? softwareProductId,
        decimal environmentalCvss,
        Severity vendorSeverity,
        Criticality deviceCriticality,
        IReadOnlyDictionary<Guid, ThreatAssessment> threatAssessments,
        IReadOnlySet<Guid> emergencyPatchVulnerabilityIds,
        IReadOnlySet<Guid> reducedSoftwareProductIds)
    {
        var adjustedCvss = environmentalCvss;
        if (softwareProductId.HasValue && reducedSoftwareProductIds.Contains(softwareProductId.Value))
        {
            adjustedCvss = Math.Round(adjustedCvss * RemediationAdjustmentFactor, 2);
        }

        threatAssessments.TryGetValue(vulnerabilityId, out var threat);

        return (
            deviceId,
            new RiskExposureInput(
                deviceId,
                vulnerabilityId,
                adjustedCvss,
                vendorSeverity,
                deviceCriticality,
                threat?.ThreatScore,
                threat?.EpssScore,
                threat?.KnownExploited ?? false,
                threat?.PublicExploit ?? false,
                threat?.ActiveAlert ?? false,
                threat?.HasRansomwareAssociation ?? false,
                threat?.HasMalwareAssociation ?? false,
                emergencyPatchVulnerabilityIds.Contains(vulnerabilityId))
        );
    }

    private static decimal ScaleDetectionScoreToComposite(decimal detectionScore) =>
        Math.Clamp(Math.Round(detectionScore * 10m, 1), 0m, 1000m);

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

    private IQueryable<Guid?> MitigatedVulnerabilityIds(Guid tenantId)
    {
        if (dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return dbContext.ApprovedVulnerabilityRemediations.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.Outcome == RemediationOutcome.AlternateMitigation)
                .Select(item => (Guid?)item.VulnerabilityId);
        }

        return dbContext.AlternateMitigationVulnIds.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Select(item => (Guid?)item.VulnerabilityId);
    }
}
