namespace PatchHound.Api.Models.Decisions;

public record RemediationDecisionListItemDto(
    Guid TenantSoftwareId,
    string SoftwareName,
    string Criticality,
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
    int AffectedDeviceCount,
    List<OpenEpisodeTrendPointDto> OpenEpisodeTrend
);

public record OpenEpisodeTrendPointDto(
    DateTimeOffset Day,
    int OpenEpisodeCount
);

public record RemediationDecisionFilterQuery(
    string? Search = null,
    string? Criticality = null,
    string? Outcome = null,
    string? ApprovalStatus = null
);
