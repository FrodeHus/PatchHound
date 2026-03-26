using PatchHound.Api.Models.Audit;

namespace PatchHound.Api.Models.Admin;

public record UserListItemDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Email,
    string DisplayName,
    string? Company,
    bool IsEnabled,
    IReadOnlyList<string> Roles,
    IReadOnlyList<UserTeamMembershipDto> Teams
);

public record UserDetailDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Email,
    string DisplayName,
    string? Company,
    bool IsEnabled,
    string EntraObjectId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<UserTeamMembershipDto> Teams,
    IReadOnlyList<UserAuditSummaryDto> RecentAudit
);

public record UserTeamMembershipDto(Guid TeamId, string TeamName, bool IsDefault);

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
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> TeamIds
);

public record UserAuditFilterRequest(
    string? EntityType = null,
    string? Action = null
);
