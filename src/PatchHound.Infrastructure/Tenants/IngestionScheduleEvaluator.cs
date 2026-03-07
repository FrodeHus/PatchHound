using Cronos;

namespace PatchHound.Infrastructure.Tenants;

public static class IngestionScheduleEvaluator
{
    public static bool IsDue(PersistedIngestionSource source, DateTimeOffset nowUtc)
    {
        if (!source.Enabled || !TenantSourceSettings.HasConfiguredCredentials(source.Credentials))
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

        var lastStartedAt = source.Runtime?.LastStartedAt?.ToUniversalTime();
        var lastCompletedAt = source.Runtime?.LastCompletedAt?.ToUniversalTime();
        if (lastStartedAt.HasValue && (!lastCompletedAt.HasValue || lastCompletedAt < lastStartedAt))
        {
            return false;
        }

        var referenceTime = lastStartedAt?.UtcDateTime ?? nowUtc.UtcDateTime.AddYears(-1);
        var nextOccurrence = expression.GetNextOccurrence(
            referenceTime,
            !lastStartedAt.HasValue
        );

        if (!nextOccurrence.HasValue)
        {
            return false;
        }

        return nextOccurrence.Value <= nowUtc.UtcDateTime;
    }
}
