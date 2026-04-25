using System.Text.Json.Serialization;

namespace PatchHound.Infrastructure.Services;

internal class NvdApiResponse
{
    [JsonPropertyName("resultsPerPage")]
    public int ResultsPerPage { get; set; }

    [JsonPropertyName("startIndex")]
    public int StartIndex { get; set; }

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("vulnerabilities")]
    public List<NvdApiVulnerabilityItem> Vulnerabilities { get; set; } = [];
}

internal class NvdApiVulnerabilityItem
{
    [JsonPropertyName("cve")]
    public NvdApiCve? Cve { get; set; }
}

internal class NvdApiCve
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("published")]
    public string? Published { get; set; }

    [JsonPropertyName("lastModified")]
    public string? LastModified { get; set; }

    [JsonPropertyName("descriptions")]
    public List<NvdApiDescription> Descriptions { get; set; } = [];

    [JsonPropertyName("metrics")]
    public NvdApiMetrics? Metrics { get; set; }

    [JsonPropertyName("references")]
    public List<NvdApiReference> References { get; set; } = [];

    [JsonPropertyName("configurations")]
    public List<NvdApiConfiguration> Configurations { get; set; } = [];
}

internal class NvdApiDescription
{
    [JsonPropertyName("lang")]
    public string Lang { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

internal class NvdApiMetrics
{
    [JsonPropertyName("cvssMetricV31")]
    public List<NvdApiCvssMetric> CvssMetricV31 { get; set; } = [];

    [JsonPropertyName("cvssMetricV30")]
    public List<NvdApiCvssMetric> CvssMetricV30 { get; set; } = [];
}

internal class NvdApiCvssMetric
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("cvssData")]
    public NvdApiCvssData? CvssData { get; set; }
}

internal class NvdApiCvssData
{
    [JsonPropertyName("baseScore")]
    public decimal BaseScore { get; set; }

    [JsonPropertyName("vectorString")]
    public string? VectorString { get; set; }
}

internal class NvdApiReference
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

internal class NvdApiConfiguration
{
    [JsonPropertyName("nodes")]
    public List<NvdApiNode> Nodes { get; set; } = [];
}

internal class NvdApiNode
{
    [JsonPropertyName("cpeMatch")]
    public List<NvdApiCpeMatch> CpeMatch { get; set; } = [];

    [JsonPropertyName("nodes")]
    public List<NvdApiNode> Children { get; set; } = [];
}

internal class NvdApiCpeMatch
{
    [JsonPropertyName("vulnerable")]
    public bool Vulnerable { get; set; }

    [JsonPropertyName("criteria")]
    public string Criteria { get; set; } = string.Empty;

    [JsonPropertyName("versionStartIncluding")]
    public string? VersionStartIncluding { get; set; }

    [JsonPropertyName("versionStartExcluding")]
    public string? VersionStartExcluding { get; set; }

    [JsonPropertyName("versionEndIncluding")]
    public string? VersionEndIncluding { get; set; }

    [JsonPropertyName("versionEndExcluding")]
    public string? VersionEndExcluding { get; set; }
}
