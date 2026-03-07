using System.Text.Json;
using System.Text.Json.Nodes;
namespace PatchHound.Infrastructure.Tenants;

public static class TenantSourceSettings
{
    public const string DefaultDefenderSchedule = "0 */6 * * *";
    public const string DefenderSourceKey = "microsoft-defender";
    public const string DefaultDefenderApiBaseUrl = "https://api.securitycenter.microsoft.com";
    public const string DefaultDefenderTokenScope = "https://api.securitycenter.microsoft.com/.default";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static List<PersistedIngestionSource> ReadSources(string settings)
    {
        var configuredSources = ParseSettingsObject(settings)["ingestionSources"] as JsonArray;
        var sources = configuredSources?
            .Deserialize<List<PersistedIngestionSource>>(JsonOptions) ?? [];

        if (sources.Any(source => string.Equals(source.Key, DefenderSourceKey, StringComparison.OrdinalIgnoreCase)))
        {
            return NormalizeSources(sources);
        }

        sources.Add(new PersistedIngestionSource
        {
            Key = DefenderSourceKey,
            DisplayName = "Microsoft Defender",
            Enabled = false,
            SyncSchedule = DefaultDefenderSchedule,
            Credentials = new PersistedSourceCredentials
            {
                ApiBaseUrl = DefaultDefenderApiBaseUrl,
                TokenScope = DefaultDefenderTokenScope,
            },
        });

        return NormalizeSources(sources);
    }

    public static string WriteSources(
        string existingSettings,
        IEnumerable<PersistedIngestionSource> sources
    )
    {
        var settings = ParseSettingsObject(existingSettings);
        settings["ingestionSources"] = JsonSerializer.SerializeToNode(
            sources.ToList(),
            JsonOptions
        );
        return settings.ToJsonString(JsonOptions);
    }

    public static bool HasConfiguredCredentials(PersistedSourceCredentials? credentials)
    {
        return !string.IsNullOrWhiteSpace(credentials?.TenantId)
            || !string.IsNullOrWhiteSpace(credentials?.ClientId)
            || !string.IsNullOrWhiteSpace(credentials?.SecretRef)
            || !string.IsNullOrWhiteSpace(credentials?.ClientSecret);
    }

    private static List<PersistedIngestionSource> NormalizeSources(IEnumerable<PersistedIngestionSource> sources)
    {
        return sources
            .Where(source => !string.IsNullOrWhiteSpace(source.Key))
            .Select(source => new PersistedIngestionSource
            {
                Key = source.Key,
                DisplayName = DefaultIfEmpty(source.DisplayName, source.Key),
                Enabled = source.Enabled,
                SyncSchedule = DefaultIfEmpty(source.SyncSchedule, DefaultDefenderSchedule),
                Credentials = new PersistedSourceCredentials
                {
                    TenantId = source.Credentials?.TenantId ?? string.Empty,
                    ClientId = source.Credentials?.ClientId ?? string.Empty,
                    ClientSecret = source.Credentials?.ClientSecret ?? string.Empty,
                    SecretRef = source.Credentials?.SecretRef ?? string.Empty,
                    ApiBaseUrl = DefaultIfEmpty(
                        source.Credentials?.ApiBaseUrl,
                        DefaultDefenderApiBaseUrl
                    ),
                    TokenScope = DefaultIfEmpty(
                        source.Credentials?.TokenScope,
                        DefaultDefenderTokenScope
                    ),
                },
                Runtime = source.Runtime ?? new PersistedIngestionRuntimeState(),
            })
            .OrderBy(source => source.DisplayName)
            .ToList();
    }

    private static JsonObject ParseSettingsObject(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return [];

        try
        {
            return JsonNode.Parse(settings) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string DefaultIfEmpty(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

public class PersistedIngestionSource
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string SyncSchedule { get; set; } = string.Empty;
    public PersistedSourceCredentials? Credentials { get; set; }
    public PersistedIngestionRuntimeState Runtime { get; set; } = new();
}

public class PersistedSourceCredentials
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string SecretRef { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string TokenScope { get; set; } = string.Empty;
}

public class PersistedIngestionRuntimeState
{
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public string LastStatus { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
}
