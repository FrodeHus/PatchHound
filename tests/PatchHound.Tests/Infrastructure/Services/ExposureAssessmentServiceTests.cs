using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure.Services;

public class ExposureAssessmentServiceTests
{
    [Fact]
    public async Task Assessment_uses_security_profile_environmental_modifiers()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        var profile = SecurityProfile.Create(
            tenantId,
            "Internet-facing",
            "critical perimeter",
            EnvironmentClass.Server,
            InternetReachability.Internet,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High);
        db.SecurityProfiles.Add(profile);

        var src = SourceSystem.Create("test", "Test");
        db.SourceSystems.Add(src);
        var device = Device.Create(tenantId, src.Id, "dev-1", "Device", Criticality.Medium);
        device.AssignSecurityProfile(profile.Id);
        db.Devices.Add(device);

        var product = SoftwareProduct.Create("Acme", "Widget", null);
        db.SoftwareProducts.Add(product);
        var vuln = Vulnerability.Create(
            "nvd",
            "CVE-2026-ENV",
            "t",
            "d",
            Severity.Medium,
            5.0m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:L/I:L/A:L",
            DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(vuln);

        var installed = InstalledSoftware.Observe(tenantId, device.Id, product.Id, src.Id, "1.0", DateTimeOffset.UtcNow);
        db.InstalledSoftware.Add(installed);
        var exposure = DeviceVulnerabilityExposure.Observe(
            tenantId, device.Id, vuln.Id, product.Id, installed.Id, "1.0", ExposureMatchSource.Product, DateTimeOffset.UtcNow);
        db.DeviceVulnerabilityExposures.Add(exposure);
        await db.SaveChangesAsync();

        var svc = new ExposureAssessmentService(db, new EnvironmentalSeverityCalculator());
        await svc.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();

        var assessment = await db.ExposureAssessments.SingleAsync(a => a.DeviceVulnerabilityExposureId == exposure.Id);
        assessment.SecurityProfileId.Should().Be(profile.Id);
        assessment.BaseCvss.Should().Be(5.0m);
        assessment.EnvironmentalCvss.Should().NotBe(5.0m);
        assessment.Reason.Should().NotBeNullOrWhiteSpace();
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
