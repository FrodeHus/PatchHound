using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vigil.Api.Auth;
using Vigil.Api.Models;
using Vigil.Api.Models.Admin;
using Vigil.Core.Services;
using Vigil.Infrastructure.Data;

namespace Vigil.Api.Controllers;

[ApiController]
[Route("api/teams")]
[Authorize]
public class TeamsController : ControllerBase
{
    private readonly VigilDbContext _dbContext;
    private readonly TeamService _teamService;

    public TeamsController(VigilDbContext dbContext, TeamService teamService)
    {
        _dbContext = dbContext;
        _teamService = teamService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ManageTeams)]
    public async Task<ActionResult<PagedResponse<TeamDto>>> List(
        [FromQuery] Guid? tenantId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.Teams.AsNoTracking().AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(t => t.TenantId == tenantId.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.Name)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(t => new TeamDto(
                t.Id,
                t.TenantId,
                t.Name,
                t.Members.Count
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<TeamDto>(items, totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ManageTeams)]
    public async Task<ActionResult<TeamDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var team = await _dbContext.Teams
            .AsNoTracking()
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (team is null)
            return NotFound();

        var userIds = team.Members.Select(m => m.UserId).ToList();
        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return Ok(new TeamDetailDto(
            team.Id,
            team.TenantId,
            team.Name,
            team.Members.Select(m => new TeamMemberDto(
                m.UserId,
                users.TryGetValue(m.UserId, out var u) ? u.DisplayName : "Unknown",
                users.TryGetValue(m.UserId, out var u2) ? u2.Email : "Unknown"
            )).ToList()
        ));
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManageTeams)]
    public async Task<ActionResult<TeamDto>> Create(
        [FromBody] CreateTeamRequest request,
        CancellationToken ct
    )
    {
        var result = await _teamService.CreateTeamAsync(request.TenantId, request.Name, ct);

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var team = result.Value;
        return CreatedAtAction(
            nameof(Get),
            new { id = team.Id },
            new TeamDto(team.Id, team.TenantId, team.Name, 0)
        );
    }

    [HttpPut("{id:guid}/members")]
    [Authorize(Policy = Policies.ManageTeams)]
    public async Task<IActionResult> UpdateMembers(
        Guid id,
        [FromBody] UpdateMembersRequest request,
        CancellationToken ct
    )
    {
        var result = request.Action.ToLowerInvariant() switch
        {
            "add" => await _teamService.AddMemberAsync(id, request.UserId, ct),
            "remove" => await _teamService.RemoveMemberAsync(id, request.UserId, ct),
            _ => null
        };

        if (result is null)
            return BadRequest(new ProblemDetails { Title = "Invalid action. Use 'add' or 'remove'." });

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return NoContent();
    }
}
