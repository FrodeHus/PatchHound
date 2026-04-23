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

namespace PatchHound.Tests.Infrastructure.Services;

public class RemediationOwnershipRoutingTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly INotificationService _notificationService;

    public RemediationOwnershipRoutingTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
        _notificationService = Substitute.For<INotificationService>();
    }

    [Fact]
    public async Task GetOrCreateActiveWorkflowAsync_UsesTenantSoftwareOwnerTeamWhenPresent()
    {
        var ownerTeam = Team.Create(_tenantId, "Software Owners");
        var product = SoftwareProduct.Create("Contoso", "Browser", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var tenantSoftware = SoftwareTenantRecord.Create(
            _tenantId,
            null,
            product.Id,
            DateTimeOffset.UtcNow.AddDays(-10),
            DateTimeOffset.UtcNow
        );
        tenantSoftware.AssignOwnerTeam(ownerTeam.Id);

        await _dbContext.AddRangeAsync(ownerTeam, product, remediationCase, tenantSoftware);
        await _dbContext.SaveChangesAsync();

        var sut = new RemediationWorkflowService(_dbContext);

        var workflow = await sut.GetOrCreateActiveWorkflowAsync(
            _tenantId,
            remediationCase.Id,
            CancellationToken.None
        );

        workflow.SoftwareOwnerTeamId.Should().Be(ownerTeam.Id);
    }

    [Fact]
    public async Task EnsurePatchingTasksAsync_UsesWorkflowSoftwareOwnerTeam()
    {
        var ownerTeam = Team.Create(_tenantId, "Software Owners");
        var product = SoftwareProduct.Create("Contoso", "Browser", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var workflow = RemediationWorkflow.Create(
            _tenantId,
            remediationCase.Id,
            ownerTeam.Id
        );
        var decision = RemediationDecision.Create(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.ApprovedForPatching,
            null,
            Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.Approved,
            maintenanceWindowDate: DateTimeOffset.UtcNow.AddDays(7)
        );
        decision.AttachToWorkflow(workflow.Id);

        var vulnerability = Vulnerability.Create(
            "nvd",
            "CVE-2026-OWNER",
            "Test vuln",
            "Test vuln",
            Severity.High,
            7.5m,
            null,
            DateTimeOffset.UtcNow
        );
        var exposure = DeviceVulnerabilityExposure.Observe(
            _tenantId,
            Guid.NewGuid(),
            vulnerability.Id,
            product.Id,
            null,
            "1.0.0",
            ExposureMatchSource.Product,
            DateTimeOffset.UtcNow
        );

        await _dbContext.AddRangeAsync(
            ownerTeam,
            product,
            remediationCase,
            workflow,
            decision,
            vulnerability,
            exposure
        );
        await _dbContext.SaveChangesAsync();

        var workflowService = new RemediationWorkflowService(_dbContext);
        var sut = new PatchingTaskService(
            _dbContext,
            new SlaService(),
            workflowService,
            _notificationService
        );

        var createdCount = await sut.EnsurePatchingTasksAsync(decision, CancellationToken.None);

        createdCount.Should().Be(1);
        var task = await _dbContext.PatchingTasks.SingleAsync();
        task.OwnerTeamId.Should().Be(ownerTeam.Id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
