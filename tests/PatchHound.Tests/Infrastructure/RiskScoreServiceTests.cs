using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class RiskScoreServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly RiskScoreService _service;

    public RiskScoreServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        tenantContext.CurrentTenantId.Returns(_tenantId);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
        _service = new RiskScoreService(_dbContext, Substitute.For<ILogger<RiskScoreService>>());
    }

    [Fact]
    public async Task RecalculateForTenantAsync_CreatesAssetScoresAndDailySnapshot()
    {
        var assetA = Asset.Create(_tenantId, "asset-a", AssetType.Device, "Asset A", Criticality.High);
        var assetB = Asset.Create(_tenantId, "asset-b", AssetType.Device, "Asset B", Criticality.Medium);
        var team = Team.Create(_tenantId, "SecOps");
        assetA.AssignTeamOwner(team.Id);
        assetA.UpdateDeviceDetails(
            "asset-a.contoso.local",
            "Active",
            "Windows",
            "11",
            "High",
            DateTimeOffset.UtcNow,
            "10.0.0.1",
            "aad-a",
            "group-1",
            "Workstations"
        );
        assetB.UpdateDeviceDetails(
            "asset-b.contoso.local",
            "Active",
            "Windows",
            "11",
            "Medium",
            DateTimeOffset.UtcNow,
            "10.0.0.2",
            "aad-b",
            "group-1",
            "Workstations"
        );

        var definitionA = VulnerabilityDefinition.Create(
            "CVE-2026-9000",
            "Critical chain",
            "Desc",
            Severity.Critical,
            "NVD"
        );
        var definitionB = VulnerabilityDefinition.Create(
            "CVE-2026-9001",
            "Moderate issue",
            "Desc",
            Severity.Medium,
            "NVD"
        );
        var tenantVulnerabilityA = TenantVulnerability.Create(
            _tenantId,
            definitionA.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );
        var tenantVulnerabilityB = TenantVulnerability.Create(
            _tenantId,
            definitionB.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );
        var episodeA = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerabilityA.Id,
            assetA.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-2)
        );
        var episodeB = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerabilityB.Id,
            assetB.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-1)
        );

        await _dbContext.AddRangeAsync(
            assetA,
            assetB,
            team,
            definitionA,
            definitionB,
            tenantVulnerabilityA,
            tenantVulnerabilityB,
            episodeA,
            episodeB,
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                episodeA.Id,
                tenantVulnerabilityA.Id,
                assetA.Id,
                null,
                92m,
                88m,
                60m,
                930m,
                "Critical",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                episodeB.Id,
                tenantVulnerabilityB.Id,
                assetB.Id,
                null,
                50m,
                45m,
                30m,
                520m,
                "Medium",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        await _service.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var assetScores = await _dbContext.AssetRiskScores
            .OrderByDescending(item => item.OverallScore)
            .ToListAsync();
        assetScores.Should().HaveCount(2);
        assetScores[0].AssetId.Should().Be(assetA.Id);
        assetScores[0].OverallScore.Should().BeGreaterThan(assetScores[1].OverallScore);
        assetScores[0].CriticalCount.Should().Be(1);
        assetScores[1].MediumCount.Should().Be(1);

        var deviceGroupScore = await _dbContext.DeviceGroupRiskScores.SingleAsync();
        deviceGroupScore.DeviceGroupName.Should().Be("Workstations");
        deviceGroupScore.AssetCount.Should().Be(2);
        deviceGroupScore.OpenEpisodeCount.Should().Be(2);
        deviceGroupScore.CriticalEpisodeCount.Should().Be(1);

        var teamScore = await _dbContext.TeamRiskScores.SingleAsync();
        teamScore.TeamId.Should().Be(team.Id);
        teamScore.AssetCount.Should().Be(1);
        teamScore.OpenEpisodeCount.Should().Be(1);
        teamScore.CriticalEpisodeCount.Should().Be(1);

        var snapshot = await _dbContext.TenantRiskScoreSnapshots.SingleAsync();
        snapshot.TenantId.Should().Be(_tenantId);
        snapshot.AssetCount.Should().Be(2);
        snapshot.CriticalAssetCount.Should().Be(0);
        snapshot.HighAssetCount.Should().Be(1);
        snapshot.OverallScore.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void CalculateTenantRisk_GivesWeightToHighRiskAssets()
    {
        var result = RiskScoreService.CalculateTenantRisk(
            [
                new RiskScoreService.AssetRiskResult(Guid.NewGuid(), 965m, 950m, 2, 0, 0, 0, 2, "[]"),
                new RiskScoreService.AssetRiskResult(Guid.NewGuid(), 810m, 800m, 0, 2, 0, 0, 2, "[]"),
                new RiskScoreService.AssetRiskResult(Guid.NewGuid(), 120m, 120m, 0, 0, 0, 3, 3, "[]"),
                new RiskScoreService.AssetRiskResult(Guid.NewGuid(), 85m, 85m, 0, 0, 0, 1, 1, "[]")
            ]
        );

        result.AssetCount.Should().Be(4);
        result.CriticalAssetCount.Should().Be(1);
        result.HighAssetCount.Should().Be(1);
        result.OverallScore.Should().BeGreaterThan(700m);
    }

    [Fact]
    public async Task RecalculateForTenantAsync_CreatesSoftwareRiskScoresFromSoftwareLinkedEpisodes()
    {
        var snapshotId = Guid.NewGuid();
        await _dbContext.TenantSourceConfigurations.AddAsync(
            TenantSourceConfiguration.Create(
                _tenantId,
                TenantSourceCatalog.DefenderSourceKey,
                "Microsoft Defender",
                true,
                string.Empty
            )
        );
        await _dbContext.SaveChangesAsync();
        var sourceConfig = await _dbContext.TenantSourceConfigurations.SingleAsync();
        sourceConfig.SetSnapshotPointers(snapshotId, null);

        var device = Asset.Create(_tenantId, "device-1", AssetType.Device, "Device 1", Criticality.High);
        var softwareAsset = Asset.Create(_tenantId, "software-1", AssetType.Software, "Contoso Agent", Criticality.Low);
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
            snapshotId,
            normalizedSoftware.Id,
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow
        );
        var vulnerabilityDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-9100",
            "Contoso issue",
            "Desc",
            Severity.Critical,
            "NVD"
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            vulnerabilityDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerability.Id,
            device.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-1)
        );

        await _dbContext.AddRangeAsync(
            device,
            softwareAsset,
            normalizedSoftware,
            tenantSoftware,
            vulnerabilityDefinition,
            tenantVulnerability,
            episode,
            NormalizedSoftwareInstallation.Create(
                _tenantId,
                snapshotId,
                tenantSoftware.Id,
                softwareAsset.Id,
                device.Id,
                SoftwareIdentitySourceSystem.Defender,
                "1.0",
                DateTimeOffset.UtcNow.AddDays(-5),
                DateTimeOffset.UtcNow,
                null,
                true,
                1
            ),
            SoftwareVulnerabilityMatch.Create(
                _tenantId,
                snapshotId,
                softwareAsset.Id,
                vulnerabilityDefinition.Id,
                SoftwareVulnerabilityMatchMethod.CpeBinding,
                MatchConfidence.High,
                "contoso-agent",
                DateTimeOffset.UtcNow
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                episode.Id,
                tenantVulnerability.Id,
                device.Id,
                snapshotId,
                88m,
                72m,
                40m,
                845m,
                "High",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        await _service.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var softwareScore = await _dbContext.TenantSoftwareRiskScores.SingleAsync();
        softwareScore.TenantSoftwareId.Should().Be(tenantSoftware.Id);
        softwareScore.SnapshotId.Should().Be(snapshotId);
        softwareScore.OverallScore.Should().BeGreaterThan(700m);
        softwareScore.MaxEpisodeRiskScore.Should().Be(845m);
        softwareScore.HighEpisodeCount.Should().Be(1);
        softwareScore.AffectedDeviceCount.Should().Be(1);
        softwareScore.OpenEpisodeCount.Should().Be(1);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
