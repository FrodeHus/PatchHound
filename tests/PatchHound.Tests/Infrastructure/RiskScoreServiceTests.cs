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
    private readonly Guid _sourceSystemId = Guid.NewGuid();
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
    public async Task RecalculateForTenantAsync_CreatesDeviceScoresAndDailySnapshot()
    {
        // Phase 1 canonical baseline: risk scoring sources rows from Devices and
        // writes into DeviceRiskScores. The legacy `VulnerabilityEpisodeRiskAssessment.AssetId`
        // field is treated as a device-identity handle (Phase 5 renames the column). Until
        // Phase 5 drops the Asset-dependent query filter on episodes, we seed a matching
        // legacy Asset row whose Id is forced to the Device.Id so the filter passes.
        var assetA = Asset.Create(_tenantId, "device-a", AssetType.Device, "Device A", Criticality.High);
        var assetB = Asset.Create(_tenantId, "device-b", AssetType.Device, "Device B", Criticality.Medium);
        var deviceA = Device.Create(_tenantId, _sourceSystemId, "device-a", "Device A", Criticality.High);
        var deviceB = Device.Create(_tenantId, _sourceSystemId, "device-b", "Device B", Criticality.Medium);
        ForceId(deviceA, assetA.Id);
        ForceId(deviceB, assetB.Id);
        var team = Team.Create(_tenantId, "SecOps");
        deviceA.AssignTeamOwner(team.Id);
        deviceA.UpdateInventoryDetails(
            computerDnsName: "device-a.contoso.local",
            healthStatus: "Active",
            osPlatform: "Windows",
            osVersion: "11",
            externalRiskLabel: "High",
            lastSeenAt: DateTimeOffset.UtcNow,
            lastIpAddress: "10.0.0.1",
            aadDeviceId: "aad-a",
            groupId: "group-1",
            groupName: "Workstations"
        );
        deviceB.UpdateInventoryDetails(
            computerDnsName: "device-b.contoso.local",
            healthStatus: "Active",
            osPlatform: "Windows",
            osVersion: "11",
            externalRiskLabel: "Medium",
            lastSeenAt: DateTimeOffset.UtcNow,
            lastIpAddress: "10.0.0.2",
            aadDeviceId: "aad-b",
            groupId: "group-1",
            groupName: "Workstations"
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
            deviceA.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-2)
        );
        var episodeB = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerabilityB.Id,
            deviceB.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-1)
        );

        await _dbContext.AddRangeAsync(
            assetA,
            assetB,
            deviceA,
            deviceB,
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
                deviceA.Id,
                null,
                92m,
                88m,
                60m,
                930m,
                "Critical",
                "[]",
                "1" // phase-2: was VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                episodeB.Id,
                tenantVulnerabilityB.Id,
                deviceB.Id,
                null,
                50m,
                45m,
                30m,
                520m,
                "Medium",
                "[]",
                "1" // phase-2: was VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        await _service.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var deviceScores = await _dbContext.DeviceRiskScores
            .OrderByDescending(item => item.OverallScore)
            .ToListAsync();
        deviceScores.Should().HaveCount(2);
        deviceScores[0].DeviceId.Should().Be(deviceA.Id);
        deviceScores[0].OverallScore.Should().BeGreaterThan(deviceScores[1].OverallScore);
        deviceScores[0].CriticalCount.Should().Be(1);
        deviceScores[1].MediumCount.Should().Be(1);

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
                "1" // phase-2: was VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
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

    // Phase 1 bridge: legacy VulnerabilityEpisodeRiskAssessment.AssetId still has a
    // query filter that traverses the Asset navigation. We keep paired Asset+Device
    // rows with synchronized Ids so the filter passes while the service reads from
    // the canonical Device table. Phase 5 removes this when the filter is dropped.
    private static void ForceId(Device device, Guid id)
    {
        typeof(Device)
            .GetProperty(nameof(Device.Id), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
            .SetValue(device, id);
    }
}
