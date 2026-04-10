using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Inventory;

public class SourceSystemSeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SourceSystemSeedHostedService> logger
) : IHostedService
{
    private static readonly (string Key, string DisplayName)[] BuiltInSourceSystems =
    [
        ("defender", "Microsoft Defender for Endpoint"),
        ("authenticated-scan", "Authenticated Scan"),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        var existingKeys = await dbContext
            .SourceSystems.Select(s => s.Key)
            .ToListAsync(cancellationToken);
        var existingSet = new HashSet<string>(existingKeys, StringComparer.Ordinal);

        var createdCount = 0;
        foreach (var (key, displayName) in BuiltInSourceSystems)
        {
            if (existingSet.Contains(key))
            {
                continue;
            }

            dbContext.SourceSystems.Add(SourceSystem.Create(key, displayName));
            createdCount++;
        }

        if (createdCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Seeded {CreatedCount} missing built-in source systems.",
                createdCount
            );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
