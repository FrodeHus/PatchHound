using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PatchHound.Core.Interfaces;

namespace PatchHound.Infrastructure.Services;

public class HttpEventPusher : IEventPusher
{
    private readonly HttpClient _httpClient;
    private readonly string? _internalToken;

    public HttpEventPusher(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(
            configuration["Frontend:InternalUrl"] ?? "http://frontend:3000"
        );
        _internalToken = configuration["Frontend:InternalEventSecret"];
    }

    public async Task PushAsync(
        string eventName,
        object data,
        string? userId = null,
        CancellationToken ct = default
    )
    {
        var payload = new
        {
            @event = eventName,
            data,
            userId,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/events")
        {
            Content = JsonContent.Create(payload),
        };

        if (!string.IsNullOrWhiteSpace(_internalToken))
        {
            request.Headers.Add("X-Internal-Token", _internalToken);
        }

        await _httpClient.SendAsync(request, ct);
    }
}
