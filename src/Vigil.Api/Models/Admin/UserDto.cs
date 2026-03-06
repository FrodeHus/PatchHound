namespace Vigil.Api.Models.Admin;

public record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<UserRoleDto> Roles
);

public record UserRoleDto(Guid TenantId, string TenantName, string Role);

public record InviteUserRequest(string Email, string DisplayName, string EntraObjectId);

public record UpdateRolesRequest(IReadOnlyList<RoleAssignment> Roles);

public record RoleAssignment(Guid TenantId, string Role);
