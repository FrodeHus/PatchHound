using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public interface IWorkflowEngine
{
    Task<WorkflowInstance> StartWorkflowAsync(
        Guid definitionId,
        string contextJson,
        Guid? triggeredBy,
        CancellationToken ct
    );

    Task ResumeWorkflowAsync(Guid instanceId, CancellationToken ct);

    Task CompleteActionAsync(
        Guid actionId,
        Guid userId,
        string? responseJson,
        CancellationToken ct
    );

    Task RejectActionAsync(
        Guid actionId,
        Guid userId,
        string? responseJson,
        CancellationToken ct
    );

    Task CancelWorkflowAsync(Guid instanceId, CancellationToken ct);
}
