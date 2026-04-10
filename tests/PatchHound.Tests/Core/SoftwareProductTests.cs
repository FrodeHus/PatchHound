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
}
