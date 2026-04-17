using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.Identity;

namespace PatchHound.Infrastructure.CredentialSources;

public class EntraGraphApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, ClientSecretCredential> _credentialCache = new();

    public EntraGraphApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public virtual async Task<List<GraphApplication>> GetApplicationsAsync(
        EntraClientConfiguration configuration,
        CancellationToken ct
    )
    {
        const string select = "id,displayName,description,passwordCredentials,keyCredentials";
        var path = $"/v1.0/applications?$select={select}";
        var results = new List<GraphApplication>();

        while (path is not null)
        {
            using var response = await SendGetAsync(configuration, path, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content.ReadFromJsonAsync<GraphPagedResponse<GraphApplication>>(ct)
                ?? new GraphPagedResponse<GraphApplication>();

            results.AddRange(page.Value);
            path = page.NextLink is not null
                ? new Uri(page.NextLink).PathAndQuery
                : null;
        }

        return results;
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        EntraClientConfiguration configuration,
        string path,
        CancellationToken ct
    )
    {
        var token = await GetAccessTokenAsync(configuration, ct);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(configuration.ApiBaseUrl.TrimEnd('/')), path)
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    protected virtual async Task<string> GetAccessTokenAsync(
        EntraClientConfiguration configuration,
        CancellationToken ct
    )
    {
        var cacheKey = $"{configuration.TenantId}:{configuration.ClientId}";
        var credential = _credentialCache.GetOrAdd(
            cacheKey,
            _ => new ClientSecretCredential(
                configuration.TenantId,
                configuration.ClientId,
                configuration.ClientSecret
            )
        );

        var token = await credential.GetTokenAsync(
            new Azure.Core.TokenRequestContext([configuration.TokenScope]),
            ct
        );

        return token.Token;
    }
}

public record EntraClientConfiguration(
    string TenantId,
    string ClientId,
    string ClientSecret,
    string ApiBaseUrl,
    string TokenScope
);

public class GraphPagedResponse<TItem>
{
    [JsonPropertyName("value")]
    public List<TItem> Value { get; set; } = [];

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }
}

public class GraphApplication
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("passwordCredentials")]
    public List<GraphPasswordCredential> PasswordCredentials { get; set; } = [];

    [JsonPropertyName("keyCredentials")]
    public List<GraphKeyCredential> KeyCredentials { get; set; } = [];
}

public class GraphPasswordCredential
{
    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("endDateTime")]
    public DateTimeOffset? EndDateTime { get; set; }
}

public class GraphKeyCredential
{
    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("endDateTime")]
    public DateTimeOffset? EndDateTime { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
