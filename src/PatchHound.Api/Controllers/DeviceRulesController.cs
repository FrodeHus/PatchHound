using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.DeviceRules;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Inventory;

namespace PatchHound.Api.Controllers;

// Phase 1 canonical cleanup (Task 14): canonical replacement for
// AssetRulesController. All reads/writes flow through DeviceRule +
// Device + DeviceBusinessLabel + SecurityProfile via the
// DeviceRuleFilterBuilder. Rule-source provenance on
// DeviceBusinessLabel distinguishes rule-assigned from manual links
// so delete/reorder can unwind only the rule's own effects.
[ApiController]
[Route("api/asset-rules")]
[Route("api/device-rules")]
[Authorize(Policy = Policies.ConfigureTenant)]
public class DeviceRulesController : ControllerBase
{
    private const string DeviceAssetType = "Device";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IDeviceRuleEvaluationService _evaluationService;
    private readonly DeviceRuleFilterBuilder _filterBuilder;
    private readonly RiskRefreshService _riskRefreshService;

    public DeviceRulesController(
        PatchHoundDbContext dbContext,
        ITenantContext tenantContext,
        IDeviceRuleEvaluationService evaluationService,
        DeviceRuleFilterBuilder filterBuilder,
        RiskRefreshService riskRefreshService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _evaluationService = evaluationService;
        _filterBuilder = filterBuilder;
        _riskRefreshService = riskRefreshService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<DeviceRuleDto>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var query = _dbContext.DeviceRules
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Priority);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var dtos = items.Select(ToDto).ToList();
        return Ok(new PagedResponse<DeviceRuleDto>(dtos, totalCount, pagination.Page, pagination.BoundedPageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeviceRuleDto>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var rule = await _dbContext.DeviceRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

        if (rule is null)
            return NotFound();

        return Ok(ToDto(rule));
    }

    [HttpPost]
    public async Task<ActionResult<DeviceRuleDto>> Create(
        [FromBody] CreateDeviceRuleRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var filter = DeserializeFilter(request.FilterDefinition);
        var operations = DeserializeOperations(request.Operations);

        if (!IsSupportedAssetType(request.AssetType))
            return BadRequest(new ProblemDetails { Title = "Only Device asset rules are supported in this slice." });

        if (filter is null || operations is null)
            return BadRequest(new ProblemDetails { Title = "Invalid filter or operations JSON." });

        var validationError = ValidateOperations(operations);
        if (validationError is not null)
            return BadRequest(new ProblemDetails { Title = validationError });

        var referenceValidationError = await ValidateOperationReferencesAsync(tenantId, operations, ct);
        if (referenceValidationError is not null)
            return BadRequest(new ProblemDetails { Title = referenceValidationError });

        var maxPriority = await _dbContext.DeviceRules
            .Where(r => r.TenantId == tenantId)
            .MaxAsync(r => (int?)r.Priority, ct) ?? 0;

        var rule = DeviceRule.Create(tenantId, request.Name, request.Description, maxPriority + 1, request.AssetType, filter, operations);
        _dbContext.DeviceRules.Add(rule);
        await _dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = rule.Id }, ToDto(rule));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DeviceRuleDto>> Update(
        Guid id,
        [FromBody] UpdateDeviceRuleRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var rule = await _dbContext.DeviceRules
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

        if (rule is null)
            return NotFound();

        var filter = DeserializeFilter(request.FilterDefinition);
        var operations = DeserializeOperations(request.Operations);

        if (!IsSupportedAssetType(request.AssetType))
            return BadRequest(new ProblemDetails { Title = "Only Device asset rules are supported in this slice." });

        if (filter is null || operations is null)
            return BadRequest(new ProblemDetails { Title = "Invalid filter or operations JSON." });

        var validationError = ValidateOperations(operations);
        if (validationError is not null)
            return BadRequest(new ProblemDetails { Title = validationError });

        var referenceValidationError = await ValidateOperationReferencesAsync(tenantId, operations, ct);
        if (referenceValidationError is not null)
            return BadRequest(new ProblemDetails { Title = referenceValidationError });

        rule.Update(request.Name, request.Description, request.Enabled, request.AssetType, filter, operations);
        await _dbContext.SaveChangesAsync(ct);

        return Ok(ToDto(rule));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var rule = await _dbContext.DeviceRules
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

        if (rule is null)
            return NotFound();

        var affectedDeviceIds = await GetMatchingDeviceIdsAsync(rule, tenantId, ct);
        var operations = rule.ParseOperations();

        _dbContext.DeviceRules.Remove(rule);
        await _dbContext.SaveChangesAsync(ct);

        // Reorder remaining rules to close the gap
        var remaining = await _dbContext.DeviceRules
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        for (var i = 0; i < remaining.Count; i++)
            remaining[i].SetPriority(i + 1);

        await _dbContext.SaveChangesAsync(ct);
        await ResetDeletedRuleEffectsAsync(tenantId, rule.Id, affectedDeviceIds, operations, ct);
        await _evaluationService.EvaluateRulesAsync(tenantId, ct);
        await _riskRefreshService.RefreshForTenantAsync(
            tenantId,
            recalculateAssessments: true,
            ct
        );
        return NoContent();
    }

