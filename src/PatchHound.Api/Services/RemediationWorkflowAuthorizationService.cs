using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Services;

public class RemediationWorkflowAuthorizationService(
    PatchHoundDbContext dbContext,
    ITenantContext tenantContext
)
{
    public async Task<Result<bool>> EnsureCanAddRecommendationAsync(
        Guid tenantId,
        Guid remediationCaseId,
        CancellationToken ct
    )
    {
        var workflow = await GetActiveWorkflowAsync(tenantId, remediationCaseId, ct);
        if (workflow is null)
            return EnsureSecurityAnalysisRole();

        if (workflow.CurrentStage != RemediationWorkflowStage.SecurityAnalysis)
            return Result<bool>.Failure($"Recommendations can only be added during the Security Analysis stage. Current stage: {workflow.CurrentStage}.");

        return EnsureSecurityAnalysisRole();
    }

    public async Task<Result<bool>> EnsureCanVerifyAsync(
        Guid tenantId,
        Guid workflowId,
        CancellationToken ct
    )
    {
        var workflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.TenantId == tenantId
                && item.Id == workflowId
                && item.Status == RemediationWorkflowStatus.Active,
                ct);
        if (workflow is null)
            return Result<bool>.Failure("Remediation workflow not found.");

        if (workflow.CurrentStage != RemediationWorkflowStage.Verification)
            return Result<bool>.Failure($"Verification can only be completed during the Verification stage. Current stage: {workflow.CurrentStage}.");

        return workflow.ProposedOutcome switch
        {
            RemediationOutcome.RiskAcceptance or RemediationOutcome.AlternateMitigation =>
                EnsureSecurityManagerApprovalAccess(tenantId),
            _ => await EnsureSoftwareOwnerDecisionAccessAsync(tenantId, workflow, ct),
        };
    }

    public async Task<Result<bool>> EnsureCanCreateDecisionAsync(
        Guid tenantId,
        Guid remediationCaseId,
        CancellationToken ct
    )
    {
        var workflow = await GetActiveWorkflowAsync(tenantId, remediationCaseId, ct);
        if (workflow is null)
            return Result<bool>.Failure("No active remediation workflow exists for this software.");

        if (workflow.CurrentStage is not RemediationWorkflowStage.RemediationDecision
            and not RemediationWorkflowStage.SecurityAnalysis)
            return Result<bool>.Failure($"A remediation decision cannot be created while the workflow is in the {workflow.CurrentStage} stage.");

        return await EnsureSoftwareOwnerDecisionAccessAsync(tenantId, workflow, ct);
    }

    public async Task<Result<bool>> EnsureCanApproveOrRejectDecisionAsync(
        Guid tenantId,
        Guid decisionId,
        string action,
        CancellationToken ct
    )
    {
        var decision = await dbContext.RemediationDecisions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == decisionId, ct);
        if (decision is null)
            return Result<bool>.Failure("Decision not found.");

        if (string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            var workflow = await GetWorkflowForDecisionAsync(decision, ct);
            if (workflow is null)
                return Result<bool>.Failure("No remediation workflow is linked to this decision.");

            if (workflow.CurrentStage != RemediationWorkflowStage.RemediationDecision)
                return Result<bool>.Failure($"A decision can only be cancelled while the Remediation Decision stage is active. Current stage: {workflow.CurrentStage}.");

            return await EnsureSoftwareOwnerDecisionAccessAsync(tenantId, workflow, ct);
        }

        return await EnsureApprovalAccessForDecisionAsync(tenantId, decision, ct);
    }

    public async Task<Result<bool>> EnsureCanResolveApprovalTaskAsync(
        Guid tenantId,
        Guid approvalTaskId,
        CancellationToken ct
    )
    {
        var approvalTask = await dbContext.ApprovalTasks.AsNoTracking()
            .Include(task => task.RemediationDecision)
            .FirstOrDefaultAsync(task => task.TenantId == tenantId && task.Id == approvalTaskId, ct);
        if (approvalTask is null)
            return Result<bool>.Failure("Approval task not found.");

        return await EnsureApprovalAccessForDecisionAsync(tenantId, approvalTask.RemediationDecision, ct);
    }

    private async Task<Result<bool>> EnsureApprovalAccessForDecisionAsync(
        Guid tenantId,
        RemediationDecision decision,
        CancellationToken ct
    )
    {
        var workflow = await GetWorkflowForDecisionAsync(decision, ct);
        if (workflow is null)
            return Result<bool>.Failure("No remediation workflow is linked to this decision.");

        if (workflow.CurrentStage != RemediationWorkflowStage.Approval)
            return Result<bool>.Failure($"This decision cannot be approved right now because the Approval stage is not active. Current stage: {workflow.CurrentStage}.");

        var roles = GetCurrentRoles(tenantId);
        if (roles.Contains(RoleName.GlobalAdmin))
            return Result<bool>.Success(true);

        return workflow.ApprovalMode switch
        {
            RemediationWorkflowApprovalMode.SecurityApproval when roles.Contains(RoleName.SecurityManager) =>
                Result<bool>.Success(true),
            RemediationWorkflowApprovalMode.TechnicalApproval when roles.Contains(RoleName.TechnicalManager) =>
                Result<bool>.Success(true),
            _ => Result<bool>.Failure("You do not have permission to act on the current approval stage."),
        };
    }

    private async Task<Result<bool>> EnsureSoftwareOwnerDecisionAccessAsync(
        Guid tenantId,
        RemediationWorkflow workflow,
        CancellationToken ct
    )
    {
        var roles = GetCurrentRoles(tenantId);
        if (roles.Contains(RoleName.GlobalAdmin))
            return Result<bool>.Success(true);

        var userId = tenantContext.CurrentUserId;
        var isMember = await dbContext.TeamMembers.AsNoTracking()
            .AnyAsync(member => member.TeamId == workflow.SoftwareOwnerTeamId && member.UserId == userId, ct);

        return isMember
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("Only members of the software owner team can complete the Remediation Decision stage.");
    }

    private Result<bool> EnsureSecurityAnalysisRole()
    {
        var tenantId = tenantContext.CurrentTenantId ?? Guid.Empty;
        var roles = GetCurrentRoles(tenantId);
        return roles.Contains(RoleName.GlobalAdmin)
            || roles.Contains(RoleName.SecurityManager)
            || roles.Contains(RoleName.SecurityAnalyst)
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("Only Global Admin, Security Manager, or Security Analyst can complete Security Analysis.");
    }

    private Result<bool> EnsureSecurityManagerApprovalAccess(Guid tenantId)
    {
        var roles = GetCurrentRoles(tenantId);
        return roles.Contains(RoleName.GlobalAdmin) || roles.Contains(RoleName.SecurityManager)
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("Only Global Admin or Security Manager can complete Verification for accepted risk or alternate mitigation.");
    }

    private IReadOnlySet<RoleName> GetCurrentRoles(Guid tenantId) =>
        tenantContext.GetRolesForTenant(tenantId)
            .Select(role => Enum.TryParse<RoleName>(role, true, out var parsed) ? parsed : (RoleName?)null)
            .OfType<RoleName>()
            .ToHashSet();

    private Task<RemediationWorkflow?> GetActiveWorkflowAsync(
        Guid tenantId,
        Guid remediationCaseId,
        CancellationToken ct
    ) =>
        dbContext.RemediationWorkflows.AsNoTracking()
            .FirstOrDefaultAsync(workflow =>
                workflow.TenantId == tenantId
                && workflow.RemediationCaseId == remediationCaseId
                && workflow.Status == RemediationWorkflowStatus.Active,
                ct);

    private async Task<RemediationWorkflow?> GetWorkflowForDecisionAsync(
        RemediationDecision decision,
        CancellationToken ct
    )
    {
        if (!decision.RemediationWorkflowId.HasValue)
            return null;

        return await dbContext.RemediationWorkflows.AsNoTracking()
            .FirstOrDefaultAsync(workflow =>
                workflow.TenantId == decision.TenantId
                && workflow.Id == decision.RemediationWorkflowId.Value,
                ct);
    }
}
