namespace PatchHound.Api.Models.Remediation;

public record RemediationCaseDto(
    Guid Id,
    Guid TenantId,
    Guid SoftwareProductId,
    string ProductName,
    string Vendor,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    int AffectedDeviceCount,
    int OpenExposureCount);
