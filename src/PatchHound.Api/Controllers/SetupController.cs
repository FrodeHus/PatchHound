using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models.Setup;
using PatchHound.Core.Common;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Credentials;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/setup")]
public class SetupController : ControllerBase
{
    private readonly ISetupService _setupService;
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;

    public SetupController(
        ISetupService setupService,
        PatchHoundDbContext dbContext,
        ISecretStore secretStore
    )
    {
        _setupService = setupService;
        _dbContext = dbContext;
        _secretStore = secretStore;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<SetupStatusDto>> GetStatus(CancellationToken ct)
    {
        var isInitialized = await _setupService.IsInitializedAsync(ct);
        var entraTenantId =
            User.FindFirstValue("tid")
            ?? User.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        var requiresSetup = await _setupService.RequiresSetupForTenantAsync(entraTenantId, ct);
        return Ok(new SetupStatusDto(isInitialized, requiresSetup));
    }

    [HttpPost("complete")]
    [Authorize]
    public async Task<IActionResult> Complete(
        [FromBody] SetupCompleteRequest request,
        CancellationToken ct
    )
    {
        if (!HasRequiredSetupRole(User))
        {
            return Forbid();
        }

        var setupIdentity = ResolveSetupIdentity(User);
        if (!setupIdentity.IsSuccess)
        {
            return BadRequest(new ProblemDetails { Title = setupIdentity.Error });
        }

        if (!await _setupService.RequiresSetupForTenantAsync(setupIdentity.Value.EntraTenantId, ct))
        {
            return Conflict(new ProblemDetails { Title = "Tenant is already initialized." });
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

        if (request.Defender.Enabled)
        {
            var clientId = request.Defender.ClientId.Trim();
            var clientSecret = request.Defender.ClientSecret.Trim();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return BadRequest(
                    new ProblemDetails
                    {
                        Title = "Client ID and client secret are required when Defender setup is enabled.",
                    }
                );
            }

            var tenant = result.Value;
            var source = await _dbContext
                .TenantSourceConfigurations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    candidate =>
                        candidate.TenantId == tenant.Id
                        && candidate.SourceKey == TenantSourceCatalog.DefenderSourceKey,
                    ct
                );

            if (source is null)
            {
                return BadRequest(
                    new ProblemDetails { Title = "Default Defender ingestion source was not created." }
                );
            }

            var credentialId = Guid.NewGuid();
            var secretRef = $"stored-credentials/{credentialId}";
            var credential = StoredCredential.Create(
                "Microsoft Defender setup credential",
                StoredCredentialTypes.EntraClientSecret,
                isGlobal: false,
                tenant.EntraTenantId,
                clientId,
                secretRef,
                DateTimeOffset.UtcNow,
                credentialId
            );
            credential.TenantScopes.Add(StoredCredentialTenant.Create(credential.Id, tenant.Id));
            await _dbContext.StoredCredentials.AddAsync(credential, ct);

            source.UpdateConfiguration(
                source.DisplayName,
                true,
                TenantSourceCatalog.DefaultDefenderSchedule,
                credentialTenantId: string.Empty,
                clientId: string.Empty,
                secretRef: string.Empty,
                apiBaseUrl: TenantSourceCatalog.DefaultDefenderApiBaseUrl,
                tokenScope: TenantSourceCatalog.DefaultDefenderTokenScope,
                storedCredentialId: credential.Id
            );

            await _dbContext.SaveChangesAsync(ct);

            await _secretStore.PutSecretAsync(
                secretRef,
                new Dictionary<string, string>
                {
                    [StoredCredentialSecretKeys.ClientSecret] = clientSecret,
                },
                ct
            );
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
            ) && string.Equals(claim.Value, "Tenant.Admin", StringComparison.OrdinalIgnoreCase)
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
            user.FindFirstValue("name") ?? user.FindFirstValue(ClaimTypes.Name) ?? adminEmail;

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
