using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Core.Services;

public class SlaService
{
    private static readonly IReadOnlyDictionary<Severity, int> DefaultSlaDays = new Dictionary<
        Severity,
        int
    >
    {
        { Severity.Critical, 7 },
        { Severity.High, 30 },
        { Severity.Medium, 90 },
        { Severity.Low, 180 },
    };

    public DateTimeOffset CalculateDueDate(
        Severity severity,
        DateTimeOffset createdAt,
        TenantSlaConfiguration? tenantSla = null
    )
    {
        var days = GetSlaDays(severity, tenantSla);
        return createdAt.AddDays(days);
    }

    public int GetSlaDays(Severity severity, TenantSlaConfiguration? tenantSla = null)
    {
        if (tenantSla is not null)
        {
            return severity switch
            {
                Severity.Critical => tenantSla.CriticalDays,
                Severity.High => tenantSla.HighDays,
                Severity.Medium => tenantSla.MediumDays,
                Severity.Low => tenantSla.LowDays,
                _ => DefaultSlaDays[Severity.Medium],
            };
        }

        return DefaultSlaDays.TryGetValue(severity, out var defaultDays) ? defaultDays : 90;
    }

    public SlaStatus GetSlaStatus(
        DateTimeOffset createdAt,
        DateTimeOffset dueDate,
        DateTimeOffset now
    )
    {
        if (now >= dueDate)
            return SlaStatus.Overdue;

        var totalDuration = dueDate - createdAt;
        var elapsed = now - createdAt;

        if (totalDuration.TotalSeconds <= 0)
            return SlaStatus.Overdue;

        var percentElapsed = elapsed.TotalSeconds / totalDuration.TotalSeconds;

        if (percentElapsed >= 0.75)
            return SlaStatus.NearDue;

        return SlaStatus.OnTrack;
    }
}

public enum SlaStatus
{
    OnTrack,
    NearDue,
    Overdue,
}
