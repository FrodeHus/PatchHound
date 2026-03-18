using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using StackExchange.Redis;

namespace PatchHound.Infrastructure.Services;

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

    public async Task PreWarmTenantVulnerabilitiesAsync(
        IReadOnlyList<TenantVulnerability> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}tv:{item.VulnerabilityDefinition.ExternalId}";
            var value = JsonSerializer.Serialize(new CachedTenantVulnerability(
                item.Id, item.Status, item.VulnerabilityDefinitionId));
            tasks.Add(batch.StringSetAsync(key, value, KeyExpiry));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmDefinitionsAsync(
        IReadOnlyList<VulnerabilityDefinition> items,
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
        IReadOnlyList<Asset> items,
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

    public async Task PreWarmProjectionsAsync(
        IReadOnlyList<VulnerabilityAsset> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}va:{item.TenantVulnerabilityId:N}:{item.AssetId:N}";
            tasks.Add(batch.StringSetAsync(key, "1", KeyExpiry));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmOpenEpisodesAsync(
        IReadOnlyList<VulnerabilityAssetEpisode> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}ep:{item.TenantVulnerabilityId:N}:{item.AssetId:N}";
            var value = JsonSerializer.Serialize(new CachedEpisode(item.Id, item.EpisodeNumber));
            tasks.Add(batch.StringSetAsync(key, value, KeyExpiry));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmLatestEpisodeNumbersAsync(
        IReadOnlyList<(Guid TenantVulnerabilityId, Guid AssetId, int EpisodeNumber)> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}epmax:{item.TenantVulnerabilityId:N}:{item.AssetId:N}";
            tasks.Add(batch.StringSetAsync(key, item.EpisodeNumber.ToString(), KeyExpiry));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmAssessmentsAsync(
        IReadOnlyList<VulnerabilityAssetAssessment> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}assess:{item.TenantVulnerabilityId:N}:{item.AssetId:N}";
            tasks.Add(batch.StringSetAsync(key, "1", KeyExpiry));
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

    internal sealed record CachedTenantVulnerability(Guid Id, VulnerabilityStatus Status, Guid DefinitionId);
    internal sealed record CachedDefinition(Guid Id, string ExternalId, string Source);
    internal sealed record CachedAsset(Guid Id, string ExternalId);
    internal sealed record CachedEpisode(Guid Id, int EpisodeNumber);
}
