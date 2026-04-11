using PatchHound.Api.Models.Assets;
using PatchHound.Api.Models.Remediation;
using PatchHound.Api.Models.SecurityProfiles;

namespace PatchHound.Api.Models.Devices;

// Phase 1 canonical cleanup (Task 13): Device-native projection of a device
// row for /api/devices. Drops the legacy `AssetType` column and renames the
// `Device*` fields to their canonical (unprefixed) names since the payload is
// unambiguously a device.
public record DeviceDto(
    Guid Id,
    string ExternalId,
    string Name,
    decimal? CurrentRiskScore,
    string? GroupName,
    string Criticality,
    string OwnerType,
    Guid? OwnerUserId,
    Guid? OwnerTeamId,
    string? SecurityProfileName,
    int VulnerabilityCount,
    int RecurringVulnerabilityCount,
    string? HealthStatus,
    string? RiskScore,
    string? ExposureLevel,
    string[] Tags,
    IReadOnlyList<BusinessLabelSummaryDto> BusinessLabels,
    string? OnboardingStatus,
    string? DeviceValue
);

public record DeviceDetailDto(
    Guid Id,
    string ExternalId,
    string Name,
    string? Description,
    string Criticality,
    DeviceCriticalityDetailDto? CriticalityDetail,
    string OwnerType,
    string? OwnerUserName,
    Guid? OwnerUserId,
    string? OwnerTeamName,
    Guid? OwnerTeamId,
    string? FallbackTeamName,
    Guid? FallbackTeamId,
    AssetSecurityProfileSummaryDto? SecurityProfile,
    string? ComputerDnsName,
    string? HealthStatus,
    string? OsPlatform,
    string? OsVersion,
    string? RiskScore,
    DateTimeOffset? LastSeenAt,
    string? LastIpAddress,
    string? AadDeviceId,
    string? GroupId,
    string? GroupName,
    string? ExposureLevel,
    bool? IsAadJoined,
    string? OnboardingStatus,
    string? DeviceValue,
    IReadOnlyList<BusinessLabelSummaryDto> BusinessLabels,
    DeviceRiskDetailDto? Risk,
    RemediationTaskSummaryDto? Remediation,
    string[] Tags,
    string Metadata
);

public record DeviceCriticalityDetailDto(
    string Source,
    string? Reason,
    Guid? RuleId,
    DateTimeOffset? UpdatedAt
);

public record DeviceRiskDetailDto(
    decimal OverallScore,
    decimal MaxEpisodeRiskScore,
    string RiskBand,
    int OpenEpisodeCount,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    DateTimeOffset CalculatedAt
);

public record DeviceFilterQuery(
    string? Criticality = null,
    string? OwnerType = null,
    string? DeviceGroup = null,
    bool? UnassignedOnly = null,
    Guid? OwnerId = null,
    Guid? TenantId = null,
    string? Search = null,
    string? HealthStatus = null,
    string? ExposureLevel = null,
    string? Tag = null,
    Guid? BusinessLabelId = null,
    string? OnboardingStatus = null
);

public record UpdateDeviceBusinessLabelsRequest(
    IReadOnlyList<Guid> BusinessLabelIds
);

public record AssignDeviceOwnerRequest(string OwnerType, Guid OwnerId);

public record SetDeviceCriticalityRequest(string Criticality);

public record AssignDeviceSecurityProfileRequest(Guid? SecurityProfileId);

public record BulkAssignDevicesRequest(List<Guid> DeviceIds, string OwnerType, Guid OwnerId);

public record BulkAssignDevicesResponse(int UpdatedCount);
