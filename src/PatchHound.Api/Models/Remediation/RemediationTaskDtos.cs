using PatchHound.Api.Models;

namespace PatchHound.Api.Models.Remediation;

public record RemediationTaskSummaryDto(
    int OpenTaskCount,
    int OverdueTaskCount,
    DateTimeOffset? NearestDueDate
);

public record RemediationTaskListItemDto(
    Guid Id,
    Guid SoftwareAssetId,
    Guid? TenantSoftwareId,
    string SoftwareName,
    string? SoftwareVendor,
    Guid OwnerTeamId,
    string OwnerTeamName,
    int AffectedDeviceCount,
    int CriticalDeviceCount,
    int HighOrWorseDeviceCount,
    string HighestDeviceCriticality,
    DateTimeOffset DueDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Status,
    IReadOnlyList<string> DeviceNames,
    IReadOnlyList<string> AssetOwners
);

public record RemediationTaskCreateResultDto(
    int CreatedCount,
    int EligibleCount
);

public record RemediationTaskTeamStatusDto(
    Guid OwnerTeamId,
    string OwnerTeamName,
    string Status,
    DateTimeOffset DueDate,
    DateTimeOffset UpdatedAt
);

public record RemediationTaskFilterQuery(
    string? Search = null,
    string? Vendor = null,
    string? Criticality = null,
    string? AssetOwner = null,
    Guid? DeviceAssetId = null,
    Guid? TenantSoftwareId = null
);
