using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Credentials;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;

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

    private static NvdFeedSyncService CreateService(
        HttpClient httpClient,
        PatchHound.Infrastructure.Data.PatchHoundDbContext db,
        ISecretStore? secretStore = null
    )
    {
        var store = secretStore ?? NullSecrets();
        return new NvdFeedSyncService(
            httpClient,
            db,
            store,
            new StoredCredentialResolver(db, store),
            NullLogger<NvdFeedSyncService>.Instance
        );
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

        var handler = new GzipArchiveHandler(feedJson);
        var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, db);

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
        var service = CreateService(httpClient, db);

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

        var handler = new GzipArchiveHandler("{}");
        var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, db);

        await service.SyncYearFeedAsync(2024, CancellationToken.None);

        handler.RequestUris.Should().BeEmpty("no HTTP requests should be made when checkpoint exists");
        (await db.NvdCveCache.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SyncYearFeedAsync_force_refreshes_when_checkpoint_exists()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        db.NvdFeedCheckpoints.Add(NvdFeedCheckpoint.Create("2024", DateTimeOffset.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        var handler = new GzipArchiveHandler(BuildSinglePageResponse("CVE-2024-1234", "Forced refresh", 7.5m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N",
            "2024-01-01T00:00:00.000", "2024-01-10T00:00:00.000",
            referenceUrl: null, criteria: "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*"));
        var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, db);

        await service.SyncYearFeedAsync(2024, force: true, CancellationToken.None);

        handler.RequestUris.Should().ContainSingle();
        var cached = await db.NvdCveCache.SingleAsync();
        cached.CveId.Should().Be("CVE-2024-1234");
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

        var handler = new GzipArchiveHandler(feedJson);
        var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, db);

        await service.SyncYearFeedAsync(2024, CancellationToken.None);

        (await db.NvdCveCache.CountAsync()).Should().Be(0, "CVEs without English descriptions should be skipped");
        (await db.NvdFeedCheckpoints.CountAsync()).Should().Be(1, "checkpoint should still be created");
    }

    [Fact]
    public async Task SyncYearFeedAsync_downloads_and_processes_compressed_year_archive()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var handler = new GzipArchiveHandler(BuildSinglePageResponse("CVE-2024-1234", "Window 1", 7.5m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N",
            "2024-01-01T00:00:00.000", "2024-01-10T00:00:00.000",
            referenceUrl: null, criteria: "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*"));
        var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, db);

        await service.SyncYearFeedAsync(2024, CancellationToken.None);

        handler.RequestUris.Should().ContainSingle();
        handler.RequestUris[0].ToString().Should()
            .Be("https://nvd.nist.gov/feeds/json/cve/2.0/nvdcve-2.0-2024.json.gz");

        var cachedIds = await db.NvdCveCache
            .OrderBy(c => c.CveId)
            .Select(c => c.CveId)
            .ToListAsync();
        cachedIds.Should().Equal("CVE-2024-1234");
    }

    [Fact]
    public async Task SyncModifiedFeedAsync_uses_global_api_key_stored_credential()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var credential = StoredCredential.Create(
            "NVD API",
            StoredCredentialTypes.ApiKey,
            isGlobal: true,
            credentialTenantId: string.Empty,
            clientId: string.Empty,
            secretRef: "stored-credentials/nvd",
            now: DateTimeOffset.UtcNow
        );
        await db.StoredCredentials.AddAsync(credential);
        await db.EnrichmentSourceConfigurations.AddAsync(
            EnrichmentSourceConfiguration.Create(
                EnrichmentSourceCatalog.NvdSourceKey,
                "NVD API",
                true,
                secretRef: string.Empty,
                apiBaseUrl: EnrichmentSourceCatalog.DefaultNvdApiBaseUrl,
                storedCredentialId: credential.Id
            )
        );
        await db.SaveChangesAsync();

        var store = NullSecrets();
        store.GetSecretAsync(
                credential.SecretRef,
                StoredCredentialSecretKeys.ApiKey,
                Arg.Any<CancellationToken>()
            )
            .Returns("nvd-api-key");

        var handler = new FakeApiHandler(BuildSinglePageResponse("CVE-2024-1234", "Window 1", 7.5m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N",
            "2024-01-01T00:00:00.000", "2024-01-10T00:00:00.000",
            referenceUrl: null, criteria: "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*"));
        var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, db, store);

        await service.SyncModifiedFeedAsync(CancellationToken.None);

        handler.RequestCount.Should().Be(1);
        handler.ApiKeys.Should().ContainSingle().Which.Should().Be("nvd-api-key");
    }

    [Fact]
    public async Task SyncModifiedFeedAsync_streams_api_response_without_buffering_content()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var handler = new NonBufferableApiHandler(BuildSinglePageResponse("CVE-2024-1234", "Window 1", 7.5m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N",
            "2024-01-01T00:00:00.000", "2024-01-10T00:00:00.000",
            referenceUrl: null, criteria: "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*"));
        var httpClient = new HttpClient(handler);
        var service = CreateService(httpClient, db);

        await service.SyncModifiedFeedAsync(CancellationToken.None);

        var cached = await db.NvdCveCache.SingleAsync();
        cached.CveId.Should().Be("CVE-2024-1234");
    }

    private static string BuildSinglePageResponse(
        string cveId, string description, decimal baseScore, string vector,
        string publishedDate, string lastModifiedDate,
        string? referenceUrl, string criteria)
    {
        var payload = new
        {
            resultsPerPage = 1,
            startIndex = 0,
            totalResults = 1,
            vulnerabilities = new[]
            {
                new
                {
                    cve = new
                    {
                        id = cveId,
                        published = publishedDate,
                        lastModified = lastModifiedDate,
                        descriptions = new[]
                        {
                            new { lang = "en", value = description }
                        },
                        metrics = new
                        {
                            cvssMetricV31 = new[]
                            {
                                new
                                {
                                    type = "Primary",
                                    cvssData = new { baseScore, vectorString = vector }
                                }
                            }
                        },
                        references = referenceUrl is null
                            ? Array.Empty<object>()
                            : new object[]
                            {
                                new { url = referenceUrl, source = "MISC", tags = Array.Empty<string>() }
                            },
                        configurations = new[]
                        {
                            new
                            {
                                nodes = new[]
                                {
                                    new
                                    {
                                        cpeMatch = new[]
                                        {
                                            new { vulnerable = true, criteria }
                                        },
                                        nodes = Array.Empty<object>()
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    internal sealed class FakeApiHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        public int RequestCount { get; private set; }
        public List<string?> ApiKeys { get; } = [];

        public FakeApiHandler(string responseJson) => _responseJson = responseJson;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            ApiKeys.Add(
                request.Headers.TryGetValues("apiKey", out var values)
                    ? values.SingleOrDefault()
                    : null
            );
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }

    internal sealed class NonBufferableApiHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new NonBufferableJsonContent(responseJson),
            });
        }
    }

    internal sealed class NonBufferableJsonContent : HttpContent
    {
        private readonly byte[] _body;

        public NonBufferableJsonContent(string responseJson)
        {
            _body = Encoding.UTF8.GetBytes(responseJson);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("Response content was buffered before the stream was read.");

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Response content was buffered before the stream was read.");

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(_body, writable: false));

        protected override bool TryComputeLength(out long length)
        {
            length = _body.Length;
            return true;
        }
    }

    internal sealed class GzipArchiveHandler(string responseJson) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            RequestUris.Add(request.RequestUri!);
            var body = Compress(responseJson);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
        }

        private static byte[] Compress(string value)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            using (var writer = new StreamWriter(gzip, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(value);
            }

            return output.ToArray();
        }
    }
}
