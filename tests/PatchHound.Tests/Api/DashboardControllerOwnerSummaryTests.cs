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
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class DashboardControllerOwnerSummaryTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly DashboardController _controller;

    public DashboardControllerOwnerSummaryTests()
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

        _controller = new DashboardController(
            _dbContext,
            new DashboardQueryService(_dbContext, Substitute.For<IRiskChangeBriefAiSummaryService>()),
            _tenantContext,
            new TenantSnapshotResolver(_dbContext)
        );
    }

    [Fact]
    public async Task GetOwnerSummary_ReturnsCloudApplicationOwnerRoutingSource()
    {
        var ownerTeam = Team.Create(_tenantId, "Identity Platform");
        var currentUser = User.Create("owner@example.com", "Owner", Guid.NewGuid().ToString());
        _tenantContext.CurrentUserId.Returns(currentUser.Id);
        var teamMember = TeamMember.Create(ownerTeam.Id, currentUser.Id);
        var source = SourceSystem.Create("Entra Applications", "Entra");
        var application = CloudApplication.Create(
            _tenantId,
            source.Id,
            externalId: "object-123",
            appId: "app-123",
            name: "Contoso SSO",
            description: null,
            isFallbackPublicClient: false,
            redirectUris: []
        );
        application.AssignOwnerTeamFromRule(ownerTeam.Id, Guid.NewGuid());
        var credential = CloudApplicationCredentialMetadata.Create(
            application.Id,
            _tenantId,
            "cred-123",
            "Password",
            "Primary secret",
            DateTimeOffset.UtcNow.AddDays(3));

        await _dbContext.AddRangeAsync(currentUser, ownerTeam, teamMember, source, application, credential);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetOwnerSummary(CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<OwnerDashboardSummaryDto>().Subject;
        var cloudAppAction = dto.CloudAppActions.Should().ContainSingle().Subject;
        cloudAppAction.AppName.Should().Be("Contoso SSO");
        cloudAppAction.OwnerTeamName.Should().Be("Identity Platform");
        cloudAppAction.OwnerAssignmentSource.Should().Be("Rule");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
