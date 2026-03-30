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
    ApprovalTaskService approvalTaskService,
    AnalystRecommendationService recommendationService,
    RemediationWorkflowAuthorizationService workflowAuthorizationService,
    RemediationWorkflowService workflowService,
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

    [HttpPost("workflow")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<EnsureRemediationWorkflowResponse>> EnsureWorkflow(
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var workflow = await workflowService.GetOrCreateActiveWorkflowAsync(
            tenantId,
            tenantSoftwareId,
            ct
        );
        await dbContext.SaveChangesAsync(ct);
        return Ok(new EnsureRemediationWorkflowResponse(workflow.Id));
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
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        if (!Enum.TryParse<RemediationOutcome>(request.Outcome, true, out var outcome))
            return BadRequest(new ProblemDetails { Title = "Invalid outcome value." });

        var result = await decisionService.AddVulnerabilityOverrideAsync(
            tenantId,
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

    [HttpPost("/api/remediation/{workflowId:guid}/analysis")]
    [Authorize(Policy = Policies.AddComments)]
    public async Task<ActionResult<AnalystRecommendationDto>> AddRecommendationByWorkflow(
        Guid workflowId,
        [FromBody] CreateRecommendationRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var workflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == workflowId, ct);
        if (workflow is null)
            return NotFound(new ProblemDetails { Title = "Remediation workflow not found." });

        var auth = await workflowAuthorizationService.EnsureCanAddRecommendationAsync(tenantId, workflow.TenantSoftwareId, ct);
        if (!auth.IsSuccess)
            return BadRequest(new ProblemDetails { Title = auth.Error });

        if (!Enum.TryParse<RemediationOutcome>(request.RecommendedOutcome, true, out var outcome))
            return BadRequest(new ProblemDetails { Title = "Invalid outcome value." });

        var userId = tenantContext.CurrentUserId;
        var result = await recommendationService.AddRecommendationForTenantSoftwareAsync(
            tenantId,
            workflow.TenantSoftwareId,
            outcome,
            request.Rationale,
            userId,
            request.TenantVulnerabilityId,
            request.PriorityOverride,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var analystDisplayName = await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.DisplayName)
            .FirstOrDefaultAsync(ct);

        var r = result.Value;
        return Created("", new AnalystRecommendationDto(
            r.Id, r.TenantVulnerabilityId, r.RecommendedOutcome.ToString(),
            r.Rationale, r.PriorityOverride, r.AnalystId, analystDisplayName, r.CreatedAt
        ));
    }

    [HttpPost("/api/remediation/{workflowId:guid}/verification")]
    [Authorize(Policy = Policies.UpdateTaskStatus)]
    public async Task<IActionResult> VerifyRecurringRemediation(
        Guid workflowId,
        [FromBody] VerifyRemediationRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var workflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == workflowId, ct);
        if (workflow is null)
            return NotFound(new ProblemDetails { Title = "Remediation workflow not found." });

        var auth = await workflowAuthorizationService.EnsureCanVerifyAsync(tenantId, workflowId, ct);
        if (!auth.IsSuccess)
            return BadRequest(new ProblemDetails { Title = auth.Error });

        var action = request.Action?.Trim();
        if (string.IsNullOrWhiteSpace(action))
            return BadRequest(new ProblemDetails { Title = "Verification action is required." });

        if (string.Equals(action, "keepCurrentDecision", StringComparison.OrdinalIgnoreCase))
        {
            if (workflow.RecurrenceSourceWorkflowId is not Guid recurrenceSourceWorkflowId)
                return BadRequest(new ProblemDetails { Title = "This remediation is not a recurrence and cannot carry a previous decision forward." });

            var previousDecision = await dbContext.RemediationDecisions.AsNoTracking()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.RemediationWorkflowId == recurrenceSourceWorkflowId
                    && item.ApprovalStatus == DecisionApprovalStatus.Approved)
                .OrderByDescending(item => item.DecidedAt)
                .FirstOrDefaultAsync(ct);
            if (previousDecision is null)
                return BadRequest(new ProblemDetails { Title = "No approved previous decision is available to carry forward." });

            var verificationResult = await decisionService.VerifyAndCarryForwardDecisionAsync(
                tenantId,
                workflowId,
                previousDecision,
                tenantContext.CurrentUserId,
                ct
            );
            if (!verificationResult.IsSuccess)
                return BadRequest(new ProblemDetails { Title = verificationResult.Error });

            return Ok();
        }

        if (string.Equals(action, "chooseNewDecision", StringComparison.OrdinalIgnoreCase))
        {
            var verificationResult = await decisionService.VerifyAndRequireNewDecisionAsync(
                tenantId,
                workflowId,
                tenantContext.CurrentUserId,
                ct
            );
            if (!verificationResult.IsSuccess)
                return BadRequest(new ProblemDetails { Title = verificationResult.Error });

            return Ok();
        }

        return BadRequest(new ProblemDetails { Title = "Verification action must be 'keepCurrentDecision' or 'chooseNewDecision'." });
    }

    [HttpPost("/api/remediation/{workflowId:guid}/decision")]
    [Authorize(Policy = Policies.UpdateTaskStatus)]
    public async Task<ActionResult<RemediationDecisionDto>> CreateDecisionByWorkflow(
        Guid workflowId,
        [FromBody] CreateDecisionRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var workflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == workflowId, ct);
        if (workflow is null)
            return NotFound(new ProblemDetails { Title = "Remediation workflow not found." });

        var auth = await workflowAuthorizationService.EnsureCanCreateDecisionAsync(tenantId, workflow.TenantSoftwareId, ct);
        if (!auth.IsSuccess)
            return BadRequest(new ProblemDetails { Title = auth.Error });

        if (!Enum.TryParse<RemediationOutcome>(request.Outcome, true, out var outcome))
            return BadRequest(new ProblemDetails { Title = "Invalid outcome value." });

        var result = await decisionService.CreateDecisionForTenantSoftwareAsync(
            tenantId,
            workflow.TenantSoftwareId,
            outcome,
            request.Justification,
            tenantContext.CurrentUserId,
            request.ExpiryDate,
            request.ReEvaluationDate,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var d = result.Value;
        return Created("", new RemediationDecisionDto(
            d.Id, d.Outcome.ToString(), d.ApprovalStatus.ToString(),
            d.Justification, d.DecidedBy, d.DecidedAt,
            d.ApprovedBy, d.ApprovedAt, d.ExpiryDate, d.ReEvaluationDate,
            d.ReopenCount, d.ReopenedAt, null, []
        ));
    }

    [HttpPost("/api/remediation/{workflowId:guid}/approval")]
    [Authorize(Policy = Policies.ResolveApprovalTask)]
    public async Task<IActionResult> ResolveApprovalByWorkflow(
        Guid workflowId,
        [FromBody] ResolveApprovalTaskRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var workflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == workflowId, ct);
        if (workflow is null)
            return NotFound(new ProblemDetails { Title = "Remediation workflow not found." });

        var currentDecision = await dbContext.RemediationDecisions.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.RemediationWorkflowId == workflowId
                && (item.ApprovalStatus == DecisionApprovalStatus.PendingApproval || item.ApprovalStatus == DecisionApprovalStatus.Reopened))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (currentDecision is null)
            return BadRequest(new ProblemDetails { Title = "No pending approval is active for this remediation." });

        var auth = await workflowAuthorizationService.EnsureCanApproveOrRejectDecisionAsync(
            tenantId,
            currentDecision.Id,
            request.Action,
            ct
        );
        if (!auth.IsSuccess)
            return BadRequest(new ProblemDetails { Title = auth.Error });

        var approvalTask = await dbContext.ApprovalTasks.AsNoTracking()
            .Where(task =>
                task.TenantId == tenantId
                && task.RemediationDecisionId == currentDecision.Id
                && task.Status == ApprovalTaskStatus.Pending)
            .OrderByDescending(task => task.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (approvalTask is null)
            return BadRequest(new ProblemDetails { Title = "No pending approval task exists for this remediation." });

        try
        {
            var action = request.Action?.ToLowerInvariant();
            if (action == "approve")
            {
                await approvalTaskService.ApproveAsync(approvalTask.Id, tenantContext.CurrentUserId, request.Justification, ct);
            }
            else if (action == "deny" || action == "reject")
            {
                await approvalTaskService.DenyAsync(approvalTask.Id, tenantContext.CurrentUserId, request.Justification, ct);
            }
            else
            {
                return BadRequest(new ProblemDetails { Title = "Action must be 'approve' or 'deny'." });
            }

            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
    }

    [HttpPost("/api/remediation/{workflowId:guid}/decision/{decisionId:guid}/cancel")]
    [Authorize(Policy = Policies.UpdateTaskStatus)]
    public async Task<IActionResult> CancelDecisionByWorkflow(
        Guid workflowId,
        Guid decisionId,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var workflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == workflowId, ct);
        if (workflow is null)
            return NotFound(new ProblemDetails { Title = "Remediation workflow not found." });

        var auth = await workflowAuthorizationService.EnsureCanApproveOrRejectDecisionAsync(
            tenantId,
            decisionId,
            "cancel",
            ct
        );
        if (!auth.IsSuccess)
            return BadRequest(new ProblemDetails { Title = auth.Error });

        var result = await decisionService.ExpireAsync(decisionId, ct);
        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return Ok();
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

        var workflowIds = await dbContext.RemediationWorkflows.AsNoTracking()
            .Where(workflow => workflow.TenantId == tenantId && workflow.TenantSoftwareId == tenantSoftwareId)
            .Select(workflow => workflow.Id)
            .ToListAsync(ct);

        var verificationStageIds = workflowIds.Count > 0
            ? await dbContext.RemediationWorkflowStageRecords.AsNoTracking()
                .Where(record =>
                    workflowIds.Contains(record.RemediationWorkflowId)
                    && record.Stage == RemediationWorkflowStage.Verification)
                .Select(record => record.Id)
                .ToListAsync(ct)
            : [];

        if (decisionIds.Count == 0 && verificationStageIds.Count == 0)
            return Ok(new List<ApprovalAuditEntryDto>());

        var entries = await dbContext.AuditLogEntries.AsNoTracking()
            .Where(entry =>
                (entry.EntityType == "RemediationDecision" && decisionIds.Contains(entry.EntityId))
                || (entry.EntityType == "RemediationWorkflowStageRecord" && verificationStageIds.Contains(entry.EntityId)))
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
