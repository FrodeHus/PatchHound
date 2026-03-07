using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using System.Text.Json.Nodes;

namespace PatchHound.Tests.Infrastructure;

public class IngestionServiceTests : IDisposable
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly IngestionService _service;
    private readonly IVulnerabilitySource _source;
    private readonly Guid _tenantId = Guid.NewGuid();

    public IngestionServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        var tenantIds = new List<Guid> { _tenantId };
        tenantContext.AccessibleTenantIds.Returns(tenantIds);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(tenantContext));

        _source = Substitute.For<IVulnerabilitySource>();
        _source.SourceKey.Returns("test-source");
       _source.SourceName.Returns("TestSource");

        var logger = Substitute.For<ILogger<IngestionService>>();
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        _service = new IngestionService(_dbContext, new[] { _source }, assessmentService, logger);
    }

    [Fact]
    public async Task NewVulnerability_CreatesVulnerabilityAndAssetAndTask()
    {
        // Arrange: create an asset with an owner so a task will be created
        var ownerId = Guid.NewGuid();
        var asset = Asset.Create(
            _tenantId,
            "ASSET-1",
            AssetType.Device,
            "Server1",
            Criticality.High
        );
        asset.AssignOwner(ownerId);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var results = new List<IngestionResult>
        {
            new(
                "CVE-2025-0001",
                "Test Vuln",
                "Description",
                Severity.High,
                8.5m,
                "CVSS:3.1/AV:N",
                DateTimeOffset.UtcNow,
                new List<IngestionAffectedAsset> { new("ASSET-1", "Server1", AssetType.Device) }
            ),
        };

        // Act
        await _service.ProcessResultsAsync(
            _tenantId,
            "TestSource",
            results,
            CancellationToken.None
        );

        // Assert
        var vuln = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.ExternalId == "CVE-2025-0001");
        vuln.Should().NotBeNull();
        vuln!.Title.Should().Be("Test Vuln");
        vuln.VendorSeverity.Should().Be(Severity.High);
        vuln.Source.Should().Be("TestSource");

        var va = await _dbContext
            .VulnerabilityAssets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(va => va.VulnerabilityId == vuln.Id);
        va.Should().NotBeNull();
        va!.Status.Should().Be(VulnerabilityStatus.Open);

        var task = await _dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.VulnerabilityId == vuln.Id);
        task.Should().NotBeNull();
        task!.AssigneeId.Should().Be(ownerId);
        task.Status.Should().Be(RemediationTaskStatus.Pending);
    }

    [Fact]
    public async Task NewVulnerability_NoOwner_SkipsTaskCreation()
    {
        // Arrange: asset without owner
        var asset = Asset.Create(
            _tenantId,
            "ASSET-2",
            AssetType.Device,
            "Server2",
            Criticality.Low
        );
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var results = new List<IngestionResult>
        {
            new(
                "CVE-2025-0002",
                "No Owner Vuln",
                "Desc",
                Severity.Medium,
                5.0m,
                null,
                null,
                new List<IngestionAffectedAsset> { new("ASSET-2", "Server2", AssetType.Device) }
            ),
        };

        // Act
        await _service.ProcessResultsAsync(
            _tenantId,
            "TestSource",
            results,
            CancellationToken.None
        );

        // Assert
        var vuln = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.ExternalId == "CVE-2025-0002");
        vuln.Should().NotBeNull();

        var tasks = await _dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .Where(t => t.VulnerabilityId == vuln!.Id)
            .ToListAsync();
        tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task ExistingVulnerability_UpdatesWithoutDuplication()
    {
        // Arrange: pre-existing vulnerability
        var existing = Vulnerability.Create(
            _tenantId,
            "CVE-2025-0003",
            "Old Title",
            "Old Desc",
            Severity.Medium,
            "TestSource",
            5.0m,
            null,
            null
        );
        await _dbContext.Vulnerabilities.AddAsync(existing);
        await _dbContext.SaveChangesAsync();

        var results = new List<IngestionResult>
        {
            new(
                "CVE-2025-0003",
                "Updated Title",
                "Updated Desc",
                Severity.High,
                7.5m,
                "CVSS:3.1/AV:N",
                DateTimeOffset.UtcNow,
                new List<IngestionAffectedAsset> { new("ASSET-3", "Server3", AssetType.Device) }
            ),
        };

        // Act
        await _service.ProcessResultsAsync(
            _tenantId,
            "TestSource",
            results,
            CancellationToken.None
        );

        // Assert: should be exactly one vulnerability with this external ID
        var vulns = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .Where(v => v.ExternalId == "CVE-2025-0003" && v.TenantId == _tenantId)
            .ToListAsync();
        vulns.Should().ContainSingle();
        vulns[0].Title.Should().Be("Updated Title");
        vulns[0].VendorSeverity.Should().Be(Severity.High);
        vulns[0].CvssScore.Should().Be(7.5m);
    }

    [Fact]
    public async Task ProcessResults_WithSecurityProfile_CreatesAssessment()
    {
        var profile = AssetSecurityProfile.Create(
            _tenantId,
            "Internal only",
            null,
            EnvironmentClass.Server,
            InternetReachability.LocalOnly,
            SecurityRequirementLevel.Medium,
            SecurityRequirementLevel.Medium,
            SecurityRequirementLevel.Medium
        );
        var asset = Asset.Create(
            _tenantId,
            "ASSET-ENV-1",
            AssetType.Device,
            "ServerEnv",
            Criticality.High
        );
        asset.AssignSecurityProfile(profile.Id);

        await _dbContext.AssetSecurityProfiles.AddAsync(profile);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var results = new List<IngestionResult>
        {
            new(
                "CVE-2025-ENV-1",
                "Reachability test",
                "Desc",
                Severity.Critical,
                9.8m,
                "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
                DateTimeOffset.UtcNow,
                new List<IngestionAffectedAsset> { new("ASSET-ENV-1", "ServerEnv", AssetType.Device) }
            ),
        };

        await _service.ProcessResultsAsync(_tenantId, "TestSource", results, CancellationToken.None);

        var assessment = await _dbContext
            .VulnerabilityAssetAssessments.IgnoreQueryFilters()
            .SingleAsync(item => item.AssetId == asset.Id);

        assessment.AssetSecurityProfileId.Should().Be(profile.Id);
        assessment.EffectiveScore.Should().BeLessThan(9.8m);
    }

    [Fact]
    public async Task ResolvedVulnerability_SecondMissingSync_MarksVulnerabilityAssetResolved()
    {
        // Arrange: existing open vulnerability with asset
        var vuln = Vulnerability.Create(
            _tenantId,
            "CVE-2025-0004",
            "Will Resolve",
            "Desc",
            Severity.Low,
            "TestSource"
        );
        await _dbContext.Vulnerabilities.AddAsync(vuln);

        var asset = Asset.Create(
            _tenantId,
            "ASSET-4",
            AssetType.Device,
            "Server4",
            Criticality.Low
        );
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var va = VulnerabilityAsset.Create(vuln.Id, asset.Id, DateTimeOffset.UtcNow);
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            vuln.Id,
            asset.Id,
            1,
            va.DetectedDate
        );
        episode.MarkMissing();
        await _dbContext.VulnerabilityAssets.AddAsync(va);
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(episode);

        var task = RemediationTask.Create(
            vuln.Id,
            asset.Id,
            _tenantId,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow.AddDays(30)
        );
        await _dbContext.RemediationTasks.AddAsync(task);
        await _dbContext.SaveChangesAsync();

        // Act: ingestion returns empty results (vulnerability no longer present)
        var results = new List<IngestionResult>();
        await _service.ProcessResultsAsync(
            _tenantId,
            "TestSource",
            results,
            CancellationToken.None
        );

        // Assert
        var updatedVuln = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .FirstAsync(v => v.Id == vuln.Id);
        updatedVuln.Status.Should().Be(VulnerabilityStatus.Resolved);

        var updatedVa = await _dbContext
            .VulnerabilityAssets.IgnoreQueryFilters()
            .FirstAsync(v => v.Id == va.Id);
        updatedVa.Status.Should().Be(VulnerabilityStatus.Resolved);
        updatedVa.ResolvedDate.Should().NotBeNull();

        var updatedTask = await _dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == task.Id);
        updatedTask.Status.Should().Be(RemediationTaskStatus.Completed);
    }

    [Fact]
    public async Task NewVulnerability_CreatesAssetIfNotExists()
    {
        // Arrange: no pre-existing asset
        var results = new List<IngestionResult>
        {
            new(
                "CVE-2025-0005",
                "New Asset Vuln",
                "Desc",
                Severity.Critical,
                9.0m,
                null,
                null,
                new List<IngestionAffectedAsset>
                {
                    new("NEW-ASSET-1", "NewServer", AssetType.CloudResource),
                }
            ),
        };

        // Act
        await _service.ProcessResultsAsync(
            _tenantId,
            "TestSource",
            results,
            CancellationToken.None
        );

        // Assert
        var asset = await _dbContext
            .Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.ExternalId == "NEW-ASSET-1" && a.TenantId == _tenantId);
        asset.Should().NotBeNull();
        asset!.Name.Should().Be("NewServer");
        asset.AssetType.Should().Be(AssetType.CloudResource);
    }

    [Fact]
    public async Task ProcessAssetsAsync_UpsertsSoftwareInventoryAsSoftwareAssets()
    {
        var snapshot = new IngestionAssetInventorySnapshot(
            [
                new(
                    "software-1",
                    "Contoso Agent 1.0",
                    AssetType.Software,
                    "Contoso Agent 1.0",
                    Metadata: """{"vendor":"Contoso","version":"1.0","exposedMachines":5}"""
                ),
            ],
            []
        );

        await _service.ProcessAssetsAsync(_tenantId, snapshot, CancellationToken.None);

        var asset = await _dbContext
            .Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.ExternalId == "software-1" && a.TenantId == _tenantId);

        asset.Should().NotBeNull();
        asset!.AssetType.Should().Be(AssetType.Software);
        asset.Name.Should().Be("Contoso Agent 1.0");

        var metadata = JsonNode.Parse(asset.Metadata);
        metadata?["vendor"]?.GetValue<string>().Should().Be("Contoso");
        metadata?["exposedMachines"]?.GetValue<int>().Should().Be(5);
    }

    [Fact]
    public async Task ProcessAssetsAsync_UpsertsNormalizedDeviceFields()
    {
        var lastSeenAt = DateTimeOffset.UtcNow;
        var snapshot = new IngestionAssetInventorySnapshot(
            [
                new(
                    "machine-1",
                    "server01.contoso.local",
                    AssetType.Device,
                    "Windows 11 23H2",
                    "server01.contoso.local",
                    "Active",
                    "Windows11",
                    "23H2",
                    "High",
                    lastSeenAt,
                    "10.0.0.15",
                    "aad-device-1"
                ),
            ],
            []
        );

        await _service.ProcessAssetsAsync(_tenantId, snapshot, CancellationToken.None);

        var asset = await _dbContext
            .Assets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.ExternalId == "machine-1" && a.TenantId == _tenantId);

        asset.Should().NotBeNull();
        asset!.AssetType.Should().Be(AssetType.Device);
        asset.Name.Should().Be("server01.contoso.local");
        asset.Description.Should().Be("Windows 11 23H2");
        asset.DeviceComputerDnsName.Should().Be("server01.contoso.local");
        asset.DeviceHealthStatus.Should().Be("Active");
        asset.DeviceOsPlatform.Should().Be("Windows11");
        asset.DeviceOsVersion.Should().Be("23H2");
        asset.DeviceRiskScore.Should().Be("High");
        asset.DeviceLastSeenAt.Should().Be(lastSeenAt);
        asset.DeviceLastIpAddress.Should().Be("10.0.0.15");
        asset.DeviceAadDeviceId.Should().Be("aad-device-1");
    }

    [Fact]
    public async Task ProcessAssetsAsync_CreatesDeviceSoftwareLinksAndEpisodes()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var snapshot = new IngestionAssetInventorySnapshot(
            [
                new("machine-1", "server01.contoso.local", AssetType.Device),
                new("software-1", "Contoso Agent 1.0", AssetType.Software),
            ],
            [
                new("machine-1", "software-1", observedAt),
            ]
        );

        await _service.ProcessAssetsAsync(_tenantId, snapshot, CancellationToken.None);

        var installation = await _dbContext
            .DeviceSoftwareInstallations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(link => link.TenantId == _tenantId);
        var episode = await _dbContext
            .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(current => current.TenantId == _tenantId);

        installation.Should().NotBeNull();
        installation!.LastSeenAt.Should().Be(observedAt);
        episode.Should().NotBeNull();
        episode!.EpisodeNumber.Should().Be(1);
        episode.RemovedAt.Should().BeNull();
    }

    [Fact]
    public async Task ProcessResultsAsync_UpdatesExistingAssetNameFromLatestMachineName()
    {
        var asset = Asset.Create(
            _tenantId,
            "machine-1",
            AssetType.Device,
            "OldMachineName",
            Criticality.Medium
        );
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var results = new List<IngestionResult>
        {
            new(
                "CVE-2025-7777",
                "Updated machine vulnerability",
                "Desc",
                Severity.High,
                7.5m,
                null,
                null,
                new List<IngestionAffectedAsset>
                {
                    new("machine-1", "FreshMachineName", AssetType.Device),
                }
            ),
        };

        await _service.ProcessResultsAsync(_tenantId, "TestSource", results, CancellationToken.None);

        var updatedAsset = await _dbContext
            .Assets.IgnoreQueryFilters()
            .FirstAsync(current => current.Id == asset.Id);

        updatedAsset.Name.Should().Be("FreshMachineName");
    }

    [Fact]
    public async Task ProcessResultsAsync_FirstMissingSync_DoesNotResolveEpisode()
    {
        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2025-2001",
            "Recurring vulnerability",
            "Desc",
            Severity.High,
            "TestSource"
        );
        var asset = Asset.Create(_tenantId, "asset-1", AssetType.Device, "Server1", Criticality.Medium);
        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var episode = VulnerabilityAssetEpisode.Create(_tenantId, vulnerability.Id, asset.Id, 1, DateTimeOffset.UtcNow.AddHours(-2));
        var projection = VulnerabilityAsset.Create(vulnerability.Id, asset.Id, DateTimeOffset.UtcNow.AddHours(-2));
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(episode);
        await _dbContext.VulnerabilityAssets.AddAsync(projection);
        await _dbContext.SaveChangesAsync();

        await _service.ProcessResultsAsync(_tenantId, "TestSource", [], CancellationToken.None);

        var updatedEpisode = await _dbContext.VulnerabilityAssetEpisodes.IgnoreQueryFilters().FirstAsync();
        var updatedProjection = await _dbContext.VulnerabilityAssets.IgnoreQueryFilters().FirstAsync();
        var updatedVulnerability = await _dbContext.Vulnerabilities.IgnoreQueryFilters().FirstAsync();

        updatedEpisode.Status.Should().Be(VulnerabilityStatus.Open);
        updatedEpisode.MissingSyncCount.Should().Be(1);
        updatedEpisode.ResolvedAt.Should().BeNull();
        updatedProjection.Status.Should().Be(VulnerabilityStatus.Open);
        updatedVulnerability.Status.Should().Be(VulnerabilityStatus.Open);
    }

    [Fact]
    public async Task ProcessResultsAsync_SecondMissingSync_ResolvesEpisodeAndProjection()
    {
        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2025-2002",
            "Recurring vulnerability",
            "Desc",
            Severity.High,
            "TestSource"
        );
        var asset = Asset.Create(_tenantId, "asset-1", AssetType.Device, "Server1", Criticality.Medium);
        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var episode = VulnerabilityAssetEpisode.Create(_tenantId, vulnerability.Id, asset.Id, 1, DateTimeOffset.UtcNow.AddHours(-2));
        episode.MarkMissing();
        var projection = VulnerabilityAsset.Create(vulnerability.Id, asset.Id, DateTimeOffset.UtcNow.AddHours(-2));
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(episode);
        await _dbContext.VulnerabilityAssets.AddAsync(projection);
        await _dbContext.SaveChangesAsync();

        await _service.ProcessResultsAsync(_tenantId, "TestSource", [], CancellationToken.None);

        var updatedEpisode = await _dbContext.VulnerabilityAssetEpisodes.IgnoreQueryFilters().FirstAsync();
        var updatedProjection = await _dbContext.VulnerabilityAssets.IgnoreQueryFilters().FirstAsync();
        var updatedVulnerability = await _dbContext.Vulnerabilities.IgnoreQueryFilters().FirstAsync();

        updatedEpisode.Status.Should().Be(VulnerabilityStatus.Resolved);
        updatedEpisode.MissingSyncCount.Should().Be(2);
        updatedEpisode.ResolvedAt.Should().NotBeNull();
        updatedProjection.Status.Should().Be(VulnerabilityStatus.Resolved);
        updatedProjection.ResolvedDate.Should().NotBeNull();
        updatedVulnerability.Status.Should().Be(VulnerabilityStatus.Resolved);
    }

    [Fact]
    public async Task ProcessResultsAsync_ReappearanceBeforeSecondMiss_ContinuesSameEpisode()
    {
        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2025-2003",
            "Recurring vulnerability",
            "Desc",
            Severity.High,
            "TestSource"
        );
        var asset = Asset.Create(_tenantId, "asset-1", AssetType.Device, "Server1", Criticality.Medium);
        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var firstSeenAt = DateTimeOffset.UtcNow.AddHours(-2);
        var episode = VulnerabilityAssetEpisode.Create(_tenantId, vulnerability.Id, asset.Id, 1, firstSeenAt);
        episode.MarkMissing();
        var projection = VulnerabilityAsset.Create(vulnerability.Id, asset.Id, firstSeenAt);
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(episode);
        await _dbContext.VulnerabilityAssets.AddAsync(projection);
        await _dbContext.SaveChangesAsync();

        await _service.ProcessResultsAsync(
            _tenantId,
            "TestSource",
            [
                new(
                    "CVE-2025-2003",
                    "Recurring vulnerability",
                    "Desc",
                    Severity.High,
                    8.0m,
                    null,
                    null,
                    [new IngestionAffectedAsset("asset-1", "Server1", AssetType.Device)]
                ),
            ],
            CancellationToken.None
        );

        var episodes = await _dbContext.VulnerabilityAssetEpisodes.IgnoreQueryFilters().ToListAsync();
        episodes.Should().HaveCount(1);
        episodes[0].EpisodeNumber.Should().Be(1);
        episodes[0].MissingSyncCount.Should().Be(0);
        episodes[0].Status.Should().Be(VulnerabilityStatus.Open);
    }

    [Fact]
    public async Task ProcessResultsAsync_ReappearanceAfterResolution_CreatesNewEpisode()
    {
        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2025-2004",
            "Recurring vulnerability",
            "Desc",
            Severity.High,
            "TestSource"
        );
        var asset = Asset.Create(_tenantId, "asset-1", AssetType.Device, "Server1", Criticality.Medium);
        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var firstSeenAt = DateTimeOffset.UtcNow.AddDays(-1);
        var firstEpisode = VulnerabilityAssetEpisode.Create(_tenantId, vulnerability.Id, asset.Id, 1, firstSeenAt);
        firstEpisode.MarkMissing();
        firstEpisode.MarkMissing();
        firstEpisode.Resolve(DateTimeOffset.UtcNow.AddHours(-2));
        var projection = VulnerabilityAsset.Create(vulnerability.Id, asset.Id, firstSeenAt);
        projection.Resolve(DateTimeOffset.UtcNow.AddHours(-2));
        vulnerability.UpdateStatus(VulnerabilityStatus.Resolved);
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(firstEpisode);
        await _dbContext.VulnerabilityAssets.AddAsync(projection);
        await _dbContext.SaveChangesAsync();

        await _service.ProcessResultsAsync(
            _tenantId,
            "TestSource",
            [
                new(
                    "CVE-2025-2004",
                    "Recurring vulnerability",
                    "Desc",
                    Severity.High,
                    8.0m,
                    null,
                    null,
                    [new IngestionAffectedAsset("asset-1", "Server1", AssetType.Device)]
                ),
            ],
            CancellationToken.None
        );

        var episodes = await _dbContext
            .VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .OrderBy(episode => episode.EpisodeNumber)
            .ToListAsync();
        var updatedProjection = await _dbContext.VulnerabilityAssets.IgnoreQueryFilters().FirstAsync();
        var updatedVulnerability = await _dbContext.Vulnerabilities.IgnoreQueryFilters().FirstAsync();

        episodes.Should().HaveCount(2);
        episodes[0].Status.Should().Be(VulnerabilityStatus.Resolved);
        episodes[1].EpisodeNumber.Should().Be(2);
        episodes[1].Status.Should().Be(VulnerabilityStatus.Open);
        updatedProjection.Status.Should().Be(VulnerabilityStatus.Open);
        updatedProjection.ResolvedDate.Should().BeNull();
        updatedVulnerability.Status.Should().Be(VulnerabilityStatus.Open);
    }

    public void Dispose() => _dbContext.Dispose();

    private static IServiceProvider BuildServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }
}
