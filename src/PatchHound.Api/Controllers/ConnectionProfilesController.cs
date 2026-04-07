using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement.Mvc;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Core.Common;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/connection-profiles")]
[Authorize(Policy = Policies.ManageAuthenticatedScans)]
[FeatureGate(FeatureFlags.AuthenticatedScans)]
public class ConnectionProfilesController(
    PatchHoundDbContext db,
    ITenantContext tenantContext,
    ConnectionProfileSecretWriter secretWriter) : ControllerBase
{
    public record ConnectionProfileDto(
        Guid Id, Guid TenantId, string Name, string Description,
        string SshHost, int SshPort, string SshUsername,
        string AuthMethod, string? HostKeyFingerprint,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public record CreateConnectionProfileRequest(
        Guid TenantId, string Name, string Description,
        string SshHost, int SshPort, string SshUsername,
        string AuthMethod, string? Password, string? PrivateKey,
        string? Passphrase, string? HostKeyFingerprint);

    public record UpdateConnectionProfileRequest(
        string Name, string Description, string SshHost,
        int SshPort, string SshUsername, string AuthMethod,
        string? Password, string? PrivateKey, string? Passphrase,
        string? HostKeyFingerprint);

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ConnectionProfileDto>>> List(
        [FromQuery] Guid tenantId, [FromQuery] PaginationQuery pagination, CancellationToken ct)
    {
        if (!tenantContext.HasAccessToTenant(tenantId)) return Forbid();

        var query = db.ConnectionProfiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(p => new ConnectionProfileDto(
                p.Id, p.TenantId, p.Name, p.Description,
                p.SshHost, p.SshPort, p.SshUsername,
                p.AuthMethod, p.HostKeyFingerprint,
                p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResponse<ConnectionProfileDto>(items, total, pagination.Page, pagination.BoundedPageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConnectionProfileDto>> Get(Guid id, CancellationToken ct)
    {
        var p = await db.ConnectionProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(p.TenantId)) return Forbid();
        return new ConnectionProfileDto(
            p.Id, p.TenantId, p.Name, p.Description,
            p.SshHost, p.SshPort, p.SshUsername,
            p.AuthMethod, p.HostKeyFingerprint,
            p.CreatedAt, p.UpdatedAt);
    }

    [HttpPost]
    public async Task<ActionResult<ConnectionProfileDto>> Create(
        [FromBody] CreateConnectionProfileRequest req, CancellationToken ct)
    {
        if (!tenantContext.HasAccessToTenant(req.TenantId)) return Forbid();

        var profile = Core.Entities.AuthenticatedScans.ConnectionProfile.Create(
            req.TenantId, req.Name, req.Description,
            req.SshHost, req.SshPort, req.SshUsername,
            req.AuthMethod, "", req.HostKeyFingerprint);

        db.ConnectionProfiles.Add(profile);
        await db.SaveChangesAsync(ct);

        var secretRef = await WriteSecretAsync(req.TenantId, profile.Id, req.AuthMethod,
            req.Password, req.PrivateKey, req.Passphrase, ct);
        profile.SetSecretRef(secretRef);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = profile.Id },
            new ConnectionProfileDto(
                profile.Id, profile.TenantId, profile.Name, profile.Description,
                profile.SshHost, profile.SshPort, profile.SshUsername,
                profile.AuthMethod, profile.HostKeyFingerprint,
                profile.CreatedAt, profile.UpdatedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ConnectionProfileDto>> Update(
        Guid id, [FromBody] UpdateConnectionProfileRequest req, CancellationToken ct)
    {
        var profile = await db.ConnectionProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(profile.TenantId)) return Forbid();

        profile.Update(req.Name, req.Description, req.SshHost, req.SshPort,
            req.SshUsername, req.AuthMethod, req.HostKeyFingerprint);

        if (!string.IsNullOrEmpty(req.Password) || !string.IsNullOrEmpty(req.PrivateKey))
        {
            var secretRef = await WriteSecretAsync(profile.TenantId, profile.Id,
                req.AuthMethod, req.Password, req.PrivateKey, req.Passphrase, ct);
            profile.SetSecretRef(secretRef);
        }

        await db.SaveChangesAsync(ct);
        return new ConnectionProfileDto(
            profile.Id, profile.TenantId, profile.Name, profile.Description,
            profile.SshHost, profile.SshPort, profile.SshUsername,
            profile.AuthMethod, profile.HostKeyFingerprint,
            profile.CreatedAt, profile.UpdatedAt);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var profile = await db.ConnectionProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(profile.TenantId)) return Forbid();

        await secretWriter.DeleteAsync(profile.TenantId, profile.Id, ct);
        db.ConnectionProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<string> WriteSecretAsync(
        Guid tenantId, Guid profileId, string authMethod,
        string? password, string? privateKey, string? passphrase, CancellationToken ct)
    {
        return authMethod switch
        {
            "password" when !string.IsNullOrEmpty(password) =>
                await secretWriter.WritePasswordAsync(tenantId, profileId, password, ct),
            "privateKey" when !string.IsNullOrEmpty(privateKey) =>
                await secretWriter.WritePrivateKeyAsync(tenantId, profileId, privateKey, passphrase, ct),
            _ => ""
        };
    }
}
