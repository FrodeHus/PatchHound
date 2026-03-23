using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RemediationDecisionService(PatchHoundDbContext dbContext, SlaService slaService)
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
        var decision = RemediationDecision.Create(
            tenantId,
            softwareAssetId,
            outcome,
            justification,
            decidedBy,
            expiryDate,
            reEvaluationDate
        );

        await dbContext.RemediationDecisions.AddAsync(decision, ct);

        if (decision.Outcome == RemediationOutcome.ApprovedForPatching
            && decision.ApprovalStatus == DecisionApprovalStatus.Approved)
        {
            await EnsurePatchingTasksAsync(decision, ct);
        }

        await dbContext.SaveChangesAsync(ct);

        return Result<RemediationDecision>.Success(decision);
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
        // Find devices that have the software asset installed, grouped by owning team
        var deviceInstallations = await dbContext.DeviceSoftwareInstallations
            .IgnoreQueryFilters()
            .Where(dsi => dsi.SoftwareAssetId == decision.SoftwareAssetId && dsi.TenantId == decision.TenantId)
            .Select(dsi => new { dsi.DeviceAssetId })
            .ToListAsync(ct);

        if (deviceInstallations.Count == 0)
            return 0;

        var deviceAssetIds = deviceInstallations.Select(d => d.DeviceAssetId).Distinct().ToList();

        var deviceTeams = await dbContext.Assets
            .IgnoreQueryFilters()
            .Where(a => deviceAssetIds.Contains(a.Id) && a.TenantId == decision.TenantId)
            .Select(a => new { a.Id, a.OwnerTeamId })
            .ToListAsync(ct);

        var teamGroups = deviceTeams
            .Where(d => d.OwnerTeamId.HasValue)
            .GroupBy(d => d.OwnerTeamId!.Value)
            .ToList();

        if (teamGroups.Count == 0)
            return 0;

        // Determine due date from highest severity vulnerability
        var tenantSla = await dbContext.TenantSlaConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == decision.TenantId, ct);

        var highestSeverity = await dbContext.SoftwareVulnerabilityMatches
            .IgnoreQueryFilters()
            .Where(svm => svm.SoftwareAssetId == decision.SoftwareAssetId && svm.TenantId == decision.TenantId)
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
                && task.SoftwareAssetId == decision.SoftwareAssetId
                && task.Status != PatchingTaskStatus.Completed)
            .Select(task => task.OwnerTeamId)
            .Distinct()
            .ToListAsync(ct);

        var tasks = teamGroups
            .Where(group => !existingOpenTeamIds.Contains(group.Key))
            .Select(group => PatchingTask.Create(
                decision.TenantId,
                decision.Id,
                decision.SoftwareAssetId,
                group.Key,
                dueDate
            ))
            .ToList();

        if (tasks.Count == 0)
            return 0;

        await dbContext.PatchingTasks.AddRangeAsync(tasks, ct);
        return tasks.Count;
    }
}
