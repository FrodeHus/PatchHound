using PatchHound.Api.Models.RiskScore;

namespace PatchHound.Api.Models.Admin;

public record TeamDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Name,
    bool IsDefault,
    int MemberCount,
    decimal? CurrentRiskScore
);

public record TeamDetailDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Name,
    bool IsDefault,
    int AssignedAssetCount,
    decimal? CurrentRiskScore,
    RollupRiskExplanationDto? RiskExplanation,
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
