using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class WorkflowDefinition
{
    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public WorkflowScope Scope { get; private set; }
    public WorkflowTrigger TriggerType { get; private set; }
    public int Version { get; private set; }
    public WorkflowDefinitionStatus Status { get; private set; }
    public string GraphJson { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    private WorkflowDefinition() { }

    public static WorkflowDefinition Create(
        Guid? tenantId,
        string name,
        string? description,
        WorkflowScope scope,
        WorkflowTrigger triggerType,
        string graphJson,
        Guid createdBy
    )
    {
        var now = DateTimeOffset.UtcNow;
        return new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description?.Trim(),
            Scope = scope,
            TriggerType = triggerType,
            Version = 1,
            Status = WorkflowDefinitionStatus.Draft,
            GraphJson = graphJson,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
        };
    }

    public void Update(string name, string? description, string graphJson)
    {
        Name = name.Trim();
        Description = description?.Trim();
        GraphJson = graphJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Publish()
    {
        if (Status == WorkflowDefinitionStatus.Published)
            Version++;

        Status = WorkflowDefinitionStatus.Published;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Archive()
    {
        Status = WorkflowDefinitionStatus.Archived;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
