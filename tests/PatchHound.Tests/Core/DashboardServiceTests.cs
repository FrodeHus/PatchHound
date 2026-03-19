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

    [Theory]
    [MemberData(nameof(ExposureScoreCases))]
    public void ExposureScore_CalculatesWeightedAverage(
        (Severity, Criticality)[] pairs,
        decimal expected
    )
    {
        var score = DashboardService.CalculateExposureScore(pairs);

        score.Should().Be(expected);
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

    [Theory]
    [MemberData(nameof(SlaComplianceCases))]
    public void SlaCompliance_CalculatesExpectedValues(
        (RemediationTaskStatus, DateTimeOffset)[] tasks,
        DateTimeOffset now,
        decimal expectedCompliance,
        int expectedOverdue
    )
    {
        var (compliance, overdue) = DashboardService.CalculateSlaCompliance(tasks, now);

        compliance.Should().Be(expectedCompliance);
        overdue.Should().Be(expectedOverdue);
    }

    // --- Average Remediation Days Tests ---

    [Fact]
    public void AverageRemediationDays_NoResolvedEpisodes_ReturnsZero()
    {
        var episodes = Array.Empty<(DateTimeOffset, DateTimeOffset)>();

        var avg = DashboardService.CalculateAverageRemediationDays(episodes);

        avg.Should().Be(0m);
    }

    [Theory]
    [MemberData(nameof(AverageRemediationDayCases))]
    public void AverageRemediationDays_ReturnsExpectedAverage(
        (DateTimeOffset, DateTimeOffset)[] episodes,
        decimal expected
    )
    {
        var avg = DashboardService.CalculateAverageRemediationDays(episodes);

        avg.Should().BeApproximately(expected, 0.1m);
    }

    public static IEnumerable<object[]> ExposureScoreCases()
    {
        yield return
        [
            new (Severity, Criticality)[]
            {
                (Severity.Critical, Criticality.Critical),
                (Severity.Critical, Criticality.Critical),
            },
            100m,
        ];
        yield return
        [
            new (Severity, Criticality)[]
            {
                (Severity.Low, Criticality.Low),
                (Severity.Low, Criticality.Low),
            },
            1.0m,
        ];
        yield return
        [
            new (Severity, Criticality)[]
            {
                (Severity.Critical, Criticality.Critical),
                (Severity.Low, Criticality.Low),
            },
            50.5m,
        ];
        yield return
        [
            new (Severity, Criticality)[]
            {
                (Severity.High, Criticality.Medium),
            },
            18.0m,
        ];
    }

    public static IEnumerable<object[]> SlaComplianceCases()
    {
        var now = DateTimeOffset.UtcNow;
        yield return
        [
            new (RemediationTaskStatus, DateTimeOffset)[]
            {
                (RemediationTaskStatus.Pending, now.AddDays(5)),
                (RemediationTaskStatus.InProgress, now.AddDays(3)),
                (RemediationTaskStatus.Completed, now.AddDays(-1)),
            },
            now,
            100m,
            0,
        ];
        yield return
        [
            new (RemediationTaskStatus, DateTimeOffset)[]
            {
                (RemediationTaskStatus.Pending, now.AddDays(-2)),
                (RemediationTaskStatus.InProgress, now.AddDays(-1)),
                (RemediationTaskStatus.Pending, now.AddDays(5)),
                (RemediationTaskStatus.Completed, now.AddDays(-3)),
            },
            now,
            50.0m,
            2,
        ];
        yield return
        [
            new (RemediationTaskStatus, DateTimeOffset)[]
            {
                (RemediationTaskStatus.Completed, now.AddDays(-10)),
                (RemediationTaskStatus.RiskAccepted, now.AddDays(-5)),
            },
            now,
            100m,
            0,
        ];
        yield return
        [
            new (RemediationTaskStatus, DateTimeOffset)[]
            {
                (RemediationTaskStatus.Pending, now.AddDays(-1)),
                (RemediationTaskStatus.InProgress, now.AddDays(-2)),
            },
            now,
            0m,
            2,
        ];
    }

    public static IEnumerable<object[]> AverageRemediationDayCases()
    {
        var now = DateTimeOffset.UtcNow;
        yield return
        [
            new (DateTimeOffset, DateTimeOffset)[]
            {
                (now.AddDays(-10), now),
            },
            10.0m,
        ];
        yield return
        [
            new (DateTimeOffset, DateTimeOffset)[]
            {
                (now.AddDays(-5), now),
                (now.AddDays(-15), now),
            },
            10.0m,
        ];
    }
}
