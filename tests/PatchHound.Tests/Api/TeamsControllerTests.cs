using FluentAssertions;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Admin;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class TeamsControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly TeamsController _controller;

    public TeamsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        var teamService = new TeamService(
            Substitute.For<ITeamRepository>(),
            Substitute.For<IUserRepository>(),
            _dbContext
        );

        _controller = new TeamsController(
            _dbContext,
            teamService,
            new TeamMembershipRuleService(_dbContext, new TeamMembershipRuleFilterBuilder()),
            _tenantContext
        );
    }

    [Fact]
    public async Task List_OrdersByCurrentRiskScore_AndReturnsRisk()
    {
        var tenant = Tenant.Create("Contoso", "entra-contoso");
        var teamHigh = Team.Create(_tenantId, "High");
        var teamLow = Team.Create(_tenantId, "Low");

        await _dbContext.AddRangeAsync(
            tenant,
            teamHigh,
            teamLow,
            TeamRiskScore.Create(_tenantId, teamHigh.Id, 820m, 810m, 1, 0, 0, 0, 2, 2, "[]", "1"),
            TeamRiskScore.Create(_tenantId, teamLow.Id, 200m, 180m, 0, 0, 1, 0, 1, 1, "[]", "1")
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            null,
            new PaginationQuery(Page: 1, PageSize: 25),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<TeamDto>>().Subject;

        payload.Items.Should().HaveCount(2);
        payload.Items[0].Id.Should().Be(teamHigh.Id);
        payload.Items[0].CurrentRiskScore.Should().Be(820m);
        payload.Items[1].Id.Should().Be(teamLow.Id);
    }

    [Fact]
    public async Task Get_ReturnsCurrentRiskScore()
    {
        var tenant = Tenant.Create("Contoso", "entra-contoso");
        var team = Team.Create(_tenantId, "Operations");
        var asset = Asset.Create(_tenantId, "device-1", AssetType.Device, "Device 1", Criticality.High);
        asset.AssignTeamOwner(team.Id);
        asset.UpdateDeviceDetails(
            "device-1.contoso.local",
            "Active",
            "Windows",
            "11",
            "High",
            DateTimeOffset.UtcNow,
            "10.0.0.1",
            "aad-1"
        );

        await _dbContext.AddRangeAsync(
            tenant,
            team,
            asset,
            AssetRiskScore.Create(_tenantId, asset.Id, 710m, 680m, 0, 1, 0, 0, 1, "[]", "1"),
            TeamRiskScore.Create(_tenantId, team.Id, 640m, 620m, 0, 1, 0, 0, 1, 1, "[]", "1")
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(team.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TeamDetailDto>().Subject;

        payload.Id.Should().Be(team.Id);
        payload.AssignedAssetCount.Should().Be(1);
        payload.CurrentRiskScore.Should().Be(640m);
        payload.TopRiskAssets.Should().ContainSingle();
        payload.TopRiskAssets[0].AssetId.Should().Be(asset.Id);
        payload.TopRiskAssets[0].CurrentRiskScore.Should().Be(710m);
    }

    [Fact]
    public async Task UpsertRule_SavesMembershipRule_AndReturnsItOnGet()
    {
        var tenant = Tenant.Create("Contoso", "entra-contoso");
        var team = Team.Create(_tenantId, "Operations");

        await _dbContext.AddRangeAsync(tenant, team);
        await _dbContext.SaveChangesAsync();

        var filter = JsonDocument.Parse("""
            {
              "type": "group",
              "operator": "AND",
              "conditions": [
                { "type": "condition", "field": "Email", "operator": "Contains", "value": "@contoso.com" }
              ]
            }
            """).RootElement.Clone();

        var updateAction = await _controller.UpsertRule(
            team.Id,
            new UpdateTeamMembershipRuleRequest(true, false, filter),
            CancellationToken.None
        );

        updateAction.Result.Should().BeOfType<NoContentResult>();

        var getAction = await _controller.Get(team.Id, CancellationToken.None);
        var getResult = getAction.Result.Should().BeOfType<OkObjectResult>().Subject;
        var getPayload = getResult.Value.Should().BeOfType<TeamDetailDto>().Subject;

        getPayload.MembershipRule.Should().NotBeNull();
        getPayload.IsDynamic.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertRule_WhenEnablingDynamicWithMembersWithoutAcknowledgement_ReturnsBadRequest()
    {
        var tenant = Tenant.Create("Contoso", "entra-contoso");
        var team = Team.Create(_tenantId, "Operations");
        var user = User.Create("owner@contoso.com", "Owner", Guid.NewGuid().ToString(), "Contoso");
        team.AddMember(user);

        await _dbContext.AddRangeAsync(tenant, team, user);
        await _dbContext.SaveChangesAsync();

        var filter = JsonDocument.Parse("""
            {
              "type": "group",
              "operator": "AND",
              "conditions": [
                { "type": "condition", "field": "Email", "operator": "Contains", "value": "@contoso.com" }
              ]
            }
            """).RootElement.Clone();

        var action = await _controller.UpsertRule(
            team.Id,
            new UpdateTeamMembershipRuleRequest(true, false, filter),
            CancellationToken.None
        );

        action.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
