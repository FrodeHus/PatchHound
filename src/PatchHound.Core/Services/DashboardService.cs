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
    /// Calculates SLA compliance as the percentage of tasks that are not overdue.
    /// A task is overdue if its status is not Completed/RiskAccepted and its DueDate has passed.
    /// </summary>
    public static (decimal CompliancePercent, int OverdueCount) CalculateSlaCompliance(
        IReadOnlyList<(RemediationTaskStatus Status, DateTimeOffset DueDate)> tasks,
        DateTimeOffset now
    )
    {
        if (tasks.Count == 0)
            return (100m, 0);

        var terminalStatuses = new HashSet<RemediationTaskStatus>
        {
            RemediationTaskStatus.Completed,
            RemediationTaskStatus.RiskAccepted,
        };

        var overdueCount = tasks.Count(t =>
            !terminalStatuses.Contains(t.Status) && t.DueDate < now
        );

        var compliancePercent = Math.Round(
            (tasks.Count - overdueCount) / (decimal)tasks.Count * 100m,
            1
        );
        return (compliancePercent, overdueCount);
    }

    /// <summary>
    /// Calculates the average number of days to complete remediation tasks.
    /// Only considers tasks with Completed status.
    /// </summary>
    public static decimal CalculateAverageRemediationDays(
        IReadOnlyList<(DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)> completedTasks
    )
    {
        if (completedTasks.Count == 0)
            return 0m;

        var totalDays = completedTasks.Sum(t => (t.UpdatedAt - t.CreatedAt).TotalDays);
        return Math.Round((decimal)(totalDays / completedTasks.Count), 1);
    }
}
