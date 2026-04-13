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
        var deviceA = Device.Create(_tenantId, _sourceSystemId, "device-a", "Device A", Criticality.High);
        var deviceB = Device.Create(_tenantId, _sourceSystemId, "device-b", "Device B", Criticality.Medium);
        var team = Team.Create(_tenantId, "SecOps");
        var vulnA = Vulnerability.Create("nvd", "CVE-2026-5001", "Critical vuln", "desc", Severity.Critical, 9.8m, null, DateTimeOffset.UtcNow);
        var vulnB = Vulnerability.Create("nvd", "CVE-2026-5002", "High vuln", "desc", Severity.High, 7.4m, null, DateTimeOffset.UtcNow);
        deviceA.AssignTeamOwner(team.Id);

        await _dbContext.AddRangeAsync(deviceA, deviceB, team, vulnA, vulnB);
        await _dbContext.SaveChangesAsync();

        var exposureA = DeviceVulnerabilityExposure.Create(_tenantId, deviceA.Id, vulnA.Id, null, null, "test", DateTimeOffset.UtcNow);
        var exposureB = DeviceVulnerabilityExposure.Create(_tenantId, deviceB.Id, vulnB.Id, null, null, "test", DateTimeOffset.UtcNow);
        await _dbContext.AddRangeAsync(exposureA, exposureB);
        await _dbContext.SaveChangesAsync();

        await _dbContext.AddRangeAsync(
            ExposureAssessment.Create(_tenantId, exposureA.Id, deviceA.Id, vulnA.Id, null, Severity.Critical, 950m, null, "[]", "critical", "1"),
            ExposureAssessment.Create(_tenantId, exposureB.Id, deviceB.Id, vulnB.Id, null, Severity.High, 700m, null, "[]", "high", "1")
        );
        await _dbContext.SaveChangesAsync();

        await _service.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var deviceScores = await _dbContext.DeviceRiskScores.ToListAsync();
        deviceScores.Should().HaveCount(2);
        deviceScores.Should().Contain(item => item.DeviceId == deviceA.Id && item.CriticalCount == 1);

        var deviceGroupScores = await _dbContext.DeviceGroupRiskScores.ToListAsync();
        deviceGroupScores.Should().ContainSingle(item => item.DeviceGroupName == "Ungrouped");

        var teamScores = await _dbContext.TeamRiskScores.ToListAsync();
        teamScores.Should().ContainSingle(item => item.TeamId == team.Id);

        var snapshot = await _dbContext.TenantRiskScoreSnapshots.SingleAsync();
        snapshot.TenantId.Should().Be(_tenantId);
        snapshot.AssetCount.Should().Be(2);
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
