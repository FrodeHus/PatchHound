using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Workflows;

public sealed class WorkflowTriggerService(
    PatchHoundDbContext dbContext,
    IWorkflowEngine workflowEngine,
    ILogger<WorkflowTriggerService> logger
) : IWorkflowTriggerService
{
    public async Task FireAsync(
        WorkflowTrigger trigger,
        Guid tenantId,
        string contextJson,
        CancellationToken ct
    )
    {
        var definitions = await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .Where(d =>
                d.Status == WorkflowDefinitionStatus.Published
                && d.TriggerType == trigger
                && (d.TenantId == tenantId || d.TenantId == null))
            .ToListAsync(ct);

        if (definitions.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Firing {TriggerType} for tenant {TenantId}. Matched {DefinitionCount} workflow definition(s).",
            trigger, tenantId, definitions.Count);

        foreach (var definition in definitions)
        {
            try
            {
                await workflowEngine.StartWorkflowAsync(definition.Id, contextJson, triggeredBy: null, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to start workflow {DefinitionId} ({DefinitionName}) for trigger {TriggerType} on tenant {TenantId}.",
                    definition.Id, definition.Name, trigger, tenantId);
            }
        }
    }
}
