namespace PatchHound.Core.Entities;

public class SoftwareRiskScore
{
    public const int CalculationVersionMaxLength = 32;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public decimal OverallScore { get; private set; }
    public decimal MaxExposureScore { get; private set; }
    public int CriticalExposureCount { get; private set; }
    public int HighExposureCount { get; private set; }
    public int MediumExposureCount { get; private set; }
    public int LowExposureCount { get; private set; }
    public int AffectedDeviceCount { get; private set; }
    public int OpenExposureCount { get; private set; }
    public string FactorsJson { get; private set; } = "[]";
    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }

    public SoftwareProduct SoftwareProduct { get; private set; } = null!;

    private SoftwareRiskScore() { }

    public static SoftwareRiskScore Create(
        Guid tenantId,
        Guid softwareProductId,
        decimal overallScore,
        decimal maxExposureScore,
        int criticalExposureCount,
        int highExposureCount,
        int mediumExposureCount,
        int lowExposureCount,
        int affectedDeviceCount,
        int openExposureCount,
        string factorsJson,
        string calculationVersion
    )
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (softwareProductId == Guid.Empty)
            throw new ArgumentException("SoftwareProductId is required.", nameof(softwareProductId));

        var (normalizedFactorsJson, normalizedCalculationVersion) =
            NormalizeAndValidate(factorsJson, calculationVersion);

        return new SoftwareRiskScore
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SoftwareProductId = softwareProductId,
            OverallScore = overallScore,
            MaxExposureScore = maxExposureScore,
            CriticalExposureCount = criticalExposureCount,
            HighExposureCount = highExposureCount,
            MediumExposureCount = mediumExposureCount,
            LowExposureCount = lowExposureCount,
            AffectedDeviceCount = affectedDeviceCount,
            OpenExposureCount = openExposureCount,
            FactorsJson = normalizedFactorsJson,
            CalculationVersion = normalizedCalculationVersion,
            CalculatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        decimal overallScore,
        decimal maxExposureScore,
        int criticalExposureCount,
        int highExposureCount,
        int mediumExposureCount,
        int lowExposureCount,
        int affectedDeviceCount,
        int openExposureCount,
        string factorsJson,
        string calculationVersion
    )
    {
        var (normalizedFactorsJson, normalizedCalculationVersion) =
            NormalizeAndValidate(factorsJson, calculationVersion);

        OverallScore = overallScore;
        MaxExposureScore = maxExposureScore;
        CriticalExposureCount = criticalExposureCount;
        HighExposureCount = highExposureCount;
        MediumExposureCount = mediumExposureCount;
        LowExposureCount = lowExposureCount;
        AffectedDeviceCount = affectedDeviceCount;
        OpenExposureCount = openExposureCount;
        FactorsJson = normalizedFactorsJson;
        CalculationVersion = normalizedCalculationVersion;
        CalculatedAt = DateTimeOffset.UtcNow;
    }

    private static (string factorsJson, string calculationVersion) NormalizeAndValidate(
        string factorsJson,
        string calculationVersion)
    {
        if (string.IsNullOrWhiteSpace(factorsJson))
            throw new ArgumentException("FactorsJson is required.", nameof(factorsJson));
        if (string.IsNullOrWhiteSpace(calculationVersion))
            throw new ArgumentException("CalculationVersion is required.", nameof(calculationVersion));

        var normalizedFactorsJson = factorsJson.Trim();
        var normalizedCalculationVersion = calculationVersion.Trim();

        if (normalizedCalculationVersion.Length > CalculationVersionMaxLength)
            throw new ArgumentException(
                $"CalculationVersion must be {CalculationVersionMaxLength} characters or fewer.",
                nameof(calculationVersion));

        return (normalizedFactorsJson, normalizedCalculationVersion);
    }
}
