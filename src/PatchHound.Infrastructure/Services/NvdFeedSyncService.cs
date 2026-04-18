using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class NvdFeedSyncService(
    HttpClient httpClient,
    PatchHoundDbContext db,
    ILogger<NvdFeedSyncService> logger)
{
    private const string FeedBaseUrl = "https://nvd.nist.gov/feeds/json/cve/1.1";

    public Task SyncYearFeedAsync(int year, CancellationToken ct) =>
        SyncFeedAsync(
            feedName: year.ToString(),
            feedUrl: $"{FeedBaseUrl}/nvdcve-1.1-{year}.json.gz",
            ct);

    public Task SyncModifiedFeedAsync(CancellationToken ct) =>
        SyncFeedAsync(
            feedName: "modified",
            feedUrl: $"{FeedBaseUrl}/nvdcve-1.1-modified.json.gz",
            ct);

    private async Task SyncFeedAsync(string feedName, string feedUrl, CancellationToken ct)
    {
        var metaUrl = feedUrl.Replace(".json.gz", ".meta", StringComparison.OrdinalIgnoreCase);
        var metaText = await httpClient.GetStringAsync(metaUrl, ct);
        var feedLastModified = ParseMetaLastModified(metaText);

        var checkpoint = await db.NvdFeedCheckpoints
            .FirstOrDefaultAsync(c => c.FeedName == feedName, ct);

        if (checkpoint is not null && checkpoint.FeedLastModified >= feedLastModified)
        {
            logger.LogDebug("NVD feed {FeedName} is up to date (last modified {LastModified})",
                feedName, feedLastModified);
            return;
        }

        logger.LogInformation("Downloading NVD feed {FeedName} from {Url}", feedName, feedUrl);

        await using var responseStream = await httpClient.GetStreamAsync(feedUrl, ct);
        await using var gz = new GZipStream(responseStream, CompressionMode.Decompress);

        var feed = await JsonSerializer.DeserializeAsync<NvdFeedResponse>(gz,
            cancellationToken: ct);

        if (feed is null)
        {
            logger.LogWarning("NVD feed {FeedName} returned null after deserialization", feedName);
            return;
        }

        var upsertCount = await UpsertItemsAsync(feed.Items, ct);

        if (checkpoint is null)
            db.NvdFeedCheckpoints.Add(NvdFeedCheckpoint.Create(feedName, feedLastModified));
        else
            checkpoint.Update(feedLastModified);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("NVD feed {FeedName}: upserted {Count} CVEs", feedName, upsertCount);
    }

    private async Task<int> UpsertItemsAsync(List<NvdFeedItem> items, CancellationToken ct)
    {
        var cveIds = items
            .Select(i => i.Cve?.Meta?.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = await db.NvdCveCache
            .Where(c => cveIds.Contains(c.CveId))
            .ToDictionaryAsync(c => c.CveId, StringComparer.OrdinalIgnoreCase, ct);

        var count = 0;
        foreach (var item in items)
        {
            var cveId = item.Cve?.Meta?.Id;
            if (string.IsNullOrEmpty(cveId)) continue;

            var (description, cvssScore, cvssVector, publishedDate, feedLastMod,
                refsJson, configsJson) = ExtractCveData(item);

            if (existing.TryGetValue(cveId, out var cached))
                cached.Update(description, cvssScore, cvssVector, publishedDate,
                    feedLastMod, refsJson, configsJson);
            else
                db.NvdCveCache.Add(NvdCveCache.Create(cveId, description, cvssScore,
                    cvssVector, publishedDate, feedLastMod, refsJson, configsJson));

            count++;
        }

        return count;
    }

    private static (string Description, decimal? CvssScore, string? CvssVector,
        DateTimeOffset? PublishedDate, DateTimeOffset FeedLastModified,
        string RefsJson, string ConfigsJson) ExtractCveData(NvdFeedItem item)
    {
        var description = item.Cve?.Description?.Data
            .FirstOrDefault(d => string.Equals(d.Lang, "en", StringComparison.OrdinalIgnoreCase))
            ?.Value ?? string.Empty;

        var cvssV3 = item.Impact?.BaseMetricV3?.CvssV3;
        decimal? cvssScore = cvssV3?.BaseScore;
        string? cvssVector = cvssV3?.VectorString;

        DateTimeOffset? publishedDate = null;
        if (!string.IsNullOrEmpty(item.PublishedDate) &&
            DateTimeOffset.TryParse(item.PublishedDate, out var pd))
            publishedDate = pd;

        var feedLastMod = DateTimeOffset.UtcNow;
        if (!string.IsNullOrEmpty(item.LastModifiedDate) &&
            DateTimeOffset.TryParse(item.LastModifiedDate, out var lm))
            feedLastMod = lm;

        var refs = (item.Cve?.References?.Data ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Select(r => new NvdCachedReference(r.Url, r.RefSource, r.Tags))
            .ToList();

        var configs = FlattenNodes(item.Configurations?.Nodes ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Cpe23Uri))
            .Select(m => new NvdCachedCpeMatch(m.Vulnerable, m.Cpe23Uri,
                m.VersionStartIncluding, m.VersionStartExcluding,
                m.VersionEndIncluding, m.VersionEndExcluding))
            .ToList();

        var refsJson = JsonSerializer.Serialize(refs);
        var configsJson = JsonSerializer.Serialize(configs);

        return (description, cvssScore, cvssVector, publishedDate, feedLastMod, refsJson, configsJson);
    }

    private static IEnumerable<NvdFeedCpeMatch> FlattenNodes(List<NvdFeedNode> nodes)
    {
        foreach (var node in nodes)
        {
            foreach (var match in node.CpeMatch)
                yield return match;
            foreach (var match in FlattenNodes(node.Children))
                yield return match;
        }
    }

    internal static DateTimeOffset ParseMetaLastModified(string metaText)
    {
        foreach (var line in metaText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("lastModifiedDate:", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = trimmed["lastModifiedDate:".Length..].Trim();
            if (DateTimeOffset.TryParse(value, out var result))
                return result.ToUniversalTime();
        }
        return DateTimeOffset.MinValue;
    }
}
