using Microsoft.AspNetCore.Authorization;
using Vigil.Core.Enums;

namespace Vigil.Api.Auth;

public class RoleRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<RoleName> AllowedRoles { get; }

    public RoleRequirement(params RoleName[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }
}
