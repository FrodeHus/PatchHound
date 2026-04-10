using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core;

public class SoftwareProductTests
{
    [Fact]
    public void Create_computes_canonical_key_from_vendor_and_name()
    {
        var p = SoftwareProduct.Create(vendor: "Microsoft", name: "Edge", primaryCpe23Uri: "cpe:2.3:a:microsoft:edge:*:*:*:*:*:*:*:*");
        Assert.Equal("microsoft::edge", p.CanonicalProductKey);
        Assert.Equal("Microsoft", p.Vendor);
        Assert.Equal("Edge", p.Name);
        Assert.NotEqual(Guid.Empty, p.Id);
    }

    [Fact]
    public void Create_trims_and_lowercases_canonical_key()
    {
        var p = SoftwareProduct.Create(vendor: "  MICROSOFT  ", name: "  Edge  ", primaryCpe23Uri: null);
        Assert.Equal("microsoft::edge", p.CanonicalProductKey);
    }

    [Fact]
    public void Create_rejects_empty_vendor_or_name()
    {
        Assert.Throws<ArgumentException>(() => SoftwareProduct.Create(" ", "x", null));
        Assert.Throws<ArgumentException>(() => SoftwareProduct.Create("x", " ", null));
    }

    [Fact]
    public void Create_rejects_vendor_longer_than_256_chars()
    {
        var longVendor = new string('a', 257);
        Assert.Throws<ArgumentException>(() => SoftwareProduct.Create(longVendor, "Edge", null));
    }

    [Fact]
    public void Create_rejects_name_longer_than_512_chars()
    {
        var longName = new string('a', 513);
        Assert.Throws<ArgumentException>(() => SoftwareProduct.Create("Microsoft", longName, null));
    }

    [Fact]
    public void Create_rejects_canonical_key_exceeding_512_chars()
    {
        var vendor = new string('a', 256);
        var name = new string('b', 260);
        // combined: 256 + 2 ("::") + 260 = 518 > 512
        Assert.Throws<ArgumentException>(() => SoftwareProduct.Create(vendor, name, null));
    }

    [Fact]
    public void Create_rejects_primary_cpe_longer_than_512_chars()
    {
        var longCpe = new string('c', 513);
        Assert.Throws<ArgumentException>(() => SoftwareProduct.Create("Microsoft", "Edge", longCpe));
    }

    [Fact]
    public void UpdatePrimaryCpe_refreshes_UpdatedAt()
    {
        var p = SoftwareProduct.Create("Microsoft", "Edge", null);
        var before = p.UpdatedAt;
        Thread.Sleep(5);
        p.UpdatePrimaryCpe("cpe:2.3:a:microsoft:edge:*:*:*:*:*:*:*:*");
        Assert.True(p.UpdatedAt > before);
    }

    [Fact]
    public void SetEndOfLife_refreshes_UpdatedAt()
    {
        var p = SoftwareProduct.Create("Microsoft", "Edge", null);
        var before = p.UpdatedAt;
        Thread.Sleep(5);
        p.SetEndOfLife(DateTimeOffset.UtcNow.AddYears(1));
        Assert.True(p.UpdatedAt > before);
    }

    [Fact]
    public void SetEndOfLife_accepts_null_to_clear()
    {
        var p = SoftwareProduct.Create("Microsoft", "Edge", null);
        p.SetEndOfLife(DateTimeOffset.UtcNow.AddYears(1));
        Assert.NotNull(p.EndOfLifeAt);
        p.SetEndOfLife(null);
        Assert.Null(p.EndOfLifeAt);
    }
}
