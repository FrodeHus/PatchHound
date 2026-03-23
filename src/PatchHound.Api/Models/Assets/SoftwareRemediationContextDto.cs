namespace PatchHound.Api.Models.Assets;

public record SoftwareRemediationContextDto(
    Guid AssetId,
    string AssetName,
    string Criticality,
    SoftwareRemediationSummaryDto Summary,
    List<SoftwareRemediationVulnDto> Vulnerabilities
);

public record SoftwareRemediationSummaryDto(
    int TotalVulnerabilities,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    int WithKnownExploit,
    int WithActiveAlert,
    int PendingRemediationTasks,
    int RiskAcceptedCount
);

public record SoftwareRemediationVulnDto(
    Guid VulnerabilityDefinitionId,
    string ExternalId,
    string Title,
    string VendorSeverity,
    double? VendorScore,
    string EffectiveSeverity,
    double? EffectiveScore,
    string? CvssVector,
    string MatchMethod,
    string Confidence,
    string Evidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? ResolvedAt,
    SoftwareRemediationThreatDto? Threat,
    SoftwareRemediationTaskDto? RemediationTask,
    SoftwareRemediationRiskAcceptanceDto? RiskAcceptance
);

public record SoftwareRemediationThreatDto(
    double? EpssScore,
    double? EpssPercentile,
    bool KnownExploited,
    bool PublicExploit,
    bool ActiveAlert,
    bool HasRansomwareAssociation
);

public record SoftwareRemediationTaskDto(
    Guid Id,
    string Status,
    string? Justification,
    DateTimeOffset DueDate,
    DateTimeOffset CreatedAt
);

public record SoftwareRemediationRiskAcceptanceDto(
    Guid Id,
    string Status,
    string Justification,
    string? Conditions,
    DateTimeOffset? ExpiryDate,
    DateTimeOffset RequestedAt
);
