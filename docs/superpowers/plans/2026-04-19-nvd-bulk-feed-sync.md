# NVD Bulk Feed Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-CVE NVD API calls with a background bulk feed sync that downloads NVD JSON 1.1 feeds, stores CVEs locally in `NvdCveCache`, and serves enrichment from the cache — then delete all the old per-CVE API code.

**Architecture:** A new `NvdFeedSyncWorker` (BackgroundService) downloads NVD's gzipped JSON year-feeds and `modified` feed on a 2-hour cycle, upserting into a `NvdCveCache` table. The existing `NvdVulnerabilityEnrichmentRunner` is rewritten to read from the cache table instead of making HTTP calls. Five legacy source files (`NvdApiClient`, `NvdGlobalConfigurationProvider`, `NvdVulnerabilityEnricher`, `NvdVulnerabilitySource`, `IVulnerabilityEnricher`) are deleted.

**Tech Stack:** .NET 10, EF Core 10 / PostgreSQL, `System.IO.Compression.GZipStream`, `System.Text.Json`, `PeriodicTimer`, xunit + FluentAssertions + NSubstitute

---

## File map

**Create:**
- `src/PatchHound.Core/Entities/NvdCveCache.cs` — cache entity + serialisable sub-records
- `src/PatchHound.Core/Entities/NvdFeedCheckpoint.cs` — per-feed last-synced state
- `src/PatchHound.Infrastructure/Data/Configurations/NvdCveCacheConfiguration.cs`
- `src/PatchHound.Infrastructure/Data/Configurations/NvdFeedCheckpointConfiguration.cs`
- `src/PatchHound.Infrastructure/Services/NvdFeedModels.cs` — JSON 1.1 feed deserialisation classes
- `src/PatchHound.Infrastructure/Services/NvdFeedSyncService.cs` — download / parse / upsert
- `src/PatchHound.Worker/NvdFeedSyncWorker.cs` — startup + 2-hour incremental background loop
- `tests/PatchHound.Tests/Infrastructure/NvdFeedSyncServiceTests.cs`
- `tests/PatchHound.Tests/Infrastructure/NvdCacheEnrichmentRunnerTests.cs`

**Modify:**
- `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` — add two DbSets
- `src/PatchHound.Infrastructure/Services/NvdVulnerabilityEnrichmentRunner.cs` — rewrite to use cache
- `src/PatchHound.Infrastructure/Tenants/EnrichmentSourceCatalog.cs` — NVD no longer needs credentials
- `src/PatchHound.Infrastructure/Services/EnrichmentJobEnqueuer.cs` — allow NVD jobs without SecretRef
- `src/PatchHound.Infrastructure/DependencyInjection.cs` — register new, remove old
- `src/PatchHound.Worker/Program.cs` — add `NvdFeedSyncWorker`

**Delete:**
- `src/PatchHound.Infrastructure/VulnerabilitySources/NvdApiClient.cs`
- `src/PatchHound.Infrastructure/VulnerabilitySources/NvdGlobalConfigurationProvider.cs`
- `src/PatchHound.Infrastructure/VulnerabilitySources/NvdVulnerabilitySource.cs`
- `src/PatchHound.Infrastructure/Services/NvdVulnerabilityEnricher.cs`
- `src/PatchHound.Core/Interfaces/IVulnerabilityEnricher.cs`
- `tests/PatchHound.Tests/Infrastructure/NvdVulnerabilityEnrichmentRunnerTests.cs`
- `tests/PatchHound.Tests/Infrastructure/NvdVulnerabilityEnricherTests.cs`

---

### Task 1: `NvdCveCache` + `NvdFeedCheckpoint` entities, EF config, migration

**Files:**
- Create: `src/PatchHound.Core/Entities/NvdCveCache.cs`
- Create: `src/PatchHound.Core/Entities/NvdFeedCheckpoint.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/NvdCveCacheConfiguration.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/NvdFeedCheckpointConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`

- [ ] **Step 1: Write `NvdCveCache` entity**

```csharp
// src/PatchHound.Core/Entities/NvdCveCache.cs
using System.Text.Json.Serialization;

namespace PatchHound.Core.Entities;

public class NvdCveCache
{
    public string CveId { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal? CvssScore { get; private set; }
    public string? CvssVector { get; private set; }
    public DateTimeOffset? PublishedDate { get; private set; }
    public DateTimeOffset FeedLastModified { get; private set; }
    public string ReferencesJson { get; private set; } = "[]";
    public string ConfigurationsJson { get; private set; } = "[]";
    public DateTimeOffset CachedAt { get; private set; }

    private NvdCveCache() { }

    public static NvdCveCache Create(
        string cveId,
        string description,
        decimal? cvssScore,
        string? cvssVector,
        DateTimeOffset? publishedDate,
        DateTimeOffset feedLastModified,
        string referencesJson,
        string configurationsJson)
    {
        if (string.IsNullOrWhiteSpace(cveId))
            throw new ArgumentException("CveId is required.", nameof(cveId));
        return new NvdCveCache
        {
            CveId = cveId,
            Description = description,
            CvssScore = cvssScore,
            CvssVector = cvssVector,
            PublishedDate = publishedDate,
            FeedLastModified = feedLastModified,
            ReferencesJson = referencesJson,
            ConfigurationsJson = configurationsJson,
            CachedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        string description,
        decimal? cvssScore,
        string? cvssVector,
        DateTimeOffset? publishedDate,
        DateTimeOffset feedLastModified,
        string referencesJson,
        string configurationsJson)
    {
        Description = description;
        CvssScore = cvssScore;
        CvssVector = cvssVector;
        PublishedDate = publishedDate;
        FeedLastModified = feedLastModified;
        ReferencesJson = referencesJson;
        ConfigurationsJson = configurationsJson;
        CachedAt = DateTimeOffset.UtcNow;
    }
}

// These records are serialised into NvdCveCache.ReferencesJson / ConfigurationsJson.
// Both NvdFeedSyncService (writes) and NvdVulnerabilityEnrichmentRunner (reads) use them.
public record NvdCachedReference(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("tags")] List<string> Tags);

public record NvdCachedCpeMatch(
    [property: JsonPropertyName("vulnerable")] bool Vulnerable,
    [property: JsonPropertyName("criteria")] string Criteria,
    [property: JsonPropertyName("vsi")] string? VersionStartIncluding,
    [property: JsonPropertyName("vse")] string? VersionStartExcluding,
    [property: JsonPropertyName("vei")] string? VersionEndIncluding,
    [property: JsonPropertyName("vee")] string? VersionEndExcluding);
```

