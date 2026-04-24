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
    private const string SoftwareAssetType = "Software";
    private const string ApplicationAssetType = "Application";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IDeviceRuleEvaluationService _evaluationService;
    private readonly DeviceRuleFilterBuilder _filterBuilder;
    private readonly SoftwareRuleFilterBuilder _softwareFilterBuilder;
    private readonly CloudApplicationRuleFilterBuilder _cloudApplicationFilterBuilder;
    private readonly RiskRefreshService _riskRefreshService;

    public DeviceRulesController(
        PatchHoundDbContext dbContext,
        ITenantContext tenantContext,
        IDeviceRuleEvaluationService evaluationService,
        DeviceRuleFilterBuilder filterBuilder,
        SoftwareRuleFilterBuilder softwareFilterBuilder,
        RiskRefreshService riskRefreshService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _evaluationService = evaluationService;
        _filterBuilder = filterBuilder;
        _softwareFilterBuilder = softwareFilterBuilder;
        _cloudApplicationFilterBuilder = new CloudApplicationRuleFilterBuilder();
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
            return BadRequest(new ProblemDetails { Title = "Unsupported asset type." });

        if (filter is null || operations is null)
            return BadRequest(new ProblemDetails { Title = "Invalid filter or operations JSON." });

        var validationError = ValidateOperations(request.AssetType, operations);
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
            return BadRequest(new ProblemDetails { Title = "Unsupported asset type." });

        if (filter is null || operations is null)
            return BadRequest(new ProblemDetails { Title = "Invalid filter or operations JSON." });

        var validationError = ValidateOperations(request.AssetType, operations);
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
        await ResetDeletedRuleEffectsAsync(tenantId, rule, operations, ct);
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
            return BadRequest(new ProblemDetails { Title = "Unsupported asset type." });

        var filter = DeserializeFilter(request.FilterDefinition);
        if (filter is null)
            return BadRequest(new ProblemDetails { Title = "Invalid filter JSON." });

        if (string.Equals(request.AssetType, DeviceAssetType, StringComparison.OrdinalIgnoreCase))
        {
            var result = await _evaluationService.PreviewFilterAsync(tenantId, filter, ct);
            return Ok(new DeviceRulePreviewDto(
                result.Count,
                result.Samples.Select(s => new DevicePreviewItemDto(s.Id, s.Name)).ToList()));
        }

        if (string.Equals(request.AssetType, ApplicationAssetType, StringComparison.OrdinalIgnoreCase))
        {
            (int Count, List<(Guid Id, string Name)> Samples) applicationPreview;
            try
            {
                applicationPreview = await PreviewApplicationFilterAsync(tenantId, filter, ct);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ProblemDetails { Title = ex.Message });
            }

            return Ok(new DeviceRulePreviewDto(
                applicationPreview.Count,
                applicationPreview.Samples.Select(s => new DevicePreviewItemDto(s.Id, s.Name)).ToList()));
        }

        (int Count, List<(Guid Id, string Name)> Samples) softwarePreview;
        try
        {
            softwarePreview = await PreviewSoftwareFilterAsync(tenantId, filter, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }

        return Ok(new DeviceRulePreviewDto(
            softwarePreview.Count,
            softwarePreview.Samples.Select(s => new DevicePreviewItemDto(s.Id, s.Name)).ToList()));
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

    private static bool IsSupportedAssetType(string assetType) =>
        string.Equals(assetType, DeviceAssetType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(assetType, SoftwareAssetType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(assetType, ApplicationAssetType, StringComparison.OrdinalIgnoreCase);

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

    private static string? ValidateOperations(string assetType, IReadOnlyList<AssetRuleOperation> operations)
    {
        if (string.Equals(assetType, SoftwareAssetType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetType, ApplicationAssetType, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var operation in operations)
            {
                switch (operation.Type)
                {
                    case "AssignOwnerTeam":
                        if (!operation.Parameters.TryGetValue("teamId", out var softwareTeamId)
                            || !Guid.TryParse(softwareTeamId, out _))
                        {
                            return "AssignOwnerTeam requires a valid teamId.";
                        }
                        break;
                    default:
                        return string.Equals(assetType, SoftwareAssetType, StringComparison.OrdinalIgnoreCase)
                            ? $"Unknown software rule operation type: {operation.Type}."
                            : $"Unknown application rule operation type: {operation.Type}.";
                }
            }

            return null;
        }

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

                case "AssignOwnerTeam":
                    if (!operation.Parameters.TryGetValue("teamId", out var ownerTeamId)
                        || !Guid.TryParse(ownerTeamId, out _))
                    {
                        return "AssignOwnerTeam requires a valid teamId.";
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

    private async Task<(int Count, List<(Guid Id, string Name)> Samples)> PreviewSoftwareFilterAsync(
        Guid tenantId,
        FilterNode filter,
        CancellationToken ct)
    {
        var predicate = _softwareFilterBuilder.Build(filter);
        var query = _dbContext.SoftwareTenantRecords
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Where(predicate);

        var count = await query.CountAsync(ct);
        var samples = await query
            .OrderBy(item => item.SoftwareProduct.Name)
            .ThenBy(item => item.SoftwareProduct.Vendor)
            .Take(5)
            .Select(item => new ValueTuple<Guid, string>(
                item.Id,
                string.IsNullOrWhiteSpace(item.SoftwareProduct.Vendor)
                    ? item.SoftwareProduct.Name
                    : $"{item.SoftwareProduct.Vendor} {item.SoftwareProduct.Name}"))
            .ToListAsync(ct);

        return (count, samples);
    }

    private async Task<(int Count, List<(Guid Id, string Name)> Samples)> PreviewApplicationFilterAsync(
        Guid tenantId,
        FilterNode filter,
        CancellationToken ct)
    {
        var predicate = _cloudApplicationFilterBuilder.Build(filter);
        var query = _dbContext.CloudApplications
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.ActiveInTenant)
            .Where(predicate);

        var count = await query.CountAsync(ct);
        var samples = await query
            .OrderBy(item => item.Name)
            .Take(5)
            .Select(item => new ValueTuple<Guid, string>(item.Id, item.Name))
            .ToListAsync(ct);

        return (count, samples);
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
                case "AssignOwnerTeam":
                    if (operation.Parameters.TryGetValue("teamId", out var teamId)
                        && Guid.TryParse(teamId, out var parsedTeamId))
                    {
                        var exists = await _dbContext.Teams
                            .AnyAsync(team => team.TenantId == tenantId && team.Id == parsedTeamId, ct);
                        if (!exists)
                            return $"{operation.Type} references a team that does not belong to the active tenant.";
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

    private async Task<List<Guid>> GetMatchingSoftwareIdsAsync(
        DeviceRule rule,
        Guid tenantId,
        CancellationToken ct)
    {
        try
        {
            if (string.Equals(rule.AssetType, ApplicationAssetType, StringComparison.OrdinalIgnoreCase))
            {
                var applicationPredicate = _cloudApplicationFilterBuilder.Build(rule.ParseFilter());
                return await _dbContext.CloudApplications
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(item => item.TenantId == tenantId && item.ActiveInTenant)
                    .Where(applicationPredicate)
                    .Select(item => item.Id)
                    .ToListAsync(ct);
            }

            var predicate = _softwareFilterBuilder.Build(rule.ParseFilter());
            return await _dbContext.SoftwareTenantRecords
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId)
                .Where(predicate)
                .Select(item => item.Id)
                .ToListAsync(ct);
        }
        catch
        {
            return [];
        }
    }

    private async Task ResetDeletedRuleEffectsAsync(
        Guid tenantId,
        DeviceRule deletedRule,
        IReadOnlyCollection<AssetRuleOperation> operations,
        CancellationToken ct)
    {
        if (operations.Count == 0)
            return;

        if (string.Equals(deletedRule.AssetType, ApplicationAssetType, StringComparison.OrdinalIgnoreCase))
        {
            var applicationIds = await GetMatchingSoftwareIdsAsync(deletedRule, tenantId, ct);
            foreach (var operation in operations)
            {
                if (operation.Type != "AssignOwnerTeam" || applicationIds.Count == 0)
                {
                    continue;
                }

                var applications = await _dbContext.CloudApplications
                    .IgnoreQueryFilters()
                    .Where(item =>
                        item.TenantId == tenantId
                        && applicationIds.Contains(item.Id)
                        && item.OwnerTeamRuleId == deletedRule.Id)
                    .ToListAsync(ct);

                foreach (var application in applications)
                {
                    application.ClearRuleAssignedOwnerTeam(deletedRule.Id);
                }

                if (applications.Count > 0)
                {
                    await _dbContext.SaveChangesAsync(ct);
                }
            }

            return;
        }

        if (string.Equals(deletedRule.AssetType, SoftwareAssetType, StringComparison.OrdinalIgnoreCase))
        {
            var softwareIds = await GetMatchingSoftwareIdsAsync(deletedRule, tenantId, ct);
            foreach (var operation in operations)
            {
                if (operation.Type != "AssignOwnerTeam" || softwareIds.Count == 0)
                {
                    continue;
                }

                var records = await _dbContext.SoftwareTenantRecords
                    .Where(item =>
                        item.TenantId == tenantId
                        && softwareIds.Contains(item.Id)
                        && item.OwnerTeamRuleId == deletedRule.Id)
                    .ToListAsync(ct);

                foreach (var record in records)
                {
                    record.ClearRuleAssignedOwnerTeam(deletedRule.Id);
                }

                if (records.Count > 0)
                {
                    await _dbContext.SaveChangesAsync(ct);
                }
            }

            return;
        }

        var deviceIds = await GetMatchingDeviceIdsAsync(deletedRule, tenantId, ct);

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
                                && device.SecurityProfileRuleId == deletedRule.Id)
                            .ToListAsync(ct);

                        foreach (var device in devices)
                            device.ClearRuleAssignedSecurityProfile(deletedRule.Id);

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
                                && device.FallbackTeamRuleId == deletedRule.Id)
                            .ToListAsync(ct);

                        foreach (var device in devices)
                            device.ClearRuleAssignedFallbackTeam(deletedRule.Id);

                        if (devices.Count > 0)
                            await _dbContext.SaveChangesAsync(ct);
                    }
                    break;

                case "AssignOwnerTeam":
                    if (operation.Parameters.TryGetValue("teamId", out _) && deviceIds.Count > 0)
                    {
                        var devices = await _dbContext.Devices
                            .Where(device =>
                                device.TenantId == tenantId
                                && deviceIds.Contains(device.Id)
                                && device.OwnerTeamRuleId == deletedRule.Id)
                            .ToListAsync(ct);

                        foreach (var device in devices)
                            device.ClearRuleAssignedOwnerTeam(deletedRule.Id);

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
                            && device.CriticalityRuleId == deletedRule.Id)
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
                                && link.AssignedByRuleId == deletedRule.Id
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
