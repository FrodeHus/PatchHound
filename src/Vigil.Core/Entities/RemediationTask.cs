using Vigil.Core.Enums;

namespace Vigil.Core.Entities;

public class RemediationTask
{
    public Guid Id { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AssigneeId { get; private set; }
    public Guid AssignedBy { get; private set; }
    public RemediationTaskStatus Status { get; private set; }
    public string? Justification { get; private set; }
    public DateTimeOffset DueDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RemediationTask() { }

    public static RemediationTask Create(
        Guid vulnerabilityId,
        Guid assetId,
        Guid tenantId,
        Guid assigneeId,
        Guid assignedBy,
        DateTimeOffset dueDate
    )
    {
        var now = DateTimeOffset.UtcNow;
        return new RemediationTask
        {
            Id = Guid.NewGuid(),
            VulnerabilityId = vulnerabilityId,
            AssetId = assetId,
            TenantId = tenantId,
            AssigneeId = assigneeId,
            AssignedBy = assignedBy,
            Status = RemediationTaskStatus.Pending,
            DueDate = dueDate,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdateStatus(RemediationTaskStatus status, string? justification = null)
    {
        Status = status;
        Justification = justification;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reassign(Guid newAssigneeId, Guid assignedBy)
    {
        AssigneeId = newAssigneeId;
        AssignedBy = assignedBy;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
