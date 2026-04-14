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
            NullLogger<IngestionService>.Instance);

        await ingestion.RunExposureDerivationAsync(tenantId, CancellationToken.None);

        (await db.DeviceVulnerabilityExposures.ToListAsync()).Should().NotBeEmpty();
        (await db.ExposureEpisodes.ToListAsync()).Should().NotBeEmpty();
        (await db.ExposureAssessments.ToListAsync()).Should().NotBeEmpty();
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
