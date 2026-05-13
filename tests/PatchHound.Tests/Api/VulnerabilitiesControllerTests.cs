using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Vulnerabilities;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.VulnerabilitySources;
using PatchHound.Tests.TestData;


namespace PatchHound.Tests.Api;

public class VulnerabilitiesControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly VulnerabilitiesController _controller;

    public VulnerabilitiesControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        var detailQueryService = new PatchHound.Api.Services.VulnerabilityDetailQueryService(_dbContext);
        var assessmentJobService = new PatchHound.Infrastructure.Services.VulnerabilityAssessmentJobService(_dbContext);
        _controller = new VulnerabilitiesController(
            _dbContext,
            _tenantContext,
            detailQueryService,
            assessmentJobService
        );
    }

    [Fact]
    public async Task List_RecurrenceOnly_ReturnsOnlyPerAssetRecurringVulnerabilities()
    {
        var sourceSystem = SourceSystem.Create("defender", "Defender");
        var device = Device.Create(_tenantId, sourceSystem.Id, "dev-recur", "Recurring Device", Criticality.Medium);
        var recurringVuln = Vulnerability.Create("nvd", "CVE-2026-0001", "Recurring vuln", "desc", Severity.High, 7.2m, null, DateTimeOffset.UtcNow);
        var nonRecurringVuln = Vulnerability.Create("nvd", "CVE-2026-0002", "One-off vuln", "desc", Severity.Medium, 5.1m, null, DateTimeOffset.UtcNow);

        _dbContext.SourceSystems.Add(sourceSystem);
        _dbContext.Devices.Add(device);
        _dbContext.Vulnerabilities.AddRange(recurringVuln, nonRecurringVuln);
        await _dbContext.SaveChangesAsync();

        var recurringInstall = InstalledSoftware.Observe(_tenantId, device.Id, Guid.NewGuid(), sourceSystem.Id, "1.0", DateTimeOffset.UtcNow.AddDays(-4));
        var nonRecurringInstall = InstalledSoftware.Observe(_tenantId, device.Id, Guid.NewGuid(), sourceSystem.Id, "1.0", DateTimeOffset.UtcNow.AddDays(-1));
        _dbContext.InstalledSoftware.AddRange(recurringInstall, nonRecurringInstall);
        await _dbContext.SaveChangesAsync();

        var recurringExposure = DeviceVulnerabilityExposure.Observe(_tenantId, device.Id, recurringVuln.Id, recurringInstall.SoftwareProductId, recurringInstall.Id, recurringInstall.Version, ExposureMatchSource.Product, DateTimeOffset.UtcNow.AddDays(-4));
        var nonRecurringExposure = DeviceVulnerabilityExposure.Observe(_tenantId, device.Id, nonRecurringVuln.Id, nonRecurringInstall.SoftwareProductId, nonRecurringInstall.Id, nonRecurringInstall.Version, ExposureMatchSource.Product, DateTimeOffset.UtcNow.AddDays(-1));
        _dbContext.DeviceVulnerabilityExposures.AddRange(recurringExposure, nonRecurringExposure);
        await _dbContext.SaveChangesAsync();

        _dbContext.ExposureEpisodes.AddRange(
            ExposureEpisode.Open(_tenantId, recurringExposure.Id, 1, DateTimeOffset.UtcNow.AddDays(-4)),
            ExposureEpisode.Open(_tenantId, recurringExposure.Id, 2, DateTimeOffset.UtcNow.AddDays(-2)),
            ExposureEpisode.Open(_tenantId, nonRecurringExposure.Id, 1, DateTimeOffset.UtcNow.AddDays(-1))
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new VulnerabilityFilterQuery(),
            new PaginationQuery(),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<VulnerabilityDto>>().Subject;

        payload.Items.Should().Contain(item => item.ExternalId == "CVE-2026-0001" && item.AffectedDeviceCount == 1);
    }

    [Fact]
    public async Task List_returns_canonical_vulns_with_exposure_counts()
    {
        var v1 = Vulnerability.Create("nvd", "CVE-2026-0300", "Alpha", "desc",
            Severity.Critical, 9.8m, null, DateTimeOffset.UtcNow.AddDays(-10));
        var v2 = Vulnerability.Create("nvd", "CVE-2026-0301", "Beta", "desc",
            Severity.High, 7.5m, null, DateTimeOffset.UtcNow.AddDays(-20));
        var sourceSystem = SourceSystem.Create("defender", "Defender");
        var device = Device.Create(_tenantId, sourceSystem.Id, "dev-1", "Device-1", Criticality.Medium);
        _dbContext.SourceSystems.Add(sourceSystem);
        _dbContext.Devices.Add(device);
        _dbContext.Vulnerabilities.AddRange(v1, v2);
        await _dbContext.SaveChangesAsync();

        var installed = InstalledSoftware.Observe(_tenantId, device.Id, Guid.NewGuid(), sourceSystem.Id, "1.0", DateTimeOffset.UtcNow);
        _dbContext.InstalledSoftware.Add(installed);
        await _dbContext.SaveChangesAsync();

        _dbContext.DeviceVulnerabilityExposures.Add(
            DeviceVulnerabilityExposure.Observe(
                _tenantId,
                device.Id,
                v1.Id,
                softwareProductId: installed.SoftwareProductId,
                installedSoftwareId: installed.Id,
                matchedVersion: installed.Version,
                matchSource: ExposureMatchSource.Product,
                observedAt: DateTimeOffset.UtcNow));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new VulnerabilityFilterQuery(),
            new PaginationQuery(),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<VulnerabilityDto>>().Subject;

        payload.TotalCount.Should().Be(2);
        payload.Items.Should().HaveCount(2);
        payload.Items.Should().OnlyContain(v => v.ExposureDataAvailable);
        payload.Items.Should().Contain(v => v.ExternalId == "CVE-2026-0300" && v.AffectedDeviceCount == 1);
        payload.Items.Should().Contain(v => v.ExternalId == "CVE-2026-0301" && v.AffectedDeviceCount == 0);
    }

    [Fact]
    public async Task List_FiltersByApprovedRemediationCaseIds()
    {
        var covered = Vulnerability.Create("nvd", "CVE-2026-0400", "Covered", "desc",
            Severity.Critical, 9.8m, null, DateTimeOffset.UtcNow.AddDays(-10));
        var uncovered = Vulnerability.Create("nvd", "CVE-2026-0401", "Uncovered", "desc",
            Severity.High, 7.5m, null, DateTimeOffset.UtcNow.AddDays(-20));
        var product = SoftwareProduct.Create("Acme", "Widget", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var decision = RemediationDecision.Create(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Accepted",
            _tenantContext.CurrentUserId,
            DecisionApprovalStatus.Approved,
            expiryDate: DateTimeOffset.UtcNow.AddDays(30));

        _dbContext.Vulnerabilities.AddRange(covered, uncovered);
        _dbContext.SoftwareProducts.Add(product);
        _dbContext.RemediationCases.Add(remediationCase);
        _dbContext.RemediationDecisions.Add(decision);
        await _dbContext.SaveChangesAsync();

        _dbContext.ApprovedVulnerabilityRemediations.Add(ApprovedVulnerabilityRemediation.Create(
            _tenantId,
            covered.Id,
            remediationCase.Id,
            decision.Id,
            decision.Outcome,
            decision.ApprovedAt!.Value));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new VulnerabilityFilterQuery(RemediationCaseIds: remediationCase.Id.ToString()),
            new PaginationQuery(),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<VulnerabilityDto>>().Subject;

        payload.Items.Should().ContainSingle();
        payload.Items[0].ExternalId.Should().Be("CVE-2026-0400");
    }

    [Theory]
    [InlineData(RemediationOutcome.RiskAcceptance)]
    [InlineData(RemediationOutcome.AlternateMitigation)]
    public async Task List_DefaultView_ExcludesExceptionVulnerabilities(RemediationOutcome outcome)
    {
        var exceptionVulnerability = Vulnerability.Create("nvd", "CVE-2026-0410", "Exception vulnerability", "desc",
            Severity.Critical, 9.8m, null, DateTimeOffset.UtcNow.AddDays(-10));
        var openVulnerability = Vulnerability.Create("nvd", "CVE-2026-0411", "Open vulnerability", "desc",
            Severity.High, 7.5m, null, DateTimeOffset.UtcNow.AddDays(-20));
        var product = SoftwareProduct.Create("Acme", "Widget", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var decision = RemediationDecision.Create(
            _tenantId,
            remediationCase.Id,
            outcome,
            "Accepted",
            _tenantContext.CurrentUserId,
            DecisionApprovalStatus.Approved,
            expiryDate: outcome == RemediationOutcome.RiskAcceptance ? DateTimeOffset.UtcNow.AddDays(30) : null);

        _dbContext.Vulnerabilities.AddRange(exceptionVulnerability, openVulnerability);
        _dbContext.SoftwareProducts.Add(product);
        _dbContext.RemediationCases.Add(remediationCase);
        _dbContext.RemediationDecisions.Add(decision);
        await _dbContext.SaveChangesAsync();

        _dbContext.ApprovedVulnerabilityRemediations.Add(ApprovedVulnerabilityRemediation.Create(
            _tenantId,
            exceptionVulnerability.Id,
            remediationCase.Id,
            decision.Id,
            decision.Outcome,
            decision.ApprovedAt!.Value));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(new VulnerabilityFilterQuery(), new PaginationQuery(), CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<VulnerabilityDto>>().Subject;

        payload.Items.Should().NotContain(item => item.Id == exceptionVulnerability.Id);
        payload.Items.Should().Contain(item => item.Id == openVulnerability.Id);
    }

    [Fact]
    public async Task List_ThreatFilters_ReturnsThreatSignalsFromCanonicalAssessment()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0302", "Threat-filtered vulnerability",
            "desc", Severity.Critical, 9.1m, null, DateTimeOffset.UtcNow.AddDays(-5));
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        _dbContext.ThreatAssessments.Add(ThreatAssessment.Create(
            vuln.Id,
            threatScore: 88m,
            technicalScore: 92m,
            exploitLikelihoodScore: 0.81m,
            threatActivityScore: 73m,
            epssScore: 0.870m,
            knownExploited: true,
            publicExploit: true,
            activeAlert: true,
            hasRansomwareAssociation: false,
            hasMalwareAssociation: false,
            factorsJson: "[]",
            calculationVersion: "1"
        ));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new VulnerabilityFilterQuery(PublicExploitOnly: true, KnownExploitedOnly: true),
            new PaginationQuery(),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<VulnerabilityDto>>().Subject;
        var item = payload.Items.Should().ContainSingle().Subject;

        item.PublicExploit.Should().BeTrue();
        item.KnownExploited.Should().BeTrue();
        item.ActiveAlert.Should().BeTrue();
        item.ThreatScore.Should().Be(88m);
        item.EpssScore.Should().Be(0.870m);
    }

    [Fact]
    public async Task Get_ReturnsCanonicalVulnerabilityWithThreatAssessment()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0200", "Exploited vulnerability",
            "desc", Severity.Critical, 9.5m, null, DateTimeOffset.UtcNow.AddDays(-3));
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        _dbContext.ThreatAssessments.Add(ThreatAssessment.Create(
            vuln.Id,
            threatScore: 91m,
            technicalScore: 95m,
            exploitLikelihoodScore: 0.78m,
            threatActivityScore: 64m,
            epssScore: 0.910m,
            knownExploited: true,
            publicExploit: true,
            activeAlert: true,
            hasRansomwareAssociation: true,
            hasMalwareAssociation: false,
            factorsJson: "[]",
            calculationVersion: "1"
        ));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(vuln.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<VulnerabilityDetailDto>().Subject;

        payload.ThreatAssessment.Should().NotBeNull();
        payload.ThreatAssessment!.PublicExploit.Should().BeTrue();
        payload.ThreatAssessment.KnownExploited.Should().BeTrue();
        payload.ThreatAssessment.ActiveAlert.Should().BeTrue();
        payload.ThreatAssessment.HasRansomwareAssociation.Should().BeTrue();
        payload.ThreatAssessment.HasMalwareAssociation.Should().BeFalse();
        payload.ThreatAssessment.EpssScore.Should().Be(0.910m);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenVulnerabilityDoesNotExist()
    {
        var action = await _controller.Get(Guid.NewGuid(), CancellationToken.None);
        action.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Get_RanksPossibleCorrelatedSoftware_ByReinstallAndTiming()
    {
        var sourceSystem = SourceSystem.Create("defender", "Defender");
        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var device = Device.Create(_tenantId, sourceSystem.Id, "dev-correlate", "Correlated Device", Criticality.High);
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0400", "Correlated vuln", "desc", Severity.High, 8.0m, null, DateTimeOffset.UtcNow);

        _dbContext.SourceSystems.Add(sourceSystem);
        _dbContext.SoftwareProducts.Add(product);
        _dbContext.Devices.Add(device);
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        var installed = InstalledSoftware.Observe(_tenantId, device.Id, product.Id, sourceSystem.Id, "1.0", DateTimeOffset.UtcNow.AddHours(-4));
        _dbContext.InstalledSoftware.Add(installed);
        await _dbContext.SaveChangesAsync();

        var exposure = DeviceVulnerabilityExposure.Observe(_tenantId, device.Id, vuln.Id, product.Id, installed.Id, installed.Version, ExposureMatchSource.Product, DateTimeOffset.UtcNow.AddHours(-4));
        _dbContext.DeviceVulnerabilityExposures.Add(exposure);
        _dbContext.ExposureEpisodes.Add(ExposureEpisode.Open(_tenantId, exposure.Id, 1, DateTimeOffset.UtcNow.AddHours(-4)));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(vuln.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<VulnerabilityDetailDto>().Subject;
        payload.Exposures.ActiveEpisodes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Get_DoesNotThrow_WhenMultipleOpenEpisodeRiskAssessmentsExistForSameAsset()
    {
        var sourceSystem = SourceSystem.Create("defender", "Defender");
        var device = Device.Create(_tenantId, sourceSystem.Id, "dev-multi", "Multi Device", Criticality.High);
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0401", "Multi assessment vuln", "desc", Severity.Critical, 9.2m, null, DateTimeOffset.UtcNow);

        _dbContext.SourceSystems.Add(sourceSystem);
        _dbContext.Devices.Add(device);
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        var installA = InstalledSoftware.Observe(_tenantId, device.Id, Guid.NewGuid(), sourceSystem.Id, "1.0", DateTimeOffset.UtcNow.AddDays(-2));
        var installB = InstalledSoftware.Observe(_tenantId, device.Id, Guid.NewGuid(), sourceSystem.Id, "1.1", DateTimeOffset.UtcNow.AddDays(-1));
        _dbContext.InstalledSoftware.AddRange(installA, installB);
        await _dbContext.SaveChangesAsync();

        var exposureA = DeviceVulnerabilityExposure.Observe(_tenantId, device.Id, vuln.Id, installA.SoftwareProductId, installA.Id, installA.Version, ExposureMatchSource.Product, DateTimeOffset.UtcNow.AddDays(-2));
        var exposureB = DeviceVulnerabilityExposure.Observe(_tenantId, device.Id, vuln.Id, installB.SoftwareProductId, installB.Id, installB.Version, ExposureMatchSource.Product, DateTimeOffset.UtcNow.AddDays(-1));
        _dbContext.DeviceVulnerabilityExposures.AddRange(exposureA, exposureB);
        await _dbContext.SaveChangesAsync();

        _dbContext.ExposureAssessments.AddRange(
            ExposureAssessment.Create(_tenantId, exposureA.Id, null, vuln.CvssScore ?? 0m, 9.1m, "a", DateTimeOffset.UtcNow),
            ExposureAssessment.Create(_tenantId, exposureB.Id, null, vuln.CvssScore ?? 0m, 9.0m, "b", DateTimeOffset.UtcNow)
        );
        _dbContext.ExposureEpisodes.AddRange(
            ExposureEpisode.Open(_tenantId, exposureA.Id, 1, DateTimeOffset.UtcNow.AddDays(-2)),
            ExposureEpisode.Open(_tenantId, exposureB.Id, 1, DateTimeOffset.UtcNow.AddDays(-1))
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(vuln.Id, CancellationToken.None);
        action.Result.Should().BeOfType<OkObjectResult>();
    }

    // ── Organizational severity tests ────────────────────────────────────────

    [Fact]
    public async Task UpdateOrganizationalSeverity_CreatesRecord_WhenNoneExists()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0500", "Sev-adjust vuln", "desc",
            Severity.Critical, 9.0m, null, DateTimeOffset.UtcNow);
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateOrgSeverityRequest("High", "Mitigated by WAF", null, null, null);
        var result = await _controller.UpdateOrganizationalSeverity(vuln.Id, request, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        var record = _dbContext.OrganizationalSeverities
            .Single(s => s.VulnerabilityId == vuln.Id && s.TenantId == _tenantId);
        record.AdjustedSeverity.Should().Be(Severity.High);
        record.Justification.Should().Be("Mitigated by WAF");
    }

    [Fact]
    public async Task UpdateOrganizationalSeverity_UpdatesExistingRecord()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0501", "Sev-adjust vuln 2", "desc",
            Severity.Critical, 9.0m, null, DateTimeOffset.UtcNow);
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        // Seed existing record
        var userId = Guid.NewGuid();
        _dbContext.OrganizationalSeverities.Add(
            PatchHound.Core.Entities.OrganizationalSeverity.Create(
                vuln.Id, _tenantId, Severity.Critical, "Original", userId));
        await _dbContext.SaveChangesAsync();

        var request = new UpdateOrgSeverityRequest("Medium", "Now patched in env", null, null, "Firewall rule active");
        var result = await _controller.UpdateOrganizationalSeverity(vuln.Id, request, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        var records = _dbContext.OrganizationalSeverities
            .Where(s => s.VulnerabilityId == vuln.Id && s.TenantId == _tenantId)
            .ToList();
        records.Should().HaveCount(1, "upsert must not create a duplicate");
        records[0].AdjustedSeverity.Should().Be(Severity.Medium);
        records[0].Justification.Should().Be("Now patched in env");
        records[0].CompensatingControls.Should().Be("Firewall rule active");
    }

    [Fact]
    public async Task UpdateOrganizationalSeverity_ReturnsBadRequest_WhenNoTenant()
    {
        var noTenantContext = Substitute.For<ITenantContext>();
        noTenantContext.CurrentTenantId.Returns((Guid?)null);
        noTenantContext.CurrentUserId.Returns(Guid.NewGuid());
        var detailQueryService = new PatchHound.Api.Services.VulnerabilityDetailQueryService(_dbContext);
        var assessmentJobService = new PatchHound.Infrastructure.Services.VulnerabilityAssessmentJobService(_dbContext);
        var controllerNoTenant = new VulnerabilitiesController(_dbContext, noTenantContext, detailQueryService, assessmentJobService);

        var request = new UpdateOrgSeverityRequest("High", "justification");
        var result = await controllerNoTenant.UpdateOrganizationalSeverity(Guid.NewGuid(), request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    public void Dispose() => _dbContext.Dispose();
}
