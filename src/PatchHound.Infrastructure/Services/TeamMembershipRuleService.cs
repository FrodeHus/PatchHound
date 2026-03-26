using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class TeamMembershipRuleService
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly TeamMembershipRuleFilterBuilder _filterBuilder;

    public TeamMembershipRuleService(
        PatchHoundDbContext dbContext,
        TeamMembershipRuleFilterBuilder filterBuilder
    )
    {
        _dbContext = dbContext;
        _filterBuilder = filterBuilder;
    }

    public async Task<(int Count, List<User> Samples)> PreviewAsync(
        Guid tenantId,
        FilterNode filter,
        CancellationToken ct
    )
    {
        var predicate = _filterBuilder.Build(tenantId, filter);
        var query = _dbContext.Users
            .AsNoTracking()
            .Where(user => user.TenantRoles.Any(role => role.TenantId == tenantId))
            .Where(predicate);

        var count = await query.CountAsync(ct);
        var samples = await query
            .OrderBy(user => user.DisplayName)
            .Take(8)
            .ToListAsync(ct);

        return (count, samples);
    }

    public async Task<int> ApplyAsync(TeamMembershipRule rule, CancellationToken ct)
    {
        var predicate = _filterBuilder.Build(rule.TenantId, rule.ParseFilter());
        var team = await _dbContext.Teams
            .Include(item => item.Members)
            .FirstOrDefaultAsync(item => item.Id == rule.TeamId, ct);

        if (team is null)
        {
            return 0;
        }

        var matches = team.IsDynamic && rule.Enabled
            ? await _dbContext.Users
                .Where(user => user.TenantRoles.Any(role => role.TenantId == rule.TenantId))
                .Where(predicate)
                .ToListAsync(ct)
            : [];

        var matchingUserIds = matches.Select(user => user.Id).ToHashSet();
        foreach (var member in team.Members.ToList())
        {
            if (!matchingUserIds.Contains(member.UserId))
            {
                team.RemoveMember(member.UserId);
            }
        }

        foreach (var user in matches)
        {
            team.AddMember(user);
        }

        rule.RecordExecution(matches.Count);
        await _dbContext.SaveChangesAsync(ct);
        return matches.Count;
    }

    public async Task ApplyForUserAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var user = await _dbContext.Users
            .Include(item => item.TenantRoles)
            .FirstOrDefaultAsync(item => item.Id == userId, ct);

        if (user is null)
        {
            return;
        }

        var rules = await _dbContext.TeamMembershipRules
            .Include(rule => rule.Team)
            .Where(rule => rule.TenantId == tenantId && rule.Enabled && rule.Team.IsDynamic)
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            return;
        }

        foreach (var rule in rules)
        {
            var predicate = _filterBuilder.Build(tenantId, rule.ParseFilter()).Compile();
            if (!predicate(user))
            {
                continue;
            }

            var membershipExists = await _dbContext.TeamMembers
                .AnyAsync(member => member.TeamId == rule.TeamId && member.UserId == userId, ct);
            if (predicate(user))
            {
                if (!membershipExists)
                {
                    await _dbContext.TeamMembers.AddAsync(TeamMember.Create(rule.TeamId, userId), ct);
                }
            }
            else if (membershipExists)
            {
                var membership = await _dbContext.TeamMembers
                    .FirstOrDefaultAsync(member => member.TeamId == rule.TeamId && member.UserId == userId, ct);
                if (membership is not null)
                {
                    _dbContext.TeamMembers.Remove(membership);
                }
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }
}
