using Microsoft.AspNetCore.Authorization;
using PatchHound.Core.Enums;

namespace PatchHound.Api.Auth;

public class RoleRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<RoleName> AllowedRoles { get; }

    public RoleRequirement(params RoleName[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }
}
