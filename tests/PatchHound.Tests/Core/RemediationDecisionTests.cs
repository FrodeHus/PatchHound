using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Tests.Core;

public class RemediationDecisionTests
{
    private static RemediationDecision CreateApprovedDecision(
        RemediationOutcome outcome = RemediationOutcome.RiskAcceptance)
    {
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: Guid.NewGuid(),
            softwareAssetId: Guid.NewGuid(),
            outcome: outcome,
            justification: "Test justification",
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.Approved
        );
        return decision;
    }

    [Fact]
    public void Reopen_from_Approved_sets_Reopened_status()
    {
        var decision = CreateApprovedDecision();
        decision.Reopen();
        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Reopened);
        decision.ReopenCount.Should().Be(1);
        decision.ReopenedAt.Should().NotBeNull();
        decision.ApprovedBy.Should().BeNull();
        decision.ApprovedAt.Should().BeNull();
        decision.ExpiryDate.Should().BeNull();
        decision.ReEvaluationDate.Should().BeNull();
    }

    [Fact]
    public void Reopen_from_Expired_sets_Reopened_status()
    {
        var decision = CreateApprovedDecision();
        decision.Expire();
        decision.Reopen();
        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Reopened);
        decision.ReopenCount.Should().Be(1);
    }

    [Fact]
    public void Reopen_from_Rejected_sets_Reopened_status()
    {
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: Guid.NewGuid(),
            softwareAssetId: Guid.NewGuid(),
            outcome: RemediationOutcome.RiskAcceptance,
            justification: "Test justification",
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.PendingApproval
        );
        decision.Reject(Guid.NewGuid());
        decision.Reopen();
        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Reopened);
    }

    [Fact]
    public void Reopen_from_PendingApproval_throws()
    {
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: Guid.NewGuid(),
            softwareAssetId: Guid.NewGuid(),
            outcome: RemediationOutcome.RiskAcceptance,
            justification: "Test justification",
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.PendingApproval
        );
        var act = () => decision.Reopen();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reopen_increments_count_on_subsequent_reopens()
    {
        var decision = CreateApprovedDecision();
        decision.Reopen();
        decision.Approve(Guid.NewGuid());
        decision.Reopen();
        decision.ReopenCount.Should().Be(2);
    }

    [Fact]
    public void UpdateDecision_changes_outcome_and_justification_when_Reopened()
    {
        var decision = CreateApprovedDecision(RemediationOutcome.RiskAcceptance);
        decision.Reopen();
        decision.UpdateDecision(RemediationOutcome.ApprovedForPatching, "Switching to patching");
        decision.Outcome.Should().Be(RemediationOutcome.ApprovedForPatching);
        decision.Justification.Should().Be("Switching to patching");
    }

    [Fact]
    public void UpdateDecision_throws_when_not_Reopened()
    {
        var decision = CreateApprovedDecision();
        var act = () => decision.UpdateDecision(RemediationOutcome.ApprovedForPatching, "Test");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpdateDecision_throws_when_justification_required_but_empty()
    {
        var decision = CreateApprovedDecision(RemediationOutcome.ApprovedForPatching);
        decision.Reopen();

        var act = () => decision.UpdateDecision(RemediationOutcome.RiskAcceptance, "");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reopen_clears_stale_expiry_and_reevaluation_dates()
    {
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: Guid.NewGuid(),
            softwareAssetId: Guid.NewGuid(),
            outcome: RemediationOutcome.RiskAcceptance,
            justification: "Test",
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.Approved,
            expiryDate: DateTimeOffset.UtcNow.AddDays(-5),
            reEvaluationDate: DateTimeOffset.UtcNow.AddDays(-3)
        );
        decision.ExpiryDate.Should().NotBeNull();
        decision.ReEvaluationDate.Should().NotBeNull();

        decision.Reopen();

        decision.ExpiryDate.Should().BeNull();
        decision.ReEvaluationDate.Should().BeNull();
    }

    [Theory]
    [InlineData(RemediationOutcome.RiskAcceptance)]
    [InlineData(RemediationOutcome.AlternateMitigation)]
    [InlineData(RemediationOutcome.ApprovedForPatching)]
    [InlineData(RemediationOutcome.PatchingDeferred)]
    public void Reopen_works_for_all_outcomes(RemediationOutcome outcome)
    {
        var reEvalDate = outcome == RemediationOutcome.PatchingDeferred
            ? DateTimeOffset.UtcNow.AddDays(30) : (DateTimeOffset?)null;
        var justification = outcome == RemediationOutcome.ApprovedForPatching
            ? null : "Justification";
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: Guid.NewGuid(),
            softwareAssetId: Guid.NewGuid(),
            outcome: outcome,
            justification: justification,
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.Approved,
            reEvaluationDate: reEvalDate
        );
        decision.Reopen();
        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Reopened);
    }
}
