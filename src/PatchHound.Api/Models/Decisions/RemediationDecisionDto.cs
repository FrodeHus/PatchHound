namespace PatchHound.Api.Models.Decisions;

public record DecisionContextDto(
    Guid TenantSoftwareId,
    string SoftwareName,
    string Criticality,
    DecisionSummaryDto Summary,
    RemediationDecisionDto? CurrentDecision,
    List<AnalystRecommendationDto> Recommendations,
    List<DecisionVulnDto> TopVulnerabilities,
    DecisionRiskDto? RiskScore,
    DecisionSlaDto? Sla,
    string? AiNarrative
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

public record RemediationDecisionDto(
    Guid Id,
    string Outcome,
    string ApprovalStatus,
    string Justification,
    Guid DecidedBy,
    DateTimeOffset DecidedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ExpiryDate,
    DateTimeOffset? ReEvaluationDate,
    List<VulnerabilityOverrideDto> Overrides
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
    DateTimeOffset? ExpiryDate,
    DateTimeOffset? ReEvaluationDate
);

public record ApproveRejectDecisionRequest(
    string Action
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
