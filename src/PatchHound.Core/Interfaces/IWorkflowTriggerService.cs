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
}
