using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Assets;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Repositories;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class AssetsControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly AssetsController _controller;

    public AssetsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());
        _tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        var assetService = new AssetService(
            new AssetRepository(_dbContext),
            Substitute.For<IUnitOfWork>()
        );
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
            new RiskScoreService(_dbContext, Substitute.For<Microsoft.Extensions.Logging.ILogger<RiskScoreService>>())
        );
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            _dbContext,
            new NormalizedSoftwareResolver(_dbContext)
        );
        var aliasResolver = new PatchHound.Api.Services.TenantSoftwareAliasResolver(_dbContext);
        var approvalTaskService = new ApprovalTaskService(_dbContext, Substitute.For<INotificationService>(), Substitute.For<IRealTimeNotifier>());
        var remediationDecisionService = new RemediationDecisionService(_dbContext, new SlaService(), approvalTaskService);
        var remediationTaskQueryService = new PatchHound.Api.Services.RemediationTaskQueryService(
            _dbContext,
            remediationDecisionService
        );
        var detailQueryService = new PatchHound.Api.Services.AssetDetailQueryService(
            _dbContext,
            snapshotResolver,
            aliasResolver,
            remediationTaskQueryService
        );
        var assetRuleEvaluationService = new AssetRuleEvaluationService(
            _dbContext,
            new AssetRuleFilterBuilder(_dbContext),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<AssetRuleEvaluationService>>()
        );
        _controller = new AssetsController(
            _dbContext,
            assetService,
            assessmentService,
            normalizedSoftwareProjectionService,
            _tenantContext,
            snapshotResolver,
            detailQueryService,
            riskRefreshService,
            assetRuleEvaluationService
        );
    }

    [Fact]
    public async Task Get_ReturnsSoftwareCpeBindings_ForSoftwareAssetsAndInstalledSoftware()
    {
        var device = Asset.Create(
            _tenantId,
            "device-1",
            AssetType.Device,
            "Device 1",
            Criticality.High
        );
        var software = Asset.Create(
            _tenantId,
            "software-1",
            AssetType.Software,
            "Contoso Agent",
            Criticality.Low
        );
        var standaloneSoftware = Asset.Create(
            _tenantId,
            "software-2",
            AssetType.Software,
            "Legacy Runtime",
            Criticality.Low
        );

        var normalizedAgent = NormalizedSoftware.Create(
            "agent",
            "contoso",
            "cpe:contoso:agent",
            "cpe:2.3:a:contoso:agent:*:*:*:*:*:*:*:*",
            SoftwareNormalizationMethod.ExplicitCpe,
            SoftwareNormalizationConfidence.High,
            new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero)
        );
        var normalizedRuntime = NormalizedSoftware.Create(
            "runtime",
            "legacy",
            "cpe:legacy:runtime",
            "cpe:2.3:a:legacy:runtime:*:*:*:*:*:*:*:*",
            SoftwareNormalizationMethod.ExplicitCpe,
            SoftwareNormalizationConfidence.High,
            new DateTimeOffset(2026, 3, 8, 9, 30, 0, TimeSpan.Zero)
        );
        var tenantAgent = TenantSoftware.Create(
            _tenantId,
            null,
            normalizedAgent.Id,
            new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero)
        );
        var tenantRuntime = TenantSoftware.Create(
            _tenantId,
            null,
            normalizedRuntime.Id,
            new DateTimeOffset(2026, 3, 8, 9, 30, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero)
        );

        await _dbContext.AddRangeAsync(device, software, standaloneSoftware);
        await _dbContext.AddRangeAsync(normalizedAgent, normalizedRuntime, tenantAgent, tenantRuntime);
        await _dbContext.NormalizedSoftwareAliases.AddRangeAsync(
            NormalizedSoftwareAlias.Create(
                normalizedAgent.Id,
                SoftwareIdentitySourceSystem.Defender,
                software.ExternalId,
                "Contoso Agent",
                "Contoso",
                "1.0",
                SoftwareNormalizationConfidence.High,
                "Resolved via software CPE binding.",
                new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero)
            ),
            NormalizedSoftwareAlias.Create(
                normalizedRuntime.Id,
                SoftwareIdentitySourceSystem.Defender,
                standaloneSoftware.ExternalId,
                "Legacy Runtime",
                "Legacy",
                "5.2",
                SoftwareNormalizationConfidence.High,
                "Resolved via software CPE binding.",
                new DateTimeOffset(2026, 3, 8, 9, 30, 0, 0, TimeSpan.Zero)
            )
        );
        await _dbContext.DeviceSoftwareInstallations.AddAsync(
            DeviceSoftwareInstallation.Create(
                _tenantId,
                device.Id,
                software.Id,
                new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero)
            )
        );
        await _dbContext.SoftwareCpeBindings.AddRangeAsync(
            SoftwareCpeBinding.Create(
                normalizedAgent.Id,
                "cpe:2.3:a:contoso:agent:1.0:*:*:*:*:*:*:*",
                CpeBindingMethod.DefenderDerived,
                MatchConfidence.High,
                "contoso",
                "agent",
                "1.0",
                new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero)
            ),
            SoftwareCpeBinding.Create(
                normalizedRuntime.Id,
                "cpe:2.3:a:legacy:runtime:5.2:*:*:*:*:*:*:*",
                CpeBindingMethod.Manual,
                MatchConfidence.Medium,
                "legacy",
                "runtime",
                "5.2",
                new DateTimeOffset(2026, 3, 8, 9, 30, 0, TimeSpan.Zero)
            )
        );
        await _dbContext.SaveChangesAsync();

        var deviceAction = await _controller.Get(device.Id, CancellationToken.None);
        var deviceResult = deviceAction.Result.Should().BeOfType<OkObjectResult>().Subject;
        var devicePayload = deviceResult.Value.Should().BeOfType<AssetDetailDto>().Subject;

        devicePayload.SoftwareInventory.Should().ContainSingle();
        devicePayload.SoftwareInventory[0].CpeBinding.Should().NotBeNull();
        devicePayload.SoftwareInventory[0].CpeBinding!.BindingMethod.Should().Be("DefenderDerived");
        devicePayload.SoftwareInventory[0].CpeBinding!.Confidence.Should().Be("High");

        var softwareAction = await _controller.Get(standaloneSoftware.Id, CancellationToken.None);
        var softwareResult = softwareAction.Result.Should().BeOfType<OkObjectResult>().Subject;
        var softwarePayload = softwareResult.Value.Should().BeOfType<AssetDetailDto>().Subject;

        softwarePayload.SoftwareCpeBinding.Should().NotBeNull();
        softwarePayload.SoftwareCpeBinding!.Cpe23Uri.Should().Contain("legacy:runtime");
        softwarePayload.SoftwareCpeBinding.BindingMethod.Should().Be("Manual");
    }

    [Fact]
    public async Task Get_ReturnsEpisodeBackedRiskBreakdown()
    {
        var asset = Asset.Create(
            _tenantId,
            "device-risk-1",
            AssetType.Device,
            "Gateway",
            Criticality.High
        );
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-9010",
            "Gateway issue",
            "Desc",
            Severity.Critical,
            "NVD"
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );

        await _dbContext.AddRangeAsync(
            asset,
            definition,
            tenantVulnerability,
            AssetRiskScore.Create(
                _tenantId,
                asset.Id,
                812m,
                790m,
                1,
                1,
                0,
                0,
                2,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                tenantVulnerability.Id,
                asset.Id,
                null,
                88m,
                73m,
                44m,
                790m,
                "High",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(asset.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<AssetDetailDto>().Subject;

        payload.Risk.Should().NotBeNull();
        payload.Risk!.OverallScore.Should().Be(812m);
        payload.Risk.MaxEpisodeRiskScore.Should().Be(790m);
        payload.Risk.RiskBand.Should().Be("High");
        payload.Risk.OpenEpisodeCount.Should().Be(2);
        payload.Risk.TopDrivers.Should().ContainSingle();
        payload.Risk.TopDrivers[0].ExternalId.Should().Be("CVE-2026-9010");
        payload.Risk.TopDrivers[0].EpisodeRiskScore.Should().Be(790m);
        payload.CriticalityDetail.Should().NotBeNull();
        payload.CriticalityDetail!.Source.Should().Be("Default");
    }

    [Fact]
    public async Task List_FiltersByDeviceGroup_AndReturnsDeviceGroupName()
    {
        var matchingAsset = Asset.Create(
            _tenantId,
            "device-1",
            AssetType.Device,
            "Workstation 1",
            Criticality.High
        );
        matchingAsset.UpdateDeviceDetails(
            "ws-001.contoso.local",
            "Active",
            "Windows",
            "11",
            "Medium",
            new DateTimeOffset(2026, 3, 13, 8, 0, 0, TimeSpan.Zero),
            "10.0.0.10",
            "aad-1",
            "rbac-group-1",
            "Tier 0 Servers"
        );

        var otherAsset = Asset.Create(
            _tenantId,
            "device-2",
            AssetType.Device,
            "Workstation 2",
            Criticality.Medium
        );
        otherAsset.UpdateDeviceDetails(
            "ws-002.contoso.local",
            "Active",
            "Windows",
            "11",
            "Low",
            new DateTimeOffset(2026, 3, 13, 8, 5, 0, TimeSpan.Zero),
            "10.0.0.11",
            "aad-2",
            "rbac-group-2",
            "Field Devices"
        );

        await _dbContext.AddRangeAsync(matchingAsset, otherAsset);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new AssetFilterQuery(DeviceGroup: "Tier 0"),
            new PaginationQuery(),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<AssetDto>>().Subject;

        payload.Items.Should().ContainSingle();
        payload.Items[0].Id.Should().Be(matchingAsset.Id);
        payload.Items[0].DeviceGroupName.Should().Be("Tier 0 Servers");
    }

    [Fact]
    public async Task AssignOwner_RecalculatesEpisodeAndAssetRisk()
    {
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-8300",
            "Ownership-sensitive risk",
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
        var asset = Asset.Create(_tenantId, "asset-owner", AssetType.Device, "Asset Owner", Criticality.High);
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerability.Id,
            asset.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-1)
        );

        await _dbContext.AddRangeAsync(definition, tenantVulnerability, asset, episode);
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

        var action = await _controller.AssignOwner(
            asset.Id,
            new AssignOwnerRequest(nameof(OwnerType.User), Guid.NewGuid()),
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var refreshedAssessment = await _dbContext.VulnerabilityEpisodeRiskAssessments
            .SingleAsync(item => item.VulnerabilityAssetEpisodeId == episode.Id);
        refreshedAssessment.OperationalScore.Should().Be(50m);
        refreshedAssessment.EpisodeRiskScore.Should().BeLessThan(742.5m);

        var assetRisk = await _dbContext.AssetRiskScores.SingleAsync(item => item.AssetId == asset.Id);
        assetRisk.OverallScore.Should().BeLessThan(742.5m);
        assetRisk.OpenEpisodeCount.Should().Be(1);
    }

    [Fact]
    public async Task ResetCriticalityOverride_RemovesManualOverrideAndReappliesRule()
    {
        var asset = Asset.Create(_tenantId, "asset-reset", AssetType.Device, "Tier0-App-01", Criticality.Low);
        asset.SetCriticality(Criticality.High);
        var rule = AssetRule.Create(
            _tenantId,
            "Tier 0 critical",
            null,
            1,
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Tier0-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "SetCriticality",
                    new Dictionary<string, string> { ["criticality"] = "Critical" }
                )
            ]
        );

        await _dbContext.AddRangeAsync(asset, rule);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.ResetCriticalityOverride(asset.Id, CancellationToken.None);

        action.Should().BeOfType<NoContentResult>();

        var refreshed = await _dbContext.Assets.SingleAsync(item => item.Id == asset.Id);
        refreshed.Criticality.Should().Be(Criticality.Critical);
        refreshed.CriticalitySource.Should().Be("Rule");
        refreshed.CriticalityRuleId.Should().Be(rule.Id);
    }

    [Fact]
    public async Task List_ReturnsHealthStatusRiskScoreExposureLevelAndTags()
    {
        var asset = Asset.Create(_tenantId, "dev-tag-1", AssetType.Device, "Tagged Host", Criticality.High);
        asset.UpdateDeviceDetails(
            "tagged.contoso.local", "Active", "Windows", "11",
            "High", new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero),
            "10.0.0.50", "aad-tag-1", null, null, "Medium", true
        );
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        _dbContext.AssetTags.Add(AssetTag.Create(_tenantId, asset.Id, "production", "Defender"));
        _dbContext.AssetTags.Add(AssetTag.Create(_tenantId, asset.Id, "tier-0", "Defender"));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(new AssetFilterQuery(), new PaginationQuery(), CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<AssetDto>>().Subject;

        payload.Items.Should().ContainSingle();
        var item = payload.Items[0];
        item.HealthStatus.Should().Be("Active");
        item.RiskScore.Should().Be("High");
        item.ExposureLevel.Should().Be("Medium");
        item.Tags.Should().BeEquivalentTo("production", "tier-0");
    }

    [Fact]
    public async Task List_FiltersByExposureLevel()
    {
        var highExposure = Asset.Create(_tenantId, "dev-high", AssetType.Device, "High Exp", Criticality.Medium);
        highExposure.UpdateDeviceDetails("h.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.1", null, null, null, "High", null);

        var lowExposure = Asset.Create(_tenantId, "dev-low", AssetType.Device, "Low Exp", Criticality.Medium);
        lowExposure.UpdateDeviceDetails("l.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.2", null, null, null, "Low", null);

        await _dbContext.Assets.AddRangeAsync(highExposure, lowExposure);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(new AssetFilterQuery(ExposureLevel: "High"), new PaginationQuery(), CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<AssetDto>>().Subject;

        payload.Items.Should().ContainSingle();
        payload.Items[0].Id.Should().Be(highExposure.Id);
    }

    [Fact]
    public async Task List_FiltersByTag()
    {
        var tagged = Asset.Create(_tenantId, "dev-tagged", AssetType.Device, "Tagged", Criticality.Medium);
        tagged.UpdateDeviceDetails("t.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.3", null);

        var untagged = Asset.Create(_tenantId, "dev-untagged", AssetType.Device, "Untagged", Criticality.Medium);
        untagged.UpdateDeviceDetails("u.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.4", null);

        await _dbContext.Assets.AddRangeAsync(tagged, untagged);
        await _dbContext.SaveChangesAsync();

        _dbContext.AssetTags.Add(AssetTag.Create(_tenantId, tagged.Id, "production", "Defender"));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(new AssetFilterQuery(Tag: "prod"), new PaginationQuery(), CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<AssetDto>>().Subject;

        payload.Items.Should().ContainSingle();
        payload.Items[0].Id.Should().Be(tagged.Id);
    }

    [Fact]
    public async Task List_OrdersByCurrentRiskScore_AndReturnsCurrentRiskScore()
    {
        var highRisk = Asset.Create(_tenantId, "dev-high-risk", AssetType.Device, "High Risk", Criticality.High);
        var lowRisk = Asset.Create(_tenantId, "dev-low-risk", AssetType.Device, "Low Risk", Criticality.High);

        await _dbContext.Assets.AddRangeAsync(highRisk, lowRisk);
        await _dbContext.AssetRiskScores.AddRangeAsync(
            AssetRiskScore.Create(
                _tenantId,
                highRisk.Id,
                910m,
                900m,
                1,
                1,
                0,
                0,
                2,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            AssetRiskScore.Create(
                _tenantId,
                lowRisk.Id,
                320m,
                300m,
                0,
                0,
                1,
                1,
                2,
                "[]",
                RiskScoreService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(new AssetFilterQuery(), new PaginationQuery(), CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<AssetDto>>().Subject;

        payload.Items.Should().HaveCount(2);
        payload.Items[0].Id.Should().Be(highRisk.Id);
        payload.Items[0].CurrentRiskScore.Should().Be(910m);
        payload.Items[1].Id.Should().Be(lowRisk.Id);
        payload.Items[1].CurrentRiskScore.Should().Be(320m);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
