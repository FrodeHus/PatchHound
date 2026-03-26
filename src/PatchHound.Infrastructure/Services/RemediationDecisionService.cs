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
    public async Task<Result<RemediationDecision>> CreateDecisionAsync(
        Guid tenantId,
        Guid softwareAssetId,
        RemediationOutcome outcome,
        string? justification,
        Guid decidedBy,
        DateTimeOffset? expiryDate,
        DateTimeOffset? reEvaluationDate,
        CancellationToken ct
    )
    {
        var remediationScope = await ResolveScopeAsync(tenantId, softwareAssetId, ct);
        if (remediationScope is null)
            return Result<RemediationDecision>.Failure("No tenant software scope was found for this software.");

        var hasOpenDecision = await dbContext.RemediationDecisions
            .Where(d =>
                d.TenantId == tenantId
                && d.TenantSoftwareId == remediationScope.TenantSoftwareId
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
                "An active remediation decision already exists for this software. Reject or expire the current decision before creating a new one.");

        var existingWorkflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .Where(workflow =>
                workflow.TenantId == tenantId
                && workflow.TenantSoftwareId == remediationScope.TenantSoftwareId
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
            remediationScope.TenantSoftwareId,
            remediationScope.RepresentativeSoftwareAssetId,
            outcome,
            justification,
            decidedBy,
            initialApprovalStatus,
            expiryDate,
            reEvaluationDate
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
        var expiryHours = tenantSla?.ApprovalExpiryHours ?? 24;
        await approvalTaskService.CreateForDecisionAsync(decision, expiryHours, ct);

        return Result<RemediationDecision>.Success(decision);
    }

    public async Task<Result<RemediationDecision>> CreateDecisionForTenantSoftwareAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        RemediationOutcome outcome,
        string? justification,
        Guid decidedBy,
        DateTimeOffset? expiryDate,
        DateTimeOffset? reEvaluationDate,
        CancellationToken ct
    )
    {
        var remediationScope = await ResolveScopeByTenantSoftwareAsync(tenantId, tenantSoftwareId, ct);
        if (remediationScope is null)
            return Result<RemediationDecision>.Failure("No tenant software scope was found for this software.");

        return await CreateDecisionAsync(
            tenantId,
            remediationScope.RepresentativeSoftwareAssetId,
            outcome,
            justification,
            decidedBy,
            expiryDate,
            reEvaluationDate,
            ct
        );
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

        var remediationScope = await ResolveScopeByTenantSoftwareAsync(tenantId, previousDecision.TenantSoftwareId, ct);
        if (remediationScope is null)
            return Result<RemediationDecision>.Failure("No tenant software scope was found for this software.");

        var carriedApprovalStatus = previousDecision.Outcome == RemediationOutcome.ApprovedForPatching
            ? DecisionApprovalStatus.Approved
            : DecisionApprovalStatus.PendingApproval;

        var decision = RemediationDecision.Create(
            tenantId,
            previousDecision.TenantSoftwareId,
            remediationScope.RepresentativeSoftwareAssetId,
            previousDecision.Outcome,
            previousDecision.Justification,
            actedBy,
            carriedApprovalStatus,
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
        var expiryHours = tenantSla?.ApprovalExpiryHours ?? 24;
        await approvalTaskService.CreateForDecisionAsync(decision, expiryHours, ct);

        return Result<RemediationDecision>.Success(decision);
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

        decision.Expire();
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

        var tenantSoftwareIds = activeWorkflows
            .Select(workflow => workflow.TenantSoftwareId)
            .Distinct()
            .ToList();

        var unresolvedTenantSoftwareIds = await dbContext.NormalizedSoftwareVulnerabilityProjections
            .IgnoreQueryFilters()
            .Where(p =>
                p.TenantId == tenantId
                && p.SnapshotId == snapshotId
                && p.ResolvedAt == null
                && tenantSoftwareIds.Contains(p.TenantSoftwareId))
            .Select(p => p.TenantSoftwareId)
            .Distinct()
            .ToListAsync(ct);

        var workflowsToClose = activeWorkflows
            .Where(workflow => !unresolvedTenantSoftwareIds.Contains(workflow.TenantSoftwareId))
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
        Guid tenantVulnerabilityId,
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
                vo => vo.RemediationDecisionId == decisionId && vo.TenantVulnerabilityId == tenantVulnerabilityId,
                ct
            );

        if (existing is not null)
            return Result<RemediationDecisionVulnerabilityOverride>.Failure(
                "An override already exists for this vulnerability on this decision."
            );

        var overrideEntity = RemediationDecisionVulnerabilityOverride.Create(
            decisionId,
            tenantVulnerabilityId,
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

    private async Task<RemediationScope?> ResolveScopeAsync(
        Guid tenantId,
        Guid softwareAssetId,
        CancellationToken ct
    )
    {
        var scopeRow = await dbContext.NormalizedSoftwareInstallations
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.SoftwareAssetId == softwareAssetId
                && item.IsActive)
            .Select(item => new { item.TenantSoftwareId })
            .FirstOrDefaultAsync(ct);

        if (scopeRow is null)
            return null;

        var representativeSoftwareAssetId = await dbContext.NormalizedSoftwareInstallations
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.TenantSoftwareId == scopeRow.TenantSoftwareId
                && item.IsActive)
            .Select(item => item.SoftwareAssetId)
            .Distinct()
            .OrderBy(id => id)
            .FirstAsync(ct);

        return new RemediationScope(scopeRow.TenantSoftwareId, representativeSoftwareAssetId);
    }

    private async Task<RemediationScope?> ResolveScopeByTenantSoftwareAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        var representativeSoftwareAssetId = await dbContext.NormalizedSoftwareInstallations
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.TenantSoftwareId == tenantSoftwareId
                && item.IsActive)
            .Select(item => item.SoftwareAssetId)
            .Distinct()
            .OrderBy(id => id)
            .FirstOrDefaultAsync(ct);

        return representativeSoftwareAssetId == Guid.Empty
            ? null
            : new RemediationScope(tenantSoftwareId, representativeSoftwareAssetId);
    }

    private sealed record RemediationScope(Guid TenantSoftwareId, Guid RepresentativeSoftwareAssetId);
}
