using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Interfaces;

namespace PatchHound.Infrastructure.Services;

public class NvdFeedSyncDispatcher(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime applicationLifetime,
    ILogger<NvdFeedSyncDispatcher> logger
) : INvdFeedSyncDispatcher
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public void QueueModifiedSync()
    {
        _ = Task.Run(() => RunModifiedSyncAsync(applicationLifetime.ApplicationStopping));
    }

    public void QueueFullSync(int fromYear, int toYear)
    {
        _ = Task.Run(() => RunFullSyncAsync(fromYear, toYear, applicationLifetime.ApplicationStopping));
    }

    private async Task RunModifiedSyncAsync(CancellationToken ct)
    {
        await RunSerializedAsync(
            "modified feed",
            async service => await service.SyncModifiedFeedAsync(ct),
            ct
        );
    }

    private async Task RunFullSyncAsync(int fromYear, int toYear, CancellationToken ct)
    {
        await RunSerializedAsync(
            $"full feed {fromYear}-{toYear}",
            async service =>
            {
                for (var year = fromYear; year <= toYear; year++)
                {
                    await service.SyncYearFeedAsync(year, force: true, ct);
                }
            },
            ct
        );
    }

    private async Task RunSerializedAsync(
        string description,
        Func<NvdFeedSyncService, Task> syncAction,
        CancellationToken ct
    )
    {
        try
        {
            await _syncLock.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<NvdFeedSyncService>();

            logger.LogInformation("NVD manual {Description} sync started.", description);
            await syncAction(service);
            logger.LogInformation("NVD manual {Description} sync completed.", description);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogDebug("NVD manual {Description} sync canceled during application shutdown.", description);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "NVD manual {Description} sync failed.", description);
        }
        finally
        {
            _syncLock.Release();
        }
    }
}
