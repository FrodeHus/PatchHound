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
    RemediationAiJobService remediationAiJobService,
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

        var remediationCaseId = await ResolveCaseIdAsync(tenantId, tenantSoftwareId, ct);
        if (remediationCaseId is null)
            return NotFound();

        var result = await queryService.BuildByCaseIdAsync(tenantId, remediationCaseId.Value, false, ct);
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

        var remediationCaseId = await ResolveCaseIdAsync(tenantId, tenantSoftwareId, ct);
        if (remediationCaseId is null)
            return NotFound(new ProblemDetails { Title = "Remediation case not found for this software." });

        var workflow = await workflowService.GetOrCreateActiveWorkflowAsync(
            tenantId,
            remediationCaseId.Value,
            ct
        );
        await dbContext.SaveChangesAsync(ct);
        await EnqueueAiDraftsAsync(tenantId, remediationCaseId.Value, ct);
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
            decisionId,
            request.VulnerabilityId,
            outcome,
            request.Justification,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var remediationCaseId = await ResolveCaseIdAsync(tenantId, tenantSoftwareId, ct);
        var vo = result.Value;
        if (remediationCaseId is not null)
            await EnqueueAiDraftsAsync(tenantId, remediationCaseId.Value, ct);
        return Created("", new VulnerabilityOverrideDto(
            vo.Id, vo.VulnerabilityId, vo.Outcome.ToString(), vo.Justification, vo.CreatedAt
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

        var remediationCaseId = await ResolveCaseIdAsync(tenantId, tenantSoftwareId, ct);
        if (remediationCaseId is null)
            return NotFound();

        var context = await queryService.BuildByCaseIdAsync(tenantId, remediationCaseId.Value, false, ct);
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

        var auth = await workflowAuthorizationService.EnsureCanAddRecommendationAsync(tenantId, workflow.RemediationCaseId, ct);
        if (!auth.IsSuccess)
            return BadRequest(new ProblemDetails { Title = auth.Error });

        if (!Enum.TryParse<RemediationOutcome>(request.RecommendedOutcome, true, out var outcome))
            return BadRequest(new ProblemDetails { Title = "Invalid outcome value." });

        var userId = tenantContext.CurrentUserId;
        var result = await recommendationService.AddRecommendationForCaseAsync(
            tenantId,
            workflow.RemediationCaseId,
            outcome,
            request.Rationale,
            userId,
            request.VulnerabilityId,
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
        await EnqueueAiDraftsAsync(tenantId, workflow.RemediationCaseId, ct);
        return Created("", new AnalystRecommendationDto(
            r.Id, r.VulnerabilityId, r.RecommendedOutcome.ToString(),
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

            await EnqueueAiDraftsAsync(tenantId, workflow.RemediationCaseId, ct);
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

            await EnqueueAiDraftsAsync(tenantId, workflow.RemediationCaseId, ct);
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

        var auth = await workflowAuthorizationService.EnsureCanCreateDecisionAsync(tenantId, workflow.RemediationCaseId, ct);
        if (!auth.IsSuccess)
            return BadRequest(new ProblemDetails { Title = auth.Error });

        if (!Enum.TryParse<RemediationOutcome>(request.Outcome, true, out var outcome))
            return BadRequest(new ProblemDetails { Title = "Invalid outcome value." });

        var result = await decisionService.CreateDecisionForCaseAsync(
            tenantId,
            workflow.RemediationCaseId,
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
        await EnqueueAiDraftsAsync(tenantId, workflow.RemediationCaseId, ct);
        return Created("", new RemediationDecisionDto(
            d.Id, d.Outcome.ToString(), d.ApprovalStatus.ToString(),
            d.Justification, d.DecidedBy, d.DecidedAt,
            d.ApprovedBy, d.ApprovedAt, d.MaintenanceWindowDate, d.ExpiryDate, d.ReEvaluationDate,
            null,
            []
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
                && item.ApprovalStatus == DecisionApprovalStatus.PendingApproval)
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
                await approvalTaskService.ApproveAsync(approvalTask.Id, tenantContext.CurrentUserId, request.Justification, request.MaintenanceWindowDate, ct);
            }
            else if (action == "deny" || action == "reject")
            {
                await approvalTaskService.DenyAsync(approvalTask.Id, tenantContext.CurrentUserId, request.Justification, ct);
            }
            else
            {
                return BadRequest(new ProblemDetails { Title = "Action must be 'approve' or 'deny'." });
            }

            await EnqueueAiDraftsAsync(tenantId, workflow.RemediationCaseId, ct);
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

        await EnqueueAiDraftsAsync(tenantId, workflow.RemediationCaseId, ct);
        return Ok();
    }

    [HttpPost("ai-summary")]
    [Authorize(Policy = Policies.GenerateAiReports)]
    public async Task<ActionResult<DecisionAiSummaryDto>> GenerateAiSummary(
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var remediationCaseId = await ResolveCaseIdAsync(tenantId, tenantSoftwareId, ct);
        if (remediationCaseId is null)
            return NotFound(new ProblemDetails { Title = "Remediation case not found for this software." });

        await EnqueueAiDraftsAsync(tenantId, remediationCaseId.Value, ct);
        var context = await queryService.BuildByCaseIdAsync(tenantId, remediationCaseId.Value, false, ct);
        if (context is null)
            return NotFound(new ProblemDetails { Title = "Remediation context not found." });

        if (!context.AiSummary.CanGenerate && string.IsNullOrWhiteSpace(context.AiSummary.Content))
        {
            return BadRequest(new ProblemDetails
            {
                Title = context.AiSummary.UnavailableMessage ?? "No enabled default AI profile is configured for this tenant."
            });
        }

        return Ok(context.AiSummary);
    }

    [HttpPost("ai-summary/review")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<DecisionAiSummaryDto>> ReviewAiSummary(
        Guid tenantSoftwareId,
        [FromBody] ReviewDecisionAiSummaryRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        // TenantSoftware still exists as snapshot-versioned table; AI review state lives there for now (TODO Phase 5)
        var tenantSoftware = await dbContext.TenantSoftware
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == tenantSoftwareId, ct);
        if (tenantSoftware is null)
            return NotFound(new ProblemDetails { Title = "Remediation context not found." });

        var action = request.Action?.Trim();
        if (string.IsNullOrWhiteSpace(action))
            return BadRequest(new ProblemDetails { Title = "Review action is required." });

        var reviewStatus = action.ToLowerInvariant() switch
        {
            "accept" => "Accepted",
            "edit" => "Edited",
            "reject" => "Rejected",
            _ => null
        };
        if (reviewStatus is null)
            return BadRequest(new ProblemDetails { Title = "Review action must be accept, edit, or reject." });

        tenantSoftware.MarkRemediationAiReviewed(reviewStatus, tenantContext.CurrentUserId);
        await dbContext.SaveChangesAsync(ct);

        var remediationCaseId = await ResolveCaseIdAsync(tenantId, tenantSoftwareId, ct);
        if (remediationCaseId is null)
            return NotFound(new ProblemDetails { Title = "Remediation context not found." });

        var context = await queryService.BuildByCaseIdAsync(tenantId, remediationCaseId.Value, false, ct);
        if (context is null)
            return NotFound(new ProblemDetails { Title = "Remediation context not found." });

        return Ok(context.AiSummary);
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

        var remediationCaseId = await ResolveCaseIdAsync(tenantId, tenantSoftwareId, ct);
        if (remediationCaseId is null)
            return Ok(new List<ApprovalAuditEntryDto>());

        var decisionIds = await dbContext.RemediationDecisions.AsNoTracking()
            .Where(decision => decision.TenantId == tenantId && decision.RemediationCaseId == remediationCaseId.Value)
            .Select(decision => decision.Id)
            .ToListAsync(ct);

        var workflowIds = await dbContext.RemediationWorkflows.AsNoTracking()
            .Where(workflow => workflow.TenantId == tenantId && workflow.RemediationCaseId == remediationCaseId.Value)
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

    /// <summary>
    /// Resolves a RemediationCase ID from a TenantSoftware ID by joining through
    /// NormalizedSoftware.CanonicalProductKey → SoftwareProduct.CanonicalProductKey.
    /// Returns null if no matching RemediationCase exists.
    /// </summary>
    private async Task<Guid?> ResolveCaseIdAsync(Guid tenantId, Guid tenantSoftwareId, CancellationToken ct)
    {
        var canonicalKey = await dbContext.TenantSoftware.AsNoTracking()
            .Where(ts => ts.TenantId == tenantId && ts.Id == tenantSoftwareId)
            .Join(dbContext.NormalizedSoftware.AsNoTracking(),
                ts => ts.NormalizedSoftwareId,
                ns => ns.Id,
                (ts, ns) => ns.CanonicalProductKey)
            .FirstOrDefaultAsync(ct);

        if (canonicalKey is null)
            return null;

        return await dbContext.RemediationCases.AsNoTracking()
            .Where(rc => rc.TenantId == tenantId)
            .Join(dbContext.SoftwareProducts.AsNoTracking(),
                rc => rc.SoftwareProductId,
                sp => sp.Id,
                (rc, sp) => new { rc.Id, sp.CanonicalProductKey })
            .Where(x => x.CanonicalProductKey == canonicalKey)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task EnqueueAiDraftsAsync(Guid tenantId, Guid remediationCaseId, CancellationToken ct)
    {
        await remediationAiJobService.EnqueueAsync(tenantId, remediationCaseId, string.Empty, ct);
    }
}
