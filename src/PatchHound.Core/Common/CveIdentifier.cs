using System.Text.RegularExpressions;

namespace PatchHound.Core.Common;

/// <summary>
/// Validates that a string looks like a CVE identifier before it crosses a trust
/// boundary (notably: before being interpolated into an LLM prompt). CVE format
/// per MITRE: CVE-YYYY-NNNN+ where the sequence number is at least four digits.
/// </summary>
public static partial class CveIdentifier
{
    [GeneratedRegex(@"^CVE-\d{4}-\d{4,}$", RegexOptions.CultureInvariant)]
    private static partial Regex CvePattern();

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && CvePattern().IsMatch(value);
}
