using FluentAssertions;
using Vigil.Core.Common;

namespace Vigil.Tests.Core;

public class ResultTests
{
    [Fact]
    public void Success_ContainsValue()
    {
        var result = Result<int>.Success(42);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_ContainsError()
    {
        var result = Result<int>.Failure("not found");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("not found");
    }

    [Fact]
    public void AccessingValue_OnFailure_Throws()
    {
        var result = Result<int>.Failure("error");
        var act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AccessingError_OnSuccess_ReturnsNull()
    {
        var result = Result<int>.Success(42);
        result.Error.Should().BeNull();
    }
}
