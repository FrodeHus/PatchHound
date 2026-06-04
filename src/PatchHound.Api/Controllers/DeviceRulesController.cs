using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.DeviceRules;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

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
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IDeviceRuleEvaluationService _evaluationService;
    private readonly DeviceRuleDefinitionService _definitionService;
    private readonly DeviceRulePreviewService _previewService;
    private readonly DeviceRuleCleanupService _cleanupService;
    private readonly RiskRefreshService _riskRefreshService;

    public DeviceRulesController(
        PatchHoundDbContext dbContext,
        ITenantContext tenantContext,
        IDeviceRuleEvaluationService evaluationService,
        DeviceRuleDefinitionService definitionService,
        DeviceRulePreviewService previewService,
        DeviceRuleCleanupService cleanupService,
        RiskRefreshService riskRefreshService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _evaluationService = evaluationService;
        _definitionService = definitionService;
        _previewService = previewService;
        _cleanupService = cleanupService;
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

        var definition = await _definitionService.ParseAndValidateAsync(
            tenantId,
            request.AssetType,
            request.FilterDefinition,
            request.Operations,
            ct);
        if (!definition.IsSuccess)
            return BadRequest(new ProblemDetails { Title = definition.Error });

        var maxPriority = await _dbContext.DeviceRules
            .Where(r => r.TenantId == tenantId)
            .MaxAsync(r => (int?)r.Priority, ct) ?? 0;

        var rule = DeviceRule.Create(
            tenantId,
            request.Name,
            request.Description,
            maxPriority + 1,
            request.AssetType,
            definition.Filter!,
            definition.Operations);
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

        var definition = await _definitionService.ParseAndValidateAsync(
            tenantId,
            request.AssetType,
            request.FilterDefinition,
            request.Operations,
            ct);
        if (!definition.IsSuccess)
            return BadRequest(new ProblemDetails { Title = definition.Error });

        rule.Update(request.Name, request.Description, request.Enabled, request.AssetType, definition.Filter!, definition.Operations);
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
        try
        {
            await _cleanupService.ResetDeletedRuleEffectsAsync(tenantId, rule, operations, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }

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

        if (!DeviceRuleAssetTypes.IsSupported(request.AssetType))
            return BadRequest(new ProblemDetails { Title = "Unsupported asset type." });

        var filterResult = _definitionService.ParseFilter(request.FilterDefinition);
        if (!filterResult.IsSuccess)
            return BadRequest(new ProblemDetails { Title = filterResult.Error });

        try
        {
            var result = await _previewService.PreviewAsync(tenantId, request.AssetType, filterResult.Filter!, ct);
            return Ok(new DeviceRulePreviewDto(
                result.Count,
                result.Samples.Select(s => new DevicePreviewItemDto(s.Id, s.Name)).ToList()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
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
        rule.AssetType,
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

}
