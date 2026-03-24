using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class ApprovalTask
{
    private readonly List<ApprovalTaskVisibleRole> _visibleRoles = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationDecisionId { get; private set; }
    public ApprovalTaskType Type { get; private set; }
    public ApprovalTaskStatus Status { get; private set; }
    public IReadOnlyCollection<ApprovalTaskVisibleRole> VisibleRoles => _visibleRoles.AsReadOnly();
    public RoleName[] VisibleToRoles => _visibleRoles.Select(role => role.Role).ToArray();
    public bool RequiresJustification { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolutionJustification { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public RemediationDecision RemediationDecision { get; private set; } = null!;

    private ApprovalTask() { }

    public static ApprovalTask Create(
        Guid tenantId,
        Guid remediationDecisionId,
        RemediationOutcome outcome,
        DateTimeOffset expiresAt
    )
    {
        var now = DateTimeOffset.UtcNow;

        var (type, status, roles, requiresJustification) = outcome switch
        {
            RemediationOutcome.RiskAcceptance or RemediationOutcome.AlternateMitigation =>
                (ApprovalTaskType.RiskAcceptanceApproval, ApprovalTaskStatus.Pending,
                    new[] { RoleName.GlobalAdmin, RoleName.SecurityManager }, true),

            RemediationOutcome.ApprovedForPatching =>
                (ApprovalTaskType.PatchingApproved, ApprovalTaskStatus.AutoApproved,
                    new[] { RoleName.GlobalAdmin, RoleName.TechnicalManager }, false),

            RemediationOutcome.PatchingDeferred =>
                (ApprovalTaskType.PatchingDeferred, ApprovalTaskStatus.AutoApproved,
                    new[] { RoleName.GlobalAdmin, RoleName.TechnicalManager }, false),

            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported outcome for approval task."),
        };

        return new ApprovalTask
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationDecisionId = remediationDecisionId,
            Type = type,
            Status = status,
            RequiresJustification = requiresJustification,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now,
        }.SetVisibleRoles(roles);
    }

    private ApprovalTask SetVisibleRoles(IEnumerable<RoleName> roles)
    {
        _visibleRoles.Clear();
        foreach (var role in roles.Distinct())
        {
            var visibleRole = ApprovalTaskVisibleRole.Create(role);
            visibleRole.AttachToTask(Id);
            _visibleRoles.Add(visibleRole);
        }

        return this;
    }

    public void Approve(Guid resolvedBy, string? justification)
    {
        if (Status != ApprovalTaskStatus.Pending)
            throw new InvalidOperationException($"Cannot approve a task with status {Status}.");

        if (RequiresJustification && string.IsNullOrWhiteSpace(justification))
            throw new ArgumentException("Justification is required to approve this task.");

        ResolvedBy = resolvedBy;
        ResolvedAt = DateTimeOffset.UtcNow;
        ResolutionJustification = justification;
        Status = ApprovalTaskStatus.Approved;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deny(Guid resolvedBy, string? justification)
    {
        if (Status != ApprovalTaskStatus.Pending)
            throw new InvalidOperationException($"Cannot deny a task with status {Status}.");

        if (RequiresJustification && string.IsNullOrWhiteSpace(justification))
            throw new ArgumentException("Justification is required to deny this task.");

        ResolvedBy = resolvedBy;
        ResolvedAt = DateTimeOffset.UtcNow;
        ResolutionJustification = justification;
        Status = ApprovalTaskStatus.Denied;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AutoDeny()
    {
        if (Status != ApprovalTaskStatus.Pending)
            throw new InvalidOperationException($"Cannot auto-deny a task with status {Status}.");

        Status = ApprovalTaskStatus.AutoDenied;
        ResolvedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsRead()
    {
        ReadAt = DateTimeOffset.UtcNow;
    }
}
