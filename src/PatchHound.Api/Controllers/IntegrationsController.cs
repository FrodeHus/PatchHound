using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Integrations;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/integrations")]
[Authorize]
public class IntegrationsController : ControllerBase
{
    private const string SentinelSecretPath = "system/sentinel-connector";
    private const string SentinelSecretKey = "clientSecret";

    private readonly ISecretStore _secretStore;
    private readonly PatchHoundDbContext _dbContext;

    public IntegrationsController(ISecretStore secretStore, PatchHoundDbContext dbContext)
    {
        _secretStore = secretStore;
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
                    TenantId: string.Empty,
                    ClientId: string.Empty,
                    HasSecret: false,
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
                TenantId: config.TenantId,
                ClientId: config.ClientId,
                HasSecret: !string.IsNullOrWhiteSpace(config.SecretRef),
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

        var hasNewSecret = !string.IsNullOrWhiteSpace(request.ClientSecret);
        var secretRef = config?.SecretRef ?? string.Empty;

        if (hasNewSecret)
        {
            secretRef = SentinelSecretPath;
        }

        if (config is null)
        {
            config = SentinelConnectorConfiguration.Create(
                enabled: request.Enabled,
                dceEndpoint: request.DceEndpoint.Trim(),
                dcrImmutableId: request.DcrImmutableId.Trim(),
                streamName: request.StreamName.Trim(),
                tenantId: request.TenantId.Trim(),
                clientId: request.ClientId.Trim(),
                secretRef: secretRef
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
                tenantId: request.TenantId.Trim(),
                clientId: request.ClientId.Trim(),
                secretRef: secretRef
            );
        }

        await _dbContext.SaveChangesAsync(ct);

        // Write secret to vault after DB commit succeeds
        if (hasNewSecret)
        {
            await _secretStore.PutSecretAsync(
                SentinelSecretPath,
                new Dictionary<string, string> { [SentinelSecretKey] = request.ClientSecret!.Trim() },
                ct
            );
        }

        return NoContent();
    }
}
