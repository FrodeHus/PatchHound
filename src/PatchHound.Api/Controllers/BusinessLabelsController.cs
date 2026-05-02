using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Assets;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/business-labels")]
[Authorize]
public class BusinessLabelsController(
    PatchHoundDbContext dbContext,
    ITenantContext tenantContext
) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<IReadOnlyList<BusinessLabelDto>>> List(CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var items = await dbContext.BusinessLabels.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .OrderBy(item => item.Name)
            .ToListAsync(ct);

        var dtos = items.Select(ToDto).ToList();

        return Ok(dtos);
    }

    [HttpPost]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<BusinessLabelDto>> Create(
        [FromBody] SaveBusinessLabelRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new ProblemDetails { Title = "Name is required." });

        if (!TryParseWeightCategory(request.WeightCategory, out var weightCategory))
            return BadRequest(new ProblemDetails { Title = "Invalid weight category." });

        var exists = await dbContext.BusinessLabels.AnyAsync(
            item => item.TenantId == tenantId && item.Name == request.Name.Trim(),
            ct
        );
        if (exists)
            return BadRequest(new ProblemDetails { Title = "A business label with that name already exists." });

        var label = BusinessLabel.Create(tenantId, request.Name, request.Description, request.Color, weightCategory);
        if (!request.IsActive)
        {
            label.Update(label.Name, label.Description, label.Color, false, weightCategory);
        }

        await dbContext.BusinessLabels.AddAsync(label, ct);
        await dbContext.SaveChangesAsync(ct);

        return Ok(ToDto(label));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<BusinessLabelDto>> Update(
        Guid id,
        [FromBody] SaveBusinessLabelRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var label = await dbContext.BusinessLabels
            .FirstOrDefaultAsync(item => item.Id == id && item.TenantId == tenantId, ct);
        if (label is null)
            return NotFound(new ProblemDetails { Title = "Business label not found." });

        if (!TryParseWeightCategory(request.WeightCategory, out var weightCategory))
            return BadRequest(new ProblemDetails { Title = "Invalid weight category." });

        var normalizedName = request.Name.Trim();
        var nameInUse = await dbContext.BusinessLabels.AnyAsync(
            item => item.TenantId == tenantId && item.Id != id && item.Name == normalizedName,
            ct
        );
        if (nameInUse)
            return BadRequest(new ProblemDetails { Title = "A business label with that name already exists." });

        label.Update(request.Name, request.Description, request.Color, request.IsActive, weightCategory);
        await dbContext.SaveChangesAsync(ct);

        return Ok(ToDto(label));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var label = await dbContext.BusinessLabels
            .FirstOrDefaultAsync(item => item.Id == id && item.TenantId == tenantId, ct);
        if (label is null)
            return NotFound(new ProblemDetails { Title = "Business label not found." });

        dbContext.BusinessLabels.Remove(label);
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    private static BusinessLabelDto ToDto(BusinessLabel item) =>
        new(
            item.Id,
            item.Name,
            item.Description,
            item.Color,
            item.IsActive,
            item.WeightCategory.ToString(),
            item.RiskWeight,
            item.CreatedAt,
            item.UpdatedAt
        );

    private static bool TryParseWeightCategory(string? value, out BusinessLabelWeightCategory category)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            category = BusinessLabelWeightCategory.Normal;
            return true;
        }
        return Enum.TryParse(value, ignoreCase: false, out category)
            && Enum.IsDefined(typeof(BusinessLabelWeightCategory), category);
    }
}