- [ ] **Step 2: Write `NvdFeedCheckpoint` entity**

```csharp
// src/PatchHound.Core/Entities/NvdFeedCheckpoint.cs
namespace PatchHound.Core.Entities;

public class NvdFeedCheckpoint
{
    public string FeedName { get; private set; } = string.Empty;
    public DateTimeOffset FeedLastModified { get; private set; }
    public DateTimeOffset SyncedAt { get; private set; }

    private NvdFeedCheckpoint() { }

    public static NvdFeedCheckpoint Create(string feedName, DateTimeOffset feedLastModified)
    {
        if (string.IsNullOrWhiteSpace(feedName))
            throw new ArgumentException("FeedName is required.", nameof(feedName));
        return new NvdFeedCheckpoint
        {
            FeedName = feedName,
            FeedLastModified = feedLastModified,
            SyncedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(DateTimeOffset feedLastModified)
    {
        FeedLastModified = feedLastModified;
        SyncedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 3: Write EF configurations**

```csharp
// src/PatchHound.Infrastructure/Data/Configurations/NvdCveCacheConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class NvdCveCacheConfiguration : IEntityTypeConfiguration<NvdCveCache>
{
    public void Configure(EntityTypeBuilder<NvdCveCache> builder)
    {
        builder.HasKey(e => e.CveId);
        builder.Property(e => e.CveId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Description).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(e => e.CvssScore).HasColumnType("numeric(4,2)");
        builder.Property(e => e.CvssVector).HasMaxLength(256);
        builder.Property(e => e.ReferencesJson).IsRequired().HasColumnType("text");
        builder.Property(e => e.ConfigurationsJson).IsRequired().HasColumnType("text");

        builder.HasIndex(e => e.PublishedDate);
        builder.HasIndex(e => e.FeedLastModified);
    }
}
```

```csharp
// src/PatchHound.Infrastructure/Data/Configurations/NvdFeedCheckpointConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class NvdFeedCheckpointConfiguration : IEntityTypeConfiguration<NvdFeedCheckpoint>
{
    public void Configure(EntityTypeBuilder<NvdFeedCheckpoint> builder)
    {
        builder.HasKey(e => e.FeedName);
        builder.Property(e => e.FeedName).IsRequired().HasMaxLength(32);
    }
}
```

- [ ] **Step 4: Add DbSets to `PatchHoundDbContext`**

In `PatchHoundDbContext.cs`, after the existing `DbSet<EnrichmentRun> EnrichmentRuns` line, add:

```csharp
public DbSet<NvdCveCache> NvdCveCache => Set<NvdCveCache>();
public DbSet<NvdFeedCheckpoint> NvdFeedCheckpoints => Set<NvdFeedCheckpoint>();
```

Also add to the `using` block at the top if not already present:
```csharp
using PatchHound.Core.Entities;
```

- [ ] **Step 5: Run migration**

```bash
cd /path/to/PatchHound
dotnet ef migrations add AddNvdCveCache \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

Expected: new migration file in `src/PatchHound.Infrastructure/Migrations/` with `CreateTable("NvdCveCache")` and `CreateTable("NvdFeedCheckpoints")`.

- [ ] **Step 6: Build to verify**

```bash
dotnet build PatchHound.slnx
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Core/Entities/NvdCveCache.cs \
        src/PatchHound.Core/Entities/NvdFeedCheckpoint.cs \
        src/PatchHound.Infrastructure/Data/Configurations/NvdCveCacheConfiguration.cs \
        src/PatchHound.Infrastructure/Data/Configurations/NvdFeedCheckpointConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        src/PatchHound.Infrastructure/Migrations/
git commit -m "feat: add NvdCveCache and NvdFeedCheckpoint entities with migration"
```

---

### Task 2: NVD feed models + `NvdFeedSyncService`

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/NvdFeedModels.cs`
- Create: `src/PatchHound.Infrastructure/Services/NvdFeedSyncService.cs`

- [ ] **Step 1: Write NVD 1.1 feed deserialization models**

The NVD JSON 1.1 feed format differs from the 2.0 API. Year feeds have URL pattern:
`https://nvd.nist.gov/feeds/json/cve/1.1/nvdcve-1.1-{year}.json.gz`
`https://nvd.nist.gov/feeds/json/cve/1.1/nvdcve-1.1-modified.json.gz`

