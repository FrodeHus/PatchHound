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
}
