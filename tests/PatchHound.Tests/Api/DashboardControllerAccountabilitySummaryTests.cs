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

public class DashboardControllerAccountabilitySummaryTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly DashboardController _controller;

    public DashboardControllerAccountabilitySummaryTests()
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
    public async Task GetSummary_ReturnsExecutiveAccountabilityRollups_ByOwnerSourceAndTenant()
    {
        var manualTeam = Team.Create(_tenantId, "Manual Owners");
        var ruleTeam = Team.Create(_tenantId, "Rule Owners");
        var defaultTeam = Team.CreateDefault(_tenantId, "Default Queue");
        var otherTenantTeam = Team.Create(_otherTenantId, "Other Tenant");
        await _dbContext.AddRangeAsync(manualTeam, ruleTeam, defaultTeam, otherTenantTeam);

        var sourceId = Guid.NewGuid();
        var manualDevice = Device.Create(_tenantId, sourceId, "manual-device", "Manual Device", Criticality.High);
        manualDevice.AssignTeamOwner(manualTeam.Id);
        var ruleDevice = Device.Create(_tenantId, sourceId, "rule-device", "Rule Device", Criticality.High);
        ruleDevice.AssignTeamOwnerFromRule(ruleTeam.Id, Guid.NewGuid());
        var defaultDevice = Device.Create(_tenantId, sourceId, "default-device", "Default Device", Criticality.Medium);
        defaultDevice.SetFallbackTeamFromRule(defaultTeam.Id, Guid.NewGuid());
        var unownedDevice = Device.Create(_tenantId, sourceId, "unowned-device", "Unowned Device", Criticality.Low);
        var otherTenantDevice = Device.Create(_otherTenantId, sourceId, "other-device", "Other Device", Criticality.High);
        otherTenantDevice.AssignTeamOwner(otherTenantTeam.Id);
        await _dbContext.AddRangeAsync(manualDevice, ruleDevice, defaultDevice, unownedDevice, otherTenantDevice);

        var manualProduct = SoftwareProduct.Create("Manual", "Agent", null);
        var ruleProduct = SoftwareProduct.Create("Rule", "Agent", null);
        var defaultProduct = SoftwareProduct.Create("Default", "Agent", null);
        var unownedProduct = SoftwareProduct.Create("Unowned", "Agent", null);
        var otherTenantProduct = SoftwareProduct.Create("Other", "Agent", null);
        await _dbContext.AddRangeAsync(manualProduct, ruleProduct, defaultProduct, unownedProduct, otherTenantProduct);
        await _dbContext.SaveChangesAsync();

        var manualSoftware = SoftwareTenantRecord.Create(_tenantId, null, manualProduct.Id, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow);
        manualSoftware.AssignOwnerTeam(manualTeam.Id);
        var ruleSoftware = SoftwareTenantRecord.Create(_tenantId, null, ruleProduct.Id, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow);
        ruleSoftware.AssignOwnerTeamFromRule(ruleTeam.Id, Guid.NewGuid());
        var defaultSoftware = SoftwareTenantRecord.Create(_tenantId, null, defaultProduct.Id, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow);
        defaultSoftware.AssignOwnerTeam(defaultTeam.Id);
        var unownedSoftware = SoftwareTenantRecord.Create(_tenantId, null, unownedProduct.Id, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow);
        var otherTenantSoftware = SoftwareTenantRecord.Create(_otherTenantId, null, otherTenantProduct.Id, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow);
        otherTenantSoftware.AssignOwnerTeam(otherTenantTeam.Id);
        await _dbContext.AddRangeAsync(manualSoftware, ruleSoftware, defaultSoftware, unownedSoftware, otherTenantSoftware);

        _dbContext.TeamRiskScores.AddRange(
            TeamRiskScore.Create(_tenantId, manualTeam.Id, 860m, 860m, 2, 1, 0, 0, 1, 3, "[]", "1"),
            TeamRiskScore.Create(_tenantId, ruleTeam.Id, 780m, 780m, 1, 2, 0, 0, 1, 3, "[]", "1"),
            TeamRiskScore.Create(_tenantId, defaultTeam.Id, 620m, 620m, 0, 1, 0, 0, 1, 1, "[]", "1"),
            TeamRiskScore.Create(_otherTenantId, otherTenantTeam.Id, 999m, 999m, 9, 9, 0, 0, 1, 18, "[]", "1")
        );

        var manualCase = RemediationCase.Create(_tenantId, manualProduct.Id);
        var manualWorkflow = RemediationWorkflow.Create(_tenantId, manualCase.Id, manualTeam.Id);
        var manualDecision = RemediationDecision.Create(_tenantId, manualCase.Id, RemediationOutcome.ApprovedForPatching, null, Guid.NewGuid());
        manualDecision.AttachToWorkflow(manualWorkflow.Id);
        var overduePatching = PatchingTask.Create(_tenantId, manualCase.Id, manualDecision.Id, manualTeam.Id, DateTimeOffset.UtcNow.AddDays(-1));
        overduePatching.AttachToWorkflow(manualWorkflow.Id);

        var ruleCase = RemediationCase.Create(_tenantId, ruleProduct.Id);
        var ruleWorkflow = RemediationWorkflow.Create(_tenantId, ruleCase.Id, ruleTeam.Id, RemediationWorkflowStage.RemediationDecision);
        var ruleDecision = RemediationDecision.Create(
            _tenantId,
            ruleCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Accepted for one cycle",
            Guid.NewGuid(),
            DecisionApprovalStatus.PendingApproval);
        ruleDecision.AttachToWorkflow(ruleWorkflow.Id);
        var overdueApproval = ApprovalTask.Create(
            _tenantId,
            ruleCase.Id,
            ruleDecision.Id,
            RemediationOutcome.RiskAcceptance,
            ApprovalTaskStatus.Pending,
            DateTimeOffset.UtcNow.AddDays(-1));
        overdueApproval.AttachToWorkflow(ruleWorkflow.Id);

        var defaultCase = RemediationCase.Create(_tenantId, defaultProduct.Id);
        var defaultWorkflow = RemediationWorkflow.Create(_tenantId, defaultCase.Id, defaultTeam.Id);
        var acceptedRisk = RemediationDecision.Create(
            _tenantId,
            defaultCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Default queue accepted temporarily",
            Guid.NewGuid(),
            DecisionApprovalStatus.Approved);
        acceptedRisk.AttachToWorkflow(defaultWorkflow.Id);

        var otherCase = RemediationCase.Create(_otherTenantId, otherTenantProduct.Id);
        var otherWorkflow = RemediationWorkflow.Create(_otherTenantId, otherCase.Id, otherTenantTeam.Id, RemediationWorkflowStage.RemediationDecision);

        await _dbContext.AddRangeAsync(
            manualCase,
            manualWorkflow,
            manualDecision,
            overduePatching,
            ruleCase,
            ruleWorkflow,
            ruleDecision,
            overdueApproval,
            defaultCase,
            defaultWorkflow,
            acceptedRisk,
            otherCase,
            otherWorkflow);
        await _dbContext.SaveChangesAsync();

        var dto = await GetSummaryAsync();

        dto.Accountability.Should().NotBeNull();
        var accountability = dto.Accountability!;
        accountability.UnownedAssetCount.Should().Be(1);
        accountability.UnownedSoftwareCount.Should().Be(1);
        accountability.DefaultRoutedAssetCount.Should().Be(1);
        accountability.DefaultRoutedSoftwareCount.Should().Be(1);
        accountability.AwaitingDecisionCount.Should().Be(1);
        accountability.OverdueApprovalCount.Should().Be(1);
        accountability.OverduePatchingTaskCount.Should().Be(1);
        accountability.AcceptedRiskCount.Should().Be(1);

        accountability.TopOwners.Should().NotContain(row => row.OwnerName == "Other Tenant");

        var manualRow = accountability.TopOwners.Should().Contain(row => row.TeamId == manualTeam.Id).Subject;
        manualRow.OwnerAssignmentSource.Should().Be("Manual");
        manualRow.OverduePatchingTaskCount.Should().Be(1);
        manualRow.CriticalOpenExposureCount.Should().Be(2);

        var ruleRow = accountability.TopOwners.Should().Contain(row => row.TeamId == ruleTeam.Id).Subject;
        ruleRow.OwnerAssignmentSource.Should().Be("Rule");
        ruleRow.AwaitingDecisionCount.Should().Be(1);
        ruleRow.OverdueApprovalCount.Should().Be(1);

        var defaultRow = accountability.TopOwners.Should().Contain(row => row.TeamId == defaultTeam.Id).Subject;
        defaultRow.OwnerAssignmentSource.Should().Be("Default");
        defaultRow.DefaultRoutedAssetCount.Should().Be(1);
        defaultRow.DefaultRoutedSoftwareCount.Should().Be(1);
        defaultRow.AcceptedRiskCount.Should().Be(1);

        var unownedRow = accountability.TopOwners.Should().Contain(row => row.TeamId == null).Subject;
        unownedRow.UnownedAssetCount.Should().Be(1);
        unownedRow.UnownedSoftwareCount.Should().Be(1);
    }

    private async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<DashboardSummaryDto>().Subject;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
