using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.ApprovalTasks;
using PatchHound.Api.Models.Decisions;
using PatchHound.Api.Services;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/software/{tenantSoftwareId:guid}/remediation")]
[Authorize]
public class RemediationDecisionsController(
    RemediationDecisionQueryService queryService,
    RemediationDecisionService decisionService,
    AnalystRecommendationService recommendationService,
    PatchHoundDbContext dbContext,
    ITenantContext tenantContext
) : ControllerBase
{
    [HttpGet("decision-context")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<DecisionContextDto>> GetDecisionContext(
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var result = await queryService.BuildByTenantSoftwareAsync(tenantId, tenantSoftwareId, ct);
        if (result is null)
            return NotFound();

        return Ok(result);
    }

    [HttpPost("decisions")]
    [Authorize(Policy = Policies.UpdateTaskStatus)]
    public async Task<ActionResult<RemediationDecisionDto>> CreateDecision(
        Guid tenantSoftwareId,
        [FromBody] CreateDecisionRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        if (!Enum.TryParse<RemediationOutcome>(request.Outcome, true, out var outcome))
            return BadRequest(new ProblemDetails { Title = "Invalid outcome value." });

        var userId = tenantContext.CurrentUserId;

        var result = await decisionService.CreateDecisionForTenantSoftwareAsync(
            tenantId,
            tenantSoftwareId,
            outcome,
            request.Justification,
            userId,
            request.ExpiryDate,
            request.ReEvaluationDate,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var d = result.Value;
        return CreatedAtAction(
            nameof(GetDecisionContext),
            new { tenantSoftwareId },
            new RemediationDecisionDto(
                d.Id, d.Outcome.ToString(), d.ApprovalStatus.ToString(),
                d.Justification, d.DecidedBy, d.DecidedAt,
                d.ApprovedBy, d.ApprovedAt, d.ExpiryDate, d.ReEvaluationDate,
                []
            )
        );
    }

    [HttpPut("decisions/{decisionId:guid}")]
    [Authorize(Policy = Policies.ApproveRiskAcceptance)]
    public async Task<IActionResult> ApproveOrReject(
        Guid tenantSoftwareId,
        Guid decisionId,
        [FromBody] ApproveRejectDecisionRequest request,
        CancellationToken ct
    )
    {
        var userId = tenantContext.CurrentUserId;

        var result = request.Action?.ToLowerInvariant() switch
        {
            "approve" => await decisionService.ApproveAsync(decisionId, userId, ct),
            "reject" => await decisionService.RejectAsync(decisionId, userId, ct),
            "cancel" => await decisionService.ExpireAsync(decisionId, ct),
            _ => null,
        };

        if (result is null)
            return BadRequest(new ProblemDetails { Title = "Action must be 'approve', 'reject', or 'cancel'." });

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return Ok();
    }

    [HttpPost("decisions/{decisionId:guid}/overrides")]
    [Authorize(Policy = Policies.UpdateTaskStatus)]
    public async Task<ActionResult<VulnerabilityOverrideDto>> AddOverride(
        Guid tenantSoftwareId,
        Guid decisionId,
        [FromBody] CreateOverrideRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<RemediationOutcome>(request.Outcome, true, out var outcome))
            return BadRequest(new ProblemDetails { Title = "Invalid outcome value." });

        var result = await decisionService.AddVulnerabilityOverrideAsync(
            decisionId,
            request.TenantVulnerabilityId,
            outcome,
            request.Justification,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var vo = result.Value;
        return Created("", new VulnerabilityOverrideDto(
            vo.Id, vo.TenantVulnerabilityId, vo.Outcome.ToString(), vo.Justification, vo.CreatedAt
        ));
    }

    [HttpGet("recommendations")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<List<AnalystRecommendationDto>>> GetRecommendations(
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var context = await queryService.BuildByTenantSoftwareAsync(tenantId, tenantSoftwareId, ct);
        if (context is null)
            return NotFound();

        return Ok(context.Recommendations);
    }

    [HttpPost("recommendations")]
    [Authorize(Policy = Policies.AddComments)]
    public async Task<ActionResult<AnalystRecommendationDto>> AddRecommendation(
        Guid tenantSoftwareId,
        [FromBody] CreateRecommendationRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        if (!Enum.TryParse<RemediationOutcome>(request.RecommendedOutcome, true, out var outcome))
            return BadRequest(new ProblemDetails { Title = "Invalid outcome value." });

        var userId = tenantContext.CurrentUserId;

        var result = await recommendationService.AddRecommendationForTenantSoftwareAsync(
            tenantId,
            tenantSoftwareId,
            outcome,
            request.Rationale,
            userId,
            request.TenantVulnerabilityId,
            request.PriorityOverride,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var r = result.Value;
        string? analystDisplayName = null;
        if (userId != Guid.Empty)
        {
            analystDisplayName = await dbContext.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.DisplayName)
                .FirstOrDefaultAsync(ct);
        }
        return Created("", new AnalystRecommendationDto(
            r.Id, r.TenantVulnerabilityId, r.RecommendedOutcome.ToString(),
            r.Rationale, r.PriorityOverride, r.AnalystId, analystDisplayName, r.CreatedAt
        ));
    }

    [HttpGet("audit-trail")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<List<ApprovalAuditEntryDto>>> GetAuditTrail(
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var decisionIds = await dbContext.RemediationDecisions.AsNoTracking()
            .Where(decision => decision.TenantId == tenantId && decision.TenantSoftwareId == tenantSoftwareId)
            .Select(decision => decision.Id)
            .ToListAsync(ct);

        if (decisionIds.Count == 0)
            return Ok(new List<ApprovalAuditEntryDto>());

        var entries = await dbContext.AuditLogEntries.AsNoTracking()
            .Where(entry => entry.EntityType == "RemediationDecision" && decisionIds.Contains(entry.EntityId))
            .OrderByDescending(entry => entry.Timestamp)
            .ToListAsync(ct);

        var userIds = entries.Select(e => e.UserId).Distinct().ToList();
        var userNames = await dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var dtos = entries
            .OrderByDescending(e => e.Timestamp)
            .Select(e =>
            {
                userNames.TryGetValue(e.UserId, out var name);
                return AuditTimelineMapper.ToDto(e, name);
            })
            .ToList();

        return Ok(dtos);
    }
}
