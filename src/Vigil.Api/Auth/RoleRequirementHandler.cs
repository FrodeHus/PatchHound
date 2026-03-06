using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Data;

namespace Vigil.Api.Auth;

public class RoleRequirementHandler : AuthorizationHandler<RoleRequirement>
{
    private readonly VigilDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public RoleRequirementHandler(VigilDbContext dbContext, ITenantContext tenantContext)
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

        var userRoles = await _dbContext
            .UserTenantRoles.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(utr => utr.UserId == userId)
            .Select(utr => utr.Role)
            .Distinct()
            .ToListAsync();

        if (userRoles.Any(role => requirement.AllowedRoles.Contains(role)))
        {
            context.Succeed(requirement);
        }
    }
}
