using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Bulk;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.Tests.TestData;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services;

/// <summary>
/// End-to-end safety net for the Postgres-native bulk ingestion pipeline introduced
/// in issue #76 (Tasks 2-8). Seeds a realistic-but-modest staged dataset (100 devices
/// × 5 software installs × 20 vulnerabilities → 2,000 exposures) and drives the full
/// post-staging pipeline through the real Postgres bulk writers:
///
///   1. StagedDeviceMergeService.MergeAsync                  (IBulkDeviceMergeWriter)
///   2. IngestionService.ProcessStagedResultsAsync           (IBulkExposureWriter + IBulkVulnerabilityReferenceWriter)
///   3. ExposureDerivationService.DeriveForTenantAsync       (CTE + IBulkExposureWriter)
///   4. ExposureEpisodeService.SyncEpisodesForTenantAsync    (run-scoped episode sync)
///   5. ExposureAssessmentService.AssessForTenantAsync
///   6. NormalizedSoftwareProjectionService.SyncTenantAsync  (IBulkSoftwareProjectionWriter)
///
/// The dataset size targets sub-30s runtime on a developer machine while still
/// stressing every bulk path. If everything still ties together end-to-end against
/// a real Postgres container, the seam-by-seam refactor is correct.
///
/// Note: this test calls the staged-pipeline stage methods directly rather than
/// going through <c>IngestionService.RunIngestionAsync</c>, because the latter is
/// dominated by lease/checkpoint orchestration and <c>IIngestionSource</c> fetch
/// loops that aren't part of the Task 2-8 surface area. The internal
/// <c>ProcessStagedResultsAsync</c> entry point is the closest stable seam to the
/// bulk-write paths under test.
/// </summary>
[Collection(PostgresCollection.Name)]
public class IngestionPipelineE2EBulkPathTests
{
    private const int DeviceCount = 100;
    private const int VulnerabilitiesPerSource = 20;
    private const int SoftwareInstallsPerDevice = 5;
    private const string SourceKey = "defender";

    // Must match PostgresFixture's default tenant id; otherwise EF query filters in
    // ExposureEpisodeService and friends silently filter out our seeded exposures.
    private static readonly Guid TenantId = Guid.Parse("00000001-0000-0000-0000-000000000001");

    private readonly PostgresFixture _fx;
    public IngestionPipelineE2EBulkPathTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task FullBulkPath_seeds_devices_software_vulnerabilities_and_exposures()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        // ── Seed: SourceSystem ────────────────────────────────────────────────
        var source = SourceSystem.Create(SourceKey, "Defender (E2E)");
        db.SourceSystems.Add(source);
        await db.SaveChangesAsync();

        // ── Seed: staged tables (deterministic) ──────────────────────────────
        var runId = Guid.NewGuid();
        var observedAt = DateTimeOffset.UtcNow;
        SeedStagedDataset(db, runId, observedAt);
        await db.SaveChangesAsync();

        // ── Wire: real Postgres bulk writers + services ───────────────────────
        var bulkDeviceMergeWriter = new PostgresBulkDeviceMergeWriter(db);
        var bulkExposureWriter = new PostgresBulkExposureWriter(db);
        var bulkVulnerabilityReferenceWriter = new PostgresBulkVulnerabilityReferenceWriter(db);
        var bulkSoftwareProjectionWriter = new PostgresBulkSoftwareProjectionWriter(db);