```csharp
// src/PatchHound.Infrastructure/Services/NvdFeedModels.cs
using System.Text.Json.Serialization;

namespace PatchHound.Infrastructure.Services;

internal class NvdFeedResponse
{
    [JsonPropertyName("CVE_Items")]
    public List<NvdFeedItem> Items { get; set; } = [];
}

internal class NvdFeedItem
{
    [JsonPropertyName("cve")]
    public NvdFeedCveSection? Cve { get; set; }

    [JsonPropertyName("configurations")]
    public NvdFeedConfigurations? Configurations { get; set; }

    [JsonPropertyName("impact")]
    public NvdFeedImpact? Impact { get; set; }

    [JsonPropertyName("publishedDate")]
    public string? PublishedDate { get; set; }

    [JsonPropertyName("lastModifiedDate")]
    public string? LastModifiedDate { get; set; }
}

internal class NvdFeedCveSection
{
    [JsonPropertyName("CVE_data_meta")]
    public NvdFeedMeta? Meta { get; set; }

    [JsonPropertyName("description")]
    public NvdFeedDescriptionWrapper? Description { get; set; }

    [JsonPropertyName("references")]
    public NvdFeedReferencesWrapper? References { get; set; }
}

internal class NvdFeedMeta
{
    [JsonPropertyName("ID")]
    public string Id { get; set; } = string.Empty;
}

internal class NvdFeedDescriptionWrapper
{
    [JsonPropertyName("description_data")]
    public List<NvdFeedDescriptionItem> Data { get; set; } = [];
}

internal class NvdFeedDescriptionItem
{
    [JsonPropertyName("lang")]
    public string Lang { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

internal class NvdFeedReferencesWrapper
{
    [JsonPropertyName("reference_data")]
    public List<NvdFeedReferenceItem> Data { get; set; } = [];
}

internal class NvdFeedReferenceItem
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("refsource")]
    public string RefSource { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

internal class NvdFeedConfigurations
{
    [JsonPropertyName("nodes")]
    public List<NvdFeedNode> Nodes { get; set; } = [];
}

internal class NvdFeedNode
{
    [JsonPropertyName("cpe_match")]
    public List<NvdFeedCpeMatch> CpeMatch { get; set; } = [];

    [JsonPropertyName("children")]
    public List<NvdFeedNode> Children { get; set; } = [];
}

internal class NvdFeedCpeMatch
{
    [JsonPropertyName("vulnerable")]
    public bool Vulnerable { get; set; }

    [JsonPropertyName("cpe23Uri")]
    public string Cpe23Uri { get; set; } = string.Empty;

    [JsonPropertyName("versionStartIncluding")]
    public string? VersionStartIncluding { get; set; }

    [JsonPropertyName("versionStartExcluding")]
    public string? VersionStartExcluding { get; set; }

    [JsonPropertyName("versionEndIncluding")]
    public string? VersionEndIncluding { get; set; }

    [JsonPropertyName("versionEndExcluding")]
    public string? VersionEndExcluding { get; set; }
}

internal class NvdFeedImpact
{
    [JsonPropertyName("baseMetricV3")]
    public NvdFeedBaseMetricV3? BaseMetricV3 { get; set; }
}

internal class NvdFeedBaseMetricV3
{
    [JsonPropertyName("cvssV3")]
    public NvdFeedCvssV3? CvssV3 { get; set; }
}

internal class NvdFeedCvssV3
{
    [JsonPropertyName("baseScore")]
    public decimal BaseScore { get; set; }

    [JsonPropertyName("vectorString")]
    public string? VectorString { get; set; }
}
```

- [ ] **Step 2: Write the failing test for `NvdFeedSyncService`**

