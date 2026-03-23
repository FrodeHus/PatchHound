using PatchHound.Core.Enums;

namespace PatchHound.Core.Services;

public class DashboardService
{
    private static readonly Dictionary<Severity, int> SeverityWeights = new()
    {
        { Severity.Critical, 10 },
        { Severity.High, 6 },
        { Severity.Medium, 3 },
        { Severity.Low, 1 },
    };

    private static readonly Dictionary<Criticality, int> CriticalityWeights = new()
    {
        { Criticality.Critical, 10 },
        { Criticality.High, 6 },
        { Criticality.Medium, 3 },
        { Criticality.Low, 1 },
    };

    /// <summary>
    /// Calculates exposure score as the weighted sum of vulnerability severities multiplied by asset criticalities.
    /// Each vulnerability-asset pair contributes: SeverityWeight × CriticalityWeight.
    /// The result is normalized to a 0-100 scale based on the theoretical maximum.
    /// </summary>
    public static decimal CalculateExposureScore(
        IReadOnlyList<(Severity Severity, Criticality Criticality)> vulnerabilityAssetPairs
    )
    {
        if (vulnerabilityAssetPairs.Count == 0)
            return 0m;

        var rawScore = vulnerabilityAssetPairs.Sum(pair =>
            SeverityWeights[pair.Severity] * CriticalityWeights[pair.Criticality]
        );

        // Max possible per pair is 10 × 10 = 100
        var maxPossible = vulnerabilityAssetPairs.Count * 100m;
        return Math.Round(rawScore / maxPossible * 100m, 1);
    }

    /// <summary>
    /// Calculates the average number of days from first seen to resolution
    /// for resolved vulnerability episodes.
    /// </summary>
    public static decimal CalculateAverageRemediationDays(
        IReadOnlyList<(DateTimeOffset FirstSeenAt, DateTimeOffset ResolvedAt)> resolvedEpisodes
    )
    {
        if (resolvedEpisodes.Count == 0)
            return 0m;

        var totalDays = resolvedEpisodes.Sum(e => (e.ResolvedAt - e.FirstSeenAt).TotalDays);
        return Math.Round((decimal)(totalDays / resolvedEpisodes.Count), 1);
    }
}
