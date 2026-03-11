using System.Net;
using System.Text;
using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.AiProviders;

namespace PatchHound.Tests.Infrastructure;

public class AiProvidersTests
{
    [Fact]
    public async Task OllamaValidateAsync_PerformsOutboundRequest()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"response":"OK"}""", Encoding.UTF8, "application/json"),
            }
        );
        var provider = new OllamaAiProvider(new HttpClient(handler));

        var profile = TenantAiProfile.Create(
            Guid.NewGuid(),
            "Local Ollama",
            TenantAiProviderType.Ollama,
            true,
            true,
            "llama3.1:8b",
            "Prompt",
            0.2m,
            1.0m,
            1200,
            60,
            baseUrl: "http://ollama.local:11434",
            keepAlive: "5m"
        );

        var result = await provider.ValidateAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.ToString().Should().Be("http://ollama.local:11434/api/generate");
    }

    [Fact]
    public async Task OllamaValidateAsync_AcceptsBaseUrlThatAlreadyIncludesApi()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"response":"OK"}""", Encoding.UTF8, "application/json"),
            }
        );
        var provider = new OllamaAiProvider(new HttpClient(handler));

        var profile = TenantAiProfile.Create(
            Guid.NewGuid(),
            "Local Ollama",
            TenantAiProviderType.Ollama,
            true,
            true,
            "llama3.1:8b",
            "Prompt",
            0.2m,
            1.0m,
            1200,
            60,
            baseUrl: "http://ollama.local:11434/api"
        );

        var result = await provider.ValidateAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.ToString().Should().Be("http://ollama.local:11434/api/generate");
    }

    [Fact]
    public async Task OllamaValidateAsync_FallsBackToOpenAiCompatibleEndpointWhenNativeApiReturns404()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString() == "http://ollama.local:11434/api/generate")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"OK"}}]}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        });
        var provider = new OllamaAiProvider(new HttpClient(handler));

        var profile = TenantAiProfile.Create(
            Guid.NewGuid(),
            "Local Ollama",
            TenantAiProviderType.Ollama,
            true,
            true,
            "llama3.1:8b",
            "Prompt",
            0.2m,
            1.0m,
            1200,
            60,
            baseUrl: "http://ollama.local:11434"
        );

        var result = await provider.ValidateAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Select(request => request.RequestUri!.ToString()).Should().ContainInOrder(
            "http://ollama.local:11434/api/generate",
            "http://ollama.local:11434/v1/chat/completions"
        );
    }

    [Fact]
    public async Task OllamaValidateAsync_ReturnsReadableModelNotFoundError()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"error":"model 'llama3.1:8b' not found"}""", Encoding.UTF8, "application/json"),
            }
        );
        var provider = new OllamaAiProvider(new HttpClient(handler));

        var profile = TenantAiProfile.Create(
            Guid.NewGuid(),
            "Local Ollama",
            TenantAiProviderType.Ollama,
            true,
            true,
            "llama3.1:8b",
            "Prompt",
            0.2m,
            1.0m,
            1200,
            60,
            baseUrl: "http://ollama.local:11434"
        );

        var result = await provider.ValidateAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("model 'llama3.1:8b' not found");
        result.Error.Should().NotContain("{\"error\"");
    }

    [Theory]
    [InlineData("http://ollama.local:11434/api/generate")]
    [InlineData("http://ollama.local:11434/api/chat")]
    [InlineData("http://ollama.local:11434/v1/chat/completions")]
    public async Task OllamaValidateAsync_NormalizesEndpointStyleBaseUrls(string configuredBaseUrl)
    {
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"response":"OK"}""", Encoding.UTF8, "application/json"),
            }
        );
        var provider = new OllamaAiProvider(new HttpClient(handler));

        var profile = TenantAiProfile.Create(
            Guid.NewGuid(),
            "Local Ollama",
            TenantAiProviderType.Ollama,
            true,
            true,
            "llama3.1:8b",
            "Prompt",
            0.2m,
            1.0m,
            1200,
            60,
            baseUrl: configuredBaseUrl
        );

        var result = await provider.ValidateAsync(
            new TenantAiProfileResolved(profile, string.Empty),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.ToString().Should().Be("http://ollama.local:11434/api/generate");
    }

    [Fact]
    public async Task OpenAiGenerateReportAsync_ReturnsParsedContent()
    {
        var tenantId = Guid.NewGuid();
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"Generated OpenAI report"}}]}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
        var provider = new OpenAiProvider(new HttpClient(handler));
        var request = BuildRequest(tenantId);
        var profile = TenantAiProfile.Create(
            tenantId,
            "OpenAI",
            TenantAiProviderType.OpenAi,
            true,
            true,
            "gpt-4.1-mini",
            "Prompt",
            0.2m,
            1.0m,
            1200,
            60,
            baseUrl: "https://api.openai.com/v1",
            secretRef: "secret"
        );

        var content = await provider.GenerateReportAsync(
            request,
            new TenantAiProfileResolved(profile, "api-key"),
            CancellationToken.None
        );

        content.Should().Be("Generated OpenAI report");
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
    }

    [Fact]
    public async Task AzureValidateAsync_PerformsChatCompletionRequest()
    {
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"OK"}}]}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
        var provider = new AzureOpenAiProvider(new HttpClient(handler));
        var profile = TenantAiProfile.Create(
            Guid.NewGuid(),
            "Azure",
            TenantAiProviderType.AzureOpenAi,
            true,
            true,
            "gpt-4o",
            "Prompt",
            0.2m,
            1.0m,
            1200,
            60,
            baseUrl: "https://example.openai.azure.com",
            deploymentName: "gpt-4o-prod",
            apiVersion: "2024-10-21",
            secretRef: "secret"
        );

        var result = await provider.ValidateAsync(
            new TenantAiProfileResolved(profile, "api-key"),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.ToString()
            .Should()
            .Be("https://example.openai.azure.com/openai/deployments/gpt-4o-prod/chat/completions?api-version=2024-10-21");
        handler.Requests[0].Headers.TryGetValues("api-key", out var headerValues).Should().BeTrue();
        headerValues!.Single().Should().Be("api-key");
    }

    private static AiReportGenerationRequest BuildRequest(Guid tenantId) =>
        new(
            VulnerabilityDefinition.Create(
                "CVE-2026-0001",
                "Test vulnerability",
                "Description",
                Severity.High,
                "NVD",
                8.1m
            ),
            [Asset.Create(tenantId, "asset-1", AssetType.Device, "srv-01", Criticality.High)]
        );

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
