using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using Xunit;

namespace PatchHound.Tests.Core;

public class DeviceTests
{
    [Fact]
    public void Create_initializes_all_identity_fields()
    {
        var tenantId = Guid.NewGuid();
        var sourceSystemId = Guid.NewGuid();
        var d = Device.Create(
            tenantId: tenantId,
            sourceSystemId: sourceSystemId,
            externalId: "dev-1",
            name: "host.example.com",
            baselineCriticality: Criticality.Medium);
        Assert.Equal(tenantId, d.TenantId);
        Assert.Equal(sourceSystemId, d.SourceSystemId);
        Assert.Equal("dev-1", d.ExternalId);
        Assert.Equal("host.example.com", d.Name);
        Assert.Equal(Criticality.Medium, d.Criticality);
        Assert.Equal(Criticality.Medium, d.BaselineCriticality);
        Assert.True(d.ActiveInTenant);
    }

    [Fact]
    public void SetCriticality_marks_source_as_manual()
    {
        var d = Device.Create(Guid.NewGuid(), Guid.NewGuid(), "d", "n", Criticality.Low);
        d.SetCriticality(Criticality.High);
        Assert.Equal(Criticality.High, d.Criticality);
        Assert.Equal("ManualOverride", d.CriticalitySource);
    }

    [Fact]
    public void Create_rejects_empty_tenantId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Device.Create(Guid.Empty, Guid.NewGuid(), "dev-1", "host", Criticality.Low));
        Assert.Equal("tenantId", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_empty_sourceSystemId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Device.Create(Guid.NewGuid(), Guid.Empty, "dev-1", "host", Criticality.Low));
        Assert.Equal("sourceSystemId", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_whitespace_externalId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Device.Create(Guid.NewGuid(), Guid.NewGuid(), "   ", "host", Criticality.Low));
        Assert.Equal("externalId", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_whitespace_name()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Device.Create(Guid.NewGuid(), Guid.NewGuid(), "dev-1", "   ", Criticality.Low));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_externalId_longer_than_256_chars()
    {
        var longId = new string('a', 257);
        var ex = Assert.Throws<ArgumentException>(() =>
            Device.Create(Guid.NewGuid(), Guid.NewGuid(), longId, "host", Criticality.Low));
        Assert.Equal("externalId", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_name_longer_than_256_chars()
    {
        var longName = new string('b', 257);
        var ex = Assert.Throws<ArgumentException>(() =>
            Device.Create(Guid.NewGuid(), Guid.NewGuid(), "dev-1", longName, Criticality.Low));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_description_longer_than_2048_chars()
    {
        var longDesc = new string('c', 2049);
        var ex = Assert.Throws<ArgumentException>(() =>
            Device.Create(Guid.NewGuid(), Guid.NewGuid(), "dev-1", "host", Criticality.Low, longDesc));
        Assert.Equal("description", ex.ParamName);
    }

    [Fact]
    public void Create_trims_externalId_name_and_description()
    {
        var d = Device.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  dev-1  ",
            "  host.example.com  ",
            Criticality.Low,
            "  a description  ");
        Assert.Equal("dev-1", d.ExternalId);
        Assert.Equal("host.example.com", d.Name);
        Assert.Equal("a description", d.Description);
    }
}
