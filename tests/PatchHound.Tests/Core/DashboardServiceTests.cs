using FluentAssertions;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;

namespace PatchHound.Tests.Core;

public class DashboardServiceTests
{
    // --- Exposure Score Tests ---

    [Fact]
    public void ExposureScore_NoPairs_ReturnsZero()
    {
        var pairs = Array.Empty<(Severity, Criticality)>();

        var score = DashboardService.CalculateExposureScore(pairs);

        score.Should().Be(0m);
    }

    [Fact]
    public void ExposureScore_AllCriticalCritical_Returns100()
    {
        var pairs = new (Severity, Criticality)[]
        {
            (Severity.Critical, Criticality.Critical),
            (Severity.Critical, Criticality.Critical),
        };

        var score = DashboardService.CalculateExposureScore(pairs);

        score.Should().Be(100m);
    }

    [Fact]
    public void ExposureScore_AllLowLow_ReturnsMinimal()
    {
        var pairs = new (Severity, Criticality)[]
        {
            (Severity.Low, Criticality.Low),
            (Severity.Low, Criticality.Low),
        };

        var score = DashboardService.CalculateExposureScore(pairs);

        // Each pair: 1 * 1 = 1, max per pair = 100, so 1/100 * 100 = 1.0
        score.Should().Be(1.0m);
    }

    [Fact]
    public void ExposureScore_MixedSeverities_CalculatesWeightedAverage()
    {
        var pairs = new (Severity, Criticality)[]
        {
            (Severity.Critical, Criticality.Critical), // 10 * 10 = 100
            (Severity.Low, Criticality.Low), // 1 * 1 = 1
        };

        var score = DashboardService.CalculateExposureScore(pairs);

        // Total raw = 101, max possible = 200, score = 101/200 * 100 = 50.5
        score.Should().Be(50.5m);
    }

    [Fact]
    public void ExposureScore_HighSeverityMediumCriticality()
    {
        var pairs = new (Severity, Criticality)[]
        {
            (Severity.High, Criticality.Medium), // 6 * 3 = 18
        };

        var score = DashboardService.CalculateExposureScore(pairs);

        // 18/100 * 100 = 18.0
        score.Should().Be(18.0m);
    }

    // --- SLA Compliance Tests ---

    [Fact]
    public void SlaCompliance_NoTasks_Returns100Percent()
    {
        var tasks = Array.Empty<(RemediationTaskStatus, DateTimeOffset)>();
        var now = DateTimeOffset.UtcNow;

        var (compliance, overdue) = DashboardService.CalculateSlaCompliance(tasks, now);

        compliance.Should().Be(100m);
        overdue.Should().Be(0);
    }

    [Fact]
    public void SlaCompliance_AllTasksWithinSla_Returns100Percent()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new (RemediationTaskStatus, DateTimeOffset)[]
        {
            (RemediationTaskStatus.Pending, now.AddDays(5)), // Due in future
            (RemediationTaskStatus.InProgress, now.AddDays(3)), // Due in future
            (RemediationTaskStatus.Completed, now.AddDays(-1)), // Completed, even though past due
        };

        var (compliance, overdue) = DashboardService.CalculateSlaCompliance(tasks, now);

        compliance.Should().Be(100m);
        overdue.Should().Be(0);
    }

    [Fact]
    public void SlaCompliance_SomeTasksOverdue_CalculatesCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new (RemediationTaskStatus, DateTimeOffset)[]
        {
            (RemediationTaskStatus.Pending, now.AddDays(-2)), // Overdue
            (RemediationTaskStatus.InProgress, now.AddDays(-1)), // Overdue
            (RemediationTaskStatus.Pending, now.AddDays(5)), // Within SLA
            (RemediationTaskStatus.Completed, now.AddDays(-3)), // Completed, not overdue
        };

        var (compliance, overdue) = DashboardService.CalculateSlaCompliance(tasks, now);

        overdue.Should().Be(2);
        compliance.Should().Be(50.0m); // 2/4 within SLA
    }

    [Fact]
    public void SlaCompliance_CompletedTasksPastDue_NotCountedAsOverdue()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new (RemediationTaskStatus, DateTimeOffset)[]
        {
            (RemediationTaskStatus.Completed, now.AddDays(-10)),
            (RemediationTaskStatus.RiskAccepted, now.AddDays(-5)),
        };

        var (compliance, overdue) = DashboardService.CalculateSlaCompliance(tasks, now);

        compliance.Should().Be(100m);
        overdue.Should().Be(0);
    }

    [Fact]
    public void SlaCompliance_AllOverdue_ReturnsZeroPercent()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new (RemediationTaskStatus, DateTimeOffset)[]
        {
            (RemediationTaskStatus.Pending, now.AddDays(-1)),
            (RemediationTaskStatus.InProgress, now.AddDays(-2)),
        };

        var (compliance, overdue) = DashboardService.CalculateSlaCompliance(tasks, now);

        compliance.Should().Be(0m);
        overdue.Should().Be(2);
    }

    // --- Average Remediation Days Tests ---

    [Fact]
    public void AverageRemediationDays_NoCompletedTasks_ReturnsZero()
    {
        var tasks = Array.Empty<(DateTimeOffset, DateTimeOffset)>();

        var avg = DashboardService.CalculateAverageRemediationDays(tasks);

        avg.Should().Be(0m);
    }

    [Fact]
    public void AverageRemediationDays_SingleTask_ReturnsExactDays()
    {
        var created = DateTimeOffset.UtcNow.AddDays(-10);
        var completed = DateTimeOffset.UtcNow;
        var tasks = new (DateTimeOffset, DateTimeOffset)[] { (created, completed) };

        var avg = DashboardService.CalculateAverageRemediationDays(tasks);

        avg.Should().BeApproximately(10.0m, 0.1m);
    }

    [Fact]
    public void AverageRemediationDays_MultipleTasks_ReturnsAverage()
    {
        var now = DateTimeOffset.UtcNow;
        var tasks = new (DateTimeOffset, DateTimeOffset)[]
        {
            (now.AddDays(-5), now), // 5 days
            (now.AddDays(-15), now), // 15 days
        };

        var avg = DashboardService.CalculateAverageRemediationDays(tasks);

        avg.Should().BeApproximately(10.0m, 0.1m);
    }
}