        var softwareResolver = new SoftwareProductResolver(db);
        var stagedDeviceMergeService = new StagedDeviceMergeService(
            db, softwareResolver, bulkDeviceMergeWriter);
        var vulnerabilityResolver = new VulnerabilityResolver(
            db, NullLogger<VulnerabilityResolver>.Instance);
        var exposureDerivationService = new ExposureDerivationService(
            db, NullLogger<ExposureDerivationService>.Instance, bulkExposureWriter);
        var exposureEpisodeService = new ExposureEpisodeService(db);
        var exposureAssessmentService = new ExposureAssessmentService(
            db, new EnvironmentalSeverityCalculator());
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            bulkSoftwareProjectionWriter);

        // IngestionService is needed only to invoke its internal ProcessStagedResultsAsync,
        // which is the seam that drives IBulkExposureWriter + IBulkVulnerabilityReferenceWriter.
        // All non-exercised dependencies are stubbed / minimally wired.
        var enrichmentEnqueuer = new EnrichmentJobEnqueuer(db, NullLogger<EnrichmentJobEnqueuer>.Instance);
        var inMemoryIngestionBulk = new InMemoryIngestionBulkWriter(db);
        var leaseManager = new IngestionLeaseManager(
            db, inMemoryIngestionBulk, NullLogger<IngestionLeaseManager>.Instance);
        var checkpointWriter = new IngestionCheckpointWriter(db);
        var stagingPipeline = new IngestionStagingPipeline(
            db, enrichmentEnqueuer, leaseManager, checkpointWriter);
        var snapshotLifecycle = new IngestionSnapshotLifecycle(db, inMemoryIngestionBulk);

        var ingestion = new IngestionService(
            db,
            Enumerable.Empty<IIngestionSource>(),
            enrichmentEnqueuer,
            stagedDeviceMergeService,
            Substitute.For<IStagedCloudApplicationMergeService>(),
            Substitute.For<IDeviceRuleEvaluationService>(),
            exposureDerivationService,
            exposureEpisodeService,
            exposureAssessmentService,
            new RiskScoreService(db, NullLogger<RiskScoreService>.Instance),
            vulnerabilityResolver,
            normalizedSoftwareProjectionService,
            remediationDecisionService: null,
            vulnerabilityAssessmentJobService: null,
            notificationService: null,
            leaseManager,
            checkpointWriter,
            stagingPipeline,
            snapshotLifecycle,
            inMemoryIngestionBulk,
            bulkExposureWriter,
            bulkVulnerabilityReferenceWriter,
            materializedViewRefreshService: null,
            NullLogger<IngestionService>.Instance);

        // ── Act 1: merge staged devices/software → canonical Device + InstalledSoftware
        var assetSummary = await ingestion.ProcessStagedAssetsAsync(
            runId, TenantId, SourceKey, snapshotId: null, CancellationToken.None);

        // ── Act 2: merge staged vulnerabilities + exposures
        var vulnSummary = await ingestion.ProcessStagedResultsAsync(
            runId, TenantId, SourceKey, snapshotId: null, "Defender (E2E)", CancellationToken.None);

        // ── Act 3: derive canonical exposures via CTE + bulk writer
        await exposureDerivationService.DeriveForTenantAsync(
            TenantId, observedAt, runId, CancellationToken.None);
        await db.SaveChangesAsync();

        // ── Act 4: episode sync (run-scoped)
        await exposureEpisodeService.SyncEpisodesForTenantAsync(
            TenantId, runId, observedAt, CancellationToken.None);
        await db.SaveChangesAsync();

        // ── Act 5: assess
        await exposureAssessmentService.AssessForTenantAsync(
            TenantId, observedAt, CancellationToken.None);
        await db.SaveChangesAsync();

        // ── Act 6: normalized software projection (snapshot-less so the writer prunes/inserts directly)
        await normalizedSoftwareProjectionService.SyncTenantAsync(
            TenantId, snapshotId: null, CancellationToken.None);

        // ── Assert ────────────────────────────────────────────────────────────
        assetSummary.PersistedMachineCount.Should().Be(DeviceCount,
            "every staged device must be merged into the canonical Devices table");

        var deviceCount = await db.Devices.IgnoreQueryFilters()
            .CountAsync(d => d.TenantId == TenantId);
        deviceCount.Should().Be(DeviceCount);

        var installedSoftwareCount = await db.InstalledSoftware.IgnoreQueryFilters()
            .CountAsync(i => i.TenantId == TenantId);
        installedSoftwareCount.Should().Be(DeviceCount * SoftwareInstallsPerDevice);

        var vulnerabilityCount = await db.Vulnerabilities.IgnoreQueryFilters()
            .CountAsync(v => v.Source == SourceKey);
        vulnerabilityCount.Should().Be(VulnerabilitiesPerSource);

        // Bulk-written exposures (DeviceVulnerabilityExposures) — both ProcessStagedResultsAsync
        // and the CTE derivation feed this table via IBulkExposureWriter.
        var exposureCount = await db.DeviceVulnerabilityExposures.IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == TenantId);
        exposureCount.Should().BeGreaterThan(0, "the bulk exposure writer must produce rows");

        // ExposureEpisodes — written by ExposureEpisodeService for each newly Open exposure.
        var episodeCount = await db.ExposureEpisodes.IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == TenantId);
        episodeCount.Should().BeGreaterThan(0, "open exposures must spawn episodes");

        // SoftwareTenantRecords + SoftwareProductInstallations — set-based projection from
        // PostgresBulkSoftwareProjectionWriter.
        var tenantSoftwareCount = await db.SoftwareTenantRecords.IgnoreQueryFilters()
            .CountAsync(r => r.TenantId == TenantId);
        tenantSoftwareCount.Should().BeGreaterThan(0,
            "the bulk software projection writer must populate SoftwareTenantRecords");

        var productInstallationsCount = await db.SoftwareProductInstallations.IgnoreQueryFilters()
            .CountAsync(p => p.TenantId == TenantId);
        productInstallationsCount.Should().BeGreaterThan(0,
            "the bulk software projection writer must populate SoftwareProductInstallations");

        // VulnerabilityReferences — written by PostgresBulkVulnerabilityReferenceWriter for
        // every staged vulnerability that carried references in its payload.
        var referenceCount = await db.VulnerabilityReferences.IgnoreQueryFilters().CountAsync();
        referenceCount.Should().BeGreaterThan(0,
            "the bulk vulnerability reference writer must produce rows");
    }

    private static void SeedStagedDataset(
        PatchHoundDbContext db,
        Guid runId,
        DateTimeOffset observedAt)
    {
        var stagedAt = observedAt;

        // Pre-generate a deterministic catalog of (vendor, product, version) tuples.
        // SoftwareInstallsPerDevice tuples per device → DeviceCount * SoftwareInstallsPerDevice
        // distinct software external IDs in total (one canonical SoftwareProduct per).
        var softwareCatalog = new List<(string ExternalId, string Vendor, string Product, string Version)>();
        for (var i = 0; i < SoftwareInstallsPerDevice; i++)
        {
            softwareCatalog.Add((
                ExternalId: $"sw-{i:D3}",
                Vendor: $"Vendor{i % 5}",
                Product: $"Product{i}",
                Version: $"{(i % 4) + 1}.0.{i}"));
        }

        // Pre-generate a deterministic vulnerability catalog.
        var vulnCatalog = new List<(string ExternalId, string Title, Severity Severity, decimal Cvss)>();
        for (var v = 0; v < VulnerabilitiesPerSource; v++)
        {
            var severity = (Severity)((v % 4) + 1); // Low, Medium, High, Critical
            vulnCatalog.Add((
                ExternalId: $"CVE-2026-E2E{v:D4}",
                Title: $"Synthetic E2E Vulnerability {v}",
                Severity: severity,
                Cvss: 5.0m + (v % 5)));
        }

        // Stage software assets (one StagedDevice row per software entry, AssetType.Software).
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
                tenantId: TenantId,
                sourceKey: SourceKey,
                externalId: sw.ExternalId,
                name: softwareAsset.Name,
                assetType: AssetType.Software,
                payloadJson: JsonSerializer.Serialize(softwareAsset),
                stagedAt: stagedAt));
        }

        // Stage devices + their software links.
        for (var d = 0; d < DeviceCount; d++)
        {
            var deviceExternalId = $"dev-{d:D4}";
            var deviceName = $"host-{d:D4}";
            var deviceAsset = new IngestionAsset(
                ExternalId: deviceExternalId,
                Name: deviceName,
                AssetType: AssetType.Device,
                Description: null,
                DeviceComputerDnsName: $"{deviceName}.e2e.local",
                DeviceHealthStatus: "Active",
                DeviceOsPlatform: "Windows11",
                DeviceOsVersion: "10.0.22631",
                DeviceRiskScore: "Medium",
                DeviceLastSeenAt: observedAt.AddMinutes(-5),
                DeviceLastIpAddress: "10.0.0.5",
                DeviceAadDeviceId: $"aad-{d:D4}");
            db.StagedDevices.Add(StagedDevice.Create(
                ingestionRunId: runId,
                tenantId: TenantId,
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
                        tenantId: TenantId,
                        sourceKey: SourceKey,
                        deviceExternalId: deviceExternalId,
                        softwareExternalId: sw.ExternalId,
                        observedAt: link.ObservedAt,
                        payloadJson: JsonSerializer.Serialize(link),
                        stagedAt: stagedAt));
            }
        }

        // Stage vulnerabilities (one per vulnCatalog entry) + matching exposures
        // (one row per (device, vulnerability, vulnerable-software) — bounded to keep
        // the test fast: each vuln targets ONE entry from the software catalog and
        // applies to all DeviceCount devices).
        for (var v = 0; v < vulnCatalog.Count; v++)
        {
            var vuln = vulnCatalog[v];
            var targetSoftware = softwareCatalog[v % softwareCatalog.Count];

            var vulnPayload = new IngestionResult(
                ExternalId: vuln.ExternalId,
                Title: vuln.Title,
                Description: $"E2E synthetic vulnerability {v}",
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
                tenantId: TenantId,
                sourceKey: SourceKey,
                externalId: vuln.ExternalId,
                title: vuln.Title,
                vendorSeverity: vuln.Severity,
                payloadJson: JsonSerializer.Serialize(vulnPayload),
                stagedAt: stagedAt));

            for (var d = 0; d < DeviceCount; d++)
            {
                var deviceExternalId = $"dev-{d:D4}";
                var deviceName = $"host-{d:D4}";
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
                        tenantId: TenantId,
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
