using FluentAssertions;
using PatchHound.Core.Entities;

namespace PatchHound.Tests.Core;

public class AssetTagTests
{
    [Fact]
    public void Create_SetsAllFields()
    {
        var tenantId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        var tag = AssetTag.Create(tenantId, assetId, "production", "Defender");

        tag.Id.Should().NotBeEmpty();
        tag.TenantId.Should().Be(tenantId);
        tag.AssetId.Should().Be(assetId);
        tag.Tag.Should().Be("production");
        tag.Source.Should().Be("Defender");
        tag.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
