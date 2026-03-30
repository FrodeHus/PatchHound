using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
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

    public async Task ApplyCustomerAccessGroupsForUserAsync(Guid userId, CancellationToken ct)
    {
        var roleAssignments = await _dbContext.UserTenantRoles
            .AsNoTracking()
            .Where(roleAssignment =>
                roleAssignment.UserId == userId
                && (roleAssignment.Role == RoleName.CustomerAdmin
                    || roleAssignment.Role == RoleName.CustomerOperator
                    || roleAssignment.Role == RoleName.CustomerViewer))
            .ToListAsync(ct);

        var tenantIds = roleAssignments
            .Select(item => item.TenantId)
            .Distinct()
            .ToList();

        if (tenantIds.Count == 0)
        {
            var existingCustomerMemberships = await _dbContext.TeamMembers
                .Where(member => member.UserId == userId)
                .Where(member =>
                    member.Team.Name == DefaultTeamHelper.CustomerAdminsTeamName
                    || member.Team.Name == DefaultTeamHelper.CustomerOperatorsTeamName
                    || member.Team.Name == DefaultTeamHelper.CustomerViewersTeamName)
                .ToListAsync(ct);

            if (existingCustomerMemberships.Count > 0)
            {
                _dbContext.TeamMembers.RemoveRange(existingCustomerMemberships);
                await _dbContext.SaveChangesAsync(ct);
            }

            return;
        }

        foreach (var tenantId in tenantIds)
        {
            await DefaultTeamHelper.EnsureCustomerAccessTeamsAsync(_dbContext, tenantId, ct);
        }

        await _dbContext.SaveChangesAsync(ct);

        var customerTeams = await _dbContext.Teams
            .Where(team =>
                tenantIds.Contains(team.TenantId)
                && (team.Name == DefaultTeamHelper.CustomerAdminsTeamName
                    || team.Name == DefaultTeamHelper.CustomerOperatorsTeamName
                    || team.Name == DefaultTeamHelper.CustomerViewersTeamName))
            .ToListAsync(ct);

        var desiredMemberships = roleAssignments
            .SelectMany(roleAssignment => customerTeams
                .Where(team => team.TenantId == roleAssignment.TenantId)
                .Where(team =>
                    (roleAssignment.Role == RoleName.CustomerAdmin && team.Name == DefaultTeamHelper.CustomerAdminsTeamName)
                    || (roleAssignment.Role == RoleName.CustomerOperator && team.Name == DefaultTeamHelper.CustomerOperatorsTeamName)
                    || (roleAssignment.Role == RoleName.CustomerViewer && team.Name == DefaultTeamHelper.CustomerViewersTeamName))
                .Select(team => team.Id))
            .ToHashSet();

        var existingMemberships = await _dbContext.TeamMembers
            .Where(member => member.UserId == userId)
            .Where(member => customerTeams.Select(team => team.Id).Contains(member.TeamId))
            .ToListAsync(ct);

        var membershipsToRemove = existingMemberships
            .Where(member => !desiredMemberships.Contains(member.TeamId))
            .ToList();
        if (membershipsToRemove.Count > 0)
        {
            _dbContext.TeamMembers.RemoveRange(membershipsToRemove);
        }

        var existingTeamIds = existingMemberships.Select(member => member.TeamId).ToHashSet();
        var membershipsToAdd = desiredMemberships
            .Where(teamId => !existingTeamIds.Contains(teamId))
            .Select(teamId => TeamMember.Create(teamId, userId))
            .ToList();

        if (membershipsToAdd.Count > 0)
        {
            await _dbContext.TeamMembers.AddRangeAsync(membershipsToAdd, ct);
        }

        await _dbContext.SaveChangesAsync(ct);
    }
}
