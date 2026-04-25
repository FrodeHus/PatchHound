using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure.Services;

public class NormalizedSoftwareProjectionServiceTests : IDisposable
{
    private readonly PatchHoundDbContext _db;
    private readonly SourceSystem _sourceSystem;
    private readonly NormalizedSoftwareProjectionService _sut;

    public NormalizedSoftwareProjectionServiceTests()
    {
        _db = TestDbContextFactory.CreateSystemContext();
        _sourceSystem = SourceSystem.Create("defender", "Defender");
        _db.SourceSystems.Add(_sourceSystem);
        _db.SaveChanges();
        _sut = new NormalizedSoftwareProjectionService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task SyncTenantAsync_projects_one_active_installation_per_installed_software_row()
    {
        var tenantId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var product = SoftwareProduct.Create("Contoso", "Agent", "cpe:2.3:a:contoso:agent:*:*:*:*:*:*:*:*");
        var devices = Enumerable.Range(1, 3)
            .Select(index => Device.Create(
                tenantId,
                _sourceSystem.Id,
                $"device-{index}",
                $"device-{index}",
                Criticality.Medium))
            .ToList();
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        _db.SoftwareProducts.Add(product);
        _db.Devices.AddRange(devices);
        _db.InstalledSoftware.AddRange(devices.Select(device =>
            InstalledSoftware.Observe(
                tenantId,
                device.Id,
                product.Id,
                _sourceSystem.Id,
                "1.2.3",
                observedAt)));
        await _db.SaveChangesAsync();

        await _sut.SyncTenantAsync(tenantId, snapshotId, CancellationToken.None);

        var tenantSoftware = await _db.SoftwareTenantRecords.IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.SnapshotId == snapshotId);
        tenantSoftware.SoftwareProductId.Should().Be(product.Id);

        var projected = await _db.SoftwareProductInstallations.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.SnapshotId == snapshotId)
            .ToListAsync();
        projected.Should().HaveCount(3);
        projected.Select(item => item.TenantSoftwareId).Distinct().Should().ContainSingle().Which.Should().Be(tenantSoftware.Id);
        projected.Select(item => item.DeviceAssetId).Should().BeEquivalentTo(devices.Select(device => device.Id));
        projected.Should().OnlyContain(item => item.IsActive);
        projected.Should().OnlyContain(item => item.DetectedVersion == "1.2.3");
    }

    [Fact]
    public async Task SyncTenantAsync_marks_missing_projected_installations_inactive()
    {
        var tenantId = Guid.NewGuid();
        var product = SoftwareProduct.Create("Contoso", "Agent", null);
        var deviceA = Device.Create(tenantId, _sourceSystem.Id, "device-a", "device-a", Criticality.Medium);
        var deviceB = Device.Create(tenantId, _sourceSystem.Id, "device-b", "device-b", Criticality.Medium);
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var installA = InstalledSoftware.Observe(tenantId, deviceA.Id, product.Id, _sourceSystem.Id, "1.0", observedAt);
        var installB = InstalledSoftware.Observe(tenantId, deviceB.Id, product.Id, _sourceSystem.Id, "1.0", observedAt);

        _db.SoftwareProducts.Add(product);
        _db.Devices.AddRange(deviceA, deviceB);
        _db.InstalledSoftware.AddRange(installA, installB);
        await _db.SaveChangesAsync();
        await _sut.SyncTenantAsync(tenantId, null, CancellationToken.None);

        _db.InstalledSoftware.Remove(installB);
        await _db.SaveChangesAsync();
        await _sut.SyncTenantAsync(tenantId, null, CancellationToken.None);

        var projected = await _db.SoftwareProductInstallations.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId)
            .ToListAsync();

        projected.Should().ContainSingle(item => item.SoftwareAssetId == installA.Id && item.IsActive);
        projected.Should().ContainSingle(item =>
            item.SoftwareAssetId == installB.Id
            && !item.IsActive
            && item.RemovedAt.HasValue);
    }
}
