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

    [Fact]
    public void Create_rejects_null_display_name()
    {
        Assert.Throws<ArgumentException>(() => SourceSystem.Create("defender", null!));
    }

    [Fact]
    public void Create_rejects_empty_display_name()
    {
        Assert.Throws<ArgumentException>(() => SourceSystem.Create("defender", "   "));
    }

    [Fact]
    public void Create_trims_display_name()
    {
        var s = SourceSystem.Create("defender", "  Microsoft Defender  ");
        Assert.Equal("Microsoft Defender", s.DisplayName);
    }

    [Fact]
    public void Create_rejects_key_longer_than_64_chars()
    {
        var longKey = new string('a', 65);
        Assert.Throws<ArgumentException>(() => SourceSystem.Create(longKey, "Display"));
    }

    [Fact]
    public void Create_rejects_display_name_longer_than_256_chars()
    {
        var longDisplayName = new string('a', 257);
        Assert.Throws<ArgumentException>(() => SourceSystem.Create("defender", longDisplayName));
    }
}
