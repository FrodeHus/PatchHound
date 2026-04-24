namespace PatchHound.Api.Models.Decisions;

public record RemediationDecisionListItemDto(
    Guid RemediationCaseId,
    string SoftwareName,
    string? SoftwareOwnerTeamName,
    string SoftwareOwnerAssignmentSource,
    string Criticality,
    string? Outcome,
    string? ApprovalStatus,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? MaintenanceWindowDate,
    DateTimeOffset? ExpiryDate,
    int TotalVulnerabilities,
    int CriticalCount,
    int HighCount,
    double? RiskScore,
    string? RiskBand,
    string? SlaStatus,
    DateTimeOffset? SlaDueDate,
    int AffectedDeviceCount,
    List<OpenEpisodeTrendPointDto> OpenEpisodeTrend,
    string? WorkflowStage
);

public record RemediationDecisionListSummaryDto(
    int SoftwareInScope,
    int WithDecision,
    int PendingApproval,
    int NoDecision
);

public record RemediationDecisionListPageDto(
    IReadOnlyList<RemediationDecisionListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    RemediationDecisionListSummaryDto Summary
);

public record OpenEpisodeTrendPointDto(
    DateTimeOffset Day,
    int OpenEpisodeCount
);

public record RemediationDecisionFilterQuery(
    string? Search = null,
    string? Criticality = null,
    string? Outcome = null,
    string? ApprovalStatus = null,
    string? DecisionState = null,
    bool? MissedMaintenanceWindow = null
);
