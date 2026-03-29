using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Infrastructure.Services;

public sealed class SentinelConnectorWorker : BackgroundService
{
    private const int MaxBatchSize = 100;
    private static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ConfigPollInterval = TimeSpan.FromSeconds(30);
    private static readonly string[] MonitorScope = ["https://monitor.azure.com/.default"];

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SentinelAuditQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SentinelConnectorWorker> _logger;

    private IConfidentialClientApplication? _msalApp;
    private string _cachedClientId = string.Empty;
    private string _cachedTenantId = string.Empty;

    public SentinelConnectorWorker(
        SentinelAuditQueue queue,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<SentinelConnectorWorker> logger
    )
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SentinelConnectorWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = await LoadConfigAsync(stoppingToken);

            if (config is null || !config.Enabled)
            {
                await DrainAsync(stoppingToken);
                await DelayOrDrain(ConfigPollInterval, stoppingToken);
                continue;
            }

            var batch = await CollectBatchAsync(stoppingToken);
            if (batch.Count == 0)
                continue;

            await PushBatchAsync(config, batch, stoppingToken);
        }
    }

    private async Task<List<SentinelAuditEvent>> CollectBatchAsync(CancellationToken ct)
    {
        var batch = new List<SentinelAuditEvent>(MaxBatchSize);
        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        batchCts.CancelAfter(BatchWindow);

        try
        {
            await foreach (var item in _queue.ReadAllAsync(batchCts.Token))
            {
                batch.Add(item);
                if (batch.Count >= MaxBatchSize)
                    break;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Batch window expired — return what we have
        }

        return batch;
    }

    private async Task PushBatchAsync(
        SentinelConnectorConfiguration config,
        List<SentinelAuditEvent> batch,
        CancellationToken ct
    )
    {
        try
        {
            var token = await AcquireTokenAsync(config, ct);
            var url =
                $"{config.DceEndpoint.TrimEnd('/')}/dataCollectionRules/{config.DcrImmutableId}"
                + $"/streams/{config.StreamName}?api-version=2023-01-01";

            var payload = batch
                .Select(e => new
                {
                    TimeGenerated = e.Timestamp.UtcDateTime.ToString("O"),
                    TenantId = e.TenantId.ToString(),
                    e.EntityType,
                    EntityId = e.EntityId.ToString(),
                    e.Action,
                    e.OldValues,
                    e.NewValues,
                    UserId = e.UserId.ToString(),
                    AuditEntryId = e.AuditEntryId.ToString(),
                })
                .ToList();

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, PayloadJsonOptions);

            using var client = _httpClientFactory.CreateClient("SentinelConnector");
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new ByteArrayContent(jsonBytes),
            };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/json"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Sentinel push failed with {StatusCode}: {Body}. Dropped {Count} events",
                    response.StatusCode,
                    body.Length > 500 ? body[..500] : body,
                    batch.Count
                );
            }
            else
            {
                _logger.LogDebug("Pushed {Count} audit events to Sentinel", batch.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to push {Count} audit events to Sentinel", batch.Count);
        }
    }

    private async Task<string> AcquireTokenAsync(
        SentinelConnectorConfiguration config,
        CancellationToken ct
    )
    {
        if (
            _msalApp is null
            || _cachedClientId != config.ClientId
            || _cachedTenantId != config.TenantId
        )
        {
            var secret = await LoadClientSecretAsync(config, ct);
            _msalApp = ConfidentialClientApplicationBuilder
                .Create(config.ClientId)
                .WithClientSecret(secret)
                .WithAuthority($"https://login.microsoftonline.com/{config.TenantId}")
                .Build();
            _cachedClientId = config.ClientId;
            _cachedTenantId = config.TenantId;
        }

        var result = await _msalApp.AcquireTokenForClient(MonitorScope).ExecuteAsync(ct);
        return result.AccessToken;
    }

    private async Task<string> LoadClientSecretAsync(
        SentinelConnectorConfiguration config,
        CancellationToken ct
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var secretStore = scope.ServiceProvider.GetRequiredService<ISecretStore>();
        var value = await secretStore.GetSecretAsync(config.SecretRef, "clientSecret", ct);
        return value
            ?? throw new InvalidOperationException(
                $"Client secret not found at vault path '{config.SecretRef}'"
            );
    }

    private async Task<SentinelConnectorConfiguration?> LoadConfigAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
            return await dbContext
                .SentinelConnectorConfigurations.AsNoTracking()
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load Sentinel connector configuration");
            return null;
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        drainCts.CancelAfter(TimeSpan.FromMilliseconds(100));
        try
        {
            await foreach (var _ in _queue.ReadAllAsync(drainCts.Token)) { }
        }
        catch (OperationCanceledException) { }
    }

    private async Task DelayOrDrain(TimeSpan delay, CancellationToken ct)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delayCts.CancelAfter(delay);
        try
        {
            await foreach (var _ in _queue.ReadAllAsync(delayCts.Token)) { }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}
