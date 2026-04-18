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
        if (cveId.Length > 64)
            throw new ArgumentException("CveId must not exceed 64 characters.", nameof(cveId));
        if (cvssVector is not null && cvssVector.Length > 256)
            throw new ArgumentException("CvssVector must not exceed 256 characters.", nameof(cvssVector));
        if (referencesJson is null)
            throw new ArgumentNullException(nameof(referencesJson));
        if (configurationsJson is null)
            throw new ArgumentNullException(nameof(configurationsJson));

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
        if (cvssVector is not null && cvssVector.Length > 256)
            throw new ArgumentException("CvssVector must not exceed 256 characters.", nameof(cvssVector));
        if (referencesJson is null)
            throw new ArgumentNullException(nameof(referencesJson));
        if (configurationsJson is null)
            throw new ArgumentNullException(nameof(configurationsJson));

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
