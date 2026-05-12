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
        var workflowService = new RemediationWorkflowService(_dbContext);
        var patchingTaskService = new PatchingTaskService(
            _dbContext,
            new SlaService(),
            workflowService,
            _notificationService
        );
        _sut = new ApprovalTaskService(
            _dbContext,
            _notificationService,
            _realTimeNotifier,
            workflowService,
            patchingTaskService,
            new ApprovedVulnerabilityRemediationService(_dbContext)
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private RemediationDecision CreateDecision(RemediationOutcome outcome = RemediationOutcome.RiskAcceptance)
    {
        var remediationCaseId = Guid.NewGuid();
        var decision = RemediationDecision.Create(
            _tenantId,
            remediationCaseId,
            outcome,
            outcome == RemediationOutcome.ApprovedForPatching ? null : "Test justification",
            _userId,
            outcome == RemediationOutcome.ApprovedForPatching ? DecisionApprovalStatus.PendingApproval : null,
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
    public async Task CreateForDecisionAsync_ApprovedForPatching_CreatesPendingTask_WhenDecisionRequiresApproval()
    {
        var decision = CreateDecision(RemediationOutcome.ApprovedForPatching);

        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        task.Status.Should().Be(ApprovalTaskStatus.Pending);
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

        var result = await _sut.ApproveAsync(task.Id, approverId, "Looks good", null, CancellationToken.None);

        result.Status.Should().Be(ApprovalTaskStatus.Approved);
        result.ResolvedBy.Should().Be(approverId);
        result.ResolutionJustification.Should().Be("Looks good");

        var updatedDecision = await _dbContext.RemediationDecisions
            .IgnoreQueryFilters()
            .FirstAsync(d => d.Id == decision.Id);
        updatedDecision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Approved);
    }

    [Fact]
    public async Task ApproveAsync_UpsertsApprovedVulnerabilityRemediationCoverage()
    {
        var sourceSystem = SourceSystem.Create("defender", "Defender");
        var softwareProduct = SoftwareProduct.Create("Acme", "Widget", null);
        var device = Device.Create(_tenantId, sourceSystem.Id, "dev-coverage", "Coverage Device", Criticality.High);
        var vulnerability = Vulnerability.Create("nvd", "CVE-2026-7001", "Covered vuln", "desc", Severity.Critical, 9.8m, null, DateTimeOffset.UtcNow);
        _dbContext.SourceSystems.Add(sourceSystem);
        _dbContext.SoftwareProducts.Add(softwareProduct);
        _dbContext.Devices.Add(device);
        _dbContext.Vulnerabilities.Add(vulnerability);
        await _dbContext.SaveChangesAsync();

        var installedSoftware = InstalledSoftware.Observe(_tenantId, device.Id, softwareProduct.Id, sourceSystem.Id, "1.0", DateTimeOffset.UtcNow);
        var remediationCase = RemediationCase.Create(_tenantId, softwareProduct.Id);
        _dbContext.InstalledSoftware.Add(installedSoftware);
        _dbContext.RemediationCases.Add(remediationCase);
        await _dbContext.SaveChangesAsync();

        _dbContext.DeviceVulnerabilityExposures.Add(DeviceVulnerabilityExposure.Observe(
            _tenantId,
            device.Id,
            vulnerability.Id,
            softwareProduct.Id,
            installedSoftware.Id,
            "1.0",
            ExposureMatchSource.Product,
            DateTimeOffset.UtcNow));
        var decision = RemediationDecision.Create(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Accepted by security manager",
            _userId,
            DecisionApprovalStatus.PendingApproval,
            expiryDate: DateTimeOffset.UtcNow.AddDays(30));
        _dbContext.RemediationDecisions.Add(decision);
        await _dbContext.SaveChangesAsync();
        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        await _sut.ApproveAsync(task.Id, Guid.NewGuid(), "Approved", null, CancellationToken.None);

        var coverage = await _dbContext.ApprovedVulnerabilityRemediations
            .IgnoreQueryFilters()
            .SingleAsync(item => item.VulnerabilityId == vulnerability.Id);
        coverage.TenantId.Should().Be(_tenantId);
        coverage.RemediationCaseId.Should().Be(remediationCase.Id);
        coverage.RemediationDecisionId.Should().Be(decision.Id);
        coverage.Outcome.Should().Be(RemediationOutcome.RiskAcceptance);
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
    public async Task ApproveAsync_PatchingDecision_RequiresMaintenanceWindowDate()
    {
        var decision = CreateDecision(RemediationOutcome.ApprovedForPatching);
        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        var act = () => _sut.ApproveAsync(task.Id, Guid.NewGuid(), "Approved", null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Maintenance window date is required*");
    }

    [Fact]
    public async Task ApproveAsync_PatchingDecision_SetsMaintenanceWindowDateOnApproval()
    {
        var decision = CreateDecision(RemediationOutcome.ApprovedForPatching);
        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);
        var maintenanceWindowDate = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);

        var result = await _sut.ApproveAsync(task.Id, Guid.NewGuid(), "Approved", maintenanceWindowDate, CancellationToken.None);

        result.Status.Should().Be(ApprovalTaskStatus.Approved);

        var updatedDecision = await _dbContext.RemediationDecisions
            .IgnoreQueryFilters()
            .FirstAsync(d => d.Id == decision.Id);
        updatedDecision.MaintenanceWindowDate.Should().Be(maintenanceWindowDate);
        updatedDecision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Approved);
    }

    [Fact]
    public async Task DenyAsync_PatchingDecision_RequiresJustification()
    {
        var decision = CreateDecision(RemediationOutcome.ApprovedForPatching);
        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        var act = () => _sut.DenyAsync(task.Id, Guid.NewGuid(), "", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Justification*required*");
    }

    [Fact]
    public async Task ApproveAsync_PatchingDecision_NotifiesSecurityManagers()
    {
        var securityManager = User.Create("security@example.test", "Security Manager", Guid.NewGuid().ToString());
        await _dbContext.Users.AddAsync(securityManager);
        await _dbContext.UserTenantRoles.AddAsync(UserTenantRole.Create(securityManager.Id, _tenantId, RoleName.SecurityManager));
        await _dbContext.SaveChangesAsync();
        var decision = CreateDecision(RemediationOutcome.ApprovedForPatching);
        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);
        var maintenanceWindowDate = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);

        _notificationService.ClearReceivedCalls();

        await _sut.ApproveAsync(task.Id, Guid.NewGuid(), "Approved", maintenanceWindowDate, CancellationToken.None);

        await _notificationService.Received(1).SendAsync(
            securityManager.Id,
            _tenantId,
            NotificationType.ApprovalTaskApproved,
            Arg.Any<string>(),
            Arg.Any<string>(),
            "ApprovalTask",
            task.Id,
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task ApproveAsync_SecurityDecision_NotifiesTechnicalManagers()
    {
        var technicalManager = User.Create("technical@example.test", "Technical Manager", Guid.NewGuid().ToString());
        await _dbContext.Users.AddAsync(technicalManager);
        await _dbContext.UserTenantRoles.AddAsync(UserTenantRole.Create(technicalManager.Id, _tenantId, RoleName.TechnicalManager));
        await _dbContext.SaveChangesAsync();
        var decision = CreateDecision(RemediationOutcome.RiskAcceptance);
        var task = await _sut.CreateForDecisionAsync(decision, 24, CancellationToken.None);

        _notificationService.ClearReceivedCalls();

        await _sut.ApproveAsync(task.Id, Guid.NewGuid(), "Approved exception", null, CancellationToken.None);

        await _notificationService.Received(1).SendAsync(
            technicalManager.Id,
            _tenantId,
            NotificationType.ApprovalTaskApproved,
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
