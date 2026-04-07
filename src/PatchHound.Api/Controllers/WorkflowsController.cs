using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement.Mvc;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Workflows;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Authorize]
[FeatureGate(FeatureFlags.Workflows)]
public class WorkflowsController(
    PatchHoundDbContext dbContext,
    IWorkflowEngine workflowEngine,
    ITenantContext tenantContext
) : ControllerBase
{
    // ─── Definitions ─────────────────────────────────────────

    [HttpGet("definitions")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<ActionResult<PagedResponse<WorkflowDefinitionDto>>> ListDefinitions(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? scope,
        [FromQuery] string? status,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = dbContext.WorkflowDefinitions.AsNoTracking().AsQueryable();

        if (tenantId.HasValue)
        {
            if (!tenantContext.HasAccessToTenant(tenantId.Value))
                return Forbid();
            query = query.Where(d => d.TenantId == tenantId.Value);
        }

        if (Enum.TryParse<WorkflowScope>(scope, true, out var parsedScope))
            query = query.Where(d => d.Scope == parsedScope);

        if (Enum.TryParse<WorkflowDefinitionStatus>(status, true, out var parsedStatus))
            query = query.Where(d => d.Status == parsedStatus);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(d => d.UpdatedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(d => new WorkflowDefinitionDto(
                d.Id, d.TenantId, d.Name, d.Description,
                d.Scope.ToString(), d.TriggerType.ToString(),
                d.Version, d.Status.ToString(),
                d.CreatedAt, d.UpdatedAt
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<WorkflowDefinitionDto>(
            items, totalCount, pagination.Page, pagination.BoundedPageSize
        ));
    }

    [HttpGet("definitions/{id:guid}")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> GetDefinition(Guid id, CancellationToken ct)
    {
        var def = await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (def is null) return NotFound();

        return Ok(new WorkflowDefinitionDetailDto(
            def.Id, def.TenantId, def.Name, def.Description,
            def.Scope.ToString(), def.TriggerType.ToString(),
            def.Version, def.Status.ToString(), def.GraphJson,
            def.CreatedAt, def.UpdatedAt, def.CreatedBy
        ));
    }

    [HttpPost("definitions")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> CreateDefinition(
        [FromBody] CreateWorkflowDefinitionRequest request,
        CancellationToken ct
    )
    {
        if (request.TenantId.HasValue && !tenantContext.HasAccessToTenant(request.TenantId.Value))
            return Forbid();

        if (!Enum.TryParse<WorkflowScope>(request.Scope, true, out var scope))
            return BadRequest(new ProblemDetails { Title = $"Invalid scope: {request.Scope}" });

        if (!Enum.TryParse<WorkflowTrigger>(request.TriggerType, true, out var trigger))
            return BadRequest(new ProblemDetails { Title = $"Invalid trigger type: {request.TriggerType}" });

        var definition = WorkflowDefinition.Create(
            request.TenantId,
            request.Name,
            request.Description,
            scope,
            trigger,
            request.GraphJson,
            tenantContext.CurrentUserId
        );

        dbContext.WorkflowDefinitions.Add(definition);
        await dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetDefinition), new { id = definition.Id },
            new WorkflowDefinitionDetailDto(
                definition.Id, definition.TenantId, definition.Name, definition.Description,
                definition.Scope.ToString(), definition.TriggerType.ToString(),
                definition.Version, definition.Status.ToString(), definition.GraphJson,
                definition.CreatedAt, definition.UpdatedAt, definition.CreatedBy
            ));
    }

    [HttpPut("definitions/{id:guid}")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<ActionResult<WorkflowDefinitionDetailDto>> UpdateDefinition(
        Guid id,
        [FromBody] UpdateWorkflowDefinitionRequest request,
        CancellationToken ct
    )
    {
        var def = await dbContext.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return NotFound();

        if (def.Status == WorkflowDefinitionStatus.Archived)
            return BadRequest(new ProblemDetails { Title = "Cannot edit an archived workflow." });

        def.Update(request.Name, request.Description, request.GraphJson);
        await dbContext.SaveChangesAsync(ct);

        return Ok(new WorkflowDefinitionDetailDto(
            def.Id, def.TenantId, def.Name, def.Description,
            def.Scope.ToString(), def.TriggerType.ToString(),
            def.Version, def.Status.ToString(), def.GraphJson,
            def.CreatedAt, def.UpdatedAt, def.CreatedBy
        ));
    }

    [HttpPost("definitions/{id:guid}/publish")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<IActionResult> PublishDefinition(Guid id, CancellationToken ct)
    {
        var def = await dbContext.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return NotFound();

        def.Publish();
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("definitions/{id:guid}/archive")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<IActionResult> ArchiveDefinition(Guid id, CancellationToken ct)
    {
        var def = await dbContext.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return NotFound();

        def.Archive();
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpDelete("definitions/{id:guid}")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<IActionResult> DeleteDefinition(Guid id, CancellationToken ct)
    {
        var def = await dbContext.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return NotFound();

        if (def.Status != WorkflowDefinitionStatus.Draft)
            return BadRequest(new ProblemDetails { Title = "Only draft workflows can be deleted." });

        dbContext.WorkflowDefinitions.Remove(def);
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("definitions/{id:guid}/run")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<ActionResult<WorkflowInstanceDto>> RunDefinition(
        Guid id,
        [FromBody] RunWorkflowRequest? request,
        CancellationToken ct
    )
    {
        var def = await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (def is null) return NotFound();

        if (def.Status != WorkflowDefinitionStatus.Published)
            return BadRequest(new ProblemDetails { Title = "Only published workflows can be run manually." });

        var contextJson = request?.ContextJson ?? "{}";
        var instance = await workflowEngine.StartWorkflowAsync(
            def.Id, contextJson, tenantContext.CurrentUserId, ct);

        return Ok(new WorkflowInstanceDto(
            instance.Id, instance.WorkflowDefinitionId, def.Name,
            instance.DefinitionVersion, instance.TenantId,
            instance.TriggerType.ToString(), instance.Status.ToString(),
            instance.StartedAt, instance.CompletedAt, instance.Error
        ));
    }

    // ─── Instances (Runs) ────────────────────────────────────

    [HttpGet("instances")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<ActionResult<PagedResponse<WorkflowInstanceDto>>> ListInstances(
        [FromQuery] Guid? definitionId,
        [FromQuery] Guid? tenantId,
        [FromQuery] string? status,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = dbContext.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.WorkflowDefinition)
            .AsQueryable();

        if (tenantId.HasValue)
        {
            if (!tenantContext.HasAccessToTenant(tenantId.Value))
                return Forbid();
            query = query.Where(i => i.TenantId == tenantId.Value);
        }

        if (definitionId.HasValue)
            query = query.Where(i => i.WorkflowDefinitionId == definitionId.Value);

        if (Enum.TryParse<WorkflowInstanceStatus>(status, true, out var parsedStatus))
            query = query.Where(i => i.Status == parsedStatus);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(i => i.StartedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(i => new WorkflowInstanceDto(
                i.Id, i.WorkflowDefinitionId, i.WorkflowDefinition.Name,
                i.DefinitionVersion, i.TenantId,
                i.TriggerType.ToString(), i.Status.ToString(),
                i.StartedAt, i.CompletedAt, i.Error
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<WorkflowInstanceDto>(
            items, totalCount, pagination.Page, pagination.BoundedPageSize
        ));
    }

    [HttpGet("instances/{id:guid}")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<ActionResult<WorkflowInstanceDetailDto>> GetInstance(Guid id, CancellationToken ct)
    {
        var instance = await dbContext.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.WorkflowDefinition)
            .Include(i => i.NodeExecutions)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (instance is null) return NotFound();

        return Ok(new WorkflowInstanceDetailDto(
            instance.Id, instance.WorkflowDefinitionId, instance.WorkflowDefinition.Name,
            instance.DefinitionVersion, instance.TenantId,
            instance.TriggerType.ToString(), instance.ContextJson,
            instance.Status.ToString(),
            instance.StartedAt, instance.CompletedAt, instance.Error,
            instance.NodeExecutions.Select(e => new WorkflowNodeExecutionDto(
                e.Id, e.NodeId, e.NodeType, e.Status.ToString(),
                e.InputJson, e.OutputJson, e.Error,
                e.StartedAt, e.CompletedAt,
                e.AssignedTeamId, e.CompletedByUserId
            )).ToList()
        ));
    }

    [HttpPost("instances/{id:guid}/cancel")]
    [Authorize(Policy = Policies.ManageWorkflows)]
    public async Task<IActionResult> CancelInstance(Guid id, CancellationToken ct)
    {
        await workflowEngine.CancelWorkflowAsync(id, ct);
        return NoContent();
    }

    // ─── Actions (Human-in-the-Loop) ─────────────────────────

    [HttpGet("actions")]
    [Authorize]
    public async Task<ActionResult<PagedResponse<WorkflowActionDto>>> ListActions(
        [FromQuery] Guid? teamId,
        [FromQuery] string? status,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = dbContext.WorkflowActions.AsNoTracking()
            .Include(a => a.WorkflowInstance)
                .ThenInclude(i => i.WorkflowDefinition)
            .AsQueryable();

        if (teamId.HasValue)
            query = query.Where(a => a.TeamId == teamId.Value);

        if (Enum.TryParse<WorkflowActionStatus>(status, true, out var parsedStatus))
            query = query.Where(a => a.Status == parsedStatus);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(a => new WorkflowActionDto(
                a.Id, a.WorkflowInstanceId, a.NodeExecutionId,
                a.TenantId, a.TeamId,
                a.ActionType.ToString(), a.Instructions,
                a.Status.ToString(), a.ResponseJson,
                a.DueAt, a.CreatedAt, a.CompletedAt,
                a.CompletedByUserId,
                a.WorkflowInstance.WorkflowDefinition.Name,
                a.WorkflowInstance.ContextJson
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<WorkflowActionDto>(
            items, totalCount, pagination.Page, pagination.BoundedPageSize
        ));
    }

    [HttpGet("actions/mine")]
    [Authorize]
    public async Task<ActionResult<PagedResponse<WorkflowActionDto>>> ListMyActions(
        [FromQuery] string? status,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var userId = tenantContext.CurrentUserId;
        var myTeamIds = await dbContext.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.UserId == userId)
            .Select(tm => tm.TeamId)
            .ToListAsync(ct);

        if (myTeamIds.Count == 0)
            return Ok(new PagedResponse<WorkflowActionDto>([], 0, pagination.Page, pagination.BoundedPageSize));

        var query = dbContext.WorkflowActions.AsNoTracking()
            .Include(a => a.WorkflowInstance)
                .ThenInclude(i => i.WorkflowDefinition)
            .Where(a => myTeamIds.Contains(a.TeamId));

        if (Enum.TryParse<WorkflowActionStatus>(status, true, out var parsedStatus))
            query = query.Where(a => a.Status == parsedStatus);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(a => new WorkflowActionDto(
                a.Id, a.WorkflowInstanceId, a.NodeExecutionId,
                a.TenantId, a.TeamId,
                a.ActionType.ToString(), a.Instructions,
                a.Status.ToString(), a.ResponseJson,
                a.DueAt, a.CreatedAt, a.CompletedAt,
                a.CompletedByUserId,
                a.WorkflowInstance.WorkflowDefinition.Name,
                a.WorkflowInstance.ContextJson
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<WorkflowActionDto>(
            items, totalCount, pagination.Page, pagination.BoundedPageSize
        ));
    }

    [HttpPost("actions/{id:guid}/complete")]
    [Authorize]
    public async Task<IActionResult> CompleteAction(
        Guid id,
        [FromBody] CompleteWorkflowActionRequest request,
        CancellationToken ct
    )
    {
        await workflowEngine.CompleteActionAsync(id, tenantContext.CurrentUserId, request.ResponseJson, ct);
        return NoContent();
    }

    [HttpPost("actions/{id:guid}/reject")]
    [Authorize]
    public async Task<IActionResult> RejectAction(
        Guid id,
        [FromBody] RejectWorkflowActionRequest request,
        CancellationToken ct
    )
    {
        await workflowEngine.RejectActionAsync(id, tenantContext.CurrentUserId, request.ResponseJson, ct);
        return NoContent();
    }
}
