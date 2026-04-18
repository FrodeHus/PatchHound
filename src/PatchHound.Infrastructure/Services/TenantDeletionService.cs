using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Infrastructure.Services;

public class TenantDeletionService(
    PatchHoundDbContext dbContext,
    ISecretStore secretStore,
    ILogger<TenantDeletionService> logger
)
{
    public async Task DeleteAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

        logger.LogInformation("Starting background deletion of tenant {TenantId}", tenantId);

        // --- collect secret refs ---
        var tenantSourceSecretRefs = await dbContext.TenantSourceConfigurations
            .IgnoreQueryFilters()
            .Where(source => source.TenantId == tenantId && source.SecretRef != string.Empty)
            .Select(source => source.SecretRef)
            .ToListAsync(ct);
        var aiProfileSecretRefs = await dbContext.TenantAiProfiles
            .IgnoreQueryFilters()
            .Where(profile => profile.TenantId == tenantId && profile.SecretRef != string.Empty)
            .Select(profile => profile.SecretRef)
            .ToListAsync(ct);
        var secretRefs = tenantSourceSecretRefs
            .Concat(aiProfileSecretRefs)
            .Where(secretRef => !string.IsNullOrWhiteSpace(secretRef))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var secretRef in secretRefs)
        {
            await secretStore.DeleteSecretPathAsync(secretRef, ct);
        }

        // --- collect user IDs to delete ---
        var affectedUserIds = await dbContext.UserTenantRoles
            .IgnoreQueryFilters()
            .Where(role => role.TenantId == tenantId)
            .Select(role => role.UserId)
            .Distinct()
            .ToListAsync(ct);
        var userIdsToDelete = affectedUserIds.Count == 0
            ? []
            : await dbContext.Users
                .IgnoreQueryFilters()
                .Where(user => affectedUserIds.Contains(user.Id) && user.AccessScope == Core.Enums.UserAccessScope.Customer)
                .Where(user => !dbContext.UserTenantRoles.IgnoreQueryFilters()
                    .Any(role => role.UserId == user.Id && role.TenantId != tenantId))
                .Where(user => !dbContext.TeamMembers.IgnoreQueryFilters()
                    .Any(member => member.UserId == user.Id && member.Team.TenantId != tenantId))
                .Select(user => user.Id)
                .ToListAsync(ct);

        // --- delete child entities ---
        await DeleteEntitiesAsync(
            dbContext.WorkflowNodeExecutions
                .IgnoreQueryFilters()
                .Where(execution =>
                    dbContext.WorkflowInstances
                        .IgnoreQueryFilters()
                        .Where(instance => instance.TenantId == tenantId)
                        .Select(instance => instance.Id)
                        .Contains(execution.WorkflowInstanceId)),
            ct);
        await DeleteEntitiesAsync(
            dbContext.ApprovalTaskVisibleRoles
                .IgnoreQueryFilters()
                .Where(item =>
                    dbContext.ApprovalTasks
                        .IgnoreQueryFilters()
                        .Where(task => task.TenantId == tenantId)
                        .Select(task => task.Id)
                        .Contains(item.ApprovalTaskId)),
            ct);
        await DeleteEntitiesAsync(
            dbContext.RemediationDecisionVulnerabilityOverrides
                .IgnoreQueryFilters()
                .Where(item =>
                    dbContext.RemediationDecisions
                        .IgnoreQueryFilters()
                        .Where(decision => decision.TenantId == tenantId)
                        .Select(decision => decision.Id)
                        .Contains(item.RemediationDecisionId)),
            ct);
        await DeleteEntitiesAsync(
            dbContext.TeamMembers
                .IgnoreQueryFilters()
                .Where(member => member.Team.TenantId == tenantId),
            ct);

        await DeleteEntitiesAsync(dbContext.RemediationWorkflowStageRecords.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.WorkflowActions.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.WorkflowInstances.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.WorkflowDefinitions.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.ApprovalTasks.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.PatchingTasks.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.AnalystRecommendations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.RemediationDecisions.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.RemediationWorkflows.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.Comments.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.RiskAcceptances.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.Notifications.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.AIReports.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.SoftwareDescriptionJobs.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceTags.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceBusinessLabels.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.BusinessLabels.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceRules.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceGroupRiskScores.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TeamRiskScores.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TenantRiskScoreSnapshots.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.OrganizationalSeverities.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceSoftwareInstallations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.SoftwareProductInstallations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.SoftwareTenantRecords.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.SecurityProfiles.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.ExposureAssessments.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.ExposureEpisodes.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceVulnerabilityExposures.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceRiskScores.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.Devices.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TeamMembershipRules.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.Teams.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.UserTenantRoles.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TenantAiProfiles.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TenantSlaConfigurations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.EnrichmentJobs.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.StagedDeviceSoftwareInstallations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.StagedDevices.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.StagedCloudApplications.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.CloudApplicationCredentialMetadata.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.CloudApplications.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.StagedVulnerabilityExposures.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.StagedVulnerabilities.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.IngestionCheckpoints.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.IngestionSnapshots.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.IngestionRuns.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TenantSourceConfigurations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);

        if (userIdsToDelete.Count > 0)
        {
            await DeleteEntitiesAsync(
                dbContext.Users.IgnoreQueryFilters().Where(user => userIdsToDelete.Contains(user.Id)),
                ct);
        }

        dbContext.Tenants.Remove(tenant);
        await dbContext.SaveChangesAsync(ct);

        await dbContext.TenantDeletionJobs
            .IgnoreQueryFilters()
            .Where(j => j.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("Completed background deletion of tenant {TenantId}", tenantId);
    }

    private static async Task DeleteEntitiesAsync<T>(IQueryable<T> query, CancellationToken ct)
        where T : class
    {
        await query.ExecuteDeleteAsync(ct);
    }
}
