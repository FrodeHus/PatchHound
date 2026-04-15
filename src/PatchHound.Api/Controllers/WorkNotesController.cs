using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.WorkNotes;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/work-notes")]
[Authorize]
public class WorkNotesController : ControllerBase
{
    private sealed record EntityResolution(string InternalEntityType, Guid TenantId);

    private static readonly Dictionary<string, string> EntityTypeMap = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["vulnerabilities"] = "TenantVulnerability",
        ["software"] = "TenantSoftware",
        ["remediations"] = "TenantSoftwareRemediation",
        ["assets"] = nameof(Asset),
        ["devices"] = nameof(Asset),
    };

    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public WorkNotesController(PatchHoundDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet("{entityType}/{entityId:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<List<WorkNoteDto>>> List(
        string entityType,
        Guid entityId,
        CancellationToken ct
    )
    {
        var resolution = await ResolveEntityAsync(entityType, entityId, ct);
        if (resolution is null)
            return BadRequest(new ProblemDetails { Title = "Invalid or unknown entity type" });

        if (!_tenantContext.HasAccessToTenant(resolution.TenantId))
            return Forbid();

        var currentUserId = _tenantContext.CurrentUserId;

        var notes = await _dbContext
            .Comments.AsNoTracking()
            .Where(c =>
                c.EntityType == resolution.InternalEntityType
                && c.EntityId == entityId
                && c.DeletedAt == null
            )
            .OrderBy(c => c.CreatedAt)
            .Select(c => new WorkNoteDto(
                c.Id,
                c.EntityType,
                c.EntityId,
                c.AuthorId,
                _dbContext.Users
                    .Where(u => u.Id == c.AuthorId)
                    .Select(u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName)
                    .FirstOrDefault() ?? "Unknown",
                c.Content,
                c.CreatedAt,
                c.UpdatedAt,
                c.AuthorId == currentUserId,
                c.AuthorId == currentUserId
            ))
            .ToListAsync(ct);

        return Ok(notes);
    }

    [HttpPost("{entityType}/{entityId:guid}")]
    [Authorize(Policy = Policies.AddComments)]
    public async Task<ActionResult<WorkNoteDto>> Create(
        string entityType,
        Guid entityId,
        [FromBody] CreateWorkNoteRequest request,
        CancellationToken ct
    )
    {
        var resolution = await ResolveEntityAsync(entityType, entityId, ct);
        if (resolution is null)
            return BadRequest(new ProblemDetails { Title = "Invalid or unknown entity type" });

        if (!_tenantContext.HasAccessToTenant(resolution.TenantId))
            return Forbid();

        var content = request.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return BadRequest(new ProblemDetails { Title = "Work note content is required." });

        var note = Comment.Create(
            resolution.TenantId,
            resolution.InternalEntityType,
            entityId,
            _tenantContext.CurrentUserId,
            content
        );

        _dbContext.Comments.Add(note);
        await _dbContext.SaveChangesAsync(ct);

        var authorDisplayName = await ResolveAuthorDisplayNameAsync(note.AuthorId, ct);
        var dto = new WorkNoteDto(
            note.Id,
            note.EntityType,
            note.EntityId,
            note.AuthorId,
            authorDisplayName,
            note.Content,
            note.CreatedAt,
            note.UpdatedAt,
            true,
            true
        );

        return Created($"/api/work-notes/{entityType}/{entityId}", dto);
    }

    [HttpPut("{noteId:guid}")]
    [Authorize(Policy = Policies.AddComments)]
    public async Task<ActionResult<WorkNoteDto>> Update(
        Guid noteId,
        [FromBody] UpdateWorkNoteRequest request,
        CancellationToken ct
    )
    {
        var note = await _dbContext.Comments.FirstOrDefaultAsync(c => c.Id == noteId, ct);
        if (note is null || note.DeletedAt != null)
            return NotFound(new ProblemDetails { Title = "Work note not found" });

        if (!_tenantContext.HasAccessToTenant(note.TenantId))
            return Forbid();

        if (note.AuthorId != _tenantContext.CurrentUserId)
            return Forbid();

        var content = request.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return BadRequest(new ProblemDetails { Title = "Work note content is required." });

        note.UpdateContent(content);
        await _dbContext.SaveChangesAsync(ct);

        var authorDisplayName = await ResolveAuthorDisplayNameAsync(note.AuthorId, ct);
        return Ok(new WorkNoteDto(
            note.Id,
            note.EntityType,
            note.EntityId,
            note.AuthorId,
            authorDisplayName,
            note.Content,
            note.CreatedAt,
            note.UpdatedAt,
            true,
            true
        ));
    }

    [HttpDelete("{noteId:guid}")]
    [Authorize(Policy = Policies.AddComments)]
    public async Task<IActionResult> Delete(Guid noteId, CancellationToken ct)
    {
        var note = await _dbContext.Comments.FirstOrDefaultAsync(c => c.Id == noteId, ct);
        if (note is null || note.DeletedAt != null)
            return NotFound(new ProblemDetails { Title = "Work note not found" });

        if (!_tenantContext.HasAccessToTenant(note.TenantId))
            return Forbid();

        if (note.AuthorId != _tenantContext.CurrentUserId)
            return Forbid();

        note.Delete();
        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<EntityResolution?> ResolveEntityAsync(string entityType, Guid entityId, CancellationToken ct)
    {
        if (!EntityTypeMap.TryGetValue(entityType, out var internalEntityType))
            return null;

        Guid? tenantId = internalEntityType switch
        {
            // Canonical Vulnerability is a global entity — scope to current tenant context.
            "TenantVulnerability" => await _dbContext.Vulnerabilities
                .IgnoreQueryFilters()
                .Where(v => v.Id == entityId)
                .Select(_ => (Guid?)_tenantContext.CurrentTenantId)
                .FirstOrDefaultAsync(ct),
            "TenantSoftware" or "TenantSoftwareRemediation" => await _dbContext.SoftwareTenantRecords
                .IgnoreQueryFilters()
                .Where(s => s.Id == entityId)
                .Select(s => (Guid?)s.TenantId)
                .FirstOrDefaultAsync(ct),
            nameof(Asset) => await _dbContext.Assets
                .IgnoreQueryFilters()
                .Where(a => a.Id == entityId)
                .Select(a => (Guid?)a.TenantId)
                .FirstOrDefaultAsync(ct),
            _ => null,
        };

        return tenantId is null ? null : new EntityResolution(internalEntityType, tenantId.Value);
    }

    private async Task<string> ResolveAuthorDisplayNameAsync(Guid authorId, CancellationToken ct)
    {
        return await _dbContext.Users
            .Where(u => u.Id == authorId)
            .Select(u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName)
            .FirstOrDefaultAsync(ct)
            ?? "Unknown";
    }
}
