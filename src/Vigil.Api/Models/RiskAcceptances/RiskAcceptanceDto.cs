namespace Vigil.Api.Models.RiskAcceptances;

public record RiskAcceptanceDto(
    Guid Id,
    Guid VulnerabilityId,
    Guid? AssetId,
    string Status,
    string Justification,
    Guid RequestedBy,
    DateTimeOffset RequestedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? Conditions,
    DateTimeOffset? ExpiryDate,
    int? ReviewFrequency
);

public record RequestRiskAcceptanceRequest(
    string Justification,
    string? Conditions = null,
    DateTimeOffset? ExpiryDate = null,
    int? ReviewFrequency = null
);

public record ApproveRejectRequest(
    string Action,
    string? Conditions = null,
    DateTimeOffset? ExpiryDate = null,
    int? ReviewFrequency = null
);

public record RiskAcceptanceFilterQuery(string? Status = null, Guid? TenantId = null);
