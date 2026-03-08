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
        logger.LogInformation("IngestionWorker started");

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
        using var scope = scopeFactory.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>() as WorkerTenantContext;
        if (tenantContext is not null)
            await tenantContext.InitializeAsync(ct);

        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var ingestionService = scope.ServiceProvider.GetRequiredService<IngestionService>();
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
            return;
        }

        var sources = await dbContext
            .TenantSourceConfigurations
            .ToListAsync(ct);
        var tenants = await dbContext.Tenants.AsNoTracking().ToDictionaryAsync(tenant => tenant.Id, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var source in sources)
        {
            if (!tenants.TryGetValue(source.TenantId, out var tenant))
            {
                continue;
            }

            if (HasBlockedManualSyncRequest(source))
            {
                logger.LogWarning(
                    "Skipping queued manual ingestion for tenant {TenantId} ({TenantName}) and source {SourceKey} because the source is disabled or credentials are missing.",
                    tenant.Id,
                    tenant.Name,
                    source.SourceKey
                );

                source.UpdateRuntime(
                    null,
                    source.LastStartedAt,
                    DateTimeOffset.UtcNow,
                    source.LastSucceededAt,
                    "Failed",
                    "Manual sync skipped because the source is disabled or credentials are incomplete."
                );
                await dbContext.SaveChangesAsync(ct);
                continue;
            }

            var isManualSync = IsManualSyncQueued(source);
            if (!isManualSync && !IngestionScheduleEvaluator.IsDue(source, now))
            {
                continue;
            }

            logger.LogInformation(
                "Running {TriggerType} ingestion for tenant {TenantId} ({TenantName}) and source {SourceKey}",
                isManualSync ? "manual" : "scheduled",
                tenant.Id,
                tenant.Name,
                source.SourceKey
            );

            await ingestionService.RunIngestionAsync(tenant.Id, source.SourceKey, ct);
        }
    }

    private static bool IsManualSyncQueued(PatchHound.Core.Entities.TenantSourceConfiguration source)
    {
        if (
            !source.Enabled
            || !TenantSourceCatalog.SupportsManualSync(source)
            || !TenantSourceCatalog.HasConfiguredCredentials(source)
        )
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

    private static bool HasBlockedManualSyncRequest(
        PatchHound.Core.Entities.TenantSourceConfiguration source
    )
    {
        return source.ManualRequestedAt.HasValue
            && (
                !source.Enabled
                || !TenantSourceCatalog.SupportsManualSync(source)
                || !TenantSourceCatalog.HasConfiguredCredentials(source)
            );
    }
}
