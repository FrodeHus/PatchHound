using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RemediationDecisionService(
    PatchHoundDbContext dbContext,
    SlaService slaService,
    ApprovalTaskService approvalTaskService,
    RemediationWorkflowService remediationWorkflowService
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

        var decision = RemediationDecision.Create(
            tenantId,
            remediationScope.TenantSoftwareId,
            remediationScope.RepresentativeSoftwareAssetId,
            outcome,
            justification,
            decidedBy,
            expiryDate,
            reEvaluationDate
        );

        await dbContext.RemediationDecisions.AddAsync(decision, ct);
        await remediationWorkflowService.AttachDecisionAsync(decision, ct);

        if (decision.Outcome == RemediationOutcome.ApprovedForPatching
            && decision.ApprovalStatus == DecisionApprovalStatus.Approved)
        {
            await EnsurePatchingTasksAsync(decision, ct);
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

        return await CreateDecisionForTenantSoftwareAsync(
            tenantId,
            previousDecision.TenantSoftwareId,
            previousDecision.Outcome,
            previousDecision.Justification,
            actedBy,
            previousDecision.ExpiryDate,
            previousDecision.ReEvaluationDate,
            ct
        );
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
            await EnsurePatchingTasksAsync(decision, ct);
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
        var openPatchingDecisions = await dbContext.RemediationDecisions
            .Where(d =>
                d.TenantId == tenantId
                && d.Outcome == RemediationOutcome.ApprovedForPatching
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
            .ToListAsync(ct);

        if (openPatchingDecisions.Count == 0)
            return 0;

        var tenantSoftwareIds = openPatchingDecisions
            .Select(d => d.TenantSoftwareId)
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

        var decisionsToClose = openPatchingDecisions
            .Where(d => !unresolvedTenantSoftwareIds.Contains(d.TenantSoftwareId))
            .ToList();

        if (decisionsToClose.Count == 0)
            return 0;

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

        foreach (var decision in decisionsToClose)
        {
            decision.Expire();
            await remediationWorkflowService.MarkPatchedWorkflowClosedAsync(decision, ct);
        }

        await dbContext.SaveChangesAsync(ct);
        return decisionsToClose.Count;
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

        return await EnsurePatchingTasksAsync(decision, ct);
    }

    private async Task<int> EnsurePatchingTasksAsync(
        RemediationDecision decision,
        CancellationToken ct
    )
    {
        var scopedInstallations = await dbContext.NormalizedSoftwareInstallations
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == decision.TenantId
                && item.TenantSoftwareId == decision.TenantSoftwareId
                && item.IsActive)
            .Select(item => new { item.DeviceAssetId, item.SoftwareAssetId })
            .ToListAsync(ct);

        if (scopedInstallations.Count == 0)
            return 0;

        var deviceAssetIds = scopedInstallations.Select(d => d.DeviceAssetId).Distinct().ToList();
        var scopedSoftwareAssetIds = scopedInstallations.Select(item => item.SoftwareAssetId).Distinct().ToList();

        var deviceTeams = await dbContext.Assets
            .IgnoreQueryFilters()
            .Where(a => deviceAssetIds.Contains(a.Id) && a.TenantId == decision.TenantId)
            .Select(a => new { a.Id, a.OwnerTeamId, a.FallbackTeamId })
            .ToListAsync(ct);

        var teamGroups = deviceTeams
            .Select(d => d.OwnerTeamId ?? d.FallbackTeamId)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        if (teamGroups.Count == 0)
            return 0;

        var representativeSoftwareAssetId = decision.SoftwareAssetId;

        // Determine due date from highest severity vulnerability
        var tenantSla = await dbContext.TenantSlaConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == decision.TenantId, ct);

        var highestSeverity = await dbContext.SoftwareVulnerabilityMatches
            .IgnoreQueryFilters()
            .Where(svm =>
                svm.TenantId == decision.TenantId
                && svm.ResolvedAt == null
                && scopedSoftwareAssetIds.Contains(svm.SoftwareAssetId))
            .Join(
                dbContext.VulnerabilityDefinitions,
                svm => svm.VulnerabilityDefinitionId,
                vd => vd.Id,
                (svm, vd) => vd.VendorSeverity
            )
            .OrderByDescending(s => s)
            .FirstOrDefaultAsync(ct);

        var dueDate = slaService.CalculateDueDate(
            highestSeverity != default ? highestSeverity : Severity.Medium,
            DateTimeOffset.UtcNow,
            tenantSla
        );

        var existingOpenTeamIds = await dbContext.PatchingTasks
            .IgnoreQueryFilters()
            .Where(task =>
                task.TenantId == decision.TenantId
                && task.TenantSoftwareId == decision.TenantSoftwareId
                && task.Status != PatchingTaskStatus.Completed)
            .Select(task => task.OwnerTeamId)
            .Distinct()
            .ToListAsync(ct);

        var tasks = teamGroups
            .Where(teamId => !existingOpenTeamIds.Contains(teamId))
            .Select(group => PatchingTask.Create(
                decision.TenantId,
                decision.Id,
                decision.TenantSoftwareId,
                representativeSoftwareAssetId,
                group,
                dueDate
            ))
            .ToList();

        if (tasks.Count == 0)
            return 0;

        foreach (var task in tasks)
        {
            await remediationWorkflowService.AttachPatchingTaskAsync(task, decision, ct);
        }

        await dbContext.PatchingTasks.AddRangeAsync(tasks, ct);
        return tasks.Count;
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
