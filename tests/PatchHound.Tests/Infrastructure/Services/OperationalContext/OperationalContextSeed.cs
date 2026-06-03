using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.Infrastructure.Services.OperationalContext;

/// <summary>
/// Seeds tenant-scoped operational-context fixtures for the DB-backed builder tests.
/// Inserts a single tenant's full graph (software product, remediation case, two active
/// healthy devices with open exposures + risk scores) plus a second-tenant leakage probe
/// for the same software product / vulnerability.
/// </summary>
internal static class OperationalContextSeed
{
    public sealed record Seed(
        Guid TenantId,
        Guid RemediationCaseId,
        Guid SoftwareProductId,
        Guid VulnerabilityId,
        Guid SourceSystemId);

    public static async Task<Seed> SeedRemediationCaseAsync(PatchHoundDbContext db)
    {
        var tenantId = Guid.NewGuid();

        var sourceSystem = SourceSystem.Create($"src-{Guid.NewGuid():N}", "Test Source");
        var product = SoftwareProduct.Create(vendor: "TestVendor", name: "TestProduct", primaryCpe23Uri: null);
        var vulnerability = Vulnerability.Create(
            source: "nvd",
            externalId: $"CVE-{Guid.NewGuid():N}".Substring(0, 16),
            title: "Test vuln",
            description: "desc",
            vendorSeverity: Severity.Critical,
            cvssScore: 9.8m,
            cvssVector: null,
            publishedDate: DateTimeOffset.UtcNow);

        db.AddRange(sourceSystem, product, vulnerability);
        await db.SaveChangesAsync();

        var deviceCritical = Device.Create(
            tenantId, sourceSystem.Id, externalId: $"dev-{Guid.NewGuid():N}",
            name: "host-critical", baselineCriticality: Criticality.Critical);
        var deviceHigh = Device.Create(
            tenantId, sourceSystem.Id, externalId: $"dev-{Guid.NewGuid():N}",
            name: "host-high", baselineCriticality: Criticality.High);
        MarkActiveHealthy(deviceCritical);
        MarkActiveHealthy(deviceHigh);

        db.AddRange(deviceCritical, deviceHigh);
        await db.SaveChangesAsync();

        var runId = Guid.NewGuid();
        var exposureCritical = DeviceVulnerabilityExposure.Observe(
            tenantId, deviceCritical.Id, vulnerability.Id, product.Id, null,
            "1.0.0", ExposureMatchSource.Product, DateTimeOffset.UtcNow, runId);
        var exposureHigh = DeviceVulnerabilityExposure.Observe(
            tenantId, deviceHigh.Id, vulnerability.Id, product.Id, null,
            "1.0.0", ExposureMatchSource.Product, DateTimeOffset.UtcNow, runId);

        var softwareRisk = SoftwareRiskScore.Create(
            tenantId, product.Id, overallScore: 880m, maxExposureScore: 950m,
            criticalExposureCount: 1, highExposureCount: 1, mediumExposureCount: 0,
            lowExposureCount: 0, affectedDeviceCount: 2, openExposureCount: 2,
            factorsJson: "[]", calculationVersion: "v1");

        var deviceRiskCritical = DeviceRiskScore.Create(
            tenantId, deviceCritical.Id, overallScore: 937m, maxEpisodeRiskScore: 950m,
            criticalCount: 1, highCount: 0, mediumCount: 0, lowCount: 0, openEpisodeCount: 1,
            factorsJson: "[]", calculationVersion: "v1");
        var deviceRiskHigh = DeviceRiskScore.Create(
            tenantId, deviceHigh.Id, overallScore: 700m, maxEpisodeRiskScore: 720m,
            criticalCount: 0, highCount: 1, mediumCount: 0, lowCount: 0, openEpisodeCount: 1,
            factorsJson: "[]", calculationVersion: "v1");

        var remediationCase = RemediationCase.Create(tenantId, product.Id);

        db.AddRange(exposureCritical, exposureHigh, softwareRisk, deviceRiskCritical, deviceRiskHigh, remediationCase);
        await db.SaveChangesAsync();

        return new Seed(tenantId, remediationCase.Id, product.Id, vulnerability.Id, sourceSystem.Id);
    }

    /// <summary>
    /// Persists a SECOND tenant's device + open exposure for the SAME software product and
    /// vulnerability. The fixture context behaves like a system context, so these rows really
    /// land in the DB; the service's explicit TenantId filter is what must exclude them.
    /// </summary>
    public static async Task SeedOtherTenantExposureForSameSoftwareAsync(PatchHoundDbContext db, Seed seed)
    {
        var otherTenantId = Guid.NewGuid();

        var otherDevice = Device.Create(
            otherTenantId, seed.SourceSystemId, externalId: $"dev-{Guid.NewGuid():N}",
            name: "host-other-tenant", baselineCriticality: Criticality.Critical);
        MarkActiveHealthy(otherDevice);

        db.Add(otherDevice);
        await db.SaveChangesAsync();

        var runId = Guid.NewGuid();
        var otherExposure = DeviceVulnerabilityExposure.Observe(
            otherTenantId, otherDevice.Id, seed.VulnerabilityId, seed.SoftwareProductId, null,
            "1.0.0", ExposureMatchSource.Product, DateTimeOffset.UtcNow, runId);

        var otherDeviceRisk = DeviceRiskScore.Create(
            otherTenantId, otherDevice.Id, overallScore: 999m, maxEpisodeRiskScore: 999m,
            criticalCount: 1, highCount: 0, mediumCount: 0, lowCount: 0, openEpisodeCount: 1,
            factorsJson: "[]", calculationVersion: "v1");

        var otherSoftwareRisk = SoftwareRiskScore.Create(
            otherTenantId, seed.SoftwareProductId, overallScore: 999m, maxExposureScore: 999m,
            criticalExposureCount: 1, highExposureCount: 0, mediumExposureCount: 0,
            lowExposureCount: 0, affectedDeviceCount: 1, openExposureCount: 1,
            factorsJson: "[]", calculationVersion: "v1");

        db.AddRange(otherExposure, otherDeviceRisk, otherSoftwareRisk);
        await db.SaveChangesAsync();
    }

    private static void MarkActiveHealthy(Device device) =>
        device.UpdateInventoryDetails(
            computerDnsName: device.Name,
            healthStatus: "Active",
            osPlatform: null,
            osVersion: null,
            externalRiskLabel: null,
            lastSeenAt: DateTimeOffset.UtcNow,
            lastIpAddress: null,
            aadDeviceId: null);
}
