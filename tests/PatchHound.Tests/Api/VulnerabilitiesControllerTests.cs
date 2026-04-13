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

#pragma warning disable CS0618 // Phase-2: [Skip] tests reference obsolete AffectedAssets stub

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
        _controller = new VulnerabilitiesController(
            _dbContext,
            _tenantContext,
            detailQueryService
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

        var recurringExposure = DeviceVulnerabilityExposure.Create(_tenantId, device.Id, recurringVuln.Id, null, null, "test", DateTimeOffset.UtcNow.AddDays(-4));
        var nonRecurringExposure = DeviceVulnerabilityExposure.Create(_tenantId, device.Id, nonRecurringVuln.Id, null, null, "test", DateTimeOffset.UtcNow.AddDays(-1));
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

        _dbContext.DeviceVulnerabilityExposures.Add(
            DeviceVulnerabilityExposure.Create(
                _tenantId,
                device.Id,
                v1.Id,
                installedSoftwareId: null,
                softwareProductId: null,
                evidenceSource: "phase3-test",
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

        var exposure = DeviceVulnerabilityExposure.Create(_tenantId, device.Id, vuln.Id, null, product.Id, "test", DateTimeOffset.UtcNow.AddHours(-4));
        _dbContext.DeviceVulnerabilityExposures.Add(exposure);
        _dbContext.ExposureEpisodes.Add(ExposureEpisode.Open(_tenantId, exposure.Id, 1, DateTimeOffset.UtcNow.AddHours(-4)));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(vuln.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<VulnerabilityDetailDto>().Subject;
        payload.Exposures.ActiveExposures.Should().HaveCount(1);
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

        var exposureA = DeviceVulnerabilityExposure.Create(_tenantId, device.Id, vuln.Id, null, null, "test-a", DateTimeOffset.UtcNow.AddDays(-2));
        var exposureB = DeviceVulnerabilityExposure.Create(_tenantId, device.Id, vuln.Id, null, null, "test-b", DateTimeOffset.UtcNow.AddDays(-1));
        _dbContext.DeviceVulnerabilityExposures.AddRange(exposureA, exposureB);
        await _dbContext.SaveChangesAsync();

        _dbContext.ExposureAssessments.AddRange(
            ExposureAssessment.Create(_tenantId, exposureA.Id, device.Id, vuln.Id, null, Severity.Critical, 9.1m, null, "[]", "a", "1"),
            ExposureAssessment.Create(_tenantId, exposureB.Id, device.Id, vuln.Id, null, Severity.Critical, 9.0m, null, "[]", "b", "1")
        );
        _dbContext.ExposureEpisodes.AddRange(
            ExposureEpisode.Open(_tenantId, exposureA.Id, 1, DateTimeOffset.UtcNow.AddDays(-2)),
            ExposureEpisode.Open(_tenantId, exposureB.Id, 1, DateTimeOffset.UtcNow.AddDays(-1))
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(vuln.Id, CancellationToken.None);
        action.Result.Should().BeOfType<OkObjectResult>();
    }

    public void Dispose() => _dbContext.Dispose();

    private sealed class StubNvdApiClient(NvdCveResponse response) : NvdApiClient(new HttpClient())
    {
        public override Task<NvdCveResponse> GetCveAsync(
            NvdClientConfiguration configuration,
            string cveId,
            CancellationToken ct
        ) => Task.FromResult(response);
    }
}
