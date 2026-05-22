using System.Text.Json;

namespace PatchHound.Infrastructure.Tenants;

public sealed record JinaReaderOptions(
    int TimeoutSeconds,
    int MaxContentChars,
    string ResponseFormat,
    bool UseReaderLmV2,
    bool RemoveImages,
    bool IncludeLinkSummary,
    bool IncludeImageSummary,
    string TargetSelector,
    string ExcludeSelector,
    string WaitForSelector
)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static JinaReaderOptions Default { get; } = new(
        TimeoutSeconds: 20,
        MaxContentChars: 8000,
        ResponseFormat: "markdown",
        UseReaderLmV2: false,
        RemoveImages: true,
        IncludeLinkSummary: true,
        IncludeImageSummary: false,
        TargetSelector: string.Empty,
        ExcludeSelector: string.Empty,
        WaitForSelector: string.Empty
    );

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static JinaReaderOptions FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        try
        {
            return JsonSerializer.Deserialize<JinaReaderOptions>(json, JsonOptions) ?? Default;
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    public JinaReaderOptions Normalize() =>
        this with
        {
            TimeoutSeconds = Math.Clamp(TimeoutSeconds, 3, 60),
            MaxContentChars = Math.Clamp(MaxContentChars, 1000, 20000),
            ResponseFormat = string.IsNullOrWhiteSpace(ResponseFormat) ? "markdown" : ResponseFormat.Trim(),
            TargetSelector = TargetSelector.Trim(),
            ExcludeSelector = ExcludeSelector.Trim(),
            WaitForSelector = WaitForSelector.Trim(),
        };
}
