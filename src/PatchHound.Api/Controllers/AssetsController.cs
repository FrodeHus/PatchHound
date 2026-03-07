using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Assets;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/assets")]
[Authorize]
public class AssetsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly AssetService _assetService;

    public AssetsController(PatchHoundDbContext dbContext, AssetService assetService)
    {
        _dbContext = dbContext;
        _assetService = assetService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<AssetDto>>> List(
        [FromQuery] AssetFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.Assets.AsNoTracking().AsQueryable();

        if (
            !string.IsNullOrEmpty(filter.AssetType)
            && Enum.TryParse<AssetType>(filter.AssetType, out var assetType)
        )
            query = query.Where(a => a.AssetType == assetType);
        if (
            !string.IsNullOrEmpty(filter.OwnerType)
            && Enum.TryParse<OwnerType>(filter.OwnerType, out var ownerType)
        )
            query = query.Where(a => a.OwnerType == ownerType);
        if (filter.OwnerId.HasValue)
            query = query.Where(a =>
                a.OwnerUserId == filter.OwnerId.Value || a.OwnerTeamId == filter.OwnerId.Value
            );
        if (filter.TenantId.HasValue)
            query = query.Where(a => a.TenantId == filter.TenantId.Value);
        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(a =>
                a.Name.Contains(filter.Search) || a.ExternalId.Contains(filter.Search)
            );

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(a => a.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(a => new AssetDto(
                a.Id,
                a.ExternalId,
                a.Name,
                a.AssetType.ToString(),
                a.Criticality.ToString(),
                a.OwnerType.ToString(),
                _dbContext.VulnerabilityAssets.Count(va => va.AssetId == a.Id)
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<AssetDto>(items, totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<AssetDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var asset = await _dbContext.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
            return NotFound();

        var vulnerabilities = await _dbContext
            .VulnerabilityAssets.AsNoTracking()
            .Where(va => va.AssetId == id)
            .Join(
                _dbContext.Vulnerabilities,
                va => va.VulnerabilityId,
                v => v.Id,
                (va, v) =>
                    new AssetVulnerabilityDto(
                        v.Id,
                        v.ExternalId,
                        v.Title,
                        v.VendorSeverity.ToString(),
                        va.Status.ToString(),
                        va.DetectedDate,
                        va.ResolvedDate
                    )
            )
            .ToListAsync(ct);

        return Ok(
            new AssetDetailDto(
                asset.Id,
                asset.ExternalId,
                asset.Name,
                asset.Description,
                asset.AssetType.ToString(),
                asset.Criticality.ToString(),
                asset.OwnerType.ToString(),
                asset.OwnerUserId,
                asset.OwnerTeamId,
                asset.FallbackTeamId,
                asset.Metadata,
                vulnerabilities
            )
        );
    }

    [HttpPut("{id:guid}/owner")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<IActionResult> AssignOwner(
        Guid id,
        [FromBody] AssignOwnerRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<OwnerType>(request.OwnerType, out var ownerType))
            return BadRequest(new ProblemDetails { Title = "Invalid owner type" });

        var result = ownerType switch
        {
            OwnerType.User => await _assetService.AssignOwnerAsync(id, request.OwnerId, ct),
            OwnerType.Team => await _assetService.AssignTeamOwnerAsync(id, request.OwnerId, ct),
            _ => throw new ArgumentOutOfRangeException(),
        };

        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        return NoContent();
    }

    [HttpPut("{id:guid}/criticality")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<IActionResult> SetCriticality(
        Guid id,
        [FromBody] SetCriticalityRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<Criticality>(request.Criticality, out var criticality))
            return BadRequest(new ProblemDetails { Title = "Invalid criticality value" });

        var result = await _assetService.SetCriticalityAsync(id, criticality, ct);
        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        return NoContent();
    }

    [HttpPost("bulk-assign")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<ActionResult<BulkAssignResponse>> BulkAssign(
        [FromBody] BulkAssignRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<OwnerType>(request.OwnerType, out var ownerType))
            return BadRequest(new ProblemDetails { Title = "Invalid owner type" });

        var result = await _assetService.BulkAssignOwnerAsync(
            request.AssetIds,
            request.OwnerId,
            ownerType,
            ct
        );
        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return Ok(new BulkAssignResponse(result.Value));
    }
}
