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
        // Phase 2: CalculateAssetScoresAsync is stubbed to return [] (no canonical
        // DeviceVulnerabilityExposure data yet). DeviceRiskScores, DeviceGroupRiskScores,
        // and TeamRiskScores are all empty. The snapshot is still written with zero counts.
        // Phase 3 will restore device-score assertions once DeviceVulnerabilityExposure lands.
        var deviceA = Device.Create(_tenantId, _sourceSystemId, "device-a", "Device A", Criticality.High);
        var deviceB = Device.Create(_tenantId, _sourceSystemId, "device-b", "Device B", Criticality.Medium);
        var team = Team.Create(_tenantId, "SecOps");
        deviceA.AssignTeamOwner(team.Id);

        await _dbContext.AddRangeAsync(deviceA, deviceB, team);
        await _dbContext.SaveChangesAsync();

        await _service.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var deviceScores = await _dbContext.DeviceRiskScores.ToListAsync();
        deviceScores.Should().BeEmpty();

        var deviceGroupScores = await _dbContext.DeviceGroupRiskScores.ToListAsync();
        deviceGroupScores.Should().BeEmpty();

        var teamScores = await _dbContext.TeamRiskScores.ToListAsync();
        teamScores.Should().BeEmpty();

        var snapshot = await _dbContext.TenantRiskScoreSnapshots.SingleAsync();
        snapshot.TenantId.Should().Be(_tenantId);
        snapshot.AssetCount.Should().Be(0);
        snapshot.OverallScore.Should().Be(0m);
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
        // Phase 2: CalculateSoftwareScoresAsync is stubbed to return [] (no canonical
        // DeviceVulnerabilityExposure data yet). TenantSoftwareRiskScores will be empty.
        // Phase 3 will restore the full assertion once DeviceVulnerabilityExposure lands.
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

        await _service.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var softwareScores = await _dbContext.TenantSoftwareRiskScores.ToListAsync();
        softwareScores.Should().BeEmpty();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
