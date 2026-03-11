using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.SecurityProfiles;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/security-profiles")]
[Authorize]
public class SecurityProfilesController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly VulnerabilityAssessmentService _assessmentService;
    private readonly ITenantContext _tenantContext;

    public SecurityProfilesController(
        PatchHoundDbContext dbContext,
        VulnerabilityAssessmentService assessmentService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _assessmentService = assessmentService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<PagedResponse<SecurityProfileDto>>> List(
        [FromQuery] Guid? tenantId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.AssetSecurityProfiles.AsNoTracking().AsQueryable();
        if (tenantId.HasValue)
        {
            if (!_tenantContext.HasAccessToTenant(tenantId.Value))
                return Forbid();
            query = query.Where(profile => profile.TenantId == tenantId.Value);
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(profile => profile.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(profile => new SecurityProfileDto(
                profile.Id,
                profile.TenantId,
                profile.Name,
                profile.Description,
                profile.EnvironmentClass.ToString(),
                profile.InternetReachability.ToString(),
                profile.ConfidentialityRequirement.ToString(),
                profile.IntegrityRequirement.ToString(),
                profile.AvailabilityRequirement.ToString(),
                profile.UpdatedAt
            ))
            .ToListAsync(ct);

        return Ok(
            new PagedResponse<SecurityProfileDto>(
                items,
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpPost]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<SecurityProfileDto>> Create(
        [FromBody] CreateSecurityProfileRequest request,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(
                new ProblemDetails
                {
                    Title = "An active tenant must be selected before creating a security profile."
                }
            );

        if (!_tenantContext.HasAccessToTenant(tenantId))
            return Forbid();

        if (!TryParseRequest(request, out var parsed, out var error))
        {
            return BadRequest(new ProblemDetails { Title = error });
        }

        var profile = AssetSecurityProfile.Create(
            tenantId,
            request.Name.Trim(),
            request.Description?.Trim(),
            parsed.EnvironmentClass,
            parsed.InternetReachability,
            parsed.ConfidentialityRequirement,
            parsed.IntegrityRequirement,
            parsed.AvailabilityRequirement
        );

        _dbContext.AssetSecurityProfiles.Add(profile);
        await _dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { tenantId = profile.TenantId }, ToDto(profile));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateSecurityProfileRequest request,
        CancellationToken ct
    )
    {
        if (!TryParseRequest(request, out var parsed, out var error))
        {
            return BadRequest(new ProblemDetails { Title = error });
        }

        var profile = await _dbContext.AssetSecurityProfiles.FirstOrDefaultAsync(
            item => item.Id == id,
            ct
        );
        if (profile is null)
        {
            return NotFound();
        }

        if (!_tenantContext.HasAccessToTenant(profile.TenantId))
            return Forbid();

        profile.Update(
            request.Name.Trim(),
            request.Description?.Trim(),
            parsed.EnvironmentClass,
            parsed.InternetReachability,
            parsed.ConfidentialityRequirement,
            parsed.IntegrityRequirement,
            parsed.AvailabilityRequirement
        );

        var affectedAssetIds = await _dbContext
            .Assets.Where(asset => asset.SecurityProfileId == id)
            .Select(asset => asset.Id)
            .ToListAsync(ct);

        foreach (var assetId in affectedAssetIds)
        {
            await _assessmentService.RecalculateForAssetAsync(assetId, ct);
        }

        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    private static SecurityProfileDto ToDto(AssetSecurityProfile profile) =>
        new(
            profile.Id,
            profile.TenantId,
            profile.Name,
            profile.Description,
            profile.EnvironmentClass.ToString(),
            profile.InternetReachability.ToString(),
            profile.ConfidentialityRequirement.ToString(),
            profile.IntegrityRequirement.ToString(),
            profile.AvailabilityRequirement.ToString(),
            profile.UpdatedAt
        );

    private static bool TryParseRequest(
        CreateSecurityProfileRequest request,
        out ParsedRequest parsed,
        out string error
    )
    {
        return TryParseValues(
            request.EnvironmentClass,
            request.InternetReachability,
            request.ConfidentialityRequirement,
            request.IntegrityRequirement,
            request.AvailabilityRequirement,
            out parsed,
            out error
        );
    }

    private static bool TryParseRequest(
        UpdateSecurityProfileRequest request,
        out ParsedRequest parsed,
        out string error
    )
    {
        return TryParseValues(
            request.EnvironmentClass,
            request.InternetReachability,
            request.ConfidentialityRequirement,
            request.IntegrityRequirement,
            request.AvailabilityRequirement,
            out parsed,
            out error
        );
    }

    private static bool TryParseValues(
        string environmentClass,
        string internetReachability,
        string confidentialityRequirement,
        string integrityRequirement,
        string availabilityRequirement,
        out ParsedRequest parsed,
        out string error
    )
    {
        parsed = default;
        error = string.Empty;

        if (!Enum.TryParse<EnvironmentClass>(environmentClass, out var parsedEnvironmentClass))
        {
            error = "Invalid environment class.";
            return false;
        }

        if (!Enum.TryParse<InternetReachability>(internetReachability, out var parsedReachability))
        {
            error = "Invalid internet reachability.";
            return false;
        }

        if (
            !Enum.TryParse<SecurityRequirementLevel>(
                confidentialityRequirement,
                out var parsedConfidentiality
            )
            || !Enum.TryParse<SecurityRequirementLevel>(
                integrityRequirement,
                out var parsedIntegrity
            )
            || !Enum.TryParse<SecurityRequirementLevel>(
                availabilityRequirement,
                out var parsedAvailability
            )
        )
        {
            error = "Invalid security requirement level.";
            return false;
        }

        parsed = new ParsedRequest(
            parsedEnvironmentClass,
            parsedReachability,
            parsedConfidentiality,
            parsedIntegrity,
            parsedAvailability
        );
        return true;
    }

    private readonly record struct ParsedRequest(
        EnvironmentClass EnvironmentClass,
        InternetReachability InternetReachability,
        SecurityRequirementLevel ConfidentialityRequirement,
        SecurityRequirementLevel IntegrityRequirement,
        SecurityRequirementLevel AvailabilityRequirement
    );
}
