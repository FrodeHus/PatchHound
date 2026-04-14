using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Remediation;
using PatchHound.Api.Services;
using PatchHound.Core.Interfaces;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/remediation/tasks")]
[Authorize]
public class RemediationTasksController(
    RemediationTaskQueryService remediationTaskQueryService,
    ITenantContext tenantContext
) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<RemediationTaskListItemDto>>> List(
        [FromQuery] RemediationTaskFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var result = await remediationTaskQueryService.ListOpenTasksAsync(
            tenantId,
            filter,
            pagination,
            ct
        );

        return Ok(result);
    }

    [HttpGet("cases/{caseId:guid}/team-statuses")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<List<RemediationTaskTeamStatusDto>>> GetTeamStatusesForSoftware(
        Guid caseId,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var result = await remediationTaskQueryService.ListTeamStatusesForSoftwareAsync(
            tenantId,
            caseId,
            ct
        );

        return Ok(result);
    }

    [HttpPost("cases/{caseId:guid}")]
    [Authorize(Policy = Policies.AssignTasks)]
    public async Task<ActionResult<RemediationTaskCreateResultDto>> CreateForSoftware(
        Guid caseId,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        if (tenantContext.CurrentUserId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails { Title = "No current user is available." });
        }

        var result = await remediationTaskQueryService.CreateMissingTasksForSoftwareAsync(
            tenantId,
            caseId,
            tenantContext.CurrentUserId,
            ct
        );

        return Ok(result);
    }
}
