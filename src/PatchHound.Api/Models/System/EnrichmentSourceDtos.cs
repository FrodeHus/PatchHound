namespace PatchHound.Api.Models.System;

public record EnrichmentSourceDto(
    string Key,
    string DisplayName,
    bool Enabled,
    IReadOnlyList<string> Targets,
    EnrichmentSourceCredentialsDto Credentials,
    string CredentialMode,
    int? RefreshTtlHours,
    EnrichmentSourceOptionsDto Options,
    EnrichmentSourceRuntimeDto Runtime,
    EnrichmentSourceQueueDto Queue,
    IReadOnlyList<EnrichmentRunDto> RecentRuns
);

public record EnrichmentSourceCredentialsDto(
    Guid? StoredCredentialId,
    IReadOnlyList<string> AcceptedCredentialTypes,
    bool HasSecret,
    string ApiBaseUrl
);

public record EnrichmentSourceRuntimeDto(
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset? LastSucceededAt,
    string LastStatus,
    string LastError
);

public record EnrichmentSourceQueueDto(
    int PendingCount,
    int RetryScheduledCount,
    int RunningCount,
    int FailedCount,
    DateTimeOffset? OldestPendingAt
);

public record EnrichmentRunDto(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int JobsClaimed,
    int JobsSucceeded,
    int JobsNoData,
    int JobsFailed,
    int JobsRetried,
    string LastError
);

public record UpdateEnrichmentSourceRequest(
    string Key,
    string DisplayName,
    bool Enabled,
    int? RefreshTtlHours,
    UpdateEnrichmentSourceCredentialsRequest Credentials,
    IReadOnlyList<string>? Targets = null,
    EnrichmentSourceOptionsDto? Options = null
);

public record UpdateEnrichmentSourceCredentialsRequest(
    Guid? StoredCredentialId,
    string Secret,
    string ApiBaseUrl
);

public record EnrichmentSourceOptionsDto(
    JinaReaderOptionsDto? JinaReader = null
);

public record JinaReaderOptionsDto(
    int TimeoutSeconds,
    int MaxContentChars,
    string ResponseFormat,
    bool UseReaderLmV2,
    bool RemoveImages,
    bool IncludeLinkSummary,
    bool IncludeImageSummary,
    string TargetSelector,
    string ExcludeSelector,
    string WaitForSelector
);

public record TriggerNvdFullSyncRequest(int FromYear, int ToYear);
