using FluentAssertions;
using PatchHound.Core.Services.OperationalContext;
using Xunit;

namespace PatchHound.Tests.Core.OperationalContext;

public class HeuristicPromptTokenEstimatorTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)] // ceil(5/4)
    public void Estimate_uses_ceil_chars_over_four(string text, int expected)
    {
        new HeuristicPromptTokenEstimator().Estimate(text).Should().Be(expected);
    }
}