```csharp
// tests/PatchHound.Tests/Infrastructure/NvdFeedSyncServiceTests.cs
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure;

public class NvdFeedSyncServiceTests
{
    [Fact]
    public async Task SyncFeedAsync_inserts_new_cve_entries()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var feedJson = BuildFeedJson("CVE-2024-1234", "Test description", 7.5m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N",
            "2024-01-01T00:00Z", "2024-01-10T00:00Z",
            referenceUrl: "https://example.com/advisory",
            criteria: "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*");

        var metaContent = "lastModifiedDate:2024-01-10T00:00:00-04:00\nsha256:abc123";
        var handler = new FakeFeedHttpHandler(feedJson, metaContent);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://nvd.nist.gov") };
        var service = new NvdFeedSyncService(httpClient, db,
            NullLogger<NvdFeedSyncService>.Instance);

        await service.SyncYearFeedAsync(2024, CancellationToken.None);

        var cached = await db.NvdCveCache.SingleAsync();
        cached.CveId.Should().Be("CVE-2024-1234");
        cached.Description.Should().Be("Test description");
        cached.CvssScore.Should().Be(7.5m);
        cached.CvssVector.Should().Be("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N");
        cached.PublishedDate.Should().NotBeNull();

        var refs = JsonSerializer.Deserialize<List<NvdCachedReference>>(cached.ReferencesJson)!;
        refs.Should().ContainSingle(r => r.Url == "https://example.com/advisory");

        var configs = JsonSerializer.Deserialize<List<NvdCachedCpeMatch>>(cached.ConfigurationsJson)!;
        configs.Should().ContainSingle(c => c.Criteria == "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*");

        var checkpoint = await db.NvdFeedCheckpoints.SingleAsync();
        checkpoint.FeedName.Should().Be("2024");
    }

    [Fact]
    public async Task SyncFeedAsync_updates_existing_cve_entry()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2024-1234", "old description",
            null, null, null,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "[]", "[]"));
        db.NvdFeedCheckpoints.Add(NvdFeedCheckpoint.Create("2024",
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var feedJson = BuildFeedJson("CVE-2024-1234", "updated description", 9.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:C/C:H/I:H/A:H",
            "2024-01-01T00:00Z", "2024-01-15T00:00Z",
            referenceUrl: null, criteria: "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");

        var metaContent = "lastModifiedDate:2024-01-15T00:00:00-04:00\nsha256:def456";
        var handler = new FakeFeedHttpHandler(feedJson, metaContent);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://nvd.nist.gov") };
        var service = new NvdFeedSyncService(httpClient, db,
            NullLogger<NvdFeedSyncService>.Instance);

        await service.SyncYearFeedAsync(2024, CancellationToken.None);

        var cached = await db.NvdCveCache.SingleAsync();
        cached.Description.Should().Be("updated description");
        cached.CvssScore.Should().Be(9.8m);
    }

    [Fact]
    public async Task SyncFeedAsync_skips_download_when_feed_not_modified()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var existingModified = new DateTimeOffset(2024, 1, 10, 4, 0, 0, TimeSpan.Zero);
        db.NvdFeedCheckpoints.Add(NvdFeedCheckpoint.Create("2024", existingModified));
        await db.SaveChangesAsync();

        var metaContent = "lastModifiedDate:2024-01-10T00:00:00-04:00\nsha256:abc123";
        var handler = new FakeFeedHttpHandler(feedJson: "should-not-be-fetched", metaContent);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://nvd.nist.gov") };
        var service = new NvdFeedSyncService(httpClient, db,
            NullLogger<NvdFeedSyncService>.Instance);

        await service.SyncYearFeedAsync(2024, CancellationToken.None);

        handler.FeedRequested.Should().BeFalse("feed should be skipped when checkpoint is current");
        (await db.NvdCveCache.CountAsync()).Should().Be(0);
    }

    private static string BuildFeedJson(
        string cveId, string description, decimal baseScore, string vector,
        string publishedDate, string lastModifiedDate,
        string? referenceUrl, string criteria)
    {
        var refs = referenceUrl is null ? "[]"
            : $@"[{{""url"":""{referenceUrl}"",""refsource"":""MISC"",""tags"":[]}}]";
        return $$"""
            {
              "CVE_Items": [{
                "cve": {
                  "CVE_data_meta": {"ID": "{{cveId}}"},
                  "description": {"description_data": [{"lang": "en","value": "{{description}}"}]},
                  "references": {"reference_data": {{refs}}}
                },
                "configurations": {
                  "nodes": [{"cpe_match": [{"vulnerable": true,"cpe23Uri": "{{criteria}}"}],"children": []}]
                },
                "impact": {
                  "baseMetricV3": {"cvssV3": {"baseScore": {{baseScore}},"vectorString": "{{vector}}"}}
                },
                "publishedDate": "{{publishedDate}}",
                "lastModifiedDate": "{{lastModifiedDate}}"
              }]
            }
            """;
    }

    internal sealed class FakeFeedHttpHandler : HttpMessageHandler
    {
        private readonly string _feedJson;
        private readonly string _metaContent;
        public bool FeedRequested { get; private set; }

        public FakeFeedHttpHandler(string feedJson, string metaContent)
        {
            _feedJson = feedJson;
            _metaContent = metaContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_metaContent),
                });
            }

            FeedRequested = true;
            var compressed = CompressJson(_feedJson);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(compressed),
            };
            response.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/gzip");
            return Task.FromResult(response);
        }

        private static byte[] CompressJson(string json)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionMode.Compress))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                gz.Write(bytes);
            }
            return ms.ToArray();
        }
    }
}
```

- [ ] **Step 3: Run the test to confirm it fails**

```bash
cd /path/to/PatchHound
dotnet test PatchHound.slnx --filter "NvdFeedSyncServiceTests" -v minimal
```

Expected: FAIL — `NvdFeedSyncService` does not exist yet.

- [ ] **Step 4: Implement `NvdFeedSyncService`**

```csharp
// src/PatchHound.Infrastructure/Services/NvdFeedSyncService.cs
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
```

- [ ] **Step 5: Run tests to confirm they pass**

```bash
dotnet test PatchHound.slnx --filter "NvdFeedSyncServiceTests" -v minimal
```

Expected: 3 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/NvdFeedModels.cs \
        src/PatchHound.Infrastructure/Services/NvdFeedSyncService.cs \
        tests/PatchHound.Tests/Infrastructure/NvdFeedSyncServiceTests.cs
git commit -m "feat: add NvdFeedSyncService — downloads and parses NVD 1.1 bulk feeds"
```

---

### Task 3: `NvdFeedSyncWorker` background service

**Files:**
- Create: `src/PatchHound.Worker/NvdFeedSyncWorker.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs` — register typed HttpClient
- Modify: `src/PatchHound.Worker/Program.cs` — register hosted service

- [ ] **Step 1: Write `NvdFeedSyncWorker`**

```csharp
// src/PatchHound.Worker/NvdFeedSyncWorker.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Worker;

public class NvdFeedSyncWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<NvdFeedSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IncrementalInterval = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SyncInitialAsync(stoppingToken);

        using var timer = new PeriodicTimer(IncrementalInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncModifiedAsync(stoppingToken);
        }
    }

    private async Task SyncInitialAsync(CancellationToken ct)
    {
        var currentYear = DateTimeOffset.UtcNow.Year;
        logger.LogInformation(
            "NvdFeedSyncWorker: starting initial sync for years {From}-{To} + modified",
            currentYear - 4, currentYear);

        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<NvdFeedSyncService>();

        for (var year = currentYear - 4; year <= currentYear; year++)
        {
            try
            {
                await service.SyncYearFeedAsync(year, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "NVD initial sync failed for year {Year}", year);
            }
        }

        try
        {
            await service.SyncModifiedFeedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "NVD initial modified feed sync failed");
        }
    }

    private async Task SyncModifiedAsync(CancellationToken ct)
    {
        logger.LogDebug("NvdFeedSyncWorker: incremental modified-feed sync");
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<NvdFeedSyncService>();
        try
        {
            await service.SyncModifiedFeedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "NVD modified feed sync failed");
        }
    }
}
```

- [ ] **Step 2: Register `NvdFeedSyncService` HttpClient in `DependencyInjection.cs`**

In `DependencyInjection.cs`, in the "Vulnerability Sources" section, **add** (after the `EndOfLifeApiClient` registration):

```csharp
services.AddHttpClient<NvdFeedSyncService>()
    .AddExternalHttpPolicies(maxConnectionsPerServer: 2);
