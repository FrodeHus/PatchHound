using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
using StackExchange.Redis;

namespace PatchHound.Infrastructure.Services;

/// <summary>
/// Scoped per-ingestion-run state cache. Registered as <c>AddScoped</c> — one instance
/// per DI scope, not a singleton. Each <see cref="IngestionService"/> scope gets its own
/// clean instance; concurrent ingestion runs for different tenants do not share state.
/// </summary>
public class IngestionStateCache
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<IngestionStateCache> _logger;
    private string _keyPrefix = string.Empty;
    private static readonly TimeSpan KeyExpiry = TimeSpan.FromHours(2);

    public bool IsAvailable => _redis?.IsConnected == true;

    public IngestionStateCache(
        IConnectionMultiplexer? redis = null,
        ILogger<IngestionStateCache>? logger = null)
    {
        _redis = redis;
        _logger = logger ?? NullLogger<IngestionStateCache>.Instance;
    }

    public void SetScope(Guid tenantId, Guid runId)
    {
        _keyPrefix = $"ingestion:{tenantId:N}:{runId:N}:";
    }

    /// <summary>
    /// Pre-warm canonical vulnerability lookup. Key: <c>vd:{ExternalId}</c>.
    /// Replaces the legacy <c>PreWarmTenantVulnerabilitiesAsync</c> and
    /// <c>PreWarmDefinitionsAsync</c> (both keyed on ExternalId).
    /// </summary>
    public async Task PreWarmVulnerabilitiesAsync(
        IReadOnlyList<Vulnerability> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}vd:{item.ExternalId}";
            var value = JsonSerializer.Serialize(new CachedDefinition(item.Id, item.ExternalId, item.Source));
            tasks.Add(batch.StringSetAsync(key, value, KeyExpiry));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmAssetsAsync(
        IReadOnlyList<Device> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}asset:{item.ExternalId}";
            var value = JsonSerializer.Serialize(new CachedAsset(item.Id, item.ExternalId));
            tasks.Add(batch.StringSetAsync(key, value, KeyExpiry));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        if (!IsAvailable || string.IsNullOrEmpty(_keyPrefix)) return;
        try
        {
            var server = _redis!.GetServers().FirstOrDefault();
            if (server is null) return;
            var keys = server.Keys(pattern: $"{_keyPrefix}*").ToArray();
            if (keys.Length > 0)
            {
                var db = _redis.GetDatabase();
                await db.KeyDeleteAsync(keys);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to clean up Redis ingestion cache keys with prefix {Prefix}",
                _keyPrefix);
        }
    }

    internal sealed record CachedDefinition(Guid Id, string ExternalId, string Source);
    internal sealed record CachedAsset(Guid Id, string ExternalId);
}
