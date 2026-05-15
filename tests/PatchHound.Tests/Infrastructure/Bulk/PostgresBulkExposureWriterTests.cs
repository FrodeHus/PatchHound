using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Bulk;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Bulk;

[Collection(PostgresCollection.Name)]
public class PostgresBulkExposureWriterTests
{
    private static readonly Guid TenantId = Guid.Parse("00000001-0000-0000-0000-000000000001");

    private readonly PostgresFixture _fx;
    public PostgresBulkExposureWriterTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task UpsertAsync_inserts_new_rows_and_reobserves_existing()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var (deviceId, vulnId) = await SeedDeviceAndVuln(db);
        var writer = new PostgresBulkExposureWriter(db);

        var run1 = Guid.NewGuid();
        var observed = DateTimeOffset.UtcNow;
        var first = await writer.UpsertAsync(new[]
        {
            new ExposureUpsertRow(TenantId, deviceId, vulnId, null, null, "1.0", "Cpe", observed, run1),
        }, CancellationToken.None);

        first.Inserted.Should().Be(1);
        first.Reobserved.Should().Be(0);

        var run2 = Guid.NewGuid();
        var laterObserved = observed.AddMinutes(5);
        var second = await writer.UpsertAsync(new[]
        {
            new ExposureUpsertRow(TenantId, deviceId, vulnId, null, null, "1.0", "Cpe", laterObserved, run2),
        }, CancellationToken.None);

        second.Inserted.Should().Be(0);
        second.Reobserved.Should().Be(1);

        var stored = await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters().SingleAsync();
        stored.LastSeenRunId.Should().Be(run2);
        stored.LastObservedAt.Should().BeCloseTo(laterObserved, TimeSpan.FromSeconds(1));
        stored.Status.Should().Be(ExposureStatus.Open);
    }

    [Fact]
    public async Task ResolveStaleAsync_resolves_only_exposures_not_seen_in_current_run()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var (deviceId, vulnA) = await SeedDeviceAndVuln(db);
        var vulnB = await SeedAnotherVuln(db);
        var writer = new PostgresBulkExposureWriter(db);

        var oldRun = Guid.NewGuid();
        await writer.UpsertAsync(new[]
        {
            new ExposureUpsertRow(TenantId, deviceId, vulnA, null, null, "1.0", "Cpe", DateTimeOffset.UtcNow, oldRun),
            new ExposureUpsertRow(TenantId, deviceId, vulnB, null, null, "1.0", "Cpe", DateTimeOffset.UtcNow, oldRun),
        }, CancellationToken.None);

        var newRun = Guid.NewGuid();
        await writer.UpsertAsync(new[]
        {
            new ExposureUpsertRow(TenantId, deviceId, vulnA, null, null, "1.0", "Cpe", DateTimeOffset.UtcNow, newRun),
        }, CancellationToken.None);

        var resolved = await writer.ResolveStaleAsync(TenantId, newRun, DateTimeOffset.UtcNow, CancellationToken.None);
        resolved.Should().Be(1);

        var byVuln = await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters()
            .ToDictionaryAsync(e => e.VulnerabilityId, e => e.Status);
        byVuln[vulnA].Should().Be(ExposureStatus.Open);
        byVuln[vulnB].Should().Be(ExposureStatus.Resolved);
    }

    private static async Task<(Guid deviceId, Guid vulnId)> SeedDeviceAndVuln(PatchHoundDbContext db)
    {
        var source = SourceSystem.Create("test-src", "Test Source");
        db.SourceSystems.Add(source);
        await db.SaveChangesAsync();

        var device = Device.Create(TenantId, source.Id, "dev-a", "DeviceA", Criticality.High);
        db.Devices.Add(device);

        var vuln = Vulnerability.Create("nvd", "CVE-2026-9001", "Test vuln", "desc",
            Severity.High, 7.5m, null, DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(vuln);

        await db.SaveChangesAsync();
        return (device.Id, vuln.Id);
    }

    private static async Task<Guid> SeedAnotherVuln(PatchHoundDbContext db)
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-9002", "Another vuln", "desc",
            Severity.Medium, 5.0m, null, DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(vuln);
        await db.SaveChangesAsync();
        return vuln.Id;
    }
}
