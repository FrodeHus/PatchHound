using System.Text.RegularExpressions;
using PatchHound.Core.Enums;

namespace PatchHound.Core.Services;

public static partial class OwnerFacingIssueSummaryFormatter
{
    public static string BuildIssueSummary(
        string? softwareDisplay,
        string? vulnerabilityTitle,
        string? vulnerabilityDescription,
        Severity severity
    )
    {
        var softwareText = string.IsNullOrWhiteSpace(softwareDisplay)
            ? "Software on this asset"
            : softwareDisplay.Trim();
        var severityText = severity switch
        {
            Severity.Critical => "urgent",
            Severity.High => "high-priority",
            Severity.Medium => "important",
            _ => "lower-priority",
        };

        var detail = FirstReadableSentence(vulnerabilityDescription)
            ?? FirstReadableSentence(vulnerabilityTitle);

        if (string.IsNullOrWhiteSpace(detail))
        {
            return $"{softwareText} has a {severityText} security issue that should be reviewed.";
        }

        return $"{softwareText} has a {severityText} security issue. {detail}";
    }

    private static string? FirstReadableSentence(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var cleaned = CollapseWhitespace(CvePrefixRegex().Replace(source.Trim(), string.Empty));
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        var sentence = cleaned
            .Split(['.', '!', '?'], 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return null;
        }

        if (!sentence.EndsWith('.'))
        {
            sentence += ".";
        }

        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    private static string CollapseWhitespace(string value)
    {
        return WhitespaceRegex().Replace(value, " ").Trim();
    }

    [GeneratedRegex(@"^\s*CVE-\d{4}-\d+\s*[:\-–]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CvePrefixRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
