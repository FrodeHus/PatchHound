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
}

public record CloudApplicationFilterQuery(
    string? Search = null,
    string? CredentialFilter = null
);
