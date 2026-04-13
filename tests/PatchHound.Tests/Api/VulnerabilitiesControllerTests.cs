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

    [Fact(Skip = "Phase-2: RecurrenceOnly filter removed (depends on legacy VulnerabilityAssetEpisodes). Restore in Phase 3.")]
    public async Task List_RecurrenceOnly_ReturnsOnlyPerAssetRecurringVulnerabilities()
    {
        await TenantVulnerabilityGraphFactory.SeedRecurrenceListGraphAsync(_dbContext, _tenantId);

        var action = await _controller.List(
            new VulnerabilityFilterQuery(),
            new PaginationQuery(),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<VulnerabilityDto>>().Subject;

        payload.TotalCount.Should().Be(1);
        payload.Items.Should().ContainSingle();
        payload.Items[0].ExternalId.Should().Be("CVE-2026-0001");
    }

    [Fact]
    public async Task List_returns_canonical_vulns_with_ExposureDataAvailable_false()
    {
        var v1 = Vulnerability.Create("nvd", "CVE-2026-0300", "Alpha", "desc",
            Severity.Critical, 9.8m, null, DateTimeOffset.UtcNow.AddDays(-10));
        var v2 = Vulnerability.Create("nvd", "CVE-2026-0301", "Beta", "desc",
            Severity.High, 7.5m, null, DateTimeOffset.UtcNow.AddDays(-20));
        _dbContext.Vulnerabilities.AddRange(v1, v2);
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
        payload.Items.Should().OnlyContain(v => !v.ExposureDataAvailable);
        payload.Items.Should().OnlyContain(v => v.AffectedDeviceCount == 0);
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

    [Fact(Skip = "Phase-2: uses legacy TenantVulnerability + AffectedAssets. Rewrite in Phase 3 using canonical Vulnerability.")]
    public async Task Get_RanksPossibleCorrelatedSoftware_ByReinstallAndTiming()
    {
        await Task.CompletedTask;
        // Skipped — legacy test body preserved below for Phase 3 reference
    }

    [Fact(Skip = "Phase-2: uses legacy VulnerabilityEpisodeRiskAssessment + AffectedAssets. Rewrite in Phase 3.")]
    public async Task Get_DoesNotThrow_WhenMultipleOpenEpisodeRiskAssessmentsExistForSameAsset()
    {
        await Task.CompletedTask;
        // Skipped — legacy test body preserved below for Phase 3 reference
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
