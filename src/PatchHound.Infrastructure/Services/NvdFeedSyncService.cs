using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Credentials;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class NvdFeedSyncService(
    HttpClient httpClient,
    PatchHoundDbContext db,
    ISecretStore secretStore,
    StoredCredentialResolver credentialResolver,
    ILogger<NvdFeedSyncService> logger) : INvdFeedSyncService
{
    private const string FeedBaseUrl = "https://nvd.nist.gov/feeds/json/cve/2.0";
    private const string ApiUrl = "https://services.nvd.nist.gov/rest/json/cves/2.0";
    private const int PageSize = 2000;
    private static readonly TimeSpan ApiKeyDelay = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan NoKeyDelay = TimeSpan.FromSeconds(7);

    public Task SyncYearFeedAsync(int year, CancellationToken ct) =>
        SyncYearFeedAsync(year, force: false, ct);

    public async Task SyncYearFeedAsync(int year, bool force, CancellationToken ct)
    {
        var feedName = year.ToString();
        var checkpoint = await db.NvdFeedCheckpoints.FirstOrDefaultAsync(c => c.FeedName == feedName, ct);
        if (checkpoint is not null && !force)
        {
            logger.LogDebug("NVD year feed {Year} already synced at {SyncedAt}, skipping.", year, checkpoint.SyncedAt);
            return;
        }

        logger.LogInformation("NVD: starting initial sync for year {Year}", year);

        var count = await FetchAndUpsertYearArchiveAsync(year, ct);

        if (checkpoint is null)
            db.NvdFeedCheckpoints.Add(NvdFeedCheckpoint.Create(feedName, DateTimeOffset.UtcNow));
        else
            checkpoint.Update(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("NVD year feed {Year}: upserted {Count} CVEs", year, count);
    }

    public async Task SyncModifiedFeedAsync(CancellationToken ct)
    {
        const string feedName = "modified";
        var checkpoint = await db.NvdFeedCheckpoints.FirstOrDefaultAsync(c => c.FeedName == feedName, ct);

        var lastModStart = checkpoint?.FeedLastModified.AddMinutes(-15)
            ?? DateTimeOffset.UtcNow.AddHours(-13);
        var lastModEnd = DateTimeOffset.UtcNow;

        var apiKey = await GetApiKeyAsync(ct);

        var count = await FetchAndUpsertAsync(
            startIndex => $"{ApiUrl}?lastModStartDate={FormatDate(lastModStart)}&lastModEndDate={FormatDate(lastModEnd)}&startIndex={startIndex}&resultsPerPage={PageSize}",
            apiKey, ct);

        if (checkpoint is null)
            db.NvdFeedCheckpoints.Add(NvdFeedCheckpoint.Create(feedName, lastModEnd));
        else
            checkpoint.Update(lastModEnd);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("NVD modified feed: upserted {Count} CVEs (window: {Start} to {End})",
            count, lastModStart, lastModEnd);
    }

    private async Task<int> FetchAndUpsertAsync(Func<int, string> urlBuilder, string? apiKey, CancellationToken ct)
    {
        var delay = apiKey is not null ? ApiKeyDelay : NoKeyDelay;
        var totalCount = 0;
        var startIndex = 0;

        while (true)
        {
            var url = urlBuilder(startIndex);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (apiKey is not null)
                request.Headers.Add("apiKey", apiKey);

            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var page = await JsonSerializer.DeserializeAsync<NvdApiResponse>(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            if (page is null || page.Vulnerabilities.Count == 0)
                break;

            totalCount += await UpsertItemsAsync(page.Vulnerabilities, ct);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            if (startIndex + page.ResultsPerPage >= page.TotalResults)
                break;

            startIndex += page.ResultsPerPage;
            await Task.Delay(delay, ct);
        }

        return totalCount;
    }

    private async Task<int> FetchAndUpsertYearArchiveAsync(int year, CancellationToken ct)
    {
        var url = $"{FeedBaseUrl}/nvdcve-2.0-{year}.json.gz";
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var compressed = await response.Content.ReadAsStreamAsync(ct);
        await using var decompressed = new GZipStream(compressed, CompressionMode.Decompress);
        var feed = await JsonSerializer.DeserializeAsync<NvdApiResponse>(decompressed, cancellationToken: ct);

        if (feed is null || feed.Vulnerabilities.Count == 0)
            return 0;

        var count = await UpsertItemsAsync(feed.Vulnerabilities, ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return count;
    }

    private async Task<int> UpsertItemsAsync(List<NvdApiVulnerabilityItem> items, CancellationToken ct)
    {
        var cveIds = items
            .Select(i => i.Cve?.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = await db.NvdCveCache
            .Where(c => cveIds.Contains(c.CveId))
            .ToDictionaryAsync(c => c.CveId, StringComparer.OrdinalIgnoreCase, ct);

        var count = 0;
        foreach (var item in items)
        {
            var cveId = item.Cve?.Id;
            if (string.IsNullOrEmpty(cveId)) continue;

            var (description, cvssScore, cvssVector, publishedDate, lastMod, refsJson, configsJson) = ExtractCveData(item);

            if (string.IsNullOrWhiteSpace(description))
            {
                logger.LogDebug("Skipping {CveId} — no English description.", cveId);
                continue;
            }

            if (existing.TryGetValue(cveId, out var cached))
                cached.Update(description, cvssScore, cvssVector, publishedDate, lastMod, refsJson, configsJson);
            else
                db.NvdCveCache.Add(NvdCveCache.Create(cveId, description, cvssScore,
                    cvssVector, publishedDate, lastMod, refsJson, configsJson));
            count++;
        }
        return count;
    }

    private static (string? Description, decimal? CvssScore, string? CvssVector,
        DateTimeOffset? PublishedDate, DateTimeOffset LastModified,
        string RefsJson, string ConfigsJson) ExtractCveData(NvdApiVulnerabilityItem item)
    {
        var cve = item.Cve!;

        var description = cve.Descriptions
            .FirstOrDefault(d => string.Equals(d.Lang, "en", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        // Prefer CVSSv3.1 Primary, fall back to v3.0
        var cvssMetric = cve.Metrics?.CvssMetricV31
            .FirstOrDefault(m => string.Equals(m.Type, "Primary", StringComparison.OrdinalIgnoreCase))
            ?? cve.Metrics?.CvssMetricV31.FirstOrDefault()
            ?? cve.Metrics?.CvssMetricV30
                .FirstOrDefault(m => string.Equals(m.Type, "Primary", StringComparison.OrdinalIgnoreCase))
            ?? cve.Metrics?.CvssMetricV30.FirstOrDefault();

        decimal? cvssScore = cvssMetric?.CvssData?.BaseScore;
        string? cvssVector = cvssMetric?.CvssData?.VectorString;

        DateTimeOffset? publishedDate = null;
        if (!string.IsNullOrEmpty(cve.Published) && DateTimeOffset.TryParse(cve.Published, out var pd))
            publishedDate = pd;

        var lastMod = DateTimeOffset.MinValue;
        if (!string.IsNullOrEmpty(cve.LastModified) && DateTimeOffset.TryParse(cve.LastModified, out var lm))
            lastMod = lm;

        var refs = cve.References
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Select(r => new NvdCachedReference(r.Url, r.Source, r.Tags))
            .ToList();

        var configs = cve.Configurations
            .SelectMany(c => FlattenNodes(c.Nodes))
            .Where(m => !string.IsNullOrWhiteSpace(m.Criteria))
            .Select(m => new NvdCachedCpeMatch(m.Vulnerable, m.Criteria,
                m.VersionStartIncluding, m.VersionStartExcluding,
                m.VersionEndIncluding, m.VersionEndExcluding))
            .ToList();

        return (description, cvssScore, cvssVector, publishedDate, lastMod,
            JsonSerializer.Serialize(refs), JsonSerializer.Serialize(configs));
    }

    private static IEnumerable<NvdApiCpeMatch> FlattenNodes(List<NvdApiNode> nodes)
    {
        foreach (var node in nodes)
        {
            foreach (var match in node.CpeMatch) yield return match;
            foreach (var match in FlattenNodes(node.Children)) yield return match;
        }
    }

    private async Task<string?> GetApiKeyAsync(CancellationToken ct)
    {
        try
        {
            var source = await db.EnrichmentSourceConfigurations
                .FirstOrDefaultAsync(s => s.SourceKey == EnrichmentSourceCatalog.NvdSourceKey, ct);

            if (source is null)
            {
                logger.LogDebug("NVD source is not configured; proceeding without API key.");
                return null;
            }

            if (source.StoredCredentialId.HasValue)
            {
                var apiKey = await credentialResolver.ResolveGlobalApiKeyAsync(
                    source.StoredCredentialId.Value,
                    ct
                );

                if (!string.IsNullOrWhiteSpace(apiKey))
                    return apiKey;

                logger.LogWarning(
                    "NVD source references stored credential {CredentialId}, but no API key could be resolved; proceeding without key.",
                    source.StoredCredentialId.Value
                );
                return null;
            }

            if (string.IsNullOrWhiteSpace(source.SecretRef))
            {
                logger.LogDebug("NVD source has no SecretRef configured; proceeding without API key.");
                return null;
            }

            return await secretStore.GetSecretAsync(source.SecretRef, StoredCredentialSecretKeys.ApiKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve NVD API key; proceeding without key (rate limits apply).");
            return null;
        }
    }

    private static string FormatDate(DateTimeOffset date) =>
        date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff");
}
