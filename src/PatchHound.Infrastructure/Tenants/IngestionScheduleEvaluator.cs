using Cronos;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Tenants;

public static class IngestionScheduleEvaluator
{
    public static bool IsDue(TenantSourceConfiguration source, DateTimeOffset nowUtc)
    {
        if (
            !source.Enabled
            || !TenantSourceCatalog.SupportsScheduling(source)
            || !TenantSourceCatalog.HasConfiguredCredentials(source)
        )
        {
            return false;
        }

        CronExpression expression;
        try
        {
            expression = CronExpression.Parse(source.SyncSchedule, CronFormat.Standard);
        }
        catch (CronFormatException)
        {
            return false;
        }

        var lastStartedAt = source.LastStartedAt?.ToUniversalTime();
        var lastCompletedAt = source.LastCompletedAt?.ToUniversalTime();
        if (
            lastStartedAt.HasValue && (!lastCompletedAt.HasValue || lastCompletedAt < lastStartedAt)
        )
        {
            return false;
        }

        var referenceTime = lastStartedAt?.UtcDateTime ?? nowUtc.UtcDateTime.AddYears(-1);
        var nextOccurrence = expression.GetNextOccurrence(referenceTime, !lastStartedAt.HasValue);

        if (!nextOccurrence.HasValue)
        {
            return false;
        }

        return nextOccurrence.Value <= nowUtc.UtcDateTime;
    }

    /// <summary>
    /// Primitive-field overload for callers (e.g. <c>IngestionWorker</c>) that hold
    /// denormalised schedule data rather than a full <see cref="TenantSourceConfiguration"/>.
    /// Includes the active-run guard: returns <see langword="false"/> when
    /// <paramref name="lastStartedAt"/> is later than <paramref name="lastCompletedAt"/>,
    /// indicating a run is currently in progress.
    /// </summary>
    public static bool IsDue(
        string sourceKey,
        bool enabled,
        string? syncSchedule,
        DateTimeOffset? lastStartedAt,
        DateTimeOffset? lastCompletedAt,
        DateTimeOffset nowUtc)
    {
        if (!enabled || string.IsNullOrWhiteSpace(syncSchedule))
        {
            return false;
        }

        CronExpression expression;
        try
        {
            expression = CronExpression.Parse(syncSchedule, CronFormat.Standard);
        }
        catch (CronFormatException)
        {
            return false;
        }

        var lastStarted = lastStartedAt?.ToUniversalTime();
        var lastCompleted = lastCompletedAt?.ToUniversalTime();

        // Active-run guard: a run is in progress when lastStartedAt > lastCompletedAt
        if (lastStarted.HasValue && (!lastCompleted.HasValue || lastCompleted < lastStarted))
        {
            return false;
        }

        var referenceTime = lastStarted?.UtcDateTime ?? nowUtc.UtcDateTime.AddYears(-1);
        var nextOccurrence = expression.GetNextOccurrence(referenceTime, !lastStarted.HasValue);

        if (!nextOccurrence.HasValue)
        {
            return false;
        }

        return nextOccurrence.Value <= nowUtc.UtcDateTime;
    }
}
