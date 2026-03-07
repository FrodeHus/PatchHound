namespace PatchHound.Api.Models.Admin;

public record TenantListItemDto(
    Guid Id,
    string Name,
    string EntraTenantId,
    int ConfiguredIngestionSourceCount
);

public record TenantDetailDto(
    Guid Id,
    string Name,
    string EntraTenantId,
    TenantAssetSummaryDto Assets,
    IReadOnlyList<TenantIngestionSourceDto> IngestionSources
);

public record TenantAssetSummaryDto(
    int TotalCount,
    int DeviceCount,
    int SoftwareCount,
    int CloudResourceCount
);

public record TenantIngestionSourceDto(
    string Key,
    string DisplayName,
    bool Enabled,
    string SyncSchedule,
    bool SupportsScheduling,
    bool SupportsManualSync,
    TenantSourceCredentialsDto Credentials,
    TenantIngestionRuntimeDto Runtime
);

public record TenantSourceCredentialsDto(
    string TenantId,
    string ClientId,
    bool HasSecret,
    string ApiBaseUrl,
    string TokenScope
);

public record TenantIngestionRuntimeDto(
    DateTimeOffset? ManualRequestedAt,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset? LastSucceededAt,
    string LastStatus,
    string LastError
);

public record UpdateTenantRequest(string Name, List<UpdateTenantIngestionSourceRequest> IngestionSources);

public record UpdateTenantIngestionSourceRequest(
    string Key,
    string DisplayName,
    bool Enabled,
    string SyncSchedule,
    UpdateTenantSourceCredentialsRequest Credentials
);

public record UpdateTenantSourceCredentialsRequest(
    string TenantId,
    string ClientId,
    string Secret,
    string ApiBaseUrl,
    string TokenScope
);
