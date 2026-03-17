using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Vulnerabilities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Api.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;

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
    private readonly TenantSnapshotResolver _snapshotResolver;
    private readonly VulnerabilityDetailQueryService _detailQueryService;

    public VulnerabilitiesController(
        PatchHoundDbContext dbContext,
        VulnerabilityService vulnerabilityService,
        AiReportService aiReportService,
        ITenantContext tenantContext,
        TenantSnapshotResolver snapshotResolver,
        VulnerabilityDetailQueryService detailQueryService
    )
    {
        _dbContext = dbContext;
        _vulnerabilityService = vulnerabilityService;
        _aiReportService = aiReportService;
        _tenantContext = tenantContext;
        _snapshotResolver = snapshotResolver;
        _detailQueryService = detailQueryService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<VulnerabilityDto>>> List(
        [FromQuery] VulnerabilityFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        var activeSnapshotId = await _snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(currentTenantId, ct);

        var query = _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v => v.TenantId == currentTenantId)
            .AsQueryable();

        if (
            !string.IsNullOrEmpty(filter.Severity)
            && Enum.TryParse<Severity>(filter.Severity, out var severity)
        )
            query = query.Where(v => v.VulnerabilityDefinition.VendorSeverity == severity);
        if (
            !string.IsNullOrEmpty(filter.Status)
            && Enum.TryParse<VulnerabilityStatus>(filter.Status, out var status)
        )
            query = query.Where(v => v.Status == status);
        if (!string.IsNullOrEmpty(filter.Source))
            query = query.Where(v => v.VulnerabilityDefinition.Source.Contains(filter.Source));
        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(v =>
                v.VulnerabilityDefinition.Title.Contains(filter.Search)
                || v.VulnerabilityDefinition.ExternalId.Contains(filter.Search)
            );
        if (filter.TenantId.HasValue)
        {
            if (!_tenantContext.HasAccessToTenant(filter.TenantId.Value))
                return Forbid();
            query = query.Where(v => v.TenantId == filter.TenantId.Value);
        }
        if (filter.PresentOnly != false)
        {
            query = query.Where(v =>
                _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open
                )
            );
        }
        if (filter.RecurrenceOnly == true)
        {
            var recurringTenantVulnerabilityIds = await _dbContext
                .VulnerabilityAssetEpisodes.AsNoTracking()
                .Where(episode => episode.TenantId == currentTenantId)
                .GroupBy(episode => new { episode.TenantVulnerabilityId, episode.AssetId })
                .Where(group => group.Count() > 1)
                .Select(group => group.Key.TenantVulnerabilityId)
                .Distinct()
                .ToListAsync(ct);
            query = query.Where(v => recurringTenantVulnerabilityIds.Contains(v.Id));
        }

        var totalCount = await query.CountAsync(ct);

        var tenantVulnerabilityIds = await query
            .OrderByDescending(v => v.VulnerabilityDefinition.CvssScore)
            .ThenByDescending(v => v.VulnerabilityDefinition.PublishedDate)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(v => v.Id)
            .ToListAsync(ct);

        var episodeRows = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(episode => tenantVulnerabilityIds.Contains(episode.TenantVulnerabilityId))
            .Select(episode => new
            {
                episode.TenantVulnerabilityId,
                episode.AssetId,
                episode.EpisodeNumber,
                episode.FirstSeenAt,
            })
            .ToListAsync(ct);

        var recentReappearanceThreshold = DateTimeOffset.UtcNow.AddDays(-30);
        var episodeCountsByVulnerabilityId = episodeRows
            .GroupBy(episode => episode.TenantVulnerabilityId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var reappearanceEpisodes = group
                        .GroupBy(episode => episode.AssetId)
                        .SelectMany(assetEpisodes =>
                            assetEpisodes.Where(episode => episode.EpisodeNumber > 1)
                        )
                        .ToList();

                    return new
                    {
                        EpisodeCount = group.Count(),
                        ReappearanceCount = reappearanceEpisodes.Count,
                        HasRecentReappearance = reappearanceEpisodes.Any(episode =>
                            episode.FirstSeenAt >= recentReappearanceThreshold
                        ),
                    };
                }
            );

        var itemRows = await query
            .OrderByDescending(v => v.VulnerabilityDefinition.CvssScore)
            .ThenByDescending(v => v.VulnerabilityDefinition.PublishedDate)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(v => new
            {
                v.Id,
                v.VulnerabilityDefinition.ExternalId,
                Title = v.VulnerabilityDefinition.Title,
                VendorSeverity = v.VulnerabilityDefinition.VendorSeverity.ToString(),
                Status = _dbContext.VulnerabilityAssetEpisodes.Any(e =>
                    e.TenantVulnerabilityId == v.Id
                    && e.Status == VulnerabilityStatus.Open
                )
                    ? nameof(VulnerabilityStatus.Open)
                    : nameof(VulnerabilityStatus.Resolved),
                Source = v.VulnerabilityDefinition.Source,
                CvssScore = v.VulnerabilityDefinition.CvssScore,
                PublishedDate = v.VulnerabilityDefinition.PublishedDate,
                AffectedAssetCount = _dbContext
                    .VulnerabilityAssets.Where(link =>
                        link.TenantVulnerabilityId == v.Id && link.SnapshotId == activeSnapshotId
                    )
                    .Count(),
                AdjustedSeverity = _dbContext
                    .OrganizationalSeverities.Where(os => os.TenantVulnerabilityId == v.Id)
                    .OrderByDescending(os => os.AdjustedAt)
                    .Select(os => os.AdjustedSeverity.ToString())
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = itemRows
            .Select(v => new VulnerabilityDto(
                v.Id,
                v.ExternalId,
                v.Title,
                v.VendorSeverity,
                v.Status,
                VulnerabilityDetailQueryService.FormatSourceDisplay(v.Source),
                v.CvssScore,
                v.PublishedDate,
                v.AffectedAssetCount,
                v.AdjustedSeverity,
                episodeCountsByVulnerabilityId.TryGetValue(v.Id, out var episodeInfo)
                    ? episodeInfo.EpisodeCount
                    : 0,
                episodeCountsByVulnerabilityId.TryGetValue(v.Id, out episodeInfo)
                    ? episodeInfo.ReappearanceCount
                    : 0,
                episodeCountsByVulnerabilityId.TryGetValue(v.Id, out episodeInfo)
                    && episodeInfo.HasRecentReappearance
            ))
            .ToList();

        return Ok(
            new PagedResponse<VulnerabilityDto>(
                items,
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<VulnerabilityDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var detail = await _detailQueryService.BuildAsync(currentTenantId, id, ct);
        if (detail is null)
            return NotFound();

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

        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var tenantVulnerabilityId = await _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(item => item.Id == id && item.TenantId == currentTenantId)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(ct);

        if (!tenantVulnerabilityId.HasValue)
            return NotFound(new ProblemDetails { Title = "Tenant vulnerability not found" });

        var result = await _vulnerabilityService.UpdateOrganizationalSeverityAsync(
            tenantVulnerabilityId.Value,
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
        var tenantVulnerability = await _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Include(item => item.VulnerabilityDefinition)
            .FirstOrDefaultAsync(item => item.Id == id, ct);

        if (tenantVulnerability is null)
            return NotFound(new ProblemDetails { Title = "Tenant vulnerability not found" });

        if (!_tenantContext.HasAccessToTenant(tenantVulnerability.TenantId))
            return Forbid();

        var assetIds = await _dbContext
            .VulnerabilityAssets.AsNoTracking()
            .Where(item => item.TenantVulnerabilityId == tenantVulnerability.Id)
            .Select(item => item.AssetId)
            .ToListAsync(ct);
        var affectedAssets = await _dbContext
            .Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToListAsync(ct);

        var result = await _aiReportService.GenerateReportAsync(
            tenantVulnerability.VulnerabilityDefinition,
            tenantVulnerability.Id,
            affectedAssets,
            tenantVulnerability.TenantId,
            _tenantContext.CurrentUserId,
            request.TenantAiProfileId,
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
                report.TenantVulnerabilityId,
                report.Content,
                report.ProviderType,
                report.ProfileName,
                report.Model,
                report.GeneratedAt
            )
        );
    }
}
