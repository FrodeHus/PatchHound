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
    private readonly RemediationWorkflowService _workflowService;
    private readonly RemediationDecisionService _sut;

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
        _workflowService = new RemediationWorkflowService(_dbContext);
        var notificationService = Substitute.For<INotificationService>();
        var patchingTaskService = new PatchingTaskService(
            _dbContext,
            new SlaService(),
            _workflowService,
            notificationService
        );

        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            notificationService,
            Substitute.For<IRealTimeNotifier>(),
            _workflowService,
            patchingTaskService
        );
        _sut = new RemediationDecisionService(
            _dbContext,
            approvalTaskService,
            _workflowService,
            patchingTaskService
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ReconcileResolvedSoftwareRemediationsAsync_ClosesDecisionAndTasks_WhenNoUnresolvedVulnerabilitiesRemain()
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

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var createResult = await _sut.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.ApprovedForPatching,
            "Patch it",
            _userId,
            null,
            null,
            CancellationToken.None
        );

        createResult.IsSuccess.Should().BeTrue();

        var openMatches = await _dbContext.SoftwareVulnerabilityMatches.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId && item.ResolvedAt == null)
            .ToListAsync();
        foreach (var match in openMatches)
        {
            match.Resolve(DateTimeOffset.UtcNow);
        }

        var projections = await _dbContext.NormalizedSoftwareVulnerabilityProjections.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId && item.TenantSoftwareId == graph.TenantSoftware.Id)
            .ToListAsync();
        foreach (var projection in projections)
        {
            projection.UpdateProjection(
                projection.SnapshotId,
                projection.BestMatchMethod,
                projection.BestConfidence,
                projection.AffectedInstallCount,
                projection.AffectedDeviceCount,
                projection.AffectedVersionCount,
                projection.FirstSeenAt,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                projection.EvidenceJson
            );
        }

        await _dbContext.SaveChangesAsync();

        var closedCount = await _sut.ReconcileResolvedSoftwareRemediationsAsync(
            _tenantId,
            null,
            CancellationToken.None
        );

        closedCount.Should().Be(1);

        var decision = await _dbContext.RemediationDecisions.IgnoreQueryFilters().FirstAsync();
        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Expired);

        var tasks = await _dbContext.PatchingTasks.IgnoreQueryFilters().ToListAsync();
        tasks.Should().OnlyContain(task => task.Status == PatchingTaskStatus.Completed);

    }

    [Fact]
    public async Task ReconcileResolvedSoftwareRemediationsAsync_LeavesDecisionOpen_WhenUnresolvedVulnerabilitiesRemain()
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

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var createResult = await _sut.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.ApprovedForPatching,
            "Patch it",
            _userId,
            null,
            null,
            CancellationToken.None
        );

        createResult.IsSuccess.Should().BeTrue();

        var closedCount = await _sut.ReconcileResolvedSoftwareRemediationsAsync(
            _tenantId,
            null,
            CancellationToken.None
        );

        closedCount.Should().Be(0);

        var decision = await _dbContext.RemediationDecisions.IgnoreQueryFilters().FirstAsync();
        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.PendingApproval);

        var tasks = await _dbContext.PatchingTasks.IgnoreQueryFilters().ToListAsync();
        tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAndApprovePatchDecisionAsync_MovesWorkflowIntoExecution()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Infrastructure");
        await _dbContext.Teams.AddAsync(team);

        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();
        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }

        await _dbContext.SaveChangesAsync();

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var createResult = await _sut.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.ApprovedForPatching,
            "Patch it",
            _userId,
            null,
            null,
            CancellationToken.None
        );

        createResult.IsSuccess.Should().BeTrue();

        var workflowAfterCreate = await _dbContext.RemediationWorkflows
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        workflowAfterCreate.CurrentStage.Should().Be(RemediationWorkflowStage.Approval);

        var approvalTask = await _dbContext.ApprovalTasks
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        approvalTask.Status.Should().Be(ApprovalTaskStatus.Pending);

        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            Substitute.For<INotificationService>(),
            Substitute.For<IRealTimeNotifier>(),
            _workflowService,
            new PatchingTaskService(_dbContext, new SlaService(), _workflowService, Substitute.For<INotificationService>())
        );

        await approvalTaskService.ApproveAsync(
            approvalTask.Id,
            _userId,
            "Approved for execution",
            CancellationToken.None
        );

        var updatedWorkflow = await _dbContext.RemediationWorkflows
            .FirstAsync(item => item.Id == workflowAfterCreate.Id);
        updatedWorkflow.CurrentStage.Should().Be(RemediationWorkflowStage.Execution);
    }

    [Fact]
    public async Task ApproveRiskAcceptanceDecisionAsync_MovesWorkflowIntoClosure_AndSkipsExecution()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Infrastructure");
        await _dbContext.Teams.AddAsync(team);

        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();
        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }

        await _dbContext.SaveChangesAsync();

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var createResult = await _sut.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.RiskAcceptance,
            "Accepted with controls.",
            _userId,
            DateTimeOffset.UtcNow.AddDays(30),
            null,
            CancellationToken.None
        );

        createResult.IsSuccess.Should().BeTrue();

        var workflowAfterCreate = await _dbContext.RemediationWorkflows
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        workflowAfterCreate.CurrentStage.Should().Be(RemediationWorkflowStage.Approval);

        var approvalTask = await _dbContext.ApprovalTasks
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();

        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            Substitute.For<INotificationService>(),
            Substitute.For<IRealTimeNotifier>(),
            _workflowService,
            new PatchingTaskService(_dbContext, new SlaService(), _workflowService, Substitute.For<INotificationService>())
        );

        await approvalTaskService.ApproveAsync(
            approvalTask.Id,
            _userId,
            "Approved as active exception",
            CancellationToken.None
        );

        var updatedWorkflow = await _dbContext.RemediationWorkflows
            .FirstAsync(item => item.Id == workflowAfterCreate.Id);
        updatedWorkflow.CurrentStage.Should().Be(RemediationWorkflowStage.Closure);

        var executionStage = await _dbContext.RemediationWorkflowStageRecords
            .Where(item => item.RemediationWorkflowId == workflowAfterCreate.Id && item.Stage == RemediationWorkflowStage.Execution)
            .OrderByDescending(item => item.StartedAt)
            .FirstAsync();
        executionStage.Status.Should().Be(RemediationWorkflowStageStatus.Skipped);
    }

    [Fact]
    public async Task GetOrCreateActiveWorkflowAsync_StartsInSecurityAnalysis_WhenNoPriorWorkflowExists()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Infrastructure");
        await _dbContext.Teams.AddAsync(team);
        await _dbContext.SaveChangesAsync();

        var workflow = await _workflowService.GetOrCreateActiveWorkflowAsync(
            _tenantId,
            graph.TenantSoftware.Id,
            CancellationToken.None
        );
        await _dbContext.SaveChangesAsync();

        workflow.CurrentStage.Should().Be(RemediationWorkflowStage.SecurityAnalysis);
        workflow.RecurrenceSourceWorkflowId.Should().BeNull();

        var stageRecords = await _dbContext.RemediationWorkflowStageRecords
            .Where(record => record.RemediationWorkflowId == workflow.Id)
            .ToListAsync();
        stageRecords.Should().ContainSingle(record => record.Stage == RemediationWorkflowStage.SecurityAnalysis);
        stageRecords.Should().NotContain(record => record.Stage == RemediationWorkflowStage.Verification);
    }

    [Fact]
    public async Task VerifyAndCarryForwardDecisionAsync_StartsRecurrenceInVerification_AndReopensRiskAcceptanceInApproval()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Infrastructure");
        await _dbContext.Teams.AddAsync(team);

        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();
        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }

        await _dbContext.SaveChangesAsync();

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var firstDecisionResult = await _sut.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.RiskAcceptance,
            "Accepted previously.",
            _userId,
            DateTimeOffset.UtcNow.AddDays(30),
            null,
            CancellationToken.None
        );

        firstDecisionResult.IsSuccess.Should().BeTrue();
        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            Substitute.For<INotificationService>(),
            Substitute.For<IRealTimeNotifier>(),
            _workflowService,
            new PatchingTaskService(_dbContext, new SlaService(), _workflowService, Substitute.For<INotificationService>())
        );
        var firstApprovalTask = await _dbContext.ApprovalTasks
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        await approvalTaskService.ApproveAsync(firstApprovalTask.Id, _userId, "Renewed exception approved.", CancellationToken.None);
        await _dbContext.SaveChangesAsync();

        var firstWorkflow = await _dbContext.RemediationWorkflows
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        firstWorkflow.Complete();
        await _dbContext.SaveChangesAsync();

        var recurringWorkflow = await _workflowService.GetOrCreateActiveWorkflowAsync(
            _tenantId,
            graph.TenantSoftware.Id,
            CancellationToken.None
        );
        await _dbContext.SaveChangesAsync();

        recurringWorkflow.CurrentStage.Should().Be(RemediationWorkflowStage.Verification);
        recurringWorkflow.RecurrenceSourceWorkflowId.Should().Be(firstWorkflow.Id);

        var carryForwardResult = await _sut.VerifyAndCarryForwardDecisionAsync(
            _tenantId,
            recurringWorkflow.Id,
            firstDecisionResult.Value,
            _userId,
            CancellationToken.None
        );

        carryForwardResult.IsSuccess.Should().BeTrue();
        carryForwardResult.Value.Outcome.Should().Be(RemediationOutcome.RiskAcceptance);
        carryForwardResult.Value.ApprovalStatus.Should().Be(DecisionApprovalStatus.PendingApproval);

        var updatedWorkflow = await _dbContext.RemediationWorkflows
            .FirstAsync(item => item.Id == recurringWorkflow.Id);
        updatedWorkflow.CurrentStage.Should().Be(RemediationWorkflowStage.Approval);

        var verificationRecord = await _dbContext.RemediationWorkflowStageRecords
            .Where(record =>
                record.RemediationWorkflowId == recurringWorkflow.Id
                && record.Stage == RemediationWorkflowStage.Verification)
            .OrderByDescending(record => record.StartedAt)
            .FirstAsync();
        verificationRecord.Status.Should().Be(RemediationWorkflowStageStatus.Completed);
    }

    [Fact]
    public async Task VerifyAndCarryForwardDecisionAsync_GoesStraightToExecution_WhenPreviousPostureWasPatch()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Infrastructure");
        await _dbContext.Teams.AddAsync(team);

        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();
        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }

        await _dbContext.SaveChangesAsync();

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var firstDecisionResult = await _sut.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.ApprovedForPatching,
            "Patch it again if it recurs.",
            _userId,
            null,
            null,
            CancellationToken.None
        );

        firstDecisionResult.IsSuccess.Should().BeTrue();
        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            Substitute.For<INotificationService>(),
            Substitute.For<IRealTimeNotifier>(),
            _workflowService,
            new PatchingTaskService(_dbContext, new SlaService(), _workflowService, Substitute.For<INotificationService>())
        );
        var firstApprovalTask = await _dbContext.ApprovalTasks
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        await approvalTaskService.ApproveAsync(firstApprovalTask.Id, _userId, "Patch again if it returns.", CancellationToken.None);

        var firstWorkflow = await _dbContext.RemediationWorkflows
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        firstWorkflow.Complete();
        await _dbContext.SaveChangesAsync();

        var recurringWorkflow = await _workflowService.GetOrCreateActiveWorkflowAsync(
            _tenantId,
            graph.TenantSoftware.Id,
            CancellationToken.None
        );
        await _dbContext.SaveChangesAsync();

        recurringWorkflow.CurrentStage.Should().Be(RemediationWorkflowStage.Verification);
        recurringWorkflow.RecurrenceSourceWorkflowId.Should().Be(firstWorkflow.Id);

        var carryForwardResult = await _sut.VerifyAndCarryForwardDecisionAsync(
            _tenantId,
            recurringWorkflow.Id,
            firstDecisionResult.Value,
            _userId,
            CancellationToken.None
        );

        carryForwardResult.IsSuccess.Should().BeTrue();
        carryForwardResult.Value.Outcome.Should().Be(RemediationOutcome.ApprovedForPatching);
        carryForwardResult.Value.ApprovalStatus.Should().Be(DecisionApprovalStatus.Approved);

        var updatedWorkflow = await _dbContext.RemediationWorkflows
            .FirstAsync(item => item.Id == recurringWorkflow.Id);
        updatedWorkflow.CurrentStage.Should().Be(RemediationWorkflowStage.Execution);
    }
}