    [HttpPost("preview")]
    public async Task<ActionResult<DeviceRulePreviewDto>> Preview(
        [FromBody] PreviewDeviceRuleFilterRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        if (!IsSupportedAssetType(request.AssetType))
            return BadRequest(new ProblemDetails { Title = "Only Device asset rules are supported in this slice." });

        var filter = DeserializeFilter(request.FilterDefinition);
        if (filter is null)
            return BadRequest(new ProblemDetails { Title = "Invalid filter JSON." });

        var result = await _evaluationService.PreviewFilterAsync(tenantId, filter, ct);
        return Ok(new DeviceRulePreviewDto(
            result.Count,
            result.Samples.Select(s => new DevicePreviewItemDto(s.Id, s.Name)).ToList()));
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        await _evaluationService.EvaluateRulesAsync(tenantId, ct);
        await _riskRefreshService.RefreshForTenantAsync(
            tenantId,
            recalculateAssessments: true,
            ct
        );
        return NoContent();
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder(
        [FromBody] ReorderDeviceRulesRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var rules = await _dbContext.DeviceRules
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(ct);

        var ruleMap = rules.ToDictionary(r => r.Id);

        for (var i = 0; i < request.RuleIds.Count; i++)
        {
            if (ruleMap.TryGetValue(request.RuleIds[i], out var rule))
                rule.SetPriority(i + 1);
        }

        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    private static DeviceRuleDto ToDto(DeviceRule rule) => new(
        rule.Id,
        DeviceAssetType,
        rule.Name,
        rule.Description,
        rule.Priority,
        rule.Enabled,
        JsonDocument.Parse(rule.FilterDefinition).RootElement,
        JsonDocument.Parse(rule.Operations).RootElement,
        rule.CreatedAt,
        rule.UpdatedAt,
        rule.LastExecutedAt,
        rule.LastMatchCount
    );

    private static bool IsSupportedAssetType(string assetType) =>
        string.Equals(assetType, DeviceAssetType, StringComparison.OrdinalIgnoreCase);

    private static FilterNode? DeserializeFilter(JsonElement element)
    {
        try
        {
            return JsonSerializer.Deserialize<FilterNode>(element.GetRawText(), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static List<AssetRuleOperation>? DeserializeOperations(JsonElement element)
    {
        try
        {
            return JsonSerializer.Deserialize<List<AssetRuleOperation>>(element.GetRawText(), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? ValidateOperations(IReadOnlyList<AssetRuleOperation> operations)
    {
        foreach (var operation in operations)
        {
            switch (operation.Type)
            {
                case "AssignSecurityProfile":
                    if (!operation.Parameters.TryGetValue("securityProfileId", out var securityProfileId)
                        || !Guid.TryParse(securityProfileId, out _))
                    {
                        return "AssignSecurityProfile requires a valid securityProfileId.";
                    }
                    break;

                case "AssignTeam":
                    if (!operation.Parameters.TryGetValue("teamId", out var teamId)
                        || !Guid.TryParse(teamId, out _))
                    {
                        return "AssignTeam requires a valid teamId.";
                    }
                    break;

                case "AssignBusinessLabel":
                    if (!operation.Parameters.TryGetValue("businessLabelId", out var businessLabelId)
                        || !Guid.TryParse(businessLabelId, out _))
                    {
                        return "AssignBusinessLabel requires a valid businessLabelId.";
                    }
                    break;

                case "SetCriticality":
                    if (!operation.Parameters.TryGetValue("criticality", out var criticality)
                        || !Enum.TryParse<Criticality>(criticality, true, out _))
                    {
                        return "SetCriticality requires a valid criticality value.";
                    }
                    break;

                default:
                    return $"Unknown device rule operation type: {operation.Type}.";
            }
        }

        return null;
    }

    private async Task<string?> ValidateOperationReferencesAsync(
        Guid tenantId,
        IReadOnlyList<AssetRuleOperation> operations,
        CancellationToken ct)
    {
        foreach (var operation in operations)
        {
            switch (operation.Type)
            {
                case "AssignSecurityProfile":
                    if (operation.Parameters.TryGetValue("securityProfileId", out var securityProfileId)
                        && Guid.TryParse(securityProfileId, out var parsedSecurityProfileId))
                    {
                        var exists = await _dbContext.SecurityProfiles
                            .AnyAsync(profile => profile.TenantId == tenantId && profile.Id == parsedSecurityProfileId, ct);
                        if (!exists)
                            return "AssignSecurityProfile references a security profile that does not belong to the active tenant.";
                    }
                    break;

                case "AssignTeam":
                    if (operation.Parameters.TryGetValue("teamId", out var teamId)
                        && Guid.TryParse(teamId, out var parsedTeamId))
                    {
                        var exists = await _dbContext.Teams
                            .AnyAsync(team => team.TenantId == tenantId && team.Id == parsedTeamId, ct);
                        if (!exists)
                            return "AssignTeam references a team that does not belong to the active tenant.";
                    }
                    break;

                case "AssignBusinessLabel":
                    if (operation.Parameters.TryGetValue("businessLabelId", out var businessLabelId)
                        && Guid.TryParse(businessLabelId, out var parsedBusinessLabelId))
                    {
                        var exists = await _dbContext.BusinessLabels
                            .AnyAsync(label => label.TenantId == tenantId && label.IsActive && label.Id == parsedBusinessLabelId, ct);
                        if (!exists)
                            return "AssignBusinessLabel references an active business label that does not belong to the active tenant.";
                    }
                    break;
            }
        }

        return null;
    }

    private async Task<List<Guid>> GetMatchingDeviceIdsAsync(
        DeviceRule rule,
        Guid tenantId,
        CancellationToken ct)
    {
        try
        {
            var predicate = _filterBuilder.Build(rule.ParseFilter());
            return await _dbContext.Devices
                .AsNoTracking()
                .Where(device => device.TenantId == tenantId)
                .Where(predicate)
                .Select(device => device.Id)
                .ToListAsync(ct);
        }
        catch
        {
            return [];
        }
    }

    private async Task ResetDeletedRuleEffectsAsync(
        Guid tenantId,
        Guid deletedRuleId,
        IReadOnlyCollection<Guid> deviceIds,
        IReadOnlyCollection<AssetRuleOperation> operations,
        CancellationToken ct)
    {
        if (operations.Count == 0)
            return;

        foreach (var operation in operations)
        {
            switch (operation.Type)
            {
                case "AssignSecurityProfile":
                    if (operation.Parameters.TryGetValue("securityProfileId", out _) && deviceIds.Count > 0)
                    {
                        var devices = await _dbContext.Devices
                            .Where(device =>
                                device.TenantId == tenantId
                                && deviceIds.Contains(device.Id)
                                && device.SecurityProfileRuleId == deletedRuleId)
                            .ToListAsync(ct);

                        foreach (var device in devices)
                            device.ClearRuleAssignedSecurityProfile(deletedRuleId);

                        if (devices.Count > 0)
                            await _dbContext.SaveChangesAsync(ct);
                    }
                    break;

                case "AssignTeam":
                    if (operation.Parameters.TryGetValue("teamId", out _) && deviceIds.Count > 0)
                    {
                        var devices = await _dbContext.Devices
                            .Where(device =>
                                device.TenantId == tenantId
                                && deviceIds.Contains(device.Id)
                                && device.FallbackTeamRuleId == deletedRuleId)
                            .ToListAsync(ct);

                        foreach (var device in devices)
                            device.ClearRuleAssignedFallbackTeam(deletedRuleId);

                        if (devices.Count > 0)
                            await _dbContext.SaveChangesAsync(ct);
                    }
                    break;

                case "SetCriticality":
                {
                    if (deviceIds.Count == 0)
                        break;

                    var devices = await _dbContext.Devices
                        .Where(device =>
                            device.TenantId == tenantId
                            && deviceIds.Contains(device.Id)
                            && device.CriticalitySource == "Rule"
                            && device.CriticalityRuleId == deletedRuleId)
                        .ToListAsync(ct);

                    foreach (var device in devices)
                        device.ResetCriticalityToBaseline();

                    if (devices.Count > 0)
                        await _dbContext.SaveChangesAsync(ct);

                    break;
                }

                case "AssignBusinessLabel":
                    if (operation.Parameters.TryGetValue("businessLabelId", out _))
                    {
                        // Rule-assigned DeviceBusinessLabel links are identified by
                        // AssignedByRuleId — independent of the current device set so
                        // we catch any stragglers from earlier evaluations.
                        var existingAssignments = await _dbContext.DeviceBusinessLabels
                            .Where(link =>
                                link.TenantId == tenantId
                                && link.AssignedByRuleId == deletedRuleId
                                && link.SourceType == DeviceBusinessLabel.RuleSourceType)
                            .ToListAsync(ct);

                        if (existingAssignments.Count > 0)
                        {
                            _dbContext.DeviceBusinessLabels.RemoveRange(existingAssignments);
                            await _dbContext.SaveChangesAsync(ct);
                        }
                    }
                    break;
            }
        }
    }
}
