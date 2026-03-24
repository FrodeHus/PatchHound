using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class ApprovalTaskServiceTests : IDisposable
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IRealTimeNotifier _realTimeNotifier;
    private readonly ApprovalTaskService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public ApprovalTaskServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.CurrentUserId.Returns(_userId);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );

        _notificationService = Substitute.For<INotificationService>();
        _realTimeNotifier = Substitute.For<IRealTimeNotifier>();
        var auditLogWriter = new AuditLogWriter(_dbContext, tenantContext);

        _sut = new ApprovalTaskService(
            _dbContext,
            _notificationService,
            auditLogWriter,
            _realTimeNotifier
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private RemediationDecision CreateDecision(RemediationOutcome outcome = RemediationOutcome.RiskAcceptance)
    {
        var tenantSoftwareId = Guid.NewGuid();
        var softwareAssetId = Guid.NewGuid();
        var decision = RemediationDecision.Create(
            _tenantId,
            tenantSoftwareId,
            softwareAssetId,
            outcome,
            outcome == RemediationOutcome.ApprovedForPatching ? null : "Test justification",
            _userId,
            reEvaluationDate: outcome == RemediationOutcome.PatchingDeferred
                ? DateTimeOffset.UtcNow.AddDays(30)
                : null
        );
        _dbContext.RemediationDecisions.Add(decision);
        _dbContext.SaveChanges();
        return decision;
    }

    [Fact]
    public async Task CreateForDecisionAsync_RiskAcceptance_CreatesPendingTask()
    {
        var decision = CreateDecision(RemediationOutcome.RiskAcceptance);

        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        task.Status.Should().Be(ApprovalTaskStatus.Pending);
        task.Type.Should().Be(ApprovalTaskType.RiskAcceptanceApproval);
        task.RequiresJustification.Should().BeTrue();

        var persisted = await _dbContext.ApprovalTasks.IgnoreQueryFilters().FirstAsync();
        persisted.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task CreateForDecisionAsync_ApprovedForPatching_CreatesAutoApprovedTask()
    {
        var decision = CreateDecision(RemediationOutcome.ApprovedForPatching);

        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        task.Status.Should().Be(ApprovalTaskStatus.AutoApproved);
        task.Type.Should().Be(ApprovalTaskType.PatchingApproved);
        task.RequiresJustification.Should().BeFalse();
    }

    [Fact]
    public async Task CreateForDecisionAsync_SendsRealTimeNotification()
    {
        var decision = CreateDecision(RemediationOutcome.RiskAcceptance);

        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        await _realTimeNotifier.Received(1)
            .NotifyApprovalTaskCreatedAsync(_tenantId, task.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAsync_ResolvesTaskAndApprovesDecision()
    {
        var decision = CreateDecision(RemediationOutcome.RiskAcceptance);
        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);
        var approverId = Guid.NewGuid();

        var result = await _sut.ApproveAsync(task.Id, approverId, "Looks good", CancellationToken.None);

        result.Status.Should().Be(ApprovalTaskStatus.Approved);
        result.ResolvedBy.Should().Be(approverId);
        result.ResolutionJustification.Should().Be("Looks good");

        var updatedDecision = await _dbContext.RemediationDecisions
            .IgnoreQueryFilters()
            .FirstAsync(d => d.Id == decision.Id);
        updatedDecision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Approved);
    }

    [Fact]
    public async Task DenyAsync_ResolvesTaskAndRejectsDecision()
    {
        var decision = CreateDecision(RemediationOutcome.RiskAcceptance);
        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);
        var denierId = Guid.NewGuid();

        var result = await _sut.DenyAsync(task.Id, denierId, "Risk too high", CancellationToken.None);

        result.Status.Should().Be(ApprovalTaskStatus.Denied);

        var updatedDecision = await _dbContext.RemediationDecisions
            .IgnoreQueryFilters()
            .FirstAsync(d => d.Id == decision.Id);
        updatedDecision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Rejected);
    }

    [Fact]
    public async Task DenyAsync_NotifiesAnalyst()
    {
        var decision = CreateDecision(RemediationOutcome.RiskAcceptance);
        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        _notificationService.ClearReceivedCalls();

        await _sut.DenyAsync(task.Id, Guid.NewGuid(), "Not acceptable", CancellationToken.None);

        await _notificationService.Received(1).SendAsync(
            decision.DecidedBy,
            _tenantId,
            NotificationType.ApprovalTaskDenied,
            Arg.Any<string>(),
            Arg.Any<string>(),
            "ApprovalTask",
            task.Id,
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task AutoDenyExpiredAsync_DeniesExpiredPendingTasks()
    {
        var decision = CreateDecision(RemediationOutcome.RiskAcceptance);
        // Create a task that's already expired
        var task = await _sut.CreateForDecisionAsync(decision, -1, CancellationToken.None);

        var count = await _sut.AutoDenyExpiredAsync(CancellationToken.None);

        count.Should().Be(1);
        var updated = await _dbContext.ApprovalTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == task.Id);
        updated.Status.Should().Be(ApprovalTaskStatus.AutoDenied);
    }

    [Fact]
    public async Task AutoDenyExpiredAsync_SkipsNonExpiredTasks()
    {
        var decision = CreateDecision(RemediationOutcome.RiskAcceptance);
        await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        var count = await _sut.AutoDenyExpiredAsync(CancellationToken.None);

        count.Should().Be(0);
    }

    [Fact]
    public async Task MarkAsReadAsync_SetsReadAt()
    {
        var decision = CreateDecision(RemediationOutcome.ApprovedForPatching);
        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        await _sut.MarkAsReadAsync(task.Id, CancellationToken.None);

        var updated = await _dbContext.ApprovalTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == task.Id);
        updated.ReadAt.Should().NotBeNull();
        updated.ReadAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
