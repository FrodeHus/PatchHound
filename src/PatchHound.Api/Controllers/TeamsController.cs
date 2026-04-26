using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Admin;
using PatchHound.Api.Models.RiskScore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/teams")]
[Authorize]
public class TeamsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly TeamService _teamService;
    private readonly TeamMembershipRuleService _teamMembershipRuleService;
    private readonly ITenantContext _tenantContext;

    public TeamsController(
        PatchHoundDbContext dbContext,
        TeamService teamService,
        TeamMembershipRuleService teamMembershipRuleService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _teamService = teamService;
        _teamMembershipRuleService = teamMembershipRuleService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewTeams)]
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
                item.Team.IsDefault,
                item.Team.IsDynamic,
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
    [Authorize(Policy = Policies.ViewTeams)]
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
            .Devices.AsNoTracking()
            .CountAsync(device => device.OwnerTeamId == team.Id || device.FallbackTeamId == team.Id, ct);
        var currentRiskScore = await _dbContext.TeamRiskScores.AsNoTracking()
            .Where(score => score.TeamId == team.Id)
            .Select(score => new
            {
                CurrentRiskScore = (decimal?)score.OverallScore,
                score.MaxAssetRiskScore,
                score.AssetCount,
                score.OpenEpisodeCount,
                score.CriticalEpisodeCount,
                score.HighEpisodeCount,
                score.MediumEpisodeCount,
                score.LowEpisodeCount,
                score.FactorsJson,
                score.CalculationVersion,
            })
            .FirstOrDefaultAsync(ct);
        var membershipRule = await _dbContext.TeamMembershipRules.AsNoTracking()
            .FirstOrDefaultAsync(rule => rule.TeamId == team.Id, ct);
        var topRiskAssets = await _dbContext.DeviceRiskScores.AsNoTracking()
            .Where(score => score.TenantId == team.TenantId)
            .Join(
                _dbContext.Devices.AsNoTracking()
                    .Where(device => device.OwnerTeamId == team.Id || device.FallbackTeamId == team.Id),
                score => score.DeviceId,
                device => device.Id,
                (score, device) => new
                {
                    device.Id,
                    AssetName = device.ComputerDnsName ?? device.Name,
                    AssetType = "Device",
                    CurrentRiskScore = score.OverallScore,
                    score.MaxEpisodeRiskScore,
                    score.OpenEpisodeCount
                }
            )
            .OrderByDescending(item => item.CurrentRiskScore)
            .ThenByDescending(item => item.OpenEpisodeCount)
            .Take(5)
            .Select(item => new TeamRiskAssetDto(
                item.Id,
                item.AssetName,
                item.AssetType,
                item.CurrentRiskScore,
                item.MaxEpisodeRiskScore,
                item.OpenEpisodeCount
            ))
            .ToListAsync(ct);

        return Ok(
            new TeamDetailDto(
                team.Id,
                team.TenantId,
                tenantName ?? "Unknown tenant",
                team.Name,
                team.IsDefault,
                team.IsDynamic,
                assignedAssetCount,
                currentRiskScore?.CurrentRiskScore,
                currentRiskScore is null
                    ? null
                    : ToRollupRiskExplanationDto(
                        currentRiskScore.CurrentRiskScore ?? 0m,
                        currentRiskScore.MaxAssetRiskScore,
                        currentRiskScore.AssetCount,
                        currentRiskScore.OpenEpisodeCount,
                        currentRiskScore.CriticalEpisodeCount,
                        currentRiskScore.HighEpisodeCount,
                        currentRiskScore.MediumEpisodeCount,
                        currentRiskScore.LowEpisodeCount,
                        currentRiskScore.FactorsJson,
                        currentRiskScore.CalculationVersion,
                        0.60m,
                        0.25m
                    ),
                topRiskAssets,
                team.Members.Select(m => new TeamMemberDto(
                        m.UserId,
                        users.TryGetValue(m.UserId, out var u) ? u.DisplayName : "Unknown",
                        users.TryGetValue(m.UserId, out var u2) ? u2.Email : "Unknown"
                    ))
                    .ToList(),
                membershipRule is null ? null : ToMembershipRuleDto(membershipRule)
            )
        );
    }

    private static RollupRiskExplanationDto ToRollupRiskExplanationDto(
        decimal overallScore,
        decimal maxAssetRiskScore,
        int assetCount,
        int openEpisodeCount,
        int criticalEpisodeCount,
        int highEpisodeCount,
        int mediumEpisodeCount,
        int lowEpisodeCount,
        string factorsJson,
        string calculationVersion,
        decimal maxWeight,
        decimal topThreeWeight
    )
    {
        var factors = ParseRiskFactors(factorsJson);
        var topThreeAverage = factors.FirstOrDefault(item => item.Name == "TopThreeAverage")?.Impact ?? 0m;
        var criticalContribution = factors.FirstOrDefault(item => item.Name == "CriticalEpisodes")?.Impact ?? 0m;
        var highContribution = factors.FirstOrDefault(item => item.Name == "HighEpisodes")?.Impact ?? 0m;
        var mediumContribution = factors.FirstOrDefault(item => item.Name == "MediumEpisodes")?.Impact ?? 0m;
        var lowContribution = factors.FirstOrDefault(item => item.Name == "LowEpisodes")?.Impact ?? 0m;

        return new RollupRiskExplanationDto(
            overallScore,
            calculationVersion,
            maxAssetRiskScore,
            topThreeAverage,
            Math.Round(maxWeight * maxAssetRiskScore, 2),
            Math.Round(topThreeWeight * topThreeAverage, 2),
            assetCount,
            openEpisodeCount,
            criticalEpisodeCount,
            highEpisodeCount,
            mediumEpisodeCount,
            lowEpisodeCount,
            criticalContribution,
            highContribution,
            mediumContribution,
            lowContribution,
            factors.Select(item => new RollupRiskExplanationFactorDto(
                item.Name,
                item.Description,
                item.Impact
            )).ToList()
        );
    }

    private static IReadOnlyList<ParsedRiskFactor> ParseRiskFactors(string factorsJson)
    {
        if (string.IsNullOrWhiteSpace(factorsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ParsedRiskFactor>>(factorsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed record ParsedRiskFactor(
        string Name,
        string Description,
        decimal Impact
    );

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
            new TeamDto(team.Id, team.TenantId, string.Empty, team.Name, team.IsDefault, team.IsDynamic, 0, null)
        );
    }

    [HttpPut("{id:guid}/name")]
    [Authorize(Policy = Policies.ManageTeams)]
    public async Task<IActionResult> Rename(
        Guid id,
        [FromBody] RenameTeamRequest request,
        CancellationToken ct
    )
    {
        var team = await _dbContext.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(team.TenantId))
            return Forbid();

        var result = await _teamService.RenameTeamAsync(id, request.Name, ct);
        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return NoContent();
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
        if (team.IsDynamic)
            return BadRequest(new ProblemDetails { Title = "Dynamic groups manage membership through rules and cannot be edited manually." });

        var userExists = await _dbContext.Users.AsNoTracking()
            .AnyAsync(user => user.Id == request.UserId, ct);
        if (!userExists)
            return BadRequest(new ProblemDetails { Title = "User not found" });

        var action = request.Action.ToLowerInvariant();
        if (action is not ("add" or "remove"))
            return BadRequest(
                new ProblemDetails { Title = "Invalid action. Use 'add' or 'remove'." }
            );

        var existingMembership = await _dbContext.TeamMembers
            .FirstOrDefaultAsync(member => member.TeamId == id && member.UserId == request.UserId, ct);

        if (action == "add")
        {
            if (existingMembership is null)
            {
                await _dbContext.TeamMembers.AddAsync(TeamMember.Create(id, request.UserId), ct);
                await _dbContext.SaveChangesAsync(ct);
            }

            return NoContent();
        }

        if (existingMembership is not null)
        {
            _dbContext.TeamMembers.Remove(existingMembership);
            await _dbContext.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    [HttpPut("{id:guid}/rule")]
    [Authorize(Policy = Policies.ManageTeams)]
    public async Task<ActionResult<TeamMembershipRuleDto>> UpsertRule(
        Guid id,
        [FromBody] UpdateTeamMembershipRuleRequest request,
        CancellationToken ct
    )
    {
        var team = await _dbContext.Teams
            .Include(item => item.Members)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
        if (team is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(team.TenantId))
            return Forbid();

        var filter = DeserializeFilter(request.FilterDefinition);
        if (filter is null)
            return BadRequest(new ProblemDetails { Title = "Invalid rule filter JSON." });

        var rule = await _dbContext.TeamMembershipRules.FirstOrDefaultAsync(item => item.TeamId == team.Id, ct);

        if (!request.IsDynamic)
        {
            team.SetDynamic(false);
            if (rule is not null)
            {
                _dbContext.TeamMembershipRules.Remove(rule);
            }

            await _dbContext.SaveChangesAsync(ct);
            return NoContent();
        }

        if (!team.IsDynamic && team.Members.Count > 0 && !request.AcknowledgeMemberReset)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Enabling dynamic membership will remove all current members before applying the rule."
            });
        }

        if (!team.IsDynamic && request.IsDynamic && team.Members.Count > 0)
        {
            foreach (var member in team.Members.ToList())
            {
                team.RemoveMember(member.UserId);
            }
        }

        team.SetDynamic(true);

        if (rule is null)
        {
            rule = TeamMembershipRule.Create(team.TenantId, team.Id, filter);
            await _dbContext.TeamMembershipRules.AddAsync(rule, ct);
        }
        rule.Update(filter, enabled: true);

        await _dbContext.SaveChangesAsync(ct);
        await _teamMembershipRuleService.ApplyAsync(rule, ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/rule/preview")]
    [Authorize(Policy = Policies.ViewTeams)]
    public async Task<ActionResult<TeamMembershipRulePreviewDto>> PreviewRule(
        Guid id,
        [FromBody] UpdateTeamMembershipRuleRequest request,
        CancellationToken ct
    )
    {
        var team = await _dbContext.Teams.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, ct);
        if (team is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(team.TenantId))
            return Forbid();

        var filter = DeserializeFilter(request.FilterDefinition);
        if (filter is null)
            return BadRequest(new ProblemDetails { Title = "Invalid rule filter JSON." });

        var preview = await _teamMembershipRuleService.PreviewAsync(team.TenantId, filter, ct);
        return Ok(new TeamMembershipRulePreviewDto(
            preview.Count,
            preview.Samples.Select(user => new TeamMembershipRulePreviewUserDto(
                user.Id,
                user.DisplayName,
                user.Email,
                user.Company
            )).ToList()
        ));
    }

    private static TeamMembershipRuleDto ToMembershipRuleDto(TeamMembershipRule rule) => new(
        rule.Id,
        JsonDocument.Parse(rule.FilterDefinition).RootElement,
        rule.CreatedAt,
        rule.UpdatedAt,
        rule.LastExecutedAt,
        rule.LastMatchCount
    );

    private static FilterNode? DeserializeFilter(JsonElement element)
    {
        try
        {
            return JsonSerializer.Deserialize<FilterNode>(
                element.GetRawText(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
            );
        }
        catch
        {
            return null;
        }
    }
}
