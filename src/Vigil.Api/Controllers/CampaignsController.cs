using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vigil.Api.Auth;
using Vigil.Api.Models;
using Vigil.Api.Models.Campaigns;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Core.Services;
using Vigil.Infrastructure.Data;

namespace Vigil.Api.Controllers;

[ApiController]
[Route("api/campaigns")]
[Authorize]
public class CampaignsController : ControllerBase
{
    private readonly VigilDbContext _dbContext;
    private readonly CampaignService _campaignService;
    private readonly ITenantContext _tenantContext;

    public CampaignsController(VigilDbContext dbContext, CampaignService campaignService, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _campaignService = campaignService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ManageCampaigns)]
    public async Task<ActionResult<PagedResponse<CampaignDto>>> List(
        [FromQuery] CampaignFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        var query = _dbContext.Campaigns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(filter.Status) && Enum.TryParse<CampaignStatus>(filter.Status, out var status))
            query = query.Where(c => c.Status == status);
        if (filter.TenantId.HasValue)
            query = query.Where(c => c.TenantId == filter.TenantId.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(c => new CampaignDto(
                c.Id,
                c.Name,
                c.Description,
                c.Status.ToString(),
                c.CreatedAt,
                _dbContext.CampaignVulnerabilities.Count(cv => cv.CampaignId == c.Id),
                _dbContext.RemediationTasks.Count(t =>
                    _dbContext.CampaignVulnerabilities
                        .Where(cv => cv.CampaignId == c.Id)
                        .Select(cv => cv.VulnerabilityId)
                        .Contains(t.VulnerabilityId)),
                _dbContext.RemediationTasks.Count(t =>
                    _dbContext.CampaignVulnerabilities
                        .Where(cv => cv.CampaignId == c.Id)
                        .Select(cv => cv.VulnerabilityId)
                        .Contains(t.VulnerabilityId)
                    && t.Status == RemediationTaskStatus.Completed)))
            .ToListAsync(ct);

        return Ok(new PagedResponse<CampaignDto>(items, totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ManageCampaigns)]
    public async Task<ActionResult<CampaignDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var campaign = await _dbContext.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (campaign is null)
            return NotFound();

        var vulnerabilityIds = await _dbContext.CampaignVulnerabilities
            .AsNoTracking()
            .Where(cv => cv.CampaignId == id)
            .Select(cv => cv.VulnerabilityId)
            .ToListAsync(ct);

        var totalTasks = await _dbContext.RemediationTasks
            .AsNoTracking()
            .CountAsync(t => vulnerabilityIds.Contains(t.VulnerabilityId), ct);

        var completedTasks = await _dbContext.RemediationTasks
            .AsNoTracking()
            .CountAsync(t => vulnerabilityIds.Contains(t.VulnerabilityId)
                && t.Status == RemediationTaskStatus.Completed, ct);

        return Ok(new CampaignDetailDto(
            campaign.Id,
            campaign.Name,
            campaign.Description,
            campaign.Status.ToString(),
            campaign.CreatedBy,
            campaign.CreatedAt,
            vulnerabilityIds.Count,
            totalTasks,
            completedTasks,
            vulnerabilityIds));
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManageCampaigns)]
    public async Task<ActionResult<CampaignDetailDto>> Create(
        [FromBody] CreateCampaignRequest request,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId is null)
            return BadRequest(new ProblemDetails { Title = "Tenant context is required" });

        var result = await _campaignService.CreateAsync(
            tenantId.Value, _tenantContext.CurrentUserId, request.Name, request.Description, ct);

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var campaign = result.Value;
        var detail = new CampaignDetailDto(
            campaign.Id,
            campaign.Name,
            campaign.Description,
            campaign.Status.ToString(),
            campaign.CreatedBy,
            campaign.CreatedAt,
            0, 0, 0,
            []);

        return CreatedAtAction(nameof(Get), new { id = campaign.Id }, detail);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageCampaigns)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateCampaignRequest request,
        CancellationToken ct)
    {
        var result = await _campaignService.UpdateAsync(id, request.Name, request.Description, ct);

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return NoContent();
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = Policies.ManageCampaigns)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        var result = await _campaignService.CloseAsync(id, ct);

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return NoContent();
    }

    [HttpPost("{id:guid}/vulnerabilities")]
    [Authorize(Policy = Policies.ManageCampaigns)]
    public async Task<IActionResult> LinkVulnerabilities(
        Guid id,
        [FromBody] LinkVulnerabilitiesRequest request,
        CancellationToken ct)
    {
        var result = await _campaignService.LinkVulnerabilitiesAsync(id, request.VulnerabilityIds, ct);

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return NoContent();
    }

    [HttpPost("{id:guid}/bulk-assign")]
    [Authorize(Policy = Policies.ManageCampaigns)]
    public async Task<IActionResult> BulkAssign(
        Guid id,
        [FromBody] BulkAssignCampaignRequest request,
        CancellationToken ct)
    {
        var campaign = await _dbContext.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (campaign is null)
            return NotFound(new ProblemDetails { Title = "Campaign not found" });

        if (campaign.Status == CampaignStatus.Closed)
            return BadRequest(new ProblemDetails { Title = "Cannot modify a closed campaign" });

        var vulnerabilityIds = await _dbContext.CampaignVulnerabilities
            .AsNoTracking()
            .Where(cv => cv.CampaignId == id)
            .Select(cv => cv.VulnerabilityId)
            .ToListAsync(ct);

        if (vulnerabilityIds.Count == 0)
            return BadRequest(new ProblemDetails { Title = "Campaign has no linked vulnerabilities" });

        // Get all affected assets for each vulnerability
        var vulnerabilityAssets = await _dbContext.VulnerabilityAssets
            .AsNoTracking()
            .Where(va => vulnerabilityIds.Contains(va.VulnerabilityId))
            .Select(va => new { va.VulnerabilityId, va.AssetId })
            .ToListAsync(ct);

        // Get existing tasks to avoid duplicates
        var existingTasks = await _dbContext.RemediationTasks
            .AsNoTracking()
            .Where(t => vulnerabilityIds.Contains(t.VulnerabilityId))
            .Select(t => new { t.VulnerabilityId, t.AssetId })
            .ToListAsync(ct);

        var existingTaskSet = existingTasks
            .Select(t => (t.VulnerabilityId, t.AssetId))
            .ToHashSet();

        var dueDate = DateTimeOffset.UtcNow.AddDays(30);
        var createdBy = _tenantContext.CurrentUserId;
        var tasksCreated = 0;

        foreach (var va in vulnerabilityAssets)
        {
            if (existingTaskSet.Contains((va.VulnerabilityId, va.AssetId)))
                continue;

            var task = RemediationTask.Create(
                va.VulnerabilityId,
                va.AssetId,
                campaign.TenantId,
                request.AssigneeId,
                createdBy,
                dueDate);

            _dbContext.RemediationTasks.Add(task);
            tasksCreated++;
        }

        if (tasksCreated > 0)
            await _dbContext.SaveChangesAsync(ct);

        return Ok(new { TasksCreated = tasksCreated });
    }
}
