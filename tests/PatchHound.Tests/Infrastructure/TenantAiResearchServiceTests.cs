using System.Net;
using System.Text;
using FluentAssertions;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Services;
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
        var service = new TenantAiResearchService(new HttpClient(handler));
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
        handler.Requests[0].RequestUri!.ToString().Should().Contain("site%3Anvd.nist.gov");
        handler.Requests[0].RequestUri!.ToString().Should().Contain("site%3Aspring.io");
        handler.Requests[1].RequestUri!.ToString().Should().Be("https://r.jina.ai/http://spring.io/security");
        handler.Requests[2].RequestUri!.ToString().Should().Be("https://r.jina.ai/http://nvd.nist.gov/vuln/detail/CVE-2026-0001");
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
