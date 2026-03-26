using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/maintenance")]
[Authorize(Policy = Policies.PerformMaintenance)]
public class MaintenanceController(
    PatchHoundDbContext dbContext,
    ITenantContext tenantContext
) : ControllerBase
{
    [HttpPost("revoke-all-remediations")]
    public async Task<IActionResult> RevokeAllRemediations(CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        await dbContext
            .RemediationWorkflowStageRecords.IgnoreQueryFilters()
            .Where(r =>
                dbContext
                    .RemediationWorkflows.IgnoreQueryFilters()
                    .Where(w => w.TenantId == tenantId)
                    .Select(w => w.Id)
                    .Contains(r.RemediationWorkflowId)
            )
            .ExecuteDeleteAsync(ct);

        await dbContext
            .ApprovalTasks.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);

        await dbContext
            .PatchingTasks.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);

        await dbContext
            .AnalystRecommendations.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);

        await dbContext
            .RemediationDecisionVulnerabilityOverrides.IgnoreQueryFilters()
            .Where(o =>
                dbContext
                    .RemediationDecisions.IgnoreQueryFilters()
                    .Where(d => d.TenantId == tenantId)
                    .Select(d => d.Id)
                    .Contains(o.RemediationDecisionId)
            )
            .ExecuteDeleteAsync(ct);

        await dbContext
            .RemediationDecisions.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);

        await dbContext
            .RemediationWorkflows.IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);

        return NoContent();
    }
}
