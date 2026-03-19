using System.Text.Json;
using PatchHound.Core.Enums;

namespace PatchHound.Core.Services;

/// <summary>
/// Pure calculation logic for the Secure Score.
/// No I/O — all data is passed in.
/// </summary>
public static class SecureScoreCalculator
{
    public const string CalculationVersion = "1.0";

    /// <summary>Input data for a single vulnerability-asset assessment.</summary>
    public record VulnerabilityInput(
        Severity EffectiveSeverity,
        decimal? EffectiveScore,
        bool IsOverdue
    );

    /// <summary>Input describing a single asset for scoring.</summary>
    public record AssetInput(
        Guid AssetId,
        string? DeviceValue,
        bool HasSecurityProfile,
        IReadOnlyList<VulnerabilityInput> Vulnerabilities
    );

    /// <summary>Output for one asset's score breakdown.</summary>
    public record AssetScoreResult(
        Guid AssetId,
        decimal OverallScore,
        decimal VulnerabilityScore,
        decimal ConfigurationScore,
        decimal DeviceValueWeight,
        int ActiveVulnerabilityCount,
        List<ScoreFactor> Factors
    );

    /// <summary>A single factor in the breakdown.</summary>
    public record ScoreFactor(string Name, string Description, decimal Impact);

    /// <summary>Tenant-level aggregate result.</summary>
    public record TenantScoreResult(
        decimal OverallScore,
        int AssetCount,
        int AssetsAboveThreshold,
        List<AssetScoreResult> AssetScores
    );

    private static readonly Dictionary<Severity, decimal> SeverityWeights = new()
    {
        { Severity.Critical, 10m },
        { Severity.High, 6m },
        { Severity.Medium, 3m },
        { Severity.Low, 1m },
    };

    /// <summary>
    /// Calculates the secure score for a single asset.
    /// Score range: 0–100 (100 = worst exposure).
    /// </summary>
    public static AssetScoreResult CalculateAssetScore(AssetInput asset)
    {
        var factors = new List<ScoreFactor>();
        var deviceWeight = ResolveDeviceValueWeight(asset.DeviceValue);

        // ── Vulnerability sub-score ──
        var vulnScore = CalculateVulnerabilitySubScore(asset.Vulnerabilities, out var vulnFactors);
        factors.AddRange(vulnFactors);

        // ── Configuration sub-score (placeholder for hardening) ──
        var configScore = 0m;
        if (!asset.HasSecurityProfile)
        {
            configScore += 10m;
            factors.Add(new ScoreFactor(
                "NoSecurityProfile",
                "Asset has no security profile assigned",
                10m));
        }

        // ── Composite ──
        // Vulnerability weight: 85%, Configuration weight: 15%
        var rawComposite = vulnScore * 0.85m + configScore * 0.15m;

        // Apply device value weight
        var weighted = rawComposite * deviceWeight;
        if (deviceWeight != 1.0m)
        {
            factors.Add(new ScoreFactor(
                "DeviceValue",
                $"Device value '{asset.DeviceValue}' applies ×{deviceWeight} multiplier",
                weighted - rawComposite));
        }

        var overall = Math.Clamp(Math.Round(weighted, 1), 0m, 100m);

        return new AssetScoreResult(
            asset.AssetId,
            overall,
            Math.Clamp(Math.Round(vulnScore, 1), 0m, 100m),
            Math.Clamp(Math.Round(configScore, 1), 0m, 100m),
            deviceWeight,
            asset.Vulnerabilities.Count,
            factors
        );
    }

    /// <summary>
    /// Aggregates per-asset scores into a tenant-level score.
    /// Weighted average by device value weight.
    /// </summary>
    public static TenantScoreResult CalculateTenantScore(
        IReadOnlyList<AssetScoreResult> assetScores,
        decimal targetScore = 40m)
    {
        if (assetScores.Count == 0)
            return new TenantScoreResult(0m, 0, 0, []);

        var totalWeight = assetScores.Sum(a => a.DeviceValueWeight);
        var weightedSum = assetScores.Sum(a => a.OverallScore * a.DeviceValueWeight);

        var overall = totalWeight > 0
            ? Math.Round(weightedSum / totalWeight, 1)
            : 0m;

        var aboveThreshold = assetScores.Count(a => a.OverallScore > targetScore);

        return new TenantScoreResult(
            Math.Clamp(overall, 0m, 100m),
            assetScores.Count,
            aboveThreshold,
            assetScores.ToList()
        );
    }

    public static string SerializeFactors(List<ScoreFactor> factors)
    {
        return JsonSerializer.Serialize(factors, JsonSerializerOptions);
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static decimal CalculateVulnerabilitySubScore(
        IReadOnlyList<VulnerabilityInput> vulnerabilities,
        out List<ScoreFactor> factors)
    {
        factors = [];

        if (vulnerabilities.Count == 0)
            return 0m;

        // Weighted severity score: each vuln contributes SeverityWeight × (EffectiveScore/10)
        // Maximum theoretical per-vuln contribution = 10 × 1.0 = 10
        var rawSum = 0m;
        var overdueCount = 0;

        foreach (var vuln in vulnerabilities)
        {
            var severityWeight = SeverityWeights.GetValueOrDefault(vuln.EffectiveSeverity, 1m);
            // Normalize CVSS (0-10) to 0-1 range; fallback to severity-based estimate
            var normalizedScore = vuln.EffectiveScore.HasValue
                ? vuln.EffectiveScore.Value / 10m
                : severityWeight / 10m;

            rawSum += severityWeight * normalizedScore;

            if (vuln.IsOverdue)
                overdueCount++;
        }

        // Normalize: use diminishing returns so that many low vulns don't dominate.
        // Score = 100 × (1 - e^(-rawSum / scale))
        // Scale factor controls how quickly the score saturates.
        var scale = 15m;
        var normalized = 100m * (1m - (decimal)Math.Exp((double)(-rawSum / scale)));

        factors.Add(new ScoreFactor(
            "VulnerabilityExposure",
            $"{vulnerabilities.Count} active vulnerabilities (weighted sum: {Math.Round(rawSum, 1)})",
            Math.Round(normalized, 1)));

        // Overdue SLA penalty: up to +15 points
        if (overdueCount > 0)
        {
            var overdueRatio = (decimal)overdueCount / vulnerabilities.Count;
            var penalty = Math.Round(overdueRatio * 15m, 1);
            normalized += penalty;

            factors.Add(new ScoreFactor(
                "OverdueSla",
                $"{overdueCount}/{vulnerabilities.Count} vulnerabilities overdue",
                penalty));
        }

        return Math.Clamp(normalized, 0m, 100m);
    }

    private static decimal ResolveDeviceValueWeight(string? deviceValue)
    {
        return deviceValue?.ToUpperInvariant() switch
        {
            "HIGH" => 1.5m,
            "LOW" => 0.8m,
            _ => 1.0m, // Normal or unset
        };
    }
}
