namespace PatchHound.Api.Models.Decisions;

public record RemediationDecisionListItemDto(
    Guid AssetId,
    string AssetName,
    string Criticality,
    Guid? TenantSoftwareId,
    string? Outcome,
    string? ApprovalStatus,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ExpiryDate,
    int TotalVulnerabilities,
    int CriticalCount,
    int HighCount,
    double? RiskScore,
    string? RiskBand,
    string? SlaStatus,
    DateTimeOffset? SlaDueDate,
    int AffectedDeviceCount
);

public record RemediationDecisionFilterQuery(
    string? Search = null,
    string? Criticality = null,
    string? Outcome = null,
    string? ApprovalStatus = null
);
