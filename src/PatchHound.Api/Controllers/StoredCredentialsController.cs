using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Credentials;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Credentials;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/stored-credentials")]
[Authorize(Policy = Policies.ConfigureTenant)]
public class StoredCredentialsController(
    PatchHoundDbContext dbContext,
    ISecretStore secretStore,
    ITenantContext tenantContext,
    AuditLogWriter auditLogWriter
) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StoredCredentialDto>>> List(
        [FromQuery] string? type,
        [FromQuery] Guid? tenantId,
        CancellationToken ct
    )
    {
        if (tenantId.HasValue && !tenantContext.HasAccessToTenant(tenantId.Value))
            return Forbid();

        var query = dbContext.StoredCredentials.AsNoTracking()
            .Include(credential => credential.TenantScopes)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(credential => credential.Type == type);

        if (tenantId.HasValue)
        {
            query = query.Where(credential =>
                credential.IsGlobal
                || credential.TenantScopes.Any(scope => scope.TenantId == tenantId.Value));
        }

        var credentials = await query
            .OrderBy(credential => credential.Name)
            .ToListAsync(ct);

        return credentials.Select(MapDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<StoredCredentialDto>> Create(
        [FromBody] CreateStoredCredentialRequest request,
        CancellationToken ct
    )
    {
        var validation = ValidateCredentialInput(
            request.Type,
            request.Name,
            request.CredentialTenantId,
            request.ClientId,
            request.ClientSecret,
            request.IsGlobal,
            request.TenantIds
        );
        if (validation is not null) return validation;

        if (!request.IsGlobal && request.TenantIds.Any(id => !tenantContext.HasAccessToTenant(id)))
            return Forbid();

        var now = DateTimeOffset.UtcNow;
        var credentialId = Guid.NewGuid();
        var secretRef = $"stored-credentials/{credentialId}";
        var credential = StoredCredential.Create(
            request.Name,
            request.Type,
            request.IsGlobal,
            request.CredentialTenantId,
            request.ClientId,
            secretRef,
            now,
            credentialId
        );

        foreach (var tenantId in NormalizeTenantIds(request.IsGlobal, request.TenantIds))
            credential.TenantScopes.Add(StoredCredentialTenant.Create(credential.Id, tenantId));

        await secretStore.PutSecretAsync(
            secretRef,
            new Dictionary<string, string>
            {
                [StoredCredentialSecretKeys.ClientSecret] = request.ClientSecret.Trim(),
            },
            ct
        );

        await dbContext.StoredCredentials.AddAsync(credential, ct);
        await dbContext.SaveChangesAsync(ct);

        await auditLogWriter.WriteAsync(
            Guid.Empty,
            nameof(StoredCredential),
            credential.Id,
            AuditAction.Created,
            null,
            new
            {
                credential.Name,
                credential.Type,
                credential.IsGlobal,
                TenantIds = credential.TenantScopes.Select(scope => scope.TenantId).ToList(),
            },
            ct
        );
        await dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { id = credential.Id }, MapDto(credential));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateStoredCredentialRequest request,
        CancellationToken ct
    )
    {
        var credential = await dbContext.StoredCredentials
            .Include(item => item.TenantScopes)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
        if (credential is null) return NotFound();

        var validation = ValidateCredentialInput(
            credential.Type,
            request.Name,
            request.CredentialTenantId,
            request.ClientId,
            request.ClientSecret,
            request.IsGlobal,
            request.TenantIds,
            requireSecret: false
        );
        if (validation is not null) return validation;

        if (!request.IsGlobal && request.TenantIds.Any(tenantId => !tenantContext.HasAccessToTenant(tenantId)))
            return Forbid();

        var oldValues = new
        {
            credential.Name,
            credential.Type,
            credential.IsGlobal,
            credential.CredentialTenantId,
            credential.ClientId,
            TenantIds = credential.TenantScopes.Select(scope => scope.TenantId).ToList(),
        };

        var now = DateTimeOffset.UtcNow;
        credential.Update(
            request.Name,
            request.IsGlobal,
            request.CredentialTenantId,
            request.ClientId,
            now
        );

        dbContext.StoredCredentialTenants.RemoveRange(credential.TenantScopes);
        foreach (var tenantId in NormalizeTenantIds(request.IsGlobal, request.TenantIds))
            credential.TenantScopes.Add(StoredCredentialTenant.Create(credential.Id, tenantId));

        if (!string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            await secretStore.PutSecretAsync(
                credential.SecretRef,
                new Dictionary<string, string>
                {
                    [StoredCredentialSecretKeys.ClientSecret] = request.ClientSecret.Trim(),
                },
                ct
            );
        }

        await auditLogWriter.WriteAsync(
            Guid.Empty,
            nameof(StoredCredential),
            credential.Id,
            AuditAction.Updated,
            oldValues,
            new
            {
                credential.Name,
                credential.Type,
                credential.IsGlobal,
                credential.CredentialTenantId,
                credential.ClientId,
                TenantIds = credential.TenantScopes.Select(scope => scope.TenantId).ToList(),
            },
            ct
        );
        await dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var inUse = await dbContext.TenantSourceConfigurations.AsNoTracking()
                .AnyAsync(source => source.StoredCredentialId == id, ct)
            || await dbContext.EnrichmentSourceConfigurations.AsNoTracking()
                .AnyAsync(source => source.StoredCredentialId == id, ct)
            || await dbContext.SentinelConnectorConfigurations.AsNoTracking()
                .AnyAsync(source => source.StoredCredentialId == id, ct);
        if (inUse)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Credential is still in use.",
                Detail = "Detach this credential from all source configurations before deleting it.",
            });
        }

        var credential = await dbContext.StoredCredentials
            .FirstOrDefaultAsync(item => item.Id == id, ct);
        if (credential is null) return NotFound();

        dbContext.StoredCredentials.Remove(credential);
        await auditLogWriter.WriteAsync(
            Guid.Empty,
            nameof(StoredCredential),
            credential.Id,
            AuditAction.Deleted,
            new { credential.Name, credential.Type, credential.IsGlobal },
            null,
            ct
        );
        await dbContext.SaveChangesAsync(ct);
        await secretStore.DeleteSecretPathAsync(credential.SecretRef, ct);

        return NoContent();
    }

    private static ActionResult? ValidateCredentialInput(
        string type,
        string name,
        string credentialTenantId,
        string clientId,
        string? clientSecret,
        bool isGlobal,
        IReadOnlyList<Guid> tenantIds,
        bool requireSecret = true
    )
    {
        if (!string.Equals(type, StoredCredentialTypes.EntraClientSecret, StringComparison.OrdinalIgnoreCase))
            return new BadRequestObjectResult(new ProblemDetails { Title = "Unsupported credential type." });

        if (string.IsNullOrWhiteSpace(name))
            return new BadRequestObjectResult(new ProblemDetails { Title = "Credential name is required." });

        if (string.IsNullOrWhiteSpace(credentialTenantId) || string.IsNullOrWhiteSpace(clientId))
            return new BadRequestObjectResult(new ProblemDetails { Title = "Tenant ID and client ID are required." });

        if (requireSecret && string.IsNullOrWhiteSpace(clientSecret))
            return new BadRequestObjectResult(new ProblemDetails { Title = "Client secret is required." });

        if (!isGlobal && tenantIds.Count == 0)
            return new BadRequestObjectResult(new ProblemDetails { Title = "Select at least one tenant or mark the credential global." });

        return null;
    }

    private static IReadOnlyList<Guid> NormalizeTenantIds(bool isGlobal, IReadOnlyList<Guid> tenantIds) =>
        isGlobal ? [] : tenantIds.Where(id => id != Guid.Empty).Distinct().ToList();

    private static StoredCredentialDto MapDto(StoredCredential credential) =>
        new(
            credential.Id,
            credential.Name,
            credential.Type,
            StoredCredentialTypes.GetDisplayName(credential.Type),
            credential.IsGlobal,
            credential.CredentialTenantId,
            credential.ClientId,
            credential.TenantScopes.Select(scope => scope.TenantId).OrderBy(id => id).ToList(),
            credential.CreatedAt,
            credential.UpdatedAt
        );
}
