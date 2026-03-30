namespace PatchHound.Api.Models.ApprovalTasks;

public record ApprovalTaskListItemDto(
    Guid Id,
    string Type,
    string Status,
    string SoftwareName,
    string Criticality,
    string Outcome,
    string HighestSeverity,
    int VulnerabilityCount,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? MaintenanceWindowDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string DecidedByName,
    string? SlaStatus,
    DateTimeOffset? SlaDueDate
);

public record ApprovalTaskDetailDto(
    Guid Id,
    string Type,
    string Status,
    Guid RemediationDecisionId,
    string SoftwareName,
    string Criticality,
    string Outcome,
    string Justification,
    string HighestSeverity,
    bool RequiresJustification,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? MaintenanceWindowDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string DecidedByName,
    string? SlaStatus,
    DateTimeOffset? SlaDueDate,
    double? RiskScore,
    string? RiskBand,
    PagedVulnerabilityList Vulnerabilities,
    List<ApprovalDeviceVersionCohortDto> DeviceVersionCohorts,
    PagedDeviceList? Devices,
    List<ApprovalRecommendationDto> Recommendations,
    List<ApprovalAuditEntryDto> AuditTrail
);

public record PagedVulnerabilityList(
    List<ApprovalVulnDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public record ApprovalVulnDto(
    Guid TenantVulnerabilityId,
    string ExternalId,
    string Title,
    string VendorSeverity,
    double? VendorScore,
    string? EffectiveSeverity,
    bool KnownExploited,
    double? EpssScore
);

public record PagedDeviceList(
    List<ApprovalDeviceDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public record ApprovalDeviceVersionCohortDto(
    string? Version,
    int ActiveInstallCount,
    int DeviceCount,
    int ActiveVulnerabilityCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt
);

public record ApprovalDeviceDto(
    Guid DeviceAssetId,
    string DeviceName,
    string Criticality,
    string? Version,
    DateTimeOffset LastSeenAt,
    int OpenVulnerabilityCount
);

public record ApprovalRecommendationDto(
    Guid Id,
    string RecommendedOutcome,
    string Rationale,
    string? PriorityOverride,
    Guid AnalystId,
    DateTimeOffset CreatedAt
);

public record ApprovalAuditEntryDto(
    string Action,
    string? UserDisplayName,
    string? Justification,
    DateTimeOffset Timestamp
);

public record ResolveApprovalTaskRequest(
    string Action,
    string? Justification,
    DateTimeOffset? MaintenanceWindowDate
);

public record ApprovalTaskFilterQuery(
    string? Status = null,
    string? Type = null,
    string? Search = null,
    bool ShowRead = false
);
