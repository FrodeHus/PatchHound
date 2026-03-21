namespace PatchHound.Api.Models.Admin;

public record TeamDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Name,
    int MemberCount,
    decimal? CurrentRiskScore
);

public record TeamDetailDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Name,
    int AssignedAssetCount,
    decimal? CurrentRiskScore,
    IReadOnlyList<TeamRiskAssetDto> TopRiskAssets,
    IReadOnlyList<TeamMemberDto> Members
);

public record TeamRiskAssetDto(
    Guid AssetId,
    string AssetName,
    string AssetType,
    decimal CurrentRiskScore,
    decimal MaxEpisodeRiskScore,
    int OpenEpisodeCount
);

public record TeamMemberDto(Guid UserId, string DisplayName, string Email);

public record CreateTeamRequest(string Name, Guid TenantId);

public record UpdateMembersRequest(Guid UserId, string Action);
