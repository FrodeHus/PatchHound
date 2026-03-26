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
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Tenants;
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

        var vulnerabilityService = new VulnerabilityService(
            Substitute.For<IRepository<TenantVulnerability>>(),
            Substitute.For<IRepository<OrganizationalSeverity>>(),
            Substitute.For<IUnitOfWork>(),
            _tenantContext
        );
        var aiConfigurationResolver = Substitute.For<ITenantAiConfigurationResolver>();

        var snapshotResolver = new TenantSnapshotResolver(_dbContext);
        var aliasResolver = new PatchHound.Api.Services.TenantSoftwareAliasResolver(_dbContext);
        var detailQueryService = new PatchHound.Api.Services.VulnerabilityDetailQueryService(
            _dbContext,
            snapshotResolver,
            aliasResolver
        );
        _controller = new VulnerabilitiesController(
            _dbContext,
            vulnerabilityService,
            new AiReportService([], aiConfigurationResolver),
            _tenantContext,
            snapshotResolver,
            detailQueryService
        );
    }

    [Fact]
    public async Task List_RecurrenceOnly_ReturnsOnlyPerAssetRecurringVulnerabilities()
    {
        await TenantVulnerabilityGraphFactory.SeedRecurrenceListGraphAsync(_dbContext, _tenantId);

        var action = await _controller.List(
            new VulnerabilityFilterQuery(RecurrenceOnly: true),
            new PaginationQuery(),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<VulnerabilityDto>>().Subject;

        payload.TotalCount.Should().Be(1);
        payload.Items.Should().ContainSingle();
        payload.Items[0].ExternalId.Should().Be("CVE-2026-0001");
        payload.Items[0].ReappearanceCount.Should().Be(1);
        payload.Items[0].HasRecentReappearance.Should().BeTrue();
    }

    [Fact]
    public async Task List_ThreatFilters_ReturnsThreatSignalsFromPersistedAssessment()
    {
        var projection = TenantVulnerabilityGraphFactory.CreateProjection(
            _tenantId,
            "CVE-2026-0300",
            "Threat-filtered vulnerability",
            Severity.Critical
        );

        await _dbContext.AddRangeAsync(
            projection.Definition,
            projection.TenantVulnerability,
            VulnerabilityThreatAssessment.Create(
                projection.Definition.Id,
                88m,
                92m,
                81m,
                73m,
                0.870m,
                true,
                true,
                true,
                false,
                false,
                "[]",
                VulnerabilityThreatAssessmentService.CalculationVersion
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                projection.TenantVulnerability.Id,
                Guid.NewGuid(),
                1,
                DateTimeOffset.UtcNow.AddDays(-2)
            )
        );
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
    public async Task Get_RanksPossibleCorrelatedSoftware_ByReinstallAndTiming()
    {
        var device = Asset.Create(
            _tenantId,
            "device-1",
            AssetType.Device,
            "Device 1",
            Criticality.High
        );
        var projection = TenantVulnerabilityGraphFactory.CreateProjection(
            _tenantId,
            "CVE-2026-0100",
            "Contoso app vulnerability",
            Severity.Critical
        );
        var link = VulnerabilityAsset.Create(
            projection.TenantVulnerability.Id,
            device.Id,
            new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero)
        );

        var closeReinstall = Asset.Create(
            _tenantId,
            "soft-1",
            AssetType.Software,
            "Contoso Agent",
            Criticality.Low
        );
        var closeInstall = Asset.Create(
            _tenantId,
            "soft-2",
            AssetType.Software,
            "Nearby Utility",
            Criticality.Low
        );
        var oldInstall = Asset.Create(
            _tenantId,
            "soft-3",
            AssetType.Software,
            "Legacy Runtime",
            Criticality.Low
        );

        await _dbContext.AddRangeAsync(
            device,
            projection.Definition,
            projection.TenantVulnerability,
            link,
            closeReinstall,
            closeInstall,
            oldInstall
        );
        await _dbContext.VulnerabilityEpisodeRiskAssessments.AddAsync(
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                projection.TenantVulnerability.Id,
                device.Id,
                null,
                82m,
                70m,
                55m,
                741m,
                "Medium",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );

        var firstEpisode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            projection.TenantVulnerability.Id,
            device.Id,
            1,
            new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero)
        );
        firstEpisode.Resolve(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));

        var secondEpisode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            projection.TenantVulnerability.Id,
            device.Id,
            2,
            new DateTimeOffset(2026, 2, 10, 9, 0, 0, TimeSpan.Zero)
        );

        var reinstallEpisode = DeviceSoftwareInstallationEpisode.Create(
            _tenantId,
            device.Id,
            closeReinstall.Id,
            2,
            new DateTimeOffset(2026, 2, 9, 9, 0, 0, TimeSpan.Zero)
        );
        var nearbyEpisode = DeviceSoftwareInstallationEpisode.Create(
            _tenantId,
            device.Id,
            closeInstall.Id,
            1,
            new DateTimeOffset(2026, 2, 8, 9, 0, 0, TimeSpan.Zero)
        );
        var oldEpisode = DeviceSoftwareInstallationEpisode.Create(
            _tenantId,
            device.Id,
            oldInstall.Id,
            1,
            new DateTimeOffset(2025, 12, 15, 9, 0, 0, TimeSpan.Zero)
        );

        await _dbContext.VulnerabilityAssetEpisodes.AddRangeAsync(firstEpisode, secondEpisode);
        await _dbContext.DeviceSoftwareInstallationEpisodes.AddRangeAsync(
            reinstallEpisode,
            nearbyEpisode,
            oldEpisode
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(projection.TenantVulnerability.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<VulnerabilityDetailDto>().Subject;
        var asset = payload.AffectedAssets.Should().ContainSingle().Subject;

        asset
            .PossibleCorrelatedSoftware.Should()
            .Equal("Contoso Agent", "Nearby Utility", "Legacy Runtime");
        asset.EpisodeRiskScore.Should().Be(741m);
        asset.EpisodeRiskBand.Should().Be("Medium");
    }

    [Fact]
    public async Task Get_ReturnsThreatSignalsIncludingPublicExploitAndEpss()
    {
        var projection = TenantVulnerabilityGraphFactory.CreateProjection(
            _tenantId,
            "CVE-2026-0200",
            "Exploited vulnerability",
            Severity.Critical
        );

        await _dbContext.AddRangeAsync(
            projection.Definition,
            projection.TenantVulnerability,
            VulnerabilityThreatAssessment.Create(
                projection.Definition.Id,
                91m,
                95m,
                78m,
                64m,
                0.910m,
                true,
                true,
                true,
                true,
                false,
                "[]",
                VulnerabilityThreatAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(projection.TenantVulnerability.Id, CancellationToken.None);

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
    public async Task Get_DoesNotThrow_WhenMultipleOpenEpisodeRiskAssessmentsExistForSameAsset()
    {
        var device = Asset.Create(
            _tenantId,
            "device-dup-risk",
            AssetType.Device,
            "Device duplicate risk",
            Criticality.High
        );
        var projection = TenantVulnerabilityGraphFactory.CreateProjection(
            _tenantId,
            "CVE-2026-0400",
            "Duplicate risk assessment vulnerability",
            Severity.Critical
        );
        var link = VulnerabilityAsset.Create(
            projection.TenantVulnerability.Id,
            device.Id,
            new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero)
        );

        await _dbContext.AddRangeAsync(device, projection.Definition, projection.TenantVulnerability, link);
        await _dbContext.VulnerabilityEpisodeRiskAssessments.AddRangeAsync(
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                projection.TenantVulnerability.Id,
                device.Id,
                null,
                60m,
                55m,
                50m,
                650m,
                "Medium",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                projection.TenantVulnerability.Id,
                device.Id,
                null,
                80m,
                70m,
                60m,
                780m,
                "High",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(projection.TenantVulnerability.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<VulnerabilityDetailDto>().Subject;
        payload.AffectedAssets.Should().ContainSingle();
        payload.AffectedAssets[0].EpisodeRiskScore.Should().Be(780m);
        payload.AffectedAssets[0].EpisodeRiskBand.Should().Be("High");
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
