using System.Text.Json;
using PatchHound.Core.Enums;

namespace PatchHound.Core.Services.RiskScoring;

public static class PatchHoundRiskScoringEngine
{
    public static RiskScoreResult CalculateSoftwareRisk(
        IReadOnlyCollection<RiskExposureInput> exposures,
        int affectedDeviceCount,
        int highValueDeviceCount)
    {
        var metrics = CalculateExposureMetrics(exposures);
        if (metrics.ExposureCount == 0)
        {
            return BuildResult(0m, metrics, CreateFactors(("BaseComponent", "No exposure detections.", 0m)));
        }

        var baseComponent = metrics.MaxDetectionScore * 6.5m;
        var countComponent = CalculateSoftwareCountComponent(metrics);
        var breadthComponent = affectedDeviceCount <= 1
            ? 0m
            : Math.Min((decimal)Math.Log10(affectedDeviceCount) * 80m, 180m);
        var highValueComponent = Math.Max(highValueDeviceCount, 0) * 25m;

        var numericScore = baseComponent
            + countComponent
            + breadthComponent
            + highValueComponent;
        var score = ApplyFloors(numericScore, metrics);

        var factorsJson = CreateFactors(
            ("BaseComponent", "Highest detection score scaled to the PatchHound 0-1000 score range.", baseComponent),
            ("CountComponent", "Severity count pressure from critical, high, medium, and low detections.", countComponent),
            ("BreadthComponent", "Logarithmic affected-device breadth contribution.", breadthComponent),
            ("HighValueDeviceComponent", "Additional contribution from high-value affected devices.", highValueComponent),
            ("FloorAdjustedScore", "Score after mandatory threat and severity floors.", score));

        return BuildResult(score, metrics, factorsJson);
    }

    public static RiskScoreResult CalculateAssetRisk(
        IReadOnlyCollection<RiskExposureInput> exposures,
        decimal businessLabelWeight)
    {
        var metrics = CalculateExposureMetrics(exposures);
        if (metrics.ExposureCount == 0)
        {
            return BuildResult(0m, metrics, CreateFactors(("BaseComponent", "No exposure detections.", 0m)));
        }

        var baseComponent = metrics.MaxDetectionScore * 6.5m;
        var countComponent = CalculateAssetCountComponent(metrics);
        var criticalityMultiplier = exposures.Max(exposure => GetAssetCriticalityMultiplier(exposure.AssetCriticality));
        var businessMultiplier = Math.Max(businessLabelWeight, 0m);
        var rawImpactMultiplier = criticalityMultiplier * businessMultiplier;
        var impactMultiplier = CompressAssetImpactMultiplier(rawImpactMultiplier);
        var numericScore = (baseComponent + countComponent) * impactMultiplier;
        var score = ApplyFloors(numericScore, metrics);

        var factorsJson = CreateFactors(
            ("BaseComponent", "Highest detection score scaled to the PatchHound 0-1000 score range.", baseComponent),
            ("CountComponent", "Diminishing severity count pressure from critical, high, medium, and low detections.", countComponent),
            ("AssetCriticalityMultiplier", "Asset criticality impact multiplier.", criticalityMultiplier),
            ("BusinessLabelWeight", "Business-label impact multiplier.", businessMultiplier),
            ("EffectiveImpactMultiplier", "Compressed asset impact multiplier applied to sparse device exposure risk.", impactMultiplier),
            ("FloorAdjustedScore", "Score after mandatory threat and severity floors.", score));

        return BuildResult(score, metrics, factorsJson);
    }

    private static ExposureMetrics CalculateExposureMetrics(IReadOnlyCollection<RiskExposureInput> exposures)
    {
        var maxDetectionScore = 0m;
        var criticalCount = 0;
        var highCount = 0;
        var mediumCount = 0;
        var lowCount = 0;
        var hasUrgentThreat = false;

        foreach (var exposure in exposures)
        {
            maxDetectionScore = Math.Max(maxDetectionScore, CalculateDetectionScore(exposure));
            hasUrgentThreat |= IsUrgentThreat(exposure);

            switch (exposure.VendorSeverity)
            {
                case Severity.Critical:
                    criticalCount++;
                    break;
                case Severity.High:
                    highCount++;
                    break;
                case Severity.Medium:
                    mediumCount++;
                    break;
                case Severity.Low:
                    lowCount++;
                    break;
            }
        }

        return new ExposureMetrics(
            exposures.Count,
            maxDetectionScore,
            criticalCount,
            highCount,
            mediumCount,
            lowCount,
            hasUrgentThreat);
    }

