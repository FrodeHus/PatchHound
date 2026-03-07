using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.RiskAcceptances;
using PatchHound.Api.Models.Tasks;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/tasks")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly RemediationTaskService _taskService;
    private readonly RiskAcceptanceService _riskAcceptanceService;
    private readonly ITenantContext _tenantContext;

    public TasksController(
        PatchHoundDbContext dbContext,
        RemediationTaskService taskService,
        RiskAcceptanceService riskAcceptanceService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _taskService = taskService;
        _riskAcceptanceService = riskAcceptanceService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<RemediationTaskDto>>> List(
        [FromQuery] TaskFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.RemediationTasks.AsNoTracking().AsQueryable();

        if (
            !string.IsNullOrEmpty(filter.Status)
            && Enum.TryParse<RemediationTaskStatus>(filter.Status, out var status)
        )
            query = query.Where(t => t.Status == status);
        if (filter.TenantId.HasValue)
            query = query.Where(t => t.TenantId == filter.TenantId.Value);
        if (filter.AssigneeId.HasValue)
            query = query.Where(t => t.AssigneeId == filter.AssigneeId.Value);

        var totalCount = await query.CountAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var items = await query
            .OrderBy(t => t.DueDate)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(t => new RemediationTaskDto(
                t.Id,
                t.VulnerabilityId,
                t.AssetId,
                _dbContext
                    .Vulnerabilities.Where(v => v.Id == t.VulnerabilityId)
                    .Select(v => v.Title)
                    .FirstOrDefault() ?? "",
                _dbContext.Assets.Where(a => a.Id == t.AssetId).Select(a => a.Name).FirstOrDefault()
                    ?? "",
                t.Status.ToString(),
                t.Justification,
                t.DueDate,
                t.CreatedAt,
                t.DueDate < now
                    && t.Status != RemediationTaskStatus.Completed
                    && t.Status != RemediationTaskStatus.RiskAccepted
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<RemediationTaskDto>(items, totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<RemediationTaskDto>> Get(Guid id, CancellationToken ct)
    {
        var task = await _dbContext
            .RemediationTasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
            return NotFound();

        var vulnTitle =
            await _dbContext
                .Vulnerabilities.Where(v => v.Id == task.VulnerabilityId)
                .Select(v => v.Title)
                .FirstOrDefaultAsync(ct) ?? "";
        var assetName =
            await _dbContext
                .Assets.Where(a => a.Id == task.AssetId)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(ct) ?? "";
        var now = DateTimeOffset.UtcNow;

        return Ok(
            new RemediationTaskDto(
                task.Id,
                task.VulnerabilityId,
                task.AssetId,
                vulnTitle,
                assetName,
                task.Status.ToString(),
                task.Justification,
                task.DueDate,
                task.CreatedAt,
                task.DueDate < now
                    && task.Status != RemediationTaskStatus.Completed
                    && task.Status != RemediationTaskStatus.RiskAccepted
            )
        );
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = Policies.UpdateTaskStatus)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateTaskStatusRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<RemediationTaskStatus>(request.Status, out var status))
            return BadRequest(new ProblemDetails { Title = "Invalid status value" });

        var result = await _taskService.UpdateStatusAsync(id, status, request.Justification, ct);
        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return NoContent();
    }

    [HttpPost("{id:guid}/risk-acceptance")]
    [Authorize(Policy = Policies.RequestRiskAcceptance)]
    public async Task<IActionResult> RequestRiskAcceptance(
        Guid id,
        [FromBody] RequestRiskAcceptanceRequest request,
        CancellationToken ct
    )
    {
        var task = await _dbContext
            .RemediationTasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
            return NotFound(new ProblemDetails { Title = "Task not found" });

        var result = await _riskAcceptanceService.RequestAsync(
            task.VulnerabilityId,
            task.TenantId,
            _tenantContext.CurrentUserId,
            request.Justification,
            task.AssetId,
            request.Conditions,
            request.ExpiryDate,
            request.ReviewFrequency,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var acceptance = result.Value;
        return CreatedAtAction(
            nameof(RiskAcceptancesController.Get),
            "RiskAcceptances",
            new { id = acceptance.Id },
            new RiskAcceptanceDto(
                acceptance.Id,
                acceptance.VulnerabilityId,
                acceptance.AssetId,
                acceptance.Status.ToString(),
                acceptance.Justification,
                acceptance.RequestedBy,
                acceptance.RequestedAt,
                acceptance.ApprovedBy,
                acceptance.ApprovedAt,
                acceptance.Conditions,
                acceptance.ExpiryDate,
                acceptance.ReviewFrequency
            )
        );
    }
}
