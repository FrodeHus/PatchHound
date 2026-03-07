using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Admin;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;
    private readonly IngestionService _ingestionService;

    public TenantsController(
        PatchHoundDbContext dbContext,
        ISecretStore secretStore,
        IngestionService ingestionService
    )
    {
        _dbContext = dbContext;
        _secretStore = secretStore;
        _ingestionService = ingestionService;
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
                t.Settings,
            })
            .ToListAsync(ct);

        return Ok(new PagedResponse<TenantListItemDto>(
            items.Select(t => new TenantListItemDto(
                t.Id,
                t.Name,
                t.EntraTenantId,
                GetConfiguredIngestionSourceCount(t.Settings)
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

        return Ok(new TenantDetailDto(
            tenant.Id,
            tenant.Name,
            tenant.EntraTenantId,
            GetIngestionSources(tenant.Settings)
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
        var existingSources = TenantSourceSettings
            .ReadSources(tenant.Settings)
            .ToDictionary(source => source.Key, StringComparer.OrdinalIgnoreCase);

        var updatedSources = new List<PersistedIngestionSource>();
        foreach (var source in request.IngestionSources)
        {
            existingSources.TryGetValue(source.Key, out var existingSource);
            var secretRef = existingSource?.Credentials?.SecretRef ?? string.Empty;
            var secretValue = source.Credentials.ClientSecret;

            if (string.IsNullOrWhiteSpace(secretValue))
            {
                secretValue = existingSource?.Credentials?.ClientSecret ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(secretValue))
            {
                secretRef = $"tenants/{tenant.Id}/sources/{source.Key}";
                await _secretStore.PutSecretAsync(
                    secretRef,
                    new Dictionary<string, string>
                    {
                        ["clientSecret"] = secretValue,
                    },
                    ct
                );
            }

            updatedSources.Add(new PersistedIngestionSource
            {
                Key = source.Key,
                DisplayName = source.DisplayName,
                Enabled = source.Enabled,
                SyncSchedule = source.SyncSchedule,
                Credentials = new PersistedSourceCredentials
                {
                    TenantId = source.Credentials.TenantId,
                    ClientId = source.Credentials.ClientId,
                    ClientSecret = string.Empty,
                    SecretRef = secretRef,
                    ApiBaseUrl = source.Credentials.ApiBaseUrl,
                    TokenScope = source.Credentials.TokenScope,
                },
            });
        }

        tenant.UpdateSettings(TenantSourceSettings.WriteSources(tenant.Settings, updatedSources));
        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/ingestion-sources/{sourceKey}/sync")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> TriggerSync(Guid id, string sourceKey, CancellationToken ct)
    {
        var tenant = await _dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        var configuredSource = TenantSourceSettings
            .ReadSources(tenant.Settings)
            .FirstOrDefault(source => string.Equals(source.Key, sourceKey, StringComparison.OrdinalIgnoreCase));

        if (configuredSource is null)
        {
            return NotFound(new ProblemDetails { Title = "Ingestion source not found" });
        }

        await _ingestionService.RunIngestionAsync(id, sourceKey, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/settings")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> UpdateSettings(
        Guid id,
        [FromBody] UpdateTenantSettingsRequest request,
        CancellationToken ct
    )
    {
        // Validate that settings is valid JSON with expected structure
        if (!TenantSourceSettings.IsValidSettingsJson(request.Settings))
            return ValidationProblem("Settings must be a valid JSON object.");

        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        tenant.UpdateSettings(request.Settings);
        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    private int GetConfiguredIngestionSourceCount(string settings)
    {
        return GetIngestionSources(settings)
            .Count(source => HasConfiguredCredentials(source.Credentials));
    }

    private List<TenantIngestionSourceDto> GetIngestionSources(string settings)
    {
        return TenantSourceSettings
            .ReadSources(settings)
            .Select(source => new TenantIngestionSourceDto(
                source.Key,
                source.DisplayName,
                source.Enabled,
                source.SyncSchedule,
                new TenantSourceCredentialsDto(
                    source.Credentials?.TenantId ?? string.Empty,
                    source.Credentials?.ClientId ?? string.Empty,
                    !string.IsNullOrWhiteSpace(source.Credentials?.SecretRef)
                        || !string.IsNullOrWhiteSpace(source.Credentials?.ClientSecret),
                    source.Credentials?.ApiBaseUrl ?? string.Empty,
                    source.Credentials?.TokenScope ?? string.Empty
                ),
                new TenantIngestionRuntimeDto(
                    source.Runtime?.LastStartedAt,
                    source.Runtime?.LastCompletedAt,
                    source.Runtime?.LastSucceededAt,
                    source.Runtime?.LastStatus ?? string.Empty,
                    source.Runtime?.LastError ?? string.Empty
                )
            ))
            .ToList();
    }

    private static bool HasConfiguredCredentials(TenantSourceCredentialsDto credentials)
    {
        return !string.IsNullOrWhiteSpace(credentials.TenantId)
            || !string.IsNullOrWhiteSpace(credentials.ClientId)
            || credentials.HasClientSecret;
    }
}
