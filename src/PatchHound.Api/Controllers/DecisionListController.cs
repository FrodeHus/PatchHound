using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Decisions;
using PatchHound.Api.Services;
using PatchHound.Core.Interfaces;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/decisions")]
[Authorize]
public class DecisionListController(
    RemediationDecisionQueryService queryService,
    ITenantContext tenantContext
) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<RemediationDecisionListPageDto>> List(
        [FromQuery] RemediationDecisionFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var result = await queryService.ListAsync(tenantId, filter, pagination, ct);
        return Ok(result);
    }
}
