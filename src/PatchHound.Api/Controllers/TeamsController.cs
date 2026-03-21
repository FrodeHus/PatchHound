using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Admin;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/teams")]
[Authorize]
public class TeamsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly TeamService _teamService;
    private readonly ITenantContext _tenantContext;

    public TeamsController(
        PatchHoundDbContext dbContext,
        TeamService teamService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _teamService = teamService;
        _tenantContext = tenantContext;
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
        {
            if (!_tenantContext.HasAccessToTenant(tenantId.Value))
                return Forbid();
            query = query.Where(t => t.TenantId == tenantId.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Select(t => new
            {
                Team = t,
                CurrentRiskScore = _dbContext.TeamRiskScores
                    .Where(score => score.TeamId == t.Id)
                    .Select(score => (decimal?)score.OverallScore)
                    .FirstOrDefault(),
            })
            .OrderByDescending(item => item.CurrentRiskScore ?? 0m)
            .ThenBy(item => item.Team.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(item => new TeamDto(
                item.Team.Id,
                item.Team.TenantId,
                _dbContext
                    .Tenants.Where(tenant => tenant.Id == item.Team.TenantId)
                    .Select(tenant => tenant.Name)
                    .FirstOrDefault() ?? "Unknown tenant",
                item.Team.Name,
                item.Team.Members.Count,
                item.CurrentRiskScore
            ))
            .ToListAsync(ct);

        return Ok(
            new PagedResponse<TeamDto>(
                items,
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ManageTeams)]
    public async Task<ActionResult<TeamDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var team = await _dbContext
            .Teams.AsNoTracking()
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (team is null)
            return NotFound();

        var userIds = team.Members.Select(m => m.UserId).ToList();
        var users = await _dbContext
            .Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);
        var tenantName = await _dbContext
            .Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == team.TenantId)
            .Select(tenant => tenant.Name)
            .FirstOrDefaultAsync(ct);
        var assignedAssetCount = await _dbContext
            .Assets.AsNoTracking()
            .CountAsync(asset => asset.OwnerTeamId == team.Id || asset.FallbackTeamId == team.Id, ct);
        var currentRiskScore = await _dbContext.TeamRiskScores.AsNoTracking()
            .Where(score => score.TeamId == team.Id)
            .Select(score => (decimal?)score.OverallScore)
            .FirstOrDefaultAsync(ct);
        var topRiskAssets = await _dbContext.AssetRiskScores.AsNoTracking()
            .Where(score => score.TenantId == team.TenantId)
            .Join(
                _dbContext.Assets.AsNoTracking()
                    .Where(asset => asset.OwnerTeamId == team.Id || asset.FallbackTeamId == team.Id),
                score => score.AssetId,
                asset => asset.Id,
                (score, asset) => new TeamRiskAssetDto(
                    asset.Id,
                    asset.AssetType == PatchHound.Core.Enums.AssetType.Device
                        ? asset.DeviceComputerDnsName ?? asset.Name
                        : asset.Name,
                    asset.AssetType.ToString(),
                    score.OverallScore,
                    score.MaxEpisodeRiskScore,
                    score.OpenEpisodeCount
                )
            )
            .OrderByDescending(item => item.CurrentRiskScore)
            .ThenByDescending(item => item.OpenEpisodeCount)
            .Take(5)
            .ToListAsync(ct);

        return Ok(
            new TeamDetailDto(
                team.Id,
                team.TenantId,
                tenantName ?? "Unknown tenant",
                team.Name,
                assignedAssetCount,
                currentRiskScore,
                topRiskAssets,
                team.Members.Select(m => new TeamMemberDto(
                        m.UserId,
                        users.TryGetValue(m.UserId, out var u) ? u.DisplayName : "Unknown",
                        users.TryGetValue(m.UserId, out var u2) ? u2.Email : "Unknown"
                    ))
                    .ToList()
            )
        );
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManageTeams)]
    public async Task<ActionResult<TeamDto>> Create(
        [FromBody] CreateTeamRequest request,
        CancellationToken ct
    )
    {
        if (!_tenantContext.HasAccessToTenant(request.TenantId))
            return Forbid();

        var result = await _teamService.CreateTeamAsync(request.TenantId, request.Name, ct);

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var team = result.Value;
        return CreatedAtAction(
            nameof(Get),
            new { id = team.Id },
            new TeamDto(team.Id, team.TenantId, string.Empty, team.Name, 0, null)
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
        var team = await _dbContext.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(team.TenantId))
            return Forbid();

        var result = request.Action.ToLowerInvariant() switch
        {
            "add" => await _teamService.AddMemberAsync(id, request.UserId, ct),
            "remove" => await _teamService.RemoveMemberAsync(id, request.UserId, ct),
            _ => null,
        };

        if (result is null)
            return BadRequest(
                new ProblemDetails { Title = "Invalid action. Use 'add' or 'remove'." }
            );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return NoContent();
    }
}
