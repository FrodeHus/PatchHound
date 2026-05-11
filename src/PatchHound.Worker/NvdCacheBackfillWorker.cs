using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Worker;

public class NvdCacheBackfillWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<NvdCacheBackfillWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NvdCacheBackfillWorker started with interval {Interval}", Interval);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunCycleAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<NvdCacheBackfillService>();
            var stats = await svc.RunAsync(ct: ct);

            if (stats.Processed > 0)
            {
                logger.LogInformation(
                    "NVD backfill cycle: processed={Processed} succeeded={Succeeded} failed={Failed}",
                    stats.Processed, stats.Succeeded, stats.Failed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error during NVD cache backfill cycle");
        }
    }
}
