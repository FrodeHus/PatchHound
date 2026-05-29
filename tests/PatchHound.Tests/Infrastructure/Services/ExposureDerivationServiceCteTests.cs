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
    public async Task Derivation_indexes_support_current_run_product_and_cpe_matches()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var indexes = await db.Database.SqlQueryRaw<string>("""
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN ('InstalledSoftware', 'VulnerabilityApplicabilities')
            """).ToListAsync();

        indexes.Should().Contain("IX_InstalledSoftware_TenantId_LastSeenRunId_SoftwareProductId");
        indexes.Should().Contain("IX_VulnerabilityApplicabilities_Vulnerable_SoftwareProductId");
        indexes.Should().Contain("IX_VulnerabilityApplicabilities_Vulnerable_CpeCriteria_Lower");
    }

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

        // Second run — first missing detection increments the miss counter but keeps the exposure open
        var secondRun = Guid.NewGuid();
        var resolveAt = DateTimeOffset.UtcNow.AddHours(1);
        var firstMiss = await svc.DeriveForTenantAsync(TenantId, resolveAt, secondRun, CancellationToken.None);

        firstMiss.Inserted.Should().Be(0);
        firstMiss.Reobserved.Should().Be(0);
        firstMiss.Resolved.Should().Be(0);

        // Third run — second consecutive miss resolves the exposure.
        var thirdRun = Guid.NewGuid();
        var result = await svc.DeriveForTenantAsync(TenantId, resolveAt, thirdRun, CancellationToken.None);

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
    public async Task DeriveForTenantAsync_matches_exact_unparseable_version_by_string_equality()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var product = SoftwareProduct.Create("Acme", "Server", "cpe:2.3:a:acme:server:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("microsoft-defender", "CVE-2026-CTE5", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, product.Id, null, vulnerable: true,
            versionStartIncluding: "Release",
            versionStartExcluding: null,
            versionEndIncluding: "RELEASE",
            versionEndExcluding: null));

        var source = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(source);
        var matchingDevice = Device.Create(TenantId, source.Id, "dev-1", "Device 1", Criticality.Medium);
        var otherDevice = Device.Create(TenantId, source.Id, "dev-2", "Device 2", Criticality.Medium);
        db.Devices.AddRange(matchingDevice, otherDevice);
        var runId = Guid.NewGuid();
        db.InstalledSoftware.Add(InstalledSoftware.Observe(
            TenantId, matchingDevice.Id, product.Id, source.Id, "release", DateTimeOffset.UtcNow, runId));
        db.InstalledSoftware.Add(InstalledSoftware.Observe(
            TenantId, otherDevice.Id, product.Id, source.Id, "2023", DateTimeOffset.UtcNow, runId));
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(
            db, NullLogger<ExposureDerivationService>.Instance, new PostgresBulkExposureWriter(db));

        var result = await svc.DeriveForTenantAsync(TenantId, DateTimeOffset.UtcNow, runId, CancellationToken.None);

        result.Inserted.Should().Be(1);
        var exposure = await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters().SingleAsync();
        exposure.DeviceId.Should().Be(matchingDevice.Id);
        exposure.MatchedVersion.Should().Be("release");
    }

    [Fact]
    public async Task DeriveForTenantAsync_derives_across_installs_from_other_sources_prior_runs()
    {
        // Regression test: derivation must be source-agnostic. Each ingestion source
        // acquires its own run id, so filtering installs by "LastSeenRunId == currentRun"
        // excludes every other source's installs and causes ResolveStaleAsync to mark
        // their exposures as Resolved — emptying the dashboard. See ExposureDerivationService
        // for the matching note.
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
        var otherSourceDevice = Device.Create(TenantId, source.Id, "dev-other-src", "Other-source Device", Criticality.Medium);
        var currentSourceDevice = Device.Create(TenantId, source.Id, "dev-current-src", "Current-source Device", Criticality.Medium);
        db.Devices.AddRange(otherSourceDevice, currentSourceDevice);

        var otherSourceRun = Guid.NewGuid();  // Stand-in for a prior ingestion of a different source.
        var currentRun = Guid.NewGuid();
        db.InstalledSoftware.AddRange(
            InstalledSoftware.Observe(TenantId, otherSourceDevice.Id, product.Id, source.Id, "1.0", DateTimeOffset.UtcNow.AddHours(-1), otherSourceRun),
            InstalledSoftware.Observe(TenantId, currentSourceDevice.Id, product.Id, source.Id, "1.0", DateTimeOffset.UtcNow, currentRun));
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(
            db, NullLogger<ExposureDerivationService>.Instance, new PostgresBulkExposureWriter(db));

        var result = await svc.DeriveForTenantAsync(TenantId, DateTimeOffset.UtcNow, currentRun, CancellationToken.None);

        result.Inserted.Should().Be(2);
        var exposures = await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters()
            .ToListAsync();
        exposures.Should().HaveCount(2);
        exposures.Select(e => e.DeviceId).Should().BeEquivalentTo(new[] { otherSourceDevice.Id, currentSourceDevice.Id });
        exposures.Should().OnlyContain(e => e.LastSeenRunId == currentRun);
    }

    /// <summary>
    /// Issue #83: on conflict the upsert must refresh the linkage columns, not freeze them
    /// at the original INSERT. When an exposure is re-derived from a *different* install
    /// (e.g. the originally-linked install was pruned and a sibling product now drives the
    /// same (device, vuln)), the row must re-point at the new install, product, version and
    /// match source — while keeping FirstObservedAt and bumping LastSeenRunId.
    /// </summary>
    [Fact]
    public async Task DeriveForTenantAsync_repoints_linkage_when_exposure_rederived_from_different_install()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        var productA = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var productB = SoftwareProduct.Create("Acme", "Gadget", "cpe:2.3:a:acme:gadget:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("nvd", "CVE-2026-CTE6", "t", "d", Severity.Critical, 9.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.AddRange(productA, productB);
        db.Vulnerabilities.Add(vuln);
        // Both products are applicable to the same vulnerability, so an install of either
        // produces an exposure on the same (device, vuln) conflict key.
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, productA.Id, null, true, null, null, null, null));
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, productB.Id, null, true, null, null, null, null));

        var source = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(source);
        var device = Device.Create(TenantId, source.Id, "dev-1", "Device", Criticality.Medium);
        db.Devices.Add(device);

        var firstRun = Guid.NewGuid();
        var installA = InstalledSoftware.Observe(TenantId, device.Id, productA.Id, source.Id, "1.0", DateTimeOffset.UtcNow, firstRun);
        db.InstalledSoftware.Add(installA);
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(
            db, NullLogger<ExposureDerivationService>.Instance, new PostgresBulkExposureWriter(db));

        var firstObservedAt = DateTimeOffset.UtcNow;
        await svc.DeriveForTenantAsync(TenantId, firstObservedAt, firstRun, CancellationToken.None);

        var initial = await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters().SingleAsync();
        initial.InstalledSoftwareId.Should().Be(installA.Id);
        initial.SoftwareProductId.Should().Be(productA.Id);
        initial.MatchedVersion.Should().Be("1.0");

        // The originally-linked install is pruned; a different product's install now drives
        // the same (device, vuln) exposure.
        db.InstalledSoftware.Remove(installA);
        var secondRun = Guid.NewGuid();
        var installB = InstalledSoftware.Observe(TenantId, device.Id, productB.Id, source.Id, "2.0", DateTimeOffset.UtcNow, secondRun);
        db.InstalledSoftware.Add(installB);
        await db.SaveChangesAsync();

        var secondObservedAt = firstObservedAt.AddHours(1);
        var result = await svc.DeriveForTenantAsync(TenantId, secondObservedAt, secondRun, CancellationToken.None);

        result.Inserted.Should().Be(0);
        result.Reobserved.Should().Be(1);
        result.Resolved.Should().Be(0);

        var refreshed = await db.DeviceVulnerabilityExposures.AsNoTracking().IgnoreQueryFilters().SingleAsync();
        refreshed.Id.Should().Be(initial.Id, "the same conflict row is updated, not replaced");
        refreshed.InstalledSoftwareId.Should().Be(installB.Id);
        refreshed.SoftwareProductId.Should().Be(productB.Id);
        refreshed.MatchedVersion.Should().Be("2.0");
        refreshed.MatchSource.Should().Be(ExposureMatchSource.Product);
        refreshed.FirstObservedAt.Should().BeCloseTo(firstObservedAt, TimeSpan.FromSeconds(1));
        refreshed.LastSeenRunId.Should().Be(secondRun);
    }
}
