using PatchHound.Api.Models.Audit;

namespace PatchHound.Api.Models.Admin;

public record UserListItemDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Company,
    bool IsEnabled,
    string AccessScope,
    IReadOnlyList<string> Roles,
    IReadOnlyList<UserTeamMembershipDto> Teams,
    IReadOnlyList<string> TenantNames
);

public record UserDetailDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Company,
    bool IsEnabled,
    string EntraObjectId,
    string AccessScope,
    Guid? CurrentTenantId,
    string? CurrentTenantName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<UserTeamMembershipDto> Teams,
    IReadOnlyList<UserAuditSummaryDto> RecentAudit,
    IReadOnlyList<UserTenantAccessDto> TenantAccess
);

public record UserTeamMembershipDto(Guid TeamId, string TeamName, bool IsDefault);
public record UserTenantAccessDto(Guid TenantId, string TenantName, IReadOnlyList<string> Roles);

public record UserAuditSummaryDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Action,
    string? Summary,
    string? UserDisplayName,
    DateTimeOffset Timestamp
);

public record InviteUserRequest(string Email, string DisplayName, string EntraObjectId);

public record UpdateUserRequest(
    string DisplayName,
    string Email,
    string? Company,
    bool IsEnabled,
    string AccessScope,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> TeamIds,
    IReadOnlyList<UserTenantAccessUpdateDto> TenantAccess
);

public record UserTenantAccessUpdateDto(Guid TenantId, IReadOnlyList<string> Roles);

public record UserAuditFilterRequest(
    string? EntityType = null,
    string? Action = null
);
