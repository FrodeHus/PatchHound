using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public static class DefaultTeamHelper
{
    public const string DefaultTeamName = "Default";
    public const string CustomerAdminsTeamName = "Customer Admins";
    public const string CustomerOperatorsTeamName = "Customer Operators";
    public const string CustomerViewersTeamName = "Customer Viewers";

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

    public static async Task<IReadOnlyDictionary<RoleName, Team>> EnsureCustomerAccessTeamsAsync(
        PatchHoundDbContext dbContext,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var mapping = new Dictionary<RoleName, string>
        {
            [RoleName.CustomerAdmin] = CustomerAdminsTeamName,
            [RoleName.CustomerOperator] = CustomerOperatorsTeamName,
            [RoleName.CustomerViewer] = CustomerViewersTeamName,
        };

        var teams = await dbContext.Teams
            .Where(team => team.TenantId == tenantId && mapping.Values.Contains(team.Name))
            .ToListAsync(ct);

        var result = new Dictionary<RoleName, Team>();
        foreach (var entry in mapping)
        {
            var team = teams.FirstOrDefault(item => item.Name == entry.Value);
            if (team is null)
            {
                team = Team.Create(tenantId, entry.Value);
                team.SetDynamic(true);
                await dbContext.Teams.AddAsync(team, ct);
                teams.Add(team);
            }
            else if (!team.IsDynamic)
            {
                team.SetDynamic(true);
            }

            result[entry.Key] = team;
        }

        return result;
    }
}
