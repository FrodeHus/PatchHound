using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.ApprovalTasks;
using PatchHound.Api.Services;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/approval-tasks")]
[Authorize]
public class ApprovalTasksController(
    ApprovalTaskQueryService queryService,
    ApprovalTaskService approvalTaskService,
    ITenantContext tenantContext
) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ViewApprovalTasks)]
    public async Task<ActionResult<PagedResponse<ApprovalTaskListItemDto>>> List(
        [FromQuery] ApprovalTaskFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var userRoles = tenantContext.GetRolesForTenant(tenantId);
        var result = await queryService.ListAsync(tenantId, userRoles, filter, pagination, ct);
        return Ok(result);
    }

    [HttpGet("pending-count")]
    [Authorize(Policy = Policies.ViewApprovalTasks)]
    public async Task<ActionResult<int>> GetPendingCount(CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var userRoles = tenantContext.GetRolesForTenant(tenantId);
        var count = await queryService.GetPendingCountAsync(tenantId, userRoles, ct);
        return Ok(count);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewApprovalTasks)]
    public async Task<ActionResult<ApprovalTaskDetailDto>> GetDetail(
        Guid id,
        [FromQuery] PaginationQuery vulnPagination,
        [FromQuery(Name = "devicePage")] int devicePage = 1,
        [FromQuery(Name = "devicePageSize")] int devicePageSize = 25,
        CancellationToken ct = default
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var userRoles = tenantContext.GetRolesForTenant(tenantId);
        var devicePagination = new PaginationQuery(devicePage, devicePageSize);
        var result = await queryService.GetDetailAsync(tenantId, id, userRoles, vulnPagination, devicePagination, ct);

        if (result is null)
            return NotFound();

        return Ok(result);
    }

    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = Policies.ResolveApprovalTask)]
    public async Task<IActionResult> Resolve(
        Guid id,
        [FromBody] ResolveApprovalTaskRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var userId = tenantContext.CurrentUserId;

        try
        {
            var result = request.Action?.ToLowerInvariant() switch
            {
                "approve" => await approvalTaskService.ApproveAsync(id, userId, request.Justification, ct),
                "deny" => await approvalTaskService.DenyAsync(id, userId, request.Justification, ct),
                _ => null,
            };

            if (result is null)
                return BadRequest(new ProblemDetails { Title = "Action must be 'approve' or 'deny'." });

            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
    }

    [HttpPost("{id:guid}/read")]
    [Authorize(Policy = Policies.ViewApprovalTasks)]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct)
    {
        try
        {
            await approvalTaskService.MarkAsReadAsync(id, ct);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ProblemDetails { Title = ex.Message });
        }
    }
}
