using System.Text.Json;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Api.Models.Decisions;

public static class PatchAssessmentDtoMapper
{
    public static PatchAssessmentDto Empty(Guid? vulnerabilityId, string jobStatus = "None", string? jobError = null) =>
        new(
            vulnerabilityId,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            NullIfWhiteSpace(jobError),
            jobStatus);

    public static PatchAssessmentDto FromAssessment(VulnerabilityPatchAssessment assessment) =>
        new(
            assessment.VulnerabilityId,
            assessment.Recommendation,
            assessment.Confidence,
            assessment.Summary,
            assessment.UrgencyTier,
            assessment.UrgencyTargetSla,
            assessment.UrgencyReason,
            ParseJsonStringArray(assessment.SimilarVulnerabilities),
            ParseJsonStringArray(assessment.CompensatingControlsUntilPatched),
            ParseJsonStringArray(assessment.References),
            assessment.AiProfileName,
            assessment.AssessedAt,
            null,
            "Succeeded");

    public static PatchAssessmentDto FromJob(Guid vulnerabilityId, VulnerabilityAssessmentJob? job)
    {
        var status = job?.Status.ToString() ?? "None";
        var error = job?.Status == VulnerabilityAssessmentJobStatus.Failed
            ? job.Error
            : null;

        return Empty(vulnerabilityId, status, error);
    }

    private static IReadOnlyList<string>? ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            return parsed is { Count: > 0 } ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
