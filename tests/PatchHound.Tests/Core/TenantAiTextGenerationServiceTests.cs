using FluentAssertions;
using NSubstitute;
using PatchHound.Core.Common;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Core;

public class TenantAiTextGenerationServiceTests
{
    [Fact]
    public async Task GenerateResolvedAsync_AppliesProfileTimeoutSecondsToProviderCall()
    {
        var tenantId = Guid.NewGuid();
        var profile = TenantAiProfileFactory.Create(tenantId, timeoutSeconds: 1);
        var resolved = new TenantAiProfileResolved(profile, "secret");

        var provider = Substitute.For<IAiReportProvider>();
        provider.ProviderType.Returns(TenantAiProviderType.OpenAi);
        provider
            .GenerateTextAsync(Arg.Any<AiTextGenerationRequest>(), Arg.Any<TenantAiProfileResolved>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var providerCt = call.Arg<CancellationToken>();
                await Task.Delay(TimeSpan.FromSeconds(5), providerCt);
                return "should not reach";
            });

        var service = new TenantAiTextGenerationService([provider], Substitute.For<ITenantAiConfigurationResolver>());

        var result = await service.GenerateResolvedAsync(
            resolved,
            new AiTextGenerationRequest(
                SystemPrompt: string.Empty,
                UserPrompt: "test",
                ExternalContext: null,
                UseProviderNativeWebResearch: false,
                MaxResearchSources: 0,
                IncludeCitations: false,
                MaxOutputTokens: 100),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("timed out after 1s");
    }

    [Fact]
    public async Task GenerateResolvedAsync_OuterCancellationPropagatesAsGenericFailure()
    {
        var tenantId = Guid.NewGuid();
        var profile = TenantAiProfileFactory.Create(tenantId, timeoutSeconds: 60);
        var resolved = new TenantAiProfileResolved(profile, "secret");

        var provider = Substitute.For<IAiReportProvider>();
        provider.ProviderType.Returns(TenantAiProviderType.OpenAi);
        provider
            .GenerateTextAsync(Arg.Any<AiTextGenerationRequest>(), Arg.Any<TenantAiProfileResolved>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var providerCt = call.Arg<CancellationToken>();
                await Task.Delay(TimeSpan.FromSeconds(5), providerCt);
                return "should not reach";
            });

        var service = new TenantAiTextGenerationService([provider], Substitute.For<ITenantAiConfigurationResolver>());

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var result = await service.GenerateResolvedAsync(
            resolved,
            new AiTextGenerationRequest(
                SystemPrompt: string.Empty,
                UserPrompt: "test",
                ExternalContext: null,
                UseProviderNativeWebResearch: false,
                MaxResearchSources: 0,
                IncludeCitations: false,
                MaxOutputTokens: 100),
            cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotContain("timed out after");
    }
}
