namespace PatchHound.Api.Models.Decisions;

public record PatchAssessmentDto(
    Guid? VulnerabilityId,
    string? Recommendation,
    string? Confidence,
    string? Summary,
    string? UrgencyTier,
    string? UrgencyTargetSla,
    string? UrgencyReason,
    IReadOnlyList<string>? SimilarVulnerabilities,
    IReadOnlyList<string>? CompensatingControlsUntilPatched,
    IReadOnlyList<string>? References,
    string? AiProfileName,
    DateTimeOffset? AssessedAt,
    string JobStatus
);
