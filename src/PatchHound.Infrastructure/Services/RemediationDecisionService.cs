using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RemediationDecisionService(
    PatchHoundDbContext dbContext,
    ApprovalTaskService approvalTaskService,
    RemediationWorkflowService remediationWorkflowService,
    PatchingTaskService patchingTaskService
)
{
    private const int DefaultApprovalExpiryHours = 24;

    public async Task<Result<RemediationDecision>> CreateDecisionForCaseAsync(
        Guid tenantId,
        Guid remediationCaseId,
        RemediationOutcome outcome,
        string? justification,
        Guid decidedBy,
        DateTimeOffset? expiryDate,
        DateTimeOffset? reEvaluationDate,
        CancellationToken ct,
        DateTimeOffset? maintenanceWindowDate = null
    )
    {
        var caseExists = await dbContext.RemediationCases
            .AnyAsync(c => c.TenantId == tenantId && c.Id == remediationCaseId, ct);
        if (!caseExists)
            return Result<RemediationDecision>.Failure("Remediation case not found.");

        var hasOpenDecision = await dbContext.RemediationDecisions
            .Where(d =>
                d.TenantId == tenantId
                && d.RemediationCaseId == remediationCaseId
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
            .AnyAsync(
                d => !d.RemediationWorkflowId.HasValue
                    || dbContext.RemediationWorkflows.Any(workflow =>
                        workflow.Id == d.RemediationWorkflowId.Value
                        && workflow.Status == RemediationWorkflowStatus.Active),
                ct
            );

        if (hasOpenDecision)
            return Result<RemediationDecision>.Failure(
                "An active remediation decision already exists for this case. Reject or expire the current decision before creating a new one.");

        var existingWorkflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .Where(workflow =>
                workflow.TenantId == tenantId
                && workflow.RemediationCaseId == remediationCaseId
                && workflow.Status == RemediationWorkflowStatus.Active)
            .OrderByDescending(workflow => workflow.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var priority = existingWorkflow?.Priority ?? RemediationWorkflowPriority.Normal;
        var approvalMode = RemediationWorkflowService.ResolveApprovalMode(outcome, priority);
        var initialApprovalStatus = approvalMode is RemediationWorkflowApprovalMode.SecurityApproval or RemediationWorkflowApprovalMode.TechnicalApproval
            ? DecisionApprovalStatus.PendingApproval
            : DecisionApprovalStatus.Approved;

        var decision = RemediationDecision.Create(
            tenantId,
            remediationCaseId,
            outcome,
            justification,
            decidedBy,
            initialApprovalStatus,
            expiryDate,
            reEvaluationDate,
            maintenanceWindowDate
        );

        await dbContext.RemediationDecisions.AddAsync(decision, ct);
        await remediationWorkflowService.AttachDecisionAsync(decision, ct);

        if (decision.Outcome == RemediationOutcome.ApprovedForPatching
            && decision.ApprovalStatus == DecisionApprovalStatus.Approved)
        {
            await patchingTaskService.EnsurePatchingTasksAsync(decision, ct);
        }

        await dbContext.SaveChangesAsync(ct);

        // Create approval task for the decision
        var tenantSla = await dbContext.TenantSlaConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        var expiryHours = NormalizeApprovalExpiryHours(tenantSla?.ApprovalExpiryHours);
        await approvalTaskService.CreateForDecisionAsync(decision, expiryHours, ct);

        return Result<RemediationDecision>.Success(decision);
    }

    public async Task<Result<RemediationDecision>> VerifyAndCarryForwardDecisionAsync(
        Guid tenantId,
        Guid workflowId,
        RemediationDecision previousDecision,
        Guid actedBy,
        CancellationToken ct
    )
    {
        var verificationResult = await remediationWorkflowService.VerifyRecurringWorkflowAsync(
            tenantId,
            workflowId,
            actedBy,
            "keepCurrentDecision",
            ct
        );
        if (!verificationResult.IsSuccess)
            return Result<RemediationDecision>.Failure(verificationResult.Error ?? "Verification failed.");

        var carriedApprovalStatus = previousDecision.Outcome == RemediationOutcome.ApprovedForPatching
            ? DecisionApprovalStatus.Approved
            : DecisionApprovalStatus.PendingApproval;

        var decision = RemediationDecision.Create(
            tenantId,
            previousDecision.RemediationCaseId,
            previousDecision.Outcome,
            previousDecision.Justification,
            actedBy,
            carriedApprovalStatus,
            previousDecision.MaintenanceWindowDate,
            previousDecision.ExpiryDate,
            previousDecision.ReEvaluationDate
        );

        await dbContext.RemediationDecisions.AddAsync(decision, ct);
        await remediationWorkflowService.AttachDecisionAsync(decision, ct);

        if (decision.Outcome == RemediationOutcome.ApprovedForPatching
            && decision.ApprovalStatus == DecisionApprovalStatus.Approved)
        {
            await patchingTaskService.EnsurePatchingTasksAsync(decision, ct);
        }

        await dbContext.SaveChangesAsync(ct);

        var tenantSla = await dbContext.TenantSlaConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        var expiryHours = NormalizeApprovalExpiryHours(tenantSla?.ApprovalExpiryHours);
        await approvalTaskService.CreateForDecisionAsync(decision, expiryHours, ct);

        return Result<RemediationDecision>.Success(decision);
    }

    private static int NormalizeApprovalExpiryHours(int? approvalExpiryHours)
    {
        return approvalExpiryHours is > 0
            ? approvalExpiryHours.Value
            : DefaultApprovalExpiryHours;
    }

    public async Task<Result<bool>> VerifyAndRequireNewDecisionAsync(
        Guid tenantId,
        Guid workflowId,
        Guid actedBy,
        CancellationToken ct
    )
    {
        var verificationResult = await remediationWorkflowService.VerifyRecurringWorkflowAsync(
            tenantId,
            workflowId,
            actedBy,
            "chooseNewDecision",
            ct
        );
        return verificationResult;
    }

    public async Task<Result<RemediationDecision>> ApproveAsync(
        Guid decisionId,
        Guid approvedBy,
        CancellationToken ct
    )
    {
        var decision = await dbContext.RemediationDecisions
            .FirstOrDefaultAsync(d => d.Id == decisionId, ct);

        if (decision is null)
            return Result<RemediationDecision>.Failure("Decision not found.");

        decision.Approve(approvedBy);

        if (decision.Outcome == RemediationOutcome.ApprovedForPatching)
        {
            await patchingTaskService.EnsurePatchingTasksAsync(decision, ct);
        }

        await dbContext.SaveChangesAsync(ct);
        return Result<RemediationDecision>.Success(decision);
    }

    public async Task<Result<RemediationDecision>> RejectAsync(
        Guid decisionId,
        Guid rejectedBy,
        CancellationToken ct
    )
    {
        var decision = await dbContext.RemediationDecisions
            .FirstOrDefaultAsync(d => d.Id == decisionId, ct);

        if (decision is null)
            return Result<RemediationDecision>.Failure("Decision not found.");

        decision.Reject(rejectedBy);
        await dbContext.SaveChangesAsync(ct);

        return Result<RemediationDecision>.Success(decision);
    }

    public async Task<Result<RemediationDecision>> ExpireAsync(
        Guid decisionId,
        CancellationToken ct
    )
    {
        var decision = await dbContext.RemediationDecisions
            .FirstOrDefaultAsync(d => d.Id == decisionId, ct);

        if (decision is null)
            return Result<RemediationDecision>.Failure("Decision not found.");

        var pendingApprovalTasks = await dbContext.ApprovalTasks
            .Where(task =>
                task.RemediationDecisionId == decisionId
                && task.Status == ApprovalTaskStatus.Pending)
            .ToListAsync(ct);

        foreach (var task in pendingApprovalTasks)
        {
            task.AutoDeny();
        }

        decision.Expire();
        await remediationWorkflowService.HandleDecisionCancelledAsync(decision, ct);
        await dbContext.SaveChangesAsync(ct);

        return Result<RemediationDecision>.Success(decision);
    }

    public async Task<int> ReconcileResolvedSoftwareRemediationsAsync(
        Guid tenantId,
        Guid? snapshotId,
        CancellationToken ct
    )
    {
        var activeWorkflows = await dbContext.RemediationWorkflows
            .Where(workflow =>
                workflow.TenantId == tenantId
                && workflow.Status == RemediationWorkflowStatus.Active)
            .ToListAsync(ct);

        if (activeWorkflows.Count == 0)
            return 0;

        var remediationCaseIds = activeWorkflows
            .Select(workflow => workflow.RemediationCaseId)
            .Distinct()
            .ToList();

        // Phase 2: canonical exposure data not yet available via DeviceVulnerabilityExposure.
        // Return empty — no open exposure means all active workflows are eligible to close.
        var unresolvedCaseIds = new List<Guid>();

        var workflowsToClose = activeWorkflows
            .Where(workflow => !unresolvedCaseIds.Contains(workflow.RemediationCaseId))
            .ToList();

        if (workflowsToClose.Count == 0)
            return 0;

        var workflowIds = workflowsToClose.Select(workflow => workflow.Id).ToList();
        var decisionsToClose = await dbContext.RemediationDecisions
            .Where(d =>
                d.TenantId == tenantId
                && d.RemediationWorkflowId.HasValue
                && workflowIds.Contains(d.RemediationWorkflowId.Value)
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
            .ToListAsync(ct);
        var decisionIds = decisionsToClose.Select(d => d.Id).ToList();
        var openTasks = await dbContext.PatchingTasks
            .Where(task =>
                task.TenantId == tenantId
                && decisionIds.Contains(task.RemediationDecisionId)
                && task.Status != PatchingTaskStatus.Completed)
            .ToListAsync(ct);

        foreach (var task in openTasks)
        {
            task.Complete();
        }

        var pendingApprovalTasks = decisionIds.Count == 0
            ? []
            : await dbContext.ApprovalTasks
                .Where(task =>
                    task.TenantId == tenantId
                    && decisionIds.Contains(task.RemediationDecisionId)
                    && task.Status == ApprovalTaskStatus.Pending)
                .ToListAsync(ct);

        foreach (var task in pendingApprovalTasks)
        {
            task.AutoDeny();
        }

        foreach (var decision in decisionsToClose)
        {
            decision.Expire();
        }

        foreach (var workflow in workflowsToClose)
        {
            await remediationWorkflowService.MarkWorkflowClosedForResolvedExposureAsync(workflow, ct);
        }

        await dbContext.SaveChangesAsync(ct);
        return workflowsToClose.Count;
    }

    public async Task<Result<RemediationDecisionVulnerabilityOverride>> AddVulnerabilityOverrideAsync(
        Guid decisionId,
        Guid vulnerabilityId,
        RemediationOutcome outcome,
        string justification,
        CancellationToken ct
    )
    {
        var decision = await dbContext.RemediationDecisions
            .FirstOrDefaultAsync(d => d.Id == decisionId, ct);

        if (decision is null)
            return Result<RemediationDecisionVulnerabilityOverride>.Failure("Decision not found.");

        var existing = await dbContext.RemediationDecisionVulnerabilityOverrides
            .FirstOrDefaultAsync(
                vo => vo.RemediationDecisionId == decisionId && vo.VulnerabilityId == vulnerabilityId,
                ct
            );

        if (existing is not null)
            return Result<RemediationDecisionVulnerabilityOverride>.Failure(
                "An override already exists for this vulnerability on this decision."
            );

        var overrideEntity = RemediationDecisionVulnerabilityOverride.Create(
            decisionId,
            vulnerabilityId,
            outcome,
            justification
        );

        await dbContext.RemediationDecisionVulnerabilityOverrides.AddAsync(overrideEntity, ct);
        await dbContext.SaveChangesAsync(ct);

        return Result<RemediationDecisionVulnerabilityOverride>.Success(overrideEntity);
    }

    public async Task<int> EnsurePatchingTasksAsync(
        Guid decisionId,
        CancellationToken ct
    )
    {
        var decision = await dbContext.RemediationDecisions
            .FirstOrDefaultAsync(d => d.Id == decisionId, ct);

        if (decision is null)
            return 0;

        return await patchingTaskService.EnsurePatchingTasksAsync(decision, ct);
    }
}
