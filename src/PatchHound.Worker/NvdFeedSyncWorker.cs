using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Interfaces;

namespace PatchHound.Worker;

public class NvdFeedSyncWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<NvdFeedSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IncrementalInterval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SyncInitialAsync(stoppingToken);

        using var timer = new PeriodicTimer(IncrementalInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncModifiedAsync(stoppingToken);
        }
    }

    private async Task SyncInitialAsync(CancellationToken ct)
    {
        var currentYear = DateTimeOffset.UtcNow.Year;
        logger.LogInformation(
            "NvdFeedSyncWorker: starting initial sync for years {From}-{To} + modified",
            currentYear - 4, currentYear);

        for (var year = currentYear - 4; year <= currentYear; year++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<INvdFeedSyncService>();
                await service.SyncYearFeedAsync(year, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "NVD initial sync failed for year {Year}", year);
            }
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<INvdFeedSyncService>();
            await service.SyncModifiedFeedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "NVD initial modified feed sync failed");
        }
    }

    private async Task SyncModifiedAsync(CancellationToken ct)
    {
        logger.LogDebug("NvdFeedSyncWorker: incremental modified-feed sync");
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<INvdFeedSyncService>();
        try
        {
            await service.SyncModifiedFeedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "NVD modified feed sync failed");
        }
    }
}
