using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class RemediationDecisionServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly RemediationDecisionService _service;

    public RemediationDecisionServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        var workflowService = new RemediationWorkflowService(_dbContext);
        var notificationService = Substitute.For<INotificationService>();
        var patchingTaskService = new PatchingTaskService(
            _dbContext,
            new SlaService(),
            workflowService,
            notificationService
        );
        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            notificationService,
            Substitute.For<IRealTimeNotifier>(),
            workflowService,
            patchingTaskService,
            new ApprovedVulnerabilityRemediationService(_dbContext)
        );

        _service = new RemediationDecisionService(
            _dbContext,
            approvalTaskService,
            workflowService,
            patchingTaskService
        );
    }

    [Fact]
    public async Task CreateDecisionForCaseAsync_RiskAcceptanceRequiresExplicitDeadlineMode()
    {
        var remediationCase = await SeedCaseAsync();

        var result = await _service.CreateDecisionForCaseAsync(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Risk accepted by asset owner.",
            _userId,
            expiryDate: null,
            reEvaluationDate: null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("deadline");
    }

    [Fact]
    public async Task CreateDecisionForCaseAsync_RiskAcceptanceForeverModeAllowsOmittedExpiryDate()
    {
        var remediationCase = await SeedCaseAsync();

        var result = await _service.CreateDecisionForCaseAsync(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Risk accepted permanently.",
            _userId,
            expiryDate: null,
            reEvaluationDate: null,
            CancellationToken.None,
            deadlineMode: RemediationDecisionDeadlineMode.Forever
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ExpiryDate.Should().BeNull();
        result.Value.ReEvaluationDate.Should().BeNull();
    }

    [Fact]
    public async Task CreateDecisionForCaseAsync_RiskAcceptanceDateModeRequiresExpiryDate()
    {
        var remediationCase = await SeedCaseAsync();

        var result = await _service.CreateDecisionForCaseAsync(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Risk accepted until a specific date.",
            _userId,
            expiryDate: null,
            reEvaluationDate: null,
            CancellationToken.None,
            deadlineMode: RemediationDecisionDeadlineMode.Date
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("expiry date");
    }

    [Fact]
    public async Task CreateDecisionForCaseAsync_PatchingDeferredDateModeRequiresReviewDate()
    {
        var remediationCase = await SeedCaseAsync();

        var result = await _service.CreateDecisionForCaseAsync(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.PatchingDeferred,
            "Defer until the next release window.",
            _userId,
            expiryDate: null,
            reEvaluationDate: null,
            CancellationToken.None,
            deadlineMode: RemediationDecisionDeadlineMode.Date
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("review date");
    }

    [Fact]
    public async Task CreateDecisionForCaseAsync_RejectsUndefinedDeadlineMode()
    {
        var remediationCase = await SeedCaseAsync();

        var result = await _service.CreateDecisionForCaseAsync(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Risk accepted with invalid deadline mode.",
            _userId,
            expiryDate: null,
            reEvaluationDate: null,
            CancellationToken.None,
            deadlineMode: (RemediationDecisionDeadlineMode)999
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("deadline mode");
    }

    [Fact]
    public async Task CreateDecisionForCaseAsync_PatchingDeferredRoutesToSecurityApproval()
    {
        var remediationCase = await SeedCaseAsync();

        var result = await _service.CreateDecisionForCaseAsync(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.PatchingDeferred,
            "Defer until the next release window.",
            _userId,
            expiryDate: null,
            reEvaluationDate: DateTimeOffset.UtcNow.AddDays(30),
            CancellationToken.None,
            deadlineMode: RemediationDecisionDeadlineMode.Date
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ApprovalStatus.Should().Be(DecisionApprovalStatus.PendingApproval);

        var workflow = await _dbContext.RemediationWorkflows.SingleAsync();
        workflow.CurrentStage.Should().Be(RemediationWorkflowStage.Approval);
        workflow.ApprovalMode.Should().Be(RemediationWorkflowApprovalMode.SecurityApproval);

        var task = await _dbContext.ApprovalTasks.Include(item => item.VisibleRoles).SingleAsync();
        task.Status.Should().Be(ApprovalTaskStatus.Pending);
        task.VisibleToRoles.Should().BeEquivalentTo([RoleName.GlobalAdmin, RoleName.SecurityManager]);
    }

    [Theory]
    [InlineData(RemediationOutcome.RiskAcceptance)]
    [InlineData(RemediationOutcome.AlternateMitigation)]
    public async Task CreateDecisionForCaseAsync_ExceptionOutcomesRouteToSecurityManager(RemediationOutcome outcome)
    {
        var remediationCase = await SeedCaseAsync();

        var result = await _service.CreateDecisionForCaseAsync(
            _tenantId,
            remediationCase.Id,
            outcome,
            "The asset owner supplied a documented exception.",
            _userId,
            expiryDate: outcome == RemediationOutcome.RiskAcceptance ? DateTimeOffset.UtcNow.AddDays(30) : null,
            reEvaluationDate: null,
            CancellationToken.None,
            deadlineMode: outcome == RemediationOutcome.RiskAcceptance ? RemediationDecisionDeadlineMode.Date : null
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ApprovalStatus.Should().Be(DecisionApprovalStatus.PendingApproval);

        var workflow = await _dbContext.RemediationWorkflows.SingleAsync();
        workflow.CurrentStage.Should().Be(RemediationWorkflowStage.Approval);
        workflow.ApprovalMode.Should().Be(RemediationWorkflowApprovalMode.SecurityApproval);

        var task = await _dbContext.ApprovalTasks.Include(item => item.VisibleRoles).SingleAsync();
        task.Status.Should().Be(ApprovalTaskStatus.Pending);
        task.VisibleToRoles.Should().BeEquivalentTo([RoleName.GlobalAdmin, RoleName.SecurityManager]);
    }

    [Fact]
    public async Task CreateDecisionForCaseAsync_ApprovedForPatchingRoutesToTechnicalManagerAndWaitsForApprovalBeforeExecution()
    {
        var remediationCase = await SeedCaseAsync();

        var result = await _service.CreateDecisionForCaseAsync(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.ApprovedForPatching,
            "Patch in the next maintenance window.",
            _userId,
            expiryDate: null,
            reEvaluationDate: null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ApprovalStatus.Should().Be(DecisionApprovalStatus.PendingApproval);

        var workflow = await _dbContext.RemediationWorkflows.SingleAsync();
        workflow.CurrentStage.Should().Be(RemediationWorkflowStage.Approval);
        workflow.ApprovalMode.Should().Be(RemediationWorkflowApprovalMode.TechnicalApproval);

        var task = await _dbContext.ApprovalTasks.Include(item => item.VisibleRoles).SingleAsync();
        task.Status.Should().Be(ApprovalTaskStatus.Pending);
        task.Type.Should().Be(ApprovalTaskType.PatchingApproved);
        task.VisibleToRoles.Should().BeEquivalentTo([RoleName.GlobalAdmin, RoleName.TechnicalManager]);
        _dbContext.PatchingTasks.Should().BeEmpty();
    }

    private async Task<RemediationCase> SeedCaseAsync()
    {
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        await _dbContext.AddRangeAsync(product, remediationCase);
        await _dbContext.SaveChangesAsync();
        return remediationCase;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
