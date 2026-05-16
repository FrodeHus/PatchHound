using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Bulk;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Bulk;

[Collection(PostgresCollection.Name)]
public class PostgresBulkSoftwareProjectionWriterTests
{
    private static readonly Guid TenantId = Guid.Parse("00000001-0000-0000-0000-0000000000A1");

    private readonly PostgresFixture _fx;
    public PostgresBulkSoftwareProjectionWriterTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task SyncTenantSoftwareAsync_projects_install_set_into_tenant_software()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var sourceSystemId = await SeedSourceSystem(db, "defender");

        var productA = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var productB = SoftwareProduct.Create("Contoso", "Agent", "cpe:2.3:a:contoso:agent:*:*:*:*:*:*:*:*");
        db.SoftwareProducts.AddRange(productA, productB);

        var dev1 = Device.Create(TenantId, sourceSystemId, "dev-1", "dev-1", Criticality.Medium);
        var dev2 = Device.Create(TenantId, sourceSystemId, "dev-2", "dev-2", Criticality.Medium);
        var dev3 = Device.Create(TenantId, sourceSystemId, "dev-3", "dev-3", Criticality.Medium);
        db.Devices.AddRange(dev1, dev2, dev3);

        var earlier = DateTimeOffset.UtcNow.AddDays(-1);
        var later = DateTimeOffset.UtcNow;
        db.InstalledSoftware.AddRange(
            InstalledSoftware.Observe(TenantId, dev1.Id, productA.Id, sourceSystemId, "1.0.0", earlier),
            InstalledSoftware.Observe(TenantId, dev2.Id, productA.Id, sourceSystemId, "1.0.0", later),
            InstalledSoftware.Observe(TenantId, dev3.Id, productB.Id, sourceSystemId, "2.5", later));
        await db.SaveChangesAsync();

        var writer = new PostgresBulkSoftwareProjectionWriter(db);
        var snapshotId = Guid.NewGuid();
        await writer.SyncTenantSoftwareAsync(TenantId, snapshotId, CancellationToken.None);

