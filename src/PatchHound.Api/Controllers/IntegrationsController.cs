using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Integrations;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Credentials;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/integrations")]
[Authorize]
public class IntegrationsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;

    public IntegrationsController(PatchHoundDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("sentinel-connector")]
    [Authorize(Policy = Policies.ManageVault)]
    public async Task<ActionResult<SentinelConnectorDto>> GetSentinelConnector(CancellationToken ct)
    {
        var config = await _dbContext
            .SentinelConnectorConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (config is null)
        {
            return Ok(
                new SentinelConnectorDto(
                    Enabled: false,
                    DceEndpoint: string.Empty,
                    DcrImmutableId: string.Empty,
                    StreamName: string.Empty,
                    StoredCredentialId: null,
                    AcceptedCredentialTypes: [StoredCredentialTypes.EntraClientSecret],
                    UpdatedAt: null
                )
            );
        }

        return Ok(
            new SentinelConnectorDto(
                Enabled: config.Enabled,
                DceEndpoint: config.DceEndpoint,
                DcrImmutableId: config.DcrImmutableId,
                StreamName: config.StreamName,
                StoredCredentialId: config.StoredCredentialId,
                AcceptedCredentialTypes: [StoredCredentialTypes.EntraClientSecret],
                UpdatedAt: config.UpdatedAt
            )
        );
    }

    [HttpPut("sentinel-connector")]
    [Authorize(Policy = Policies.ManageVault)]
    public async Task<IActionResult> UpdateSentinelConnector(
        [FromBody] UpdateSentinelConnectorRequest request,
        CancellationToken ct
    )
    {
        var config = await _dbContext.SentinelConnectorConfigurations.FirstOrDefaultAsync(ct);
        if (request.Enabled && !request.StoredCredentialId.HasValue)
        {
            return ValidationProblem(
                "Sentinel requires a global Entra ID stored credential before it can be enabled."
            );
        }

        if (request.StoredCredentialId.HasValue)
        {
            var exists = await _dbContext.StoredCredentials.AsNoTracking().AnyAsync(
                credential =>
                    credential.Id == request.StoredCredentialId.Value
                    && credential.Type == StoredCredentialTypes.EntraClientSecret
                    && credential.IsGlobal,
                ct
            );

            if (!exists)
                return ValidationProblem("Sentinel requires a global Entra ID stored credential.");
        }

        if (config is null)
        {
            config = SentinelConnectorConfiguration.Create(
                enabled: request.Enabled,
                dceEndpoint: request.DceEndpoint.Trim(),
                dcrImmutableId: request.DcrImmutableId.Trim(),
                streamName: request.StreamName.Trim(),
                storedCredentialId: request.StoredCredentialId
            );
            await _dbContext.SentinelConnectorConfigurations.AddAsync(config, ct);
        }
        else
        {
            config.Update(
                enabled: request.Enabled,
                dceEndpoint: request.DceEndpoint.Trim(),
                dcrImmutableId: request.DcrImmutableId.Trim(),
                streamName: request.StreamName.Trim(),
                storedCredentialId: request.StoredCredentialId
            );
        }

        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }
}
