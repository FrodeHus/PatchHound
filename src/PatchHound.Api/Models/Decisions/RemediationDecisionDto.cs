namespace PatchHound.Api.Models.Decisions;

public record DecisionContextDto(
    Guid RemediationCaseId,
    Guid? TenantSoftwareId,
    string SoftwareName,
    string? SoftwareVendor,
    string? SoftwareCategory,
    string? SoftwareDescription,
    Guid? SoftwareOwnerTeamId,
    string? SoftwareOwnerTeamName,
    string SoftwareOwnerAssignmentSource,
    string Criticality,
    List<DecisionBusinessLabelDto> BusinessLabels,
    DecisionSummaryDto Summary,
    DecisionWorkflowSummaryDto Workflow,
    DecisionWorkflowStateDto WorkflowState,
    RemediationDecisionDto? CurrentDecision,
    RemediationDecisionDto? PreviousDecision,
    DecisionApprovalResolutionDto? LatestApprovalResolution,
    List<AnalystRecommendationDto> Recommendations,
    List<DecisionVulnDto> TopVulnerabilities,
    List<DecisionVulnDto> OpenVulnerabilities,
    DecisionRiskDto? RiskScore,
    DecisionSlaDto? Sla,
    PatchAssessmentDto PatchAssessment,
    List<PatchAssessmentDto> PatchAssessments,
    ThreatIntelDto ThreatIntel
);

public record DecisionBusinessLabelDto(
    Guid Id,
    string Name,
    string? Color,
    string WeightCategory,
    double RiskWeight,
    int AffectedDeviceCount
);

public record DecisionApprovalResolutionDto(
    string Status,
    string? Justification,
    DateTimeOffset? ResolvedAt,
    string? ResolvedByDisplayName
);


public record ThreatIntelDto(
    string? Summary,
    DateTimeOffset? GeneratedAt,
    string? ProfileName,
    bool CanGenerate,
    string? UnavailableMessage
);

public record AiCitationDto(string Key, string EntityType, Guid EntityId, string Label, string Fact);

public record AiRecommendationDraftDto(
    string RecommendedOutcome,
    string PriorityOverride,
    string Rationale,
    bool OperationalContextUsed = false,
    bool Uncited = false,
    IReadOnlyList<AiCitationDto>? Citations = null,
    Guid? ContextSnapshotId = null
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
    Guid VulnerabilityId,
    string Outcome,
    string Justification,
    DateTimeOffset CreatedAt
);

public record AnalystRecommendationDto(
    Guid Id,
    Guid? VulnerabilityId,
    string RecommendedOutcome,
    string Rationale,
    string? PriorityOverride,
    Guid AnalystId,
    string? AnalystDisplayName,
    DateTimeOffset CreatedAt,
    Guid? ContextSnapshotId = null
);

public record RecommendationContextSnapshotDto(
    Guid Id,
    Guid RemediationCaseId,
    DateTimeOffset GeneratedAt,
    PatchHound.Core.Models.OperationalContext.OperationalContextPack Context,
    IReadOnlyList<PatchHound.Core.Models.OperationalContext.OperationalContextCitation> Citations
);

public record DecisionVulnDto(
    Guid VulnerabilityId,
    Guid VulnerabilityDefinitionId,
    string ExternalId,
    string Title,
    string? Description,
    string VendorSeverity,
    double? VendorScore,
    string? EffectiveSeverity,
    double? EffectiveScore,
    string? CvssVector,
    DateTimeOffset? FirstSeenAt,
    int AffectedDeviceCount,
    int AffectedVersionCount,
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
    DateTimeOffset? ReEvaluationDate,
    string? DeadlineMode
);

public record CreateOverrideRequest(
    Guid VulnerabilityId,
    string Outcome,
    string Justification
);

public record CreateRecommendationRequest(
    string RecommendedOutcome,
    string Rationale,
    string? PriorityOverride,
    Guid? VulnerabilityId,
    Guid? ContextSnapshotId = null
);

public record VerifyRemediationRequest(
    string Action
);

public record EnsureRemediationWorkflowResponse(
    Guid WorkflowId
);

