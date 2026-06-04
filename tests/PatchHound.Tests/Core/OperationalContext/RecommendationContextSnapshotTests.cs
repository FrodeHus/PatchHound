using FluentAssertions;
using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core.OperationalContext;

public class RecommendationContextSnapshotTests
{
    private static RecommendationContextSnapshot New(string hash = "abc") =>
        RecommendationContextSnapshot.Create(
            tenantId: Guid.NewGuid(), remediationCaseId: Guid.NewGuid(),
            contextJson: "{\"contextKind\":\"RemediationCase\"}", contextHash: hash,
            citationsJson: "[]", generatedBy: Guid.NewGuid());

    [Fact]
    public void Create_sets_fields_and_timestamp()
    {
        var s = New();
        s.Id.Should().NotBeEmpty();
        s.ContextJson.Should().Contain("RemediationCase");
        s.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_rejects_hash_over_64_chars()
    {
        var act = () => New(hash: new string('a', 65));
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Create_rejects_empty_tenant(string tenant)
    {
        var act = () => RecommendationContextSnapshot.Create(
            Guid.Parse(tenant), Guid.NewGuid(), "{}", "h", "[]", Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }
}
