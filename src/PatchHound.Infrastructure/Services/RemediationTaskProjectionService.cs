using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RemediationTaskProjectionService(PatchHoundDbContext dbContext, SlaService slaService)
{
    private static readonly Guid SystemUserId = Guid.Empty;
    private static readonly string AssetNotificationEntityType = "Asset";

    public async Task EnsureOpenTaskAsync(
        Guid tenantId,
        TenantVulnerability tenantVulnerability,
        VulnerabilityDefinition definition,
        Asset asset,
        CancellationToken ct
    )
    {
        var existingTask = await dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                task =>
                    task.TenantId == tenantId
                    && task.TenantVulnerabilityId == tenantVulnerability.Id
                    && task.AssetId == asset.Id
                    && task.Status != RemediationTaskStatus.Completed,
                ct
            );

        if (existingTask is not null)
        {
            return;
        }

        var assigneeId = ResolveAssignee(asset);
        if (!assigneeId.HasValue)
        {
            return;
        }

        var tenantSla = await dbContext
            .TenantSlaConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(config => config.TenantId == tenantId, ct);

        var task = RemediationTask.Create(
            tenantVulnerability.Id,
            asset.Id,
            tenantId,
            assigneeId.Value,
            SystemUserId,
            slaService.CalculateDueDate(
                definition.VendorSeverity,
                DateTimeOffset.UtcNow,
                tenantSla
            )
        );

        await dbContext.RemediationTasks.AddAsync(task, ct);
        await QueueOwnerNotificationsAsync(
            tenantId,
            [(definition, asset)],
            DateTimeOffset.UtcNow.AddHours(-12),
            ct
        );
    }

    public async Task EnsureOpenTasksAsync(
        Guid tenantId,
        IReadOnlyList<(
            TenantVulnerability TenantVulnerability,
            VulnerabilityDefinition Definition,
            Asset Asset
        )> openedProjectionPairs,
        HashSet<string> openTaskPairKeys,
        CancellationToken ct
    )
    {
        if (openedProjectionPairs.Count == 0)
        {
            return;
        }

        var tenantSla = await dbContext
            .TenantSlaConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(config => config.TenantId == tenantId, ct);
        var tasksToCreate = new List<RemediationTask>();
        var notificationInputs = new List<(VulnerabilityDefinition Definition, Asset Asset)>();
        var dueDateCalculationTime = DateTimeOffset.UtcNow;

        foreach (var (tenantVulnerability, definition, asset) in openedProjectionPairs)
        {
            var pairKey = BuildPairKey(tenantVulnerability.Id, asset.Id);
            if (openTaskPairKeys.Contains(pairKey))
            {
                continue;
            }

            var assigneeId = ResolveAssignee(asset);
            if (!assigneeId.HasValue)
            {
                continue;
            }

            tasksToCreate.Add(
                RemediationTask.Create(
                    tenantVulnerability.Id,
                    asset.Id,
                    tenantId,
                    assigneeId.Value,
                    SystemUserId,
                    slaService.CalculateDueDate(
                        definition.VendorSeverity,
                        dueDateCalculationTime,
                        tenantSla
                    )
                )
            );
            openTaskPairKeys.Add(pairKey);
            notificationInputs.Add((definition, asset));
        }

        if (tasksToCreate.Count > 0)
        {
            await dbContext.RemediationTasks.AddRangeAsync(tasksToCreate, ct);
        }

        if (notificationInputs.Count > 0)
        {
            await QueueOwnerNotificationsAsync(
                tenantId,
                notificationInputs,
                dueDateCalculationTime.AddHours(-12),
                ct
            );
        }
    }

    public async Task CloseOpenTasksAsync(
        IReadOnlyList<(Guid TenantVulnerabilityId, Guid AssetId)> pairs,
        CancellationToken ct
    )
    {
        if (pairs.Count == 0)
        {
            return;
        }

        var pairKeys = pairs
            .Select(pair => BuildPairKey(pair.TenantVulnerabilityId, pair.AssetId))
            .ToHashSet(StringComparer.Ordinal);
        var tenantVulnerabilityIds = pairs
            .Select(pair => pair.TenantVulnerabilityId)
            .Distinct()
            .ToList();
        var assetIds = pairs.Select(pair => pair.AssetId).Distinct().ToList();

        var tasks = await dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .Where(task =>
                tenantVulnerabilityIds.Contains(task.TenantVulnerabilityId)
                && assetIds.Contains(task.AssetId)
                && task.Status != RemediationTaskStatus.Completed
            )
            .ToListAsync(ct);

        foreach (var task in tasks)
        {
            if (!pairKeys.Contains(BuildPairKey(task.TenantVulnerabilityId, task.AssetId)))
            {
                continue;
            }

            task.UpdateStatus(
                RemediationTaskStatus.Completed,
                "Auto-closed: vulnerability resolved in source"
            );
        }
    }

    private static Guid? ResolveAssignee(Asset asset)
    {
        if (asset.OwnerUserId.HasValue)
        {
            return asset.OwnerUserId.Value;
        }

        if (asset.OwnerTeamId.HasValue)
        {
            return asset.OwnerTeamId.Value;
        }

        if (asset.FallbackTeamId.HasValue)
        {
            return asset.FallbackTeamId.Value;
        }

        return null;
    }

    private static string BuildPairKey(Guid tenantVulnerabilityId, Guid assetId)
    {
        return $"{tenantVulnerabilityId:N}:{assetId:N}";
    }

    private async Task QueueOwnerNotificationsAsync(
        Guid tenantId,
        IReadOnlyList<(VulnerabilityDefinition Definition, Asset Asset)> items,
        DateTimeOffset threshold,
        CancellationToken ct
    )
    {
        if (items.Count == 0)
        {
            return;
        }

        var ownerTeamIds = items
            .Where(item => !item.Asset.OwnerUserId.HasValue)
            .Select(item => item.Asset.OwnerTeamId ?? item.Asset.FallbackTeamId)
            .Where(teamId => teamId.HasValue)
            .Select(teamId => teamId!.Value)
            .Distinct()
            .ToList();

        var teamMembers = ownerTeamIds.Count == 0
            ? new List<TeamMember>()
            : await dbContext.TeamMembers.IgnoreQueryFilters()
                .Where(item => ownerTeamIds.Contains(item.TeamId))
                .ToListAsync(ct);

        var teamMemberIdsByTeam = teamMembers
            .GroupBy(item => item.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.UserId).Distinct().ToArray()
            );

        var candidates = new List<NotificationCandidate>();

        foreach (var (definition, asset) in items)
        {
            var title = $"Software on {asset.Name} needs review";
            var body =
                $"{OwnerFacingIssueSummaryFormatter.BuildIssueSummary(null, definition.Title, definition.Description, definition.VendorSeverity)} Open the asset view to see business impact, affected software, and the next step. Technical reference: {definition.ExternalId}.";

            if (asset.OwnerUserId.HasValue)
            {
                candidates.Add(
                    new NotificationCandidate(asset.OwnerUserId.Value, asset.Id, title, body)
                );
                continue;
            }

            var ownerTeamId = asset.OwnerTeamId ?? asset.FallbackTeamId;
            if (!ownerTeamId.HasValue)
            {
                continue;
            }

            if (!teamMemberIdsByTeam.TryGetValue(ownerTeamId.Value, out var memberIds))
            {
                continue;
            }

            foreach (var memberId in memberIds)
            {
                candidates.Add(new NotificationCandidate(memberId, asset.Id, title, body));
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var uniqueCandidates = candidates
            .GroupBy(candidate => candidate.DedupeKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var candidateUserIds = uniqueCandidates
            .Select(candidate => candidate.UserId)
            .Distinct()
            .ToList();
        var candidateAssetIds = uniqueCandidates
            .Select(candidate => candidate.AssetId)
            .Distinct()
            .ToList();

        var existingNotificationKeys = await dbContext.Notifications.IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.Type == NotificationType.TaskAssigned
                && item.RelatedEntityType == AssetNotificationEntityType
                && item.SentAt >= threshold
                && item.RelatedEntityId.HasValue
                && candidateUserIds.Contains(item.UserId)
                && candidateAssetIds.Contains(item.RelatedEntityId.Value)
            )
            .Select(item => new { item.UserId, AssetId = item.RelatedEntityId!.Value, item.Title })
            .ToListAsync(ct);

        var knownKeys = existingNotificationKeys
            .Select(item => BuildNotificationDedupeKey(item.UserId, item.AssetId, item.Title))
            .Concat(
                dbContext.Notifications.Local
                    .Where(item =>
                        item.TenantId == tenantId
                        && item.Type == NotificationType.TaskAssigned
                        && item.RelatedEntityType == AssetNotificationEntityType
                        && item.SentAt >= threshold
                        && item.RelatedEntityId.HasValue
                    )
                    .Select(item =>
                        BuildNotificationDedupeKey(
                            item.UserId,
                            item.RelatedEntityId!.Value,
                            item.Title
                        )
                    )
            )
            .ToHashSet(StringComparer.Ordinal);

        var notificationsToAdd = new List<Notification>();

        foreach (var candidate in uniqueCandidates)
        {
            if (!knownKeys.Add(candidate.DedupeKey))
            {
                continue;
            }

            notificationsToAdd.Add(
                Notification.Create(
                    candidate.UserId,
                    tenantId,
                    NotificationType.TaskAssigned,
                    candidate.Title,
                    candidate.Body,
                    AssetNotificationEntityType,
                    candidate.AssetId
                )
            );
        }

        if (notificationsToAdd.Count > 0)
        {
            await dbContext.Notifications.AddRangeAsync(notificationsToAdd, ct);
        }
    }

    private static string BuildNotificationDedupeKey(Guid userId, Guid assetId, string title)
    {
        return $"{userId:N}:{assetId:N}:{title}";
    }

    private sealed record NotificationCandidate(Guid UserId, Guid AssetId, string Title, string Body)
    {
        public string DedupeKey => BuildNotificationDedupeKey(UserId, AssetId, Title);
    }
}
