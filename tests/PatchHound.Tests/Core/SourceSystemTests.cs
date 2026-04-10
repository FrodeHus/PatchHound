using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core;

public class SourceSystemTests
{
    [Fact]
    public void Create_trims_and_lowercases_key()
    {
        var s = SourceSystem.Create("  Defender  ", "Microsoft Defender for Endpoint");
        Assert.Equal("defender", s.Key);
        Assert.Equal("Microsoft Defender for Endpoint", s.DisplayName);
        Assert.NotEqual(Guid.Empty, s.Id);
    }

    [Fact]
    public void Create_rejects_empty_key()
    {
        Assert.Throws<ArgumentException>(() => SourceSystem.Create("  ", "x"));
    }
}
