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
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure.Services;

public class IngestionServicePhase3Tests
{
    [Fact]
    public async Task RunExposureDerivationAsync_produces_canonical_exposures_for_tenant()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);

        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var vulnerability = Vulnerability.Create("nvd", "CVE-2026-PIPE", "t", "d", Severity.High, 7.5m, "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:L/I:L/A:L", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vulnerability);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vulnerability.Id, product.Id, null, true, null, null, null, null));

        var profile = SecurityProfile.Create(
            tenantId,
            "Internet-facing",
            null,
            EnvironmentClass.Server,
            InternetReachability.Internet,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High);
        db.SecurityProfiles.Add(profile);

        var source = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(source);
        var device = Device.Create(tenantId, source.Id, "dev-1", "Device 1", Criticality.High);
        device.AssignSecurityProfile(profile.Id);
        db.Devices.Add(device);
        db.InstalledSoftware.Add(InstalledSoftware.Observe(tenantId, device.Id, product.Id, source.Id, "1.0", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var ingestion = new IngestionService(
            db,
            Enumerable.Empty<IVulnerabilitySource>(),
            new EnrichmentJobEnqueuer(db, NullLogger<EnrichmentJobEnqueuer>.Instance),
            Substitute.For<IStagedDeviceMergeService>(),
            Substitute.For<IDeviceRuleEvaluationService>(),
            new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance),
            new ExposureEpisodeService(db),
            new ExposureAssessmentService(db, new EnvironmentalSeverityCalculator()),
            new RiskScoreService(db, NullLogger<RiskScoreService>.Instance),
            new VulnerabilityResolver(db),
            NullLogger<IngestionService>.Instance);

        await ingestion.RunExposureDerivationAsync(tenantId, CancellationToken.None);

        (await db.DeviceVulnerabilityExposures.ToListAsync()).Should().NotBeEmpty();
        (await db.ExposureEpisodes.ToListAsync()).Should().NotBeEmpty();
        (await db.ExposureAssessments.ToListAsync()).Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that when a <see cref="StagedVulnerabilityExposure"/> carries
    /// ProductVendor/ProductName in its payload and a matching <see cref="InstalledSoftware"/>
    /// row exists for the device, <see cref="ProcessStagedResultsAsync"/> populates
    /// <c>SoftwareProductId</c> and <c>InstalledSoftwareId</c> on the resulting
    /// <see cref="DeviceVulnerabilityExposure"/> instead of leaving them null.
    /// </summary>
    [Fact]
    public async Task ProcessStagedResultsAsync_links_exposure_to_software_product_when_installed_software_exists()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);

        // Seed canonical entities
        var product = SoftwareProduct.Create("Microsoft", "Edge", "cpe:2.3:a:microsoft:edge:*:*:*:*:*:*:*:*");
        db.SoftwareProducts.Add(product);

        var vuln = Vulnerability.Create(
            "microsoft-defender", "CVE-2026-LINK", "Edge vuln", "desc",
            Severity.High, 7.5m, null, DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, product.Id, null, true, null, null, null, null));

        var sourceSystem = SourceSystem.Create("microsoft-defender", "Defender");
        db.SourceSystems.Add(sourceSystem);

        var device = Device.Create(tenantId, sourceSystem.Id, "machine-abc", "Machine ABC", Criticality.Medium);
        db.Devices.Add(device);

        var installed = InstalledSoftware.Observe(
            tenantId, device.Id, product.Id, sourceSystem.Id, "120.0", DateTimeOffset.UtcNow);
        db.InstalledSoftware.Add(installed);

        var run = IngestionRun.Start(tenantId, "microsoft-defender", DateTimeOffset.UtcNow);
        db.IngestionRuns.Add(run);

        // Stage vulnerability + exposure (mimics what DefenderVulnerabilitySource produces)
        db.StagedVulnerabilities.Add(StagedVulnerability.Create(
            run.Id, tenantId, "microsoft-defender",
            externalId: "CVE-2026-LINK",
            title: "Edge vuln",
            vendorSeverity: Severity.High,
            payloadJson: "{}",
            stagedAt: DateTimeOffset.UtcNow));

        var affectedAssetPayload = new IngestionAffectedAsset(
            ExternalAssetId: "machine-abc",
            AssetName: "Machine ABC",
            AssetType: AssetType.Device,
            ProductVendor: "Microsoft",
            ProductName: "Edge",
            ProductVersion: "120.0");

        db.StagedVulnerabilityExposures.Add(StagedVulnerabilityExposure.Create(
            run.Id, tenantId, "microsoft-defender",
            vulnerabilityExternalId: "CVE-2026-LINK",
            assetExternalId: "machine-abc",
            assetName: "Machine ABC",
            assetType: AssetType.Device,
            payloadJson: JsonSerializer.Serialize(affectedAssetPayload),
            stagedAt: DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();

        var ingestion = CreateIngestionService(db);

        await ingestion.ProcessStagedResultsAsync(
            run.Id, tenantId, "microsoft-defender", snapshotId: null, "Defender", CancellationToken.None);

        var exposures = await db.DeviceVulnerabilityExposures.ToListAsync();
        exposures.Should().ContainSingle();
        exposures[0].VulnerabilityId.Should().Be(vuln.Id);
        exposures[0].DeviceId.Should().Be(device.Id);
        exposures[0].SoftwareProductId.Should().Be(product.Id,
            "the exposure must be linked to the matching SoftwareProduct");
        exposures[0].InstalledSoftwareId.Should().Be(installed.Id,
            "the exposure must be linked to the matching InstalledSoftware row");
    }

    /// <summary>
    /// When no <see cref="InstalledSoftware"/> row exists for the device+product,
    /// the exposure should still be created — just without software linkage.
    /// </summary>
    [Fact]
    public async Task ProcessStagedResultsAsync_creates_exposure_without_software_link_when_no_installed_software()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);

        var vuln = Vulnerability.Create(
            "microsoft-defender", "CVE-2026-NOLINK", "t", "d",
            Severity.Medium, 5.0m, null, DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(vuln);

        var sourceSystem = SourceSystem.Create("microsoft-defender", "Defender");
        db.SourceSystems.Add(sourceSystem);

        var device = Device.Create(tenantId, sourceSystem.Id, "machine-xyz", "Machine XYZ", Criticality.Low);
        db.Devices.Add(device);

        var run = IngestionRun.Start(tenantId, "microsoft-defender", DateTimeOffset.UtcNow);
        db.IngestionRuns.Add(run);

        db.StagedVulnerabilities.Add(StagedVulnerability.Create(
            run.Id, tenantId, "microsoft-defender",
            "CVE-2026-NOLINK", "t", Severity.Medium, "{}", DateTimeOffset.UtcNow));

        var affectedAssetPayload = new IngestionAffectedAsset(
            ExternalAssetId: "machine-xyz",
            AssetName: "Machine XYZ",
            AssetType: AssetType.Device,
            ProductVendor: "Unknown",
            ProductName: "SomeApp");

        db.StagedVulnerabilityExposures.Add(StagedVulnerabilityExposure.Create(
            run.Id, tenantId, "microsoft-defender",
            "CVE-2026-NOLINK", "machine-xyz", "Machine XYZ",
            AssetType.Device,
            JsonSerializer.Serialize(affectedAssetPayload),
            DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();

        var ingestion = CreateIngestionService(db);

        await ingestion.ProcessStagedResultsAsync(
            run.Id, tenantId, "microsoft-defender", snapshotId: null, "Defender", CancellationToken.None);

        var exposures = await db.DeviceVulnerabilityExposures.ToListAsync();
        exposures.Should().ContainSingle();
        exposures[0].SoftwareProductId.Should().BeNull(
            "no InstalledSoftware exists for this device+product so linkage should be absent");
        exposures[0].InstalledSoftwareId.Should().BeNull();
    }

    private static IngestionService CreateIngestionService(PatchHoundDbContext db) =>
        new(
            db,
            Enumerable.Empty<IVulnerabilitySource>(),
            new EnrichmentJobEnqueuer(db, NullLogger<EnrichmentJobEnqueuer>.Instance),
            Substitute.For<IStagedDeviceMergeService>(),
            Substitute.For<IDeviceRuleEvaluationService>(),
            new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance),
            new ExposureEpisodeService(db),
            new ExposureAssessmentService(db, new EnvironmentalSeverityCalculator()),
            new RiskScoreService(db, NullLogger<RiskScoreService>.Instance),
            new VulnerabilityResolver(db),
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
