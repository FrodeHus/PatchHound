using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;

namespace PatchHound.Tests.Infrastructure.Services;

public class DeviceResolverTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private SourceSystem _sourceSystem = null!;
    private DeviceResolver _sut = null!;
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _db = await TestDbContextFactory.CreateAsync();
        _sourceSystem = SourceSystem.Create("defender", "Defender");
        _db.SourceSystems.Add(_sourceSystem);
        await _db.SaveChangesAsync();

        _sut = new DeviceResolver(_db);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Resolve_creates_device_on_first_observation()
    {
        var observation = new DeviceObservation(
            TenantId: _tenantId,
            SourceSystemId: _sourceSystem.Id,
            ExternalId: "dev-001",
            Name: "workstation-01",
            BaselineCriticality: Criticality.Medium);

        var device = await _sut.ResolveAsync(observation, CancellationToken.None);

        device.Should().NotBeNull();
        device.TenantId.Should().Be(_tenantId);
        device.SourceSystemId.Should().Be(_sourceSystem.Id);
        device.ExternalId.Should().Be("dev-001");
        device.Name.Should().Be("workstation-01");
        device.BaselineCriticality.Should().Be(Criticality.Medium);

        var allDevices = await _db.Devices.IgnoreQueryFilters().ToListAsync();
        allDevices.Should().ContainSingle();
    }

    [Fact]
    public async Task Resolve_returns_existing_device_on_second_observation()
    {
        var observation = new DeviceObservation(
            TenantId: _tenantId,
            SourceSystemId: _sourceSystem.Id,
            ExternalId: "dev-002",
            Name: "workstation-02",
            BaselineCriticality: Criticality.Low);

        var first = await _sut.ResolveAsync(observation, CancellationToken.None);
        var second = await _sut.ResolveAsync(observation, CancellationToken.None);

        second.Id.Should().Be(first.Id);

        var allDevices = await _db.Devices.IgnoreQueryFilters().ToListAsync();
        allDevices.Should().ContainSingle();
    }

    [Fact]
    public async Task Resolve_same_external_id_different_source_systems_creates_two_devices()
    {
        var otherSourceSystem = SourceSystem.Create("qualys", "Qualys");
        _db.SourceSystems.Add(otherSourceSystem);
        await _db.SaveChangesAsync();

        var observation1 = new DeviceObservation(
            TenantId: _tenantId,
            SourceSystemId: _sourceSystem.Id,
            ExternalId: "shared-ext-id",
            Name: "host-from-defender",
            BaselineCriticality: Criticality.Medium);
        var observation2 = new DeviceObservation(
            TenantId: _tenantId,
            SourceSystemId: otherSourceSystem.Id,
            ExternalId: "shared-ext-id",
            Name: "host-from-qualys",
            BaselineCriticality: Criticality.High);

        var first = await _sut.ResolveAsync(observation1, CancellationToken.None);
        var second = await _sut.ResolveAsync(observation2, CancellationToken.None);

        first.Id.Should().NotBe(second.Id);
        first.SourceSystemId.Should().Be(_sourceSystem.Id);
        second.SourceSystemId.Should().Be(otherSourceSystem.Id);

        var allDevices = await _db.Devices.IgnoreQueryFilters().ToListAsync();
        allDevices.Should().HaveCount(2);
    }
}
