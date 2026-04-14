namespace PatchHound.Core.Entities;

public class ExposureAssessment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceVulnerabilityExposureId { get; private set; }
    public Guid? SecurityProfileId { get; private set; }
    public decimal BaseCvss { get; private set; }
    public decimal EnvironmentalCvss { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset CalculatedAt { get; private set; }

    public decimal Score => EnvironmentalCvss;

    public DeviceVulnerabilityExposure Exposure { get; private set; } = null!;
    public SecurityProfile? SecurityProfile { get; private set; }

    private ExposureAssessment() { }

    public static ExposureAssessment Create(
        Guid tenantId,
        Guid exposureId,
        Guid? securityProfileId,
        decimal baseCvss,
        decimal environmentalCvss,
        string reason,
        DateTimeOffset calculatedAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException(nameof(tenantId));
        if (exposureId == Guid.Empty) throw new ArgumentException(nameof(exposureId));

        return new ExposureAssessment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceVulnerabilityExposureId = exposureId,
            SecurityProfileId = securityProfileId,
            BaseCvss = baseCvss,
            EnvironmentalCvss = environmentalCvss,
            Reason = reason?.Trim() ?? string.Empty,
            CalculatedAt = calculatedAt,
        };
    }

    public void Update(decimal baseCvss, decimal environmentalCvss, string reason, DateTimeOffset calculatedAt)
    {
        BaseCvss = baseCvss;
        EnvironmentalCvss = environmentalCvss;
        Reason = reason?.Trim() ?? string.Empty;
        CalculatedAt = calculatedAt;
    }
}
