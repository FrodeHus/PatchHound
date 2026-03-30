using FluentAssertions;
using NSubstitute;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Core;

public class AiReportServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _profileId = Guid.NewGuid();

    private readonly IAiReportProvider _azureProvider;
    private readonly ITenantAiConfigurationResolver _resolver;
    private readonly AiReportService _service;

    public AiReportServiceTests()
    {
        _azureProvider = Substitute.For<IAiReportProvider>();
        _azureProvider.ProviderType.Returns(TenantAiProviderType.AzureOpenAi);
        _resolver = Substitute.For<ITenantAiConfigurationResolver>();

        _service = new AiReportService(new[] { _azureProvider }, _resolver);
    }

    [Fact]
    public async Task GenerateReport_DefaultProfileFound_ReturnsAIReport()
    {
        var vulnerability = VulnerabilityDefinition.Create(
            "CVE-2025-1234",
            "Test Vulnerability",
            "A critical vulnerability",
            Severity.Critical,
            "Defender",
            9.8m
        );

        var assets = new List<Asset>
        {
            Asset.Create(_tenantId, "asset-1", AssetType.Device, "web-server-01", Criticality.High),
        };

        var profile = TenantAiProfileFactory.Create(
            _tenantId,
            providerType: TenantAiProviderType.AzureOpenAi,
            name: "Default analysis",
            model: "gpt-4o",
            systemPrompt: "System prompt",
            topP: 1.0m,
            baseUrl: "https://example.openai.azure.com",
            deploymentName: "gpt-4o-prod",
            apiVersion: "2024-10-21",
            secretRef: "tenants/test/ai/default"
        );

        _resolver
            .ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));

        _azureProvider
            .GenerateReportAsync(
                Arg.Any<AiReportGenerationRequest>(),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("# AI Report\n\nThis is a generated report.");

        var result = await _service.GenerateReportAsync(
            vulnerability,
            Guid.NewGuid(),
            assets,
            _tenantId,
            _userId,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantAiProfileId.Should().Be(profile.Id);
        result.Value.TenantId.Should().Be(_tenantId);
        result.Value.GeneratedBy.Should().Be(_userId);
        result.Value.ProviderType.Should().Be(TenantAiProviderType.AzureOpenAi.ToString());
        result.Value.ProfileName.Should().Be("Default analysis");
        result.Value.Model.Should().Be("gpt-4o");
        result.Value.MaxOutputTokens.Should().Be(1200);
        result.Value.Temperature.Should().Be(0.2m);
        result.Value.Content.Should().Be("# AI Report\n\nThis is a generated report.");
        result.Value.SystemPromptHash.Should().NotBeEmpty();
        result.Value.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GenerateReport_NoDefaultProfileConfigured_ReturnsFailure()
    {
        var vulnerability = VulnerabilityDefinition.Create(
            "CVE-2025-1234",
            "Test Vulnerability",
            "A critical vulnerability",
            Severity.Critical,
            "Defender"
        );

        _resolver
            .ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Failure("No enabled default AI profile is configured for this tenant."));

        var result = await _service.GenerateReportAsync(
            vulnerability,
            Guid.NewGuid(),
            [],
            _tenantId,
            _userId,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("No enabled default AI profile");
    }

    [Fact]
    public async Task GenerateReport_WhenProviderThrows_ReturnsFailure()
    {
        var vulnerability = VulnerabilityDefinition.Create(
            "CVE-2025-5678",
            "Another Vulnerability",
            "Description",
            Severity.High,
            "Qualys"
        );

        var profile = TenantAiProfileFactory.Create(
            _tenantId,
            providerType: TenantAiProviderType.AzureOpenAi,
            name: "Broken Azure",
            model: "gpt-4o",
            systemPrompt: "System prompt"
        );

        _resolver
            .ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, string.Empty)));
        _azureProvider
            .GenerateReportAsync(
                Arg.Any<AiReportGenerationRequest>(),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<string>>(_ => throw new InvalidOperationException("API key is required for Azure OpenAI."));

        var result = await _service.GenerateReportAsync(
            vulnerability,
            Guid.NewGuid(),
            [],
            _tenantId,
            _userId,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("API key is required for Azure OpenAI.");
    }

    [Fact]
    public async Task GenerateReport_ProfileOverride_SelectsRequestedProfile()
    {
        var vulnerability = VulnerabilityDefinition.Create(
            "CVE-2025-9999",
            "Test",
            "Desc",
            Severity.Medium,
            "Scanner"
        );

        var profile = TenantAiProfileFactory.Create(
            _tenantId,
            providerType: TenantAiProviderType.AzureOpenAi,
            name: "Ollama local",
            isDefault: false,
            model: "gpt-4o-mini",
            systemPrompt: "System prompt",
            temperature: 0.1m,
            maxOutputTokens: 900,
            timeoutSeconds: 45,
            baseUrl: "https://example.openai.azure.com",
            deploymentName: "gpt-4o-mini",
            apiVersion: "2024-10-21",
            secretRef: "tenants/test/ai/alt"
        );

        _resolver
            .ResolveByIdAsync(_tenantId, _profileId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));
        _azureProvider
            .GenerateReportAsync(
                Arg.Any<AiReportGenerationRequest>(),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("Azure report");

        var result = await _service.GenerateReportAsync(
            vulnerability,
            Guid.NewGuid(),
            [],
            _tenantId,
            _userId,
            _profileId,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().Be("Azure report");
        await _resolver.Received(1).ResolveByIdAsync(_tenantId, _profileId, Arg.Any<CancellationToken>());
    }
}
