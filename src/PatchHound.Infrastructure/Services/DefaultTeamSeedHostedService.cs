using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class DefaultTeamSeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DefaultTeamSeedHostedService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var createdCount = await DefaultTeamHelper.EnsureDefaultTeamsForAllTenantsAsync(dbContext, cancellationToken);

        if (createdCount > 0)
        {
            logger.LogInformation(
                "Ensured {CreatedCount} missing default assignment teams across tenants.",
                createdCount
            );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
