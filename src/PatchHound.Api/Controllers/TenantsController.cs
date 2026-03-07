using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Admin;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Options;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly DefenderOptions _defenderOptions;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public TenantsController(PatchHoundDbContext dbContext, IOptions<DefenderOptions> defenderOptions)
    {
        _dbContext = dbContext;
        _defenderOptions = defenderOptions.Value;
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
            .Take(pagination.PageSize)
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

        var settings = ParseSettingsObject(tenant.Settings);
        settings["ingestionSources"] = JsonSerializer.SerializeToNode(
            request.IngestionSources.Select(MapToPersistedSource).ToList(),
            JsonOptions
        );

        tenant.UpdateSettings(settings.ToJsonString(JsonOptions));
        await _dbContext.SaveChangesAsync(ct);

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
        var defaults = new Dictionary<string, TenantIngestionSourceDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["microsoft-defender"] = new(
                "microsoft-defender",
                "Microsoft Defender",
                false,
                "0 */6 * * *",
                new TenantSourceCredentialsDto(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.IsNullOrWhiteSpace(_defenderOptions.ApiBaseUrl)
                        ? "https://api.securitycenter.microsoft.com"
                        : _defenderOptions.ApiBaseUrl,
                    string.IsNullOrWhiteSpace(_defenderOptions.TokenScope)
                        ? "https://api.securitycenter.microsoft.com/.default"
                        : _defenderOptions.TokenScope
                )
            ),
        };

        var settingsObject = ParseSettingsObject(settings);
        var sourcesNode = settingsObject["ingestionSources"] as JsonArray;

        if (sourcesNode is null)
            return defaults.Values.ToList();

        foreach (var node in sourcesNode)
        {
            if (node is null)
                continue;

            var parsed = node.Deserialize<PersistedIngestionSource>(JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Key))
                continue;

            var fallback = defaults.GetValueOrDefault(parsed.Key);
            defaults[parsed.Key] = new TenantIngestionSourceDto(
                parsed.Key,
                string.IsNullOrWhiteSpace(parsed.DisplayName)
                    ? fallback?.DisplayName ?? parsed.Key
                    : parsed.DisplayName,
                parsed.Enabled,
                string.IsNullOrWhiteSpace(parsed.SyncSchedule) ? "0 */6 * * *" : parsed.SyncSchedule,
                new TenantSourceCredentialsDto(
                    parsed.Credentials?.TenantId ?? string.Empty,
                    parsed.Credentials?.ClientId ?? string.Empty,
                    parsed.Credentials?.ClientSecret ?? string.Empty,
                    string.IsNullOrWhiteSpace(parsed.Credentials?.ApiBaseUrl)
                        ? fallback?.Credentials.ApiBaseUrl ?? "https://api.securitycenter.microsoft.com"
                        : parsed.Credentials.ApiBaseUrl,
                    string.IsNullOrWhiteSpace(parsed.Credentials?.TokenScope)
                        ? fallback?.Credentials.TokenScope ?? "https://api.securitycenter.microsoft.com/.default"
                        : parsed.Credentials.TokenScope
                )
            );
        }

        return defaults.Values.OrderBy(source => source.DisplayName).ToList();
    }

    private static JsonObject ParseSettingsObject(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return [];

        try
        {
            return JsonNode.Parse(settings) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool HasConfiguredCredentials(TenantSourceCredentialsDto credentials)
    {
        return !string.IsNullOrWhiteSpace(credentials.TenantId)
            || !string.IsNullOrWhiteSpace(credentials.ClientId)
            || !string.IsNullOrWhiteSpace(credentials.ClientSecret);
    }

    private static PersistedIngestionSource MapToPersistedSource(
        UpdateTenantIngestionSourceRequest request
    )
    {
        return new PersistedIngestionSource
        {
            Key = request.Key,
            DisplayName = request.DisplayName,
            Enabled = request.Enabled,
            SyncSchedule = request.SyncSchedule,
            Credentials = new PersistedSourceCredentials
            {
                TenantId = request.Credentials.TenantId,
                ClientId = request.Credentials.ClientId,
                ClientSecret = request.Credentials.ClientSecret,
                ApiBaseUrl = request.Credentials.ApiBaseUrl,
                TokenScope = request.Credentials.TokenScope,
            },
        };
    }

    private sealed class PersistedIngestionSource
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string SyncSchedule { get; set; } = string.Empty;
        public PersistedSourceCredentials? Credentials { get; set; }
    }

    private sealed class PersistedSourceCredentials
    {
        public string TenantId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string ApiBaseUrl { get; set; } = string.Empty;
        public string TokenScope { get; set; } = string.Empty;
    }
}
