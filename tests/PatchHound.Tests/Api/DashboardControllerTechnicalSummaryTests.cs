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
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class DashboardControllerTechnicalSummaryTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly DashboardController _controller;

    public DashboardControllerTechnicalSummaryTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.GetRolesForTenant(_tenantId).Returns([]);

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
    public async Task GetTechnicalManagerSummary_UsesCurrentSoftwareOwnerRouting()
    {
        var defaultTeam = Team.Create(_tenantId, "Default");
        var staleTaskOwnerTeam = Team.Create(_tenantId, "Old Delivery Team");
        var currentOwnerTeam = Team.Create(_tenantId, "Platform Engineering");
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        var tenantSoftware = SoftwareTenantRecord.Create(
            _tenantId,
            null,
            product.Id,
            DateTimeOffset.UtcNow.AddDays(-7),
            DateTimeOffset.UtcNow.AddDays(-1)
        );
        tenantSoftware.AssignOwnerTeamFromRule(currentOwnerTeam.Id, Guid.NewGuid());

        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var workflow = RemediationWorkflow.Create(_tenantId, remediationCase.Id, currentOwnerTeam.Id);
        var decision = RemediationDecision.Create(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.ApprovedForPatching,
            justification: null,
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.Approved
        );
        var task = PatchingTask.Create(
            _tenantId,
            remediationCase.Id,
            decision.Id,
            staleTaskOwnerTeam.Id,
            DateTimeOffset.UtcNow.AddDays(10)
        );
        task.AttachToWorkflow(workflow.Id);
        decision.AttachToWorkflow(workflow.Id);

        await _dbContext.AddRangeAsync(
            defaultTeam,
            staleTaskOwnerTeam,
            currentOwnerTeam,
            product,
            tenantSoftware,
            remediationCase,
            workflow,
            decision,
            task
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetTechnicalManagerSummary(CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<TechnicalManagerDashboardSummaryDto>().Subject;
        var approvedTask = dto.ApprovedPatchingTasks.Should().ContainSingle().Subject;
        approvedTask.OwnerTeamName.Should().Be("Platform Engineering");
        approvedTask.OwnerAssignmentSource.Should().Be("Rule");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
