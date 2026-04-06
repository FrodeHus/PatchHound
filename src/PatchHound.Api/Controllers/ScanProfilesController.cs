using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/scan-profiles")]
[Authorize(Policy = Policies.ManageAuthenticatedScans)]
public class ScanProfilesController(
    PatchHoundDbContext db,
    ITenantContext tenantContext,
    ScanJobDispatcher dispatcher) : ControllerBase
{
    public record ScanProfileDto(
        Guid Id, Guid TenantId, string Name, string Description,
        string CronSchedule, Guid ConnectionProfileId, Guid ScanRunnerId,
        bool Enabled, DateTimeOffset? ManualRequestedAt,
        DateTimeOffset? LastRunStartedAt,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
        List<Guid> ToolIds);

    public record CreateScanProfileRequest(
        Guid TenantId, string Name, string Description,
        string CronSchedule, Guid ConnectionProfileId, Guid ScanRunnerId,
        bool Enabled, List<Guid>? ToolIds);

    public record UpdateScanProfileRequest(
        string Name, string Description, string CronSchedule,
        Guid ConnectionProfileId, Guid ScanRunnerId,
        bool Enabled, List<Guid>? ToolIds);

    public record TriggerRunRequest(string TriggerKind = "manual");

    public record TriggerRunResponse(Guid RunId);

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ScanProfileDto>>> List(
        [FromQuery] Guid tenantId, [FromQuery] PaginationQuery pagination, CancellationToken ct)
    {
        if (!tenantContext.HasAccessToTenant(tenantId)) return Forbid();

        var query = db.ScanProfiles.AsNoTracking().Where(p => p.TenantId == tenantId);
        var total = await query.CountAsync(ct);
        var profiles = await query
            .OrderBy(p => p.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var profileIds = profiles.Select(p => p.Id).ToList();
        var toolAssignments = await db.ScanProfileTools.AsNoTracking()
            .Where(t => profileIds.Contains(t.ScanProfileId))
            .OrderBy(t => t.ExecutionOrder)
            .ToListAsync(ct);
        var toolsByProfile = toolAssignments
            .GroupBy(t => t.ScanProfileId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.ScanningToolId).ToList());

        var items = profiles.Select(p => new ScanProfileDto(
            p.Id, p.TenantId, p.Name, p.Description,
            p.CronSchedule, p.ConnectionProfileId, p.ScanRunnerId,
            p.Enabled, p.ManualRequestedAt, p.LastRunStartedAt,
            p.CreatedAt, p.UpdatedAt,
            toolsByProfile.GetValueOrDefault(p.Id, []))).ToList();

        return new PagedResponse<ScanProfileDto>(items, total, pagination.Page, pagination.BoundedPageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScanProfileDto>> Get(Guid id, CancellationToken ct)
    {
        var p = await db.ScanProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(p.TenantId)) return Forbid();

        var toolIds = await db.ScanProfileTools.AsNoTracking()
            .Where(t => t.ScanProfileId == id)
            .OrderBy(t => t.ExecutionOrder)
            .Select(t => t.ScanningToolId)
            .ToListAsync(ct);

        return new ScanProfileDto(p.Id, p.TenantId, p.Name, p.Description,
            p.CronSchedule, p.ConnectionProfileId, p.ScanRunnerId,
            p.Enabled, p.ManualRequestedAt, p.LastRunStartedAt,
            p.CreatedAt, p.UpdatedAt, toolIds);
    }

    [HttpPost]
    public async Task<ActionResult<ScanProfileDto>> Create(
        [FromBody] CreateScanProfileRequest req, CancellationToken ct)
    {
        if (!tenantContext.HasAccessToTenant(req.TenantId)) return Forbid();

        var profile = ScanProfile.Create(
            req.TenantId, req.Name, req.Description,
            req.CronSchedule, req.ConnectionProfileId, req.ScanRunnerId, req.Enabled);
        db.ScanProfiles.Add(profile);

        var toolIds = await SyncToolAssignments(profile.Id, req.ToolIds, ct);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = profile.Id },
            new ScanProfileDto(profile.Id, profile.TenantId, profile.Name, profile.Description,
                profile.CronSchedule, profile.ConnectionProfileId, profile.ScanRunnerId,
                profile.Enabled, profile.ManualRequestedAt, profile.LastRunStartedAt,
                profile.CreatedAt, profile.UpdatedAt, toolIds));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ScanProfileDto>> Update(
        Guid id, [FromBody] UpdateScanProfileRequest req, CancellationToken ct)
    {
        var profile = await db.ScanProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(profile.TenantId)) return Forbid();

        profile.Update(req.Name, req.Description, req.CronSchedule,
            req.ConnectionProfileId, req.ScanRunnerId, req.Enabled);

        var toolIds = await SyncToolAssignments(profile.Id, req.ToolIds, ct);
        await db.SaveChangesAsync(ct);

        return new ScanProfileDto(profile.Id, profile.TenantId, profile.Name, profile.Description,
            profile.CronSchedule, profile.ConnectionProfileId, profile.ScanRunnerId,
            profile.Enabled, profile.ManualRequestedAt, profile.LastRunStartedAt,
            profile.CreatedAt, profile.UpdatedAt, toolIds);
    }

    [HttpPost("{id:guid}/trigger")]
    public async Task<ActionResult<TriggerRunResponse>> Trigger(
        Guid id, [FromBody] TriggerRunRequest req, CancellationToken ct)
    {
        var profile = await db.ScanProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(profile.TenantId)) return Forbid();

        var runId = await dispatcher.StartRunAsync(id, req.TriggerKind, tenantContext.CurrentUserId, ct);
        return new TriggerRunResponse(runId);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var profile = await db.ScanProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(profile.TenantId)) return Forbid();

        var tools = await db.ScanProfileTools.Where(t => t.ScanProfileId == id).ToListAsync(ct);
        db.ScanProfileTools.RemoveRange(tools);
        var assignments = await db.AssetScanProfileAssignments.Where(a => a.ScanProfileId == id).ToListAsync(ct);
        db.AssetScanProfileAssignments.RemoveRange(assignments);
        db.ScanProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<List<Guid>> SyncToolAssignments(Guid profileId, List<Guid>? toolIds, CancellationToken ct)
    {
        if (toolIds is null) return [];

        var existing = await db.ScanProfileTools.Where(t => t.ScanProfileId == profileId).ToListAsync(ct);
        db.ScanProfileTools.RemoveRange(existing);

        for (var i = 0; i < toolIds.Count; i++)
        {
            db.ScanProfileTools.Add(ScanProfileTool.Create(profileId, toolIds[i], i));
        }

        return toolIds;
    }
}