```

Also add the using statement if not present:
```csharp
using PatchHound.Infrastructure.Services;
```

- [ ] **Step 3: Register `NvdFeedSyncWorker` in `Program.cs`**

In `src/PatchHound.Worker/Program.cs`, after the existing `AddHostedService` lines, add:

```csharp
builder.Services.AddHostedService<NvdFeedSyncWorker>();
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build PatchHound.slnx
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Worker/NvdFeedSyncWorker.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        src/PatchHound.Worker/Program.cs
git commit -m "feat: add NvdFeedSyncWorker — startup + 2h incremental NVD feed sync"
```

---

### Task 4: Rewrite `NvdVulnerabilityEnrichmentRunner` to use `NvdCveCache`

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/NvdVulnerabilityEnrichmentRunner.cs`

The rewritten runner:
- Removes `NvdApiClient` and `NvdGlobalConfigurationProvider` dependencies
- Sets `MinimumDelay` to `TimeSpan.Zero` (no rate limiting needed)
- Looks up `NvdCveCache` by CVE ID instead of making HTTP calls
- Cache miss → `NoData` (will be populated on the next feed sync)
- Reuses existing `BuildResolveInput` logic but adapted to read from `NvdCachedReference` / `NvdCachedCpeMatch`

- [ ] **Step 1: Write the failing test for the cache-based runner**

```csharp
// tests/PatchHound.Tests/Infrastructure/NvdCacheEnrichmentRunnerTests.cs
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure;

public class NvdCacheEnrichmentRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_returns_Succeeded_and_writes_canonical_rows_from_cache()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var vuln = Vulnerability.Create("nvd", "CVE-2024-9999", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        db.Vulnerabilities.Add(vuln);

        var refs = JsonSerializer.Serialize(new[]
        {
            new NvdCachedReference("https://nvd.nist.gov/vuln/detail/CVE-2024-9999", "NVD", ["Vendor Advisory"])
        });
        var configs = JsonSerializer.Serialize(new[]
        {
            new NvdCachedCpeMatch(true, "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*", null, null, null, null)
        });
        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2024-9999",
            "Cache-based description", 9.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:C/C:H/I:H/A:H",
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow, refs, configs));
        await db.SaveChangesAsync();

        var scopeFactory = new FakeScopeFactory(db);
        var runner = new NvdVulnerabilityEnrichmentRunner(
            scopeFactory, NullLogger<NvdVulnerabilityEnrichmentRunner>.Instance);

        var job = EnrichmentJob.Create(Guid.NewGuid(), "nvd",
            EnrichmentTargetModel.Vulnerability, vuln.Id, vuln.ExternalId, 100,
            DateTimeOffset.UtcNow);

        var result = await runner.ExecuteAsync(job, CancellationToken.None);

        result.Outcome.Should().Be(EnrichmentJobExecutionOutcome.Succeeded);
        var reloaded = await db.Vulnerabilities.SingleAsync();
        reloaded.Description.Should().Be("Cache-based description");
        reloaded.CvssScore.Should().Be(9.8m);

        var dbRefs = await db.VulnerabilityReferences.ToListAsync();
        dbRefs.Should().ContainSingle(r =>
            r.Url == "https://nvd.nist.gov/vuln/detail/CVE-2024-9999");

        var dbApps = await db.VulnerabilityApplicabilities.ToListAsync();
        dbApps.Should().ContainSingle(a =>
            a.CpeCriteria == "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*");
    }

    [Fact]
    public async Task ExecuteAsync_returns_NoData_when_cve_not_in_cache()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var vuln = Vulnerability.Create("nvd", "CVE-2024-0001", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        db.Vulnerabilities.Add(vuln);
        await db.SaveChangesAsync();

        var scopeFactory = new FakeScopeFactory(db);
        var runner = new NvdVulnerabilityEnrichmentRunner(
            scopeFactory, NullLogger<NvdVulnerabilityEnrichmentRunner>.Instance);

        var job = EnrichmentJob.Create(Guid.NewGuid(), "nvd",
            EnrichmentTargetModel.Vulnerability, vuln.Id, vuln.ExternalId, 100,
            DateTimeOffset.UtcNow);

        var result = await runner.ExecuteAsync(job, CancellationToken.None);

        result.Outcome.Should().Be(EnrichmentJobExecutionOutcome.NoData);
    }

    [Fact]
    public async Task ExecuteAsync_returns_NoData_when_target_vulnerability_missing()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var scopeFactory = new FakeScopeFactory(db);
        var runner = new NvdVulnerabilityEnrichmentRunner(
            scopeFactory, NullLogger<NvdVulnerabilityEnrichmentRunner>.Instance);

        var job = EnrichmentJob.Create(Guid.NewGuid(), "nvd",
            EnrichmentTargetModel.Vulnerability, Guid.NewGuid(), "CVE-2024-0000", 100,
            DateTimeOffset.UtcNow);

        var result = await runner.ExecuteAsync(job, CancellationToken.None);

        result.Outcome.Should().Be(EnrichmentJobExecutionOutcome.NoData);
    }

    [Fact]
    public async Task ExecuteAsync_resolves_applicability_to_SoftwareProduct_when_alias_exists()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var nvdSource = SourceSystem.Create("nvd", "NVD");
        db.SourceSystems.Add(nvdSource);
        var product = SoftwareProduct.Create("Acme", "Widget",
            "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        db.SoftwareProducts.Add(product);
        db.SoftwareAliases.Add(SoftwareAlias.Create(product.Id, nvdSource.Id,
            "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*"));

        var vuln = Vulnerability.Create("nvd", "CVE-2024-5555", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        db.Vulnerabilities.Add(vuln);

        var configs = JsonSerializer.Serialize(new[]
        {
            new NvdCachedCpeMatch(true, "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*",
                null, null, null, null)
        });
        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2024-5555", "desc", 7m, null,
            null, DateTimeOffset.UtcNow, "[]", configs));
        await db.SaveChangesAsync();

        var scopeFactory = new FakeScopeFactory(db);
        var runner = new NvdVulnerabilityEnrichmentRunner(
            scopeFactory, NullLogger<NvdVulnerabilityEnrichmentRunner>.Instance);

        var job = EnrichmentJob.Create(Guid.NewGuid(), "nvd",
            EnrichmentTargetModel.Vulnerability, vuln.Id, vuln.ExternalId, 100,
            DateTimeOffset.UtcNow);

        var result = await runner.ExecuteAsync(job, CancellationToken.None);

        result.Outcome.Should().Be(EnrichmentJobExecutionOutcome.Succeeded);
        var app = await db.VulnerabilityApplicabilities.SingleAsync();
        app.SoftwareProductId.Should().Be(product.Id);
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly PatchHoundDbContext _db;

        public FakeScopeFactory(PatchHoundDbContext db) => _db = db;

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public void Dispose() { }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(PatchHoundDbContext)) return _db;
            if (serviceType == typeof(VulnerabilityResolver)) return new VulnerabilityResolver(_db);
            return null;
        }
    }
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test PatchHound.slnx --filter "NvdCacheEnrichmentRunnerTests" -v minimal
```

