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
    IReadOnlyList<TenantIngestionSourceDto> IngestionSources
);

public record TenantIngestionSourceDto(
    string Key,
    string DisplayName,
    bool Enabled,
    string SyncSchedule,
    TenantSourceCredentialsDto Credentials
);

public record TenantSourceCredentialsDto(
    string TenantId,
    string ClientId,
    bool HasClientSecret,
    string ApiBaseUrl,
    string TokenScope
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
    string ClientSecret,
    string ApiBaseUrl,
    string TokenScope
);

public record UpdateTenantSettingsRequest(string Settings);
