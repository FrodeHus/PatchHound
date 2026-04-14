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

        var queryService = new RemediationTaskQueryService(_dbContext);
        _controller = new RemediationTasksController(queryService, _tenantContext);
    }

    // Seeds a SoftwareProduct + RemediationCase + Team, returns (remediationCase, team).
    private async Task<(RemediationCase remediationCase, Team team)> SeedCaseAsync()
    {
        var product = SoftwareProduct.Create("acme", "agent", null);
        await _dbContext.SoftwareProducts.AddAsync(product);

        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        await _dbContext.RemediationCases.AddAsync(remediationCase);

        var team = Team.Create(_tenantId, "Platform");
        await _dbContext.Teams.AddAsync(team);

        await _dbContext.SaveChangesAsync();

        return (remediationCase, team);
    }

    // Creates decision + approves it, returning the resulting PatchingTask.
    private async Task<PatchingTask> CreateAndApproveDecisionAsync(Guid remediationCaseId, Guid userId)
    {
        var workflowService = new RemediationWorkflowService(_dbContext);
        var notificationService = Substitute.For<INotificationService>();
        var patchingTaskService = new PatchingTaskService(_dbContext, new SlaService(), workflowService, notificationService);
        var approvalTaskService = new ApprovalTaskService(_dbContext, notificationService, Substitute.For<IRealTimeNotifier>(), workflowService, patchingTaskService);
        var decisionService = new RemediationDecisionService(_dbContext, approvalTaskService, workflowService, patchingTaskService);

        var createResult = await decisionService.CreateDecisionForCaseAsync(
            _tenantId,
            remediationCaseId,
            RemediationOutcome.ApprovedForPatching,
            "Create software task",
            userId,
            null,
            null,
            CancellationToken.None
        );
        createResult.IsSuccess.Should().BeTrue();

        var approvalTask = await _dbContext.ApprovalTasks
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();

        await approvalTaskService.ApproveAsync(
            approvalTask.Id,
            userId,
            "Approved for execution",
            new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None
        );

        return await _dbContext.PatchingTasks
            .OrderByDescending(t => t.CreatedAt)
            .FirstAsync();
    }

    [Fact]
    public async Task List_ReturnsOpenTasksForCaseFilter()
    {
        var (remediationCase, _) = await SeedCaseAsync();
        var userId = _tenantContext.CurrentUserId;

        await CreateAndApproveDecisionAsync(remediationCase.Id, userId);

        var action = await _controller.List(
            new RemediationTaskFilterQuery(TenantSoftwareId: remediationCase.Id),
            new PaginationQuery(1, 25),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<RemediationTaskListItemDto>>().Subject;

        payload.TotalCount.Should().Be(1);
        payload.Items.Should().HaveCount(1);
        payload.Items.Should().OnlyContain(item => item.SoftwareName == "agent");
    }

    [Fact]
    public async Task CreateForSoftware_ReturnsStubZeroCounts()
    {
        // Phase 4: CreateMissingTasksForSoftwareAsync is stubbed to (0, 0).
        // Phase 5 will re-implement device-level task creation.
        var (remediationCase, _) = await SeedCaseAsync();
        var userId = _tenantContext.CurrentUserId;

        await CreateAndApproveDecisionAsync(remediationCase.Id, userId);

        var action = await _controller.CreateForSoftware(remediationCase.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<RemediationTaskCreateResultDto>().Subject;

        payload.CreatedCount.Should().Be(0);
        payload.EligibleCount.Should().Be(0);
        _dbContext.PatchingTasks.Should().HaveCount(1);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
