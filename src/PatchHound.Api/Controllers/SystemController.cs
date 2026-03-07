using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.System;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;
using Microsoft.EntityFrameworkCore;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/system")]
[Authorize]
public class SystemController : ControllerBase
{
    private readonly ISecretStore _secretStore;
    private readonly PatchHoundDbContext _dbContext;
    private readonly AuditLogWriter _auditLogWriter;

    public SystemController(
        ISecretStore secretStore,
        PatchHoundDbContext dbContext,
        AuditLogWriter auditLogWriter
    )
    {
        _secretStore = secretStore;
        _dbContext = dbContext;
        _auditLogWriter = auditLogWriter;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<SystemStatusDto>> GetStatus(CancellationToken ct)
    {
        var status = await _secretStore.GetStatusAsync(ct);
        return Ok(new SystemStatusDto(status.IsAvailable, status.IsInitialized, status.IsSealed));
    }

    [HttpPost("openbao/unseal")]
    [Authorize(Policy = Policies.ManageVault)]
    public async Task<ActionResult<SystemStatusDto>> Unseal(
        [FromBody] OpenBaoUnsealRequest request,
        CancellationToken ct
    )
    {
        var keys = request.Keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .ToList();

        if (keys.Count < 3)
        {
            return BadRequest(new ProblemDetails { Title = "Three unseal keys are required." });
        }

        var status = await _secretStore.UnsealAsync(keys, ct);
        return Ok(new SystemStatusDto(status.IsAvailable, status.IsInitialized, status.IsSealed));
    }

    [HttpGet("enrichment-sources")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<ActionResult<IReadOnlyList<EnrichmentSourceDto>>> GetEnrichmentSources(CancellationToken ct)
    {
        var sources = await _dbContext.EnrichmentSourceConfigurations.AsNoTracking()
            .OrderBy(source => source.DisplayName)
            .ToListAsync(ct);

        return Ok(sources.Select(MapEnrichmentSourceDto).ToList());
    }

    [HttpPut("enrichment-sources")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> UpdateEnrichmentSources(
        [FromBody] List<UpdateEnrichmentSourceRequest> request,
        CancellationToken ct
    )
    {
        var existingSources = await _dbContext.EnrichmentSourceConfigurations
            .ToDictionaryAsync(source => source.SourceKey, StringComparer.OrdinalIgnoreCase, ct);

        var pendingSecretWrites = new List<(string Path, string Key, string Value, string SourceKey, bool HadSecret, string OldSecretRef)>();

        foreach (var source in request)
        {
            existingSources.TryGetValue(source.Key, out var existingSource);
            var secretRef = existingSource?.SecretRef ?? string.Empty;
            var secretValue = source.Credentials.Secret.Trim();

            if (!string.IsNullOrWhiteSpace(secretValue))
            {
                var hadSecret = !string.IsNullOrWhiteSpace(secretRef);
                var oldSecretRef = secretRef;
                secretRef = $"system/enrichment-sources/{source.Key}";
                pendingSecretWrites.Add((
                    secretRef,
                    EnrichmentSourceCatalog.GetSecretKeyName(source.Key),
                    secretValue,
                    source.Key,
                    hadSecret,
                    oldSecretRef
                ));
            }

            if (existingSource is null)
            {
                existingSource = EnrichmentSourceConfiguration.Create(
                    source.Key,
                    source.DisplayName,
                    source.Enabled,
                    secretRef,
                    source.Credentials.ApiBaseUrl
                );
                await _dbContext.EnrichmentSourceConfigurations.AddAsync(existingSource, ct);
                existingSources[source.Key] = existingSource;
                continue;
            }

            existingSource.UpdateConfiguration(
                source.DisplayName,
                source.Enabled,
                secretRef,
                source.Credentials.ApiBaseUrl
            );
        }

        await _dbContext.SaveChangesAsync(ct);

        // Write secrets to vault after DB commit succeeds
        foreach (var (path, key, value, sourceKey, hadSecret, oldSecretRef) in pendingSecretWrites)
        {
            await _secretStore.PutSecretAsync(
                path,
                new Dictionary<string, string> { [key] = value },
                ct
            );

            if (existingSources.TryGetValue(sourceKey, out var auditSource))
            {
                await _auditLogWriter.WriteAsync(
                    Guid.Empty,
                    "EnrichmentSourceSecret",
                    auditSource.Id,
                    hadSecret ? AuditAction.Updated : AuditAction.Created,
                    hadSecret ? new { Key = sourceKey, HasSecret = true, SecretRef = oldSecretRef } : null,
                    new { Key = sourceKey, HasSecret = true, SecretRef = path },
                    ct
                );
            }
        }

        if (pendingSecretWrites.Count > 0)
            await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    private static EnrichmentSourceDto MapEnrichmentSourceDto(EnrichmentSourceConfiguration source)
    {
        return new EnrichmentSourceDto(
            source.SourceKey,
            source.DisplayName,
            source.Enabled,
            new EnrichmentSourceCredentialsDto(
                !string.IsNullOrWhiteSpace(source.SecretRef),
                source.ApiBaseUrl
            ),
            new EnrichmentSourceRuntimeDto(
                source.LastStartedAt,
                source.LastCompletedAt,
                source.LastSucceededAt,
                source.LastStatus,
                source.LastError
            )
        );
    }
}
