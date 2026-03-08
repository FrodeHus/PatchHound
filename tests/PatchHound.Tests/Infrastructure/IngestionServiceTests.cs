using System.Text.Json.Nodes;
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
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.VulnerabilitySources;

namespace PatchHound.Tests.Infrastructure;

public class IngestionServiceTests : IDisposable
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly IngestionService _service;
    private readonly IVulnerabilitySource _source;
    private readonly IAssetInventorySource _assetInventorySource;
    private readonly Guid _tenantId = Guid.NewGuid();

    public IngestionServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        var tenantIds = new List<Guid> { _tenantId };
        tenantContext.AccessibleTenantIds.Returns(tenantIds);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)
            )
            .Options;

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(tenantContext));

        _source = Substitute.For<IVulnerabilitySource, IAssetInventorySource>();
        _assetInventorySource = (IAssetInventorySource)_source;
        _source.SourceKey.Returns("test-source");
        _source.SourceName.Returns("TestSource");

        var logger = Substitute.For<ILogger<IngestionService>>();
        var taskProjectionService = new RemediationTaskProjectionService(
            _dbContext,
            new SlaService()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        var stagedMergeService = new StagedVulnerabilityMergeService(
            _dbContext,
            assessmentService,
            taskProjectionService
        );
        var stagedAssetMergeService = new StagedAssetMergeService(_dbContext);
        _service = new IngestionService(
            _dbContext,
            new[] { _source },
            [],
            assessmentService,
            taskProjectionService,
            stagedMergeService,
            stagedAssetMergeService,
            logger
        );
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
                new List<IngestionAffectedAsset> { new("ASSET-1", "Server1", AssetType.Device) },
                "Microsoft",
                "Windows Server",
                "2022"
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
        vuln.ProductVendor.Should().Be("Microsoft");
        vuln.ProductName.Should().Be("Windows Server");
        vuln.ProductVersion.Should().Be("2022");

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
    public async Task ExistingVulnerability_PreservesCatalogFieldsWhenIncomingValuesAreEmpty()
    {
        var existing = Vulnerability.Create(
            _tenantId,
            "CVE-2026-PRESERVE-1",
            "Catalog Title",
            "Catalog Description",
            Severity.High,
            "MicrosoftDefender",
            9.4m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
            DateTimeOffset.UtcNow.AddDays(-10),
            "Contoso",
            "Contoso Agent",
            "2.0"
        );
        await _dbContext.Vulnerabilities.AddAsync(existing);
        await _dbContext.SaveChangesAsync();

        var results = new List<IngestionResult>
        {
            new(
                "CVE-2026-PRESERVE-1",
                "Catalog Title",
                "Catalog Description",
                Severity.High,
                null,
                null,
                null,
                []
            ),
        };

        await _service.ProcessResultsAsync(
            _tenantId,
            "MicrosoftDefender",
            results,
            CancellationToken.None
        );

        var updated = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .SingleAsync(v => v.ExternalId == "CVE-2026-PRESERVE-1");

        updated.CvssScore.Should().Be(9.4m);
        updated.CvssVector.Should().Be("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H");
        updated.PublishedDate.Should().Be(existing.PublishedDate);
        updated.ProductVendor.Should().Be("Contoso");
        updated.ProductName.Should().Be("Contoso Agent");
        updated.ProductVersion.Should().Be("2.0");
    }

    [Fact]
    public async Task ProcessResultsAsync_DeduplicatesDuplicateVulnerabilityRowsBeforePersistence()
    {
        var results = new List<IngestionResult>
        {
            new(
                "CVE-2026-DUPE-1",
                "Duplicate Vuln",
                "Description",
                Severity.High,
                8.5m,
                null,
                null,
                [new IngestionAffectedAsset("ASSET-D1", "Server01", AssetType.Device)],
                References: [new IngestionReference("https://ref-1", "nvd", ["advisory"])],
                Sources: ["MicrosoftDefender"]
            ),
            new(
                "CVE-2026-DUPE-1",
                "Duplicate Vuln",
                "Description",
                Severity.High,
                8.5m,
                null,
                null,
                [new IngestionAffectedAsset("ASSET-D2", "Server02", AssetType.Device)],
                References: [new IngestionReference("https://ref-1", "nvd", ["advisory"])],
                Sources: ["NVD"]
            ),
        };

        await _service.ProcessResultsAsync(
            _tenantId,
            "TestSource",
            results,
            CancellationToken.None
        );

        var vulnerabilities = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .Where(v => v.ExternalId == "CVE-2026-DUPE-1")
            .Include(v => v.References)
            .Include(v => v.AffectedAssets)
            .ToListAsync();

        vulnerabilities.Should().ContainSingle();
        vulnerabilities[0].AffectedAssets.Should().HaveCount(2);
        vulnerabilities[0].References.Should().ContainSingle();
        vulnerabilities[0]
            .GetSources()
            .Should()
            .Contain(["MicrosoftDefender", "NVD", "TestSource"]);
    }

    [Fact]
    public async Task ProcessStagedResultsAsync_UsesStagedPayloadsForMerge()
    {
        var run = IngestionRun.Start(_tenantId, "test-source", DateTimeOffset.UtcNow);
        await _dbContext.IngestionRuns.AddAsync(run);
        await _dbContext.StagedVulnerabilities.AddAsync(
            StagedVulnerability.Create(
                run.Id,
                _tenantId,
                "test-source",
                "CVE-2026-STAGE-MERGE-1",
                "Stage merge title",
                Severity.Critical,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionResult(
                        "CVE-2026-STAGE-MERGE-1",
                        "Stage merge title",
                        "From staged payload",
                        Severity.Critical,
                        9.9m,
                        "CVSS:3.1/AV:N",
                        DateTimeOffset.UtcNow,
                        [
                            new IngestionAffectedAsset(
                                "STAGE-ASSET-1",
                                "StageAsset",
                                AssetType.Device
                            ),
                        ]
                    )
                ),
                DateTimeOffset.UtcNow
            )
        );
        await _dbContext.StagedVulnerabilityExposures.AddAsync(
            StagedVulnerabilityExposure.Create(
                run.Id,
                _tenantId,
                "test-source",
                "CVE-2026-STAGE-MERGE-1",
                "STAGE-ASSET-1",
                "StageAsset",
                AssetType.Device,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionAffectedAsset("STAGE-ASSET-1", "StageAsset", AssetType.Device)
                ),
                DateTimeOffset.UtcNow
            )
        );
        await _dbContext.SaveChangesAsync();

        await _service.ProcessStagedResultsAsync(
            run.Id,
            _tenantId,
            "test-source",
            "TestSource",
            CancellationToken.None
        );

        var vulnerability = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .SingleAsync(item => item.ExternalId == "CVE-2026-STAGE-MERGE-1");
        vulnerability.Title.Should().Be("Stage merge title");
        vulnerability.Description.Should().Be("From staged payload");
        vulnerability.AffectedAssets.Should().ContainSingle();
    }

    [Fact]
    public async Task RunIngestionAsync_WithConfiguredSource_CreatesCompletedIngestionRunAndClearsLease()
    {
        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.TenantSourceConfigurations.AddAsync(
            TenantSourceConfiguration.Create(
                _tenantId,
                "test-source",
                "Test Source",
                true,
                "0 * * * *"
            )
        );
        await _dbContext.SaveChangesAsync();

        _source
            .FetchVulnerabilitiesAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(
                [
                    new IngestionResult(
                        "CVE-2026-0001",
                        "Test Vuln",
                        "Description",
                        Severity.High,
                        8.0m,
                        null,
                        null,
                        []
                    ),
                ]
            );

        var started = await _service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();

        var run = await _dbContext.IngestionRuns.IgnoreQueryFilters().SingleAsync();
        run.Status.Should().Be("Succeeded");
        run.CompletedAt.Should().NotBeNull();
        run.FetchedVulnerabilityCount.Should().Be(1);
        run.StagedVulnerabilityCount.Should().Be(1);
        run.StagedExposureCount.Should().Be(0);
        run.MergedExposureCount.Should().Be(0);
        run.StagedAssetCount.Should().Be(0);
        run.StagedSoftwareLinkCount.Should().Be(0);

        var source = await _dbContext.TenantSourceConfigurations.IgnoreQueryFilters().SingleAsync();
        source.ActiveIngestionRunId.Should().BeNull();
        source.LeaseAcquiredAt.Should().BeNull();
        source.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task ProcessStagedAssetsAsync_UsesStagedSnapshotForMerge()
    {
        var run = IngestionRun.Start(_tenantId, "test-source", DateTimeOffset.UtcNow);
        await _dbContext.IngestionRuns.AddAsync(run);
        await _dbContext.StagedAssets.AddRangeAsync(
            StagedAsset.Create(
                run.Id,
                _tenantId,
                "test-source",
                "DEVICE-STAGE-1",
                "Device from stage",
                AssetType.Device,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionAsset(
                        "DEVICE-STAGE-1",
                        "Device from stage",
                        AssetType.Device,
                        DeviceComputerDnsName: "device-stage-1.contoso.local"
                    )
                ),
                DateTimeOffset.UtcNow
            ),
            StagedAsset.Create(
                run.Id,
                _tenantId,
                "test-source",
                "SOFTWARE-STAGE-1",
                "Software from stage",
                AssetType.Software,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionAsset(
                        "SOFTWARE-STAGE-1",
                        "Software from stage",
                        AssetType.Software
                    )
                ),
                DateTimeOffset.UtcNow
            )
        );
        await _dbContext.StagedDeviceSoftwareInstallations.AddAsync(
            StagedDeviceSoftwareInstallation.Create(
                run.Id,
                _tenantId,
                "test-source",
                "DEVICE-STAGE-1",
                "SOFTWARE-STAGE-1",
                DateTimeOffset.UtcNow,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionDeviceSoftwareLink(
                        "DEVICE-STAGE-1",
                        "SOFTWARE-STAGE-1",
                        DateTimeOffset.UtcNow
                    )
                ),
                DateTimeOffset.UtcNow
            )
        );
        await _dbContext.SaveChangesAsync();

        await _service.ProcessStagedAssetsAsync(
            run.Id,
            _tenantId,
            "test-source",
            CancellationToken.None
        );

        var assets = await _dbContext
            .Assets.IgnoreQueryFilters()
            .Where(item =>
                item.ExternalId == "DEVICE-STAGE-1" || item.ExternalId == "SOFTWARE-STAGE-1"
            )
            .ToListAsync();
        var installations = await _dbContext
            .DeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId)
            .ToListAsync();

        assets.Should().HaveCount(2);
        installations.Should().ContainSingle();
    }

    [Fact]
    public async Task RunIngestionAsync_CreatesStagedSnapshotRowsBeforeMerge()
    {
        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.TenantSourceConfigurations.AddAsync(
            TenantSourceConfiguration.Create(
                _tenantId,
                "test-source",
                "Test Source",
                true,
                "0 * * * *"
            )
        );
        await _dbContext.SaveChangesAsync();

        _assetInventorySource
            .FetchAssetsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(
                new IngestionAssetInventorySnapshot(
                    [
                        new IngestionAsset("DEVICE-1", "Server01", AssetType.Device),
                        new IngestionAsset("SOFTWARE-1", "Contoso Agent", AssetType.Software),
                    ],
                    [
                        new IngestionDeviceSoftwareLink(
                            "DEVICE-1",
                            "SOFTWARE-1",
                            DateTimeOffset.UtcNow
                        ),
                    ]
                )
            );
        _source
            .FetchVulnerabilitiesAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(
                [
                    new IngestionResult(
                        "CVE-2026-STAGED-1",
                        "Staged vuln",
                        "Description",
                        Severity.High,
                        8.0m,
                        "CVSS:3.1/AV:N",
                        DateTimeOffset.UtcNow,
                        [new IngestionAffectedAsset("DEVICE-1", "Server01", AssetType.Device)]
                    ),
                ]
            );

        await _service.RunIngestionAsync(_tenantId, CancellationToken.None);

        var run = await _dbContext.IngestionRuns.IgnoreQueryFilters().SingleAsync();
        var stagedVulnerabilities = await _dbContext
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == run.Id)
            .ToListAsync();
        var stagedExposures = await _dbContext
            .StagedVulnerabilityExposures.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == run.Id)
            .ToListAsync();
        var stagedAssets = await _dbContext
            .StagedAssets.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == run.Id)
            .ToListAsync();
        var stagedLinks = await _dbContext
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == run.Id)
            .ToListAsync();

        stagedVulnerabilities.Should().ContainSingle();
        stagedVulnerabilities[0].ExternalId.Should().Be("CVE-2026-STAGED-1");
        System
            .Text.Json.JsonSerializer.Deserialize<IngestionResult>(
                stagedVulnerabilities[0].PayloadJson
            )!
            .AffectedAssets.Should()
            .BeEmpty();
        stagedExposures.Should().ContainSingle();
        stagedExposures[0].AssetExternalId.Should().Be("DEVICE-1");
        stagedAssets.Should().HaveCount(2);
        stagedLinks.Should().ContainSingle();
        stagedLinks[0].DeviceExternalId.Should().Be("DEVICE-1");
    }

    [Fact]
    public async Task RunIngestionAsync_WhenLeaseAlreadyActive_SkipsRun()
    {
        var source = TenantSourceConfiguration.Create(
            _tenantId,
            "test-source",
            "Test Source",
            true,
            "0 * * * *"
        );
        source.AcquireLease(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(5)
        );

        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.TenantSourceConfigurations.AddAsync(source);
        await _dbContext.SaveChangesAsync();

        var started = await _service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeFalse();
        await _source
            .DidNotReceive()
            .FetchVulnerabilitiesAsync(_tenantId, Arg.Any<CancellationToken>());
        (await _dbContext.IngestionRuns.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RunIngestionAsync_PrunesExpiredStagingArtifactsForOldCompletedRuns()
    {
        var oldRun = IngestionRun.Start(
            _tenantId,
            "test-source",
            DateTimeOffset.UtcNow.AddDays(-10)
        );
        oldRun.CompleteSucceeded(
            DateTimeOffset.UtcNow.AddDays(-8),
            1,
            1,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0
        );
        await _dbContext.IngestionRuns.AddAsync(oldRun);
        await _dbContext.StagedVulnerabilities.AddAsync(
            StagedVulnerability.Create(
                oldRun.Id,
                _tenantId,
                "test-source",
                "CVE-OLD-1",
                "Old vuln",
                Severity.Low,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionResult(
                        "CVE-OLD-1",
                        "Old vuln",
                        null,
                        Severity.Low,
                        null,
                        null,
                        null,
                        []
                    )
                ),
                DateTimeOffset.UtcNow.AddDays(-8)
            )
        );
        await _dbContext.StagedVulnerabilityExposures.AddAsync(
            StagedVulnerabilityExposure.Create(
                oldRun.Id,
                _tenantId,
                "test-source",
                "CVE-OLD-1",
                "ASSET-OLD-1",
                "Old asset",
                AssetType.Device,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionAffectedAsset("ASSET-OLD-1", "Old asset", AssetType.Device)
                ),
                DateTimeOffset.UtcNow.AddDays(-8)
            )
        );
        await _dbContext.StagedAssets.AddAsync(
            StagedAsset.Create(
                oldRun.Id,
                _tenantId,
                "test-source",
                "ASSET-OLD-1",
                "Old asset",
                AssetType.Device,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionAsset("ASSET-OLD-1", "Old asset", AssetType.Device)
                ),
                DateTimeOffset.UtcNow.AddDays(-8)
            )
        );
        await _dbContext.StagedDeviceSoftwareInstallations.AddAsync(
            StagedDeviceSoftwareInstallation.Create(
                oldRun.Id,
                _tenantId,
                "test-source",
                "ASSET-OLD-1",
                "SOFTWARE-OLD-1",
                DateTimeOffset.UtcNow.AddDays(-8),
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionDeviceSoftwareLink(
                        "ASSET-OLD-1",
                        "SOFTWARE-OLD-1",
                        DateTimeOffset.UtcNow.AddDays(-8)
                    )
                ),
                DateTimeOffset.UtcNow.AddDays(-8)
            )
        );

        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.TenantSourceConfigurations.AddAsync(
            TenantSourceConfiguration.Create(
                _tenantId,
                "test-source",
                "Test Source",
                true,
                "0 * * * *"
            )
        );
        await _dbContext.SaveChangesAsync();

        _source.FetchVulnerabilitiesAsync(_tenantId, Arg.Any<CancellationToken>()).Returns([]);

        await _service.RunIngestionAsync(_tenantId, CancellationToken.None);

        (await _dbContext.IngestionRuns.IgnoreQueryFilters().AnyAsync(item => item.Id == oldRun.Id))
            .Should()
            .BeFalse();
        (
            await _dbContext
                .StagedVulnerabilities.IgnoreQueryFilters()
                .AnyAsync(item => item.IngestionRunId == oldRun.Id)
        )
            .Should()
            .BeFalse();
        (
            await _dbContext
                .StagedVulnerabilityExposures.IgnoreQueryFilters()
                .AnyAsync(item => item.IngestionRunId == oldRun.Id)
        )
            .Should()
            .BeFalse();
        (
            await _dbContext
                .StagedAssets.IgnoreQueryFilters()
                .AnyAsync(item => item.IngestionRunId == oldRun.Id)
        )
            .Should()
            .BeFalse();
        (
            await _dbContext
                .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
                .AnyAsync(item => item.IngestionRunId == oldRun.Id)
        )
            .Should()
            .BeFalse();
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
                new List<IngestionAffectedAsset>
                {
                    new("ASSET-ENV-1", "ServerEnv", AssetType.Device),
                }
            ),
        };

        await _service.ProcessResultsAsync(
            _tenantId,
            "TestSource",
            results,
            CancellationToken.None
        );

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
            [new("machine-1", "software-1", observedAt)]
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
    public async Task ProcessAssetsAsync_DeduplicatesDuplicateAssetsAndSoftwareLinks()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var snapshot = new IngestionAssetInventorySnapshot(
            [
                new IngestionAsset("device-1", "Server01", AssetType.Device),
                new IngestionAsset("device-1", "Server01 updated", AssetType.Device),
                new IngestionAsset("software-1", "Contoso Agent 1.0", AssetType.Software),
            ],
            [
                new IngestionDeviceSoftwareLink(
                    "device-1",
                    "software-1",
                    observedAt.AddMinutes(-2)
                ),
                new IngestionDeviceSoftwareLink("device-1", "software-1", observedAt),
            ]
        );

        await _service.ProcessAssetsAsync(_tenantId, snapshot, CancellationToken.None);

        var assets = await _dbContext.Assets.IgnoreQueryFilters().ToListAsync();
        assets.Should().HaveCount(2);
        assets.Single(asset => asset.ExternalId == "device-1").Name.Should().Be("Server01 updated");

        var installations = await _dbContext
            .DeviceSoftwareInstallations.IgnoreQueryFilters()
            .ToListAsync();
        installations.Should().ContainSingle();
        installations[0].LastSeenAt.Should().Be(observedAt);
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

        await _service.ProcessResultsAsync(
            _tenantId,
            "TestSource",
            results,
            CancellationToken.None
        );

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
        var asset = Asset.Create(
            _tenantId,
            "asset-1",
            AssetType.Device,
            "Server1",
            Criticality.Medium
        );
        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            vulnerability.Id,
            asset.Id,
            1,
            DateTimeOffset.UtcNow.AddHours(-2)
        );
        var projection = VulnerabilityAsset.Create(
            vulnerability.Id,
            asset.Id,
            DateTimeOffset.UtcNow.AddHours(-2)
        );
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(episode);
        await _dbContext.VulnerabilityAssets.AddAsync(projection);
        await _dbContext.SaveChangesAsync();

        await _service.ProcessResultsAsync(_tenantId, "TestSource", [], CancellationToken.None);

        var updatedEpisode = await _dbContext
            .VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .FirstAsync();
        var updatedProjection = await _dbContext
            .VulnerabilityAssets.IgnoreQueryFilters()
            .FirstAsync();
        var updatedVulnerability = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .FirstAsync();

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
        var asset = Asset.Create(
            _tenantId,
            "asset-1",
            AssetType.Device,
            "Server1",
            Criticality.Medium
        );
        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            vulnerability.Id,
            asset.Id,
            1,
            DateTimeOffset.UtcNow.AddHours(-2)
        );
        episode.MarkMissing();
        var projection = VulnerabilityAsset.Create(
            vulnerability.Id,
            asset.Id,
            DateTimeOffset.UtcNow.AddHours(-2)
        );
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(episode);
        await _dbContext.VulnerabilityAssets.AddAsync(projection);
        await _dbContext.SaveChangesAsync();

        await _service.ProcessResultsAsync(_tenantId, "TestSource", [], CancellationToken.None);

        var updatedEpisode = await _dbContext
            .VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .FirstAsync();
        var updatedProjection = await _dbContext
            .VulnerabilityAssets.IgnoreQueryFilters()
            .FirstAsync();
        var updatedVulnerability = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .FirstAsync();

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
        var asset = Asset.Create(
            _tenantId,
            "asset-1",
            AssetType.Device,
            "Server1",
            Criticality.Medium
        );
        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var firstSeenAt = DateTimeOffset.UtcNow.AddHours(-2);
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            vulnerability.Id,
            asset.Id,
            1,
            firstSeenAt
        );
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

        var episodes = await _dbContext
            .VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .ToListAsync();
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
        var asset = Asset.Create(
            _tenantId,
            "asset-1",
            AssetType.Device,
            "Server1",
            Criticality.Medium
        );
        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var firstSeenAt = DateTimeOffset.UtcNow.AddDays(-1);
        var firstEpisode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            vulnerability.Id,
            asset.Id,
            1,
            firstSeenAt
        );
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
        var updatedProjection = await _dbContext
            .VulnerabilityAssets.IgnoreQueryFilters()
            .FirstAsync();
        var updatedVulnerability = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .FirstAsync();

        episodes.Should().HaveCount(2);
        episodes[0].Status.Should().Be(VulnerabilityStatus.Resolved);
        episodes[1].EpisodeNumber.Should().Be(2);
        episodes[1].Status.Should().Be(VulnerabilityStatus.Open);
        updatedProjection.Status.Should().Be(VulnerabilityStatus.Open);
        updatedProjection.ResolvedDate.Should().BeNull();
        updatedVulnerability.Status.Should().Be(VulnerabilityStatus.Open);
    }

    [Fact]
    public async Task RunIngestionAsync_WhenNvdConfigured_EnrichesAndPersistsNormalizedVulnerabilityFields()
    {
        var source = Substitute.For<IVulnerabilitySource>();
        source.SourceKey.Returns("test-source");
        source.SourceName.Returns("TestSource");
        source
            .FetchVulnerabilitiesAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(
                [
                    new IngestionResult(
                        "CVE-2026-1234",
                        "CVE-2026-1234 - Product",
                        "Defender description",
                        Severity.High,
                        8.1m,
                        null,
                        null,
                        [new IngestionAffectedAsset("ASSET-1", "Server1", AssetType.Device)],
                        "Contoso",
                        "Contoso Agent",
                        "1.0"
                    ),
                ]
            );

        var asset = Asset.Create(
            _tenantId,
            "ASSET-1",
            AssetType.Device,
            "Server1",
            Criticality.High
        );
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.TenantSourceConfigurations.AddAsync(
            TenantSourceConfiguration.Create(
                _tenantId,
                "test-source",
                "Test Source",
                true,
                string.Empty
            )
        );
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(
            EnrichmentSourceConfiguration.Create(
                "nvd",
                "NVD API",
                true,
                "system/enrichment-sources/nvd",
                apiBaseUrl: "https://services.nvd.nist.gov"
            )
        );
        await _dbContext.SaveChangesAsync();

        var secretStore = Substitute.For<ISecretStore>();
        secretStore
            .GetSecretAsync("system/enrichment-sources/nvd", "apiKey", Arg.Any<CancellationToken>())
            .Returns("nvd-api-key");

        var provider = new NvdGlobalConfigurationProvider(_dbContext, secretStore);
        var enricher = new NvdVulnerabilityEnricher(
            new FakeNvdApiClient(),
            provider,
            Substitute.For<ILogger<NvdVulnerabilityEnricher>>()
        );

        var service = new IngestionService(
            _dbContext,
            [source],
            [enricher],
            new VulnerabilityAssessmentService(_dbContext, new EnvironmentalSeverityCalculator()),
            new RemediationTaskProjectionService(_dbContext, new SlaService()),
            new StagedVulnerabilityMergeService(
                _dbContext,
                new VulnerabilityAssessmentService(
                    _dbContext,
                    new EnvironmentalSeverityCalculator()
                ),
                new RemediationTaskProjectionService(_dbContext, new SlaService())
            ),
            new StagedAssetMergeService(_dbContext),
            Substitute.For<ILogger<IngestionService>>()
        );

        await service.RunIngestionAsync(_tenantId, CancellationToken.None);

        var vulnerability = await _dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .SingleAsync(item => item.ExternalId == "CVE-2026-1234");

        vulnerability.Description.Should().Be("NVD-enriched description");
        vulnerability.CvssScore.Should().Be(9.8m);
        vulnerability.CvssVector.Should().Be("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H");
        vulnerability
            .PublishedDate.Should()
            .Be(new DateTimeOffset(2026, 2, 2, 10, 0, 0, TimeSpan.Zero));
        vulnerability.ProductVendor.Should().Be("Contoso");
        vulnerability.ProductName.Should().Be("Contoso Agent");
        vulnerability.ProductVersion.Should().Be("1.0");
    }

    public void Dispose() => _dbContext.Dispose();

    private static IServiceProvider BuildServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }

    private sealed class FakeNvdApiClient : NvdApiClient
    {
        public FakeNvdApiClient()
            : base(new HttpClient()) { }

        public override Task<NvdCveResponse> GetCveAsync(
            NvdClientConfiguration configuration,
            string cveId,
            CancellationToken ct
        )
        {
            return Task.FromResult(
                new NvdCveResponse
                {
                    Vulnerabilities =
                    [
                        new NvdCveItem
                        {
                            Cve = new NvdCveRecord
                            {
                                Id = cveId,
                                Published = new DateTimeOffset(2026, 2, 2, 10, 0, 0, TimeSpan.Zero),
                                Descriptions =
                                [
                                    new NvdDescription
                                    {
                                        Lang = "en",
                                        Value = "NVD-enriched description",
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
                                                BaseScore = 9.8m,
                                                VectorString =
                                                    "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
                                            },
                                        },
                                    ],
                                },
                            },
                        },
                    ],
                }
            );
        }
    }
}
