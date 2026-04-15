using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class PatchingTaskService(
    PatchHoundDbContext dbContext,
    SlaService slaService,
    RemediationWorkflowService remediationWorkflowService,
    INotificationService notificationService
)
{
    public async Task<int> EnsurePatchingTasksAsync(Guid decisionId, CancellationToken ct)
    {
        var decision = await dbContext.RemediationDecisions
            .FirstOrDefaultAsync(d => d.Id == decisionId, ct);

        if (decision is null)
            return 0;

        return await EnsurePatchingTasksAsync(decision, ct);
    }

    public async Task<int> EnsurePatchingTasksAsync(RemediationDecision decision, CancellationToken ct)
    {
        // TODO Phase 5: group patching tasks by device owner team once DeviceVulnerabilityExposure
        // is available for this case. For now, create a single patching task for the default team.
        var defaultTeamId = (await DefaultTeamHelper.EnsureDefaultTeamAsync(dbContext, decision.TenantId, ct)).Id;

        var tenantSla = await dbContext.TenantSlaConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == decision.TenantId, ct);

        // Determine highest severity from open DeviceVulnerabilityExposures for this remediation case.
        var softwareProductId = await dbContext.RemediationCases
            .IgnoreQueryFilters()
            .Where(c => c.Id == decision.RemediationCaseId && c.TenantId == decision.TenantId)
            .Select(c => (Guid?)c.SoftwareProductId)
            .FirstOrDefaultAsync(ct);

        var highestSeverity = softwareProductId.HasValue
            ? await dbContext.DeviceVulnerabilityExposures
                .AsNoTracking()
                .Where(e => e.TenantId == decision.TenantId
                    && e.SoftwareProductId == softwareProductId
                    && e.Status == ExposureStatus.Open)
                .Select(e => (Severity?)e.Vulnerability.VendorSeverity)
                .MaxAsync(ct) ?? Severity.Medium
            : Severity.Medium;

        var dueDate = slaService.CalculateDueDate(
            highestSeverity,
            DateTimeOffset.UtcNow,
            tenantSla
        );

        var existingOpenForTeam = await dbContext.PatchingTasks
            .IgnoreQueryFilters()
            .AnyAsync(task =>
                task.TenantId == decision.TenantId
                && task.RemediationCaseId == decision.RemediationCaseId
                && task.OwnerTeamId == defaultTeamId
                && task.Status != PatchingTaskStatus.Completed,
                ct);

        if (existingOpenForTeam)
            return 0;

        var task = PatchingTask.Create(
            decision.TenantId,
            decision.RemediationCaseId,
            decision.Id,
            defaultTeamId,
            dueDate
        );

        await remediationWorkflowService.AttachPatchingTaskAsync(task, decision, ct);
        await dbContext.PatchingTasks.AddAsync(task, ct);
        await dbContext.SaveChangesAsync(ct);

        // Resolve software name via RemediationCase → SoftwareProduct
        var softwareName = await dbContext.RemediationCases
            .IgnoreQueryFilters()
            .Where(c => c.Id == decision.RemediationCaseId && c.TenantId == decision.TenantId)
            .Join(
                dbContext.SoftwareProducts,
                c => c.SoftwareProductId,
                sp => sp.Id,
                (c, sp) => sp.Name
            )
            .FirstOrDefaultAsync(ct)
            ?? "Unknown software";

        var severityLabel = highestSeverity.ToString();
        var maintenanceWindowText = decision.MaintenanceWindowDate is DateTimeOffset maintenanceWindowDate
            ? $" Maintenance window: {maintenanceWindowDate:yyyy-MM-dd}."
            : string.Empty;

        await notificationService.SendToTeamAsync(
            task.OwnerTeamId,
            decision.TenantId,
            NotificationType.TaskAssigned,
            $"Remediation task assigned: {softwareName}",
            $"Patch {softwareName}. Highest severity: {severityLabel}.{maintenanceWindowText}",
            "PatchingTask",
            task.Id,
            ct
        );

        return 1;
    }
}
