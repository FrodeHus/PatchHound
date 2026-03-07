using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Vulnerabilities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/vulnerabilities")]
[Authorize]
public class VulnerabilitiesController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly VulnerabilityService _vulnerabilityService;
    private readonly AiReportService _aiReportService;
    private readonly ITenantContext _tenantContext;

    public VulnerabilitiesController(
        PatchHoundDbContext dbContext,
        VulnerabilityService vulnerabilityService,
        AiReportService aiReportService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _vulnerabilityService = vulnerabilityService;
        _aiReportService = aiReportService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<VulnerabilityDto>>> List(
        [FromQuery] VulnerabilityFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.Vulnerabilities.AsNoTracking().AsQueryable();

        if (
            !string.IsNullOrEmpty(filter.Severity)
            && Enum.TryParse<Severity>(filter.Severity, out var severity)
        )
            query = query.Where(v => v.VendorSeverity == severity);
        if (
            !string.IsNullOrEmpty(filter.Status)
            && Enum.TryParse<VulnerabilityStatus>(filter.Status, out var status)
        )
            query = query.Where(v => v.Status == status);
        if (!string.IsNullOrEmpty(filter.Source))
            query = query.Where(v => v.Source == filter.Source);
        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(v =>
                v.Title.Contains(filter.Search) || v.ExternalId.Contains(filter.Search)
            );
        if (filter.TenantId.HasValue)
            query = query.Where(v => v.TenantId == filter.TenantId.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(v => v.CvssScore)
            .ThenByDescending(v => v.PublishedDate)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
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
                _dbContext
                    .OrganizationalSeverities.Where(os => os.VulnerabilityId == v.Id)
                    .OrderByDescending(os => os.AdjustedAt)
                    .Select(os => os.AdjustedSeverity.ToString())
                    .FirstOrDefault()
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<VulnerabilityDto>(items, totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<VulnerabilityDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var vulnerability = await _dbContext
            .Vulnerabilities.AsNoTracking()
            .Include(v => v.AffectedAssets)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vulnerability is null)
            return NotFound();

        var orgSeverity = await _dbContext
            .OrganizationalSeverities.AsNoTracking()
            .Where(os => os.VulnerabilityId == id)
            .OrderByDescending(os => os.AdjustedAt)
            .FirstOrDefaultAsync(ct);

        // Get asset names for affected assets
        var assetIds = vulnerability.AffectedAssets.Select(a => a.AssetId).ToList();
        var assets = await _dbContext
            .Assets.AsNoTracking()
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
            vulnerability
                .AffectedAssets.Select(va => new AffectedAssetDto(
                    va.AssetId,
                    assets.TryGetValue(va.AssetId, out var asset) ? asset.Name : "Unknown",
                    assets.TryGetValue(va.AssetId, out var a2)
                        ? a2.AssetType.ToString()
                        : "Unknown",
                    va.Status.ToString(),
                    va.DetectedDate,
                    va.ResolvedDate
                ))
                .ToList(),
            orgSeverity is null
                ? null
                : new OrganizationalSeverityDto(
                    orgSeverity.AdjustedSeverity.ToString(),
                    orgSeverity.Justification,
                    orgSeverity.AssetCriticalityFactor,
                    orgSeverity.ExposureFactor,
                    orgSeverity.CompensatingControls,
                    orgSeverity.AdjustedAt
                )
        );

        return Ok(detail);
    }

    [HttpPut("{id:guid}/organizational-severity")]
    [Authorize(Policy = Policies.AdjustSeverity)]
    public async Task<IActionResult> UpdateOrganizationalSeverity(
        Guid id,
        [FromBody] UpdateOrgSeverityRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<Severity>(request.AdjustedSeverity, out var severity))
            return BadRequest(new ProblemDetails { Title = "Invalid severity value" });

        var result = await _vulnerabilityService.UpdateOrganizationalSeverityAsync(
            id,
            severity,
            request.Justification,
            request.AssetCriticalityFactor,
            request.ExposureFactor,
            request.CompensatingControls,
            ct
        );

        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        return NoContent();
    }

    [HttpPost("{id:guid}/ai-report")]
    [Authorize(Policy = Policies.GenerateAiReports)]
    public async Task<ActionResult<AiReportDto>> GenerateAiReport(
        Guid id,
        [FromBody] GenerateAiReportRequest request,
        CancellationToken ct
    )
    {
        var vulnerability = await _dbContext
            .Vulnerabilities.AsNoTracking()
            .Include(v => v.AffectedAssets)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vulnerability is null)
            return NotFound(new ProblemDetails { Title = "Vulnerability not found" });

        var assetIds = vulnerability.AffectedAssets.Select(a => a.AssetId).ToList();
        var affectedAssets = await _dbContext
            .Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToListAsync(ct);

        var result = await _aiReportService.GenerateReportAsync(
            vulnerability,
            affectedAssets,
            vulnerability.TenantId,
            _tenantContext.CurrentUserId,
            request.ProviderName,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var report = result.Value;
        _dbContext.AIReports.Add(report);
        await _dbContext.SaveChangesAsync(ct);

        return Ok(
            new AiReportDto(
                report.Id,
                report.VulnerabilityId,
                report.Content,
                report.Provider,
                report.GeneratedAt
            )
        );
    }
}
