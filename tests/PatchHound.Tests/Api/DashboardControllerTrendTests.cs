using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class DashboardControllerTrendTests : IDisposable
{
    private readonly Guid _tenantId;
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly DashboardController _controller;

    public DashboardControllerTrendTests()
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
    public async Task GetTrends_DoesNotCountExposureResolvedToday_AsOpenToday()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        var now = DateTimeOffset.UtcNow;
        var openedAt = now.AddDays(-1);

        seed.ExposureA.Resolve(now);
        var episode = ExposureEpisode.Open(_tenantId, seed.ExposureA.Id, 1, openedAt);
        episode.Close(now);
        _dbContext.ExposureEpisodes.Add(episode);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetTrends(new DashboardFilterQuery(), CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<TrendDataDto>().Subject;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);

        dto.Items.Should().NotContain(item =>
            item.Date == today
            && item.Severity == Severity.Critical.ToString(),
            "a vulnerability resolved today should not remain in today's open vulnerability trend point");
    }

    [Fact]
    public async Task GetTrends_CountsUniqueVulnerabilitiesBySeverity()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        var now = DateTimeOffset.UtcNow;
        var openedAt = now.AddDays(-2);

        var duplicateExposure = DeviceVulnerabilityExposure.Observe(
            _tenantId,
            seed.DeviceB.Id,
            seed.ExposureA.VulnerabilityId,
            seed.ProductB.Id,
            seed.InstallB.Id,
            "2.0.0",
            ExposureMatchSource.Product,
            now,
            runId: Guid.NewGuid());

        _dbContext.DeviceVulnerabilityExposures.Add(duplicateExposure);
        await _dbContext.SaveChangesAsync();

        _dbContext.ExposureEpisodes.AddRange(
            ExposureEpisode.Open(_tenantId, seed.ExposureA.Id, 1, openedAt),
            ExposureEpisode.Open(_tenantId, duplicateExposure.Id, 1, openedAt));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetTrends(new DashboardFilterQuery(), CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<TrendDataDto>().Subject;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);

        dto.Items.Should().ContainSingle(item =>
            item.Date == today
            && item.Severity == Severity.Critical.ToString()
            && item.Count == 1,
            "the trend should count each vulnerability once per severity/day even when it appears on multiple devices");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
