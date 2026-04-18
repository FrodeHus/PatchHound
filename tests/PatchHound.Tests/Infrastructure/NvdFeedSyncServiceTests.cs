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
        return $$$"""
            {
              "CVE_Items": [{
                "cve": {
                  "CVE_data_meta": {"ID": "{{{cveId}}}"},
                  "description": {"description_data": [{"lang": "en","value": "{{{description}}}"}]},
                  "references": {"reference_data": {{{refs}}}}
                },
                "configurations": {
                  "nodes": [{"cpe_match": [{"vulnerable": true,"cpe23Uri": "{{{criteria}}}"}],"children": []}]
                },
                "impact": {
                  "baseMetricV3": {"cvssV3": {"baseScore": {{{baseScore}}},"vectorString": "{{{vector}}}"}}
                },
                "publishedDate": "{{{publishedDate}}}",
                "lastModifiedDate": "{{{lastModifiedDate}}}"
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
