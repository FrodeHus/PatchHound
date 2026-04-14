using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure.Services;

public class ExposureDerivationServiceTests
{
    [Fact]
    public async Task Derives_exposure_for_product_keyed_applicability()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);

        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("nvd", "CVE-2026-7001", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, softwareProductId: product.Id, cpeCriteria: null, vulnerable: true, null, null, null, null));

        var sourceSystem = SourceSystem.Create("test-source", "Test");
        db.SourceSystems.Add(sourceSystem);
        var device = Device.Create(tenantId, sourceSystem.Id, "dev-1", "Device 1", Criticality.Medium);
        db.Devices.Add(device);
        var installed = InstalledSoftware.Observe(tenantId, device.Id, product.Id, sourceSystem.Id, "1.2.3", DateTimeOffset.UtcNow);
        db.InstalledSoftware.Add(installed);
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);
        var result = await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();

        var exposures = await db.DeviceVulnerabilityExposures.ToListAsync();
        exposures.Should().ContainSingle();
        exposures[0].DeviceId.Should().Be(device.Id);
        exposures[0].VulnerabilityId.Should().Be(vuln.Id);
        exposures[0].SoftwareProductId.Should().Be(product.Id);
        exposures[0].MatchSource.Should().Be(ExposureMatchSource.Product);
        exposures[0].MatchedVersion.Should().Be("1.2.3");
        result.Inserted.Should().Be(1);
        result.Reobserved.Should().Be(0);
        result.Resolved.Should().Be(0);
    }

    [Fact]
    public async Task Re_deriving_same_state_reobserves_without_duplicating()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        await SeedProductKeyedExposureAsync(db, tenantId);

        var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow.AddMinutes(5), CancellationToken.None);
        await db.SaveChangesAsync();

        var exposures = await db.DeviceVulnerabilityExposures.ToListAsync();
        exposures.Should().ContainSingle();
        exposures[0].LastObservedAt.Should().BeAfter(exposures[0].FirstObservedAt);
    }

    [Fact]
    public async Task Resolves_exposure_when_installed_software_row_is_missing_on_next_derive()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        var (_, _, _, installed) = await SeedProductKeyedExposureAsync(db, tenantId);

        var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();

        db.InstalledSoftware.Remove(installed);
        await db.SaveChangesAsync();

        var resolvedAt = DateTimeOffset.UtcNow.AddHours(1);
        await svc.DeriveForTenantAsync(tenantId, resolvedAt, CancellationToken.None);
        await db.SaveChangesAsync();

        var exposures = await db.DeviceVulnerabilityExposures.ToListAsync();
        exposures.Should().ContainSingle();
        exposures[0].Status.Should().Be(ExposureStatus.Resolved);
        exposures[0].ResolvedAt.Should().Be(resolvedAt);
    }

    [Fact]
    public async Task Cpe_fallback_match_for_applicability_without_software_product_id()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("nvd", "CVE-2026-7002", "t", "d", Severity.Critical, 9.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, softwareProductId: null, cpeCriteria: "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*", vulnerable: true, null, null, null, null));

        var src = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(src);
        var device = Device.Create(tenantId, src.Id, "dev-1", "Device", Criticality.Medium);
        db.Devices.Add(device);
        db.InstalledSoftware.Add(InstalledSoftware.Observe(tenantId, device.Id, product.Id, src.Id, "1.0", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();

        var exposure = await db.DeviceVulnerabilityExposures.SingleAsync();
        exposure.MatchSource.Should().Be(ExposureMatchSource.Cpe);
    }

    [Fact]
    public async Task Re_running_ingestion_does_not_duplicate_exposures()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        await SeedProductKeyedExposureAsync(db, tenantId);

        var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);

        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);
        await db.SaveChangesAsync();
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow.AddMinutes(2), CancellationToken.None);
        await db.SaveChangesAsync();

        (await db.DeviceVulnerabilityExposures.ToListAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task Source_collision_same_external_id_different_source_produces_two_devices_and_two_exposures()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);

        var srcA = SourceSystem.Create("defender", "Defender");
        var srcB = SourceSystem.Create("tanium", "Tanium");
        db.SourceSystems.AddRange(srcA, srcB);

        var deviceA = Device.Create(tenantId, srcA.Id, "same-external-id", "Defender Device", Criticality.Medium);
        var deviceB = Device.Create(tenantId, srcB.Id, "same-external-id", "Tanium Device", Criticality.Medium);
        db.Devices.AddRange(deviceA, deviceB);

        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        db.SoftwareProducts.Add(product);
        var vuln = Vulnerability.Create("nvd", "CVE-2026-COLL", "t", "d", Severity.High, 7m, "v", DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(vuln.Id, product.Id, null, true, null, null, null, null));

        db.InstalledSoftware.Add(InstalledSoftware.Observe(tenantId, deviceA.Id, product.Id, srcA.Id, "1.0", DateTimeOffset.UtcNow));
        db.InstalledSoftware.Add(InstalledSoftware.Observe(tenantId, deviceB.Id, product.Id, srcB.Id, "1.0", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();

        var exposures = await db.DeviceVulnerabilityExposures.ToListAsync();
        exposures.Should().HaveCount(2);
        exposures.Select(e => e.DeviceId).Distinct().Should().HaveCount(2);
    }

    private static async Task<(SoftwareProduct Product, Vulnerability Vulnerability, Device Device, InstalledSoftware InstalledSoftware)> SeedProductKeyedExposureAsync(
        PatchHoundDbContext db,
        Guid tenantId)
    {
        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("nvd", "CVE-2026-SEED", "t", "d", Severity.High, 7m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, product.Id, null, true, null, null, null, null));

        var src = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(src);
        var device = Device.Create(tenantId, src.Id, "dev-seed", "Seed", Criticality.Medium);
        db.Devices.Add(device);
        var installed = InstalledSoftware.Observe(tenantId, device.Id, product.Id, src.Id, "1.2.3", DateTimeOffset.UtcNow);
        db.InstalledSoftware.Add(installed);
        await db.SaveChangesAsync();
        return (product, vuln, device, installed);
    }

    private static async Task<PatchHoundDbContext> CreateTenantDbAsync(Guid tenantId)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(tenantId);
        tenantContext.AccessibleTenantIds.Returns([tenantId]);
        tenantContext.IsSystemContext.Returns(false);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
