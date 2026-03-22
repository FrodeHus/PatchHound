using PatchHound.Core.Enums;

namespace PatchHound.Core.Interfaces;

public interface IWorkflowTriggerService
{
    Task FireAsync(
        WorkflowTrigger trigger,
        Guid tenantId,
        string contextJson,
        CancellationToken ct
    );

    Task FireManyAsync(
        Guid tenantId,
        IReadOnlyList<(WorkflowTrigger Trigger, string ContextJson)> triggers,
        CancellationToken ct
    );
}
