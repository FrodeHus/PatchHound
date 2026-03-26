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
    string LastError,
    Guid? ActiveIngestionRunId,
    DateTimeOffset? LeaseExpiresAt,
    string? ActiveSnapshotStatus,
    string? BuildingSnapshotStatus,
    string? ActivePhase,
    int? ActiveBatchNumber,
    string? ActiveCheckpointStatus,
    int? ActiveRecordsCommitted,
    DateTimeOffset? ActiveCheckpointCommittedAt,
    int? ActiveStagedMachineCount,
    int? ActiveStagedVulnerabilityCount,
    int? ActiveStagedSoftwareCount,
    int? ActivePersistedMachineCount,
    int? ActivePersistedVulnerabilityCount,
    int? ActivePersistedSoftwareCount
);

public record TenantIngestionRunDto(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int StagedMachineCount,
    int StagedVulnerabilityCount,
    int StagedSoftwareCount,
    int PersistedMachineCount,
    int DeactivatedMachineCount,
    int PersistedVulnerabilityCount,
    int PersistedSoftwareCount,
    string Error,
    string? SnapshotStatus,
    string? LatestPhase,
    int? LatestBatchNumber,
    string? LatestCheckpointStatus,
    int? LatestRecordsCommitted,
    DateTimeOffset? LastCheckpointCommittedAt
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
