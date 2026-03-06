using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vigil.Api.Auth;
using Vigil.Api.Models;
using Vigil.Api.Models.Admin;
using Vigil.Core.Enums;
using Vigil.Core.Services;
using Vigil.Infrastructure.Data;

namespace Vigil.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly VigilDbContext _dbContext;
    private readonly UserService _userService;

    public UsersController(VigilDbContext dbContext, UserService userService)
    {
        _dbContext = dbContext;
        _userService = userService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<ActionResult<PagedResponse<UserDto>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.Users.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var users = await query
            .OrderBy(u => u.DisplayName)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Include(u => u.TenantRoles)
            .ToListAsync(ct);

        var tenantIds = users
            .SelectMany(u => u.TenantRoles.Select(r => r.TenantId))
            .Distinct()
            .ToList();

        var tenantNames = await _dbContext
            .Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var items = users
            .Select(u => new UserDto(
                u.Id,
                u.Email,
                u.DisplayName,
                u.TenantRoles.Select(r => new UserRoleDto(
                        r.TenantId,
                        tenantNames.GetValueOrDefault(r.TenantId, "Unknown"),
                        r.Role.ToString()
                    ))
                    .ToList()
            ))
            .ToList();

        return Ok(new PagedResponse<UserDto>(items, totalCount));
    }

    [HttpPost("invite")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<ActionResult<UserDto>> Invite(
        [FromBody] InviteUserRequest request,
        CancellationToken ct
    )
    {
        var result = await _userService.InviteUserAsync(
            request.Email,
            request.DisplayName,
            request.EntraObjectId,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var user = result.Value;
        return CreatedAtAction(
            nameof(List),
            new UserDto(user.Id, user.Email, user.DisplayName, [])
        );
    }

    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> UpdateRoles(
        Guid id,
        [FromBody] UpdateRolesRequest request,
        CancellationToken ct
    )
    {
        foreach (var assignment in request.Roles)
        {
            if (!Enum.TryParse<RoleName>(assignment.Role, out var role))
                return BadRequest(
                    new ProblemDetails { Title = $"Invalid role: {assignment.Role}" }
                );

            var result = await _userService.AssignRoleAsync(id, assignment.TenantId, role, ct);
            if (!result.IsSuccess)
                return BadRequest(new ProblemDetails { Title = result.Error });
        }

        return NoContent();
    }
}
