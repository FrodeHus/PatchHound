using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Audit;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/audit-log")]
[Authorize]
public class AuditLogController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public AuditLogController(PatchHoundDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewAuditLogs)]
    public async Task<ActionResult<PagedResponse<AuditLogDto>>> List(
        [FromQuery] AuditLogFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.AuditLogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(filter.EntityType))
            query = query.Where(e => e.EntityType == filter.EntityType);
        if (filter.EntityId.HasValue)
            query = query.Where(e => e.EntityId == filter.EntityId.Value);
        if (filter.Action.HasValue)
            query = query.Where(e => e.Action == filter.Action.Value);
        if (filter.UserId.HasValue)
            query = query.Where(e => e.UserId == filter.UserId.Value);
        if (filter.TenantId.HasValue)
        {
            if (!_tenantContext.HasAccessToTenant(filter.TenantId.Value))
                return Forbid();
            query = query.Where(e => e.TenantId == filter.TenantId.Value);
        }
        if (filter.FromDate.HasValue)
            query = query.Where(e => e.Timestamp >= filter.FromDate.Value);
        if (filter.ToDate.HasValue)
            query = query.Where(e => e.Timestamp <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(e => new AuditLogDto(
                e.Id,
                e.TenantId,
                e.EntityType,
                e.EntityId,
                e.Action,
                e.OldValues,
                e.NewValues,
                e.UserId,
                e.Timestamp
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<AuditLogDto>(items, totalCount));
    }
}
