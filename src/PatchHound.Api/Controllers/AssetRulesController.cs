using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.AssetRules;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/asset-rules")]
[Authorize(Policy = Policies.ConfigureTenant)]
public class AssetRulesController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IDeviceRuleEvaluationService _evaluationService;
    private readonly RiskRefreshService _riskRefreshService;

    public AssetRulesController(
        PatchHoundDbContext dbContext,
        ITenantContext tenantContext,
        IDeviceRuleEvaluationService evaluationService,
        RiskRefreshService riskRefreshService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _evaluationService = evaluationService;
        _riskRefreshService = riskRefreshService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<AssetRuleDto>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var query = _dbContext.AssetRules
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Priority);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var dtos = items.Select(ToDto).ToList();
        return Ok(new PagedResponse<AssetRuleDto>(dtos, totalCount, pagination.Page, pagination.BoundedPageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AssetRuleDto>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var rule = await _dbContext.AssetRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

        if (rule is null)
            return NotFound();

        return Ok(ToDto(rule));
    }

    [HttpPost]
    public async Task<ActionResult<AssetRuleDto>> Create(
        [FromBody] CreateAssetRuleRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var filter = DeserializeFilter(request.FilterDefinition);
        var operations = DeserializeOperations(request.Operations);

        if (filter is null || operations is null)
            return BadRequest(new ProblemDetails { Title = "Invalid filter or operations JSON." });

        var validationError = ValidateOperations(operations);
        if (validationError is not null)
            return BadRequest(new ProblemDetails { Title = validationError });

        var referenceValidationError = await ValidateOperationReferencesAsync(tenantId, operations, ct);
        if (referenceValidationError is not null)
            return BadRequest(new ProblemDetails { Title = referenceValidationError });

        var maxPriority = await _dbContext.AssetRules
            .Where(r => r.TenantId == tenantId)
            .MaxAsync(r => (int?)r.Priority, ct) ?? 0;

        var rule = AssetRule.Create(tenantId, request.Name, request.Description, maxPriority + 1, filter, operations);
        _dbContext.AssetRules.Add(rule);
        await _dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = rule.Id }, ToDto(rule));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AssetRuleDto>> Update(
        Guid id,
        [FromBody] UpdateAssetRuleRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var rule = await _dbContext.AssetRules
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

        if (rule is null)
            return NotFound();

        var filter = DeserializeFilter(request.FilterDefinition);
        var operations = DeserializeOperations(request.Operations);

        if (filter is null || operations is null)
            return BadRequest(new ProblemDetails { Title = "Invalid filter or operations JSON." });

        var validationError = ValidateOperations(operations);
        if (validationError is not null)
            return BadRequest(new ProblemDetails { Title = validationError });

        var referenceValidationError = await ValidateOperationReferencesAsync(tenantId, operations, ct);
        if (referenceValidationError is not null)
            return BadRequest(new ProblemDetails { Title = referenceValidationError });

        rule.Update(request.Name, request.Description, request.Enabled, filter, operations);
        await _dbContext.SaveChangesAsync(ct);

        return Ok(ToDto(rule));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var rule = await _dbContext.AssetRules
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

        if (rule is null)
            return NotFound();

        var affectedAssetIds = await GetMatchingAssetIdsAsync(rule, tenantId, ct);
        var operations = rule.ParseOperations();

        _dbContext.AssetRules.Remove(rule);
        await _dbContext.SaveChangesAsync(ct);

        // Reorder remaining rules to close the gap
        var remaining = await _dbContext.AssetRules
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        for (var i = 0; i < remaining.Count; i++)
            remaining[i].SetPriority(i + 1);

        await _dbContext.SaveChangesAsync(ct);
        await ResetDeletedRuleEffectsAsync(tenantId, rule.Id, affectedAssetIds, operations, ct);
        await _evaluationService.EvaluateRulesAsync(tenantId, ct);
        await _riskRefreshService.RefreshForTenantAsync(
            tenantId,
            recalculateAssessments: true,
            ct
        );
        return NoContent();
    }

    [HttpPost("preview")]
    public async Task<ActionResult<AssetRulePreviewDto>> Preview(
        [FromBody] PreviewFilterRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var filter = DeserializeFilter(request.FilterDefinition);
        if (filter is null)
            return BadRequest(new ProblemDetails { Title = "Invalid filter JSON." });

        var result = await _evaluationService.PreviewFilterAsync(tenantId, filter, ct);
        return Ok(new AssetRulePreviewDto(
            result.Count,
            result.Samples.Select(s => new AssetPreviewItemDto(s.Id, s.Name, "Device")).ToList()));
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
        [FromBody] ReorderRulesRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var rules = await _dbContext.AssetRules
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

    private static AssetRuleDto ToDto(AssetRule rule) => new(
        rule.Id,
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
                    return $"Unknown asset rule operation type: {operation.Type}.";
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
                        var exists = await _dbContext.AssetSecurityProfiles
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

    private async Task<List<Guid>> GetMatchingAssetIdsAsync(
        AssetRule rule,
        Guid tenantId,
        CancellationToken ct)
    {
        try
        {
            var predicate = new AssetRuleFilterBuilder(_dbContext).Build(rule.ParseFilter());
            return await _dbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.TenantId == tenantId)
                .Where(predicate)
                .Select(asset => asset.Id)
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
        IReadOnlyCollection<Guid> assetIds,
        IReadOnlyCollection<AssetRuleOperation> operations,
        CancellationToken ct)
    {
        if (assetIds.Count == 0 || operations.Count == 0)
            return;

        foreach (var operation in operations)
        {
            switch (operation.Type)
            {
                case "AssignSecurityProfile":
                    if (operation.Parameters.TryGetValue("securityProfileId", out _))
                    {
                        var assets = await _dbContext.Assets
                            .Where(asset =>
                                asset.TenantId == tenantId
                                && assetIds.Contains(asset.Id)
                                && asset.SecurityProfileRuleId == deletedRuleId)
                            .ToListAsync(ct);

                        foreach (var asset in assets)
                            asset.ClearRuleAssignedSecurityProfile(deletedRuleId);

                        if (assets.Count > 0)
                            await _dbContext.SaveChangesAsync(ct);
                    }
                    break;

                case "AssignTeam":
                    if (operation.Parameters.TryGetValue("teamId", out _))
                    {
                        var assets = await _dbContext.Assets
                            .Where(asset =>
                                asset.TenantId == tenantId
                                && assetIds.Contains(asset.Id)
                                && asset.FallbackTeamRuleId == deletedRuleId)
                            .ToListAsync(ct);

                        foreach (var asset in assets)
                            asset.ClearRuleAssignedFallbackTeam(deletedRuleId);

                        if (assets.Count > 0)
                            await _dbContext.SaveChangesAsync(ct);
                    }
                    break;

                case "SetCriticality":
                {
                    var assets = await _dbContext.Assets
                        .Where(asset =>
                            asset.TenantId == tenantId
                            && assetIds.Contains(asset.Id)
                            && asset.CriticalitySource == "Rule"
                            && asset.CriticalityRuleId == deletedRuleId)
                        .ToListAsync(ct);

                    foreach (var asset in assets)
                        asset.ResetCriticalityToBaseline();

                    if (assets.Count > 0)
                        await _dbContext.SaveChangesAsync(ct);

                    break;
                }

                case "AssignBusinessLabel":
                    if (operation.Parameters.TryGetValue("businessLabelId", out _))
                    {
                        var existingAssignments = await _dbContext.AssetBusinessLabels
                            .Where(link =>
                                link.AssignedByRuleId == deletedRuleId
                                && link.SourceType == AssetBusinessLabel.RuleSourceType)
                            .ToListAsync(ct);

                        if (existingAssignments.Count > 0)
                        {
                            _dbContext.AssetBusinessLabels.RemoveRange(existingAssignments);
                            await _dbContext.SaveChangesAsync(ct);
                        }
                    }
                    break;
            }
        }
    }
}
