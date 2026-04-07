using Microsoft.FeatureManagement;
using PatchHound.Core.Common;
using PatchHound.Infrastructure.AuthenticatedScans;

namespace PatchHound.Worker;

public class ScanSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    IFeatureManager featureManager,
    ILogger<ScanSchedulerWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ScanSchedulerWorker started with {Interval}s interval", Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await featureManager.IsEnabledAsync(FeatureFlags.AuthenticatedScans))
                {
                    using var scope = scopeFactory.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<ScanSchedulerTickHandler>();
                    await handler.TickAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during scan scheduler tick");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
