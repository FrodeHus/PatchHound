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
        var defaultTeamId = (await DefaultTeamHelper.EnsureDefaultTeamAsync(dbContext, decision.TenantId, ct)).Id;

        var deviceTeams = await dbContext.Assets
            .IgnoreQueryFilters()
            .Where(a => deviceAssetIds.Contains(a.Id) && a.TenantId == decision.TenantId)
            .Select(a => new { a.Id, a.OwnerTeamId, a.FallbackTeamId })
            .ToListAsync(ct);

        var teamGroups = deviceTeams
            .GroupBy(device => device.OwnerTeamId ?? device.FallbackTeamId ?? defaultTeamId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).Distinct().Count());

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

        var tasks = teamGroups.Keys
            .Where(teamId => !existingOpenTeamIds.Contains(teamId))
            .Select(teamId => PatchingTask.Create(
                decision.TenantId,
                decision.Id,
                decision.TenantSoftwareId,
                decision.SoftwareAssetId,
                teamId,
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
        await dbContext.SaveChangesAsync(ct);

        var softwareName = await dbContext.TenantSoftware
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == decision.TenantId && item.Id == decision.TenantSoftwareId)
            .Join(
                dbContext.NormalizedSoftware.IgnoreQueryFilters(),
                tenantSoftware => tenantSoftware.NormalizedSoftwareId,
                normalizedSoftware => normalizedSoftware.Id,
                (tenantSoftware, normalizedSoftware) => normalizedSoftware.CanonicalName
            )
            .FirstOrDefaultAsync(ct)
            ?? "Unknown software";

        var severityLabel = highestSeverity != default ? highestSeverity.ToString() : Severity.Medium.ToString();

        foreach (var task in tasks)
        {
            var affectedDeviceCount = teamGroups.GetValueOrDefault(task.OwnerTeamId);
            await notificationService.SendToTeamAsync(
                task.OwnerTeamId,
                decision.TenantId,
                NotificationType.TaskAssigned,
                $"Remediation task assigned: {softwareName}",
                $"Patch {softwareName}. Highest severity: {severityLabel}. Affected devices for your team: {affectedDeviceCount}.",
                "PatchingTask",
                task.Id,
                ct
            );
        }

        return tasks.Count;
    }
}
