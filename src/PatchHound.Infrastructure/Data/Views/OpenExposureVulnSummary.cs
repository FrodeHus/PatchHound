using PatchHound.Core.Enums;

namespace PatchHound.Infrastructure.Data.Views;

public class OpenExposureVulnSummary
{
    public Guid TenantId { get; set; }
    public Guid VulnerabilityId { get; set; }
    public Severity VendorSeverity { get; set; }
    public int AffectedDeviceCount { get; set; }
    public DateTimeOffset LatestSeenAt { get; set; }
    public decimal? MaxCvss { get; set; }
    public DateTimeOffset? PublishedDate { get; set; }
}
