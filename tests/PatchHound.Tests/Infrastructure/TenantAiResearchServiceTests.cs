using System.Net;
using System.Text.Json;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Options;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class TenantAiResearchServiceTests
{
    [Fact]
    public async Task ResearchAsync_ReturnsContextAndSources_FromManagedSearchResponse()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    Title: Bing
                    Markdown Content:
                    [Spring Framework Security Advisory](https://spring.io/security)
                    [NVD CVE Entry](https://nvd.nist.gov/vuln/detail/CVE-2026-0001)
                    Spring Framework remains widely deployed in enterprise environments.
                    """,
                    Encoding.UTF8,
                    "text/plain"
                ),
            }
        );
        await using var db = TestDbContextFactory.CreateSystemContext();
        var service = CreateService(db, handler);
        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.Ollama,
            allowExternalResearch: true,
            webResearchMode: TenantAiWebResearchMode.PatchHoundManaged
        );

        var result = await service.ResearchAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            new AiWebResearchRequest(
                "Spring Framework enterprise software",
                ["nvd.nist.gov", "spring.io"],
                5,
                true
            ),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Context.Should().Contain("External research context:");
        result.Value.Context.Should().Contain("Sources:");
        result.Value.Sources.Should().HaveCount(2);
        result.Value.Sources[0].Url.Should().Be("https://spring.io/security");
        handler.Requests.Should().HaveCount(3);
        handler.Requests[0].RequestUri!.ToString().Should().StartWith("https://r.jina.ai/http://www.google.com/search?");
        handler.Requests[0].RequestUri!.ToString().Should().Contain("site%3Anvd.nist.gov");
        handler.Requests[0].RequestUri!.ToString().Should().Contain("site%3Aspring.io");
        handler.Requests[1].RequestUri!.ToString().Should().Be("https://r.jina.ai/http://spring.io/security");
        handler.Requests[2].RequestUri!.ToString().Should().Be("https://r.jina.ai/http://nvd.nist.gov/vuln/detail/CVE-2026-0001");
    }

    [Fact]
    public async Task ResearchAsync_UsesConfiguredBingSearchProvider()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    Title: Bing
                    Markdown Content:
                    [NVD CVE Entry](https://nvd.nist.gov/vuln/detail/CVE-2026-0001)
                    """,
                    Encoding.UTF8,
                    "text/plain"
                ),
            }
        );
        await using var db = TestDbContextFactory.CreateSystemContext();
        var service = CreateService(
            db,
            handler,
            new AiResearchOptions { JinaSearchProvider = "Bing" }
        );
        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.Ollama,
            allowExternalResearch: true,
            webResearchMode: TenantAiWebResearchMode.PatchHoundManaged
        );

        await service.ResearchAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            new AiWebResearchRequest("CVE-2026-0001", [], 1, true),
            CancellationToken.None
        );

        handler.Requests[0].RequestUri!.ToString().Should().StartWith("https://r.jina.ai/http://www.bing.com/search?");
    }

    [Fact]
    public async Task ResearchAsync_UsesSelectedJinaReaderSourceAndSendsOptionalApiKey()
    {
        var handler = new RecordingHttpMessageHandler(
            request =>
            {
                if (request.RequestUri!.ToString().Contains("/search?"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """
                            Title: Jina Search
                            Markdown Content:
                            [Vendor advisory](https://vendor.example/advisory)
                            """,
                            Encoding.UTF8,
                            "text/plain"
                        ),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        Title: Vendor advisory
                        URL Source: https://vendor.example/advisory
                        Markdown Content:
                        Exploitation details and patch guidance.
                        """,
                        Encoding.UTF8,
                        "text/plain"
                    ),
                };
            }
        );
        await using var db = TestDbContextFactory.CreateSystemContext();
        db.EnrichmentSourceConfigurations.Add(
            EnrichmentSourceConfiguration.Create(
                EnrichmentSourceCatalog.JinaReaderSourceKey,
                "Jina Reader",
                true,
                "system/enrichment-sources/jina-reader",
                EnrichmentSourceCatalog.DefaultJinaReaderApiBaseUrl,
                targets: "AIResearch"
            )
        );
        await db.SaveChangesAsync();

        var secretStore = Substitute.For<ISecretStore>();
        secretStore
            .GetSecretAsync("system/enrichment-sources/jina-reader", "apiKey", Arg.Any<CancellationToken>())
            .Returns("jina-key");
        var service = CreateService(db, handler, secretStore: secretStore);
        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.Ollama,
            allowExternalResearch: true,
            webResearchMode: TenantAiWebResearchMode.PatchHoundManaged,
            researchSourceKey: EnrichmentSourceCatalog.JinaReaderSourceKey
        );

        var result = await service.ResearchAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            new AiWebResearchRequest(
                "CVE-2026-0001",
                [],
                1,
                true,
                Providers: [AiResearchProviderKind.ExternalWebSearch],
                ResearchSourceKey: EnrichmentSourceCatalog.JinaReaderSourceKey
            ),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Context.Should().Contain("Exploitation details and patch guidance.");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.ToString().Should().StartWith("https://r.jina.ai/http://www.google.com/search?");
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be("jina-key");
    }

    [Fact]
    public async Task ResearchAsync_UsesSelectedJinaReaderSource_ReturnsUnavailableNote_WhenJinaIsRateLimited()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(
                    "The requested URL returned error: 429 Too Many Requests",
                    Encoding.UTF8,
                    "text/plain"
                ),
            }
        );
        await using var db = TestDbContextFactory.CreateSystemContext();
        db.EnrichmentSourceConfigurations.Add(
            EnrichmentSourceConfiguration.Create(
                EnrichmentSourceCatalog.JinaReaderSourceKey,
                "Jina Reader",
                true,
                apiBaseUrl: EnrichmentSourceCatalog.DefaultJinaReaderApiBaseUrl,
                targets: "AIResearch"
            )
        );
        await db.SaveChangesAsync();

        var service = CreateService(db, handler);
        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.Ollama,
            allowExternalResearch: true,
            webResearchMode: TenantAiWebResearchMode.PatchHoundManaged,
            researchSourceKey: EnrichmentSourceCatalog.JinaReaderSourceKey
        );

        var result = await service.ResearchAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            new AiWebResearchRequest(
                "CVE-2026-0001",
                [],
                1,
                true,
                Providers: [AiResearchProviderKind.ExternalWebSearch],
                ResearchSourceKey: EnrichmentSourceCatalog.JinaReaderSourceKey
            ),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Context.Should().Be("External research was not available during the time of assessment");
        result.Value.Sources.Should().BeEmpty();
    }

    [Fact]
    public void AddHttpClient_CanResolveTenantAiResearchService_WithConfiguredOptions()
    {
        var services = new ServiceCollection();
        services.Configure<AiResearchOptions>(options => options.JinaSearchProvider = "Bing");
        services.AddSingleton(TestDbContextFactory.CreateSystemContext());
        services.AddScoped<LocalVulnerabilityIntelResearchProvider>();
        services.AddHttpClient<ExternalWebSearchResearchProvider>();
        services.AddScoped<IAiResearchSourceProvider>(sp => sp.GetRequiredService<ExternalWebSearchResearchProvider>());
        services.AddSingleton(Substitute.For<ISecretStore>());
        services.AddHttpClient<JinaReaderAiResearchProvider>();
        services.AddScoped<IAiResearchSourceProvider>(sp => sp.GetRequiredService<JinaReaderAiResearchProvider>());
        services.AddScoped<ITenantAiResearchService, TenantAiResearchService>();

        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<ITenantAiResearchService>();

        service.Should().BeOfType<TenantAiResearchService>();
    }

    [Fact]
    public async Task ResearchAsync_ReturnsLocalVulnerabilityIntel_WithoutExternalHttp()
    {
        await using var db = TestDbContextFactory.CreateSystemContext();
        var vulnerability = SeedLocalIntel(db);
        var handler = new RecordingHttpMessageHandler(_ =>
            throw new InvalidOperationException("External HTTP should not be used for local intel only.")
        );
        var service = CreateService(db, handler);
        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.Ollama,
            allowExternalResearch: false,
            webResearchMode: TenantAiWebResearchMode.Disabled
        );

        var result = await service.ResearchAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            new AiWebResearchRequest(
                "CVE-2026-1234",
                [],
                5,
                true,
                [vulnerability.Id],
                [AiResearchProviderKind.LocalVulnerabilityIntel]
            ),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Context.Should().Contain("Local vulnerability intel:");
        result.Value.Context.Should().Contain("NVD cached description");
        result.Value.Context.Should().Contain("Known exploited: true");
        result.Value.Context.Should().Contain("https://vendor.example/advisory");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResearchAsync_CombinesLocalIntelAndExternalWebSearch_WhenBothRequested()
    {
        await using var db = TestDbContextFactory.CreateSystemContext();
        var vulnerability = SeedLocalIntel(db);
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    Title: Search
                    Markdown Content:
                    [Vendor update](https://vendor.example/update)
                    Exploitation is being discussed publicly.
                    """,
                    Encoding.UTF8,
                    "text/plain"
                ),
            }
        );
        var service = CreateService(db, handler);
        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.Ollama,
            allowExternalResearch: true,
            webResearchMode: TenantAiWebResearchMode.PatchHoundManaged
        );

        var result = await service.ResearchAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            new AiWebResearchRequest(
                "CVE-2026-1234",
                [],
                5,
                true,
                [vulnerability.Id],
                [AiResearchProviderKind.LocalVulnerabilityIntel, AiResearchProviderKind.ExternalWebSearch]
            ),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Context.Should().Contain("Local vulnerability intel:");
        result.Value.Context.Should().Contain("NVD cached description");
        result.Value.Context.Should().Contain("External research context:");
        result.Value.Context.Should().Contain("Exploitation is being discussed publicly.");
        handler.Requests.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ResearchAsync_LocalIntelCacheMiss_ReturnsEmptyBundleWithoutExternalHttp()
    {
        await using var db = TestDbContextFactory.CreateSystemContext();
        var handler = new RecordingHttpMessageHandler(_ =>
            throw new InvalidOperationException("External HTTP should not be used for local intel only.")
        );
        var service = CreateService(db, handler);
        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.Ollama,
            allowExternalResearch: false,
            webResearchMode: TenantAiWebResearchMode.Disabled
        );

        var result = await service.ResearchAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            new AiWebResearchRequest(
                "CVE-2026-9999",
                [],
                5,
                true,
                [Guid.NewGuid()],
                [AiResearchProviderKind.LocalVulnerabilityIntel]
            ),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Context.Should().BeEmpty();
        result.Value.Sources.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    private static TenantAiResearchService CreateService(
        PatchHoundDbContext db,
        RecordingHttpMessageHandler handler,
        AiResearchOptions? options = null,
        ISecretStore? secretStore = null
    )
    {
        var httpClient = new HttpClient(handler);
        var externalProvider = new ExternalWebSearchResearchProvider(
            httpClient,
            Options.Create(options ?? new AiResearchOptions())
        );
        var jinaProvider = new JinaReaderAiResearchProvider(
            httpClient,
            db,
            secretStore ?? Substitute.For<ISecretStore>(),
            Options.Create(options ?? new AiResearchOptions())
        );
        return new TenantAiResearchService(
            new LocalVulnerabilityIntelResearchProvider(db),
            [externalProvider, jinaProvider],
            db
        );
    }

    private static Vulnerability SeedLocalIntel(PatchHoundDbContext db)
    {
        var vulnerability = Vulnerability.Create(
            "nvd",
            "CVE-2026-1234",
            "CVE-2026-1234",
            "Canonical description",
            Severity.Critical,
            9.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
        );
        db.Vulnerabilities.Add(vulnerability);
        db.VulnerabilityReferences.Add(
            VulnerabilityReference.Create(
                vulnerability.Id,
                "https://vendor.example/advisory",
                "Vendor",
                ["advisory"]
            )
        );
        db.VulnerabilityApplicabilities.Add(
            VulnerabilityApplicability.Create(
                vulnerability.Id,
                null,
                "cpe:2.3:a:vendor:product:*:*:*:*:*:*:*:*",
                true,
                "1.0",
                null,
                "2.0",
                null,
                "nvd"
            )
        );
        db.ThreatAssessments.Add(
            ThreatAssessment.Create(
                vulnerability.Id,
                95m,
                90m,
                88m,
                70m,
                0.91m,
                knownExploited: true,
                publicExploit: true,
                activeAlert: false,
                hasRansomwareAssociation: true,
                hasMalwareAssociation: false,
                "[]",
                "test"
            )
        );
        db.NvdCveCache.Add(
            NvdCveCache.Create(
                "CVE-2026-1234",
                "NVD cached description",
                9.8m,
                "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
                new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                DateTimeOffset.UtcNow,
                JsonSerializer.Serialize(
                    new[] { new NvdCachedReference("https://nvd.nist.gov/vuln/detail/CVE-2026-1234", "NVD", new List<string> { "nvd" }) }
                ),
                JsonSerializer.Serialize(
                    new[] { new NvdCachedCpeMatch(true, "cpe:2.3:a:vendor:product:*:*:*:*:*:*:*:*", "1.0", null, "2.0", null) }
                )
            )
        );
        db.SaveChanges();

        return vulnerability;
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder
    ) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
