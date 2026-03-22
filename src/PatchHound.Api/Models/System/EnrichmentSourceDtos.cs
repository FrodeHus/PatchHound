namespace PatchHound.Api.Models.System;

public record EnrichmentSourceDto(
    string Key,
    string DisplayName,
    bool Enabled,
    EnrichmentSourceCredentialsDto Credentials,
    int? RefreshTtlHours,
    EnrichmentSourceRuntimeDto Runtime,
    EnrichmentSourceQueueDto Queue,
    IReadOnlyList<EnrichmentRunDto> RecentRuns
);

public record EnrichmentSourceCredentialsDto(bool HasSecret, string ApiBaseUrl);

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
    UpdateEnrichmentSourceCredentialsRequest Credentials
);

public record UpdateEnrichmentSourceCredentialsRequest(string Secret, string ApiBaseUrl);
