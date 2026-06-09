using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Services;
using PatchHound.Core.Common;
using PatchHound.Core.Constants;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Models.OperationalContext;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class AiRecommendationDraftServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;

    public AiRecommendationDraftServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
    }

    [Fact]
    public async Task GenerateAsync_LimitsPromptToMostRelevantPatchAssessments()
    {
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var device = CanonicalTestData.MakeDevice(_tenantId);
        var installedSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, device.Id, product.Id);

        await _dbContext.AddRangeAsync(product, remediationCase, device, installedSoftware);

        for (var i = 0; i < 26; i++)
        {
            var vulnerability = CreateVulnerability($"CVE-2026-HIGH-{i:00}", 9.9m - (i * 0.1m));
            var exposure = CreateExposure(device.Id, product.Id, installedSoftware.Id, vulnerability.Id);
            var assessment = CreateAssessment(
                vulnerability.Id,
                PatchUrgencyTier.Emergency,
                $"Emergency reason {i:00}");
            await _dbContext.AddRangeAsync(vulnerability, exposure, assessment);
        }

        var lowPriorityVulnerability = CreateVulnerability("CVE-2026-LOW-OUT", 9.8m);
        var lowPriorityExposure = CreateExposure(device.Id, product.Id, installedSoftware.Id, lowPriorityVulnerability.Id);
        var lowPriorityAssessment = CreateAssessment(
            lowPriorityVulnerability.Id,
            PatchUrgencyTier.LowPriority,
            "Low priority reason should not be sent.");
        await _dbContext.AddRangeAsync(lowPriorityVulnerability, lowPriorityExposure, lowPriorityAssessment);
        await _dbContext.SaveChangesAsync();

        AiTextGenerationRequest? capturedRequest = null;
        var provider = Substitute.For<IAiReportProvider>();
        provider.ProviderType.Returns(TenantAiProviderType.OpenAi);
        provider
            .GenerateTextAsync(
                Arg.Do<AiTextGenerationRequest>(request => capturedRequest = request),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>())
            .Returns("""
            {
              "recommendedOutcome": "ApprovedForPatching",
              "priorityOverride": "Critical",
              "rationale": "Patch the highest urgency vulnerabilities first."
            }
            """);

        var profile = TenantAiProfileFactory.Create(_tenantId, name: "Recommendation profile");
        var aiResolver = Substitute.For<ITenantAiConfigurationResolver>();
        aiResolver.ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));

        var contextService = Substitute.For<IAiOperationalContextService>();

        var service = new AiRecommendationDraftService(
            _dbContext,
            new TenantAiTextGenerationService([provider], aiResolver),
            aiResolver,
            contextService
        );

        var result = await service.GenerateAsync(_tenantId, remediationCase.Id, _userId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.UserPrompt.Should().Contain("Only the top 25 of 27 assessments are included");
        capturedRequest.UserPrompt.Should().Contain("CVE-2026-HIGH-00");
        capturedRequest.UserPrompt.Should().NotContain("CVE-2026-LOW-OUT");
        capturedRequest.UserPrompt.Should().NotContain("Low priority reason should not be sent.");
    }

    [Fact]
    public async Task GenerateAsync_injects_operational_context_when_profile_allows()
    {
        var caseId = await SeedSingleAssessmentCaseAsync();

        AiTextGenerationRequest? capturedRequest = null;
        var provider = StubProvider(
            request => capturedRequest = request,
            """
            {
              "recommendedOutcome": "ApprovedForPatching",
              "priorityOverride": "Critical",
              "rationale": "Patch now.",
              "citations": ["device-risk-top-1"]
            }
            """);

        var profile = TenantAiProfileFactory.Create(_tenantId, allowOperationalContext: true);
        var aiResolver = ResolverFor(profile);

        var contextService = Substitute.For<IAiOperationalContextService>();
        contextService
            .BuildForRemediationCaseAsync(_tenantId, caseId, Arg.Any<AiOperationalContextOptions>(), Arg.Any<CancellationToken>())
            .Returns(PackWith("device-risk-top-1"));

        var service = new AiRecommendationDraftService(
            _dbContext,
            new TenantAiTextGenerationService([provider], aiResolver),
            aiResolver,
            contextService);

        var result = await service.GenerateAsync(_tenantId, caseId, _userId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        await contextService.Received(1).BuildForRemediationCaseAsync(
            _tenantId, caseId, Arg.Any<AiOperationalContextOptions>(), Arg.Any<CancellationToken>());
        capturedRequest.Should().NotBeNull();
        capturedRequest!.OperationalContext.Should().NotBeNull();
        result.Value.OperationalContextUsed.Should().BeTrue();
        result.Value.Uncited.Should().BeFalse();
        result.Value.Citations.Should().NotBeNull();
        result.Value.Citations!.Select(c => c.Key).Should().Equal("device-risk-top-1");
    }

    [Fact]
    public async Task GenerateAsync_skips_context_when_profile_disallows()
    {
        var caseId = await SeedSingleAssessmentCaseAsync();

        var provider = StubProvider(
            _ => { },
            """
            {
              "recommendedOutcome": "ApprovedForPatching",
              "priorityOverride": "Critical",
              "rationale": "Patch now."
            }
            """);

        var profile = TenantAiProfileFactory.Create(_tenantId, allowOperationalContext: false);
        var aiResolver = ResolverFor(profile);
        var contextService = Substitute.For<IAiOperationalContextService>();

        var service = new AiRecommendationDraftService(
            _dbContext,
            new TenantAiTextGenerationService([provider], aiResolver),
            aiResolver,
            contextService);

        var result = await service.GenerateAsync(_tenantId, caseId, _userId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        await contextService.DidNotReceiveWithAnyArgs().BuildForRemediationCaseAsync(
            default, default, default!, default);
        result.Value.OperationalContextUsed.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_marks_uncited_when_model_cites_unknown_keys()
    {
        var caseId = await SeedSingleAssessmentCaseAsync();

        var provider = StubProvider(
            _ => { },
            """
            {
              "recommendedOutcome": "ApprovedForPatching",
              "priorityOverride": "Critical",
              "rationale": "Patch now.",
              "citations": ["nope"]
            }
            """);

        var profile = TenantAiProfileFactory.Create(_tenantId, allowOperationalContext: true);
        var aiResolver = ResolverFor(profile);

        var contextService = Substitute.For<IAiOperationalContextService>();
        contextService
            .BuildForRemediationCaseAsync(_tenantId, caseId, Arg.Any<AiOperationalContextOptions>(), Arg.Any<CancellationToken>())
            .Returns(PackWith("device-risk-top-1"));

        var service = new AiRecommendationDraftService(
            _dbContext,
            new TenantAiTextGenerationService([provider], aiResolver),
            aiResolver,
            contextService);

        var result = await service.GenerateAsync(_tenantId, caseId, _userId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.OperationalContextUsed.Should().BeTrue();
        result.Value.Uncited.Should().BeTrue();
        result.Value.Citations.Should().BeEmpty();
        result.Value.RecommendedOutcome.Should().Be("ApprovedForPatching");
        result.Value.PriorityOverride.Should().Be("Critical");
        result.Value.Rationale.Should().Be("Patch now.");
    }

    [Fact]
    public async Task GenerateAsync_persists_context_snapshot_when_grounded()
    {
        var caseId = await SeedSingleAssessmentCaseAsync();
        const string packJson = "{\"contextKind\":\"RemediationCase\"}";

        var provider = StubProvider(
            _ => { },
            """
            {
              "recommendedOutcome": "ApprovedForPatching",
              "priorityOverride": "Critical",
              "rationale": "Patch now.",
              "citations": ["device-risk-top-1"]
            }
            """);

        var profile = TenantAiProfileFactory.Create(_tenantId, allowOperationalContext: true);
        var aiResolver = ResolverFor(profile);

        var contextService = Substitute.For<IAiOperationalContextService>();
        contextService
            .BuildForRemediationCaseAsync(_tenantId, caseId, Arg.Any<AiOperationalContextOptions>(), Arg.Any<CancellationToken>())
            .Returns(PackWithJson(packJson, "device-risk-top-1"));

        var service = new AiRecommendationDraftService(
            _dbContext,
            new TenantAiTextGenerationService([provider], aiResolver),
            aiResolver,
            contextService);

        var result = await service.GenerateAsync(_tenantId, caseId, _userId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.ContextSnapshotId.Should().NotBeNull();

        var snapshot = await _dbContext.RecommendationContextSnapshots
            .FirstOrDefaultAsync(s => s.Id == result.Value.ContextSnapshotId);
        snapshot.Should().NotBeNull();
        snapshot!.TenantId.Should().Be(_tenantId);
        snapshot.RemediationCaseId.Should().Be(caseId);
        snapshot.GeneratedBy.Should().Be(_userId);
        snapshot.ContextJson.Should().Be(packJson);
        snapshot.ContextHash.Should().HaveLength(64);
    }

    [Fact]
    public async Task GenerateAsync_does_not_persist_snapshot_when_context_disallowed()
    {
        var caseId = await SeedSingleAssessmentCaseAsync();

        var provider = StubProvider(
            _ => { },
            """
            {
              "recommendedOutcome": "ApprovedForPatching",
              "priorityOverride": "Critical",
              "rationale": "Patch now."
            }
            """);

        var profile = TenantAiProfileFactory.Create(_tenantId, allowOperationalContext: false);
        var aiResolver = ResolverFor(profile);
        var contextService = Substitute.For<IAiOperationalContextService>();

        var service = new AiRecommendationDraftService(
            _dbContext,
            new TenantAiTextGenerationService([provider], aiResolver),
            aiResolver,
            contextService);

        var result = await service.GenerateAsync(_tenantId, caseId, _userId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.ContextSnapshotId.Should().BeNull();
        (await _dbContext.RecommendationContextSnapshots.AnyAsync()).Should().BeFalse();
    }

    private static IAiReportProvider StubProvider(Action<AiTextGenerationRequest> capture, string response)
    {
        var provider = Substitute.For<IAiReportProvider>();
        provider.ProviderType.Returns(TenantAiProviderType.OpenAi);
        provider
            .GenerateTextAsync(
                Arg.Do(capture),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        return provider;
    }

    private ITenantAiConfigurationResolver ResolverFor(TenantAiProfile profile)
    {
        var aiResolver = Substitute.For<ITenantAiConfigurationResolver>();
        aiResolver.ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));
        return aiResolver;
    }

    private static AiOperationalContextResult PackWith(params string[] keys) =>
        PackWithJson("{}", keys);

    private static AiOperationalContextResult PackWithJson(string packJson, params string[] keys) =>
        new()
        {
            Pack = new OperationalContextPack(),
            PackJson = packJson,
            Citations = keys
                .Select((key, index) => new OperationalContextCitation
                {
                    Key = key,
                    EntityType = "Device",
                    EntityId = Guid.NewGuid(),
                    Label = $"label-{index}",
                    Fact = $"fact-{index}",
                })
                .ToList(),
        };

    private async Task<Guid> SeedSingleAssessmentCaseAsync()
    {
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var device = CanonicalTestData.MakeDevice(_tenantId);
        var installedSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, device.Id, product.Id);
        var vulnerability = CreateVulnerability("CVE-2026-CTX-01", 9.5m);
        var exposure = CreateExposure(device.Id, product.Id, installedSoftware.Id, vulnerability.Id);
        var assessment = CreateAssessment(vulnerability.Id, PatchUrgencyTier.Emergency, "Emergency reason.");

        await _dbContext.AddRangeAsync(
            product, remediationCase, device, installedSoftware, vulnerability, exposure, assessment);
        await _dbContext.SaveChangesAsync();
        return remediationCase.Id;
    }

    private static Vulnerability CreateVulnerability(string externalId, decimal cvssScore) =>
        Vulnerability.Create(
            "nvd",
            externalId,
            "Remote code execution",
            "A remotely exploitable vulnerability.",
            Severity.Critical,
            cvssScore,
            null,
            DateTimeOffset.UtcNow.AddDays(-30)
        );

    private DeviceVulnerabilityExposure CreateExposure(
        Guid deviceId,
        Guid productId,
        Guid installedSoftwareId,
        Guid vulnerabilityId) =>
        DeviceVulnerabilityExposure.Observe(
            _tenantId,
            deviceId,
            vulnerabilityId,
            productId,
            installedSoftwareId,
            "1.2.3",
            ExposureMatchSource.Product,
            DateTimeOffset.UtcNow.AddDays(-2),
            runId: Guid.NewGuid());

    private static VulnerabilityPatchAssessment CreateAssessment(
        Guid vulnerabilityId,
        string urgencyTier,
        string urgencyReason) =>
        VulnerabilityPatchAssessment.Create(
            vulnerabilityId,
            "Patch immediately.",
            "High",
            "Known exploitation is credible.",
            urgencyTier,
            "Within 24 hours",
            urgencyReason,
            "[]",
            "[]",
            "[]",
            "Default AI",
            null,
            DateTimeOffset.UtcNow
        );

    public void Dispose() => _dbContext.Dispose();
}
