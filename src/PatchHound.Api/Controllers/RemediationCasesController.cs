using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Remediation;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/remediation/cases")]
[Authorize]
public class RemediationCasesController(
    PatchHoundDbContext db,
    RemediationCaseService caseService,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<RemediationCaseDto>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var query = db.RemediationCases.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.UpdatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip(pagination.Skip).Take(pagination.BoundedPageSize)
            .Select(c => new RemediationCaseDto(
                c.Id, c.TenantId, c.SoftwareProductId,
                c.SoftwareProduct.Name, c.SoftwareProduct.Vendor,
                c.Status.ToString(), c.CreatedAt, c.UpdatedAt, c.ClosedAt,
                db.InstalledSoftware.Count(i =>
                    i.TenantId == tenantId && i.SoftwareProductId == c.SoftwareProductId),
                db.DeviceVulnerabilityExposures.Count(e =>
                    e.TenantId == tenantId
                    && e.SoftwareProductId == c.SoftwareProductId
                    && e.Status == ExposureStatus.Open)))
            .ToListAsync(ct);

        return Ok(new PagedResponse<RemediationCaseDto>(items, total, pagination.Page, pagination.BoundedPageSize));
    }

    [HttpGet("{caseId:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<RemediationCaseDto>> Get(Guid caseId, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var c = await db.RemediationCases.AsNoTracking()
            .Include(x => x.SoftwareProduct)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == caseId, ct);
        if (c is null) return NotFound();

        return Ok(new RemediationCaseDto(
            c.Id, c.TenantId, c.SoftwareProductId,
            c.SoftwareProduct.Name, c.SoftwareProduct.Vendor,
            c.Status.ToString(), c.CreatedAt, c.UpdatedAt, c.ClosedAt,
            await db.InstalledSoftware.CountAsync(i =>
                i.TenantId == tenantId && i.SoftwareProductId == c.SoftwareProductId, ct),
            await db.DeviceVulnerabilityExposures.CountAsync(e =>
                e.TenantId == tenantId
                && e.SoftwareProductId == c.SoftwareProductId
                && e.Status == ExposureStatus.Open, ct)));
    }

    public record CreateCaseRequest(Guid SoftwareProductId);

    [HttpPost]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<RemediationCaseDto>> GetOrCreate(
        [FromBody] CreateCaseRequest req, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        try
        {
            var c = await caseService.GetOrCreateAsync(tenantId, req.SoftwareProductId, ct);
            return await Get(c.Id, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
    }
}
