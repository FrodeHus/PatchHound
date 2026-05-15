namespace PatchHound.Infrastructure.Data.Views;

public class ExposureLatestAssessment
{
    public Guid TenantId { get; set; }
    public Guid DeviceVulnerabilityExposureId { get; set; }
    public decimal EnvironmentalCvss { get; set; }
}
