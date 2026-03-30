namespace PatchHound.Api.Models.Decisions;

public record DecisionContextDto(
    Guid TenantSoftwareId,
    string SoftwareName,
    string Criticality,
    DecisionSummaryDto Summary,
    DecisionWorkflowSummaryDto Workflow,
    DecisionWorkflowStateDto WorkflowState,
    RemediationDecisionDto? CurrentDecision,
    RemediationDecisionDto? PreviousDecision,
    List<AnalystRecommendationDto> Recommendations,
    List<DecisionVulnDto> TopVulnerabilities,
    DecisionRiskDto? RiskScore,
    DecisionSlaDto? Sla,
    DecisionAiSummaryDto AiSummary
);

public record DecisionAiSummaryDto(
    string? Content,
    string? OwnerRecommendation,
    string? AnalystAssessment,
    string? ExceptionRecommendation,
    string? RecommendedOutcome,
    string? RecommendedPriority,
    string Status,
    bool IsStale,
    string? ReviewStatus,
    DateTimeOffset? ReviewedAt,
    string? ReviewedByDisplayName,
    DateTimeOffset? GeneratedAt,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? CompletedAt,
    string? ProviderType,
    string? ProfileName,
    string? Model,
    bool CanGenerate,
    bool IsGenerating,
    string? LastError,
    string? UnavailableMessage
);

public record DecisionSummaryDto(
    int TotalVulnerabilities,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    int WithKnownExploit,
    int WithActiveAlert
);

public record DecisionWorkflowSummaryDto(
    int AffectedDeviceCount,
    int AffectedOwnerTeamCount,
    int OpenPatchingTaskCount,
    int CompletedPatchingTaskCount,
    List<OpenEpisodeTrendPointDto> OpenEpisodeTrend
);

public record DecisionWorkflowStateDto(
    Guid? WorkflowId,
    string CurrentStage,
    string CurrentStageLabel,
    string CurrentStageDescription,
    string CurrentActorSummary,
    bool CanActOnCurrentStage,
    List<string> CurrentUserRoles,
    List<string> CurrentUserTeams,
    List<string> ExpectedRoles,
    string? ExpectedTeamName,
    bool? IsInExpectedTeam,
    bool IsRecurrence,
    bool HasActiveWorkflow,
    List<DecisionWorkflowStageDto> Stages
);

public record DecisionWorkflowStageDto(
    string Id,
    string Label,
    string State,
    string Description
);

public record RemediationDecisionDto(
    Guid Id,
    string Outcome,
    string ApprovalStatus,
    string Justification,
    Guid DecidedBy,
    DateTimeOffset DecidedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? MaintenanceWindowDate,
    DateTimeOffset? ExpiryDate,
    DateTimeOffset? ReEvaluationDate,
    DecisionRejectionDto? LatestRejection,
    List<VulnerabilityOverrideDto> Overrides
);

public record DecisionRejectionDto(
    string? Comment,
    DateTimeOffset? RejectedAt
);

public record VulnerabilityOverrideDto(
    Guid Id,
    Guid TenantVulnerabilityId,
    string Outcome,
    string Justification,
    DateTimeOffset CreatedAt
);

public record AnalystRecommendationDto(
    Guid Id,
    Guid? TenantVulnerabilityId,
    string RecommendedOutcome,
    string Rationale,
    string? PriorityOverride,
    Guid AnalystId,
    string? AnalystDisplayName,
    DateTimeOffset CreatedAt
);

public record DecisionVulnDto(
    Guid TenantVulnerabilityId,
    Guid VulnerabilityDefinitionId,
    string ExternalId,
    string Title,
    string VendorSeverity,
    double? VendorScore,
    string? EffectiveSeverity,
    double? EffectiveScore,
    string? CvssVector,
    bool KnownExploited,
    bool PublicExploit,
    bool ActiveAlert,
    double? EpssScore,
    double? EpisodeRiskScore,
    string? OverrideOutcome
);

public record DecisionRiskDto(
    double CompositeScore,
    string RiskBand,
    DateTimeOffset? AssessedAt
);

public record DecisionSlaDto(
    int CriticalDays,
    int HighDays,
    int MediumDays,
    int LowDays,
    string SlaStatus,
    DateTimeOffset? DueDate
);

public record CreateDecisionRequest(
    string Outcome,
    string? Justification,
    DateTimeOffset? MaintenanceWindowDate,
    DateTimeOffset? ExpiryDate,
    DateTimeOffset? ReEvaluationDate
);

public record CreateOverrideRequest(
    Guid TenantVulnerabilityId,
    string Outcome,
    string Justification
);

public record CreateRecommendationRequest(
    string RecommendedOutcome,
    string Rationale,
    string? PriorityOverride,
    Guid? TenantVulnerabilityId
);

public record VerifyRemediationRequest(
    string Action
);

public record EnsureRemediationWorkflowResponse(
    Guid WorkflowId
);

public record ReviewDecisionAiSummaryRequest(
    string Action
);
