using System.Security.Claims;

namespace PatchHound.Api.Auth;

public static class RoleClaimReader
{
    private static readonly string[] SupportedClaimTypes =
    [
        "roles",
        "role",
        ClaimTypes.Role,
    ];

    public static IReadOnlyList<string> ReadClaims(ClaimsPrincipal principal)
    {
        return principal.Claims
            .Where(claim => SupportedClaimTypes.Contains(claim.Type, StringComparer.OrdinalIgnoreCase))
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
