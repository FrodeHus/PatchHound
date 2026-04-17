using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.CloudApplications;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/cloud-applications")]
[Authorize]
public class CloudApplicationsController(
    PatchHoundDbContext dbContext,
    ITenantContext tenantContext
) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<CloudApplicationListItemDto>>> List(
        [FromQuery] CloudApplicationFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var now = DateTimeOffset.UtcNow;
        var soonThreshold = now.AddDays(30);

        var query = dbContext.CloudApplications
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == currentTenantId && a.ActiveInTenant);

        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(a => a.Name.Contains(filter.Search));

        if (filter.CredentialFilter == "expired")
            query = query.Where(a => a.Credentials.Any(c => c.ExpiresAt < now));
        else if (filter.CredentialFilter == "expiring-soon")
            query = query.Where(a => a.Credentials.Any(c => c.ExpiresAt >= now && c.ExpiresAt <= soonThreshold));

        var totalCount = await query.CountAsync(ct);

        var apps = await query
            .OrderBy(a => a.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Description,
                CredentialCount = a.Credentials.Count,
                ExpiredCount = a.Credentials.Count(c => c.ExpiresAt < now),
                ExpiringCount = a.Credentials.Count(c => c.ExpiresAt >= now && c.ExpiresAt <= soonThreshold),
                NextExpiryAt = a.Credentials
                    .Where(c => c.ExpiresAt >= now)
                    .OrderBy(c => c.ExpiresAt)
                    .Select(c => (DateTimeOffset?)c.ExpiresAt)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = apps.Select(a => new CloudApplicationListItemDto(
            a.Id,
            a.Name,
            a.Description,
            a.CredentialCount,
            a.ExpiredCount,
            a.ExpiringCount,
            a.NextExpiryAt
        )).ToList();

        return Ok(new PagedResponse<CloudApplicationListItemDto>(
            items,
            totalCount,
            pagination.Page,
            pagination.PageSize
        ));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<CloudApplicationDetailDto>> GetDetail(Guid id, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var app = await dbContext.CloudApplications
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.Id == id && a.TenantId == currentTenantId && a.ActiveInTenant)
            .Select(a => new
            {
                a.Id,
                a.ExternalId,
                a.AppId,
                a.Name,
                a.Description,
                a.IsFallbackPublicClient,
                a.RedirectUris,
                a.OwnerTeamId,
                OwnerTeamName = dbContext.Teams
                    .Where(t => t.Id == a.OwnerTeamId)
                    .Select(t => t.Name)
                    .FirstOrDefault(),
                Credentials = a.Credentials
                    .OrderBy(c => c.ExpiresAt)
                    .Select(c => new { c.Id, c.ExternalId, c.Type, c.DisplayName, c.ExpiresAt })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (app is null)
            return NotFound();

        var dto = new CloudApplicationDetailDto(
            app.Id,
            app.ExternalId,
            app.AppId,
            app.Name,
            app.Description,
            app.IsFallbackPublicClient,
            app.RedirectUris,
            app.OwnerTeamId,
            app.OwnerTeamName,
            app.Credentials.Select(c => new CloudApplicationCredentialDto(c.Id, c.ExternalId, c.Type, c.DisplayName, c.ExpiresAt)).ToList()
        );

        return Ok(dto);
    }

    [HttpPut("{id:guid}/owner")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<IActionResult> AssignOwner(Guid id, [FromBody] AssignCloudApplicationOwnerRequest request, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var app = await dbContext.CloudApplications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == currentTenantId, ct);

        if (app is null)
            return NotFound();

        if (request.TeamId.HasValue)
        {
            var teamExists = await dbContext.Teams
                .AnyAsync(t => t.Id == request.TeamId.Value && t.TenantId == currentTenantId, ct);
            if (!teamExists)
                return BadRequest(new ProblemDetails { Title = "Team not found." });
        }

        app.AssignOwnerTeam(request.TeamId);
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record CloudApplicationFilterQuery(
    string? Search = null,
    string? CredentialFilter = null
);
