using PatchHound.Core.Enums;

namespace PatchHound.Api.Auth;

public static class EntraRoleNormalizer
{
    private static readonly IReadOnlyDictionary<string, RoleName> RoleMap = new Dictionary<
        string,
        RoleName
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["Tenant.Admin"] = RoleName.GlobalAdmin,
        ["Tenant.SecurityManager"] = RoleName.SecurityManager,
        ["Tenant.SecurityAnalyst"] = RoleName.SecurityAnalyst,
        ["Tenant.AssetOwner"] = RoleName.AssetOwner,
        ["Tenant.Stakeholder"] = RoleName.Stakeholder,
        ["Tenant.Auditor"] = RoleName.Auditor,
    };

    public static IReadOnlyList<RoleName> Normalize(IEnumerable<string> rawRoles)
    {
        return rawRoles
            .Select(role =>
                RoleMap.TryGetValue(role, out var mappedRole) ? mappedRole : ParseFallback(role)
            )
            .Where(role => role.HasValue)
            .Select(role => role!.Value)
            .Distinct()
            .ToList();
    }

    private static RoleName? ParseFallback(string rawRole)
    {
        if (!Enum.TryParse<RoleName>(rawRole, out var parsedRole))
        {
            return null;
        }

        return parsedRole is RoleName.CustomerAdmin or RoleName.CustomerOperator or RoleName.CustomerViewer
            ? null
            : parsedRole;
    }
}
