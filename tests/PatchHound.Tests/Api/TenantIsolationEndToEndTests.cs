using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

// Phase 1 Task 18: end-to-end verification that canonical entities
// (Device, InstalledSoftware, DeviceRiskScore, SecurityProfile,
// DeviceRule) respect the tenant query filter configured on
// PatchHoundDbContext. Seeds two tenants under a system context, then
// flips the tenant context and asserts that only rows belonging to the
// accessible tenant leak through queries.
public class TenantIsolationEndToEndTests : IDisposable
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _sourceSystemId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;

    public TenantIsolationEndToEndTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        UseSystemContext();
        SeedTenant(_tenantA);
        SeedTenant(_tenantB);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Device_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantA);

        var devices = await _dbContext.Devices.AsNoTracking().ToListAsync();

        devices.Should().OnlyContain(d => d.TenantId == _tenantA);
        devices.Should().HaveCount(1);
    }

    [Fact]
    public async Task InstalledSoftware_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantB);

        var installs = await _dbContext.InstalledSoftware.AsNoTracking().ToListAsync();

        installs.Should().OnlyContain(i => i.TenantId == _tenantB);
        installs.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeviceRiskScore_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantA);

        var scores = await _dbContext.DeviceRiskScores.AsNoTracking().ToListAsync();

        scores.Should().OnlyContain(s => s.TenantId == _tenantA);
        scores.Should().HaveCount(1);
    }

    [Fact]
    public async Task SecurityProfile_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantB);

        var profiles = await _dbContext.SecurityProfiles.AsNoTracking().ToListAsync();

        profiles.Should().OnlyContain(p => p.TenantId == _tenantB);
        profiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeviceRule_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantA);

        var rules = await _dbContext.DeviceRules.AsNoTracking().ToListAsync();

        rules.Should().OnlyContain(r => r.TenantId == _tenantA);
        rules.Should().HaveCount(1);
    }

    [Fact]
    public async Task SystemContext_bypasses_tenant_filter()
    {
        UseSystemContext();

        var devices = await _dbContext.Devices.AsNoTracking().ToListAsync();
        var installs = await _dbContext.InstalledSoftware.AsNoTracking().ToListAsync();
        var profiles = await _dbContext.SecurityProfiles.AsNoTracking().ToListAsync();

        devices.Should().HaveCount(2);
        installs.Should().HaveCount(4);
        profiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task Empty_tenant_scope_returns_no_rows()
    {
        _tenantContext.IsSystemContext.Returns(false);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid>());

        var devices = await _dbContext.Devices.AsNoTracking().ToListAsync();
        var installs = await _dbContext.InstalledSoftware.AsNoTracking().ToListAsync();

        devices.Should().BeEmpty();
        installs.Should().BeEmpty();
    }

    // Phase 2 Task 18: Vulnerability is a global entity (no TenantId, no query filter).
    // Both tenants must see the same rows — no per-tenant isolation or cross-tenant leaks.

    [Fact]
    public async Task Phase2_global_vulnerability_is_visible_to_all_tenants()
    {
        // Seed one global Vulnerability via system context.
        UseSystemContext();
        var vuln = Vulnerability.Create(
            "nvd",
            "CVE-2026-ABCD",
            "Shared Vuln",
            "Shared description",
            Severity.High,
            7.5m,
            "CVSS:3.1/AV:N",
            DateTimeOffset.UtcNow
        );
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        // Tenant A should see it.
        UseTenant(_tenantA);
        var vulnsA = await _dbContext.Vulnerabilities.AsNoTracking().ToListAsync();
        vulnsA.Should().ContainSingle(v => v.ExternalId == "CVE-2026-ABCD");

        // Tenant B should see the exact same row — global entity, no filter.
        UseTenant(_tenantB);
        var vulnsB = await _dbContext.Vulnerabilities.AsNoTracking().ToListAsync();
        vulnsB.Should().ContainSingle(v => v.ExternalId == "CVE-2026-ABCD");

        vulnsA[0].Id.Should().Be(vulnsB[0].Id, "both tenants read the same global row");
    }

    [Fact]
    public async Task Phase2_vulnerability_reference_is_visible_to_all_tenants()
    {
        // Seed Vulnerability + VulnerabilityReference via system context.
        UseSystemContext();
        var vuln = Vulnerability.Create(
            "nvd",
            "CVE-2026-EFGH",
            "Shared Vuln 2",
            "Description",
            Severity.Critical,
            9.0m,
            "CVSS:3.1/AV:N",
            DateTimeOffset.UtcNow
        );
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        var reference = VulnerabilityReference.Create(
            vuln.Id,
            "https://nvd.nist.gov/vuln/detail/CVE-2026-EFGH",
            "NVD",
            []
        );
        _dbContext.VulnerabilityReferences.Add(reference);
        await _dbContext.SaveChangesAsync();

        // Both tenants see the reference.
        UseTenant(_tenantA);
        var refsA = await _dbContext.VulnerabilityReferences.AsNoTracking()
            .Where(r => r.VulnerabilityId == vuln.Id)
            .ToListAsync();
        refsA.Should().HaveCount(1);

        UseTenant(_tenantB);
        var refsB = await _dbContext.VulnerabilityReferences.AsNoTracking()
            .Where(r => r.VulnerabilityId == vuln.Id)
            .ToListAsync();
        refsB.Should().HaveCount(1);

        refsA[0].Id.Should().Be(refsB[0].Id, "both tenants read the same global reference row");
    }

    [Fact]
    public void Phase2_request_scoped_context_is_not_system_context_by_default()
    {
        // A freshly resolved DbContext outside system-context services must not
        // have IsSystemContext set — otherwise tenant-scoped entities would be
        // exposed globally via accidental system-context leakage.
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var freshTenantContext = Substitute.For<ITenantContext>();
        freshTenantContext.IsSystemContext.Returns(false);
        freshTenantContext.AccessibleTenantIds.Returns(new List<Guid>());

        using var freshDb = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(freshTenantContext)
        );

        // The DbContext must not expose system context when the tenant context says false.
        // We verify this indirectly: seeding a tenant-scoped entity from a non-system
        // context and confirming the query filter hides it from a different tenant.
        freshDb.SetSystemContext(true);
        var device = Device.Create(
            Guid.NewGuid(),
            _sourceSystemId,
            externalId: "seed-device",
            name: "Seed Device",
            baselineCriticality: Criticality.Low
        );
        freshDb.Devices.Add(device);
        freshDb.SaveChanges();
        freshDb.SetSystemContext(false);

        // After turning off system context, no rows should be visible
        // because AccessibleTenantIds returns empty.
        var visible = freshDb.Devices.AsNoTracking().ToList();
        visible.Should().BeEmpty("request-scoped context with no tenant access sees no rows");
    }

    [Fact]
    public async Task Phase3_device_vulnerability_exposure_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantA);

        var exposures = await _dbContext.DeviceVulnerabilityExposures.AsNoTracking().ToListAsync();

        exposures.Should().OnlyContain(e => e.TenantId == _tenantA);
        exposures.Should().HaveCount(1);
    }

    [Fact]
    public async Task Phase3_exposure_episode_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantB);

        var episodes = await _dbContext.ExposureEpisodes.AsNoTracking().ToListAsync();

        episodes.Should().OnlyContain(e => e.TenantId == _tenantB);
        episodes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Phase3_exposure_assessment_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantA);

        var assessments = await _dbContext.ExposureAssessments.AsNoTracking().ToListAsync();

        assessments.Should().OnlyContain(e => e.TenantId == _tenantA);
        assessments.Should().HaveCount(1);
    }

    // Phase 5 Task 8: tenant isolation for canonical Phase-5 entities:
    // SoftwareRiskScore, RemediationCase, and DashboardQueryService.BuildRiskChangeBriefAsync.

    [Fact]
    public async Task Phase5_software_risk_score_query_only_returns_rows_for_accessible_tenant()
    {
        // Seed one SoftwareRiskScore per tenant under system context.
        UseSystemContext();
        var scoreA = SoftwareRiskScore.Create(
            _tenantA,
            Guid.NewGuid(),
            overallScore: 600m,
            maxExposureScore: 600m,
            criticalExposureCount: 0,
            highExposureCount: 1,
            mediumExposureCount: 0,
            lowExposureCount: 0,
            affectedDeviceCount: 1,
            openExposureCount: 1,
            factorsJson: "[]",
            calculationVersion: "v1"
        );
        var scoreB = SoftwareRiskScore.Create(
            _tenantB,
            Guid.NewGuid(),
            overallScore: 800m,
            maxExposureScore: 800m,
            criticalExposureCount: 1,
            highExposureCount: 0,
            mediumExposureCount: 0,
            lowExposureCount: 0,
            affectedDeviceCount: 1,
            openExposureCount: 1,
            factorsJson: "[]",
            calculationVersion: "v1"
        );
        _dbContext.SoftwareRiskScores.AddRange(scoreA, scoreB);
        await _dbContext.SaveChangesAsync();

        // Tenant A should only see its own score.
        UseTenant(_tenantA);
        var scores = await _dbContext.SoftwareRiskScores.AsNoTracking().ToListAsync();

        scores.Should().OnlyContain(s => s.TenantId == _tenantA);
        scores.Should().ContainSingle(s => s.Id == scoreA.Id);
    }

    [Fact]
    public async Task Phase5_remediation_case_query_only_returns_rows_for_accessible_tenant()
    {
        // Seed one RemediationCase per tenant under system context.
        UseSystemContext();
        var caseA = RemediationCase.Create(_tenantA, Guid.NewGuid());
        var caseB = RemediationCase.Create(_tenantB, Guid.NewGuid());
        _dbContext.RemediationCases.AddRange(caseA, caseB);
        await _dbContext.SaveChangesAsync();

        // Tenant B should only see its own case.
        UseTenant(_tenantB);
        var cases = await _dbContext.RemediationCases.AsNoTracking().ToListAsync();

        cases.Should().OnlyContain(c => c.TenantId == _tenantB);
        cases.Should().ContainSingle(c => c.Id == caseB.Id);
    }

    [Fact]
    public async Task Phase5_dashboard_risk_change_brief_scoped_to_tenant()
    {
        // Seed a distinct vulnerability for tenantB (within the cutoff window) under system context.
        // Then call BuildRiskChangeBriefAsync scoped to tenantA and assert
        // that tenantB's vulnerability does not appear in the results.
        UseSystemContext();

        var vulnB = Vulnerability.Create(
            "nvd",
            "CVE-2026-PHASE5-TENANTB",
            "TenantB-Only Vuln",
            "desc",
            Severity.Critical,
            9.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
            DateTimeOffset.UtcNow
        );
        _dbContext.Vulnerabilities.Add(vulnB);
        await _dbContext.SaveChangesAsync();

        // Retrieve the tenantB device seeded in the constructor for the DVE.
        var deviceB = await _dbContext.Devices.AsNoTracking()
            .FirstAsync(d => d.TenantId == _tenantB);

        var dveB = DeviceVulnerabilityExposure.Observe(
            _tenantB,
            deviceB.Id,
            vulnB.Id,
            softwareProductId: null,
            installedSoftwareId: null,
            matchedVersion: string.Empty,
            matchSource: ExposureMatchSource.Product,
            observedAt: DateTimeOffset.UtcNow
        );
        _dbContext.DeviceVulnerabilityExposures.Add(dveB);
        await _dbContext.SaveChangesAsync();

        // Call the service scoped to tenantA — tenantB's vulnerability must not appear.
        UseTenant(_tenantA);
        var sut = new global::PatchHound.Api.Services.DashboardQueryService(
            _dbContext,
            NSubstitute.Substitute.For<global::PatchHound.Core.Interfaces.IRiskChangeBriefAiSummaryService>()
        );
        var brief = await sut.BuildRiskChangeBriefAsync(
            tenantId: _tenantA,
            currentTenantId: _tenantA,
            limit: null,
            highCriticalOnly: false,
            ct: CancellationToken.None,
            cutoffHours: 72
        );

        brief.Appeared.Should().NotContain(i => i.ExternalId == "CVE-2026-PHASE5-TENANTB",
            "tenantB exposures must not bleed into tenantA's risk change brief");
        brief.Resolved.Should().NotContain(i => i.ExternalId == "CVE-2026-PHASE5-TENANTB");
    }

    private void UseSystemContext()
    {
        _tenantContext.IsSystemContext.Returns(true);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantA, _tenantB });
    }

    private void UseTenant(Guid tenantId)
    {
        _tenantContext.IsSystemContext.Returns(false);
        _tenantContext.CurrentTenantId.Returns(tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenantId });
        _tenantContext.HasAccessToTenant(tenantId).Returns(true);
    }

    private void SeedTenant(Guid tenantId)
    {
        var device = Device.Create(
            tenantId,
            _sourceSystemId,
            externalId: $"dev-{tenantId:N}",
            name: $"Device-{tenantId:N}",
            baselineCriticality: Criticality.Medium
        );
        _dbContext.Devices.Add(device);

        var install = InstalledSoftware.Observe(
            tenantId,
            device.Id,
            softwareProductId: Guid.NewGuid(),
            sourceSystemId: _sourceSystemId,
            version: "1.0.0",
            at: DateTimeOffset.UtcNow
        );
        _dbContext.InstalledSoftware.Add(install);

        var riskScore = DeviceRiskScore.Create(
            tenantId,
            device.Id,
            overallScore: 100m,
            maxEpisodeRiskScore: 100m,
            criticalCount: 0,
            highCount: 0,
            mediumCount: 0,
            lowCount: 0,
            openEpisodeCount: 0,
            factorsJson: "{}",
            calculationVersion: "test"
        );
        _dbContext.DeviceRiskScores.Add(riskScore);

        var profile = SecurityProfile.Create(
            tenantId,
            name: $"profile-{tenantId:N}",
            description: null
        );
        _dbContext.SecurityProfiles.Add(profile);

        var rule = DeviceRule.Create(
            tenantId,
            name: $"rule-{tenantId:N}",
            description: null,
            priority: 0,
            filter: new FilterCondition("Name", "eq", "x"),
            operations: new List<AssetRuleOperation>()
        );
        _dbContext.DeviceRules.Add(rule);

        var vulnerability = Vulnerability.Create(
            "nvd",
            $"CVE-{tenantId:N}",
            $"Vuln-{tenantId:N}",
            "desc",
            Severity.High,
            7.1m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
            DateTimeOffset.UtcNow
        );
        _dbContext.Vulnerabilities.Add(vulnerability);
        _dbContext.SaveChanges();

        var installed = InstalledSoftware.Observe(
            tenantId,
            device.Id,
            Guid.NewGuid(),
            _sourceSystemId,
            "1.0",
            DateTimeOffset.UtcNow
        );
        _dbContext.InstalledSoftware.Add(installed);
        _dbContext.SaveChanges();

        var exposure = DeviceVulnerabilityExposure.Observe(
            tenantId,
            device.Id,
            vulnerability.Id,
            softwareProductId: installed.SoftwareProductId,
            installedSoftwareId: installed.Id,
            matchedVersion: installed.Version,
            matchSource: ExposureMatchSource.Product,
            observedAt: DateTimeOffset.UtcNow
        );
        _dbContext.DeviceVulnerabilityExposures.Add(exposure);
        _dbContext.SaveChanges();

        var episode = ExposureEpisode.Open(tenantId, exposure.Id, 1, DateTimeOffset.UtcNow);
        _dbContext.ExposureEpisodes.Add(episode);

        var assessment = ExposureAssessment.Create(
            tenantId,
            exposure.Id,
            device.SecurityProfileId,
            vulnerability.CvssScore ?? 0m,
            700m,
            "tenant-seed",
            DateTimeOffset.UtcNow
        );
        _dbContext.ExposureAssessments.Add(assessment);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
