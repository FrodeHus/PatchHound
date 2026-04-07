using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/authenticated-scan-runs")]
[Authorize(Policy = Policies.ManageAuthenticatedScans)]
public class AuthenticatedScanRunsController(
    PatchHoundDbContext db,
    ITenantContext tenantContext) : ControllerBase
{
    public record ScanRunListDto(
        Guid Id, Guid ScanProfileId, string ProfileName,
        string TriggerKind, Guid? TriggeredByUserId,
        DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        string Status, int TotalDevices,
        int SucceededCount, int FailedCount, int EntriesIngested);

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ScanRunListDto>>> List(
        [FromQuery] Guid tenantId,
        [FromQuery] Guid? profileId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (!tenantContext.HasAccessToTenant(tenantId)) return Forbid();

        var query = db.AuthenticatedScanRuns.AsNoTracking()
            .Where(r => r.TenantId == tenantId);

        if (profileId.HasValue)
            query = query.Where(r => r.ScanProfileId == profileId.Value);

        var total = await query.CountAsync(ct);

        var runs = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var profileIds = runs.Select(r => r.ScanProfileId).Distinct().ToList();
        var profileNames = await db.ScanProfiles.AsNoTracking()
            .Where(p => profileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var items = runs.Select(r => new ScanRunListDto(
            r.Id, r.ScanProfileId,
            profileNames.GetValueOrDefault(r.ScanProfileId, "\u2014"),
            r.TriggerKind, r.TriggeredByUserId,
            r.StartedAt, r.CompletedAt, r.Status,
            r.TotalDevices, r.SucceededCount, r.FailedCount,
            r.EntriesIngested)).ToList();

        return new PagedResponse<ScanRunListDto>(items, total, pagination.Page, pagination.BoundedPageSize);
    }
}
