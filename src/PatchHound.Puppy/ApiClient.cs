using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PatchHound.Puppy;

public interface IRunnerApiClient
{
    Task SendHeartbeatAsync(CancellationToken ct);
    Task<JobPayload?> GetNextJobAsync(CancellationToken ct);
    Task SendJobHeartbeatAsync(Guid jobId, CancellationToken ct);
    Task PostResultAsync(Guid jobId, string status, string stdout, string stderr,
        string? errorMessage, CancellationToken ct);
}

public class ApiClient : IRunnerApiClient
{
    private readonly HttpClient _http;
    private readonly RunnerOptions _options;
    private readonly string _version;

    public ApiClient(HttpClient httpClient, RunnerOptions options)
    {
        _http = httpClient;
        _options = options;
        _version = GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0";

        _http.BaseAddress = new Uri(options.CentralUrl.TrimEnd('/'));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.BearerToken);
    }

    public async Task SendHeartbeatAsync(CancellationToken ct)
    {
        var request = new HeartbeatRequest(_version, _options.Hostname);
        var response = await _http.PostAsJsonAsync("/api/scan-runner/heartbeat", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JobPayload?> GetNextJobAsync(CancellationToken ct)
    {
        var response = await _http.GetAsync("/api/scan-runner/jobs/next", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JobPayload>(ct);
    }

    public async Task SendJobHeartbeatAsync(Guid jobId, CancellationToken ct)
    {
        var response = await _http.PostAsync(
            $"/api/scan-runner/jobs/{jobId}/heartbeat", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PostResultAsync(
        Guid jobId, string status, string stdout, string stderr,
        string? errorMessage, CancellationToken ct)
    {
        var request = new PostResultRequest(status, stdout, stderr, errorMessage);
        var response = await _http.PostAsJsonAsync(
            $"/api/scan-runner/jobs/{jobId}/result", request, ct);
        response.EnsureSuccessStatusCode();
    }
}
