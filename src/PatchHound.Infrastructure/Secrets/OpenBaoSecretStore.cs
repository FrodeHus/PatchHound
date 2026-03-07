using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PatchHound.Infrastructure.Options;

namespace PatchHound.Infrastructure.Secrets;

public class OpenBaoSecretStore : ISecretStore
{
    private readonly HttpClient _httpClient;
    private readonly OpenBaoOptions _options;
    private readonly ILogger<OpenBaoSecretStore> _logger;

    public OpenBaoSecretStore(
        HttpClient httpClient,
        IOptions<OpenBaoOptions> options,
        ILogger<OpenBaoSecretStore> logger
    )
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.Address);
    }

    public async Task<string?> GetSecretAsync(string path, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            _logger.LogWarning("OpenBao token is not configured. Secret reads are disabled.");
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/{_options.KvMount}/data/{path}"
        );
        request.Headers.Add("X-Vault-Token", _options.Token);

        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OpenBaoKvResponse>(cancellationToken: ct);
        return payload?.Data.Data.GetValueOrDefault(key);
    }

    public async Task PutSecretAsync(
        string path,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException(
                "OpenBao token is not configured. Secret writes are disabled."
            );
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/{_options.KvMount}/data/{path}"
        );
        request.Headers.Add("X-Vault-Token", _options.Token);
        request.Content = JsonContent.Create(new { data = values });

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private sealed class OpenBaoKvResponse
    {
        public OpenBaoKvData Data { get; set; } = new();
    }

    private sealed class OpenBaoKvData
    {
        public Dictionary<string, string> Data { get; set; } = [];
    }
}
