using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Remediation;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class RemediationTasksControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly RemediationTasksController _controller;

    public RemediationTasksControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        var decisionService = new RemediationDecisionService(_dbContext, new SlaService());
        var queryService = new RemediationTaskQueryService(_dbContext, decisionService);
        _controller = new RemediationTasksController(queryService, _tenantContext);
    }

    [Fact]
    public async Task List_ReturnsOpenTasksForTenantSoftwareFilter()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Platform");
        await _dbContext.Teams.AddAsync(team);

        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();

        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }

        await _dbContext.SaveChangesAsync();

        var softwareAssetIds = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .Distinct()
            .ToListAsync();

        var decisionService = new RemediationDecisionService(_dbContext, new SlaService());
        foreach (var softwareAssetId in softwareAssetIds)
        {
            var createResult = await decisionService.CreateDecisionAsync(
                _tenantId,
                softwareAssetId,
                RemediationOutcome.ApprovedForPatching,
                "Create software task",
                _tenantContext.CurrentUserId,
                null,
                null,
                CancellationToken.None
            );
            createResult.IsSuccess.Should().BeTrue();
        }

        var action = await _controller.List(
            new RemediationTaskFilterQuery(TenantSoftwareId: graph.TenantSoftware.Id),
            new PaginationQuery(1, 25),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<RemediationTaskListItemDto>>().Subject;

        payload.TotalCount.Should().Be(2);
        payload.Items.Should().HaveCount(2);
        payload.Items.Should().OnlyContain(item => item.SoftwareName == "agent");
        payload.Items.Should().OnlyContain(item => item.OwnerTeamName == "Platform");
    }

    [Fact]
    public async Task CreateForSoftware_CreatesMissingTasksForCurrentExposure()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Platform");
        await _dbContext.Teams.AddAsync(team);
        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();
        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }
        await _dbContext.SaveChangesAsync();

        var action = await _controller.CreateForSoftware(graph.TenantSoftware.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<RemediationTaskCreateResultDto>().Subject;

        payload.CreatedCount.Should().Be(2);
        payload.EligibleCount.Should().Be(2);
        _dbContext.PatchingTasks.Should().HaveCount(2);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
