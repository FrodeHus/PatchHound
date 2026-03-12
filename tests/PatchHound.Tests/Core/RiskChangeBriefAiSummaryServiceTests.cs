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

public class RiskChangeBriefAiSummaryServiceTests
{
    private readonly ITenantAiConfigurationResolver _resolver;
    private readonly ITenantAiResearchService _researchService;
    private readonly IAiReportProvider _provider;
    private readonly TenantAiTextGenerationService _textGenerationService;
    private readonly RiskChangeBriefAiSummaryService _service;

    public RiskChangeBriefAiSummaryServiceTests()
    {
        _resolver = Substitute.For<ITenantAiConfigurationResolver>();
        _researchService = Substitute.For<ITenantAiResearchService>();
        _provider = Substitute.For<IAiReportProvider>();
        _provider.ProviderType.Returns(TenantAiProviderType.OpenAi);
        _provider.ValidateAsync(Arg.Any<TenantAiProfileResolved>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AiProviderValidationResult.Success()));
        _textGenerationService = new TenantAiTextGenerationService([_provider], _resolver);
        _service = new RiskChangeBriefAiSummaryService(
            _resolver,
            _researchService,
            _textGenerationService
        );
    }

    [Fact]
    public async Task GenerateAsync_ReturnsNull_WhenResearchIsDisabled()
    {
        var tenantId = Guid.NewGuid();
        var profile = TenantAiProfileFactory.Create(tenantId, allowExternalResearch: false);
        _resolver.ResolveDefaultAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));

        var result = await _service.GenerateAsync(
            tenantId,
            CreateBrief(),
            CancellationToken.None
        );

        result.Should().BeNull();
        await _provider
            .DidNotReceive()
            .GenerateTextAsync(Arg.Any<AiTextGenerationRequest>(), Arg.Any<TenantAiProfileResolved>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_UsesProviderNativeResearch_ForOpenAi()
    {
        var tenantId = Guid.NewGuid();
        var profile = TenantAiProfileFactory.Create(
            tenantId,
            providerType: TenantAiProviderType.OpenAi,
            allowExternalResearch: true,
            webResearchMode: TenantAiWebResearchMode.ProviderNative,
            allowedDomains: "nvd.nist.gov, cisa.gov"
        );
        _resolver.ResolveDefaultAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));
        _resolver.ResolveByIdAsync(tenantId, profile.Id, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));
        _provider
            .GenerateTextAsync(
                Arg.Any<AiTextGenerationRequest>(),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult("summary"));

        var result = await _service.GenerateAsync(tenantId, CreateBrief(), CancellationToken.None);

        result.Should().Be("summary");
        await _provider
            .Received()
            .GenerateTextAsync(
                Arg.Is<AiTextGenerationRequest>(request =>
                    request.UseProviderNativeWebResearch
                    && request.AllowedDomains != null
                    && request.AllowedDomains.Count == 2
                    && request.AllowedDomains.Contains("nvd.nist.gov")
                ),
                Arg.Is<TenantAiProfileResolved>(resolved => resolved.Profile.Id == profile.Id),
                Arg.Any<CancellationToken>()
            );
    }

    private static RiskChangeBriefSummaryInput CreateBrief()
    {
        return new RiskChangeBriefSummaryInput(
            1,
            0,
            [
                new RiskChangeBriefSummaryItemInput(
                    "CVE-2026-0001",
                    "Test vulnerability",
                    "Critical",
                    2,
                    DateTimeOffset.UtcNow
                ),
            ],
            []
        );
    }
}
