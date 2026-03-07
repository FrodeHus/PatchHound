using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Api.Models.Setup;
using PatchHound.Core.Common;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using System.Security.Claims;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/setup")]
public class SetupController : ControllerBase
{
    private readonly ISetupService _setupService;

    public SetupController(ISetupService setupService)
    {
        _setupService = setupService;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<SetupStatusDto>> GetStatus(CancellationToken ct)
    {
        var isInitialized = await _setupService.IsInitializedAsync(ct);
        return Ok(new SetupStatusDto(isInitialized));
    }

    [HttpPost("complete")]
    [Authorize]
    public async Task<IActionResult> Complete(
        [FromBody] SetupCompleteRequest request,
        CancellationToken ct
    )
    {
        if (await _setupService.IsInitializedAsync(ct))
        {
            return Conflict(new ProblemDetails { Title = "Application is already initialized." });
        }

        if (!HasRequiredSetupRole(User))
        {
            return Forbid();
        }

        var setupIdentity = ResolveSetupIdentity(User);
        if (!setupIdentity.IsSuccess)
        {
            return BadRequest(new ProblemDetails { Title = setupIdentity.Error });
        }

        var result = await _setupService.CompleteSetupAsync(
            new SetupRequest(
                request.TenantName,
                setupIdentity.Value.EntraTenantId,
                setupIdentity.Value.AdminEmail,
                setupIdentity.Value.AdminDisplayName,
                setupIdentity.Value.AdminEntraObjectId
            ),
            ct
        );

        if (!result.IsSuccess)
        {
            return BadRequest(new ProblemDetails { Title = result.Error });
        }

        return Ok();
    }

    private static bool HasRequiredSetupRole(ClaimsPrincipal user)
    {
        return user.Claims.Any(claim =>
            (
                claim.Type == "roles"
                || claim.Type == ClaimTypes.Role
                || claim.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
            )
            && string.Equals(claim.Value, "Tenant.Admin", StringComparison.Ordinal)
        );
    }

    private static Result<SetupIdentity> ResolveSetupIdentity(ClaimsPrincipal user)
    {
        var entraTenantId =
            user.FindFirstValue("tid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");

        if (string.IsNullOrWhiteSpace(entraTenantId))
        {
            return Result<SetupIdentity>.Failure("Authenticated tenant ID claim is missing.");
        }

        var adminEntraObjectId =
            user.FindFirstValue("oid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");

        if (string.IsNullOrWhiteSpace(adminEntraObjectId))
        {
            return Result<SetupIdentity>.Failure("Authenticated user object ID claim is missing.");
        }

        var adminEmail =
            user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue(ClaimTypes.Upn)
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("email");

        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            return Result<SetupIdentity>.Failure("Authenticated user email claim is missing.");
        }

        var adminDisplayName =
            user.FindFirstValue("name")
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? adminEmail;

        return Result<SetupIdentity>.Success(
            new SetupIdentity(
                entraTenantId.Trim(),
                adminEmail.Trim(),
                adminDisplayName.Trim(),
                adminEntraObjectId.Trim()
            )
        );
    }

    private sealed record SetupIdentity(
        string EntraTenantId,
        string AdminEmail,
        string AdminDisplayName,
        string AdminEntraObjectId
    );
}
