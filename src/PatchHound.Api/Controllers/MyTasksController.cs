using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.MyTasks;
using PatchHound.Api.Services;
using PatchHound.Core.Interfaces;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/my-tasks")]
[Authorize]
public class MyTasksController(
    MyTasksQueryService queryService,
    ITenantContext tenantContext
) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<MyTasksPageDto>> List(
        [FromQuery] MyTasksQuery query,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var userRoles = tenantContext.GetRolesForTenant(tenantId);
        return Ok(await queryService.ListAsync(tenantId, userRoles, query, ct));
    }
}
