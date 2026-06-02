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
}
