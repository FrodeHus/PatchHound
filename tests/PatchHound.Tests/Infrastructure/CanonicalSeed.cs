using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.Infrastructure;

/// <summary>
/// Shared fixture that seeds a minimal canonical dataset for Phase-5 service tests.
/// Creates one tenant, two devices, two software products, two installations,
/// two open exposures (one critical, one high), and the matching assessments.
/// </summary>
internal sealed class CanonicalSeed
{
    private static readonly Guid DefaultSourceSystemId = Guid.NewGuid();

    public Guid TenantId { get; }
    public SoftwareProduct ProductA { get; }
    public SoftwareProduct ProductB { get; }
    public Device DeviceA { get; }
    public Device DeviceB { get; }
    public InstalledSoftware InstallA { get; }
    public InstalledSoftware InstallB { get; }
    public DeviceVulnerabilityExposure ExposureA { get; }
    public DeviceVulnerabilityExposure ExposureB { get; }
    public ExposureAssessment AssessmentA { get; }
    public ExposureAssessment AssessmentB { get; }

    private CanonicalSeed(
        Guid tenantId,
        SoftwareProduct productA,
        SoftwareProduct productB,
        Device deviceA,
        Device deviceB,
        InstalledSoftware installA,
        InstalledSoftware installB,
        DeviceVulnerabilityExposure exposureA,
        DeviceVulnerabilityExposure exposureB,
        ExposureAssessment assessmentA,
        ExposureAssessment assessmentB)
    {
        TenantId = tenantId;
        ProductA = productA;
        ProductB = productB;
        DeviceA = deviceA;
        DeviceB = deviceB;
        InstallA = installA;
        InstallB = installB;
        ExposureA = exposureA;
        ExposureB = exposureB;
        AssessmentA = assessmentA;
        AssessmentB = assessmentB;
    }

    /// <summary>
    /// Builds the seed data and persists it into <paramref name="db"/>.
    /// The db context must be a tenant-scoped context for <see cref="TenantId"/>
    /// or a system context.
    /// </summary>
    public static async Task<CanonicalSeed> PlantAsync(PatchHoundDbContext db, Guid tenantId)
    {
        var productA = SoftwareProduct.Create("VendorA", "ProductA", null);
        var productB = SoftwareProduct.Create("VendorB", "ProductB", null);
        db.SoftwareProducts.AddRange(productA, productB);
        await db.SaveChangesAsync();

        var vuln1 = Vulnerability.Create("nvd", "CVE-2026-0001", "Critical vuln", "desc",
            Severity.Critical, 9.8m, null, DateTimeOffset.UtcNow);
        var vuln2 = Vulnerability.Create("nvd", "CVE-2026-0002", "High vuln", "desc",
            Severity.High, 7.5m, null, DateTimeOffset.UtcNow);
        db.Vulnerabilities.AddRange(vuln1, vuln2);
        await db.SaveChangesAsync();

        var deviceA = Device.Create(tenantId, DefaultSourceSystemId, "dev-a", "DeviceA", Criticality.High);
        var deviceB = Device.Create(tenantId, DefaultSourceSystemId, "dev-b", "DeviceB", Criticality.Medium);
        db.Devices.AddRange(deviceA, deviceB);
        await db.SaveChangesAsync();

        var installA = InstalledSoftware.Observe(tenantId, deviceA.Id, productA.Id, DefaultSourceSystemId, "1.0.0", DateTimeOffset.UtcNow);
        var installB = InstalledSoftware.Observe(tenantId, deviceB.Id, productB.Id, DefaultSourceSystemId, "2.0.0", DateTimeOffset.UtcNow);
        db.InstalledSoftware.AddRange(installA, installB);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var exposureA = DeviceVulnerabilityExposure.Observe(tenantId, deviceA.Id, vuln1.Id,
            productA.Id, installA.Id, "1.0.0", ExposureMatchSource.Product, now);
        var exposureB = DeviceVulnerabilityExposure.Observe(tenantId, deviceB.Id, vuln2.Id,
            productB.Id, installB.Id, "2.0.0", ExposureMatchSource.Product, now);
        db.DeviceVulnerabilityExposures.AddRange(exposureA, exposureB);
        await db.SaveChangesAsync();

        var assessmentA = ExposureAssessment.Create(tenantId, exposureA.Id, null,
            baseCvss: 9.8m, environmentalCvss: 9.5m, "Critical exposure", now);
        var assessmentB = ExposureAssessment.Create(tenantId, exposureB.Id, null,
            baseCvss: 7.5m, environmentalCvss: 7.0m, "High exposure", now);
        db.ExposureAssessments.AddRange(assessmentA, assessmentB);
        await db.SaveChangesAsync();

        return new CanonicalSeed(
            tenantId, productA, productB,
            deviceA, deviceB,
            installA, installB,
            exposureA, exposureB,
            assessmentA, assessmentB);
    }
}
