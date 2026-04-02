using System.Text.RegularExpressions;

namespace PatchHound.Infrastructure.Services;

public static partial class AdvancedToolTemplateRenderer
{
    private static readonly IReadOnlyDictionary<string, string> AllowedDeviceParameters =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["deviceName"] = "Device name",
            ["vuln.name"] = "Vulnerability external id / CVE",
            ["vuln.vendor"] = "Software vendor from vulnerability context",
            ["vuln.product"] = "Software product from vulnerability context",
            ["vuln.version"] = "Software version from vulnerability context",
        };

    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9\.\-_]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    public static IReadOnlyList<string> ExtractPlaceholders(string query)
    {
        return PlaceholderRegex()
            .Matches(query)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<KeyValuePair<string, string>> GetAllowedDeviceParameters() =>
        AllowedDeviceParameters.ToList();

    public static bool IsAllowedParameter(string parameterName) =>
        AllowedDeviceParameters.ContainsKey(parameterName);

    public static string Render(
        string query,
        IReadOnlyDictionary<string, string?> values
    )
    {
        return PlaceholderRegex().Replace(
            query,
            match =>
            {
                var key = match.Groups[1].Value;
                if (!values.TryGetValue(key, out var value))
                {
                    throw new InvalidOperationException(
                        $"Missing value for placeholder '{{{{{key}}}}}'."
                    );
                }

                return value ?? string.Empty;
            }
        );
    }

    public static void ValidateAllowedParameters(string query)
    {
        var invalid = ExtractPlaceholders(query)
            .Where(parameter => !IsAllowedParameter(parameter))
            .ToList();

        if (invalid.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported placeholders: {string.Join(", ", invalid)}."
        );
    }
}
