using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Worker;

public class IngestionWorker(IServiceScopeFactory scopeFactory, ILogger<IngestionWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("IngestionWorker started with polling interval {Interval}", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIngestionCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during ingestion cycle");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunIngestionCycleAsync(CancellationToken ct)
    {
        var cycleStartedAt = DateTimeOffset.UtcNow;
        logger.LogInformation(
            "Starting ingestion polling cycle at {CycleStartedAt}",
            cycleStartedAt
        );

        var scheduledSources = await LoadScheduledSourcesAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var runnableSources = 0;
        var dueSources = 0;
        var manualSources = 0;
        var startedRuns = 0;

        foreach (var source in scheduledSources)
        {
            if (HasBlockedManualSyncRequest(source))
            {
                logger.LogWarning(
                    "Skipping queued manual ingestion for tenant {TenantId} ({TenantName}) and source {SourceKey} because the source is disabled or credentials are missing.",
                    source.TenantId,
                    source.TenantName,
                    source.SourceKey
                );

                await ClearBlockedManualSyncRequestAsync(source.Id, ct);
                continue;
            }

            var isManualSync = IsManualSyncQueued(source);
            var isDue = IngestionScheduleEvaluator.IsDue(
                source.SourceKey,
                source.Enabled,
                source.SyncSchedule,
                source.LastStartedAt,
                source.LastCompletedAt,
                now);

            if (source.Enabled && HasConfiguredCredentials(source))
            {
                runnableSources++;
            }

            if (!isManualSync && !isDue)
            {
                continue;
            }

            dueSources++;
            if (isManualSync)
            {
                manualSources++;
            }

            logger.LogInformation(
                "Running {TriggerType} ingestion for tenant {TenantId} ({TenantName}) and source {SourceKey}",
                isManualSync ? "manual" : "scheduled",
                source.TenantId,
                source.TenantName,
                source.SourceKey
            );

            if (await RunSourceIngestionAsync(source.TenantId, source.SourceKey, ct))
            {
                startedRuns++;
            }
        }

        logger.LogInformation(
            "Completed ingestion polling cycle at {CycleCompletedAt}. Sources scanned: {SourceCount}. Runnable sources: {RunnableSources}. Due sources: {DueSources}. Manual syncs queued: {ManualSources}. Started runs: {StartedRuns}.",
            DateTimeOffset.UtcNow,
            scheduledSources.Count,
            runnableSources,
            dueSources,
            manualSources,
            startedRuns
        );
    }

    private async Task<List<ScheduledSource>> LoadScheduledSourcesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var secretStore = scope.ServiceProvider.GetRequiredService<ISecretStore>();

        var openBaoStatus = await secretStore.GetStatusAsync(ct);
        if (!openBaoStatus.IsAvailable || !openBaoStatus.IsInitialized || openBaoStatus.IsSealed)
        {
            logger.LogWarning(
                "Skipping ingestion cycle because OpenBao is not ready. Available: {IsAvailable}. Initialized: {IsInitialized}. Sealed: {IsSealed}",
                openBaoStatus.IsAvailable,
                openBaoStatus.IsInitialized,
                openBaoStatus.IsSealed
            );
            return [];
        }

        var tenantNames = await dbContext
            .Tenants.AsNoTracking()
            .ToDictionaryAsync(tenant => tenant.Id, tenant => tenant.Name, ct);
        var sources = await dbContext.TenantSourceConfigurations.AsNoTracking().ToListAsync(ct);

        return sources
            .Where(source => tenantNames.ContainsKey(source.TenantId))
            .Select(source => new ScheduledSource(
                source.Id,
                source.TenantId,
                tenantNames[source.TenantId],
                source.SourceKey,
                source.Enabled,
                source.CredentialTenantId,
                source.ClientId,
                source.SecretRef,
                source.ApiBaseUrl,
                source.TokenScope,
                source.SyncSchedule,
                source.StoredCredentialId,
                source.ManualRequestedAt,
                source.LastStartedAt,
                source.LastCompletedAt,
                source.LinkedSourceKey
            ))
            .ToList();
    }

    private async Task ClearBlockedManualSyncRequestAsync(Guid sourceId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        await dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .Where(item => item.Id == sourceId)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(item => item.ManualRequestedAt, (DateTimeOffset?)null)
                        .SetProperty(item => item.LastCompletedAt, DateTimeOffset.UtcNow)
                        .SetProperty(item => item.LastStatus, "Failed")
                        .SetProperty(
                            item => item.LastError,
                            "Manual sync skipped because the source is disabled or credentials are incomplete."
                        ),
                ct
            );
    }

    private async Task<bool> RunSourceIngestionAsync(
        Guid tenantId,
        string sourceKey,
        CancellationToken ct
    )
    {
        using var scope = scopeFactory.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<IngestionService>();
        var started = await ingestionService.RunIngestionAsync(tenantId, sourceKey, ct);
        if (!started)
        {
            return false;
        }

        try
        {
            var briefingService =
                scope.ServiceProvider.GetRequiredService<ExecutiveDashboardBriefingService>();
            await briefingService.RefreshAsync(tenantId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to refresh executive dashboard briefing after ingestion for tenant {TenantId}",
                tenantId
            );
        }

        return true;
    }

    private static bool IsManualSyncQueued(ScheduledSource source)
    {
        if (!source.Enabled || !SupportsManualSync(source) || !HasConfiguredCredentials(source))
        {
            return false;
        }

        var manualRequestedAt = source.ManualRequestedAt?.ToUniversalTime();
        if (!manualRequestedAt.HasValue)
        {
            return false;
        }

        var lastStartedAt = source.LastStartedAt?.ToUniversalTime();
        return !lastStartedAt.HasValue || manualRequestedAt > lastStartedAt;
    }

    private static bool HasBlockedManualSyncRequest(ScheduledSource source)
    {
        return source.ManualRequestedAt.HasValue
            && (
                !source.Enabled || !SupportsManualSync(source) || !HasConfiguredCredentials(source)
            );
    }

    internal static bool HasConfiguredCredentials(ScheduledSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.LinkedSourceKey))
            return true;

        if (source.StoredCredentialId.HasValue)
            return true;

        return !string.IsNullOrWhiteSpace(source.CredentialTenantId)
            && !string.IsNullOrWhiteSpace(source.ClientId)
            && !string.IsNullOrWhiteSpace(source.SecretRef);
    }

    private static bool SupportsManualSync(ScheduledSource source) =>
        TenantSourceCatalog.SupportsManualSync(source.SourceKey);

    internal sealed record ScheduledSource(
        Guid Id,
        Guid TenantId,
        string TenantName,
        string SourceKey,
        bool Enabled,
        string CredentialTenantId,
        string ClientId,
        string SecretRef,
        string ApiBaseUrl,
        string TokenScope,
        string SyncSchedule,
        Guid? StoredCredentialId,
        DateTimeOffset? ManualRequestedAt,
        DateTimeOffset? LastStartedAt,
        DateTimeOffset? LastCompletedAt,
        string? LinkedSourceKey = null
    );
}
