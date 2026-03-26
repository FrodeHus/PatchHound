using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class PatchingTask
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? RemediationWorkflowId { get; private set; }
    public Guid RemediationDecisionId { get; private set; }
    public Guid TenantSoftwareId { get; private set; }
    public Guid SoftwareAssetId { get; private set; }
    public Guid OwnerTeamId { get; private set; }
    public PatchingTaskStatus Status { get; private set; }
    public DateTimeOffset DueDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public RemediationDecision RemediationDecision { get; private set; } = null!;
    public RemediationWorkflow? RemediationWorkflow { get; private set; }

    private PatchingTask() { }

    public static PatchingTask Create(
        Guid tenantId,
        Guid remediationDecisionId,
        Guid tenantSoftwareId,
        Guid softwareAssetId,
        Guid ownerTeamId,
        DateTimeOffset dueDate
    )
    {
        var now = DateTimeOffset.UtcNow;
        return new PatchingTask
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationDecisionId = remediationDecisionId,
            TenantSoftwareId = tenantSoftwareId,
            SoftwareAssetId = softwareAssetId,
            OwnerTeamId = ownerTeamId,
            Status = PatchingTaskStatus.Pending,
            DueDate = dueDate,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Start()
    {
        Status = PatchingTaskStatus.InProgress;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete()
    {
        Status = PatchingTaskStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AttachToWorkflow(Guid remediationWorkflowId)
    {
        RemediationWorkflowId = remediationWorkflowId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ReassignTenantSoftware(Guid newTenantSoftwareId)
    {
        TenantSoftwareId = newTenantSoftwareId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
