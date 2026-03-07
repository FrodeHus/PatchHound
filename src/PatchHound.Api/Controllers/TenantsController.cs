using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Admin;
using PatchHound.Core.Enums;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;

    public TenantsController(PatchHoundDbContext dbContext, ISecretStore secretStore)
    {
        _dbContext = dbContext;
        _secretStore = secretStore;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<PagedResponse<TenantListItemDto>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.Tenants.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.EntraTenantId,
            })
            .ToListAsync(ct);

        var tenantIds = items.Select(item => item.Id).ToList();
        var sourceCounts = await _dbContext
            .TenantSourceConfigurations.AsNoTracking()
            .Where(source => tenantIds.Contains(source.TenantId))
            .ToListAsync(ct);

        return Ok(new PagedResponse<TenantListItemDto>(
            items.Select(t => new TenantListItemDto(
                t.Id,
                t.Name,
                t.EntraTenantId,
                sourceCounts.Count(source =>
                    source.TenantId == t.Id && TenantSourceCatalog.HasConfiguredCredentials(source))
            )).ToList(),
            totalCount
        ));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<TenantDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var tenant = await _dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        var assetCounts = await _dbContext
            .Assets.AsNoTracking()
            .Where(asset => asset.TenantId == id)
            .GroupBy(asset => asset.AssetType)
            .Select(group => new { AssetType = group.Key, Count = group.Count() })
            .ToListAsync(ct);

        var assetSummary = new TenantAssetSummaryDto(
            assetCounts.Sum(item => item.Count),
            assetCounts.FirstOrDefault(item => item.AssetType == AssetType.Device)?.Count ?? 0,
            assetCounts.FirstOrDefault(item => item.AssetType == AssetType.Software)?.Count ?? 0,
            assetCounts.FirstOrDefault(item => item.AssetType == AssetType.CloudResource)?.Count ?? 0
        );

        var sources = await _dbContext
            .TenantSourceConfigurations.AsNoTracking()
            .Where(source => source.TenantId == id)
            .OrderBy(source => source.DisplayName)
            .ToListAsync(ct);

        return Ok(new TenantDetailDto(
            tenant.Id,
            tenant.Name,
            tenant.EntraTenantId,
            assetSummary,
            sources.Select(MapSourceDto).ToList()
        ));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTenantRequest request,
        CancellationToken ct
    )
    {
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
            return ValidationProblem("Tenant name is required.");

        tenant.UpdateName(request.Name.Trim());
        var existingSources = await _dbContext
            .TenantSourceConfigurations
            .Where(source => source.TenantId == tenant.Id)
            .ToDictionaryAsync(source => source.SourceKey, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var source in request.IngestionSources)
        {
            existingSources.TryGetValue(source.Key, out var existingSource);
            var secretRef = existingSource?.SecretRef ?? string.Empty;
            var secretValue = source.Credentials.Secret.Trim();

            if (string.IsNullOrWhiteSpace(secretValue))
            {
                secretValue = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(secretValue))
            {
                secretRef = $"tenants/{tenant.Id}/sources/{source.Key}";
                await _secretStore.PutSecretAsync(
                    secretRef,
                    new Dictionary<string, string>
                    {
                        [TenantSourceCatalog.GetSecretKeyName(source.Key)] = secretValue,
                    },
                    ct
                );
            }

            if (existingSource is null)
            {
                var created = TenantSourceConfiguration.Create(
                    tenant.Id,
                    source.Key,
                    source.DisplayName,
                    source.Enabled,
                    source.SyncSchedule,
                    source.Credentials.TenantId,
                    source.Credentials.ClientId,
                    secretRef,
                    source.Credentials.ApiBaseUrl,
                    source.Credentials.TokenScope
                );
                await _dbContext.TenantSourceConfigurations.AddAsync(created, ct);
                existingSources[source.Key] = created;
                continue;
            }

            existingSource.UpdateConfiguration(
                source.DisplayName,
                source.Enabled,
                source.SyncSchedule,
                source.Credentials.TenantId,
                source.Credentials.ClientId,
                secretRef,
                source.Credentials.ApiBaseUrl,
                source.Credentials.TokenScope
            );
        }

        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/ingestion-sources/{sourceKey}/sync")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> TriggerSync(Guid id, string sourceKey, CancellationToken ct)
    {
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        var configuredSource = await _dbContext
            .TenantSourceConfigurations
            .FirstOrDefaultAsync(source =>
                source.TenantId == tenant.Id
                && string.Equals(source.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase), ct);

        if (configuredSource is null)
        {
            return NotFound(new ProblemDetails { Title = "Ingestion source not found" });
        }

        if (!TenantSourceCatalog.SupportsManualSync(configuredSource))
        {
            return BadRequest(new ProblemDetails { Title = "This source does not support manual sync." });
        }

        configuredSource.QueueManualSync(DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(ct);

        return Accepted();
    }

    private static TenantIngestionSourceDto MapSourceDto(TenantSourceConfiguration source)
    {
        return new TenantIngestionSourceDto(
            source.SourceKey,
            source.DisplayName,
            source.Enabled,
            source.SyncSchedule,
            TenantSourceCatalog.SupportsScheduling(source),
            TenantSourceCatalog.SupportsManualSync(source),
            new TenantSourceCredentialsDto(
                source.CredentialTenantId,
                source.ClientId,
                !string.IsNullOrWhiteSpace(source.SecretRef),
                source.ApiBaseUrl,
                source.TokenScope
            ),
            new TenantIngestionRuntimeDto(
                source.ManualRequestedAt,
                source.LastStartedAt,
                source.LastCompletedAt,
                source.LastSucceededAt,
                source.LastStatus,
                source.LastError
            )
        );
    }
}
