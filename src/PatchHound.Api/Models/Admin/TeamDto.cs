namespace PatchHound.Api.Models.Admin;

public record TeamDto(Guid Id, Guid TenantId, string TenantName, string Name, int MemberCount);

public record TeamDetailDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Name,
    int AssignedAssetCount,
    IReadOnlyList<TeamMemberDto> Members
);

public record TeamMemberDto(Guid UserId, string DisplayName, string Email);

public record CreateTeamRequest(string Name, Guid TenantId);

public record UpdateMembersRequest(Guid UserId, string Action);
