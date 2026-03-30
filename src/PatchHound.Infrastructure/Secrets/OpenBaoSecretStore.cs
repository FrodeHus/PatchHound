using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PatchHound.Infrastructure.Options;

namespace PatchHound.Infrastructure.Secrets;

public class OpenBaoSecretStore : ISecretStore
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<OpenBaoOptions> _optionsMonitor;
    private readonly ILogger<OpenBaoSecretStore> _logger;

    public OpenBaoSecretStore(
        HttpClient httpClient,
        IOptionsMonitor<OpenBaoOptions> optionsMonitor,
        ILogger<OpenBaoSecretStore> logger
    )
    {
        _httpClient = httpClient;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(optionsMonitor.CurrentValue.Address);
    }

    private OpenBaoOptions Options => _optionsMonitor.CurrentValue;

    public async Task<string?> GetSecretAsync(string path, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Options.Token))
        {
            _logger.LogWarning("OpenBao token is not configured. Secret reads are disabled.");
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/{Options.KvMount}/data/{path}"
        );
        request.Headers.Add("X-Vault-Token", Options.Token);

        using var response = await SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        var payload = await response.Content.ReadFromJsonAsync<OpenBaoKvResponse>(
            cancellationToken: ct
        );
        return payload?.Data.Data.GetValueOrDefault(key);
    }

    public async Task PutSecretAsync(
        string path,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(Options.Token))
        {
            throw new InvalidOperationException(
                "OpenBao token is not configured. Secret writes are disabled."
            );
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/{Options.KvMount}/data/{path}"
        );
        request.Headers.Add("X-Vault-Token", Options.Token);
        request.Content = JsonContent.Create(new { data = values });

        using var response = await SendAsync(request, ct);
    }

    public async Task DeleteSecretPathAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Options.Token))
        {
            throw new InvalidOperationException(
                "OpenBao token is not configured. Secret deletes are disabled."
            );
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/v1/{Options.KvMount}/metadata/{path}"
        );
        request.Headers.Add("X-Vault-Token", Options.Token);

        using var response = await SendAsync(request, ct);
    }

    public async Task<OpenBaoStatus> GetStatusAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/v1/sys/seal-status", ct);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<OpenBaoSealStatusResponse>(
                cancellationToken: ct
            );
            return new OpenBaoStatus(true, payload?.Initialized ?? false, payload?.Sealed ?? true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Unable to retrieve OpenBao seal status.");
            return new OpenBaoStatus(false, false, true);
        }
    }

    public async Task<OpenBaoStatus> UnsealAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0)
        {
            throw new ArgumentException("At least one unseal key is required.", nameof(keys));
        }

        OpenBaoSealStatusResponse? lastPayload = null;

        foreach (var key in keys.Where(key => !string.IsNullOrWhiteSpace(key)))
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "/v1/sys/unseal",
                new { key = key.Trim() },
                ct
            );

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "OpenBao unseal request failed. StatusCode: {StatusCode}.",
                    (int)response.StatusCode
                );
                throw new SecretStoreUnavailableException(
                    $"OpenBao unseal returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                    response.StatusCode
                );
            }

            lastPayload = await response.Content.ReadFromJsonAsync<OpenBaoSealStatusResponse>(
                cancellationToken: ct
            );

            if (lastPayload is { Sealed: false })
            {
                break;
            }
        }

        if (lastPayload is null)
        {
            return await GetStatusAsync(ct);
        }

        return new OpenBaoStatus(true, lastPayload.Initialized, lastPayload.Sealed);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            // 404 is a valid "not found" response for GET and DELETE requests
            if (
                response.StatusCode == System.Net.HttpStatusCode.NotFound
                && (request.Method == HttpMethod.Get || request.Method == HttpMethod.Delete)
            )
            {
                return response;
            }

            // Surface a clear error when the token is expired or revoked
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogError(
                    "OpenBao returned {StatusCode} for {Path}. The Vault token may be expired or revoked. "
                    + "Restart the application or update the OpenBao:Token configuration to restore secret access.",
                    (int)response.StatusCode,
                    request.RequestUri?.PathAndQuery
                );
                throw new SecretStoreUnavailableException(
                    $"OpenBao authentication failed ({(int)response.StatusCode}). The Vault token may be expired or revoked.",
                    response.StatusCode
                );
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "OpenBao request failed. Method: {Method}. Path: {Path}. StatusCode: {StatusCode}. Response: {Response}",
                request.Method,
                request.RequestUri?.PathAndQuery,
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(responseBody) ? "<empty>" : responseBody
            );

            throw new SecretStoreUnavailableException(
                $"OpenBao returned {(int)response.StatusCode} {response.ReasonPhrase} for {request.RequestUri?.PathAndQuery}.",
                response.StatusCode
            );
        }
        catch (HttpRequestException ex)
        {
            throw new SecretStoreUnavailableException("OpenBao could not be reached.", null, ex);
        }
    }

    private sealed class OpenBaoKvResponse
    {
        public OpenBaoKvData Data { get; set; } = new();
    }

    private sealed class OpenBaoSealStatusResponse
    {
        public bool Initialized { get; set; }
        public bool Sealed { get; set; }
    }

    private sealed class OpenBaoKvData
    {
        public Dictionary<string, string> Data { get; set; } = [];
    }
}
