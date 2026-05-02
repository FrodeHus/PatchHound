using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class DashboardControllerExecutiveSummaryTests : IDisposable
{
    private readonly Guid _tenantId;
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly DashboardController _controller;

    public DashboardControllerExecutiveSummaryTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId = Guid.NewGuid());
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        _controller = new DashboardController(
            _dbContext,
            new DashboardQueryService(_dbContext, Substitute.For<IRiskChangeBriefAiSummaryService>()),
            _tenantContext,
            new TenantSnapshotResolver(_dbContext)
        );
    }

    [Fact]
    public async Task GetSummary_PopulatesExecutiveExposureScoreAndTopDriver_WhenRiskRollupsExist()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        SeedRiskScores(seed);
        _dbContext.TenantRiskScoreSnapshots.Add(TenantRiskScoreSnapshot.Create(
            _tenantId,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            700m,
            2,
            0,
            1));
        await _dbContext.SaveChangesAsync();

        var dto = await GetSummaryAsync();

        dto.ExposureScore.Should().BeGreaterThan(0m);
        dto.ExecutiveExposure.Should().NotBeNull();
        dto.ExecutiveExposure!.Score.Should().Be(dto.ExposureScore);
        dto.ExecutiveExposure.RiskLevel.Should().Be("High");
        dto.ExecutiveExposure.ScoreDelta.Should().BeGreaterThan(0m);
        dto.ExecutiveExposure.Trend.Should().Be("Worsening");
        dto.ExecutiveExposure.Scope.Should().Be("Tenant");
        dto.ExecutiveExposure.TopDriver.Should().Be("Device group: Tier 0 Servers");
        dto.ExecutiveExposure.TopDriverDetail.Should().Contain("open episodes");
    }

    [Fact]
    public async Task GetSummary_ReturnsNoBaselineTrend_WhenTenantHasNoPriorSnapshot()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        SeedRiskScores(seed);
        await _dbContext.SaveChangesAsync();

        var dto = await GetSummaryAsync();

        dto.ExecutiveExposure.Should().NotBeNull();
        dto.ExecutiveExposure!.Score.Should().BeGreaterThan(0m);
        dto.ExecutiveExposure.ScoreDelta.Should().BeNull();
        dto.ExecutiveExposure.Trend.Should().Be("NoBaseline");
    }

    [Fact]
    public async Task GetSummary_ReturnsImprovingTrend_WhenCurrentScoreIsBelowPriorSnapshot()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        SeedRiskScores(seed);
        _dbContext.TenantRiskScoreSnapshots.Add(TenantRiskScoreSnapshot.Create(
            _tenantId,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            900m,
            2,
            1,
            1));
        await _dbContext.SaveChangesAsync();

        var dto = await GetSummaryAsync();

        dto.ExecutiveExposure.Should().NotBeNull();
        dto.ExecutiveExposure!.ScoreDelta.Should().BeLessThan(0m);
        dto.ExecutiveExposure.Trend.Should().Be("Improving");
    }

    private void SeedRiskScores(CanonicalSeed seed)
    {
        _dbContext.DeviceRiskScores.AddRange(
            DeviceRiskScore.Create(_tenantId, seed.DeviceA.Id, 900m, 900m, 1, 0, 0, 0, 1, "[]", "1"),
            DeviceRiskScore.Create(_tenantId, seed.DeviceB.Id, 820m, 820m, 0, 1, 0, 0, 1, "[]", "1")
        );
        _dbContext.DeviceGroupRiskScores.Add(DeviceGroupRiskScore.Create(
            _tenantId,
            "tier-0",
            "tier-0",
            "Tier 0 Servers",
            930m,
            900m,
            1,
            1,
            0,
            0,
            2,
            2,
            "[]",
            "1"));
        _dbContext.SoftwareRiskScores.Add(SoftwareRiskScore.Create(
            _tenantId,
            seed.ProductA.Id,
            760m,
            900m,
            1,
            0,
            0,
            0,
            1,
            1,
            "[]",
            "1"));
    }

    private async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<DashboardSummaryDto>().Subject;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
