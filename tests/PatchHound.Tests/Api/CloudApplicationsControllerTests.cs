using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.CloudApplications;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class CloudApplicationsControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _sourceSystemId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly CloudApplicationsController _controller;

    public CloudApplicationsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        _controller = new CloudApplicationsController(_dbContext, _tenantContext);
    }

    [Fact]
    public async Task GetDetail_ReturnsOwnerRoutingFields()
    {
        var ownerTeam = Team.Create(_tenantId, "Platform Engineering");
        var application = CloudApplication.Create(
            _tenantId,
            _sourceSystemId,
            "app-object-1",
            "client-id-1",
            "Contoso Portal",
            null,
            false,
            []
        );
        application.AssignOwnerTeamFromRule(ownerTeam.Id, Guid.NewGuid());

        await _dbContext.AddRangeAsync(ownerTeam, application);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetDetail(application.Id, CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<CloudApplicationDetailDto>().Subject;
        dto.OwnerTeamId.Should().Be(ownerTeam.Id);
        dto.OwnerTeamName.Should().Be("Platform Engineering");
        dto.OwnerTeamManagedByRule.Should().BeTrue();
        dto.OwnerAssignmentSource.Should().Be("Rule");
    }

    [Fact]
    public async Task List_ReturnsOwnerRoutingFields()
    {
        var ownerTeam = Team.Create(_tenantId, "Platform Engineering");
        var application = CloudApplication.Create(
            _tenantId,
            _sourceSystemId,
            "app-object-1",
            "client-id-1",
            "Contoso Portal",
            null,
            false,
            []
        );
        application.AssignOwnerTeam(ownerTeam.Id);

        await _dbContext.AddRangeAsync(ownerTeam, application);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(new CloudApplicationFilterQuery(), new PaginationQuery(), CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<PagedResponse<CloudApplicationListItemDto>>().Subject;
        var item = payload.Items.Should().ContainSingle().Subject;
        item.OwnerTeamName.Should().Be("Platform Engineering");
        item.OwnerAssignmentSource.Should().Be("Manual");
    }

    [Fact]
    public async Task AssignOwner_WithNullTeamId_ReappliesMatchingRule()
    {
        var previousTeam = Team.Create(_tenantId, "Previous Owners");
        var ruleTeam = Team.Create(_tenantId, "Application Owners");
        var application = CloudApplication.Create(
            _tenantId,
            _sourceSystemId,
            "app-object-1",
            "client-id-1",
            "Contoso Portal",
            null,
            false,
            []
        );
        application.AssignOwnerTeam(previousTeam.Id);

        var rule = DeviceRule.Create(
            _tenantId,
            "Portal ownership",
            null,
            1,
            "Application",
            new PatchHound.Core.Models.FilterCondition("Name", "Equals", "Contoso Portal"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "AssignOwnerTeam",
                    new Dictionary<string, string> { ["teamId"] = ruleTeam.Id.ToString() }
                )
            ]
        );

        await _dbContext.AddRangeAsync(previousTeam, ruleTeam, application, rule);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.AssignOwner(
            application.Id,
            new AssignCloudApplicationOwnerRequest(null),
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var stored = await _dbContext.CloudApplications.SingleAsync(item => item.Id == application.Id);
        stored.OwnerTeamId.Should().Be(ruleTeam.Id);
        stored.OwnerTeamRuleId.Should().Be(rule.Id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