        var records = await db.SoftwareTenantRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.TenantId == TenantId && r.SnapshotId == snapshotId)
            .ToListAsync();
        records.Should().HaveCount(2);
        records.Select(r => r.SoftwareProductId).Should().BeEquivalentTo(new[] { productA.Id, productB.Id });

        var recA = records.Single(r => r.SoftwareProductId == productA.Id);
        recA.FirstSeenAt.Should().BeCloseTo(earlier, TimeSpan.FromSeconds(1));
        recA.LastSeenAt.Should().BeCloseTo(later, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SyncTenantSoftwareAsync_deletes_stale_records()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        await SeedSourceSystem(db, "defender");

        var product = SoftwareProduct.Create("Acme", "Widget", null);
        db.SoftwareProducts.Add(product);

        // Pre-seed a tenant record whose product has no InstalledSoftware backing it.
        var stale = SoftwareTenantRecord.Create(
            TenantId, snapshotId: null, product.Id,
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow.AddDays(-1));
        db.SoftwareTenantRecords.Add(stale);
        await db.SaveChangesAsync();

        var writer = new PostgresBulkSoftwareProjectionWriter(db);
        await writer.SyncTenantSoftwareAsync(TenantId, snapshotId: null, CancellationToken.None);

        var remaining = await db.SoftwareTenantRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.TenantId == TenantId)
            .ToListAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncTenantSoftwareAsync_extends_observation_window_on_existing_record()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var sourceSystemId = await SeedSourceSystem(db, "defender");

        var product = SoftwareProduct.Create("Acme", "Widget", null);
        var device = Device.Create(TenantId, sourceSystemId, "dev-window", "dev-window", Criticality.Medium);
        var originalFirst = DateTimeOffset.UtcNow.AddDays(-10);
        var originalLast = DateTimeOffset.UtcNow.AddDays(-5);
        var snapshotId = Guid.NewGuid();
        var existing = SoftwareTenantRecord.Create(TenantId, snapshotId, product.Id, originalFirst, originalLast);

        db.SoftwareProducts.Add(product);
        db.Devices.Add(device);
        db.SoftwareTenantRecords.Add(existing);
        var newerFirst = DateTimeOffset.UtcNow.AddDays(-2);
        var newerLast = DateTimeOffset.UtcNow;
        var install = InstalledSoftware.Observe(TenantId, device.Id, product.Id, sourceSystemId, "1.0", newerFirst);
        db.InstalledSoftware.Add(install);
        await db.SaveChangesAsync();

        // Bump LastSeenAt on the install row to differ from FirstSeenAt.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "InstalledSoftware" SET "LastSeenAt" = {newerLast} WHERE "Id" = {install.Id}
            """);

        var writer = new PostgresBulkSoftwareProjectionWriter(db);
        await writer.SyncTenantSoftwareAsync(TenantId, snapshotId, CancellationToken.None);

        var reloaded = await db.SoftwareTenantRecords.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(r => r.Id == existing.Id);
        reloaded.FirstSeenAt.Should().BeCloseTo(originalFirst, TimeSpan.FromSeconds(1)); // earlier preserved
        reloaded.LastSeenAt.Should().BeCloseTo(newerLast, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SyncSoftwareInstallationsAsync_creates_active_installations()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var sourceSystemId = await SeedSourceSystem(db, "defender");

        var product = SoftwareProduct.Create("Acme", "Widget", null);
        var device = Device.Create(TenantId, sourceSystemId, "dev-x", "dev-x", Criticality.Medium);
        var observed = DateTimeOffset.UtcNow.AddMinutes(-5);
        var install = InstalledSoftware.Observe(TenantId, device.Id, product.Id, sourceSystemId, "1.2.3", observed);

        db.SoftwareProducts.Add(product);
        db.Devices.Add(device);
        db.InstalledSoftware.Add(install);
        await db.SaveChangesAsync();

        var writer = new PostgresBulkSoftwareProjectionWriter(db);
        var snapshotId = Guid.NewGuid();
        await writer.SyncTenantSoftwareAsync(TenantId, snapshotId, CancellationToken.None);
        await writer.SyncSoftwareInstallationsAsync(TenantId, snapshotId, CancellationToken.None);

        var tenantRec = await db.SoftwareTenantRecords.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(r => r.TenantId == TenantId && r.SoftwareProductId == product.Id);
        var projected = await db.SoftwareProductInstallations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(p => p.TenantId == TenantId && p.SoftwareAssetId == install.Id);

        projected.SnapshotId.Should().Be(snapshotId);
        projected.TenantSoftwareId.Should().Be(tenantRec.Id);
        projected.DeviceAssetId.Should().Be(device.Id);
        projected.IsActive.Should().BeTrue();
        projected.RemovedAt.Should().BeNull();
        projected.DetectedVersion.Should().Be("1.2.3");
        projected.SourceSystem.Should().Be(SoftwareIdentitySourceSystem.Defender);
        projected.CurrentEpisodeNumber.Should().Be(1);
    }

    [Fact]
    public async Task SyncSoftwareInstallationsAsync_marks_missing_installs_inactive()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var sourceSystemId = await SeedSourceSystem(db, "defender");

        var product = SoftwareProduct.Create("Acme", "Widget", null);
        var deviceA = Device.Create(TenantId, sourceSystemId, "dev-a", "dev-a", Criticality.Medium);
        var deviceB = Device.Create(TenantId, sourceSystemId, "dev-b", "dev-b", Criticality.Medium);
        var observed = DateTimeOffset.UtcNow.AddMinutes(-5);
        var installA = InstalledSoftware.Observe(TenantId, deviceA.Id, product.Id, sourceSystemId, "1.0", observed);
        var installB = InstalledSoftware.Observe(TenantId, deviceB.Id, product.Id, sourceSystemId, "1.0", observed);

        db.SoftwareProducts.Add(product);
        db.Devices.AddRange(deviceA, deviceB);
        db.InstalledSoftware.AddRange(installA, installB);
        await db.SaveChangesAsync();

        var writer = new PostgresBulkSoftwareProjectionWriter(db);
        await writer.SyncTenantSoftwareAsync(TenantId, snapshotId: null, CancellationToken.None);
        await writer.SyncSoftwareInstallationsAsync(TenantId, snapshotId: null, CancellationToken.None);

        // Remove installB and re-sync.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "InstalledSoftware" WHERE "Id" = {installB.Id}
            """);

        await writer.SyncTenantSoftwareAsync(TenantId, snapshotId: null, CancellationToken.None);
        await writer.SyncSoftwareInstallationsAsync(TenantId, snapshotId: null, CancellationToken.None);

        var projected = await db.SoftwareProductInstallations.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.TenantId == TenantId)
            .ToListAsync();
        projected.Should().HaveCount(2);
        projected.Should().ContainSingle(p => p.SoftwareAssetId == installA.Id && p.IsActive);
        projected.Should().ContainSingle(p => p.SoftwareAssetId == installB.Id && !p.IsActive && p.RemovedAt.HasValue);
    }

    [Fact]
    public async Task SyncSoftwareInstallationsAsync_resolves_authenticated_scan_source_system()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var sourceSystemId = await SeedSourceSystem(db, "authenticated-scan");

        var product = SoftwareProduct.Create("Acme", "Widget", null);
        var device = Device.Create(TenantId, sourceSystemId, "dev-auth", "dev-auth", Criticality.Medium);
        var install = InstalledSoftware.Observe(TenantId, device.Id, product.Id, sourceSystemId, "9.9", DateTimeOffset.UtcNow);

        db.SoftwareProducts.Add(product);
        db.Devices.Add(device);
        db.InstalledSoftware.Add(install);
        await db.SaveChangesAsync();

        var writer = new PostgresBulkSoftwareProjectionWriter(db);
        await writer.SyncTenantSoftwareAsync(TenantId, snapshotId: null, CancellationToken.None);
        await writer.SyncSoftwareInstallationsAsync(TenantId, snapshotId: null, CancellationToken.None);

        var projected = await db.SoftwareProductInstallations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(p => p.SoftwareAssetId == install.Id);
        projected.SourceSystem.Should().Be(SoftwareIdentitySourceSystem.AuthenticatedScan);
    }

    private static async Task<Guid> SeedSourceSystem(PatchHoundDbContext db, string key)
    {
        var source = SourceSystem.Create(key, key);
        db.SourceSystems.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }
}
