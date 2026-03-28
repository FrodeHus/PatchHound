using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Interfaces;

namespace PatchHound.Infrastructure.Services;

public class HttpEventPusher : IEventPusher
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpEventPusher> _logger;
    private readonly string? _internalToken;

    public HttpEventPusher(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<HttpEventPusher> logger
    )
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(
            configuration["Frontend:InternalUrl"] ?? "http://frontend:3000"
        );
        _internalToken = configuration["Frontend:InternalEventSecret"];
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_internalToken))
        {
            _logger.LogWarning(
                "Frontend:InternalEventSecret is not configured. Event pushing is disabled"
            );
        }
    }

    public async Task PushAsync(
        string eventName,
        object data,
        string? userId = null,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(_internalToken))
        {
            _logger.LogDebug(
                "Skipping event push for {EventName}: internal event secret is not configured",
                eventName
            );
            return;
        }

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

        request.Headers.Add("X-Internal-Token", _internalToken);
        await _httpClient.SendAsync(request, ct);
    }
}
