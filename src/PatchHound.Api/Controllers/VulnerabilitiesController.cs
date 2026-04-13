using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Vulnerabilities;
using PatchHound.Api.Services;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/vulnerabilities")]
[Authorize]
public class VulnerabilitiesController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly VulnerabilityDetailQueryService _detailQueryService;

    public VulnerabilitiesController(
        PatchHoundDbContext dbContext,
        ITenantContext tenantContext,
        VulnerabilityDetailQueryService detailQueryService
    )
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
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
        if (_tenantContext.CurrentTenantId is null)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var query = _dbContext.Vulnerabilities.AsNoTracking().AsQueryable();

        if (
            !string.IsNullOrEmpty(filter.Severity)
            && Enum.TryParse<Severity>(filter.Severity, out var severity)
        )
            query = query.Where(v => v.VendorSeverity == severity);
        if (!string.IsNullOrEmpty(filter.Source))
            query = query.Where(v => v.Source.Contains(filter.Source));
        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(v =>
                v.Title.Contains(filter.Search) || v.ExternalId.Contains(filter.Search)
            );
        if (filter.MinAgeDays.HasValue)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-filter.MinAgeDays.Value);
            query = query.Where(v => v.PublishedDate <= cutoff);
        }
        if (filter.PublicExploitOnly == true)
            query = query.Where(v =>
                _dbContext.ThreatAssessments.Any(a => a.VulnerabilityId == v.Id && a.PublicExploit)
            );
        if (filter.KnownExploitedOnly == true)
            query = query.Where(v =>
                _dbContext.ThreatAssessments.Any(a =>
                    a.VulnerabilityId == v.Id && a.KnownExploited
                )
            );
        if (filter.ActiveAlertOnly == true)
            query = query.Where(v =>
                _dbContext.ThreatAssessments.Any(a => a.VulnerabilityId == v.Id && a.ActiveAlert)
            );

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(v => v.CvssScore)
            .ThenByDescending(v => v.PublishedDate)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(v => new
            {
                v.Id,
                v.ExternalId,
                v.Title,
                Severity = v.VendorSeverity.ToString(),
                v.Source,
                v.CvssScore,
                v.PublishedDate,
                Threat = _dbContext
                    .ThreatAssessments.Where(a => a.VulnerabilityId == v.Id)
                    .Select(a => new
                    {
                        a.ThreatScore,
                        a.EpssScore,
                        a.PublicExploit,
                        a.KnownExploited,
                        a.ActiveAlert,
                    })
                    .FirstOrDefault(),
                AffectedDeviceCount = _dbContext.DeviceVulnerabilityExposures
                    .Where(e =>
                        e.TenantId == _tenantContext.CurrentTenantId.Value
                        && e.VulnerabilityId == v.Id
                        && e.Status == ExposureStatus.Open)
                    .Select(e => e.DeviceId)
                    .Distinct()
                    .Count(),
            })
            .ToListAsync(ct);

        var dtos = items
            .Select(v => new VulnerabilityDto(
                v.Id,
                v.ExternalId,
                v.Title,
                v.Severity,
                v.Source,
                v.CvssScore,
                v.PublishedDate,
                ExposureDataAvailable: true,
                AffectedDeviceCount: v.AffectedDeviceCount,
                ThreatScore: v.Threat?.ThreatScore,
                EpssScore: v.Threat?.EpssScore,
                PublicExploit: v.Threat?.PublicExploit ?? false,
                KnownExploited: v.Threat?.KnownExploited ?? false,
                ActiveAlert: v.Threat?.ActiveAlert ?? false
            ))
            .ToList();

        return Ok(new PagedResponse<VulnerabilityDto>(dtos, total, pagination.Page, pagination.BoundedPageSize));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<VulnerabilityDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is null)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var detail = await _detailQueryService.BuildAsync(_tenantContext.CurrentTenantId.Value, id, ct);
        if (detail is null)
            return NotFound();

        return Ok(detail);
    }

    [HttpPut("{id:guid}/organizational-severity")]
    [Authorize(Policy = Policies.AdjustSeverity)]
    public IActionResult UpdateOrganizationalSeverity(Guid id, [FromBody] UpdateOrgSeverityRequest _) =>
        Conflict(new ProblemDetails
        {
            Title = "Organizational severity is disabled during canonical migration; restored in Phase 3.",
        });

    [HttpPost("{id:guid}/ai-report")]
    [Authorize(Policy = Policies.GenerateAiReports)]
    public IActionResult GenerateAiReport(Guid id, [FromBody] GenerateAiReportRequest _) =>
        Conflict(new ProblemDetails
        {
            Title = "AI report generation is disabled during canonical migration; restored in Phase 4 (case-first).",
        });
}
