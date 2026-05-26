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
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class AiRecommendationDraftServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
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

        var service = new AiRecommendationDraftService(
            _dbContext,
            new TenantAiTextGenerationService([provider], aiResolver)
        );

        var result = await service.GenerateAsync(_tenantId, remediationCase.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.UserPrompt.Should().Contain("Only the top 25 of 27 assessments are included");
        capturedRequest.UserPrompt.Should().Contain("CVE-2026-HIGH-00");
        capturedRequest.UserPrompt.Should().NotContain("CVE-2026-LOW-OUT");
        capturedRequest.UserPrompt.Should().NotContain("Low priority reason should not be sent.");
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
