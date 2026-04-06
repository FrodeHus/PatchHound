using System.Net;
using System.Text.Json;
using PatchHound.Puppy;
using Xunit;

namespace PatchHound.Tests.Puppy;

public class ApiClientTests
{
    private static readonly RunnerOptions DefaultOptions = new()
    {
        CentralUrl = "https://patchhound.example.com",
        BearerToken = "test-token"
    };

    private static readonly string AssemblyVersion =
        typeof(ApiClient).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private ApiClient CreateClient(HttpClient httpClient)
    {
        return new ApiClient(httpClient, DefaultOptions);
    }

    [Fact]
    public async Task SendHeartbeatAsync_posts_to_correct_endpoint()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"ok":true}""");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        await client.SendHeartbeatAsync(CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/scan-runner/heartbeat", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal($"Bearer {DefaultOptions.BearerToken}",
            handler.LastRequest.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task GetNextJobAsync_returns_null_on_204()
    {
        var handler = new FakeHandler(HttpStatusCode.NoContent, "");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        var result = await client.GetNextJobAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetNextJobAsync_deserializes_job_payload()
    {
        var payload = new JobPayload(
            Guid.NewGuid(), Guid.NewGuid(),
            new HostTarget("host.example.com", 22, "admin", "password"),
            new JobCredentials("s3cret", null, null),
            null,
            [new ToolPayload(Guid.NewGuid(), "tool-1", "python", "/usr/bin/python3",
                300, "print('hi')", "NormalizedSoftware")],
            DateTimeOffset.UtcNow.AddMinutes(10));

        var json = JsonSerializer.Serialize(payload);
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        var result = await client.GetNextJobAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(payload.JobId, result.JobId);
        Assert.Equal("host.example.com", result.HostTarget.Host);
        Assert.Single(result.Tools);
    }

    [Fact]
    public async Task SendJobHeartbeatAsync_posts_to_correct_endpoint()
    {
        var jobId = Guid.NewGuid();
        var handler = new FakeHandler(HttpStatusCode.OK, """{"ok":true}""");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        await client.SendJobHeartbeatAsync(jobId, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal($"/api/scan-runner/jobs/{jobId}/heartbeat",
            handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PostResultAsync_sends_result_body()
    {
        var jobId = Guid.NewGuid();
        var handler = new FakeHandler(HttpStatusCode.OK, """{"ok":true}""");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        await client.PostResultAsync(jobId, "Succeeded", "stdout", "stderr", null,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal($"/api/scan-runner/jobs/{jobId}/result",
            handler.LastRequest.RequestUri!.AbsolutePath);

        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        var parsed = JsonDocument.Parse(body);
        Assert.Equal("Succeeded", parsed.RootElement.GetProperty("status").GetString());
        Assert.Equal("stdout", parsed.RootElement.GetProperty("stdout").GetString());
    }

    [Fact]
    public async Task GetNextJobAsync_throws_on_server_error()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, "oops");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetNextJobAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SendHeartbeatAsync_includes_version_and_hostname()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"ok":true}""");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        await client.SendHeartbeatAsync(CancellationToken.None);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        var parsed = JsonDocument.Parse(body);
        Assert.Equal(DefaultOptions.Hostname, parsed.RootElement.GetProperty("hostname").GetString());
        Assert.Equal(AssemblyVersion, parsed.RootElement.GetProperty("version").GetString());
    }

    /// <summary>
    /// Minimal HttpMessageHandler that records the last request and returns a canned response.
    /// </summary>
    private class FakeHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
