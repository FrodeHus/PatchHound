using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Admin;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly UserService _userService;
    private readonly TeamMembershipRuleService _teamMembershipRuleService;
    private readonly ITenantContext _tenantContext;

    public UsersController(
        PatchHoundDbContext dbContext,
        UserService userService,
        TeamMembershipRuleService teamMembershipRuleService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _userService = userService;
        _teamMembershipRuleService = teamMembershipRuleService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<ActionResult<PagedResponse<UserListItemDto>>> List(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] string? status,
        [FromQuery] Guid? teamId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var tenantName = await _dbContext.Tenants.AsNoTracking()
            .Where(item => item.Id == tenantId)
            .Select(item => item.Name)
            .FirstOrDefaultAsync(ct) ?? "Unknown tenant";

        var query = _dbContext.Users.AsNoTracking()
            .Where(user => user.TenantRoles.Any(roleAssignment => roleAssignment.TenantId == tenantId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim().ToLower();
            query = query.Where(user =>
                user.DisplayName.ToLower().Contains(searchTerm)
                || user.Email.ToLower().Contains(searchTerm)
                || (user.Company != null && user.Company.ToLower().Contains(searchTerm)));
        }

        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<RoleName>(role, out var parsedRole))
        {
            query = query.Where(user => user.TenantRoles.Any(roleAssignment =>
                roleAssignment.TenantId == tenantId && roleAssignment.Role == parsedRole));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = status.Trim().ToLowerInvariant() switch
            {
                "enabled" => query.Where(user => user.IsEnabled),
                "disabled" => query.Where(user => !user.IsEnabled),
                _ => query,
            };
        }

        if (teamId.HasValue)
        {
            query = query.Where(user => _dbContext.TeamMembers.Any(member =>
                member.UserId == user.Id && member.TeamId == teamId.Value));
        }

        var totalCount = await query.CountAsync(ct);

        var users = await query
            .OrderBy(user => user.DisplayName)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var userIds = users.Select(item => item.Id).ToList();
        var roleAssignments = await _dbContext.UserTenantRoles.AsNoTracking()
            .Where(roleAssignment => roleAssignment.TenantId == tenantId && userIds.Contains(roleAssignment.UserId))
            .Select(roleAssignment => new
            {
                roleAssignment.UserId,
                RoleName = roleAssignment.Role.ToString()
            })
            .ToListAsync(ct);
        var teamMemberships = await _dbContext.TeamMembers.AsNoTracking()
            .Where(member => userIds.Contains(member.UserId) && member.Team.TenantId == tenantId)
            .Select(member => new
            {
                member.UserId,
                member.TeamId,
                TeamName = member.Team.Name,
                member.Team.IsDefault
            })
            .ToListAsync(ct);

        var items = users.Select(user => new UserListItemDto(
            user.Id,
            tenantId,
            tenantName,
            user.Email,
            user.DisplayName,
            user.Company,
            user.IsEnabled,
            roleAssignments
                .Where(roleAssignment => roleAssignment.UserId == user.Id)
                .Select(roleAssignment => roleAssignment.RoleName)
                .Distinct()
                .OrderBy(roleName => roleName)
                .ToList(),
            teamMemberships
                .Where(membership => membership.UserId == user.Id)
                .OrderBy(membership => membership.TeamName)
                .Select(membership => new UserTeamMembershipDto(
                    membership.TeamId,
                    membership.TeamName,
                    membership.IsDefault))
                .ToList()
        )).ToList();

        return Ok(new PagedResponse<UserListItemDto>(
            items,
            totalCount,
            pagination.Page,
            pagination.BoundedPageSize
        ));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<ActionResult<UserDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var tenantName = await _dbContext.Tenants.AsNoTracking()
            .Where(item => item.Id == tenantId)
            .Select(item => item.Name)
            .FirstOrDefaultAsync(ct) ?? "Unknown tenant";

        var user = await _dbContext.Users.AsNoTracking()
            .Where(item => item.Id == id)
            .Where(item => item.TenantRoles.Any(roleAssignment => roleAssignment.TenantId == tenantId))
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return NotFound();
        }

        var roles = await _dbContext.UserTenantRoles.AsNoTracking()
            .Where(roleAssignment => roleAssignment.UserId == id && roleAssignment.TenantId == tenantId)
            .Select(roleAssignment => roleAssignment.Role.ToString())
            .Distinct()
            .OrderBy(roleName => roleName)
            .ToListAsync(ct);

        var teams = await _dbContext.TeamMembers.AsNoTracking()
            .Where(member => member.UserId == id && member.Team.TenantId == tenantId)
            .OrderBy(member => member.Team.Name)
            .Select(member => new UserTeamMembershipDto(
                member.TeamId,
                member.Team.Name,
                member.Team.IsDefault
            ))
            .ToListAsync(ct);

        var auditEntries = await BuildUserAuditAsync(tenantId, id, null, null, ct);

        return Ok(new UserDetailDto(
            user.Id,
            tenantId,
            tenantName,
            user.Email,
            user.DisplayName,
            user.Company,
            user.IsEnabled,
            user.EntraObjectId,
            roles,
            teams,
            auditEntries.Take(12).ToList()
        ));
    }

    [HttpGet("{id:guid}/audit")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<ActionResult<PagedResponse<UserAuditSummaryDto>>> GetAudit(
        Guid id,
        [FromQuery] string? entityType,
        [FromQuery] string? action,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var tenantName = await _dbContext.Tenants.AsNoTracking()
            .Where(item => item.Id == tenantId)
            .Select(item => item.Name)
            .FirstOrDefaultAsync(ct) ?? "Unknown tenant";

        var entries = await BuildUserAuditAsync(tenantId, id, entityType, action, ct);
        var pageItems = entries
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToList();

        return Ok(new PagedResponse<UserAuditSummaryDto>(
            pageItems,
            entries.Count,
            pagination.Page,
            pagination.BoundedPageSize
        ));
    }

    [HttpPost("invite")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<ActionResult<UserDetailDto>> Invite(
        [FromBody] InviteUserRequest request,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var tenantName = await _dbContext.Tenants.AsNoTracking()
            .Where(item => item.Id == tenantId)
            .Select(item => item.Name)
            .FirstOrDefaultAsync(ct) ?? "Unknown tenant";

        var result = await _userService.InviteUserAsync(
            request.Email,
            request.DisplayName,
            request.EntraObjectId,
            ct
        );

        if (!result.IsSuccess)
        {
            return BadRequest(new ProblemDetails { Title = result.Error });
        }

        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, new UserDetailDto(
            result.Value.Id,
            tenantId,
            tenantName,
            result.Value.Email,
            result.Value.DisplayName,
            result.Value.Company,
            result.Value.IsEnabled,
            result.Value.EntraObjectId,
            [],
            [],
            []
        ));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var user = await _dbContext.Users
            .Include(item => item.TenantRoles)
            .FirstOrDefaultAsync(item => item.Id == id, ct);

        if (user is null)
        {
            return NotFound();
        }

        var currentTenantRoles = await _dbContext.UserTenantRoles
            .Where(roleAssignment => roleAssignment.UserId == id && roleAssignment.TenantId == tenantId)
            .ToListAsync(ct);

        var requestedRoles = request.Roles
            .Select(roleName => Enum.TryParse<RoleName>(roleName, out var parsedRole) ? parsedRole : (RoleName?)null)
            .ToList();

        if (requestedRoles.Any(roleName => roleName is null))
        {
            return BadRequest(new ProblemDetails { Title = "One or more roles are invalid." });
        }

        user.UpdateProfile(request.Email.Trim(), request.DisplayName.Trim(), request.Company);
        user.SetEnabled(request.IsEnabled);

        var requestedRoleSet = requestedRoles.Select(roleName => roleName!.Value).ToHashSet();
        var existingRoleSet = currentTenantRoles.Select(roleAssignment => roleAssignment.Role).ToHashSet();

        var rolesToRemove = currentTenantRoles
            .Where(roleAssignment => !requestedRoleSet.Contains(roleAssignment.Role))
            .ToList();
        if (rolesToRemove.Count > 0)
        {
            _dbContext.UserTenantRoles.RemoveRange(rolesToRemove);
        }

        var rolesToAdd = requestedRoleSet
            .Where(roleName => !existingRoleSet.Contains(roleName))
            .Select(roleName => UserTenantRole.Create(id, tenantId, roleName))
            .ToList();
        if (rolesToAdd.Count > 0)
        {
            await _dbContext.UserTenantRoles.AddRangeAsync(rolesToAdd, ct);
        }

        var tenantTeams = await _dbContext.Teams
            .Where(team => team.TenantId == tenantId)
            .Select(team => new { team.Id })
            .ToListAsync(ct);
        var validTeamIds = tenantTeams.Select(team => team.Id).ToHashSet();
        if (request.TeamIds.Any(teamId => !validTeamIds.Contains(teamId)))
        {
            return BadRequest(new ProblemDetails { Title = "One or more assignment groups are invalid for this tenant." });
        }

        var existingMemberships = await _dbContext.TeamMembers
            .Where(member => member.UserId == id && validTeamIds.Contains(member.TeamId))
            .ToListAsync(ct);
        var existingMembershipTeamIds = existingMemberships.Select(member => member.TeamId).ToHashSet();
        var requestedTeamIds = request.TeamIds.ToHashSet();

        var membershipsToRemove = existingMemberships
            .Where(member => !requestedTeamIds.Contains(member.TeamId))
            .ToList();
        if (membershipsToRemove.Count > 0)
        {
            _dbContext.TeamMembers.RemoveRange(membershipsToRemove);
        }

        var membershipsToAdd = requestedTeamIds
            .Where(teamId => !existingMembershipTeamIds.Contains(teamId))
            .Select(teamId => TeamMember.Create(teamId, id))
            .ToList();
        if (membershipsToAdd.Count > 0)
        {
            await _dbContext.TeamMembers.AddRangeAsync(membershipsToAdd, ct);
        }

        await _dbContext.SaveChangesAsync(ct);
        await _teamMembershipRuleService.ApplyForUserAsync(tenantId, id, ct);
        return NoContent();
    }

    private async Task<List<UserAuditSummaryDto>> BuildUserAuditAsync(
        Guid tenantId,
        Guid userId,
        string? entityType,
        string? action,
        CancellationToken ct
    )
    {
        var teamIds = await _dbContext.TeamMembers.AsNoTracking()
            .Where(member => member.UserId == userId && member.Team.TenantId == tenantId)
            .Select(member => member.TeamId)
            .ToListAsync(ct);

        var auditUsers = await _dbContext.Users.AsNoTracking()
            .ToDictionaryAsync(item => item.Id, item => item.DisplayName, ct);

        var entries = await _dbContext.AuditLogEntries.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId)
            .Where(entry =>
                entry.EntityType == nameof(User)
                || entry.EntityType == nameof(UserTenantRole)
                || entry.EntityType == nameof(TeamMember))
            .OrderByDescending(entry => entry.Timestamp)
            .ToListAsync(ct);

        entries = entries
            .Where(entry =>
                (entry.EntityType == nameof(User) && entry.EntityId == userId)
                || (entry.EntityType == nameof(UserTenantRole) && AuditEntryReferencesUser(entry, userId, tenantId))
                || (entry.EntityType == nameof(TeamMember) && AuditEntryReferencesUser(entry, userId, teamIds)))
            .ToList();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            entries = entries
                .Where(entry => string.Equals(entry.EntityType, entityType.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            entries = entries
                .Where(entry => string.Equals(entry.Action.ToString(), action.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return entries.Select(entry => new UserAuditSummaryDto(
            entry.Id,
            entry.EntityType,
            entry.EntityId,
            entry.Action.ToString(),
            SummarizeUserAuditEntry(entry),
            auditUsers.GetValueOrDefault(entry.UserId),
            entry.Timestamp
        )).ToList();
    }

    private bool AuditEntryReferencesUser(AuditLogEntry entry, Guid userId, Guid tenantId)
    {
        var oldValues = ParseAuditValues(entry.OldValues);
        var newValues = ParseAuditValues(entry.NewValues);

        return (
            GetGuidValue(oldValues, "UserId") == userId || GetGuidValue(newValues, "UserId") == userId
        ) && (
            GetGuidValue(oldValues, "TenantId") == tenantId || GetGuidValue(newValues, "TenantId") == tenantId
        );
    }

    private bool AuditEntryReferencesUser(AuditLogEntry entry, Guid userId, IReadOnlyCollection<Guid> teamIds)
    {
        var oldValues = ParseAuditValues(entry.OldValues);
        var newValues = ParseAuditValues(entry.NewValues);

        return (GetGuidValue(oldValues, "UserId") == userId || GetGuidValue(newValues, "UserId") == userId)
            && (
                (GetGuidValue(oldValues, "TeamId") is Guid oldTeamId && teamIds.Contains(oldTeamId))
                || (GetGuidValue(newValues, "TeamId") is Guid newTeamId && teamIds.Contains(newTeamId))
            );
    }

    private static Dictionary<string, JsonElement> ParseAuditValues(string? values)
    {
        if (string.IsNullOrWhiteSpace(values))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(values) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static Guid? GetGuidValue(Dictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static string? SummarizeUserAuditEntry(AuditLogEntry entry)
    {
        var values = ParseAuditValues(entry.NewValues);
        var oldValues = ParseAuditValues(entry.OldValues);

        return entry.EntityType switch
        {
            nameof(User) => values.TryGetValue("IsEnabled", out var enabled)
                ? $"Enabled set to {enabled.ToString().ToLowerInvariant()}."
                : values.TryGetValue("DisplayName", out var displayName)
                    ? $"Profile updated to {displayName.GetString()}."
                    : entry.Action.ToString(),
            nameof(UserTenantRole) => values.TryGetValue("Role", out var role)
                ? $"Role changed to {role.GetString()}."
                : oldValues.TryGetValue("Role", out var oldRole)
                    ? $"Role removed: {oldRole.GetString()}."
                    : entry.Action.ToString(),
            nameof(TeamMember) => values.TryGetValue("TeamId", out _)
                ? "Added to assignment group."
                : "Removed from assignment group.",
            _ => entry.Action.ToString(),
        };
    }
}
