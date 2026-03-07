using Microsoft.AspNetCore.Authorization;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;

namespace PatchHound.Api.Auth;

public class RoleRequirementHandler : AuthorizationHandler<RoleRequirement>
{
    private readonly ITenantContext _tenantContext;

    public RoleRequirementHandler(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement
    )
    {
        var normalizedClaimRoles = EntraRoleNormalizer.Normalize(RoleClaimReader.ReadClaims(context.User));

        if (normalizedClaimRoles.Any(role => requirement.AllowedRoles.Contains(role)))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (_tenantContext.AccessibleTenantIds.Count == 0)
            return Task.CompletedTask;

        // If a current tenant is selected, check roles only for that tenant.
        // Otherwise, check if the user has the required role in any accessible tenant.
        if (_tenantContext.CurrentTenantId is Guid currentTenantId)
        {
            var tenantRoles = _tenantContext.GetRolesForTenant(currentTenantId);
            if (tenantRoles.Any(role =>
                Enum.TryParse<RoleName>(role, out var roleName)
                && requirement.AllowedRoles.Contains(roleName)))
            {
                context.Succeed(requirement);
            }
        }
        else
        {
            // No specific tenant selected — check all accessible tenants
            foreach (var tenantId in _tenantContext.AccessibleTenantIds)
            {
                var tenantRoles = _tenantContext.GetRolesForTenant(tenantId);
                if (tenantRoles.Any(role =>
                    Enum.TryParse<RoleName>(role, out var roleName)
                    && requirement.AllowedRoles.Contains(roleName)))
                {
                    context.Succeed(requirement);
                    break;
                }
            }
        }

        return Task.CompletedTask;
    }
}