Expected: FAIL — constructor signature mismatch (still expects `NvdApiClient`).

- [ ] **Step 3: Rewrite `NvdVulnerabilityEnrichmentRunner`**

Replace the entire file with:

```csharp
// src/PatchHound.Infrastructure/Services/NvdVulnerabilityEnrichmentRunner.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class NvdVulnerabilityEnrichmentRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<NvdVulnerabilityEnrichmentRunner> logger
) : IEnrichmentSourceRunner
{
    public string SourceKey => EnrichmentSourceCatalog.NvdSourceKey;
    public EnrichmentTargetModel TargetModel => EnrichmentTargetModel.Vulnerability;
    public TimeSpan MinimumDelay => TimeSpan.Zero;

    private Dictionary<string, Guid>? _cachedAliasMap;

    public async Task<EnrichmentJobExecutionResult> ExecuteAsync(
        EnrichmentJob job, CancellationToken ct)
    {
        if (job.TargetModel != EnrichmentTargetModel.Vulnerability)
            return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.Failed,
                $"Unsupported target model {job.TargetModel}.");

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<VulnerabilityResolver>();

        db.SetSystemContext(true);
        try
        {
            var vulnerability = await db.Vulnerabilities.IgnoreQueryFilters()
                .FirstOrDefaultAsync(v => v.Id == job.TargetId, ct);
            if (vulnerability is null)
                return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.NoData,
                    "Vulnerability no longer exists.");

            if (!vulnerability.ExternalId.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase))
                return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.NoData,
                    "Vulnerability external id is not a CVE.");

            var cached = await db.NvdCveCache
                .FirstOrDefaultAsync(c => c.CveId == vulnerability.ExternalId, ct);
            if (cached is null)
            {
                logger.LogDebug(
                    "CVE {CveId} not yet in NvdCveCache — will be populated on next feed sync.",
                    vulnerability.ExternalId);
                return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.NoData,
                    $"CVE {vulnerability.ExternalId} not in local cache yet.");
            }

            var aliasMap = _cachedAliasMap ??= await LoadAliasMapAsync(db, ct);
            var resolveInput = BuildResolveInput(vulnerability.ExternalId, cached, aliasMap);

            try
            {
                await resolver.ResolveAsync(resolveInput, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.Retry,
                    "Concurrency conflict persisting vulnerability.",
                    DateTimeOffset.UtcNow.AddMinutes(1));
            }

            logger.LogInformation(
                "NVD cache enrichment succeeded for CVE {CveId} (Vulnerability {VulnerabilityId}).",
                vulnerability.ExternalId, vulnerability.Id);

            return new EnrichmentJobExecutionResult(EnrichmentJobExecutionOutcome.Succeeded);
        }
        finally
        {
            db.SetSystemContext(false);
        }
    }

    private static async Task<Dictionary<string, Guid>> LoadAliasMapAsync(
        PatchHoundDbContext db, CancellationToken ct)
    {
        var sourceSystemId = await db.SourceSystems
            .Where(s => s.Key == "nvd")
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

        if (sourceSystemId is null)
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        return await db.SoftwareAliases
            .Where(a => a.SourceSystemId == sourceSystemId)
            .ToDictionaryAsync(a => a.ExternalId, a => a.SoftwareProductId,
                StringComparer.OrdinalIgnoreCase, ct);
    }

    internal static VulnerabilityResolveInput BuildResolveInput(
        string cveId,
        NvdCveCache cached,
        IReadOnlyDictionary<string, Guid> aliasMap)
    {
        var refs = JsonSerializer.Deserialize<List<NvdCachedReference>>(
            cached.ReferencesJson) ?? [];
        var cpes = JsonSerializer.Deserialize<List<NvdCachedCpeMatch>>(
            cached.ConfigurationsJson) ?? [];

        var references = refs
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Select(r => new VulnerabilityReferenceInput(
                r.Url,
                string.IsNullOrWhiteSpace(r.Source) ? "NVD" : r.Source,
                (IReadOnlyList<string>)r.Tags))
            .ToList();

        var applicabilities = cpes
            .Where(m => !string.IsNullOrWhiteSpace(m.Criteria))
            .Select(m => new VulnerabilityApplicabilityInput(
                SoftwareProductId: aliasMap.TryGetValue(m.Criteria, out var pid) ? pid : null,
                CpeCriteria: m.Criteria,
                Vulnerable: m.Vulnerable,
                VersionStartIncluding: m.VersionStartIncluding,
                VersionStartExcluding: m.VersionStartExcluding,
                VersionEndIncluding: m.VersionEndIncluding,
                VersionEndExcluding: m.VersionEndExcluding))
            .ToList();

        var severity = cached.CvssScore switch
        {
            >= 9.0m => Severity.Critical,
            >= 7.0m => Severity.High,
            >= 4.0m => Severity.Medium,
            _ => Severity.Low,
        };

        return new VulnerabilityResolveInput(
            Source: "nvd",
            ExternalId: cveId,
            Title: cveId,
            Description: cached.Description,
            VendorSeverity: severity,
            CvssScore: cached.CvssScore,
            CvssVector: cached.CvssVector,
            PublishedDate: cached.PublishedDate,
            References: references,
            Applicabilities: applicabilities);
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test PatchHound.slnx --filter "NvdCacheEnrichmentRunnerTests" -v minimal
```

