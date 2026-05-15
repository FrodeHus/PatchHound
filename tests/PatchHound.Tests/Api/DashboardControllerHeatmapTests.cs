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

public class DashboardControllerHeatmapTests : IDisposable
{
    private readonly Guid _tenantId;
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly DashboardController _controller;

    public DashboardControllerHeatmapTests()
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
    public async Task GetHeatmap_DeviceGroup_ReturnsSeverityCounts()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        seed.DeviceA.UpdateInventoryDetails(null, null, "Windows", null, null, null, null, null, groupName: "Production");
        seed.DeviceB.UpdateInventoryDetails(null, null, "Linux", null, null, null, null, null, groupName: "Workstations");
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetHeatmap(new DashboardFilterQuery(), "deviceGroup", CancellationToken.None);

        var dto = ReadOk(action);
        dto.Should().ContainEquivalentOf(new HeatmapRowDto("Production", 1, 0, 0, 0));
        dto.Should().ContainEquivalentOf(new HeatmapRowDto("Workstations", 0, 1, 0, 0));
    }

    [Fact]
    public async Task GetHeatmap_DeviceGroup_CountsUniqueVulnerabilitiesWithinGroup()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        seed.DeviceA.UpdateInventoryDetails(null, null, "Windows", null, null, null, null, null, groupName: "Production");
        seed.DeviceB.UpdateInventoryDetails(null, null, "Linux", null, null, null, null, null, groupName: "Production");
        _dbContext.DeviceVulnerabilityExposures.Add(DeviceVulnerabilityExposure.Observe(
            _tenantId,
            seed.DeviceB.Id,
            seed.ExposureA.VulnerabilityId,
            seed.ProductB.Id,
            seed.InstallB.Id,
            "2.0.0",
            ExposureMatchSource.Product,
            DateTimeOffset.UtcNow,
            runId: Guid.NewGuid()));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetHeatmap(new DashboardFilterQuery(), "deviceGroup", CancellationToken.None);

        var dto = ReadOk(action);
        dto.Should().ContainEquivalentOf(new HeatmapRowDto("Production", 1, 1, 0, 0));
    }

    [Fact]
    public async Task GetHeatmap_OwnerTeam_UsesTeamOwnerAndUnownedBucket()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Infrastructure");
        seed.DeviceA.AssignTeamOwner(team.Id);
        _dbContext.Teams.Add(team);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetHeatmap(new DashboardFilterQuery(), "ownerTeam", CancellationToken.None);

        var dto = ReadOk(action);
        dto.Should().ContainEquivalentOf(new HeatmapRowDto("Infrastructure", 1, 0, 0, 0));
        dto.Should().ContainEquivalentOf(new HeatmapRowDto("Unowned", 0, 1, 0, 0));
    }

    [Fact]
    public async Task GetHeatmap_BusinessLabel_IncludesUnlabeledBucket()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        var label = BusinessLabel.Create(_tenantId, "Customer Portal", null, null);
        _dbContext.BusinessLabels.Add(label);
        _dbContext.DeviceBusinessLabels.Add(DeviceBusinessLabel.Create(_tenantId, seed.DeviceA.Id, label.Id));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetHeatmap(new DashboardFilterQuery(), "businessService", CancellationToken.None);

        var dto = ReadOk(action);
        dto.Should().ContainEquivalentOf(new HeatmapRowDto("Customer Portal", 1, 0, 0, 0));
        dto.Should().ContainEquivalentOf(new HeatmapRowDto("Unlabeled", 0, 1, 0, 0));
    }

    [Fact]
    public async Task GetHeatmap_Platform_AppliesDashboardFilters()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        seed.DeviceA.UpdateInventoryDetails(null, null, "Windows", null, null, null, null, null, groupName: "Production");
        seed.DeviceB.UpdateInventoryDetails(null, null, "Linux", null, null, null, null, null, groupName: "Workstations");
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetHeatmap(
            new DashboardFilterQuery(Platform: "Windows"),
            "platform",
            CancellationToken.None);

        var dto = ReadOk(action);
        dto.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new HeatmapRowDto("Windows", 1, 0, 0, 0));
    }

    [Fact]
    public async Task GetHeatmap_UnsupportedGroup_ReturnsBadRequest()
    {
        await CanonicalSeed.PlantAsync(_dbContext, _tenantId);

        var action = await _controller.GetHeatmap(new DashboardFilterQuery(), "businessUnit", CancellationToken.None);

        action.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static List<HeatmapRowDto> ReadOk(ActionResult<List<HeatmapRowDto>> action)
    {
        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeAssignableTo<List<HeatmapRowDto>>().Subject;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
