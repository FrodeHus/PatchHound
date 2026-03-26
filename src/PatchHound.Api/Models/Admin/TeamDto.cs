using PatchHound.Api.Models.RiskScore;
using System.Text.Json;

namespace PatchHound.Api.Models.Admin;

public record TeamDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Name,
    bool IsDefault,
    bool IsDynamic,
    int MemberCount,
    decimal? CurrentRiskScore
);

public record TeamDetailDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Name,
    bool IsDefault,
    bool IsDynamic,
    int AssignedAssetCount,
    decimal? CurrentRiskScore,
    RollupRiskExplanationDto? RiskExplanation,
    IReadOnlyList<TeamRiskAssetDto> TopRiskAssets,
    IReadOnlyList<TeamMemberDto> Members,
    TeamMembershipRuleDto? MembershipRule
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

public record TeamMembershipRuleDto(
    Guid Id,
    JsonElement FilterDefinition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastExecutedAt,
    int? LastMatchCount
);

public record TeamMembershipRulePreviewDto(int Count, IReadOnlyList<TeamMembershipRulePreviewUserDto> Samples);

public record TeamMembershipRulePreviewUserDto(Guid UserId, string DisplayName, string Email, string? Company);

public record CreateTeamRequest(string Name, Guid TenantId);

public record UpdateMembersRequest(Guid UserId, string Action);

public record UpdateTeamMembershipRuleRequest(bool IsDynamic, bool AcknowledgeMemberReset, JsonElement FilterDefinition);
