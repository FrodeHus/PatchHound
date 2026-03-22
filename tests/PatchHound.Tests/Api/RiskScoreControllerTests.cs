using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.RiskScore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class RiskScoreControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly RiskScoreController _controller;

    public RiskScoreControllerTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );

        _controller = new RiskScoreController(
            _dbContext,
            new RiskScoreService(_dbContext, Substitute.For<ILogger<RiskScoreService>>()),
            tenantContext
        );
    }

    [Fact]
    public async Task GetSummary_ReturnsTopAssetsAndHistory()
    {
        var assetA = Asset.Create(_tenantId, "asset-a", AssetType.Device, "Gateway", Criticality.High);
        var assetB = Asset.Create(_tenantId, "asset-b", AssetType.Device, "Laptop", Criticality.Low);
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-8700",
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
            assetA,
            assetB,
            definition,
            tenantVulnerability,
            AssetRiskScore.Create(
                _tenantId,
                assetA.Id,
                940m,
                930m,
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
                assetB.Id,
                410m,
                390m,
                0,
                0,
                1,
                1,
                2,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                tenantVulnerability.Id,
                assetA.Id,
                null,
                92m,
                85m,
                60m,
                930m,
                "Critical",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            ),
            TenantRiskScoreSnapshot.Create(_tenantId, new DateOnly(2026, 3, 20), 880m, 2, 1, 0),
            TenantRiskScoreSnapshot.Create(_tenantId, new DateOnly(2026, 3, 21), 910m, 2, 1, 1)
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(null, null, null, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<RiskScoreSummaryDto>().Subject;

        payload.OverallScore.Should().BeGreaterThan(700m);
        payload.AssetCount.Should().Be(2);
        payload.CriticalAssetCount.Should().Be(1);
        payload.TopRiskAssets.Should().HaveCount(2);
        payload.TopRiskAssets[0].AssetName.Should().Be("Gateway");
        payload.TopRiskAssets[0].OverallScore.Should().Be(940m);
        payload.TopRiskAssets[0].EpisodeDrivers.Should().ContainSingle();
        payload.TopRiskAssets[0].EpisodeDrivers[0].ExternalId.Should().Be("CVE-2026-8700");
        payload.TopRiskAssets[0].EpisodeDrivers[0].EpisodeRiskScore.Should().Be(930m);
        payload.History.Should().HaveCount(2);
        payload.History[^1].OverallScore.Should().Be(910m);
    }

    [Fact]
    public async Task GetDeviceGroupDetail_ReturnsTopAssetsForGroup()
    {
        var asset = Asset.Create(_tenantId, "asset-group", AssetType.Device, "Gateway", Criticality.High);
        asset.UpdateDeviceDetails(
            "gateway.contoso.local",
            "Active",
            "Windows",
            "11",
            "High",
            DateTimeOffset.UtcNow,
            "10.0.0.1",
            "aad-1",
            "group-1",
            "Tier 0 Servers"
        );
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-8800",
            "Tier 0 issue",
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
            DeviceGroupRiskScore.Create(
                _tenantId,
                "id:group-1",
                "group-1",
                "Tier 0 Servers",
                810m,
                790m,
                1,
                1,
                0,
                0,
                1,
                2,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            AssetRiskScore.Create(
                _tenantId,
                asset.Id,
                790m,
                780m,
                1,
                0,
                0,
                0,
                1,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                tenantVulnerability.Id,
                asset.Id,
                null,
                91m,
                82m,
                58m,
                780m,
                "High",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetDeviceGroupDetail("Tier 0 Servers", CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DeviceGroupRiskDetailDto>().Subject;

        payload.DeviceGroupName.Should().Be("Tier 0 Servers");
        payload.OverallScore.Should().Be(810m);
        payload.TopRiskAssets.Should().ContainSingle();
        payload.TopRiskAssets[0].AssetId.Should().Be(asset.Id);
        payload.TopRiskAssets[0].EpisodeDrivers.Should().ContainSingle();
        payload.TopRiskAssets[0].EpisodeDrivers[0].ExternalId.Should().Be("CVE-2026-8800");
    }

    [Fact]
    public async Task GetSummary_WithFilters_RecomputesLiveRiskFromMatchingEpisodes()
    {
        var oldPublishedDate = DateTime.UtcNow.Date.AddDays(-45);
        var newPublishedDate = DateTime.UtcNow.Date.AddDays(-5);

        var matchingAsset = Asset.Create(_tenantId, "asset-filter-match", AssetType.Device, "Gateway", Criticality.High);
        matchingAsset.UpdateDeviceDetails(
            "gateway.contoso.local",
            "Active",
            "Windows",
            "11",
            "High",
            DateTimeOffset.UtcNow,
            "10.0.0.10",
            "aad-match",
            "group-match",
            "Tier 0 Servers"
        );

        var filteredOutAsset = Asset.Create(_tenantId, "asset-filter-out", AssetType.Device, "Laptop", Criticality.Medium);
        filteredOutAsset.UpdateDeviceDetails(
            "laptop.contoso.local",
            "Active",
            "macOS",
            "14",
            "Medium",
            DateTimeOffset.UtcNow,
            "10.0.0.20",
            "aad-out",
            "group-out",
            "User Devices"
        );

        var oldDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-9001",
            "Old issue",
            "Desc",
            Severity.Critical,
            "NVD",
            publishedDate: new DateTimeOffset(oldPublishedDate, TimeSpan.Zero)
        );
        var newDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-9002",
            "New issue",
            "Desc",
            Severity.Critical,
            "NVD",
            publishedDate: new DateTimeOffset(newPublishedDate, TimeSpan.Zero)
        );

        var matchingTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            oldDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );
        var filteredOutTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            newDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );

        await _dbContext.AddRangeAsync(
            matchingAsset,
            filteredOutAsset,
            oldDefinition,
            newDefinition,
            matchingTenantVulnerability,
            filteredOutTenantVulnerability,
            AssetRiskScore.Create(
                _tenantId,
                matchingAsset.Id,
                930m,
                910m,
                1,
                0,
                0,
                0,
                1,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            AssetRiskScore.Create(
                _tenantId,
                filteredOutAsset.Id,
                910m,
                900m,
                1,
                0,
                0,
                0,
                1,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                matchingTenantVulnerability.Id,
                matchingAsset.Id,
                null,
                91m,
                85m,
                50m,
                910m,
                "Critical",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                filteredOutTenantVulnerability.Id,
                filteredOutAsset.Id,
                null,
                90m,
                84m,
                48m,
                900m,
                "Critical",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            ),
            TenantRiskScoreSnapshot.Create(_tenantId, DateOnly.FromDateTime(DateTime.UtcNow.Date), 920m, 2, 2, 0)
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(30, "Windows", "Tier 0 Servers", CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<RiskScoreSummaryDto>().Subject;

        payload.AssetCount.Should().Be(1);
        payload.CriticalAssetCount.Should().Be(0);
        payload.OverallScore.Should().BeGreaterThan(700m);
        payload.TopRiskAssets.Should().ContainSingle();
        payload.TopRiskAssets[0].AssetId.Should().Be(matchingAsset.Id);
        payload.TopRiskAssets[0].EpisodeDrivers.Should().ContainSingle();
        payload.TopRiskAssets[0].EpisodeDrivers[0].ExternalId.Should().Be("CVE-2026-9001");
        payload.History.Should().BeEmpty();
        payload.CalculatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSoftwareDetail_ReturnsTopAssetsForSoftware()
    {
        var normalizedSoftware = NormalizedSoftware.Create(
            "agent",
            "contoso",
            "contoso:agent",
            "cpe:2.3:a:contoso:agent:*:*:*:*:*:*:*:*",
            SoftwareNormalizationMethod.ExplicitCpe,
            SoftwareNormalizationConfidence.High,
            DateTimeOffset.UtcNow
        );
        var tenantSoftware = TenantSoftware.Create(
            _tenantId,
            null,
            normalizedSoftware.Id,
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow
        );
        var asset = Asset.Create(_tenantId, "device-sw", AssetType.Device, "Device", Criticality.High);
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-8900",
            "Contoso issue",
            "Desc",
            Severity.High,
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
            normalizedSoftware,
            tenantSoftware,
            asset,
            definition,
            tenantVulnerability,
            TenantSoftwareRiskScore.Create(
                _tenantId,
                tenantSoftware.Id,
                null,
                760m,
                740m,
                0,
                1,
                1,
                0,
                1,
                2,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            AssetRiskScore.Create(
                _tenantId,
                asset.Id,
                740m,
                720m,
                0,
                1,
                0,
                0,
                1,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            NormalizedSoftwareInstallation.Create(
                _tenantId,
                null,
                tenantSoftware.Id,
                Guid.NewGuid(),
                asset.Id,
                SoftwareIdentitySourceSystem.Defender,
                "1.0",
                DateTimeOffset.UtcNow.AddDays(-5),
                DateTimeOffset.UtcNow,
                null,
                true,
                1
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                tenantVulnerability.Id,
                asset.Id,
                null,
                88m,
                72m,
                40m,
                720m,
                "High",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSoftwareDetail(tenantSoftware.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<SoftwareRiskDetailDto>().Subject;

        payload.TenantSoftwareId.Should().Be(tenantSoftware.Id);
        payload.SoftwareName.Should().Be("agent");
        payload.OverallScore.Should().Be(760m);
        payload.TopRiskAssets.Should().ContainSingle();
        payload.TopRiskAssets[0].AssetId.Should().Be(asset.Id);
        payload.TopRiskAssets[0].EpisodeDrivers.Should().ContainSingle();
        payload.TopRiskAssets[0].EpisodeDrivers[0].ExternalId.Should().Be("CVE-2026-8900");
    }

    [Fact]
    public async Task Recalculate_RebuildsTenantRiskScores()
    {
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-8950",
            "Rebuild issue",
            "Desc",
            Severity.High,
            "NVD"
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );
        var asset = Asset.Create(_tenantId, "asset-recalc", AssetType.Device, "Gateway", Criticality.High);
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerability.Id,
            asset.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-1)
        );

        await _dbContext.AddRangeAsync(
            definition,
            tenantVulnerability,
            asset,
            episode,
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                episode.Id,
                tenantVulnerability.Id,
                asset.Id,
                null,
                85m,
                70m,
                40m,
                700m,
                "Medium",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Recalculate(CancellationToken.None);

        action.Should().BeOfType<NoContentResult>();
        (await _dbContext.AssetRiskScores.AnyAsync(item => item.AssetId == asset.Id)).Should().BeTrue();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
