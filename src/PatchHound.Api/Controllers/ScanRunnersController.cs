using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/scan-runners")]
[Authorize(Policy = Policies.ManageAuthenticatedScans)]
public class ScanRunnersController(
    PatchHoundDbContext db,
    ITenantContext tenantContext) : ControllerBase
{
    public record ScanRunnerDto(
        Guid Id, Guid TenantId, string Name, string Description,
        DateTimeOffset? LastSeenAt, string Version, bool Enabled,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public record CreateScanRunnerRequest(Guid TenantId, string Name, string Description);
    public record CreateScanRunnerResponse(ScanRunnerDto Runner, string BearerSecret);
    public record UpdateScanRunnerRequest(string Name, string Description, bool Enabled);
    public record RotateSecretResponse(string BearerSecret);

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ScanRunnerDto>>> List(
        [FromQuery] Guid tenantId, [FromQuery] PaginationQuery pagination, CancellationToken ct)
    {
        if (!tenantContext.HasAccessToTenant(tenantId)) return Forbid();

        var query = db.ScanRunners.AsNoTracking().Where(r => r.TenantId == tenantId);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(r => r.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(r => new ScanRunnerDto(
                r.Id, r.TenantId, r.Name, r.Description,
                r.LastSeenAt, r.Version, r.Enabled,
                r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);
        return new PagedResponse<ScanRunnerDto>(items, total, pagination.Page, pagination.BoundedPageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScanRunnerDto>> Get(Guid id, CancellationToken ct)
    {
        var r = await db.ScanRunners.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(r.TenantId)) return Forbid();
        return new ScanRunnerDto(r.Id, r.TenantId, r.Name, r.Description,
            r.LastSeenAt, r.Version, r.Enabled, r.CreatedAt, r.UpdatedAt);
    }

    [HttpPost]
    public async Task<ActionResult<CreateScanRunnerResponse>> Create(
        [FromBody] CreateScanRunnerRequest req, CancellationToken ct)
    {
        if (!tenantContext.HasAccessToTenant(req.TenantId)) return Forbid();

        var (secret, hash) = GenerateBearerSecret();
        var runner = Core.Entities.AuthenticatedScans.ScanRunner.Create(
            req.TenantId, req.Name, req.Description, hash);

        db.ScanRunners.Add(runner);
        await db.SaveChangesAsync(ct);

        var dto = new ScanRunnerDto(runner.Id, runner.TenantId, runner.Name, runner.Description,
            runner.LastSeenAt, runner.Version, runner.Enabled, runner.CreatedAt, runner.UpdatedAt);
        return CreatedAtAction(nameof(Get), new { id = runner.Id }, new CreateScanRunnerResponse(dto, secret));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ScanRunnerDto>> Update(
        Guid id, [FromBody] UpdateScanRunnerRequest req, CancellationToken ct)
    {
        var runner = await db.ScanRunners.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (runner is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(runner.TenantId)) return Forbid();

        runner.Update(req.Name, req.Description, req.Enabled);
        await db.SaveChangesAsync(ct);

        return new ScanRunnerDto(runner.Id, runner.TenantId, runner.Name, runner.Description,
            runner.LastSeenAt, runner.Version, runner.Enabled, runner.CreatedAt, runner.UpdatedAt);
    }

    [HttpPost("{id:guid}/rotate-secret")]
    public async Task<ActionResult<RotateSecretResponse>> RotateSecret(Guid id, CancellationToken ct)
    {
        var runner = await db.ScanRunners.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (runner is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(runner.TenantId)) return Forbid();

        var (secret, hash) = GenerateBearerSecret();
        runner.RotateSecret(hash);
        await db.SaveChangesAsync(ct);

        return new RotateSecretResponse(secret);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var runner = await db.ScanRunners.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (runner is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(runner.TenantId)) return Forbid();

        db.ScanRunners.Remove(runner);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static (string Secret, string Hash) GenerateBearerSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
        return (secret, hash);
    }
}
