using FluentAssertions;
using PatchHound.Core.Models.OperationalContext;
using PatchHound.Core.Services.OperationalContext;
using Xunit;

namespace PatchHound.Tests.Core.OperationalContext;

public class OperationalContextTruncatorTests
{
    private static OperationalContextPack BigPack()
    {
        var pack = new OperationalContextPack
        {
            ContextKind = "RemediationCase",
            Scope = new()
            {
                OpenExposureCount = 100,
                CriticalityDistribution = new() { ["Critical"] = 3, ["High"] = 8 },
            },
            Workflow = new() { Status = "Open", CurrentStage = "Approval" },
        };
        for (var i = 0; i < 50; i++)
        {
            pack.Citations.Add(new OperationalContextCitation
            {
                Key = $"device-risk-top-{i}", EntityType = "Device", EntityId = Guid.NewGuid(),
                Label = $"host-{i}", Fact = new string('x', 200), RiskWeight = i,
            });
        }
        return pack;
    }

    [Fact]
    public void Trims_detail_and_marks_truncated_when_over_budget()
    {
        var pack = BigPack();
        var truncator = new OperationalContextTruncator(new HeuristicPromptTokenEstimator());

        var (result, tokens) = truncator.Fit(pack, maxTokens: 50);

        result.Limits.Truncated.Should().BeTrue();
        // Aggregates are never dropped — token count reflects best-effort trim
        result.Scope.OpenExposureCount.Should().Be(100); // aggregates preserved
        result.Scope.CriticalityDistribution.Should().ContainKey("Critical");
        // All detail (workflow + citations) trimmed; token count is minimized
        result.Workflow.Should().BeNull();
        result.Citations.Should().BeEmpty();
    }

    [Fact]
    public void Keeps_everything_and_marks_not_truncated_when_within_budget()
    {
        var pack = new OperationalContextPack { ContextKind = "X" };
        var truncator = new OperationalContextTruncator(new HeuristicPromptTokenEstimator());

        var (result, _) = truncator.Fit(pack, maxTokens: 100000);

        result.Limits.Truncated.Should().BeFalse();
    }

    [Fact]
    public void Truncates_to_within_budget_and_marks_truncated_for_realistic_pack()
    {
        // Build a pack with workflow + a handful of citations that exceeds a tight budget,
        // but is small enough that dropping citations brings it within budget.
        var pack = new OperationalContextPack
        {
            ContextKind = "RemediationCase",
            Scope = new()
            {
                OpenExposureCount = 42,
                CriticalityDistribution = new() { ["Critical"] = 2, ["High"] = 5 },
                TopBusinessLabels = [new() { Name = "Finance", DeviceCount = 10 }],
                TopOwnerTeams = [new() { Name = "TeamA", DeviceCount = 4 }],
            },
            Workflow = new() { Status = "Open", CurrentStage = "Approval" },
        };
        for (var i = 0; i < 6; i++)
        {
            pack.Citations.Add(new OperationalContextCitation
            {
                Key = $"device-risk-top-{i}", EntityType = "Device", EntityId = Guid.NewGuid(),
                Label = $"host-{i}", Fact = new string('y', 80), RiskWeight = i,
            });
        }

        var estimator = new HeuristicPromptTokenEstimator();
        var truncator = new OperationalContextTruncator(estimator);

        // Measure the full pack first, then set a budget just below it
        var json = System.Text.Json.JsonSerializer.Serialize(pack, OperationalContextPack.SerializerOptions);
        var fullTokens = estimator.Estimate(json);
        var budget = fullTokens - 50; // tight enough to force truncation

        var (result, tokenEstimate) = truncator.Fit(pack, maxTokens: budget);

        result.Limits.Truncated.Should().BeTrue();
        tokenEstimate.Should().BeLessThanOrEqualTo(budget);
    }
}