    private static decimal CalculateDetectionScore(RiskExposureInput exposure)
    {
        var score = Math.Max(ClampPercent(exposure.EnvironmentalCvss * 10m), SeverityFallback(exposure.VendorSeverity));

        if (exposure.ThreatScore.HasValue)
        {
            score = Math.Max(score, ClampPercent(exposure.ThreatScore.Value));
        }

        if (IsUrgentThreat(exposure))
        {
            score = Math.Max(score, 95m);
        }

        if (exposure.PublicExploit || exposure.EpssScore >= 0.50m)
        {
            score = Math.Max(score, 80m);
        }

        return score;
    }

    private static decimal CalculateSoftwareCountComponent(ExposureMetrics metrics) =>
        metrics.CriticalCount * 45m
        + metrics.HighCount * 12m
        + metrics.MediumCount * 3m
        + metrics.LowCount;

    private static decimal CalculateAssetCountComponent(ExposureMetrics metrics) =>
        Math.Min(metrics.CriticalCount * 15m, 60m)
        + Math.Min(metrics.HighCount * 3m, 60m)
        + Math.Min(metrics.MediumCount, 20m)
        + Math.Min(metrics.LowCount * 0.25m, 8m);

    private static decimal ApplyFloors(decimal numericScore, ExposureMetrics metrics)
    {
        var score = numericScore;

        if (metrics.HasUrgentThreat)
        {
            score = Math.Max(score, RiskBand.HighThreshold);
        }

        if (metrics.CriticalCount > 0)
        {
            score = Math.Max(score, RiskBand.MediumThreshold);
        }

        if (metrics.HighCount >= 10)
        {
            score = Math.Max(score, RiskBand.MediumThreshold);
        }

        return ClampScore(score);
    }

    private static bool IsUrgentThreat(RiskExposureInput exposure) =>
        exposure.IsEmergencyPatchRecommended
        || exposure.KnownExploited
        || exposure.ActiveAlert
        || exposure.HasRansomwareAssociation
        || exposure.HasMalwareAssociation;

    private static decimal SeverityFallback(Severity severity) => severity switch
    {
        Severity.Critical => 90m,
        Severity.High => 70m,
        Severity.Medium => 40m,
        Severity.Low => 10m,
        _ => 0m,
    };

    private static decimal GetAssetCriticalityMultiplier(Criticality criticality) => criticality switch
    {
        Criticality.Critical => 1.35m,
        Criticality.High => 1.20m,
        Criticality.Medium => 1.00m,
        Criticality.Low => 0.85m,
        _ => 1.00m,
    };

    private static decimal CompressAssetImpactMultiplier(decimal multiplier)
    {
        if (multiplier <= 1m)
        {
            return multiplier;
        }

        return Math.Min(1m + ((multiplier - 1m) * 0.35m), 1.6m);
    }

    private static decimal ClampPercent(decimal score) => Math.Clamp(score, 0m, 100m);

    private static decimal ClampScore(decimal score) => Math.Clamp(Math.Round(score, 1), 0m, 1000m);

    private static RiskScoreResult BuildResult(decimal score, ExposureMetrics metrics, string factorsJson) =>
        new(
            score,
            Math.Round(metrics.MaxDetectionScore, 1),
            metrics.CriticalCount,
            metrics.HighCount,
            metrics.MediumCount,
            metrics.LowCount,
            RiskBand.FromScore(score),
            factorsJson);

    private static string CreateFactors(params (string Name, string Description, decimal Impact)[] factors) =>
        JsonSerializer.Serialize(
            factors.Select(factor => new RiskContributionFactor(
                factor.Name,
                factor.Description,
                Math.Round(factor.Impact, 3))));

    private sealed record ExposureMetrics(
        int ExposureCount,
        decimal MaxDetectionScore,
        int CriticalCount,
        int HighCount,
        int MediumCount,
        int LowCount,
        bool HasUrgentThreat);
}
