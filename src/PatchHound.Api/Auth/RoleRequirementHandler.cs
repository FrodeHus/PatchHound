using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Auth;

public class RoleRequirementHandler : AuthorizationHandler<RoleRequirement>
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public RoleRequirementHandler(PatchHoundDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement
    )
    {
        var userId = _tenantContext.CurrentUserId;
        if (userId == Guid.Empty)
            return;

        var accessibleTenantIds = _tenantContext.AccessibleTenantIds;
        if (accessibleTenantIds.Count == 0)
            return;

        var userRoles = await _dbContext
            .UserTenantRoles.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(utr => utr.UserId == userId && accessibleTenantIds.Contains(utr.TenantId))
            .Select(utr => utr.Role)
            .Distinct()
            .ToListAsync();

        if (userRoles.Any(role => requirement.AllowedRoles.Contains(role)))
        {
            context.Succeed(requirement);
        }
    }
}
