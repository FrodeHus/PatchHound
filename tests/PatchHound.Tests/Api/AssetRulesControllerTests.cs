using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class AssetRulesControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly FakeAssetRuleEvaluationService _evaluationService;
    private readonly AssetRulesController _controller;

    public AssetRulesControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(_tenantContext));
        var snapshotResolver = new TenantSnapshotResolver(_dbContext);
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator(),
            snapshotResolver
        );
        var riskRefreshService = new RiskRefreshService(
            _dbContext,
            snapshotResolver,
            assessmentService,
            new VulnerabilityEpisodeRiskAssessmentService(_dbContext),
            new RiskScoreService(_dbContext, Substitute.For<ILogger<RiskScoreService>>())
        );
        _evaluationService = new FakeAssetRuleEvaluationService(_dbContext);
        _controller = new AssetRulesController(
            _dbContext,
            _tenantContext,
            _evaluationService,
            riskRefreshService
        );
    }

    [Fact]
    public async Task Run_RecalculatesRiskAfterAssignmentOperations()
    {
        var team = Team.Create(_tenantId, "Fallback team");
        var asset = Asset.Create(_tenantId, "asset-rule", AssetType.Device, "Rule Match", Criticality.High);
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-8600",
            "Rule-sensitive risk",
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
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerability.Id,
            asset.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-1)
        );
        _evaluationService.Configure(asset.Id, team.Id);

        await _dbContext.AddRangeAsync(team, asset, definition, tenantVulnerability, episode);
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

        var action = await _controller.Run(CancellationToken.None);

        action.Should().BeOfType<NoContentResult>();

        var refreshedAsset = await _dbContext.Assets.SingleAsync(item => item.Id == asset.Id);
        refreshedAsset.FallbackTeamId.Should().Be(team.Id);

        var refreshedAssessment = await _dbContext.VulnerabilityEpisodeRiskAssessments
            .SingleAsync(item => item.VulnerabilityAssetEpisodeId == episode.Id);
        refreshedAssessment.OperationalScore.Should().Be(50m);
        refreshedAssessment.EpisodeRiskScore.Should().BeLessThan(742.5m);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private sealed class FakeAssetRuleEvaluationService(PatchHoundDbContext dbContext)
        : IAssetRuleEvaluationService
    {
        private Guid _assetId;
        private Guid _teamId;

        public void Configure(Guid assetId, Guid teamId)
        {
            _assetId = assetId;
            _teamId = teamId;
        }

        public async Task EvaluateRulesAsync(Guid tenantId, CancellationToken ct)
        {
            var asset = await dbContext.Assets.FirstAsync(
                item => item.Id == _assetId && item.TenantId == tenantId,
                ct
            );
            await dbContext.Teams.AnyAsync(item => item.Id == _teamId && item.TenantId == tenantId, ct);
            dbContext.Entry(asset).Property(nameof(Asset.FallbackTeamId)).CurrentValue = _teamId;
            await dbContext.SaveChangesAsync(ct);
        }

        public Task<AssetRulePreviewResult> PreviewFilterAsync(
            Guid tenantId,
            FilterNode filter,
            CancellationToken ct
        )
        {
            return Task.FromResult(new AssetRulePreviewResult(0, []));
        }
    }
}
