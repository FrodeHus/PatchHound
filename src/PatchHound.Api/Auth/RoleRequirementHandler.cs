using Microsoft.AspNetCore.Authorization;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;

namespace PatchHound.Api.Auth;

public class RoleRequirementHandler : AuthorizationHandler<RoleRequirement>
{
    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RoleRequirementHandler(ITenantContext tenantContext, IHttpContextAccessor httpContextAccessor)
    {
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement
    )
    {
        // All roles — including Entra claim roles — must be explicitly activated.
        // Per spec: "GlobalAdmin follows the same activation pattern — no special treatment."

        if (_tenantContext.AccessibleTenantIds.Count == 0)
            return Task.CompletedTask;

        // Read active roles from header
        var activeRolesHeader = _httpContextAccessor.HttpContext?
            .Request.Headers["X-Active-Roles"].FirstOrDefault();

        var headerRoles = string.IsNullOrWhiteSpace(activeRolesHeader)
            ? Array.Empty<string>()
            : activeRolesHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Check against current tenant or all accessible tenants
        if (_tenantContext.CurrentTenantId is Guid currentTenantId)
        {
            var assignedRoles = _tenantContext.GetRolesForTenant(currentTenantId);
            var effectiveRoles = BuildEffectiveRoles(headerRoles, assignedRoles);

            if (effectiveRoles.Any(role => requirement.AllowedRoles.Contains(role)))
            {
                context.Succeed(requirement);
            }
        }
        else
        {
            foreach (var tenantId in _tenantContext.AccessibleTenantIds)
            {
                var assignedRoles = _tenantContext.GetRolesForTenant(tenantId);
                var effectiveRoles = BuildEffectiveRoles(headerRoles, assignedRoles);

                if (effectiveRoles.Any(role => requirement.AllowedRoles.Contains(role)))
                {
                    context.Succeed(requirement);
                    break;
                }
            }
        }

        return Task.CompletedTask;
    }

    private static List<RoleName> BuildEffectiveRoles(
        string[] headerRoles,
        IReadOnlyList<string> assignedRoles)
    {
        var effective = new List<RoleName> { RoleName.Stakeholder };

        foreach (var headerRole in headerRoles)
        {
            if (Enum.TryParse<RoleName>(headerRole, out var roleName)
                && roleName != RoleName.Stakeholder
                && assignedRoles.Contains(headerRole, StringComparer.OrdinalIgnoreCase))
            {
                effective.Add(roleName);
            }
        }

        return effective;
    }
}
