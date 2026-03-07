using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Api.Models.Setup;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

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
    [AllowAnonymous]
    public async Task<IActionResult> Complete(
        [FromBody] SetupCompleteRequest request,
        CancellationToken ct
    )
    {
        if (await _setupService.IsInitializedAsync(ct))
        {
            return Conflict(new ProblemDetails { Title = "Application is already initialized." });
        }

        var result = await _setupService.CompleteSetupAsync(
            new SetupRequest(
                request.TenantName,
                request.EntraTenantId,
                request.TenantSettings,
                request.AdminEmail,
                request.AdminDisplayName,
                request.AdminEntraObjectId
            ),
            ct
        );

        if (!result.IsSuccess)
        {
            return BadRequest(new ProblemDetails { Title = result.Error });
        }

        return Ok();
    }
}
