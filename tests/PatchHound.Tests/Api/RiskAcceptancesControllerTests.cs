using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.RiskAcceptances;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Repositories;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class RiskAcceptancesControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly RiskAcceptancesController _controller;

    public RiskAcceptancesControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(_tenantContext));
        var snapshotResolver = new TenantSnapshotResolver(_dbContext);
        var riskRefreshService = new RiskRefreshService(
            _dbContext,
            snapshotResolver,
            new VulnerabilityAssessmentService(
                _dbContext,
                new EnvironmentalSeverityCalculator(),
                snapshotResolver
            ),
            new VulnerabilityEpisodeRiskAssessmentService(_dbContext),
            new RiskScoreService(_dbContext, Substitute.For<ILogger<RiskScoreService>>())
        );

        _controller = new RiskAcceptancesController(
            _dbContext,
            new RiskAcceptanceService(new RiskAcceptanceRepository(_dbContext), _dbContext),
            riskRefreshService,
            _tenantContext
        );
    }

    [Fact]
    public async Task ApproveOrReject_Approve_RecalculatesEpisodeRisk()
    {
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-8400",
            "Accepted risk",
            "Desc",
            Severity.High,
            "NVD",
            8.0m
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );
        var asset = Asset.Create(_tenantId, "asset-accept", AssetType.Device, "Asset Accept", Criticality.High);
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerability.Id,
            asset.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-1)
        );
        var acceptance = RiskAcceptance.Create(
            tenantVulnerability.Id,
            _tenantId,
            Guid.NewGuid(),
            "Temporary acceptance",
            asset.Id
        );

        await _dbContext.AddRangeAsync(definition, tenantVulnerability, asset, episode, acceptance);
        await _dbContext.VulnerabilityEpisodeRiskAssessments.AddAsync(
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                episode.Id,
                tenantVulnerability.Id,
                asset.Id,
                null,
                80m,
                70m,
                65m,
                742.5m,
                "Medium",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.ApproveOrReject(
            acceptance.Id,
            new ApproveRejectRequest("Approve"),
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var refreshedAssessment = await _dbContext.VulnerabilityEpisodeRiskAssessments
            .SingleAsync(item => item.VulnerabilityAssetEpisodeId == episode.Id);
        refreshedAssessment.OperationalScore.Should().Be(45m);
        refreshedAssessment.EpisodeRiskScore.Should().BeLessThan(742.5m);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