Expected: 4 tests PASS.

- [ ] **Step 5: Build to verify**

```bash
dotnet build PatchHound.slnx
```

Expected: Build succeeded. (Will show errors about still-referenced `NvdApiClient` etc. — those are resolved in Task 6.)

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/NvdVulnerabilityEnrichmentRunner.cs \
        tests/PatchHound.Tests/Infrastructure/NvdCacheEnrichmentRunnerTests.cs
git commit -m "feat: rewrite NvdVulnerabilityEnrichmentRunner to read from NvdCveCache"
```

---

### Task 5: Update `EnrichmentSourceCatalog` and `EnrichmentJobEnqueuer`

**Files:**
- Modify: `src/PatchHound.Infrastructure/Tenants/EnrichmentSourceCatalog.cs`
- Modify: `src/PatchHound.Infrastructure/Services/EnrichmentJobEnqueuer.cs`

- [ ] **Step 1: Update `EnrichmentSourceCatalog`**

Replace the NVD-related members so NVD no longer requires credentials:

```csharp
// In EnrichmentSourceCatalog.cs — replace the NVD-related constants and methods:

// Remove:
//   public const string DefaultNvdApiBaseUrl = "https://services.nvd.nist.gov";
// Add:
public const string DefaultNvdFeedBaseUrl = "https://nvd.nist.gov/feeds/json/cve/1.1";
```

Replace `CreateDefaultNvd()`:
```csharp
public static EnrichmentSourceConfiguration CreateDefaultNvd()
{
    return EnrichmentSourceConfiguration.Create(
        NvdSourceKey,
        "NVD (Feed Sync)",
        false,
        apiBaseUrl: DefaultNvdFeedBaseUrl
    );
}
```

Replace `HasConfiguredCredentials` — add NVD to the "always configured" set:
```csharp
public static bool HasConfiguredCredentials(EnrichmentSourceConfiguration source)
{
    if (string.Equals(source.SourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(source.SourceKey, DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(source.SourceKey, EndOfLifeSourceKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(source.SourceKey, SupplyChainSourceKey, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return !string.IsNullOrWhiteSpace(source.SecretRef);
}
```

Replace `RequiresCredentials` — NVD no longer requires them:
```csharp
public static bool RequiresCredentials(string sourceKey)
{
    return !string.Equals(sourceKey, NvdSourceKey, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(sourceKey, DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(sourceKey, EndOfLifeSourceKey, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(sourceKey, SupplyChainSourceKey, StringComparison.OrdinalIgnoreCase);
}
```

Also remove `GetSecretKeyName` entirely (only called by the now-deleted `NvdGlobalConfigurationProvider`). If other callers exist, keep it — check first with:
```bash
grep -rn "GetSecretKeyName" src/ --include="*.cs"
```
If output is only `EnrichmentSourceCatalog.cs` itself, delete the method.

- [ ] **Step 2: Update `EnrichmentJobEnqueuer` — remove SecretRef gate for NVD**

In `EnrichmentJobEnqueuer.cs`, find the LINQ query that builds `enabledSourceKeys`:

```csharp
// Current (line ~55):
var enabledSourceKeys = enabledSources
    .Where(source =>
        string.Equals(source.SourceKey, EnrichmentSourceCatalog.DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
            ? defenderConfiguredForTenant
            : !string.IsNullOrWhiteSpace(source.SecretRef))
    .Select(source => source.SourceKey)
    ...
```

Replace with:
```csharp
var enabledSourceKeys = enabledSources
    .Where(source =>
        string.Equals(source.SourceKey, EnrichmentSourceCatalog.DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
            ? defenderConfiguredForTenant
            : string.Equals(source.SourceKey, EnrichmentSourceCatalog.NvdSourceKey, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(source.SecretRef))
    .Select(source => source.SourceKey)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build PatchHound.slnx
```

Expected: Build succeeded (or same errors from unreferenced legacy classes — resolved next task).

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Tenants/EnrichmentSourceCatalog.cs \
        src/PatchHound.Infrastructure/Services/EnrichmentJobEnqueuer.cs
git commit -m "feat: NVD source no longer requires API key credentials"
```

---

### Task 6: Delete all legacy NVD code + clean up DI

**Files to delete:**
- `src/PatchHound.Infrastructure/VulnerabilitySources/NvdApiClient.cs`
- `src/PatchHound.Infrastructure/VulnerabilitySources/NvdGlobalConfigurationProvider.cs`
- `src/PatchHound.Infrastructure/VulnerabilitySources/NvdVulnerabilitySource.cs`
- `src/PatchHound.Infrastructure/Services/NvdVulnerabilityEnricher.cs`
- `src/PatchHound.Core/Interfaces/IVulnerabilityEnricher.cs`
- `tests/PatchHound.Tests/Infrastructure/NvdVulnerabilityEnrichmentRunnerTests.cs`
- `tests/PatchHound.Tests/Infrastructure/NvdVulnerabilityEnricherTests.cs`

**Files to modify:**
- `src/PatchHound.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Delete legacy source files**

```bash
rm src/PatchHound.Infrastructure/VulnerabilitySources/NvdApiClient.cs
rm src/PatchHound.Infrastructure/VulnerabilitySources/NvdGlobalConfigurationProvider.cs
rm src/PatchHound.Infrastructure/VulnerabilitySources/NvdVulnerabilitySource.cs
rm src/PatchHound.Infrastructure/Services/NvdVulnerabilityEnricher.cs
rm src/PatchHound.Core/Interfaces/IVulnerabilityEnricher.cs
```

- [ ] **Step 2: Delete old test files**

```bash
rm tests/PatchHound.Tests/Infrastructure/NvdVulnerabilityEnrichmentRunnerTests.cs
rm tests/PatchHound.Tests/Infrastructure/NvdVulnerabilityEnricherTests.cs
```

- [ ] **Step 3: Remove NVD registrations from `DependencyInjection.cs`**

Remove these four lines from `DependencyInjection.cs`:

```csharp
// Remove:
services.AddScoped<NvdVulnerabilitySource>();
services.AddHttpClient<NvdApiClient>().AddExternalHttpPolicies(maxConnectionsPerServer: 1);
services.AddScoped<NvdGlobalConfigurationProvider>();
```

Also remove the `using PatchHound.Infrastructure.VulnerabilitySources;` line **only if** it was only used for NVD types (check — `DefenderVulnerabilitySource` and `EntraApplicationSource` still live there, so keep the using).

- [ ] **Step 4: Build to verify — no remaining references**

```bash
dotnet build PatchHound.slnx 2>&1
```

Expected: Build succeeded, 0 errors. If there are `CS0246` errors naming any of the deleted types, find and fix the caller:
```bash
grep -rn "NvdApiClient\|NvdGlobalConfigurationProvider\|NvdVulnerabilitySource\|NvdVulnerabilityEnricher\|IVulnerabilityEnricher" src/ --include="*.cs"
```

- [ ] **Step 5: Run full test suite**

```bash
dotnet test PatchHound.slnx -v minimal
```

Expected: All tests pass, 0 failures.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: delete legacy per-CVE NVD API code — replaced by bulk feed sync"
```

---

### Task 7: Verify end-to-end wiring

This task runs the app and confirms the feed sync worker starts, then confirms enrichment jobs process from cache.

- [ ] **Step 1: Verify the app builds in release mode**

```bash
dotnet build PatchHound.slnx -c Release
```

Expected: 0 errors, 0 warnings related to NVD.

- [ ] **Step 2: Run all tests one final time**

```bash
dotnet test PatchHound.slnx -v minimal
```

Expected: All tests pass, including new `NvdFeedSyncServiceTests` (3 tests) and `NvdCacheEnrichmentRunnerTests` (4 tests).

- [ ] **Step 3: Confirm no remaining NVD API references in source**

```bash
grep -rn "services.nvd.nist.gov\|NvdApiClient\|NvdGlobalConfigurationProvider\|NvdVulnerabilitySource\|NvdVulnerabilityEnricher\|IVulnerabilityEnricher" \
  src/ tests/ --include="*.cs"
```

Expected: No output.

- [ ] **Step 4: Confirm `NvdFeedSyncWorker` is registered**

```bash
grep -n "NvdFeedSyncWorker" src/PatchHound.Worker/Program.cs
```

Expected: one matching line with `AddHostedService<NvdFeedSyncWorker>()`.

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "chore: verify NVD bulk feed migration complete — all tests green"
```

---

## Summary of what was deleted vs added

| Deleted | Reason |
|---------|--------|
| `NvdApiClient.cs` | Per-CVE HTTP client — no longer called |
| `NvdGlobalConfigurationProvider.cs` | API key fetcher — bulk feeds need no auth |
| `NvdVulnerabilitySource.cs` | Dead code — `FetchVulnerabilitiesAsync` threw, never injected as `IVulnerabilitySource` |
| `NvdVulnerabilityEnricher.cs` | Never registered in DI, never resolved |
| `IVulnerabilityEnricher.cs` | Only one implementer (now deleted), never used |
| Old test files (2) | Test the deleted HTTP path |

| Added | Purpose |
|-------|---------|
| `NvdCveCache` entity + migration | Local cache of CVE data |
| `NvdFeedCheckpoint` entity + migration | Tracks per-feed last-sync state |
| `NvdFeedModels.cs` | Deserialises NVD 1.1 JSON feed format |
| `NvdFeedSyncService` | Download → parse → upsert logic |
| `NvdFeedSyncWorker` | Startup full-sync + 2h incremental |
| New test files (2) | Test the cache-based sync and enrichment |
