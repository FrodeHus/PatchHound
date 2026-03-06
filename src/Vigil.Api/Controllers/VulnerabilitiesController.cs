using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vigil.Api.Auth;
using Vigil.Api.Models;
using Vigil.Api.Models.Vulnerabilities;
using Vigil.Core.Enums;
using Vigil.Core.Services;
using Vigil.Infrastructure.Data;

namespace Vigil.Api.Controllers;

[ApiController]
[Route("api/vulnerabilities")]
[Authorize]
public class VulnerabilitiesController : ControllerBase
{
    private readonly VigilDbContext _dbContext;
    private readonly VulnerabilityService _vulnerabilityService;

    public VulnerabilitiesController(VigilDbContext dbContext, VulnerabilityService vulnerabilityService)
    {
        _dbContext = dbContext;
        _vulnerabilityService = vulnerabilityService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<VulnerabilityDto>>> List(
        [FromQuery] VulnerabilityFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        var query = _dbContext.Vulnerabilities.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(filter.Severity) && Enum.TryParse<Severity>(filter.Severity, out var severity))
            query = query.Where(v => v.VendorSeverity == severity);
        if (!string.IsNullOrEmpty(filter.Status) && Enum.TryParse<VulnerabilityStatus>(filter.Status, out var status))
            query = query.Where(v => v.Status == status);
        if (!string.IsNullOrEmpty(filter.Source))
            query = query.Where(v => v.Source == filter.Source);
        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(v => v.Title.Contains(filter.Search) || v.ExternalId.Contains(filter.Search));
        if (filter.TenantId.HasValue)
            query = query.Where(v => v.TenantId == filter.TenantId.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(v => v.CvssScore)
            .ThenByDescending(v => v.PublishedDate)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(v => new VulnerabilityDto(
                v.Id,
                v.ExternalId,
                v.Title,
                v.VendorSeverity.ToString(),
                v.Status.ToString(),
                v.Source,
                v.CvssScore,
                v.PublishedDate,
                v.AffectedAssets.Count,
                _dbContext.OrganizationalSeverities
                    .Where(os => os.VulnerabilityId == v.Id)
                    .OrderByDescending(os => os.AdjustedAt)
                    .Select(os => os.AdjustedSeverity.ToString())
                    .FirstOrDefault()))
            .ToListAsync(ct);

        return Ok(new PagedResponse<VulnerabilityDto>(items, totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<VulnerabilityDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var vulnerability = await _dbContext.Vulnerabilities
            .AsNoTracking()
            .Include(v => v.AffectedAssets)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vulnerability is null)
            return NotFound();

        var orgSeverity = await _dbContext.OrganizationalSeverities
            .AsNoTracking()
            .Where(os => os.VulnerabilityId == id)
            .OrderByDescending(os => os.AdjustedAt)
            .FirstOrDefaultAsync(ct);

        // Get asset names for affected assets
        var assetIds = vulnerability.AffectedAssets.Select(a => a.AssetId).ToList();
        var assets = await _dbContext.Assets
            .AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        var detail = new VulnerabilityDetailDto(
            vulnerability.Id,
            vulnerability.ExternalId,
            vulnerability.Title,
            vulnerability.Description,
            vulnerability.VendorSeverity.ToString(),
            vulnerability.Status.ToString(),
            vulnerability.Source,
            vulnerability.CvssScore,
            vulnerability.CvssVector,
            vulnerability.PublishedDate,
            vulnerability.AffectedAssets.Select(va => new AffectedAssetDto(
                va.AssetId,
                assets.TryGetValue(va.AssetId, out var asset) ? asset.Name : "Unknown",
                assets.TryGetValue(va.AssetId, out var a2) ? a2.AssetType.ToString() : "Unknown",
                va.Status.ToString(),
                va.DetectedDate,
                va.ResolvedDate)).ToList(),
            orgSeverity is null ? null : new OrganizationalSeverityDto(
                orgSeverity.AdjustedSeverity.ToString(),
                orgSeverity.Justification,
                orgSeverity.AssetCriticalityFactor,
                orgSeverity.ExposureFactor,
                orgSeverity.CompensatingControls,
                orgSeverity.AdjustedAt));

        return Ok(detail);
    }

    [HttpPut("{id:guid}/organizational-severity")]
    [Authorize(Policy = Policies.AdjustSeverity)]
    public async Task<IActionResult> UpdateOrganizationalSeverity(
        Guid id,
        [FromBody] UpdateOrgSeverityRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<Severity>(request.AdjustedSeverity, out var severity))
            return BadRequest(new ProblemDetails { Title = "Invalid severity value" });

        var result = await _vulnerabilityService.UpdateOrganizationalSeverityAsync(
            id, severity, request.Justification,
            request.AssetCriticalityFactor, request.ExposureFactor, request.CompensatingControls, ct);

        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        return NoContent();
    }
}
