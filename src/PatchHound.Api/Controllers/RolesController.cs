using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly AuditLogWriter _auditLogWriter;
    private readonly PatchHoundDbContext _dbContext;

    public RolesController(
        ITenantContext tenantContext,
        AuditLogWriter auditLogWriter,
        PatchHoundDbContext dbContext)
    {
        _tenantContext = tenantContext;
        _auditLogWriter = auditLogWriter;
        _dbContext = dbContext;
    }

    public class ActivateRequest
    {
        public string[] Roles { get; set; } = Array.Empty<string>();
    }

    public class ActivateResponse
    {
        public string[] Roles { get; set; } = Array.Empty<string>();
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate(
        [FromBody] ActivateRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest("No tenant selected.");
        }

        var assignedRoles = _tenantContext.GetRolesForTenant(tenantId);

        // Validate all requested role names are valid enum values
        var invalidRoles = request.Roles
            .Where(r => !Enum.TryParse<RoleName>(r, out _))
            .ToList();

        if (invalidRoles.Count > 0)
        {
            return BadRequest($"Invalid role name(s): {string.Join(", ", invalidRoles)}");
        }

        // Validate all requested roles are assigned to the user
        var unassignedRoles = request.Roles
            .Where(r => !assignedRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (unassignedRoles.Count > 0)
        {
            return Forbid();
        }

        // Compute diff for audit logging
        var previousHeader = Request.Headers["X-Active-Roles"].FirstOrDefault();
        var previousRoles = string.IsNullOrWhiteSpace(previousHeader)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                previousHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

        var newRoles = new HashSet<string>(request.Roles, StringComparer.OrdinalIgnoreCase);

        // Activated: in new but not in previous
        foreach (var role in newRoles.Except(previousRoles, StringComparer.OrdinalIgnoreCase))
        {
            await _auditLogWriter.WriteAsync(
                tenantId,
                "RoleActivation",
                _tenantContext.CurrentUserId,
                AuditAction.Activated,
                null,
                new { Role = role },
                ct);
        }

        // Deactivated: in previous but not in new
        foreach (var role in previousRoles.Except(newRoles, StringComparer.OrdinalIgnoreCase))
        {
            await _auditLogWriter.WriteAsync(
                tenantId,
                "RoleActivation",
                _tenantContext.CurrentUserId,
                AuditAction.Deactivated,
                new { Role = role },
                null,
                ct);
        }

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new ActivateResponse { Roles = request.Roles });
    }
}
