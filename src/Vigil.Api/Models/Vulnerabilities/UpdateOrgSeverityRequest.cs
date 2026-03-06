namespace Vigil.Api.Models.Vulnerabilities;

public record UpdateOrgSeverityRequest(
    string AdjustedSeverity,
    string Justification,
    string? AssetCriticalityFactor = null,
    string? ExposureFactor = null,
    string? CompensatingControls = null
);
