using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RemediationWorkflowService(PatchHoundDbContext dbContext)
{
    public async Task<RemediationWorkflow> GetOrCreateActiveWorkflowAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        var existing = await dbContext.RemediationWorkflows
            .FirstOrDefaultAsync(workflow =>
                workflow.TenantId == tenantId
                && workflow.TenantSoftwareId == tenantSoftwareId
                && workflow.Status == RemediationWorkflowStatus.Active,
                ct);
        if (existing is not null)
            return existing;

        var softwareOwnerTeamId = await ResolveSoftwareOwnerTeamIdAsync(tenantId, tenantSoftwareId, ct);
        var previousWorkflow = await dbContext.RemediationWorkflows.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.TenantSoftwareId == tenantSoftwareId
                && item.Status != RemediationWorkflowStatus.Active
                && item.ProposedOutcome != null)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var isRecurrence = previousWorkflow is not null;
        var initialStage = isRecurrence
            ? RemediationWorkflowStage.Verification
            : RemediationWorkflowStage.SecurityAnalysis;
        var workflow = RemediationWorkflow.Create(
            tenantId,
            tenantSoftwareId,
            softwareOwnerTeamId,
            initialStage,
            previousWorkflow?.Id
        );

        if (previousWorkflow is not null)
        {
            workflow.SetDecisionContext(
                previousWorkflow.ProposedOutcome,
                previousWorkflow.Priority,
                previousWorkflow.ApprovalMode
            );
        }

        await dbContext.RemediationWorkflows.AddAsync(workflow, ct);
        await dbContext.RemediationWorkflowStageRecords.AddAsync(
            RemediationWorkflowStageRecord.Create(
                tenantId,
                workflow.Id,
                initialStage,
                RemediationWorkflowStageStatus.InProgress,
                assignedRole: DetermineVerificationAssignedRole(previousWorkflow?.ProposedOutcome),
                assignedTeamId: DetermineVerificationAssignedTeamId(previousWorkflow?.ProposedOutcome, softwareOwnerTeamId),
                summary: isRecurrence
                    ? "Recurring exposure detected. Verify whether the previous remediation decision should be kept or replaced."
                    : "Security analysis is waiting for a recommendation."
            ),
            ct
        );

        return workflow;
    }

    public async Task AttachRecommendationAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        AnalystRecommendation recommendation,
        CancellationToken ct
    )
    {
        var workflow = await GetOrCreateActiveWorkflowAsync(tenantId, tenantSoftwareId, ct);
        recommendation.AttachToWorkflow(workflow.Id);

        var priority = ParsePriority(recommendation.PriorityOverride);
        workflow.SetDecisionContext(recommendation.RecommendedOutcome, priority, RemediationWorkflowApprovalMode.None);

        if (workflow.CurrentStage == RemediationWorkflowStage.Verification)
        {
            await CompleteOpenStageAsync(
                workflow.Id,
                RemediationWorkflowStage.Verification,
                recommendation.AnalystId,
                "Previous remediation posture was not reused. Security analysis opened for a fresh recommendation.",
                ct
            );

            workflow.MoveToStage(RemediationWorkflowStage.SecurityAnalysis);
            await dbContext.RemediationWorkflowStageRecords.AddAsync(
                RemediationWorkflowStageRecord.Create(
                    tenantId,
                    workflow.Id,
                    RemediationWorkflowStage.SecurityAnalysis,
                    RemediationWorkflowStageStatus.InProgress,
                    summary: "Security analysis is waiting for a recommendation."
                ),
                ct
            );
        }

        if (workflow.CurrentStage != RemediationWorkflowStage.SecurityAnalysis)
            return;

        await CompleteOpenStageAsync(
            workflow.Id,
            RemediationWorkflowStage.SecurityAnalysis,
            recommendation.AnalystId,
            "Security analysis completed with a recommendation.",
            ct
        );

        workflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
        await dbContext.RemediationWorkflowStageRecords.AddAsync(
            RemediationWorkflowStageRecord.Create(
                tenantId,
                workflow.Id,
                RemediationWorkflowStage.RemediationDecision,
                RemediationWorkflowStageStatus.InProgress,
                assignedTeamId: workflow.SoftwareOwnerTeamId,
                summary: "Waiting for the software owner team to choose a remediation posture."
            ),
            ct
        );
    }

    public async Task AttachDecisionAsync(
        RemediationDecision decision,
        CancellationToken ct
    )
    {
        var workflow = await GetOrCreateActiveWorkflowAsync(decision.TenantId, decision.TenantSoftwareId, ct);
        decision.AttachToWorkflow(workflow.Id);

        var priority = workflow.Priority ?? RemediationWorkflowPriority.Normal;
        var approvalMode = DetermineApprovalMode(decision.Outcome, priority);
        workflow.SetDecisionContext(decision.Outcome, priority, approvalMode);

        if (workflow.CurrentStage == RemediationWorkflowStage.SecurityAnalysis)
        {
            await CompleteOpenStageAsync(
                workflow.Id,
                RemediationWorkflowStage.SecurityAnalysis,
                decision.DecidedBy,
                "Security analysis was bypassed and the remediation decision was recorded directly.",
                ct
            );

            await dbContext.RemediationWorkflowStageRecords.AddAsync(
                RemediationWorkflowStageRecord.Create(
                    decision.TenantId,
                    workflow.Id,
                    RemediationWorkflowStage.RemediationDecision,
                    RemediationWorkflowStageStatus.InProgress,
                    assignedTeamId: workflow.SoftwareOwnerTeamId,
                    summary: "Decision stage opened directly from the software owner team."
                ),
                ct
            );
            workflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
        }

        if (workflow.CurrentStage == RemediationWorkflowStage.RemediationDecision)
        {
            await CompleteOpenStageAsync(
                workflow.Id,
                RemediationWorkflowStage.RemediationDecision,
                decision.DecidedBy,
                "Remediation decision submitted.",
                ct
            );
        }

        if (decision.ApprovalStatus == DecisionApprovalStatus.PendingApproval && RequiresApproval(approvalMode))
        {
            workflow.MoveToStage(RemediationWorkflowStage.Approval);
            await dbContext.RemediationWorkflowStageRecords.AddAsync(
                RemediationWorkflowStageRecord.Create(
                    decision.TenantId,
                    workflow.Id,
                    RemediationWorkflowStage.Approval,
                    RemediationWorkflowStageStatus.InProgress,
                    assignedRole: approvalMode == RemediationWorkflowApprovalMode.SecurityApproval
                        ? RoleName.SecurityManager
                        : RoleName.TechnicalManager,
                    summary: "Waiting for approval before remediation can continue."
                ),
                ct
            );
            return;
        }

        if (decision.Outcome == RemediationOutcome.ApprovedForPatching)
        {
            workflow.MoveToStage(RemediationWorkflowStage.Execution);
            await dbContext.RemediationWorkflowStageRecords.AddAsync(
                RemediationWorkflowStageRecord.Create(
                    decision.TenantId,
                    workflow.Id,
                    RemediationWorkflowStage.Execution,
                    RemediationWorkflowStageStatus.InProgress,
                    summary: "Approved patching is waiting for execution by device owner teams."
                ),
                ct
            );
            return;
        }

        await dbContext.RemediationWorkflowStageRecords.AddAsync(
            RemediationWorkflowStageRecord.Create(
                decision.TenantId,
                workflow.Id,
                RemediationWorkflowStage.Execution,
                RemediationWorkflowStageStatus.Skipped,
                summary: "Execution is not applicable for the approved remediation posture."
            ),
            ct
        );

        workflow.MoveToStage(RemediationWorkflowStage.Closure);
        await dbContext.RemediationWorkflowStageRecords.AddAsync(
            RemediationWorkflowStageRecord.Create(
                decision.TenantId,
                workflow.Id,
                RemediationWorkflowStage.Closure,
                RemediationWorkflowStageStatus.InProgress,
                summary: "The approved remediation posture is now active for this software. Execution is not applicable."
            ),
            ct
        );
    }

    public Task AttachApprovalTaskAsync(ApprovalTask task, RemediationDecision decision, CancellationToken ct)
    {
        if (decision.RemediationWorkflowId.HasValue)
            task.AttachToWorkflow(decision.RemediationWorkflowId.Value);

        return Task.CompletedTask;
    }

    public async Task HandleApprovalOutcomeAsync(
        ApprovalTask task,
        RemediationDecision decision,
        bool approved,
        Guid? actedBy,
        CancellationToken ct
    )
    {
        if (!decision.RemediationWorkflowId.HasValue)
            return;

        var workflow = await dbContext.RemediationWorkflows
            .FirstOrDefaultAsync(item => item.Id == decision.RemediationWorkflowId.Value, ct);
        if (workflow is null)
            return;

        await CompleteOpenStageAsync(
            workflow.Id,
            RemediationWorkflowStage.Approval,
            actedBy,
            approved ? "Approval granted." : "Approval denied.",
            ct
        );

        if (!approved)
        {
            workflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
            await dbContext.RemediationWorkflowStageRecords.AddAsync(
                RemediationWorkflowStageRecord.Create(
                    decision.TenantId,
                    workflow.Id,
                    RemediationWorkflowStage.RemediationDecision,
                    RemediationWorkflowStageStatus.InProgress,
                    assignedTeamId: workflow.SoftwareOwnerTeamId,
                    summary: "Decision must be revised after denial."
                ),
                ct
            );
            return;
        }

        if (decision.Outcome == RemediationOutcome.ApprovedForPatching)
        {
            workflow.MoveToStage(RemediationWorkflowStage.Execution);
            await dbContext.RemediationWorkflowStageRecords.AddAsync(
                RemediationWorkflowStageRecord.Create(
                    decision.TenantId,
                    workflow.Id,
                    RemediationWorkflowStage.Execution,
                    RemediationWorkflowStageStatus.InProgress,
                    summary: "Approved patching is now in execution."
                ),
                ct
            );
            return;
        }

        await dbContext.RemediationWorkflowStageRecords.AddAsync(
            RemediationWorkflowStageRecord.Create(
                decision.TenantId,
                workflow.Id,
                RemediationWorkflowStage.Execution,
                RemediationWorkflowStageStatus.Skipped,
                summary: "Execution is not applicable for the approved remediation posture."
            ),
            ct
        );

        workflow.MoveToStage(RemediationWorkflowStage.Closure);
        await dbContext.RemediationWorkflowStageRecords.AddAsync(
            RemediationWorkflowStageRecord.Create(
                decision.TenantId,
                workflow.Id,
                RemediationWorkflowStage.Closure,
                RemediationWorkflowStageStatus.InProgress,
                summary: "The approved remediation posture is now active for this software. Execution is not applicable."
            ),
            ct
        );
    }

    public Task AttachPatchingTaskAsync(PatchingTask task, RemediationDecision decision, CancellationToken ct)
    {
        if (decision.RemediationWorkflowId.HasValue)
            task.AttachToWorkflow(decision.RemediationWorkflowId.Value);

        return Task.CompletedTask;
    }

    public async Task MarkPatchedWorkflowClosedAsync(
        RemediationDecision decision,
        CancellationToken ct
    )
    {
        if (!decision.RemediationWorkflowId.HasValue)
            return;

        var workflow = await dbContext.RemediationWorkflows
            .FirstOrDefaultAsync(item => item.Id == decision.RemediationWorkflowId.Value, ct);
        if (workflow is null || workflow.Status != RemediationWorkflowStatus.Active)
            return;

        await CompleteOpenStageAsync(
            workflow.Id,
            RemediationWorkflowStage.Execution,
            null,
            "Execution completed and exposure resolved.",
            ct,
            systemCompleted: true
        );

        workflow.MoveToStage(RemediationWorkflowStage.Closure);
        await dbContext.RemediationWorkflowStageRecords.AddAsync(
            RemediationWorkflowStageRecord.Create(
                decision.TenantId,
                workflow.Id,
                RemediationWorkflowStage.Closure,
                RemediationWorkflowStageStatus.AutoCompleted,
                summary: "Remediation closed automatically after all linked exposure resolved."
            ),
            ct
        );
        workflow.Complete();
    }

    public async Task MarkWorkflowClosedForResolvedExposureAsync(
        RemediationWorkflow workflow,
        CancellationToken ct
    )
    {
        if (workflow.Status != RemediationWorkflowStatus.Active)
            return;

        await CompleteOpenStageAsync(
            workflow.Id,
            workflow.CurrentStage,
            null,
            "Exposure resolved automatically during ingestion reconciliation.",
            ct,
            systemCompleted: true
        );

        if (workflow.CurrentStage != RemediationWorkflowStage.Closure)
        {
            workflow.MoveToStage(RemediationWorkflowStage.Closure);
            await dbContext.RemediationWorkflowStageRecords.AddAsync(
                RemediationWorkflowStageRecord.Create(
                    workflow.TenantId,
                    workflow.Id,
                    RemediationWorkflowStage.Closure,
                    RemediationWorkflowStageStatus.AutoCompleted,
                    summary: "Remediation closed automatically after no unresolved exposure remained."
                ),
                ct
            );
        }

        workflow.Complete();
    }

    public async Task<Result<bool>> VerifyRecurringWorkflowAsync(
        Guid tenantId,
        Guid workflowId,
        Guid actedBy,
        string action,
        CancellationToken ct
    )
    {
        var workflow = await dbContext.RemediationWorkflows
            .FirstOrDefaultAsync(item =>
                item.TenantId == tenantId
                && item.Id == workflowId
                && item.Status == RemediationWorkflowStatus.Active,
                ct);
        if (workflow is null)
            return Result<bool>.Failure("Remediation workflow not found.");

        if (workflow.CurrentStage != RemediationWorkflowStage.Verification)
            return Result<bool>.Failure("This remediation is not currently waiting for verification.");

        if (string.Equals(action, "keepCurrentDecision", StringComparison.OrdinalIgnoreCase))
        {
            if (!workflow.ProposedOutcome.HasValue)
                return Result<bool>.Failure("No previous remediation decision is available to carry forward.");

            var latestHistoricalDecision = await dbContext.RemediationDecisions.AsNoTracking()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.TenantSoftwareId == workflow.TenantSoftwareId
                    && item.RemediationWorkflowId == workflow.RecurrenceSourceWorkflowId
                    && item.ApprovalStatus == DecisionApprovalStatus.Approved)
                .OrderByDescending(item => item.DecidedAt)
                .FirstOrDefaultAsync(ct);
            if (latestHistoricalDecision is null)
                return Result<bool>.Failure("No approved previous decision could be found for this recurrence.");

            await CompleteOpenStageAsync(
                workflow.Id,
                RemediationWorkflowStage.Verification,
                actedBy,
                "Previous remediation decision was confirmed for this recurrence.",
                ct
            );

            return Result<bool>.Success(true);
        }

        if (string.Equals(action, "chooseNewDecision", StringComparison.OrdinalIgnoreCase))
        {
            await CompleteOpenStageAsync(
                workflow.Id,
                RemediationWorkflowStage.Verification,
                actedBy,
                "Previous remediation decision was not reused. A new remediation decision is required.",
                ct
            );

            workflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
            await dbContext.RemediationWorkflowStageRecords.AddAsync(
                RemediationWorkflowStageRecord.Create(
                    tenantId,
                    workflow.Id,
                    RemediationWorkflowStage.RemediationDecision,
                    RemediationWorkflowStageStatus.InProgress,
                    assignedTeamId: workflow.SoftwareOwnerTeamId,
                    summary: "The software owner team must record a new remediation decision for this recurring exposure."
                ),
                ct
            );

            await dbContext.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }

        return Result<bool>.Failure("Verification action must be 'keepCurrentDecision' or 'chooseNewDecision'.");
    }

    private async Task CompleteOpenStageAsync(
        Guid workflowId,
        RemediationWorkflowStage stage,
        Guid? completedByUserId,
        string summary,
        CancellationToken ct,
        bool systemCompleted = false
    )
    {
        var openRecord = await dbContext.RemediationWorkflowStageRecords
            .Where(record =>
                record.RemediationWorkflowId == workflowId
                && record.Stage == stage
                && (record.Status == RemediationWorkflowStageStatus.InProgress
                    || record.Status == RemediationWorkflowStageStatus.Pending))
            .OrderByDescending(record => record.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (openRecord is null)
            return;

        openRecord.Complete(completedByUserId, systemCompleted, summary);
    }

    private async Task<Guid> ResolveSoftwareOwnerTeamIdAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        var candidateTeamIds = await dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantId == tenantId && item.TenantSoftwareId == tenantSoftwareId && item.IsActive)
            .Join(
                dbContext.Assets,
                item => item.SoftwareAssetId,
                asset => asset.Id,
                (item, asset) => asset.OwnerTeamId ?? asset.FallbackTeamId
            )
            .Where(teamId => teamId != null)
            .Select(teamId => teamId!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (candidateTeamIds.Count == 1)
            return candidateTeamIds[0];

        var defaultTeam = await DefaultTeamHelper.EnsureDefaultTeamAsync(dbContext, tenantId, ct);
        return defaultTeam.Id;
    }

    private static bool RequiresApproval(RemediationWorkflowApprovalMode mode) =>
        mode is RemediationWorkflowApprovalMode.SecurityApproval or RemediationWorkflowApprovalMode.TechnicalApproval;

    private static RemediationWorkflowApprovalMode DetermineApprovalMode(
        RemediationOutcome outcome,
        RemediationWorkflowPriority priority
    ) =>
        outcome switch
        {
            RemediationOutcome.RiskAcceptance or RemediationOutcome.AlternateMitigation =>
                RemediationWorkflowApprovalMode.SecurityApproval,
            RemediationOutcome.ApprovedForPatching =>
                RemediationWorkflowApprovalMode.TechnicalApproval,
            RemediationOutcome.PatchingDeferred when priority == RemediationWorkflowPriority.Emergency =>
                RemediationWorkflowApprovalMode.TechnicalApproval,
            RemediationOutcome.PatchingDeferred =>
                RemediationWorkflowApprovalMode.TechnicalAutoApproved,
            _ => RemediationWorkflowApprovalMode.None,
        };

    public static RemediationWorkflowApprovalMode ResolveApprovalMode(
        RemediationOutcome outcome,
        RemediationWorkflowPriority priority
    ) => DetermineApprovalMode(outcome, priority);

    private static RemediationWorkflowPriority ParsePriority(string? priorityOverride) =>
        string.Equals(priorityOverride?.Trim(), "Emergency", StringComparison.OrdinalIgnoreCase)
        || string.Equals(priorityOverride?.Trim(), "Emergency patch required", StringComparison.OrdinalIgnoreCase)
            ? RemediationWorkflowPriority.Emergency
            : RemediationWorkflowPriority.Normal;

    private static RoleName? DetermineVerificationAssignedRole(RemediationOutcome? outcome) =>
        outcome switch
        {
            RemediationOutcome.RiskAcceptance or RemediationOutcome.AlternateMitigation => RoleName.SecurityManager,
            _ => null,
        };

    private static Guid? DetermineVerificationAssignedTeamId(RemediationOutcome? outcome, Guid softwareOwnerTeamId) =>
        outcome switch
        {
            RemediationOutcome.ApprovedForPatching or RemediationOutcome.PatchingDeferred => softwareOwnerTeamId,
            _ => null,
        };
}
