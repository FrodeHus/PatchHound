using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core;

public class SoftwareAliasTests
{
    [Fact]
    public void Create_sets_fields_and_timestamp()
    {
        var productId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;

        var a = SoftwareAlias.Create(
            softwareProductId: productId,
            sourceSystemId: sourceId,
            externalId: "win10",
            observedVendor: "Microsoft",
            observedName: "Windows 10");

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.Equal(productId, a.SoftwareProductId);
        Assert.Equal(sourceId, a.SourceSystemId);
        Assert.Equal("win10", a.ExternalId);
        Assert.Equal("Microsoft", a.ObservedVendor);
        Assert.Equal("Windows 10", a.ObservedName);
        Assert.InRange(a.CreatedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void Create_trims_externalId()
    {
        var a = SoftwareAlias.Create(Guid.NewGuid(), Guid.NewGuid(), "  win10  ");
        Assert.Equal("win10", a.ExternalId);
    }

    [Fact]
    public void Create_rejects_empty_externalId()
    {
        Assert.Throws<ArgumentException>(() =>
            SoftwareAlias.Create(Guid.NewGuid(), Guid.NewGuid(), "   "));
    }

    [Fact]
    public void Create_rejects_empty_softwareProductId()
    {
        Assert.Throws<ArgumentException>(() =>
            SoftwareAlias.Create(Guid.Empty, Guid.NewGuid(), "win10"));
    }

    [Fact]
    public void Create_rejects_empty_sourceSystemId()
    {
        Assert.Throws<ArgumentException>(() =>
            SoftwareAlias.Create(Guid.NewGuid(), Guid.Empty, "win10"));
    }

    [Fact]
    public void Create_rejects_externalId_longer_than_256_chars()
    {
        var longId = new string('a', 257);
        Assert.Throws<ArgumentException>(() =>
            SoftwareAlias.Create(Guid.NewGuid(), Guid.NewGuid(), longId));
    }

    [Fact]
    public void Create_rejects_observedVendor_longer_than_256_chars()
    {
        var longVendor = new string('a', 257);
        Assert.Throws<ArgumentException>(() =>
            SoftwareAlias.Create(Guid.NewGuid(), Guid.NewGuid(), "win10", observedVendor: longVendor));
    }

    [Fact]
    public void Create_rejects_observedName_longer_than_512_chars()
    {
        var longName = new string('a', 513);
        Assert.Throws<ArgumentException>(() =>
            SoftwareAlias.Create(Guid.NewGuid(), Guid.NewGuid(), "win10", observedName: longName));
    }
}
