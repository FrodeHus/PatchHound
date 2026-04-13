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
    private readonly RiskRefreshService _riskRefreshService;
    private readonly ITenantContext _tenantContext;

    public SecurityProfilesController(
        PatchHoundDbContext dbContext,
        RiskRefreshService riskRefreshService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _riskRefreshService = riskRefreshService;
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
                profile.ModifiedAttackVector.ToString(),
                profile.ModifiedAttackComplexity.ToString(),
                profile.ModifiedPrivilegesRequired.ToString(),
                profile.ModifiedUserInteraction.ToString(),
                profile.ModifiedScope.ToString(),
                profile.ModifiedConfidentialityImpact.ToString(),
                profile.ModifiedIntegrityImpact.ToString(),
                profile.ModifiedAvailabilityImpact.ToString(),
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
            parsed.AvailabilityRequirement,
            parsed.ModifiedAttackVector,
            parsed.ModifiedAttackComplexity,
            parsed.ModifiedPrivilegesRequired,
            parsed.ModifiedUserInteraction,
            parsed.ModifiedScope,
            parsed.ModifiedConfidentialityImpact,
            parsed.ModifiedIntegrityImpact,
            parsed.ModifiedAvailabilityImpact
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
            parsed.AvailabilityRequirement,
            parsed.ModifiedAttackVector,
            parsed.ModifiedAttackComplexity,
            parsed.ModifiedPrivilegesRequired,
            parsed.ModifiedUserInteraction,
            parsed.ModifiedScope,
            parsed.ModifiedConfidentialityImpact,
            parsed.ModifiedIntegrityImpact,
            parsed.ModifiedAvailabilityImpact
        );

        var affectedAssetIds = await _dbContext
            .Assets.Where(asset => asset.SecurityProfileId == id)
            .Select(asset => asset.Id)
            .ToListAsync(ct);

        foreach (var assetId in affectedAssetIds)
        {
            // phase-5: re-introduce per-asset assessment recalculation via DeviceVulnerabilityExposure
            _ = assetId;
        }

        await _riskRefreshService.RefreshForAssetsAsync(
            profile.TenantId,
            affectedAssetIds,
            recalculateAssessments: false,
            ct
        );

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var profile = await _dbContext.AssetSecurityProfiles.FirstOrDefaultAsync(
            item => item.Id == id,
            ct
        );
        if (profile is null)
            return NotFound();

        if (!_tenantContext.HasAccessToTenant(profile.TenantId))
            return Forbid();

        var assignedAssetCount = await _dbContext
            .Assets.CountAsync(asset => asset.SecurityProfileId == id, ct);

        if (assignedAssetCount > 0)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title =
                        $"Cannot delete this profile because it is assigned to {assignedAssetCount} asset(s). Unassign them first.",
                }
            );
        }

        _dbContext.AssetSecurityProfiles.Remove(profile);
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
            profile.ModifiedAttackVector.ToString(),
            profile.ModifiedAttackComplexity.ToString(),
            profile.ModifiedPrivilegesRequired.ToString(),
            profile.ModifiedUserInteraction.ToString(),
            profile.ModifiedScope.ToString(),
            profile.ModifiedConfidentialityImpact.ToString(),
            profile.ModifiedIntegrityImpact.ToString(),
            profile.ModifiedAvailabilityImpact.ToString(),
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
            request.ModifiedAttackVector,
            request.ModifiedAttackComplexity,
            request.ModifiedPrivilegesRequired,
            request.ModifiedUserInteraction,
            request.ModifiedScope,
            request.ModifiedConfidentialityImpact,
            request.ModifiedIntegrityImpact,
            request.ModifiedAvailabilityImpact,
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
            request.ModifiedAttackVector,
            request.ModifiedAttackComplexity,
            request.ModifiedPrivilegesRequired,
            request.ModifiedUserInteraction,
            request.ModifiedScope,
            request.ModifiedConfidentialityImpact,
            request.ModifiedIntegrityImpact,
            request.ModifiedAvailabilityImpact,
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
        string modifiedAttackVector,
        string modifiedAttackComplexity,
        string modifiedPrivilegesRequired,
        string modifiedUserInteraction,
        string modifiedScope,
        string modifiedConfidentialityImpact,
        string modifiedIntegrityImpact,
        string modifiedAvailabilityImpact,
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

        if (
            !Enum.TryParse<CvssModifiedAttackVector>(modifiedAttackVector, out var parsedModifiedAttackVector)
            || !Enum.TryParse<CvssModifiedAttackComplexity>(modifiedAttackComplexity, out var parsedModifiedAttackComplexity)
            || !Enum.TryParse<CvssModifiedPrivilegesRequired>(modifiedPrivilegesRequired, out var parsedModifiedPrivilegesRequired)
            || !Enum.TryParse<CvssModifiedUserInteraction>(modifiedUserInteraction, out var parsedModifiedUserInteraction)
            || !Enum.TryParse<CvssModifiedScope>(modifiedScope, out var parsedModifiedScope)
            || !Enum.TryParse<CvssModifiedImpact>(modifiedConfidentialityImpact, out var parsedModifiedConfidentialityImpact)
            || !Enum.TryParse<CvssModifiedImpact>(modifiedIntegrityImpact, out var parsedModifiedIntegrityImpact)
            || !Enum.TryParse<CvssModifiedImpact>(modifiedAvailabilityImpact, out var parsedModifiedAvailabilityImpact)
        )
        {
            error = "Invalid CVSS environmental metric override.";
            return false;
        }

        parsed = new ParsedRequest(
            parsedEnvironmentClass,
            parsedReachability,
            parsedConfidentiality,
            parsedIntegrity,
            parsedAvailability,
            parsedModifiedAttackVector,
            parsedModifiedAttackComplexity,
            parsedModifiedPrivilegesRequired,
            parsedModifiedUserInteraction,
            parsedModifiedScope,
            parsedModifiedConfidentialityImpact,
            parsedModifiedIntegrityImpact,
            parsedModifiedAvailabilityImpact
        );
        return true;
    }

    private readonly record struct ParsedRequest(
        EnvironmentClass EnvironmentClass,
        InternetReachability InternetReachability,
        SecurityRequirementLevel ConfidentialityRequirement,
        SecurityRequirementLevel IntegrityRequirement,
        SecurityRequirementLevel AvailabilityRequirement,
        CvssModifiedAttackVector ModifiedAttackVector,
        CvssModifiedAttackComplexity ModifiedAttackComplexity,
        CvssModifiedPrivilegesRequired ModifiedPrivilegesRequired,
        CvssModifiedUserInteraction ModifiedUserInteraction,
        CvssModifiedScope ModifiedScope,
        CvssModifiedImpact ModifiedConfidentialityImpact,
        CvssModifiedImpact ModifiedIntegrityImpact,
        CvssModifiedImpact ModifiedAvailabilityImpact
    );
}
