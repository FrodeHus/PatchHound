using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.SecurityProfiles;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class SecurityProfilesControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly SecurityProfilesController _controller;

    public SecurityProfilesControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(_tenantContext));
        var riskRefreshService = new RiskRefreshService(
            _dbContext,
            new RiskScoreService(_dbContext, Substitute.For<ILogger<RiskScoreService>>())
        );

        _controller = new SecurityProfilesController(
            _dbContext,
            riskRefreshService,
            _tenantContext
        );
    }

    [Fact(Skip = "Legacy Asset-centric assertions. Phase-5 will re-introduce per-asset risk recalculation via DeviceVulnerabilityExposure.")]
    public async Task Update_RecalculatesRiskForAssignedAssets()
    {
        var profile = AssetSecurityProfile.Create(
            _tenantId,
            "Internet profile",
            null,
            EnvironmentClass.Server,
            InternetReachability.Internet,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High
        );
        var asset = Asset.Create(_tenantId, "asset-profile", AssetType.Device, "Asset Profile", Criticality.High);
        asset.AssignSecurityProfile(profile.Id);
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-8500",
            "Profile-sensitive risk",
            "Desc",
            Severity.High,
            "NVD",
            8.0m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H"
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerability.Id,
            asset.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-2)
        );
        var vulnerabilityAsset = VulnerabilityAsset.Create(
            tenantVulnerability.Id,
            asset.Id,
            DateTimeOffset.UtcNow.AddDays(-2)
        );

        await _dbContext.AddRangeAsync(profile, asset, definition, tenantVulnerability, episode, vulnerabilityAsset);
        await _dbContext.VulnerabilityAssetAssessments.AddAsync(
            VulnerabilityAssetAssessment.Create(
                _tenantId,
                null,
                tenantVulnerability.Id,
                asset.Id,
                profile.Id,
                Severity.High,
                8.0m,
                definition.CvssVector,
                Severity.High,
                8.0m,
                definition.CvssVector,
                "[]",
                "seeded",
                EnvironmentalSeverityCalculator.CalculationVersion
            )
        );
        await _dbContext.VulnerabilityEpisodeRiskAssessments.AddAsync(
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                episode.Id,
                tenantVulnerability.Id,
                asset.Id,
                null,
                80m,
                80m,
                65m,
                782.5m,
                "High",
                "[]",
                "1" // phase-2: was VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Update(
            profile.Id,
            new UpdateSecurityProfileRequest(
                "Local profile",
                null,
                nameof(EnvironmentClass.Server),
                nameof(InternetReachability.LocalOnly),
                nameof(SecurityRequirementLevel.High),
                nameof(SecurityRequirementLevel.High),
                nameof(SecurityRequirementLevel.High),
                nameof(CvssModifiedAttackVector.Local),
                nameof(CvssModifiedAttackComplexity.NotDefined),
                nameof(CvssModifiedPrivilegesRequired.NotDefined),
                nameof(CvssModifiedUserInteraction.NotDefined),
                nameof(CvssModifiedScope.NotDefined),
                nameof(CvssModifiedImpact.NotDefined),
                nameof(CvssModifiedImpact.NotDefined),
                nameof(CvssModifiedImpact.NotDefined)
            ),
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var refreshedAssessment = await _dbContext.VulnerabilityEpisodeRiskAssessments
            .SingleAsync(item => item.VulnerabilityAssetEpisodeId == episode.Id);
        refreshedAssessment.EpisodeRiskScore.Should().NotBe(782.5m);
        refreshedAssessment.ContextScore.Should().NotBe(80m);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
