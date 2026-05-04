using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Tests.Core;

public class ApprovalTaskTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CaseId = Guid.NewGuid();
    private static readonly Guid DecisionId = Guid.NewGuid();
    private static readonly DateTimeOffset Expiry = DateTimeOffset.UtcNow.AddHours(24);

    [Theory]
    [InlineData(RemediationOutcome.RiskAcceptance)]
    [InlineData(RemediationOutcome.AlternateMitigation)]
    public void Create_RiskAcceptanceOrAlternateMitigation_SetsPendingApproval(RemediationOutcome outcome)
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, outcome, null, Expiry);

        task.Id.Should().NotBeEmpty();
        task.TenantId.Should().Be(TenantId);
        task.RemediationDecisionId.Should().Be(DecisionId);
        task.Type.Should().Be(ApprovalTaskType.RiskAcceptanceApproval);
        task.Status.Should().Be(ApprovalTaskStatus.Pending);
        task.RequiresJustification.Should().BeTrue();
        task.VisibleToRoles.Should().BeEquivalentTo(new[] { RoleName.GlobalAdmin, RoleName.SecurityManager });
        task.ExpiresAt.Should().Be(Expiry);
        task.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_ApprovedForPatching_SetsPendingApproval_WhenExplicitlyRequired()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.ApprovedForPatching, ApprovalTaskStatus.Pending, Expiry);

        task.Type.Should().Be(ApprovalTaskType.PatchingApproved);
        task.Status.Should().Be(ApprovalTaskStatus.Pending);
        task.RequiresJustification.Should().BeFalse();
        task.VisibleToRoles.Should().BeEquivalentTo(new[] { RoleName.GlobalAdmin, RoleName.TechnicalManager });
    }

    [Fact]
    public void Create_PatchingDeferred_RoutesToSecurityApproval()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.PatchingDeferred, null, Expiry);

        task.Type.Should().Be(ApprovalTaskType.PatchingDeferred);
        task.Status.Should().Be(ApprovalTaskStatus.Pending);
        task.RequiresJustification.Should().BeTrue();
        task.VisibleToRoles.Should().BeEquivalentTo(new[] { RoleName.GlobalAdmin, RoleName.SecurityManager });
    }

    [Fact]
    public void Approve_PendingTask_WithJustification_SetsApproved()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.RiskAcceptance, null, Expiry);
        var userId = Guid.NewGuid();

        task.Approve(userId, "Accepted per review meeting");

        task.Status.Should().Be(ApprovalTaskStatus.Approved);
        task.ResolvedBy.Should().Be(userId);
        task.ResolvedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        task.ResolutionJustification.Should().Be("Accepted per review meeting");
    }

    [Fact]
    public void Approve_PendingTask_RequiresJustification_ThrowsWhenMissing()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.RiskAcceptance, null, Expiry);

        var act = () => task.Approve(Guid.NewGuid(), null);

        act.Should().Throw<ArgumentException>().WithMessage("*Justification*required*");
    }

    [Fact]
    public void Approve_NonPendingTask_Throws()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.ApprovedForPatching, ApprovalTaskStatus.AutoApproved, Expiry);

        var act = () => task.Approve(Guid.NewGuid(), "reason");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot approve*");
    }

    [Fact]
    public void Deny_PendingTask_WithJustification_SetsDenied()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.AlternateMitigation, null, Expiry);
        var userId = Guid.NewGuid();

        task.Deny(userId, "Insufficient risk documentation");

        task.Status.Should().Be(ApprovalTaskStatus.Denied);
        task.ResolvedBy.Should().Be(userId);
        task.ResolutionJustification.Should().Be("Insufficient risk documentation");
    }

    [Fact]
    public void Deny_PendingTask_RequiresJustification_ThrowsWhenMissing()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.AlternateMitigation, null, Expiry);

        var act = () => task.Deny(Guid.NewGuid(), "");

        act.Should().Throw<ArgumentException>().WithMessage("*Justification*required*");
    }

    [Fact]
    public void Deny_NonPendingTask_Throws()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.RiskAcceptance, null, Expiry);
        task.Approve(Guid.NewGuid(), "approved");

        var act = () => task.Deny(Guid.NewGuid(), "reason");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot deny*");
    }

    [Fact]
    public void AutoDeny_PendingTask_SetsAutoDenied()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.RiskAcceptance, null, Expiry);

        task.AutoDeny();

        task.Status.Should().Be(ApprovalTaskStatus.AutoDenied);
        task.ResolvedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        task.ResolvedBy.Should().BeNull();
    }

    [Fact]
    public void AutoDeny_NonPendingTask_Throws()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.ApprovedForPatching, ApprovalTaskStatus.AutoApproved, Expiry);

        var act = () => task.AutoDeny();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot auto-deny*");
    }

    [Fact]
    public void MarkAsRead_SetsReadAt()
    {
        var task = ApprovalTask.Create(TenantId, CaseId, DecisionId, RemediationOutcome.ApprovedForPatching, ApprovalTaskStatus.AutoApproved, Expiry);

        task.MarkAsRead();

        task.ReadAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
