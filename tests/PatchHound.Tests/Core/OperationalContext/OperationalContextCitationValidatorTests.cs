using FluentAssertions;
using PatchHound.Core.Models.OperationalContext;
using PatchHound.Core.Services.OperationalContext;
using Xunit;

namespace PatchHound.Tests.Core.OperationalContext;

public class OperationalContextCitationValidatorTests
{
    private static IReadOnlyList<OperationalContextCitation> Pack() =>
    [
        new() { Key = "device-risk-top-1", EntityType = "Device", EntityId = Guid.NewGuid(), Label = "h1", Fact = "f1" },
        new() { Key = "device-risk-top-2", EntityType = "Device", EntityId = Guid.NewGuid(), Label = "h2", Fact = "f2" },
    ];

    [Fact]
    public void Keeps_only_keys_present_in_pack_preserving_pack_order()
    {
        var result = OperationalContextCitationValidator.Validate(
            modelKeys: ["device-risk-top-2", "device-risk-top-1", "bogus"], pack: Pack());

        result.Citations.Select(c => c.Key).Should().Equal("device-risk-top-1", "device-risk-top-2");
        result.Uncited.Should().BeFalse();
    }

    [Fact]
    public void Marks_uncited_when_no_valid_keys()
    {
        var result = OperationalContextCitationValidator.Validate(
            modelKeys: ["bogus", "also-bogus"], pack: Pack());

        result.Citations.Should().BeEmpty();
        result.Uncited.Should().BeTrue();
    }

    [Fact]
    public void Marks_uncited_when_model_returns_no_keys()
    {
        var result = OperationalContextCitationValidator.Validate(modelKeys: [], pack: Pack());

        result.Uncited.Should().BeTrue();
    }

    [Fact]
    public void Empty_pack_is_always_uncited()
    {
        var result = OperationalContextCitationValidator.Validate(
            modelKeys: ["device-risk-top-1"], pack: []);

        result.Citations.Should().BeEmpty();
        result.Uncited.Should().BeTrue();
    }
}
