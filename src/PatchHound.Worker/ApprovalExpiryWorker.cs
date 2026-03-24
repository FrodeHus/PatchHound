using Microsoft.Extensions.DependencyInjection;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Worker;

public class ApprovalExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<ApprovalExpiryWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ApprovalExpiryWorker started with polling interval {Interval}", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckExpiredTasksAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during approval expiry check cycle");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CheckExpiredTasksAsync(CancellationToken ct)
    {
        logger.LogInformation("Starting approval expiry check at {Time}", DateTimeOffset.UtcNow);

        using var scope = scopeFactory.CreateScope();
        var approvalTaskService = scope.ServiceProvider.GetRequiredService<ApprovalTaskService>();

        var expiredCount = await approvalTaskService.AutoDenyExpiredAsync(ct);

        logger.LogInformation(
            "Completed approval expiry check. Auto-denied {ExpiredCount} tasks.",
            expiredCount
        );
    }
}
