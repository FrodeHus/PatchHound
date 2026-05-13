namespace PatchHound.Api.Models.Decisions;

public record PatchAssessmentDto(
    string? Recommendation,
    string? Confidence,
    string? Summary,
    string? UrgencyTier,
    string? UrgencyTargetSla,
    string? UrgencyReason,
    string? SimilarVulnerabilities,
    string? CompensatingControlsUntilPatched,
    string? References,
    string? AiProfileName,
    DateTimeOffset? AssessedAt,
    string JobStatus
);
