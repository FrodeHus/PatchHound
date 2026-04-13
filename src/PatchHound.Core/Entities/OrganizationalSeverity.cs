using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class OrganizationalSeverity
{
    public Guid Id { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public Guid TenantId { get; private set; }
    public Severity AdjustedSeverity { get; private set; }
    public string Justification { get; private set; } = null!;
    public string? AssetCriticalityFactor { get; private set; }
    public string? ExposureFactor { get; private set; }
    public string? CompensatingControls { get; private set; }
    public Guid AdjustedBy { get; private set; }
    public DateTimeOffset AdjustedAt { get; private set; }

    public Vulnerability Vulnerability { get; private set; } = null!;

    private OrganizationalSeverity() { }

    public static OrganizationalSeverity Create(
        Guid vulnerabilityId,
        Guid tenantId,
        Severity adjustedSeverity,
        string justification,
        Guid adjustedBy,
        string? assetCriticalityFactor = null,
        string? exposureFactor = null,
        string? compensatingControls = null
    )
    {
        return new OrganizationalSeverity
        {
            Id = Guid.NewGuid(),
            VulnerabilityId = vulnerabilityId,
            TenantId = tenantId,
            AdjustedSeverity = adjustedSeverity,
            Justification = justification,
            AdjustedBy = adjustedBy,
            AdjustedAt = DateTimeOffset.UtcNow,
            AssetCriticalityFactor = assetCriticalityFactor,
            ExposureFactor = exposureFactor,
            CompensatingControls = compensatingControls,
        };
    }
}
