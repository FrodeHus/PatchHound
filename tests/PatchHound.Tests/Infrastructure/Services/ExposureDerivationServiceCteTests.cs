using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Bulk;
using PatchHound.Tests.Infrastructure;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services;

/// <summary>
/// Verifies the server-side CTE path of <see cref="ExposureDerivationService"/>. Runs
/// against a real PostgreSQL container because the CTE cannot be exercised by the
/// EF Core InMemory provider; the LINQ fallback is exercised by the legacy
/// <see cref="ExposureDerivationServiceTests"/> suite.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ExposureDerivationServiceCteTests
{
    private static readonly Guid TenantId = Guid.Parse("00000001-0000-0000-0000-000000000001");

    private readonly PostgresFixture _fx;
    public ExposureDerivationServiceCteTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task DeriveForTenantAsync_inserts_one_exposure_per_installed_product()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var otherProduct = SoftwareProduct.Create("Acme", "Gadget", "cpe:2.3:a:acme:gadget:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("nvd", "CVE-2026-CTE1", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.AddRange(product, otherProduct);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, product.Id, null, true, null, null, null, null));
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, otherProduct.Id, null, true, null, null, null, null));

        var source = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(source);
        var deviceA = Device.Create(TenantId, source.Id, "dev-a", "Device A", Criticality.Medium);
        var deviceB = Device.Create(TenantId, source.Id, "dev-b", "Device B", Criticality.Medium);
        db.Devices.AddRange(deviceA, deviceB);
        var runId = Guid.NewGuid();
        db.InstalledSoftware.AddRange(
            InstalledSoftware.Observe(TenantId, deviceA.Id, product.Id, source.Id, "1.0", DateTimeOffset.UtcNow, runId),
            InstalledSoftware.Observe(TenantId, deviceB.Id, product.Id, source.Id, "1.0", DateTimeOffset.UtcNow, runId));
        await db.SaveChangesAsync();

        var observedAt = DateTimeOffset.UtcNow;
        var svc = new ExposureDerivationService(
            db, NullLogger<ExposureDerivationService>.Instance, new PostgresBulkExposureWriter(db));

        var result = await svc.DeriveForTenantAsync(TenantId, observedAt, runId, CancellationToken.None);

        result.Inserted.Should().Be(2);
        result.Reobserved.Should().Be(0);
        result.Resolved.Should().Be(0);

        var exposures = await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters().ToListAsync();
        exposures.Should().HaveCount(2);
        exposures.Should().OnlyContain(e => e.LastSeenRunId == runId);
        exposures.Select(e => e.DeviceId).Should().BeEquivalentTo(new[] { deviceA.Id, deviceB.Id });
        exposures.Should().OnlyContain(e => e.MatchSource == ExposureMatchSource.Product);
    }

    [Fact]
    public async Task DeriveForTenantAsync_resolves_exposure_when_install_is_gone()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("nvd", "CVE-2026-CTE2", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, product.Id, null, true, null, null, null, null));

        var source = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(source);
        var device = Device.Create(TenantId, source.Id, "dev-1", "Device 1", Criticality.Medium);
        db.Devices.Add(device);
        var firstRun = Guid.NewGuid();
        var installed = InstalledSoftware.Observe(TenantId, device.Id, product.Id, source.Id, "1.0", DateTimeOffset.UtcNow, firstRun);
        db.InstalledSoftware.Add(installed);
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(
            db, NullLogger<ExposureDerivationService>.Instance, new PostgresBulkExposureWriter(db));

        // First run — install present, exposure inserted
        await svc.DeriveForTenantAsync(TenantId, DateTimeOffset.UtcNow, firstRun, CancellationToken.None);

        // Remove the install so the next derive yields zero active pairs for this tenant
        db.InstalledSoftware.Remove(installed);
        await db.SaveChangesAsync();

        // Second run — exposure should be resolved by ResolveStaleAsync (LastSeenRunId != newRun)
        var secondRun = Guid.NewGuid();
        var resolveAt = DateTimeOffset.UtcNow.AddHours(1);
        var result = await svc.DeriveForTenantAsync(TenantId, resolveAt, secondRun, CancellationToken.None);

        result.Inserted.Should().Be(0);
        result.Reobserved.Should().Be(0);
        result.Resolved.Should().Be(1);

        var exposure = await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters().SingleAsync();
        exposure.Status.Should().Be(ExposureStatus.Resolved);
        exposure.ResolvedAt.Should().BeCloseTo(resolveAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DeriveForTenantAsync_matches_via_cpe_fallback_when_applicability_has_no_product()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("nvd", "CVE-2026-CTE3", "t", "d", Severity.Critical, 9.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, softwareProductId: null,
            cpeCriteria: "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*",
            vulnerable: true, null, null, null, null));

        var source = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(source);
        var device = Device.Create(TenantId, source.Id, "dev-1", "Device", Criticality.Medium);
        db.Devices.Add(device);
        var runId = Guid.NewGuid();
        db.InstalledSoftware.Add(InstalledSoftware.Observe(TenantId, device.Id, product.Id, source.Id, "1.0", DateTimeOffset.UtcNow, runId));
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(
            db, NullLogger<ExposureDerivationService>.Instance, new PostgresBulkExposureWriter(db));

        var result = await svc.DeriveForTenantAsync(TenantId, DateTimeOffset.UtcNow, runId, CancellationToken.None);

        result.Inserted.Should().Be(1);
        var exposure = await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters().SingleAsync();
        exposure.MatchSource.Should().Be(ExposureMatchSource.Cpe);
    }

    /// <summary>
    /// Proves <see cref="ExposureDerivationService.VersionMatches"/> runs over the
    /// Postgres CTE output: the CTE itself does not encode version ranges (no semver
    /// in pure SQL), so any filtering at this layer must happen client-side after the
    /// reader yields rows. Without that, the installed version "2.0" would falsely
    /// produce an exposure against an applicability capped at VersionEndIncluding="1.5".
    /// </summary>
    [Fact]
    public async Task DeriveForTenantAsync_skips_exposure_when_installed_version_is_above_VersionEndIncluding()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("nvd", "CVE-2026-CTE4", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, product.Id, null, vulnerable: true,
            versionStartIncluding: null,
            versionStartExcluding: null,
            versionEndIncluding: "1.5",
            versionEndExcluding: null));

        var source = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(source);
        var device = Device.Create(TenantId, source.Id, "dev-1", "Device", Criticality.Medium);
        db.Devices.Add(device);
        var runId = Guid.NewGuid();
        db.InstalledSoftware.Add(InstalledSoftware.Observe(
            TenantId, device.Id, product.Id, source.Id, "2.0", DateTimeOffset.UtcNow, runId));
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(
            db, NullLogger<ExposureDerivationService>.Instance, new PostgresBulkExposureWriter(db));

        var result = await svc.DeriveForTenantAsync(TenantId, DateTimeOffset.UtcNow, runId, CancellationToken.None);

        result.Inserted.Should().Be(0);
        (await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeriveForTenantAsync_only_derives_from_installs_seen_in_current_run()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("nvd", "CVE-2026-CTE5", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, product.Id, null, true, null, null, null, null));

        var source = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(source);
        var staleDevice = Device.Create(TenantId, source.Id, "dev-old", "Old Device", Criticality.Medium);
        var currentDevice = Device.Create(TenantId, source.Id, "dev-current", "Current Device", Criticality.Medium);
        db.Devices.AddRange(staleDevice, currentDevice);

        var oldRun = Guid.NewGuid();
        var currentRun = Guid.NewGuid();
        db.InstalledSoftware.AddRange(
            InstalledSoftware.Observe(TenantId, staleDevice.Id, product.Id, source.Id, "1.0", DateTimeOffset.UtcNow.AddHours(-1), oldRun),
            InstalledSoftware.Observe(TenantId, currentDevice.Id, product.Id, source.Id, "1.0", DateTimeOffset.UtcNow, currentRun));
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(
            db, NullLogger<ExposureDerivationService>.Instance, new PostgresBulkExposureWriter(db));

        var result = await svc.DeriveForTenantAsync(TenantId, DateTimeOffset.UtcNow, currentRun, CancellationToken.None);

        result.Inserted.Should().Be(1);
        var exposure = await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters().SingleAsync();
        exposure.DeviceId.Should().Be(currentDevice.Id);
        exposure.LastSeenRunId.Should().Be(currentRun);
    }
}
