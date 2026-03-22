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
    private readonly Dictionary<(Guid TenantId, WorkflowTrigger Trigger), List<Guid>> cachedDefinitionIds =
        new();

    public async Task FireAsync(
        WorkflowTrigger trigger,
        Guid tenantId,
        string contextJson,
        CancellationToken ct
    )
    {
        await FireManyAsync(tenantId, [(trigger, contextJson)], ct);
    }

    public async Task FireManyAsync(
        Guid tenantId,
        IReadOnlyList<(WorkflowTrigger Trigger, string ContextJson)> triggers,
        CancellationToken ct
    )
    {
        if (triggers.Count == 0)
        {
            return;
        }

        var groupedTriggers = triggers
            .GroupBy(item => item.Trigger)
            .ToList();

        foreach (var triggerGroup in groupedTriggers)
        {
            var definitionIds = await GetPublishedDefinitionIdsAsync(
                tenantId,
                triggerGroup.Key,
                ct
            );

            if (definitionIds.Count == 0)
            {
                continue;
            }

            logger.LogInformation(
                "Firing {TriggerType} for tenant {TenantId}. Matched {DefinitionCount} workflow definition(s) across {TriggerCount} trigger event(s).",
                triggerGroup.Key,
                tenantId,
                definitionIds.Count,
                triggerGroup.Count()
            );

            foreach (var (_, contextJson) in triggerGroup)
            {
                foreach (var definitionId in definitionIds)
                {
                    try
                    {
                        await workflowEngine.StartWorkflowAsync(
                            definitionId,
                            contextJson,
                            triggeredBy: null,
                            ct
                        );
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Failed to start workflow {DefinitionId} for trigger {TriggerType} on tenant {TenantId}.",
                            definitionId,
                            triggerGroup.Key,
                            tenantId
                        );
                    }
                }
            }
        }
    }

    private async Task<List<Guid>> GetPublishedDefinitionIdsAsync(
        Guid tenantId,
        WorkflowTrigger trigger,
        CancellationToken ct
    )
    {
        if (cachedDefinitionIds.TryGetValue((tenantId, trigger), out var cachedIds))
        {
            return cachedIds;
        }

        var definitionIds = await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .Where(d =>
                d.Status == WorkflowDefinitionStatus.Published
                && d.TriggerType == trigger
                && (d.TenantId == tenantId || d.TenantId == null))
            .Select(d => d.Id)
            .ToListAsync(ct);

        cachedDefinitionIds[(tenantId, trigger)] = definitionIds;
        return definitionIds;
    }
}
