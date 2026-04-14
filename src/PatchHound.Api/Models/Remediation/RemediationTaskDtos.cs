using PatchHound.Api.Models;

namespace PatchHound.Api.Models.Remediation;

public record RemediationTaskSummaryDto(
    int OpenTaskCount,
    int OverdueTaskCount,
    DateTimeOffset? NearestDueDate
);

public record RemediationTaskListItemDto(
    Guid Id,
    Guid RemediationCaseId,
    string SoftwareName,
    string? SoftwareVendor,
    Guid OwnerTeamId,
    string OwnerTeamName,
    int AffectedDeviceCount,
    int CriticalDeviceCount,
    int HighOrWorseDeviceCount,
    string HighestDeviceCriticality,
    DateTimeOffset DueDate,
    DateTimeOffset? MaintenanceWindowDate,
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
    DateTimeOffset? MaintenanceWindowDate,
    DateTimeOffset UpdatedAt
);

public record RemediationTaskFilterQuery(
    string? Search = null,
    string? Vendor = null,
    string? Criticality = null,
    string? AssetOwner = null,
    Guid? TaskId = null,
    Guid? DeviceAssetId = null,
    Guid? CaseId = null
);
