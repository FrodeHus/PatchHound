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
    TenantSlaConfigurationDto Sla,
    IReadOnlyList<TenantIngestionSourceDto> IngestionSources
);

public record TenantAssetSummaryDto(
    int TotalCount,
    int DeviceCount,
    int SoftwareCount,
    int CloudResourceCount
);

public record TenantSlaConfigurationDto(
    int CriticalDays,
    int HighDays,
    int MediumDays,
    int LowDays
);

public record TenantIngestionSourceDto(
    string Key,
    string DisplayName,
    bool Enabled,
    string SyncSchedule,
    bool SupportsScheduling,
    bool SupportsManualSync,
    TenantSourceCredentialsDto Credentials,
    TenantIngestionRuntimeDto Runtime,
    IReadOnlyList<TenantIngestionRunDto> RecentRuns
);

public record TenantSourceCredentialsDto(
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

public record TenantIngestionRunDto(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int FetchedVulnerabilityCount,
    int FetchedAssetCount,
    int FetchedSoftwareInstallationCount,
    int StagedVulnerabilityCount,
    int StagedExposureCount,
    int MergedExposureCount,
    int OpenedProjectionCount,
    int ResolvedProjectionCount,
    int StagedAssetCount,
    int MergedAssetCount,
    int StagedSoftwareLinkCount,
    int ResolvedSoftwareLinkCount,
    int InstallationsCreated,
    int InstallationsTouched,
    int InstallationEpisodesOpened,
    int InstallationEpisodesSeen,
    int StaleInstallationsMarked,
    int InstallationsRemoved,
    string Error
);

public record UpdateTenantRequest(
    string Name,
    UpdateTenantSlaConfigurationRequest Sla,
    List<UpdateTenantIngestionSourceRequest> IngestionSources
);

public record CreateTenantRequest(string Name, string EntraTenantId);

public record UpdateTenantSlaConfigurationRequest(
    int CriticalDays,
    int HighDays,
    int MediumDays,
    int LowDays
);

public record UpdateTenantIngestionSourceRequest(
    string Key,
    string DisplayName,
    bool Enabled,
    string SyncSchedule,
    UpdateTenantSourceCredentialsRequest Credentials
);

public record UpdateTenantSourceCredentialsRequest(
    string ClientId,
    string Secret,
    string ApiBaseUrl,
    string TokenScope
);
