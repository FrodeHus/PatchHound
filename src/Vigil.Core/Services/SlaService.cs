using System.Text.Json;
using Vigil.Core.Enums;

namespace Vigil.Core.Services;

public class SlaService
{
    private static readonly IReadOnlyDictionary<Severity, int> DefaultSlaDays =
        new Dictionary<Severity, int>
        {
            { Severity.Critical, 7 },
            { Severity.High, 30 },
            { Severity.Medium, 90 },
            { Severity.Low, 180 },
        };

    public DateTimeOffset CalculateDueDate(
        Severity severity,
        DateTimeOffset createdAt,
        string? tenantSettings = null)
    {
        var days = GetSlaDays(severity, tenantSettings);
        return createdAt.AddDays(days);
    }

    public int GetSlaDays(Severity severity, string? tenantSettings = null)
    {
        if (!string.IsNullOrWhiteSpace(tenantSettings))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<TenantSlaSettings>(tenantSettings);
                if (settings?.SlaDays is not null && settings.SlaDays.TryGetValue(severity, out var overrideDays))
                {
                    return overrideDays;
                }
            }
            catch (JsonException)
            {
                // Fall through to defaults if JSON is invalid
            }
        }

        return DefaultSlaDays.TryGetValue(severity, out var defaultDays) ? defaultDays : 90;
    }

    public SlaStatus GetSlaStatus(
        DateTimeOffset createdAt,
        DateTimeOffset dueDate,
        DateTimeOffset now)
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

internal class TenantSlaSettings
{
    public Dictionary<Severity, int>? SlaDays { get; set; }
}
