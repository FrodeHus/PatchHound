using System.Text.Json.Serialization;

namespace PatchHound.Infrastructure.Services;

internal class NvdFeedResponse
{
    [JsonPropertyName("CVE_Items")]
    public List<NvdFeedItem> Items { get; set; } = [];
}

internal class NvdFeedItem
{
    [JsonPropertyName("cve")]
    public NvdFeedCveSection? Cve { get; set; }

    [JsonPropertyName("configurations")]
    public NvdFeedConfigurations? Configurations { get; set; }

    [JsonPropertyName("impact")]
    public NvdFeedImpact? Impact { get; set; }

    [JsonPropertyName("publishedDate")]
    public string? PublishedDate { get; set; }

    [JsonPropertyName("lastModifiedDate")]
    public string? LastModifiedDate { get; set; }
}

internal class NvdFeedCveSection
{
    [JsonPropertyName("CVE_data_meta")]
    public NvdFeedMeta? Meta { get; set; }

    [JsonPropertyName("description")]
    public NvdFeedDescriptionWrapper? Description { get; set; }

    [JsonPropertyName("references")]
    public NvdFeedReferencesWrapper? References { get; set; }
}

internal class NvdFeedMeta
{
    [JsonPropertyName("ID")]
    public string Id { get; set; } = string.Empty;
}

internal class NvdFeedDescriptionWrapper
{
    [JsonPropertyName("description_data")]
    public List<NvdFeedDescriptionItem> Data { get; set; } = [];
}

internal class NvdFeedDescriptionItem
{
    [JsonPropertyName("lang")]
    public string Lang { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

internal class NvdFeedReferencesWrapper
{
    [JsonPropertyName("reference_data")]
    public List<NvdFeedReferenceItem> Data { get; set; } = [];
}

internal class NvdFeedReferenceItem
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("refsource")]
    public string RefSource { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

internal class NvdFeedConfigurations
{
    [JsonPropertyName("nodes")]
    public List<NvdFeedNode> Nodes { get; set; } = [];
}

internal class NvdFeedNode
{
    [JsonPropertyName("cpe_match")]
    public List<NvdFeedCpeMatch> CpeMatch { get; set; } = [];

    [JsonPropertyName("children")]
    public List<NvdFeedNode> Children { get; set; } = [];
}

internal class NvdFeedCpeMatch
{
    [JsonPropertyName("vulnerable")]
    public bool Vulnerable { get; set; }

    [JsonPropertyName("cpe23Uri")]
    public string Cpe23Uri { get; set; } = string.Empty;

    [JsonPropertyName("versionStartIncluding")]
    public string? VersionStartIncluding { get; set; }

    [JsonPropertyName("versionStartExcluding")]
    public string? VersionStartExcluding { get; set; }

    [JsonPropertyName("versionEndIncluding")]
    public string? VersionEndIncluding { get; set; }

    [JsonPropertyName("versionEndExcluding")]
    public string? VersionEndExcluding { get; set; }
}

internal class NvdFeedImpact
{
    [JsonPropertyName("baseMetricV3")]
    public NvdFeedBaseMetricV3? BaseMetricV3 { get; set; }
}

internal class NvdFeedBaseMetricV3
{
    [JsonPropertyName("cvssV3")]
    public NvdFeedCvssV3? CvssV3 { get; set; }
}

internal class NvdFeedCvssV3
{
    [JsonPropertyName("baseScore")]
    public decimal BaseScore { get; set; }

    [JsonPropertyName("vectorString")]
    public string? VectorString { get; set; }
}
