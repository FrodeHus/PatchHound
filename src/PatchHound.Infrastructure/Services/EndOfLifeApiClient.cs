using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PatchHound.Infrastructure.Services;

public class EndOfLifeApiClient(HttpClient httpClient)
{
    public virtual async Task<EndOfLifeProductResponse?> GetProductAsync(
        string apiBaseUrl,
        string productSlug,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(
                new Uri(apiBaseUrl.TrimEnd('/')),
                $"/api/v1/products/{Uri.EscapeDataString(productSlug)}"
            )
        );
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EndOfLifeProductResponse>(
            cancellationToken: ct
        );
    }
}

public class EndOfLifeProductResponse
{
    [JsonPropertyName("result")]
    public EndOfLifeProduct? Result { get; set; }
}

public class EndOfLifeProduct
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("releases")]
    public List<EndOfLifeRelease> Releases { get; set; } = [];
}

public class EndOfLifeRelease
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("isLts")]
    public bool IsLts { get; set; }

    [JsonPropertyName("isEol")]
    public bool IsEol { get; set; }

    [JsonPropertyName("eolFrom")]
    public string? EolFrom { get; set; }

    [JsonPropertyName("isEoas")]
    public bool IsEoas { get; set; }

    [JsonPropertyName("eoasFrom")]
    public string? EoasFrom { get; set; }

    [JsonPropertyName("isMaintained")]
    public bool IsMaintained { get; set; }

    [JsonPropertyName("latest")]
    public EndOfLifeLatestVersion? Latest { get; set; }
}

public class EndOfLifeLatestVersion
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}
