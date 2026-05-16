using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core;

public class InstalledSoftwareTests
{
    [Fact]
    public void Observe_sets_identity_and_timestamps()
    {
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();

        var i = InstalledSoftware.Observe(tenantId, deviceId, productId, sourceId, version: "1.2.3", at, runId);

        Assert.Equal(tenantId, i.TenantId);
        Assert.Equal(deviceId, i.DeviceId);
        Assert.Equal(productId, i.SoftwareProductId);
        Assert.Equal(sourceId, i.SourceSystemId);
        Assert.Equal("1.2.3", i.Version);
        Assert.Equal(at, i.FirstSeenAt);
        Assert.Equal(at, i.LastSeenAt);
        Assert.Equal(runId, i.LastSeenRunId);
    }

    [Fact]
    public void Observe_with_null_version_uses_empty_sentinel()
    {
        var i = InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), version: null, DateTimeOffset.UtcNow);
        Assert.Equal("", i.Version);
    }

    [Fact]
    public void Touch_updates_last_seen_but_not_first_seen()
    {
        var first = DateTimeOffset.UtcNow.AddDays(-1);
        var i = InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.0", first);
        var next = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        i.Touch(next, runId);
        Assert.Equal(first, i.FirstSeenAt);
        Assert.Equal(next, i.LastSeenAt);
        Assert.Equal(runId, i.LastSeenRunId);
    }

    [Fact]
    public void Touch_ignores_older_timestamp()
    {
        var first = DateTimeOffset.UtcNow;
        var i = InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.0", first);
        i.Touch(first.AddDays(-1));
        Assert.Equal(first, i.LastSeenAt);
    }

    [Fact]
    public void Touch_ignores_equal_timestamp()
    {
        var at = DateTimeOffset.UtcNow;
        var i = InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.0", at);
        i.Touch(at);
        Assert.Equal(at, i.LastSeenAt);
    }

    [Fact]
    public void Observe_rejects_default_at()
    {
        Assert.Throws<ArgumentException>(
            () => InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.0", default));
    }

    [Fact]
    public void Observe_rejects_empty_tenantId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            InstalledSoftware.Observe(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.0", DateTimeOffset.UtcNow));
        Assert.Equal("tenantId", ex.ParamName);
    }

    [Fact]
    public void Observe_rejects_empty_deviceId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            InstalledSoftware.Observe(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "1.0", DateTimeOffset.UtcNow));
        Assert.Equal("deviceId", ex.ParamName);
    }

    [Fact]
    public void Observe_rejects_empty_softwareProductId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "1.0", DateTimeOffset.UtcNow));
        Assert.Equal("softwareProductId", ex.ParamName);
    }

    [Fact]
    public void Observe_rejects_empty_sourceSystemId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "1.0", DateTimeOffset.UtcNow));
        Assert.Equal("sourceSystemId", ex.ParamName);
    }

    [Fact]
    public void Observe_rejects_version_longer_than_128_chars()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 129), DateTimeOffset.UtcNow));
        Assert.Equal("version", ex.ParamName);
    }

    [Fact]
    public void Observe_trims_version()
    {
        var i = InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  1.2.3  ", DateTimeOffset.UtcNow);
        Assert.Equal("1.2.3", i.Version);
    }
}
