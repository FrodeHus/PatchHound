using System.Text.Json;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.IngestionBenchmark;

/// <summary>
/// Seeds the staged-ingestion tables (StagedDevices for both Device and Software
/// asset types, StagedDeviceSoftwareInstallations linking them, StagedVulnerabilities,
/// and StagedVulnerabilityExposures) with deterministic external IDs so re-runs
/// exercise UPSERT/reobserve paths.
/// </summary>
public static class BenchmarkSeeder
{
    public const string SourceKey = "benchmark";

    public static async Task EnsureSourceSystemAsync(
        PatchHoundDbContext db,
        CancellationToken ct)
    {
        if (!db.SourceSystems.Any(s => s.Key == SourceKey))
        {
            db.SourceSystems.Add(SourceSystem.Create(SourceKey, "Benchmark Synthetic Source"));
            await db.SaveChangesAsync(ct);
        }
    }

    public static void Seed(
        PatchHoundDbContext db,
        Guid tenantId,
        Guid runId,
        DateTimeOffset observedAt,
        BenchmarkOptions opts)
    {
        var stagedAt = observedAt;

        var softwareCatalog = new List<(string ExternalId, string Vendor, string Product, string Version)>();
        for (var i = 0; i < opts.SoftwarePerDevice; i++)
        {
            softwareCatalog.Add((
                ExternalId: $"sw-{i:D3}",
                Vendor: $"Vendor{i % 5}",
                Product: $"Product{i}",
                Version: $"{(i % 4) + 1}.0.{i}"));
        }

        var vulnCatalog = new List<(string ExternalId, string Title, Severity Severity, decimal Cvss)>();
        for (var v = 0; v < opts.VulnsPerDevice; v++)
        {
            var severity = (Severity)((v % 4) + 1);
            vulnCatalog.Add((
                ExternalId: $"CVE-2026-BENCH{v:D4}",
                Title: $"Synthetic Benchmark Vulnerability {v}",
                Severity: severity,
                Cvss: 5.0m + (v % 5)));
        }

        // Software assets — one StagedDevice row per software entry (AssetType.Software).
        foreach (var sw in softwareCatalog)
        {
            var softwareAsset = new IngestionAsset(
                ExternalId: sw.ExternalId,
                Name: $"{sw.Vendor} {sw.Product} {sw.Version}",
                AssetType: AssetType.Software,
                Description: null,
                Metadata: JsonSerializer.Serialize(new
                {
                    softwareId = sw.ExternalId,
                    name = sw.Product,
                    vendor = sw.Vendor,
                    version = sw.Version,
                    derivedFromSoftwareInventory = true,
                }));
            db.StagedDevices.Add(StagedDevice.Create(
                ingestionRunId: runId,
                tenantId: tenantId,
                sourceKey: SourceKey,
                externalId: sw.ExternalId,
                name: softwareAsset.Name,
                assetType: AssetType.Software,
                payloadJson: JsonSerializer.Serialize(softwareAsset),
                stagedAt: stagedAt));
        }

        // Devices + software links.
        for (var d = 0; d < opts.DevicesPerTenant; d++)
        {
            var deviceExternalId = $"dev-{d:D6}";
            var deviceName = $"host-{d:D6}";
            var deviceAsset = new IngestionAsset(
                ExternalId: deviceExternalId,
                Name: deviceName,
                AssetType: AssetType.Device,
                Description: null,
                DeviceComputerDnsName: $"{deviceName}.bench.local",
                DeviceHealthStatus: "Active",
                DeviceOsPlatform: "Windows11",
                DeviceOsVersion: "10.0.22631",
                DeviceRiskScore: "Medium",
                DeviceLastSeenAt: observedAt.AddMinutes(-5),
                DeviceLastIpAddress: "10.0.0.5",
                DeviceAadDeviceId: $"aad-{d:D6}");
            db.StagedDevices.Add(StagedDevice.Create(
                ingestionRunId: runId,
                tenantId: tenantId,
                sourceKey: SourceKey,
                externalId: deviceExternalId,
                name: deviceName,
                assetType: AssetType.Device,
                payloadJson: JsonSerializer.Serialize(deviceAsset),
                stagedAt: stagedAt));

            foreach (var sw in softwareCatalog)
            {
                var link = new IngestionDeviceSoftwareLink(
                    DeviceExternalId: deviceExternalId,
                    SoftwareExternalId: sw.ExternalId,
                    ObservedAt: observedAt);
                db.StagedDeviceSoftwareInstallations.Add(
                    StagedDeviceSoftwareInstallation.Create(
                        ingestionRunId: runId,
                        tenantId: tenantId,
                        sourceKey: SourceKey,
                        deviceExternalId: deviceExternalId,
                        softwareExternalId: sw.ExternalId,
                        observedAt: link.ObservedAt,
                        payloadJson: JsonSerializer.Serialize(link),
                        stagedAt: stagedAt));
            }
        }

        // Vulnerabilities + exposures (one exposure per (device, vuln)).
        for (var v = 0; v < vulnCatalog.Count; v++)
        {
            var vuln = vulnCatalog[v];
            var targetSoftware = softwareCatalog[v % softwareCatalog.Count];

            var vulnPayload = new IngestionResult(
                ExternalId: vuln.ExternalId,
                Title: vuln.Title,
                Description: $"Benchmark synthetic vulnerability {v}",
                VendorSeverity: vuln.Severity,
                CvssScore: vuln.Cvss,
                CvssVector: "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:L/I:L/A:L",
                PublishedDate: observedAt.AddDays(-30),
                AffectedAssets: Array.Empty<IngestionAffectedAsset>(),
                ProductVendor: targetSoftware.Vendor,
                ProductName: targetSoftware.Product,
                ProductVersion: targetSoftware.Version,
                References: new[]
                {
                    new IngestionReference($"https://example.com/{vuln.ExternalId}", "nvd", new[] { "advisory" }),
                });

            db.StagedVulnerabilities.Add(StagedVulnerability.Create(
                ingestionRunId: runId,
                tenantId: tenantId,
                sourceKey: SourceKey,
                externalId: vuln.ExternalId,
                title: vuln.Title,
                vendorSeverity: vuln.Severity,
                payloadJson: JsonSerializer.Serialize(vulnPayload),
                stagedAt: stagedAt));

            for (var d = 0; d < opts.DevicesPerTenant; d++)
            {
                var deviceExternalId = $"dev-{d:D6}";
                var deviceName = $"host-{d:D6}";
                var exposureAsset = new IngestionAffectedAsset(
                    ExternalAssetId: deviceExternalId,
                    AssetName: deviceName,
                    AssetType: AssetType.Device,
                    ProductVendor: targetSoftware.Vendor,
                    ProductName: targetSoftware.Product,
                    ProductVersion: targetSoftware.Version);
                db.StagedVulnerabilityExposures.Add(
                    StagedVulnerabilityExposure.Create(
                        ingestionRunId: runId,
                        tenantId: tenantId,
                        sourceKey: SourceKey,
                        vulnerabilityExternalId: vuln.ExternalId,
                        assetExternalId: deviceExternalId,
                        assetName: deviceName,
                        assetType: AssetType.Device,
                        payloadJson: JsonSerializer.Serialize(exposureAsset),
                        stagedAt: stagedAt));
            }
        }
    }
}
