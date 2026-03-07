using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Admin;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;

    public TenantsController(PatchHoundDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<ActionResult<PagedResponse<TenantDto>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.Tenants.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.Name)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(t => new TenantDto(t.Id, t.Name, t.EntraTenantId, t.Settings))
            .ToListAsync(ct);

        return Ok(new PagedResponse<TenantDto>(items, totalCount));
    }

    [HttpPut("{id:guid}/settings")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> UpdateSettings(
        Guid id,
        [FromBody] UpdateTenantSettingsRequest request,
        CancellationToken ct
    )
    {
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        tenant.UpdateSettings(request.Settings);
        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }
}
