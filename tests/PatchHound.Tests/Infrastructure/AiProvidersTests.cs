using System.Net;
using System.Text;
using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.AiProviders;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class AiProvidersTests
{
    [Theory]
    [InlineData("http://ollama.local:11434")]
    [InlineData("http://ollama.local:11434/api")]
    [InlineData("http://ollama.local:11434/api/generate")]
    [InlineData("http://ollama.local:11434/api/chat")]
    [InlineData("http://ollama.local:11434/v1/chat/completions")]
    public async Task OllamaValidateAsync_NormalizesConfiguredBaseUrl(string configuredBaseUrl)
    {
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"response":"OK"}""", Encoding.UTF8, "application/json"),
            }
        );
        var provider = new OllamaAiProvider(new HttpClient(handler));

        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.Ollama,
            name: "Local Ollama",
            model: "llama3.1:8b",
            topP: 1.0m,
            baseUrl: configuredBaseUrl,
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

        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.Ollama,
            name: "Local Ollama",
            model: "llama3.1:8b",
            topP: 1.0m,
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

        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.Ollama,
            name: "Local Ollama",
            model: "llama3.1:8b",
            topP: 1.0m,
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
        var profile = TenantAiProfileFactory.Create(
            tenantId,
            providerType: TenantAiProviderType.OpenAi,
            name: "OpenAI",
            topP: 1.0m,
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
    public async Task OpenAiGenerateTextAsync_UsesResponsesApi_WhenProviderNativeResearchIsEnabled()
    {
        var tenantId = Guid.NewGuid();
        var handler = new RecordingHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"output_text":"Generated summary with research"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
        var provider = new OpenAiProvider(new HttpClient(handler));
        var profile = TenantAiProfileFactory.Create(
            tenantId,
            providerType: TenantAiProviderType.OpenAi,
            name: "OpenAI",
            topP: 1.0m,
            baseUrl: "https://api.openai.com/v1",
            secretRef: "secret"
        );

        var content = await provider.GenerateTextAsync(
            new AiTextGenerationRequest(
                "System",
                "User",
                UseProviderNativeWebResearch: true
            ),
            new TenantAiProfileResolved(profile, "api-key"),
            CancellationToken.None
        );

        content.Should().Be("Generated summary with research");
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.openai.com/v1/responses");
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
        var profile = TenantAiProfileFactory.Create(
            Guid.NewGuid(),
            providerType: TenantAiProviderType.AzureOpenAi,
            name: "Azure",
            model: "gpt-4o",
            topP: 1.0m,
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
