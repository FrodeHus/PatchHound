using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.AdvancedTools;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/advanced-tools")]
[Authorize]
public class AdvancedToolsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly AdvancedToolExecutionService _executionService;
    private readonly AuditLogWriter _auditLogWriter;

    public AdvancedToolsController(
        PatchHoundDbContext dbContext,
        ITenantContext tenantContext,
        AdvancedToolExecutionService executionService,
        AuditLogWriter auditLogWriter
    )
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _executionService = executionService;
        _auditLogWriter = auditLogWriter;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<AdvancedToolCatalogDto>> List(
        [FromQuery] string? assetType,
        CancellationToken ct
    )
    {
        var tools = await _dbContext.AdvancedTools.AsNoTracking()
            .OrderBy(tool => tool.Name)
            .ToListAsync(ct);

        var filteredTools = string.IsNullOrWhiteSpace(assetType)
            ? tools
            : tools.Where(tool =>
                    tool.GetSupportedAssetTypes().Any(type =>
                        string.Equals(type.ToString(), assetType, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToList();

        return Ok(
            new AdvancedToolCatalogDto(
                filteredTools.Select(MapTool).ToList(),
                AdvancedToolTemplateRenderer.GetAllowedDeviceParameters()
                    .Select(parameter => new AdvancedToolParameterDefinitionDto(
                        parameter.Key,
                        parameter.Value
                    ))
                    .ToList()
            )
        );
    }

    [HttpPost]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<AdvancedToolDto>> Create(
        [FromBody] SaveAdvancedToolRequest request,
        CancellationToken ct
    )
    {
        var supportedAssetTypes = ParseSupportedAssetTypes(request.SupportedAssetTypes);
        AdvancedToolTemplateRenderer.ValidateAllowedParameters(request.KqlQuery);

        var tool = AdvancedTool.Create(
            request.Name,
            request.Description,
            supportedAssetTypes,
            request.KqlQuery,
            request.Enabled
        );

        await _dbContext.AdvancedTools.AddAsync(tool, ct);
        await _auditLogWriter.WriteAsync(
            Guid.Empty,
            nameof(AdvancedTool),
            tool.Id,
            AuditAction.Created,
            null,
            new
            {
                tool.Name,
                tool.Description,
                SupportedAssetTypes = supportedAssetTypes.Select(value => value.ToString()).ToArray(),
                tool.KqlQuery,
                tool.Enabled,
            },
            ct
        );
        await _dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { id = tool.Id }, MapTool(tool));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] SaveAdvancedToolRequest request,
        CancellationToken ct
    )
    {
        var tool = await _dbContext.AdvancedTools.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (tool is null)
        {
            return NotFound(new ProblemDetails { Title = "Advanced tool was not found." });
        }

        var before = MapTool(tool);
        var supportedAssetTypes = ParseSupportedAssetTypes(request.SupportedAssetTypes);
        AdvancedToolTemplateRenderer.ValidateAllowedParameters(request.KqlQuery);

        tool.Update(
            request.Name,
            request.Description,
            supportedAssetTypes,
            request.KqlQuery,
            request.Enabled
        );

        await _auditLogWriter.WriteAsync(
            Guid.Empty,
            nameof(AdvancedTool),
            tool.Id,
            AuditAction.Updated,
            before,
            MapTool(tool),
            ct
        );
        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tool = await _dbContext.AdvancedTools.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (tool is null)
        {
            return NotFound(new ProblemDetails { Title = "Advanced tool was not found." });
        }

        var before = MapTool(tool);
        _dbContext.AdvancedTools.Remove(tool);
        await _auditLogWriter.WriteAsync(
            Guid.Empty,
            nameof(AdvancedTool),
            tool.Id,
            AuditAction.Deleted,
            before,
            null,
            ct
        );
        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("test")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<AdvancedToolExecutionResultDto>> Test(
        [FromBody] AdvancedToolTestRequest request,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        try
        {
            var result = await _executionService.TestQueryAsync(
                tenantId,
                request.KqlQuery,
                request.SampleParameters,
                ct
            );

            return Ok(
                new AdvancedToolExecutionResultDto(
                    result.Schema.Select(column => new AdvancedToolSchemaColumnDto(column.Name, column.Type)).ToList(),
                    result.Results,
                    AdvancedToolTemplateRenderer.Render(request.KqlQuery, request.SampleParameters)
                )
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
    }

    [HttpPost("assets/{assetId:guid}/run")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<AdvancedToolAssetExecutionResultDto>> RunForAsset(
        Guid assetId,
        [FromBody] RunAdvancedToolForAssetRequest request,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var query = request.KqlQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            if (request.ToolId is not Guid toolId)
            {
                return BadRequest(new ProblemDetails { Title = "A tool or query is required." });
            }

            query = await _dbContext.AdvancedTools.AsNoTracking()
                .Where(tool => tool.Id == toolId && tool.Enabled)
                .Select(tool => tool.KqlQuery)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(query))
            {
                return NotFound(new ProblemDetails { Title = "Advanced tool was not found." });
            }
        }

        try
        {
            var results = await _executionService.RunForAssetAsync(
                tenantId,
                assetId,
                query,
                request.UseAllOpenVulnerabilities,
                request.VulnerabilityIds,
                ct
            );

            return Ok(
                new AdvancedToolAssetExecutionResultDto(
                    results.Select(result => new AdvancedToolRenderedQueryDto(
                        result.Label,
                        result.VulnerabilityId,
                        result.VulnerabilityExternalId,
                        result.Query,
                        result.Result.Schema.Select(column => new AdvancedToolSchemaColumnDto(column.Name, column.Type)).ToList(),
                        result.Result.Results
                    )).ToList()
                )
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
    }

    private static AdvancedToolDto MapTool(AdvancedTool tool) =>
        new(
            tool.Id,
            tool.Name,
            tool.Description,
            tool.GetSupportedAssetTypes().Select(type => type.ToString()).ToList(),
            tool.KqlQuery,
            tool.Enabled,
            tool.CreatedAt,
            tool.UpdatedAt
        );

    private static IReadOnlyList<AdvancedToolAssetType> ParseSupportedAssetTypes(
        IReadOnlyList<string> supportedAssetTypes
    )
    {
        var parsed = supportedAssetTypes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value =>
            {
                if (
                    !Enum.TryParse<AdvancedToolAssetType>(value, true, out var assetType)
                )
                {
                    throw new InvalidOperationException(
                        $"Unsupported asset type '{value}'."
                    );
                }

                return assetType;
            })
            .ToList();

        if (parsed.Count == 0)
        {
            throw new InvalidOperationException("At least one supported asset type is required.");
        }

        return parsed;
    }
}
