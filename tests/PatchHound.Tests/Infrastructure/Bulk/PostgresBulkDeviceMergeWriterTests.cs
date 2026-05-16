using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Bulk;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Bulk;

[Collection(PostgresCollection.Name)]
public class PostgresBulkDeviceMergeWriterTests
{
    private static readonly Guid TenantId = Guid.Parse("00000001-0000-0000-0000-000000000001");

    private readonly PostgresFixture _fx;
    public PostgresBulkDeviceMergeWriterTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task UpsertDevicesAsync_inserts_then_updates_without_duplication()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var sourceSystemId = await SeedSourceSystem(db, "defender");
        var writer = new PostgresBulkDeviceMergeWriter(db);

        var observed = DateTimeOffset.UtcNow;
        var rows = new[]
        {
            new DeviceMergeRow(TenantId, sourceSystemId, "ext-1", "host-1", null, "Active",
                "Windows", "10", null, observed, null, null, null, null, null, null, null, null, true),
            new DeviceMergeRow(TenantId, sourceSystemId, "ext-2", "host-2", null, "Active",
                "Linux", "Ubuntu 22.04", null, observed, null, null, null, null, null, null, null, null, true),
        };

        var firstIds = await writer.UpsertDevicesAsync(rows, CancellationToken.None);
        firstIds.Should().HaveCount(2);
        firstIds.Should().ContainKey((sourceSystemId, "ext-1"));
        firstIds.Should().ContainKey((sourceSystemId, "ext-2"));

        var stored = await db.Devices.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        stored.Should().HaveCount(2);
        stored.Should().OnlyContain(d => d.ActiveInTenant);

        // Second upsert with same external IDs → update, not duplicate.
        var rerun = await writer.UpsertDevicesAsync(rows, CancellationToken.None);
        rerun.Should().HaveCount(2);
        rerun[(sourceSystemId, "ext-1")].Should().Be(firstIds[(sourceSystemId, "ext-1")]);

        (await db.Devices.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task UpsertDevicesAsync_updates_mutable_inventory_fields_on_conflict()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var sourceSystemId = await SeedSourceSystem(db, "defender");
        var writer = new PostgresBulkDeviceMergeWriter(db);

        var initial = new DeviceMergeRow(TenantId, sourceSystemId, "ext-mut", "host-mut", "host-mut.local",
            "Active", "Windows", "10", "Low", DateTimeOffset.UtcNow, "10.0.0.1", null, null, null, null, null, null, null, true);
        await writer.UpsertDevicesAsync(new[] { initial }, CancellationToken.None);

        var updated = initial with { HealthStatus = "Inactive", OsVersion = "11", IsActive = false, ExternalRiskLabel = "High" };
        await writer.UpsertDevicesAsync(new[] { updated }, CancellationToken.None);

        var stored = await db.Devices.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        stored.HealthStatus.Should().Be("Inactive");
        stored.OsVersion.Should().Be("11");
        stored.ActiveInTenant.Should().BeFalse();
        stored.ExternalRiskLabel.Should().Be("High");
    }

    [Fact]
    public async Task UpsertDevicesAsync_updates_name_on_conflict()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var sourceSystemId = await SeedSourceSystem(db, "defender");
        var writer = new PostgresBulkDeviceMergeWriter(db);

        var initial = new DeviceMergeRow(TenantId, sourceSystemId, "ext-rename", "old-name",
            null, "Active", "Windows", "10", null, DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null, true);
        await writer.UpsertDevicesAsync(new[] { initial }, CancellationToken.None);

        var renamed = initial with { Name = "new-name" };
        await writer.UpsertDevicesAsync(new[] { renamed }, CancellationToken.None);

        var stored = await db.Devices.IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.ExternalId == "ext-rename");
        stored.Name.Should().Be("new-name");
    }

    [Fact]
    public async Task UpsertInstalledSoftwareAsync_inserts_and_updates_last_seen_at()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var sourceSystemId = await SeedSourceSystem(db, "defender");
        var writer = new PostgresBulkDeviceMergeWriter(db);

        // Seed a device via the writer.
        var deviceRows = new[]
        {
            new DeviceMergeRow(TenantId, sourceSystemId, "ext-isw", "host-isw", null, "Active",
                "Windows", "10", null, DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null, true),
        };
        var deviceMap = await writer.UpsertDevicesAsync(deviceRows, CancellationToken.None);
        var deviceId = deviceMap[(sourceSystemId, "ext-isw")];

        // Seed a software product directly.
        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        db.SoftwareProducts.Add(product);
        await db.SaveChangesAsync();

        var observed = DateTimeOffset.UtcNow;
        var firstRun = Guid.NewGuid();
        var first = await writer.UpsertInstalledSoftwareAsync(new[]
        {
            new InstalledSoftwareMergeRow(TenantId, deviceId, product.Id, sourceSystemId, "1.0.0", observed, firstRun),
        }, CancellationToken.None);
        first.Should().Be(1);

        var laterObserved = observed.AddMinutes(5);
        var secondRun = Guid.NewGuid();
        var second = await writer.UpsertInstalledSoftwareAsync(new[]
        {
            new InstalledSoftwareMergeRow(TenantId, deviceId, product.Id, sourceSystemId, "1.0.0", laterObserved, secondRun),
        }, CancellationToken.None);
        second.Should().Be(1);

        (await db.InstalledSoftware.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        var stored = await db.InstalledSoftware.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        stored.LastSeenAt.Should().BeCloseTo(laterObserved, TimeSpan.FromSeconds(1));
        stored.LastSeenRunId.Should().Be(secondRun);
    }

    private static async Task<Guid> SeedSourceSystem(PatchHoundDbContext db, string key)
    {
        var source = SourceSystem.Create(key, key);
        db.SourceSystems.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }
}
