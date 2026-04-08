using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/scanning-tools")]
[Authorize(Policy = Policies.ManageAuthenticatedScans)]
public class ScanningToolsController(
    PatchHoundDbContext db,
    ITenantContext tenantContext,
    ScanningToolVersionStore versionStore) : ControllerBase
{
    public record ScanningToolDto(
        Guid Id, Guid TenantId, string Name, string Description,
        string ScriptType, string InterpreterPath, int TimeoutSeconds,
        string OutputModel, Guid? CurrentVersionId,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public record ScanningToolVersionDto(
        Guid Id, Guid ScanningToolId, int VersionNumber,
        string ScriptContent, Guid EditedByUserId, DateTimeOffset EditedAt);

    public record CreateScanningToolRequest(
        Guid TenantId, string Name, string Description,
        string ScriptType, string InterpreterPath, int TimeoutSeconds,
        string InitialScript);

    public record UpdateScanningToolRequest(
        string Name, string Description, string ScriptType,
        string InterpreterPath, int TimeoutSeconds);

    public record PublishVersionRequest(string ScriptContent);

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ScanningToolDto>>> List(
        [FromQuery] Guid? tenantId, [FromQuery] PaginationQuery pagination, CancellationToken ct)
    {
        var effectiveTenantId = tenantId ?? tenantContext.CurrentTenantId;
        if (effectiveTenantId is null || !tenantContext.HasAccessToTenant(effectiveTenantId.Value)) return Forbid();

        var query = db.ScanningTools.AsNoTracking().Where(t => t.TenantId == effectiveTenantId.Value);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(t => t.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(t => new ScanningToolDto(
                t.Id, t.TenantId, t.Name, t.Description,
                t.ScriptType, t.InterpreterPath, t.TimeoutSeconds,
                t.OutputModel, t.CurrentVersionId,
                t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct);
        return new PagedResponse<ScanningToolDto>(items, total, pagination.Page, pagination.BoundedPageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScanningToolDto>> Get(Guid id, CancellationToken ct)
    {
        var t = await db.ScanningTools.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(t.TenantId)) return Forbid();
        return new ScanningToolDto(t.Id, t.TenantId, t.Name, t.Description,
            t.ScriptType, t.InterpreterPath, t.TimeoutSeconds,
            t.OutputModel, t.CurrentVersionId, t.CreatedAt, t.UpdatedAt);
    }

    [HttpPost]
    public async Task<ActionResult<ScanningToolDto>> Create(
        [FromBody] CreateScanningToolRequest req, CancellationToken ct)
    {
        var effectiveTenantId = req.TenantId != Guid.Empty ? req.TenantId : tenantContext.CurrentTenantId;
        if (effectiveTenantId is null || !tenantContext.HasAccessToTenant(effectiveTenantId.Value)) return Forbid();

        var tool = Core.Entities.AuthenticatedScans.ScanningTool.Create(
            effectiveTenantId.Value, req.Name, req.Description,
            req.ScriptType, req.InterpreterPath, req.TimeoutSeconds,
            "NormalizedSoftware");
        db.ScanningTools.Add(tool);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(req.InitialScript))
        {
            await versionStore.PublishNewVersionAsync(
                tool.Id, req.InitialScript, tenantContext.CurrentUserId, ct);
        }

        await db.Entry(tool).ReloadAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = tool.Id },
            new ScanningToolDto(tool.Id, tool.TenantId, tool.Name, tool.Description,
                tool.ScriptType, tool.InterpreterPath, tool.TimeoutSeconds,
                tool.OutputModel, tool.CurrentVersionId, tool.CreatedAt, tool.UpdatedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ScanningToolDto>> Update(
        Guid id, [FromBody] UpdateScanningToolRequest req, CancellationToken ct)
    {
        var tool = await db.ScanningTools.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tool is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(tool.TenantId)) return Forbid();

        tool.UpdateMetadata(req.Name, req.Description, req.ScriptType, req.InterpreterPath, req.TimeoutSeconds);
        await db.SaveChangesAsync(ct);

        return new ScanningToolDto(tool.Id, tool.TenantId, tool.Name, tool.Description,
            tool.ScriptType, tool.InterpreterPath, tool.TimeoutSeconds,
            tool.OutputModel, tool.CurrentVersionId, tool.CreatedAt, tool.UpdatedAt);
    }

    [HttpPost("{id:guid}/versions")]
    public async Task<ActionResult<ScanningToolVersionDto>> PublishVersion(
        Guid id, [FromBody] PublishVersionRequest req, CancellationToken ct)
    {
        var tool = await db.ScanningTools.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tool is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(tool.TenantId)) return Forbid();

        var version = await versionStore.PublishNewVersionAsync(
            id, req.ScriptContent, tenantContext.CurrentUserId, ct);

        return new ScanningToolVersionDto(
            version.Id, version.ScanningToolId, version.VersionNumber,
            version.ScriptContent, version.EditedByUserId, version.EditedAt);
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<List<ScanningToolVersionDto>>> ListVersions(Guid id, CancellationToken ct)
    {
        var tool = await db.ScanningTools.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tool is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(tool.TenantId)) return Forbid();

        var versions = await db.ScanningToolVersions.AsNoTracking()
            .Where(v => v.ScanningToolId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new ScanningToolVersionDto(
                v.Id, v.ScanningToolId, v.VersionNumber,
                v.ScriptContent, v.EditedByUserId, v.EditedAt))
            .ToListAsync(ct);
        return versions;
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tool = await db.ScanningTools.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tool is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(tool.TenantId)) return Forbid();

        var versions = await db.ScanningToolVersions.Where(v => v.ScanningToolId == id).ToListAsync(ct);
        db.ScanningToolVersions.RemoveRange(versions);
        db.ScanningTools.Remove(tool);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
