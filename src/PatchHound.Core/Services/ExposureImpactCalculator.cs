using PatchHound.Core.Enums;

namespace PatchHound.Core.Services;

/// <summary>
/// Pure calculation logic for Software Exposure Impact scores.
/// No I/O — all data is passed in.
/// </summary>
public static class ExposureImpactCalculator
{
    public const string CalculationVersion = "software-impact-v1";

    /// <summary>A vulnerability linked to a software product.</summary>
    public record SoftwareVulnerabilityInput(
        Severity Severity,
        decimal? CvssScore,
        string? ExternalId = null
    );

    /// <summary>Input describing a single software product for impact scoring.</summary>
    public record SoftwareImpactInput(
        Guid TenantSoftwareId,
        int DeviceCount,
        int HighValueDeviceCount,
        IReadOnlyList<SoftwareVulnerabilityInput> Vulnerabilities
    );

    /// <summary>Computed impact score for one software product.</summary>
    public record SoftwareImpactResult(Guid TenantSoftwareId, decimal ImpactScore);

    /// <summary>Detailed breakdown of one vulnerability's contribution to software impact.</summary>
    public record SoftwareImpactVulnerabilityFactor(
        string? ExternalId,
        Severity Severity,
        decimal? CvssScore,
        decimal SeverityWeight,
        decimal NormalizedScore,
        decimal Contribution
    );

    /// <summary>Detailed breakdown of the software impact calculation.</summary>
    public record SoftwareImpactBreakdown(
        Guid TenantSoftwareId,
        decimal ImpactScore,
        decimal RawVulnerabilitySum,
        decimal VulnerabilityComponent,
        decimal DeviceReachWeight,
        decimal HighValueRatio,
        decimal HighValueBonus,
        decimal RawScore,
        IReadOnlyList<SoftwareImpactVulnerabilityFactor> VulnerabilityFactors
    );

    /// <summary>Input describing one installed software on a device.</summary>
    public record InstalledSoftwareInput(Guid TenantSoftwareId, decimal ImpactScore);

    /// <summary>Device exposure = sum of installed software impacts.</summary>
    public record DeviceExposureResult(Guid DeviceAssetId, decimal ExposureScore);

    private static readonly Dictionary<Severity, decimal> SeverityWeights = new()
    {
        { Severity.Critical, 10m },
        { Severity.High, 6m },
        { Severity.Medium, 3m },
        { Severity.Low, 1m },
    };

    /// <summary>
    /// Calculates the exposure impact score for a software product.
    /// Factors: linked vulnerabilities (CVSS-weighted), device count, high-value device ratio.
    /// Score range: 0–100 (100 = highest impact).
    /// </summary>
    public static SoftwareImpactResult CalculateSoftwareImpact(SoftwareImpactInput input)
    {
        var breakdown = CalculateSoftwareImpactBreakdown(input);
        return new SoftwareImpactResult(input.TenantSoftwareId, breakdown.ImpactScore);
    }

    /// <summary>
    /// Calculates the exposure impact score for a software product and returns the intermediate values.
    /// </summary>
    public static SoftwareImpactBreakdown CalculateSoftwareImpactBreakdown(SoftwareImpactInput input)
    {
        if (input.Vulnerabilities.Count == 0 || input.DeviceCount == 0)
        {
            return new SoftwareImpactBreakdown(
                input.TenantSoftwareId,
                0m,
                0m,
                0m,
                1m,
                0m,
                1m,
                0m,
                []
            );
        }

        // Vulnerability severity sum uses the shared severity weighting for impact scoring.
        var rawVulnSum = 0m;
        var vulnerabilityFactors = new List<SoftwareImpactVulnerabilityFactor>(input.Vulnerabilities.Count);
        foreach (var vuln in input.Vulnerabilities)
        {
            var severityWeight = SeverityWeights.GetValueOrDefault(vuln.Severity, 1m);
            var normalizedScore = vuln.CvssScore.HasValue
                ? vuln.CvssScore.Value / 10m
                : severityWeight / 10m;
            var contribution = severityWeight * normalizedScore;
            rawVulnSum += contribution;
            vulnerabilityFactors.Add(
                new SoftwareImpactVulnerabilityFactor(
                    vuln.ExternalId,
                    vuln.Severity,
                    vuln.CvssScore,
                    severityWeight,
                    normalizedScore,
                    contribution
                )
            );
        }

        // Diminishing returns on vulnerability severity
        var vulnComponent = 100m * (1m - (decimal)Math.Exp((double)(-rawVulnSum / 15m)));

        // Device reach weight: logarithmic scale so 1 device ≈ 1.0, 100 devices ≈ 1.87
        var deviceReachWeight = 1m + (decimal)Math.Log10(Math.Max(input.DeviceCount, 1));

        // High-value device bonus: up to +30% for fully high-value fleet
        var highValueRatio = input.DeviceCount > 0
            ? (decimal)input.HighValueDeviceCount / input.DeviceCount
            : 0m;
        var highValueBonus = 1m + highValueRatio * 0.3m;

        var rawScore = vulnComponent * deviceReachWeight * highValueBonus / 3m;
        var clamped = Math.Clamp(Math.Round(rawScore, 1), 0m, 100m);

        return new SoftwareImpactBreakdown(
            input.TenantSoftwareId,
            clamped,
            Math.Round(rawVulnSum, 3),
            Math.Round(vulnComponent, 3),
            Math.Round(deviceReachWeight, 3),
            Math.Round(highValueRatio, 3),
            Math.Round(highValueBonus, 3),
            Math.Round(rawScore, 3),
            vulnerabilityFactors
        );
    }

    /// <summary>
    /// Calculates device exposure as the sum of its installed software impact scores
    /// using diminishing returns.
    /// </summary>
    public static DeviceExposureResult CalculateDeviceExposure(
        Guid deviceAssetId,
        IReadOnlyList<InstalledSoftwareInput> installedSoftware)
    {
        if (installedSoftware.Count == 0)
            return new DeviceExposureResult(deviceAssetId, 0m);

        var rawSum = installedSoftware.Sum(s => s.ImpactScore);

        // Diminishing returns: scale=60 so a device with many low-impact software doesn't saturate quickly
        var exposure = 100m * (1m - (decimal)Math.Exp((double)(-rawSum / 60m)));
        var clamped = Math.Clamp(Math.Round(exposure, 1), 0m, 100m);

        return new DeviceExposureResult(deviceAssetId, clamped);
    }
}
