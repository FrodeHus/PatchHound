using System.Net.Http.Json;
using PatchHound.Core.Interfaces;

namespace PatchHound.Infrastructure.Services;

public class HttpEventPusher : IEventPusher
{
    private readonly HttpClient _httpClient;

    public HttpEventPusher(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("http://frontend:3000");
    }

    public async Task PushAsync(string eventName, object data, string? userId = null, CancellationToken ct = default)
    {
        var payload = new { @event = eventName, data, userId };
        await _httpClient.PostAsJsonAsync("/api/internal/events", payload, ct);
    }
}
