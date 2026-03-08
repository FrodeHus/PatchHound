using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Vulnerabilities;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Tenants;
using PatchHound.Infrastructure.VulnerabilitySources;

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

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(_tenantContext));

        var vulnerabilityService = new VulnerabilityService(
            Substitute.For<IVulnerabilityRepository>(),
            Substitute.For<IRepository<OrganizationalSeverity>>(),
            Substitute.For<IUnitOfWork>(),
            _tenantContext
        );

        _controller = new VulnerabilitiesController(
            _dbContext,
            vulnerabilityService,
            new AiReportService([]),
            _tenantContext
        );
    }

    [Fact]
    public async Task List_RecurrenceOnly_ReturnsOnlyPerAssetRecurringVulnerabilities()
    {
        var recurringVulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2026-0001",
            "Recurring vulnerability",
            "Desc",
            Severity.High,
            "MicrosoftDefender"
        );
        var recurringAsset = Asset.Create(
            _tenantId,
            "device-1",
            AssetType.Device,
            "Device 1",
            Criticality.High
        );
        var recurringLink = VulnerabilityAsset.Create(
            recurringVulnerability.Id,
            recurringAsset.Id,
            DateTimeOffset.UtcNow.AddDays(-10)
        );

        var nonRecurringAcrossAssets = Vulnerability.Create(
            _tenantId,
            "CVE-2026-0002",
            "Multi asset no recurrence",
            "Desc",
            Severity.High,
            "MicrosoftDefender"
        );
        var assetTwo = Asset.Create(
            _tenantId,
            "device-2",
            AssetType.Device,
            "Device 2",
            Criticality.Medium
        );
        var assetThree = Asset.Create(
            _tenantId,
            "device-3",
            AssetType.Device,
            "Device 3",
            Criticality.Medium
        );
        var linkTwo = VulnerabilityAsset.Create(
            nonRecurringAcrossAssets.Id,
            assetTwo.Id,
            DateTimeOffset.UtcNow.AddDays(-6)
        );
        var linkThree = VulnerabilityAsset.Create(
            nonRecurringAcrossAssets.Id,
            assetThree.Id,
            DateTimeOffset.UtcNow.AddDays(-5)
        );

        await _dbContext.AddRangeAsync(
            recurringVulnerability,
            recurringAsset,
            recurringLink,
            nonRecurringAcrossAssets,
            assetTwo,
            assetThree,
            linkTwo,
            linkThree
        );

        await _dbContext.VulnerabilityAssetEpisodes.AddRangeAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                recurringVulnerability.Id,
                recurringAsset.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-20)
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                recurringVulnerability.Id,
                recurringAsset.Id,
                2,
                DateTimeOffset.UtcNow.AddDays(-3)
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                nonRecurringAcrossAssets.Id,
                assetTwo.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-6)
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                nonRecurringAcrossAssets.Id,
                assetThree.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-5)
            )
        );
        await _dbContext.SaveChangesAsync();

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
    public async Task Get_RanksPossibleCorrelatedSoftware_ByReinstallAndTiming()
    {
        var device = Asset.Create(
            _tenantId,
            "device-1",
            AssetType.Device,
            "Device 1",
            Criticality.High
        );
        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2026-0100",
            "Contoso app vulnerability",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender"
        );
        var link = VulnerabilityAsset.Create(
            vulnerability.Id,
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
            vulnerability,
            link,
            closeReinstall,
            closeInstall,
            oldInstall
        );

        var firstEpisode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            vulnerability.Id,
            device.Id,
            1,
            new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero)
        );
        firstEpisode.Resolve(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));

        var secondEpisode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            vulnerability.Id,
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

        var action = await _controller.Get(vulnerability.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<VulnerabilityDetailDto>().Subject;
        var asset = payload.AffectedAssets.Should().ContainSingle().Subject;

        asset
            .PossibleCorrelatedSoftware.Should()
            .Equal("Contoso Agent", "Nearby Utility", "Legacy Runtime");
    }

    [Fact]
    public async Task List_ReturnsPaginationMetadata()
    {
        for (var i = 0; i < 3; i++)
        {
            await _dbContext.Vulnerabilities.AddAsync(
                Vulnerability.Create(
                    _tenantId,
                    $"CVE-2026-100{i}",
                    $"Vulnerability {i}",
                    "Desc",
                    Severity.Medium,
                    "MicrosoftDefender"
                )
            );
        }

        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new VulnerabilityFilterQuery(),
            new PaginationQuery(2, 2),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<VulnerabilityDto>>().Subject;

        payload.Page.Should().Be(2);
        payload.PageSize.Should().Be(2);
        payload.TotalCount.Should().Be(3);
        payload.TotalPages.Should().Be(2);
        payload.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Get_FillsMissingDetailFields_FromNvd_WhenConfigured()
    {
        var databaseName = Guid.NewGuid().ToString();
        var requestOptions = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using var seedContext = new PatchHoundDbContext(
            requestOptions,
            BuildServiceProvider(_tenantContext)
        );
        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2026-4242",
            "Missing detail vulnerability",
            "",
            Severity.High,
            "MicrosoftDefender"
        );

        await seedContext.Vulnerabilities.AddAsync(vulnerability);
        await seedContext.SaveChangesAsync();

        var configurationOptions = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var configurationContext = new PatchHoundDbContext(
            configurationOptions,
            BuildServiceProvider(_tenantContext)
        );
        await configurationContext.EnrichmentSourceConfigurations.AddAsync(
            EnrichmentSourceConfiguration.Create(
                EnrichmentSourceCatalog.NvdSourceKey,
                "NVD",
                true,
                "global/enrichment/nvd",
                "https://services.nvd.nist.gov"
            )
        );
        await configurationContext.SaveChangesAsync();

        var secretStore = Substitute.For<ISecretStore>();
        secretStore
            .GetSecretAsync("global/enrichment/nvd", "apiKey", Arg.Any<CancellationToken>())
            .Returns("nvd-api-key");

        var configurationProvider = new NvdGlobalConfigurationProvider(
            configurationContext,
            secretStore
        );
        var nvdApiClient = new StubNvdApiClient(
            new NvdCveResponse
            {
                Vulnerabilities =
                [
                    new NvdCveItem
                    {
                        Cve = new NvdCveRecord
                        {
                            Published = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                            Descriptions =
                            [
                                new NvdDescription
                                {
                                    Lang = "en",
                                    Value = "Enriched description from NVD.",
                                },
                            ],
                            Metrics = new NvdMetricCollection
                            {
                                CvssMetricV31 =
                                [
                                    new NvdCvssMetric
                                    {
                                        CvssData = new NvdCvssData
                                        {
                                            BaseScore = 8.8m,
                                            VectorString =
                                                "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
                                        },
                                    },
                                ],
                            },
                            References =
                            [
                                new NvdReference
                                {
                                    Url = "https://nvd.nist.gov/vuln/detail/CVE-2026-4242",
                                    Source = "nvd@nist.gov",
                                    Tags = ["Vendor Advisory"],
                                },
                            ],
                        },
                    },
                ],
            }
        );

        await using var requestContext = new PatchHoundDbContext(
            requestOptions,
            BuildServiceProvider(_tenantContext)
        );

        var controller = new VulnerabilitiesController(
            requestContext,
            new VulnerabilityService(
                Substitute.For<IVulnerabilityRepository>(),
                Substitute.For<IRepository<OrganizationalSeverity>>(),
                Substitute.For<IUnitOfWork>(),
                _tenantContext
            ),
            new AiReportService([]),
            _tenantContext,
            configurationProvider,
            nvdApiClient
        );

        var action = await controller.Get(vulnerability.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var detail = result.Value.Should().BeOfType<VulnerabilityDetailDto>().Subject;

        detail.Description.Should().Be("Enriched description from NVD.");
        detail.CvssScore.Should().Be(8.8m);
        detail.CvssVector.Should().Be("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H");
        detail.PublishedDate.Should().Be(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        detail.References.Should().ContainSingle();
        detail.References[0].Url.Should().Be("https://nvd.nist.gov/vuln/detail/CVE-2026-4242");
        detail.References[0].Source.Should().Be("nvd@nist.gov");
        detail.References[0].Tags.Should().Contain("Vendor Advisory");

        var persisted = await requestContext
            .Vulnerabilities.Include(v => v.References)
            .FirstAsync(v => v.Id == vulnerability.Id);
        persisted.Description.Should().Be("Enriched description from NVD.");
        persisted.CvssScore.Should().Be(8.8m);
        persisted.CvssVector.Should().Be("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H");
        persisted
            .PublishedDate.Should()
            .Be(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        persisted.References.Should().ContainSingle();
        persisted
            .References.First()
            .Url.Should()
            .Be("https://nvd.nist.gov/vuln/detail/CVE-2026-4242");
        persisted.References.First().Source.Should().Be("nvd@nist.gov");
        persisted.References.First().GetTags().Should().Contain("Vendor Advisory");
    }

    public void Dispose() => _dbContext.Dispose();

    private static IServiceProvider BuildServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }

    private sealed class StubNvdApiClient(NvdCveResponse response) : NvdApiClient(new HttpClient())
    {
        public override Task<NvdCveResponse> GetCveAsync(
            NvdClientConfiguration configuration,
            string cveId,
            CancellationToken ct
        ) => Task.FromResult(response);
    }
}
