namespace PatchHound.Core.Entities;

public class DeviceRiskScore
{
    public const int CalculationVersionMaxLength = 32;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public decimal OverallScore { get; private set; }
    public decimal MaxEpisodeRiskScore { get; private set; }
    public int CriticalCount { get; private set; }
    public int HighCount { get; private set; }
    public int MediumCount { get; private set; }
    public int LowCount { get; private set; }
    public int OpenEpisodeCount { get; private set; }
    public string FactorsJson { get; private set; } = "[]";
    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }

    private DeviceRiskScore() { }

    public static DeviceRiskScore Create(
        Guid tenantId,
        Guid deviceId,
        decimal overallScore,
        decimal maxEpisodeRiskScore,
        int criticalCount,
        int highCount,
        int mediumCount,
        int lowCount,
        int openEpisodeCount,
        string factorsJson,
        string calculationVersion
    )
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));
        }
        if (string.IsNullOrWhiteSpace(factorsJson))
        {
            throw new ArgumentException("FactorsJson is required.", nameof(factorsJson));
        }
        if (string.IsNullOrWhiteSpace(calculationVersion))
        {
            throw new ArgumentException("CalculationVersion is required.", nameof(calculationVersion));
        }

        var normalizedFactorsJson = factorsJson.Trim();
        var normalizedCalculationVersion = calculationVersion.Trim();

        if (normalizedCalculationVersion.Length > CalculationVersionMaxLength)
        {
            throw new ArgumentException(
                $"CalculationVersion must be {CalculationVersionMaxLength} characters or fewer.",
                nameof(calculationVersion));
        }

        return new DeviceRiskScore
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = deviceId,
            OverallScore = overallScore,
            MaxEpisodeRiskScore = maxEpisodeRiskScore,
            CriticalCount = criticalCount,
            HighCount = highCount,
            MediumCount = mediumCount,
            LowCount = lowCount,
            OpenEpisodeCount = openEpisodeCount,
            FactorsJson = normalizedFactorsJson,
            CalculationVersion = normalizedCalculationVersion,
            CalculatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        decimal overallScore,
        decimal maxEpisodeRiskScore,
        int criticalCount,
        int highCount,
        int mediumCount,
        int lowCount,
        int openEpisodeCount,
        string factorsJson,
        string calculationVersion
    )
    {
        OverallScore = overallScore;
        MaxEpisodeRiskScore = maxEpisodeRiskScore;
        CriticalCount = criticalCount;
        HighCount = highCount;
        MediumCount = mediumCount;
        LowCount = lowCount;
        OpenEpisodeCount = openEpisodeCount;
        FactorsJson = factorsJson;
        CalculationVersion = calculationVersion;
        CalculatedAt = DateTimeOffset.UtcNow;
    }
}
