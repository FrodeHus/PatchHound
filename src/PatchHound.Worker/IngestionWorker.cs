using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        var tenants = await dbContext.Tenants.AsNoTracking().ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            var sources = TenantSourceSettings.ReadSources(tenant.Settings);
            var now = DateTimeOffset.UtcNow;

            foreach (var source in sources.Where(source =>
                IsManualSyncQueued(source) || IngestionScheduleEvaluator.IsDue(source, now)))
            {
                var isManualSync = IsManualSyncQueued(source);
                logger.LogInformation(
                    "Running {TriggerType} ingestion for tenant {TenantId} ({TenantName}) and source {SourceKey}",
                    isManualSync ? "manual" : "scheduled",
                    tenant.Id,
                    tenant.Name,
                    source.Key
                );

                await ingestionService.RunIngestionAsync(tenant.Id, source.Key, ct);
            }
        }
    }

    private static bool IsManualSyncQueued(PersistedIngestionSource source)
    {
        if (!source.Enabled || !TenantSourceSettings.HasConfiguredCredentials(source.Credentials))
        {
            return false;
        }

        var manualRequestedAt = source.Runtime?.ManualRequestedAt?.ToUniversalTime();
        if (!manualRequestedAt.HasValue)
        {
            return false;
        }

        var lastStartedAt = source.Runtime?.LastStartedAt?.ToUniversalTime();
        return !lastStartedAt.HasValue || manualRequestedAt > lastStartedAt;
    }
}
