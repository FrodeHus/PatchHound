using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RemediationDecision
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? RemediationWorkflowId { get; private set; }
    public Guid TenantSoftwareId { get; private set; }
    public Guid SoftwareAssetId { get; private set; }
    public RemediationOutcome Outcome { get; private set; }
    public DecisionApprovalStatus ApprovalStatus { get; private set; }
    public string Justification { get; private set; } = null!;
    public Guid DecidedBy { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? ExpiryDate { get; private set; }
    public DateTimeOffset? ReEvaluationDate { get; private set; }
    public DateTimeOffset? LastSlaNotifiedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Asset SoftwareAsset { get; private set; } = null!;
    public RemediationWorkflow? RemediationWorkflow { get; private set; }
    public ICollection<RemediationDecisionVulnerabilityOverride> VulnerabilityOverrides { get; private set; } = [];

    private static readonly RemediationOutcome[] OutcomesRequiringApproval =
    [
        RemediationOutcome.RiskAcceptance,
        RemediationOutcome.AlternateMitigation,
    ];

    private static readonly RemediationOutcome[] OutcomesRequiringJustification =
    [
        RemediationOutcome.RiskAcceptance,
        RemediationOutcome.AlternateMitigation,
        RemediationOutcome.PatchingDeferred,
    ];

    private RemediationDecision() { }

    public static RemediationDecision Create(
        Guid tenantId,
        Guid tenantSoftwareId,
        Guid softwareAssetId,
        RemediationOutcome outcome,
        string? justification,
        Guid decidedBy,
        DateTimeOffset? expiryDate = null,
        DateTimeOffset? reEvaluationDate = null
    )
    {
        if (OutcomesRequiringJustification.Contains(outcome) && string.IsNullOrWhiteSpace(justification))
            throw new ArgumentException($"Justification is required for {outcome}.");

        if (outcome == RemediationOutcome.PatchingDeferred && !reEvaluationDate.HasValue)
            throw new ArgumentException("Re-evaluation date is required for PatchingDeferred.");

        var now = DateTimeOffset.UtcNow;
        var requiresApproval = OutcomesRequiringApproval.Contains(outcome);

        return new RemediationDecision
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TenantSoftwareId = tenantSoftwareId,
            SoftwareAssetId = softwareAssetId,
            Outcome = outcome,
            ApprovalStatus = requiresApproval ? DecisionApprovalStatus.PendingApproval : DecisionApprovalStatus.Approved,
            Justification = justification ?? string.Empty,
            DecidedBy = decidedBy,
            DecidedAt = now,
            ApprovedBy = requiresApproval ? null : decidedBy,
            ApprovedAt = requiresApproval ? null : now,
            ExpiryDate = expiryDate,
            ReEvaluationDate = reEvaluationDate,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Approve(Guid approvedBy)
    {
        if (ApprovalStatus != DecisionApprovalStatus.PendingApproval)
            throw new InvalidOperationException($"Cannot approve a decision with status {ApprovalStatus}.");

        ApprovedBy = approvedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        ApprovalStatus = DecisionApprovalStatus.Approved;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(Guid rejectedBy)
    {
        if (ApprovalStatus != DecisionApprovalStatus.PendingApproval)
            throw new InvalidOperationException($"Cannot reject a decision with status {ApprovalStatus}.");

        ApprovedBy = rejectedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        ApprovalStatus = DecisionApprovalStatus.Rejected;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Expire()
    {
        ApprovalStatus = DecisionApprovalStatus.Expired;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSlaNotified()
    {
        LastSlaNotifiedAt = DateTimeOffset.UtcNow;
    }

    public void AttachToWorkflow(Guid remediationWorkflowId)
    {
        RemediationWorkflowId = remediationWorkflowId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
