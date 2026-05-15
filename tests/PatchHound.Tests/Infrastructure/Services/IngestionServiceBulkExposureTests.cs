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
using PatchHound.Infrastructure.Tenants;
using PatchHound.Tests.TestData;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services;

public class IngestionServiceBulkExposureTests
{
    [Fact]
    public async Task ProcessStagedResultsAsync_routes_exposure_upserts_through_bulk_writer()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);

        var vuln = Vulnerability.Create(
            "microsoft-defender", "CVE-2026-BULK", "Bulk vuln", "desc",
            Severity.High, 7.5m, null, DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(vuln);

        var sourceSystem = SourceSystem.Create("microsoft-defender", "Defender");
        db.SourceSystems.Add(sourceSystem);

        var device = Device.Create(tenantId, sourceSystem.Id, "machine-bulk", "Machine Bulk", Criticality.Medium);
        db.Devices.Add(device);

        var run = IngestionRun.Start(tenantId, "microsoft-defender", DateTimeOffset.UtcNow);
        db.IngestionRuns.Add(run);

        db.StagedVulnerabilities.Add(StagedVulnerability.Create(
            run.Id, tenantId, "microsoft-defender",
            externalId: "CVE-2026-BULK",
            title: "Bulk vuln",
            vendorSeverity: Severity.High,
            payloadJson: "{}",
            stagedAt: DateTimeOffset.UtcNow));

        db.StagedVulnerabilityExposures.Add(StagedVulnerabilityExposure.Create(
            run.Id, tenantId, "microsoft-defender",
            vulnerabilityExternalId: "CVE-2026-BULK",
            assetExternalId: "machine-bulk",
            assetName: "Machine Bulk",
            assetType: AssetType.Device,
            payloadJson: "{}",
            stagedAt: DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();

        var bulkExposureWriter = Substitute.For<IBulkExposureWriter>();
        bulkExposureWriter.UpsertAsync(
                Arg.Any<IReadOnlyCollection<ExposureUpsertRow>>(),
                Arg.Any<CancellationToken>())
            .Returns(new BulkExposureUpsertResult(1, 0));

        var ingestion = CreateIngestionService(db, bulkExposureWriter);

        await ingestion.ProcessStagedResultsAsync(
            run.Id, tenantId, "microsoft-defender", snapshotId: null, "Defender", CancellationToken.None);

        await bulkExposureWriter.Received(1).UpsertAsync(
            Arg.Is<IReadOnlyCollection<ExposureUpsertRow>>(rows =>
                rows.Count == 1
                && rows.First().DeviceId == device.Id
                && rows.First().VulnerabilityId == vuln.Id
                && rows.First().RunId == run.Id
                && rows.First().TenantId == tenantId),
            Arg.Any<CancellationToken>());
    }

    private static IngestionService CreateIngestionService(
        PatchHoundDbContext db,
        IBulkExposureWriter bulkExposureWriter) =>
        new(
            db,
            Enumerable.Empty<IIngestionSource>(),
            new EnrichmentJobEnqueuer(db, NullLogger<EnrichmentJobEnqueuer>.Instance),
            Substitute.For<IStagedDeviceMergeService>(),
            Substitute.For<IStagedCloudApplicationMergeService>(),
            Substitute.For<IDeviceRuleEvaluationService>(),
            new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance),
            new ExposureEpisodeService(db),
            new ExposureAssessmentService(db, new EnvironmentalSeverityCalculator()),
            new RiskScoreService(db, NullLogger<RiskScoreService>.Instance),
            new VulnerabilityResolver(db, NullLogger<VulnerabilityResolver>.Instance),
            normalizedSoftwareProjectionService: null,
            remediationDecisionService: null,
            vulnerabilityAssessmentJobService: null,
            notificationService: null,
            new IngestionLeaseManager(db, new InMemoryIngestionBulkWriter(db), NullLogger<IngestionLeaseManager>.Instance),
            new IngestionCheckpointWriter(db),
            new IngestionStagingPipeline(db, new EnrichmentJobEnqueuer(db, NullLogger<EnrichmentJobEnqueuer>.Instance), new IngestionLeaseManager(db, new InMemoryIngestionBulkWriter(db), NullLogger<IngestionLeaseManager>.Instance), new IngestionCheckpointWriter(db)),
            new IngestionSnapshotLifecycle(db, new InMemoryIngestionBulkWriter(db)),
            new InMemoryIngestionBulkWriter(db),
            bulkExposureWriter,
            materializedViewRefreshService: null,
            NullLogger<IngestionService>.Instance);

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
