using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vigil.Api.Auth;
using Vigil.Api.Models;
using Vigil.Api.Models.RiskAcceptances;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Core.Services;
using Vigil.Infrastructure.Data;

namespace Vigil.Api.Controllers;

[ApiController]
[Route("api/risk-acceptances")]
[Authorize]
public class RiskAcceptancesController : ControllerBase
{
    private readonly VigilDbContext _dbContext;
    private readonly RiskAcceptanceService _riskAcceptanceService;
    private readonly ITenantContext _tenantContext;

    public RiskAcceptancesController(
        VigilDbContext dbContext,
        RiskAcceptanceService riskAcceptanceService,
        ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _riskAcceptanceService = riskAcceptanceService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<RiskAcceptanceDto>>> List(
        [FromQuery] RiskAcceptanceFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        var query = _dbContext.RiskAcceptances.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(filter.Status) && Enum.TryParse<RiskAcceptanceStatus>(filter.Status, out var status))
            query = query.Where(r => r.Status == status);
        if (filter.TenantId.HasValue)
            query = query.Where(r => r.TenantId == filter.TenantId.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.RequestedAt)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(r => new RiskAcceptanceDto(
                r.Id,
                r.VulnerabilityId,
                r.AssetId,
                r.Status.ToString(),
                r.Justification,
                r.RequestedBy,
                r.RequestedAt,
                r.ApprovedBy,
                r.ApprovedAt,
                r.Conditions,
                r.ExpiryDate,
                r.ReviewFrequency))
            .ToListAsync(ct);

        return Ok(new PagedResponse<RiskAcceptanceDto>(items, totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<RiskAcceptanceDto>> Get(Guid id, CancellationToken ct)
    {
        var r = await _dbContext.RiskAcceptances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
            return NotFound();

        return Ok(new RiskAcceptanceDto(
            r.Id,
            r.VulnerabilityId,
            r.AssetId,
            r.Status.ToString(),
            r.Justification,
            r.RequestedBy,
            r.RequestedAt,
            r.ApprovedBy,
            r.ApprovedAt,
            r.Conditions,
            r.ExpiryDate,
            r.ReviewFrequency));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ApproveRiskAcceptance)]
    public async Task<IActionResult> ApproveOrReject(
        Guid id,
        [FromBody] ApproveRejectRequest request,
        CancellationToken ct)
    {
        if (string.Equals(request.Action, "Approve", StringComparison.OrdinalIgnoreCase))
        {
            var result = await _riskAcceptanceService.ApproveAsync(
                id, _tenantContext.CurrentUserId,
                request.Conditions, request.ExpiryDate, request.ReviewFrequency, ct);

            if (!result.IsSuccess)
                return BadRequest(new ProblemDetails { Title = result.Error });

            return NoContent();
        }

        if (string.Equals(request.Action, "Reject", StringComparison.OrdinalIgnoreCase))
        {
            var result = await _riskAcceptanceService.RejectAsync(
                id, _tenantContext.CurrentUserId, ct);

            if (!result.IsSuccess)
                return BadRequest(new ProblemDetails { Title = result.Error });

            return NoContent();
        }

        return BadRequest(new ProblemDetails { Title = "Action must be 'Approve' or 'Reject'" });
    }
}
