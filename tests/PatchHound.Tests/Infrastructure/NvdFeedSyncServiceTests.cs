using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure;

public class NvdFeedSyncServiceTests
{
    private static ISecretStore NullSecrets()
    {
        var store = Substitute.For<ISecretStore>();
        store.GetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        return store;
    }

    [Fact]
    public async Task SyncYearFeedAsync_inserts_new_cve_entries()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var feedJson = BuildSinglePageResponse("CVE-2024-1234", "Test description", 7.5m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N",
            "2024-01-01T00:00:00.000", "2024-01-10T00:00:00.000",
            referenceUrl: "https://example.com/advisory",
            criteria: "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*");

        var handler = new FakeApiHandler(feedJson);
        var httpClient = new HttpClient(handler);
        var service = new NvdFeedSyncService(httpClient, db, NullSecrets(),
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
    public async Task SyncYearFeedAsync_updates_existing_cve_entry()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2024-1234", "old description",
            null, null, null, DateTimeOffset.MinValue, "[]", "[]"));
        db.NvdFeedCheckpoints.Add(NvdFeedCheckpoint.Create("2024",
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        // Year feed is skipped when checkpoint exists — so we simulate a direct call via modified feed
        var feedJson = BuildSinglePageResponse("CVE-2024-1234", "updated description", 9.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:C/C:H/I:H/A:H",
            "2024-01-01T00:00:00.000", "2024-01-15T00:00:00.000",
            referenceUrl: null, criteria: "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");

        var handler = new FakeApiHandler(feedJson);
        var httpClient = new HttpClient(handler);
        var service = new NvdFeedSyncService(httpClient, db, NullSecrets(),
            NullLogger<NvdFeedSyncService>.Instance);

        await service.SyncModifiedFeedAsync(CancellationToken.None);

        var cached = await db.NvdCveCache.SingleAsync();
        cached.Description.Should().Be("updated description");
        cached.CvssScore.Should().Be(9.8m);
    }

    [Fact]
    public async Task SyncYearFeedAsync_skips_when_checkpoint_exists()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        db.NvdFeedCheckpoints.Add(NvdFeedCheckpoint.Create("2024", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var handler = new FakeApiHandler("{}");
        var httpClient = new HttpClient(handler);
        var service = new NvdFeedSyncService(httpClient, db, NullSecrets(),
            NullLogger<NvdFeedSyncService>.Instance);

        await service.SyncYearFeedAsync(2024, CancellationToken.None);

        handler.RequestCount.Should().Be(0, "no HTTP requests should be made when checkpoint exists");
        (await db.NvdCveCache.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SyncYearFeedAsync_skips_cve_with_no_english_description()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var feedJson = """
            {
              "resultsPerPage": 1,
              "startIndex": 0,
              "totalResults": 1,
              "vulnerabilities": [{
                "cve": {
                  "id": "CVE-2024-9000",
                  "published": "2024-01-01T00:00:00.000",
                  "lastModified": "2024-01-10T00:00:00.000",
                  "descriptions": [{"lang": "es", "value": "descripción"}],
                  "metrics": {},
                  "references": [],
                  "configurations": []
                }
              }]
            }
            """;

        var handler = new FakeApiHandler(feedJson);
        var httpClient = new HttpClient(handler);
        var service = new NvdFeedSyncService(httpClient, db, NullSecrets(),
            NullLogger<NvdFeedSyncService>.Instance);

        await service.SyncYearFeedAsync(2024, CancellationToken.None);

        (await db.NvdCveCache.CountAsync()).Should().Be(0, "CVEs without English descriptions should be skipped");
        (await db.NvdFeedCheckpoints.CountAsync()).Should().Be(1, "checkpoint should still be created");
    }

    private static string BuildSinglePageResponse(
        string cveId, string description, decimal baseScore, string vector,
        string publishedDate, string lastModifiedDate,
        string? referenceUrl, string criteria)
    {
        var refs = referenceUrl is null ? "[]"
            : $$$"""[{"url":"{{{referenceUrl}}}","source":"MISC","tags":[]}]""";
        return $$$"""
            {
              "resultsPerPage": 1,
              "startIndex": 0,
              "totalResults": 1,
              "vulnerabilities": [{
                "cve": {
                  "id": "{{{cveId}}}",
                  "published": "{{{publishedDate}}}",
                  "lastModified": "{{{lastModifiedDate}}}",
                  "descriptions": [{"lang": "en", "value": "{{{description}}}"}],
                  "metrics": {
                    "cvssMetricV31": [{
                      "type": "Primary",
                      "cvssData": {"baseScore": {{{baseScore}}}, "vectorString": "{{{vector}}}"}
                    }]
                  },
                  "references": {{{refs}}},
                  "configurations": [{
                    "nodes": [{"cpeMatch": [{"vulnerable": true, "criteria": "{{{criteria}}}"}], "nodes": []}]
                  }]
                }
              }]
            }
            """;
    }

    internal sealed class FakeApiHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        public int RequestCount { get; private set; }

        public FakeApiHandler(string responseJson) => _responseJson = responseJson;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }
}
