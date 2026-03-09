using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Infrastructure.Services;

public interface IEnrichmentSourceRunner
{
    string SourceKey { get; }
    EnrichmentTargetModel TargetModel { get; }
    TimeSpan MinimumDelay { get; }

    Task<EnrichmentJobExecutionResult> ExecuteAsync(EnrichmentJob job, CancellationToken ct);
}

public enum EnrichmentJobExecutionOutcome
{
    Succeeded,
    NoData,
    Retry,
    Failed,
}

public sealed record EnrichmentJobExecutionResult(
    EnrichmentJobExecutionOutcome Outcome,
    string? Error = null,
    DateTimeOffset? NextAttemptAt = null
);
