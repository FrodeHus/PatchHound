using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class ExposureAssessment
{
    public const int CalculationVersionMaxLength = 32;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceVulnerabilityExposureId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public Guid? SecurityProfileId { get; private set; }
    public Severity EffectiveSeverity { get; private set; }
    public decimal? Score { get; private set; }
    public string? Vector { get; private set; }
    public string FactorsJson { get; private set; } = "[]";
    public string ReasonSummary { get; private set; } = string.Empty;
    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }

    public DeviceVulnerabilityExposure Exposure { get; private set; } = null!;

    private ExposureAssessment() { }

    public static ExposureAssessment Create(
        Guid tenantId,
        Guid deviceVulnerabilityExposureId,
        Guid deviceId,
        Guid vulnerabilityId,
        Guid? securityProfileId,
        Severity effectiveSeverity,
        decimal? score,
        string? vector,
        string factorsJson,
        string reasonSummary,
        string calculationVersion)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException(nameof(tenantId));
        if (deviceVulnerabilityExposureId == Guid.Empty) throw new ArgumentException(nameof(deviceVulnerabilityExposureId));
        if (deviceId == Guid.Empty) throw new ArgumentException(nameof(deviceId));
        if (vulnerabilityId == Guid.Empty) throw new ArgumentException(nameof(vulnerabilityId));
        if (string.IsNullOrWhiteSpace(factorsJson)) throw new ArgumentException(nameof(factorsJson));
        if (string.IsNullOrWhiteSpace(calculationVersion)) throw new ArgumentException(nameof(calculationVersion));

        var normalizedVersion = calculationVersion.Trim();
        if (normalizedVersion.Length > CalculationVersionMaxLength)
            throw new ArgumentException(nameof(calculationVersion));

        return new ExposureAssessment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceVulnerabilityExposureId = deviceVulnerabilityExposureId,
            DeviceId = deviceId,
            VulnerabilityId = vulnerabilityId,
            SecurityProfileId = securityProfileId,
            EffectiveSeverity = effectiveSeverity,
            Score = score,
            Vector = vector,
            FactorsJson = factorsJson.Trim(),
            ReasonSummary = reasonSummary?.Trim() ?? string.Empty,
            CalculationVersion = normalizedVersion,
            CalculatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        Guid? securityProfileId,
        Severity effectiveSeverity,
        decimal? score,
        string? vector,
        string factorsJson,
        string reasonSummary,
        string calculationVersion)
    {
        if (string.IsNullOrWhiteSpace(factorsJson)) throw new ArgumentException(nameof(factorsJson));
        if (string.IsNullOrWhiteSpace(calculationVersion)) throw new ArgumentException(nameof(calculationVersion));

        var normalizedVersion = calculationVersion.Trim();
        if (normalizedVersion.Length > CalculationVersionMaxLength)
            throw new ArgumentException(nameof(calculationVersion));

        SecurityProfileId = securityProfileId;
        EffectiveSeverity = effectiveSeverity;
        Score = score;
        Vector = vector;
        FactorsJson = factorsJson.Trim();
        ReasonSummary = reasonSummary?.Trim() ?? string.Empty;
        CalculationVersion = normalizedVersion;
        CalculatedAt = DateTimeOffset.UtcNow;
    }
}
