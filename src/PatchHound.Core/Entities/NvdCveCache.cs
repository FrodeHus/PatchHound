using System.Text.Json.Serialization;

namespace PatchHound.Core.Entities;

public class NvdCveCache
{
    public string CveId { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal? CvssScore { get; private set; }
    public string? CvssVector { get; private set; }
    public DateTimeOffset? PublishedDate { get; private set; }
    public DateTimeOffset FeedLastModified { get; private set; }
    public string ReferencesJson { get; private set; } = "[]";
    public string ConfigurationsJson { get; private set; } = "[]";
    public DateTimeOffset CachedAt { get; private set; }

    private NvdCveCache() { }

    public static NvdCveCache Create(
        string cveId,
        string description,
        decimal? cvssScore,
        string? cvssVector,
        DateTimeOffset? publishedDate,
        DateTimeOffset feedLastModified,
        string referencesJson,
        string configurationsJson)
    {
        if (string.IsNullOrWhiteSpace(cveId))
            throw new ArgumentException("CveId is required.", nameof(cveId));
        return new NvdCveCache
        {
            CveId = cveId,
            Description = description,
            CvssScore = cvssScore,
            CvssVector = cvssVector,
            PublishedDate = publishedDate,
            FeedLastModified = feedLastModified,
            ReferencesJson = referencesJson,
            ConfigurationsJson = configurationsJson,
            CachedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        string description,
        decimal? cvssScore,
        string? cvssVector,
        DateTimeOffset? publishedDate,
        DateTimeOffset feedLastModified,
        string referencesJson,
        string configurationsJson)
    {
        Description = description;
        CvssScore = cvssScore;
        CvssVector = cvssVector;
        PublishedDate = publishedDate;
        FeedLastModified = feedLastModified;
        ReferencesJson = referencesJson;
        ConfigurationsJson = configurationsJson;
        CachedAt = DateTimeOffset.UtcNow;
    }
}

// Serialised into NvdCveCache.ReferencesJson
public record NvdCachedReference(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("tags")] List<string> Tags);

// Serialised into NvdCveCache.ConfigurationsJson
public record NvdCachedCpeMatch(
    [property: JsonPropertyName("vulnerable")] bool Vulnerable,
    [property: JsonPropertyName("criteria")] string Criteria,
    [property: JsonPropertyName("vsi")] string? VersionStartIncluding,
    [property: JsonPropertyName("vse")] string? VersionStartExcluding,
    [property: JsonPropertyName("vei")] string? VersionEndIncluding,
    [property: JsonPropertyName("vee")] string? VersionEndExcluding);
