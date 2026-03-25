using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public static class DefaultTeamHelper
{
    public const string DefaultTeamName = "Default";

    public static async Task<Team> EnsureDefaultTeamAsync(
        PatchHoundDbContext dbContext,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var existingTeam = await dbContext.Teams
            .FirstOrDefaultAsync(team => team.TenantId == tenantId && team.IsDefault, ct);
        if (existingTeam is not null)
            return existingTeam;

        var legacyNamedTeam = await dbContext.Teams
            .FirstOrDefaultAsync(team => team.TenantId == tenantId && team.Name == DefaultTeamName, ct);
        if (legacyNamedTeam is not null)
        {
            dbContext.Entry(legacyNamedTeam).Property(nameof(Team.IsDefault)).CurrentValue = true;
            await dbContext.SaveChangesAsync(ct);
            return legacyNamedTeam;
        }

        var team = Team.CreateDefault(tenantId, DefaultTeamName);
        await dbContext.Teams.AddAsync(team, ct);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return team;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(team).State = EntityState.Detached;

            return await dbContext.Teams
                .FirstAsync(item => item.TenantId == tenantId && item.IsDefault, ct);
        }
    }

    public static async Task<int> EnsureDefaultTeamsForAllTenantsAsync(
        PatchHoundDbContext dbContext,
        CancellationToken ct
    )
    {
        var tenantIds = await dbContext.Tenants
            .Select(tenant => tenant.Id)
            .ToListAsync(ct);

        var createdCount = 0;
        foreach (var tenantId in tenantIds)
        {
            var exists = await dbContext.Teams
                .AnyAsync(team => team.TenantId == tenantId && team.IsDefault, ct);
            if (exists)
                continue;

            await EnsureDefaultTeamAsync(dbContext, tenantId, ct);
            createdCount++;
        }

        return createdCount;
    }
}
