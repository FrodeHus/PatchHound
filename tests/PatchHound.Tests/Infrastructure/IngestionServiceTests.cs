using System.Net;
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
        _assetInventorySource
            .FetchAssetsAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new IngestionAssetInventorySnapshot([], []));

        var logger = Substitute.For<ILogger<IngestionService>>();
        var taskProjectionService = new RemediationTaskProjectionService(
            _dbContext,
            new SlaService()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        var normalizedSoftwareResolver = new NormalizedSoftwareResolver(_dbContext);
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            _dbContext,
            normalizedSoftwareResolver
        );
        var softwareMatchService = new SoftwareVulnerabilityMatchService(
            _dbContext,
            normalizedSoftwareProjectionService
        );
        var enrichmentJobEnqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );
        var dbContextFactory = Substitute.For<IDbContextFactory<PatchHoundDbContext>>();
        dbContextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_dbContext);
        var stagedMergeService = new StagedVulnerabilityMergeService(
            _dbContext,
            dbContextFactory,
            assessmentService,
            taskProjectionService,
            new IngestionStateCache()
        );
        var stagedAssetMergeService = new StagedAssetMergeService(_dbContext);
        _service = new IngestionService(
            _dbContext,
            new[] { _source },
            enrichmentJobEnqueuer,
            assessmentService,
            softwareMatchService,
            normalizedSoftwareProjectionService,
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
            .VulnerabilityDefinitions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.ExternalId == "CVE-2025-0001");
        vuln.Should().NotBeNull();
        vuln!.Title.Should().Be("Test Vuln");
        vuln.VendorSeverity.Should().Be(Severity.High);
        vuln.Source.Should().Be("TestSource");
        vuln.ProductVendor.Should().Be("Microsoft");
        vuln.ProductName.Should().Be("Windows Server");
        vuln.ProductVersion.Should().Be("2022");

        var tenantVulnerability = await _dbContext
            .TenantVulnerabilities.IgnoreQueryFilters()
            .FirstAsync(tv =>
                tv.TenantId == _tenantId && tv.VulnerabilityDefinitionId == vuln.Id
            );

        var va = await _dbContext
            .VulnerabilityAssets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(va => va.TenantVulnerabilityId == tenantVulnerability.Id);
        va.Should().NotBeNull();
        va!.Status.Should().Be(VulnerabilityStatus.Open);

        var task = await _dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantVulnerabilityId == tenantVulnerability.Id);
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
            .VulnerabilityDefinitions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.ExternalId == "CVE-2025-0002");
        vuln.Should().NotBeNull();
        var tenantVulnerability = await _dbContext
            .TenantVulnerabilities.IgnoreQueryFilters()
            .FirstAsync(tv => tv.TenantId == _tenantId && tv.VulnerabilityDefinitionId == vuln!.Id);

        var tasks = await _dbContext
            .RemediationTasks.IgnoreQueryFilters()
            .Where(t => t.TenantVulnerabilityId == tenantVulnerability!.Id)
            .ToListAsync();
        tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task ExistingVulnerability_UpdatesWithoutDuplication()
    {
        // Arrange: pre-existing vulnerability
        var existing = VulnerabilityDefinition.Create(
            "CVE-2025-0003",
            "Old Title",
            "Old Desc",
            Severity.Medium,
            "TestSource",
            5.0m,
            null,
            null
        );
        await _dbContext.VulnerabilityDefinitions.AddAsync(existing);
        await _dbContext.TenantVulnerabilities.AddAsync(
            TenantVulnerability.Create(
                _tenantId,
                existing.Id,
                VulnerabilityStatus.Open,
                DateTimeOffset.UtcNow
            )
        );
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
            .VulnerabilityDefinitions.IgnoreQueryFilters()
            .Where(v => v.ExternalId == "CVE-2025-0003")
            .ToListAsync();
        vulns.Should().ContainSingle();
        vulns[0].Title.Should().Be("Updated Title");
        vulns[0].VendorSeverity.Should().Be(Severity.High);
        vulns[0].CvssScore.Should().Be(7.5m);
    }

    [Fact]
    public async Task ExistingVulnerability_PreservesCatalogFieldsWhenIncomingValuesAreEmpty()
    {
        var existing = VulnerabilityDefinition.Create(
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
        await _dbContext.VulnerabilityDefinitions.AddAsync(existing);
        await _dbContext.TenantVulnerabilities.AddAsync(
            TenantVulnerability.Create(
                _tenantId,
                existing.Id,
                VulnerabilityStatus.Open,
                DateTimeOffset.UtcNow
            )
        );
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
            .VulnerabilityDefinitions.IgnoreQueryFilters()
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
            .VulnerabilityDefinitions.IgnoreQueryFilters()
            .Where(v => v.ExternalId == "CVE-2026-DUPE-1")
            .Include(v => v.References)
            .ToListAsync();

        vulnerabilities.Should().ContainSingle();
        vulnerabilities[0].References.Should().ContainSingle();
        vulnerabilities[0]
            .GetSources()
            .Should()
            .Contain(["MicrosoftDefender", "NVD", "TestSource"]);
        (
            await _dbContext
                .TenantVulnerabilities.IgnoreQueryFilters()
                .CountAsync(v => v.TenantId == _tenantId && v.VulnerabilityDefinitionId == vulnerabilities[0].Id)
        ).Should().Be(1);
        (
            await _dbContext
                .VulnerabilityAssets.IgnoreQueryFilters()
                .CountAsync()
        ).Should().Be(2);

        var run = await _dbContext.IngestionRuns.IgnoreQueryFilters().SingleAsync();
        var checkpoints = await _dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == run.Id)
            .OrderBy(item => item.Phase)
            .ToListAsync();
        checkpoints.Should().HaveCount(2);
        checkpoints.Select(item => item.Phase).Should().Equal("vulnerability-merge", "vulnerability-staging");
        checkpoints.Should().Contain(item =>
            item.Phase == "vulnerability-staging"
            && item.BatchNumber == 0
            && item.RecordsCommitted == 1
            && item.Status == "Staged");
        checkpoints.Should().Contain(item =>
            item.Phase == "vulnerability-merge"
            && item.BatchNumber == 0
            && item.Status == "Completed");
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
            null,
            "TestSource",
            CancellationToken.None
        );

        var vulnerability = await _dbContext
            .VulnerabilityDefinitions.IgnoreQueryFilters()
            .SingleAsync(item => item.ExternalId == "CVE-2026-STAGE-MERGE-1");
        vulnerability.Title.Should().Be("Stage merge title");
        vulnerability.Description.Should().Be("From staged payload");
        var tenantVulnerability = await _dbContext
            .TenantVulnerabilities.IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == _tenantId && item.VulnerabilityDefinitionId == vulnerability.Id);
        (
            await _dbContext
                .VulnerabilityAssets.IgnoreQueryFilters()
                .CountAsync(item => item.TenantVulnerabilityId == tenantVulnerability.Id)
        ).Should().Be(1);
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
        run.StagedVulnerabilityCount.Should().Be(1);
        run.StagedMachineCount.Should().Be(0);
        run.StagedSoftwareCount.Should().Be(0);
        run.PersistedVulnerabilityCount.Should().Be(1);

        var source = await _dbContext.TenantSourceConfigurations.IgnoreQueryFilters().SingleAsync();
        source.ActiveIngestionRunId.Should().BeNull();
        source.LeaseAcquiredAt.Should().BeNull();
        source.LeaseExpiresAt.Should().BeNull();
        source.LastStatus.Should().Be(IngestionRunStatuses.Succeeded);
        source.LastCompletedAt.Should().NotBeNull();
        source.LastError.Should().BeEmpty();
    }

    [Fact]
    public async Task RunIngestionAsync_WhenAuthFails_MarksRunAsFailedTerminal()
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
            .Returns<Task<IReadOnlyList<IngestionResult>>>(_ =>
                throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized)
            );

        var started = await _service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();

        var run = await _dbContext.IngestionRuns.IgnoreQueryFilters().SingleAsync();
        run.Status.Should().Be(IngestionRunStatuses.FailedTerminal);
        run.Error.Should().Be("Ingestion failed: external API authentication failed (401 Unauthorized).");
    }

    [Fact]
    public async Task RunIngestionAsync_WhenThrottledAfterRetries_MarksRunAsFailedRecoverableWithReason()
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
            .Returns<Task<IReadOnlyList<IngestionResult>>>(_ =>
                throw new HttpRequestException("Too many requests", null, HttpStatusCode.TooManyRequests)
            );

        var started = await _service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();

        var run = await _dbContext.IngestionRuns.IgnoreQueryFilters().SingleAsync();
        run.Status.Should().Be(IngestionRunStatuses.FailedRecoverable);
        run.Error.Should().Contain("429 Too Many Requests");
        run.Error.Should().Contain("retry limit");
    }

    [Fact]
    public async Task RunIngestionAsync_WhenCredentialsAreIncomplete_UsesSanitizedTerminalReason()
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
            .Returns<Task<IReadOnlyList<IngestionResult>>>(_ =>
                throw new IngestionTerminalException("Microsoft Defender source credentials could not be resolved.")
            );

        var started = await _service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();

        var run = await _dbContext.IngestionRuns.IgnoreQueryFilters().SingleAsync();
        run.Status.Should().Be(IngestionRunStatuses.FailedTerminal);
        run.Error.Should().Be(
            "Ingestion failed: source configuration is incomplete or invalid. Review source credentials and settings before retrying."
        );
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
            null,
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
            .BeNullOrEmpty();
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
    public async Task RunIngestionAsync_WhenRunningLeaseExpired_ResumesSameRunFromCheckpoint()
    {
        var sourceConfiguration = TenantSourceConfiguration.Create(
            _tenantId,
            "batch-source",
            "Batch Source",
            true,
            "0 * * * *"
        );
        var existingRun = IngestionRun.Start(
            _tenantId,
            "batch-source",
            DateTimeOffset.UtcNow.AddMinutes(-10)
        );
        sourceConfiguration.AcquireLease(
            existingRun.Id,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-1)
        );

        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.IngestionRuns.AddAsync(existingRun);
        await _dbContext.TenantSourceConfigurations.AddAsync(sourceConfiguration);
        await _dbContext.StagedVulnerabilities.AddAsync(
            StagedVulnerability.Create(
                existingRun.Id,
                _tenantId,
                "batch-source",
                "CVE-2026-RESUME-1",
                "Resumed vuln 1",
                Severity.High,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionResult(
                        "CVE-2026-RESUME-1",
                        "Resumed vuln 1",
                        "Description",
                        Severity.High,
                        8.0m,
                        null,
                        null,
                        []
                    )
                ),
                DateTimeOffset.UtcNow.AddMinutes(-9),
                1
            )
        );
        await _dbContext.IngestionCheckpoints.AddAsync(
            IngestionCheckpoint.Start(
                existingRun.Id,
                _tenantId,
                "batch-source",
                "vulnerability-staging",
                DateTimeOffset.UtcNow.AddMinutes(-9)
            )
        );
        await _dbContext.SaveChangesAsync();
        var checkpoint = await _dbContext.IngestionCheckpoints.IgnoreQueryFilters().SingleAsync();
        checkpoint.CommitBatch(
            1,
            "cursor-2",
            1,
            "Running",
            DateTimeOffset.UtcNow.AddMinutes(-9)
        );
        await _dbContext.SaveChangesAsync();

        var batchSource = Substitute.For<IVulnerabilitySource, IVulnerabilityBatchSource>();
        ((IVulnerabilitySource)batchSource).SourceKey.Returns("batch-source");
        ((IVulnerabilitySource)batchSource).SourceName.Returns("BatchSource");
        var batchInterface = (IVulnerabilityBatchSource)batchSource;
        batchInterface
            .FetchVulnerabilityBatchAsync(
                _tenantId,
                "cursor-2",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new SourceBatchResult<IngestionResult>(
                    [
                        new IngestionResult(
                            "CVE-2026-RESUME-2",
                            "Resumed vuln 2",
                            "Description",
                            Severity.Critical,
                            9.1m,
                            null,
                            null,
                            []
                        ),
                    ],
                    null,
                    true
                )
            );

        var logger = Substitute.For<ILogger<IngestionService>>();
        var taskProjectionService = new RemediationTaskProjectionService(
            _dbContext,
            new SlaService()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        var normalizedSoftwareResolver = new NormalizedSoftwareResolver(_dbContext);
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            _dbContext,
            normalizedSoftwareResolver
        );
        var softwareMatchService = new SoftwareVulnerabilityMatchService(
            _dbContext,
            normalizedSoftwareProjectionService
        );
        var enrichmentJobEnqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );
        var dbContextFactory = Substitute.For<IDbContextFactory<PatchHoundDbContext>>();
        dbContextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_dbContext);
        var stagedMergeService = new StagedVulnerabilityMergeService(
            _dbContext,
            dbContextFactory,
            assessmentService,
            taskProjectionService,
            new IngestionStateCache()
        );
        var stagedAssetMergeService = new StagedAssetMergeService(_dbContext);
        var service = new IngestionService(
            _dbContext,
            [(IVulnerabilitySource)batchSource],
            enrichmentJobEnqueuer,
            assessmentService,
            softwareMatchService,
            normalizedSoftwareProjectionService,
            taskProjectionService,
            stagedMergeService,
            stagedAssetMergeService,
            logger
        );

        var started = await service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();
        (await _dbContext.IngestionRuns.IgnoreQueryFilters().CountAsync()).Should().Be(1);

        var stagedBatchNumbers = await _dbContext
            .StagedVulnerabilities.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == existingRun.Id)
            .OrderBy(item => item.BatchNumber)
            .Select(item => item.BatchNumber)
            .ToListAsync();
        stagedBatchNumbers.Should().Equal([1, 2]);

        var updatedCheckpoint = await _dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .SingleAsync(item =>
                item.IngestionRunId == existingRun.Id && item.Phase == "vulnerability-staging"
            );
        updatedCheckpoint.BatchNumber.Should().Be(2);
        updatedCheckpoint.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task RunIngestionAsync_WhenConcurrencyRetryOccurs_PreservesCommittedStagedBatches()
    {
        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.TenantSourceConfigurations.AddAsync(
            TenantSourceConfiguration.Create(
                _tenantId,
                "retry-source",
                "Retry Source",
                true,
                "0 * * * *"
            )
        );
        await _dbContext.SaveChangesAsync();

        var batchSource = Substitute.For<IVulnerabilitySource, IVulnerabilityBatchSource>();
        batchSource.SourceKey.Returns("retry-source");
        batchSource.SourceName.Returns("RetrySource");

        var batchInterface = (IVulnerabilityBatchSource)batchSource;
        batchInterface
            .FetchVulnerabilityBatchAsync(_tenantId, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                new SourceBatchResult<IngestionResult>(
                    [
                        new IngestionResult(
                            "CVE-2026-RETRY-1",
                            "Retry vuln 1",
                            "Description",
                            Severity.High,
                            8.1m,
                            null,
                            null,
                            []
                        ),
                    ],
                    "cursor-2",
                    false
                )
            );

        var secondCursorAttempt = 0;
        batchInterface
            .FetchVulnerabilityBatchAsync(
                _tenantId,
                "cursor-2",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                secondCursorAttempt++;
                if (secondCursorAttempt == 1)
                {
                    throw new DbUpdateConcurrencyException("Simulated concurrency conflict");
                }

                return new SourceBatchResult<IngestionResult>(
                    [
                        new IngestionResult(
                            "CVE-2026-RETRY-2",
                            "Retry vuln 2",
                            "Description",
                            Severity.Critical,
                            9.3m,
                            null,
                            null,
                            []
                        ),
                    ],
                    null,
                    true
                );
            });

        var logger = Substitute.For<ILogger<IngestionService>>();
        var taskProjectionService = new RemediationTaskProjectionService(
            _dbContext,
            new SlaService()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        var normalizedSoftwareResolver = new NormalizedSoftwareResolver(_dbContext);
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            _dbContext,
            normalizedSoftwareResolver
        );
        var softwareMatchService = new SoftwareVulnerabilityMatchService(
            _dbContext,
            normalizedSoftwareProjectionService
        );
        var enrichmentJobEnqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );
        var dbContextFactory = Substitute.For<IDbContextFactory<PatchHoundDbContext>>();
        dbContextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_dbContext);
        var stagedMergeService = new StagedVulnerabilityMergeService(
            _dbContext,
            dbContextFactory,
            assessmentService,
            taskProjectionService,
            new IngestionStateCache()
        );
        var stagedAssetMergeService = new StagedAssetMergeService(_dbContext);
        var service = new IngestionService(
            _dbContext,
            [batchSource],
            enrichmentJobEnqueuer,
            assessmentService,
            softwareMatchService,
            normalizedSoftwareProjectionService,
            taskProjectionService,
            stagedMergeService,
            stagedAssetMergeService,
            logger
        );

        var started = await service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();

        var vulnerabilities = await _dbContext
            .VulnerabilityDefinitions.IgnoreQueryFilters()
            .OrderBy(item => item.ExternalId)
            .Select(item => item.ExternalId)
            .ToListAsync();
        vulnerabilities.Should().Equal(["CVE-2026-RETRY-1", "CVE-2026-RETRY-2"]);

        var checkpoints = await _dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .Where(item => item.SourceKey == "retry-source" && item.Phase == "vulnerability-staging")
            .ToListAsync();
        checkpoints.Should().ContainSingle();
        checkpoints[0].BatchNumber.Should().Be(2);
        checkpoints[0].Status.Should().Be("Completed");

        await batchInterface
            .Received(1)
            .FetchVulnerabilityBatchAsync(_tenantId, null, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunIngestionAsync_WhenAssetBatchLeaseExpired_ResumesSameRunFromCheckpoint()
    {
        var sourceConfiguration = TenantSourceConfiguration.Create(
            _tenantId,
            "asset-batch-source",
            "Asset Batch Source",
            true,
            "0 * * * *"
        );
        var existingRun = IngestionRun.Start(
            _tenantId,
            "asset-batch-source",
            DateTimeOffset.UtcNow.AddMinutes(-10)
        );
        sourceConfiguration.AcquireLease(
            existingRun.Id,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-1)
        );

        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.IngestionRuns.AddAsync(existingRun);
        await _dbContext.TenantSourceConfigurations.AddAsync(sourceConfiguration);
        await _dbContext.StagedAssets.AddAsync(
            StagedAsset.Create(
                existingRun.Id,
                _tenantId,
                "asset-batch-source",
                "DEVICE-1",
                "Server01",
                AssetType.Device,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionAsset("DEVICE-1", "Server01", AssetType.Device)
                ),
                DateTimeOffset.UtcNow.AddMinutes(-9),
                1
            )
        );
        await _dbContext.IngestionCheckpoints.AddAsync(
            IngestionCheckpoint.Start(
                existingRun.Id,
                _tenantId,
                "asset-batch-source",
                "asset-staging",
                DateTimeOffset.UtcNow.AddMinutes(-9)
            )
        );
        await _dbContext.SaveChangesAsync();

        var checkpoint = await _dbContext
            .IngestionCheckpoints.IgnoreQueryFilters()
            .SingleAsync(item =>
                item.IngestionRunId == existingRun.Id && item.Phase == "asset-staging"
            );
        checkpoint.CommitBatch(
            1,
            "asset-cursor-2",
            1,
            "Running",
            DateTimeOffset.UtcNow.AddMinutes(-9)
        );
        await _dbContext.SaveChangesAsync();

        var batchSource = Substitute.For<
            IVulnerabilitySource,
            IAssetInventoryBatchSource,
            IVulnerabilityBatchSource
        >();
        batchSource.SourceKey.Returns("asset-batch-source");
        batchSource.SourceName.Returns("AssetBatchSource");

        var assetBatchInterface = (IAssetInventoryBatchSource)batchSource;
        assetBatchInterface
            .FetchAssetBatchAsync(
                _tenantId,
                "asset-cursor-2",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new SourceBatchResult<IngestionAssetInventorySnapshot>(
                    [
                        new IngestionAssetInventorySnapshot(
                        [new IngestionAsset("SOFTWARE-1", "Agent 1.0", AssetType.Software)],
                        [new IngestionDeviceSoftwareLink("DEVICE-1", "SOFTWARE-1", DateTimeOffset.UtcNow)]
                        ),
                    ],
                    null,
                    true
                )
            );

        var vulnerabilityBatchInterface = (IVulnerabilityBatchSource)batchSource;
        vulnerabilityBatchInterface
            .FetchVulnerabilityBatchAsync(_tenantId, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SourceBatchResult<IngestionResult>([], null, true));

        var logger = Substitute.For<ILogger<IngestionService>>();
        var taskProjectionService = new RemediationTaskProjectionService(
            _dbContext,
            new SlaService()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        var normalizedSoftwareResolver = new NormalizedSoftwareResolver(_dbContext);
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            _dbContext,
            normalizedSoftwareResolver
        );
        var softwareMatchService = new SoftwareVulnerabilityMatchService(
            _dbContext,
            normalizedSoftwareProjectionService
        );
        var enrichmentJobEnqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );
        var dbContextFactory = Substitute.For<IDbContextFactory<PatchHoundDbContext>>();
        dbContextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_dbContext);
        var stagedMergeService = new StagedVulnerabilityMergeService(
            _dbContext,
            dbContextFactory,
            assessmentService,
            taskProjectionService,
            new IngestionStateCache()
        );
        var stagedAssetMergeService = new StagedAssetMergeService(_dbContext);
        var service = new IngestionService(
            _dbContext,
            [batchSource],
            enrichmentJobEnqueuer,
            assessmentService,
            softwareMatchService,
            normalizedSoftwareProjectionService,
            taskProjectionService,
            stagedMergeService,
            stagedAssetMergeService,
            logger
        );

        var started = await service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();
        (await _dbContext.IngestionRuns.IgnoreQueryFilters().CountAsync()).Should().Be(1);

        var stagedAssets = await _dbContext
            .StagedAssets.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == existingRun.Id)
            .OrderBy(item => item.BatchNumber)
            .Select(item => item.BatchNumber)
            .ToListAsync();
        stagedAssets.Should().Equal([1, 2]);

        var stagedLinks = await _dbContext
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == existingRun.Id)
            .ToListAsync();
        stagedLinks.Should().ContainSingle();
        stagedLinks[0].BatchNumber.Should().Be(2);
    }

    [Fact]
    public async Task RunIngestionAsync_WhenAssetPhasesCompleted_DoesNotRefetchAssetsOnResume()
    {
        var sourceConfiguration = TenantSourceConfiguration.Create(
            _tenantId,
            "resume-source",
            "Resume Source",
            true,
            "0 * * * *"
        );
        var existingRun = IngestionRun.Start(
            _tenantId,
            "resume-source",
            DateTimeOffset.UtcNow.AddMinutes(-10)
        );
        sourceConfiguration.AcquireLease(
            existingRun.Id,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-1)
        );

        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.IngestionRuns.AddAsync(existingRun);
        await _dbContext.TenantSourceConfigurations.AddAsync(sourceConfiguration);
        await _dbContext.IngestionCheckpoints.AddRangeAsync(
            IngestionCheckpoint.Start(
                existingRun.Id,
                _tenantId,
                "resume-source",
                "asset-staging",
                DateTimeOffset.UtcNow.AddMinutes(-9)
            ),
            IngestionCheckpoint.Start(
                existingRun.Id,
                _tenantId,
                "resume-source",
                "asset-merge",
                DateTimeOffset.UtcNow.AddMinutes(-9)
            ),
            IngestionCheckpoint.Start(
                existingRun.Id,
                _tenantId,
                "resume-source",
                "vulnerability-staging",
                DateTimeOffset.UtcNow.AddMinutes(-9)
            )
        );
        await _dbContext.SaveChangesAsync();

        var checkpoints = await _dbContext.IngestionCheckpoints.IgnoreQueryFilters().ToListAsync();
        checkpoints.Single(item => item.Phase == "asset-staging")
            .CommitBatch(1, null, 2, "Completed", DateTimeOffset.UtcNow.AddMinutes(-9));
        checkpoints.Single(item => item.Phase == "asset-merge")
            .CommitBatch(1, null, 2, "Completed", DateTimeOffset.UtcNow.AddMinutes(-9));
        checkpoints.Single(item => item.Phase == "vulnerability-staging")
            .CommitBatch(1, null, 0, "Completed", DateTimeOffset.UtcNow.AddMinutes(-9));
        await _dbContext.SaveChangesAsync();

        var batchSource = Substitute.For<
            IVulnerabilitySource,
            IAssetInventoryBatchSource,
            IVulnerabilityBatchSource
        >();
        batchSource.SourceKey.Returns("resume-source");
        batchSource.SourceName.Returns("ResumeSource");

        var vulnerabilityBatchInterface = (IVulnerabilityBatchSource)batchSource;
        vulnerabilityBatchInterface
            .FetchVulnerabilityBatchAsync(_tenantId, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SourceBatchResult<IngestionResult>([], null, true));

        var logger = Substitute.For<ILogger<IngestionService>>();
        var taskProjectionService = new RemediationTaskProjectionService(
            _dbContext,
            new SlaService()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        var normalizedSoftwareResolver = new NormalizedSoftwareResolver(_dbContext);
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            _dbContext,
            normalizedSoftwareResolver
        );
        var softwareMatchService = new SoftwareVulnerabilityMatchService(
            _dbContext,
            normalizedSoftwareProjectionService
        );
        var enrichmentJobEnqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );
        var dbContextFactory = Substitute.For<IDbContextFactory<PatchHoundDbContext>>();
        dbContextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_dbContext);
        var stagedMergeService = new StagedVulnerabilityMergeService(
            _dbContext,
            dbContextFactory,
            assessmentService,
            taskProjectionService,
            new IngestionStateCache()
        );
        var stagedAssetMergeService = new StagedAssetMergeService(_dbContext);
        var service = new IngestionService(
            _dbContext,
            [batchSource],
            enrichmentJobEnqueuer,
            assessmentService,
            softwareMatchService,
            normalizedSoftwareProjectionService,
            taskProjectionService,
            stagedMergeService,
            stagedAssetMergeService,
            logger
        );

        var started = await service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();
        await ((IAssetInventoryBatchSource)batchSource)
            .DidNotReceive()
            .FetchAssetBatchAsync(_tenantId, Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunIngestionAsync_WhenBatchSourceHasMultiplePages_MergesAllPages()
    {
        var batchSource = Substitute.For<
            IVulnerabilitySource,
            IAssetInventoryBatchSource,
            IVulnerabilityBatchSource
        >();
        batchSource.SourceKey.Returns("paged-source");
        batchSource.SourceName.Returns("PagedSource");

        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.TenantSourceConfigurations.AddAsync(
            TenantSourceConfiguration.Create(
                _tenantId,
                "paged-source",
                "Paged Source",
                true,
                "0 * * * *"
            )
        );
        await _dbContext.SaveChangesAsync();

        var assetBatchInterface = (IAssetInventoryBatchSource)batchSource;
        assetBatchInterface
            .FetchAssetBatchAsync(_tenantId, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                new SourceBatchResult<IngestionAssetInventorySnapshot>(
                    [
                        new IngestionAssetInventorySnapshot(
                            [
                                new IngestionAsset("DEVICE-1", "Device 1", AssetType.Device),
                                new IngestionAsset("DEVICE-2", "Device 2", AssetType.Device),
                            ],
                            []
                        ),
                    ],
                    "asset-cursor-2",
                    false
                )
            );
        assetBatchInterface
            .FetchAssetBatchAsync(_tenantId, "asset-cursor-2", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                new SourceBatchResult<IngestionAssetInventorySnapshot>(
                    [
                        new IngestionAssetInventorySnapshot(
                            [
                                new IngestionAsset("DEVICE-3", "Device 3", AssetType.Device),
                                new IngestionAsset("DEVICE-4", "Device 4", AssetType.Device),
                            ],
                            []
                        ),
                    ],
                    null,
                    true
                )
            );

        var vulnerabilityBatchInterface = (IVulnerabilityBatchSource)batchSource;
        vulnerabilityBatchInterface
            .FetchVulnerabilityBatchAsync(_tenantId, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                new SourceBatchResult<IngestionResult>(
                    [
                        new IngestionResult(
                            "CVE-2026-1001",
                            "Paged Vuln 1",
                            "Desc 1",
                            Severity.High,
                            8.0m,
                            null,
                            DateTimeOffset.UtcNow,
                            [new IngestionAffectedAsset("DEVICE-1", "Device 1", AssetType.Device)]
                        ),
                        new IngestionResult(
                            "CVE-2026-1002",
                            "Paged Vuln 2",
                            "Desc 2",
                            Severity.Critical,
                            9.0m,
                            null,
                            DateTimeOffset.UtcNow,
                            [new IngestionAffectedAsset("DEVICE-3", "Device 3", AssetType.Device)]
                        ),
                    ],
                    "vulnerability-cursor-2",
                    false
                )
            );
        vulnerabilityBatchInterface
            .FetchVulnerabilityBatchAsync(
                _tenantId,
                "vulnerability-cursor-2",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new SourceBatchResult<IngestionResult>(
                    [
                        new IngestionResult(
                            "CVE-2026-1003",
                            "Paged Vuln 3",
                            "Desc 3",
                            Severity.High,
                            7.5m,
                            null,
                            DateTimeOffset.UtcNow,
                            [new IngestionAffectedAsset("DEVICE-4", "Device 4", AssetType.Device)]
                        ),
                    ],
                    null,
                    true
                )
            );

        var logger = Substitute.For<ILogger<IngestionService>>();
        var taskProjectionService = new RemediationTaskProjectionService(
            _dbContext,
            new SlaService()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        var normalizedSoftwareResolver = new NormalizedSoftwareResolver(_dbContext);
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            _dbContext,
            normalizedSoftwareResolver
        );
        var softwareMatchService = new SoftwareVulnerabilityMatchService(
            _dbContext,
            normalizedSoftwareProjectionService
        );
        var enrichmentJobEnqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );
        var dbContextFactory = Substitute.For<IDbContextFactory<PatchHoundDbContext>>();
        dbContextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_dbContext);
        var stagedMergeService = new StagedVulnerabilityMergeService(
            _dbContext,
            dbContextFactory,
            assessmentService,
            taskProjectionService,
            new IngestionStateCache()
        );
        var stagedAssetMergeService = new StagedAssetMergeService(_dbContext);
        var service = new IngestionService(
            _dbContext,
            [batchSource],
            enrichmentJobEnqueuer,
            assessmentService,
            softwareMatchService,
            normalizedSoftwareProjectionService,
            taskProjectionService,
            stagedMergeService,
            stagedAssetMergeService,
            logger
        );

        var started = await service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();

        var run = await _dbContext
            .IngestionRuns.IgnoreQueryFilters()
            .SingleAsync(item => item.SourceKey == "paged-source");
        run.Status.Should().Be(IngestionRunStatuses.Succeeded);
        run.StagedMachineCount.Should().Be(4);
        run.StagedVulnerabilityCount.Should().Be(3);

        var assets = await _dbContext
            .Assets.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId)
            .ToListAsync();
        assets.Should().HaveCount(4);

        var tenantVulnerabilities = await _dbContext
            .TenantVulnerabilities.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId)
            .ToListAsync();
        tenantVulnerabilities.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunIngestionAsync_WhenAbortRequested_StopsRunAndMarksItFailedTerminal()
    {
        var batchSource = Substitute.For<
            IVulnerabilitySource,
            IAssetInventoryBatchSource,
            IVulnerabilityBatchSource
        >();
        batchSource.SourceKey.Returns("abort-source");
        batchSource.SourceName.Returns("AbortSource");

        await _dbContext.Tenants.AddAsync(Tenant.Create("Acme", _tenantId.ToString()));
        await _dbContext.TenantSourceConfigurations.AddAsync(
            TenantSourceConfiguration.Create(
                _tenantId,
                "abort-source",
                "Abort Source",
                true,
                "0 * * * *"
            )
        );
        await _dbContext.SaveChangesAsync();

        ((IAssetInventoryBatchSource)batchSource)
            .FetchAssetBatchAsync(_tenantId, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                new SourceBatchResult<IngestionAssetInventorySnapshot>(
                    [new IngestionAssetInventorySnapshot([], [])],
                    null,
                    true
                )
            );

        var vulnerabilityBatchInterface = (IVulnerabilityBatchSource)batchSource;
        var fetchCount = 0;
        vulnerabilityBatchInterface
            .FetchVulnerabilityBatchAsync(
                _tenantId,
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                fetchCount++;
                if (fetchCount == 1)
                {
                    var activeRun = _dbContext.IngestionRuns.IgnoreQueryFilters().Single();
                    activeRun.RequestAbort(DateTimeOffset.UtcNow);
                    _dbContext.SaveChanges();

                    return Task.FromResult(
                        new SourceBatchResult<IngestionResult>(
                            [
                                new IngestionResult(
                                    "CVE-2026-ABORT-1",
                                    "Abort vuln 1",
                                    "Desc",
                                    Severity.High,
                                    8.2m,
                                    null,
                                    DateTimeOffset.UtcNow,
                                    []
                                ),
                            ],
                            "vulnerability-cursor-2",
                            false
                        )
                    );
                }

                return Task.FromResult(
                    new SourceBatchResult<IngestionResult>(
                        [
                            new IngestionResult(
                                "CVE-2026-ABORT-2",
                                "Abort vuln 2",
                                "Desc",
                                Severity.Critical,
                                9.4m,
                                null,
                                DateTimeOffset.UtcNow,
                                []
                            ),
                        ],
                        null,
                        true
                    )
                );
            });

        var logger = Substitute.For<ILogger<IngestionService>>();
        var taskProjectionService = new RemediationTaskProjectionService(
            _dbContext,
            new SlaService()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        var normalizedSoftwareResolver = new NormalizedSoftwareResolver(_dbContext);
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            _dbContext,
            normalizedSoftwareResolver
        );
        var softwareMatchService = new SoftwareVulnerabilityMatchService(
            _dbContext,
            normalizedSoftwareProjectionService
        );
        var enrichmentJobEnqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );
        var dbContextFactory = Substitute.For<IDbContextFactory<PatchHoundDbContext>>();
        dbContextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_dbContext);
        var stagedMergeService = new StagedVulnerabilityMergeService(
            _dbContext,
            dbContextFactory,
            assessmentService,
            taskProjectionService,
            new IngestionStateCache()
        );
        var stagedAssetMergeService = new StagedAssetMergeService(_dbContext);
        var service = new IngestionService(
            _dbContext,
            [batchSource],
            enrichmentJobEnqueuer,
            assessmentService,
            softwareMatchService,
            normalizedSoftwareProjectionService,
            taskProjectionService,
            stagedMergeService,
            stagedAssetMergeService,
            logger
        );

        var started = await service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();
        var run = await _dbContext
            .IngestionRuns.IgnoreQueryFilters()
            .SingleAsync(item => item.SourceKey == "abort-source");
        run.Status.Should().Be(IngestionRunStatuses.FailedTerminal);
        run.Error.Should().Be("Ingestion failed: the run was aborted by an operator.");
        run.CompletedAt.Should().NotBeNull();
        (
            await _dbContext
                .StagedVulnerabilities.IgnoreQueryFilters()
                .CountAsync(item => item.IngestionRunId == run.Id)
        ).Should().Be(1);
    }

    [Fact]
    public async Task RunIngestionAsync_WhenPreviouslyAbortedRunHasLiveLease_StartsNewRun()
    {
        var tenant = Tenant.Create("Acme", _tenantId.ToString());
        await _dbContext.Tenants.AddAsync(tenant);

        var sourceConfiguration = TenantSourceConfiguration.Create(
            _tenantId,
            "resume-after-abort",
            "Resume After Abort",
            true,
            "0 * * * *"
        );
        var abortedRun = IngestionRun.Start(
            _tenantId,
            "resume-after-abort",
            DateTimeOffset.UtcNow.AddMinutes(-5)
        );
        sourceConfiguration.AcquireLease(
            abortedRun.Id,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(20)
        );
        abortedRun.RequestAbort(DateTimeOffset.UtcNow.AddSeconds(-30));

        await _dbContext.TenantSourceConfigurations.AddAsync(sourceConfiguration);
        await _dbContext.IngestionRuns.AddAsync(abortedRun);
        await _dbContext.SaveChangesAsync();

        var batchSource = Substitute.For<IVulnerabilitySource, IVulnerabilityBatchSource>();
        ((IVulnerabilitySource)batchSource).SourceKey.Returns("resume-after-abort");
        ((IVulnerabilitySource)batchSource).SourceName.Returns("Resume After Abort");
        ((IVulnerabilityBatchSource)batchSource)
            .FetchVulnerabilityBatchAsync(
                _tenantId,
                null,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new SourceBatchResult<IngestionResult>(
                    [
                        new IngestionResult(
                            "CVE-2026-0001",
                            "Recovered",
                            "Recovered",
                            Severity.High,
                            8.0m,
                            null,
                            null,
                            []
                        ),
                    ],
                    null,
                    true
                )
            );

        var logger = Substitute.For<ILogger<IngestionService>>();
        var taskProjectionService = new RemediationTaskProjectionService(
            _dbContext,
            new SlaService()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        var normalizedSoftwareResolver = new NormalizedSoftwareResolver(_dbContext);
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            _dbContext,
            normalizedSoftwareResolver
        );
        var softwareMatchService = new SoftwareVulnerabilityMatchService(
            _dbContext,
            normalizedSoftwareProjectionService
        );
        var enrichmentJobEnqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );
        var dbContextFactory = Substitute.For<IDbContextFactory<PatchHoundDbContext>>();
        dbContextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_dbContext);
        var stagedMergeService = new StagedVulnerabilityMergeService(
            _dbContext,
            dbContextFactory,
            assessmentService,
            taskProjectionService,
            new IngestionStateCache()
        );
        var stagedAssetMergeService = new StagedAssetMergeService(_dbContext);
        var service = new IngestionService(
            _dbContext,
            [batchSource],
            enrichmentJobEnqueuer,
            assessmentService,
            softwareMatchService,
            normalizedSoftwareProjectionService,
            taskProjectionService,
            stagedMergeService,
            stagedAssetMergeService,
            logger
        );

        var started = await service.RunIngestionAsync(_tenantId, CancellationToken.None);

        started.Should().BeTrue();

        var runs = await _dbContext
            .IngestionRuns.IgnoreQueryFilters()
            .Where(item => item.SourceKey == "resume-after-abort")
            .OrderBy(item => item.StartedAt)
            .ToListAsync();

        runs.Should().HaveCount(2);
        runs[0].Status.Should().Be(IngestionRunStatuses.FailedTerminal);
        runs[0].Error.Should().Be("Ingestion failed: the run was aborted by an operator.");
        runs[0].CompletedAt.Should().NotBeNull();
        runs[1].Status.Should().Be(IngestionRunStatuses.Succeeded);

        var source = await _dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .SingleAsync(item => item.SourceKey == "resume-after-abort");
        source.ActiveIngestionRunId.Should().BeNull();
        source.LeaseAcquiredAt.Should().BeNull();
        source.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task RunIngestionAsync_PrunesExpiredStagingArtifactsForOldCompletedRuns()
    {
        var oldRun = IngestionRun.Start(
            _tenantId,
            "test-source",
            DateTimeOffset.UtcNow.AddDays(-10)
        );
        oldRun.CompleteSucceeded(DateTimeOffset.UtcNow.AddDays(-8), 1, 1, 1, 1, 1, 1);
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
                        string.Empty,
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
    public async Task RunIngestionAsync_PrunesFailedRunsOlderThan24Hours()
    {
        var failedRun = IngestionRun.Start(
            _tenantId,
            "test-source",
            DateTimeOffset.UtcNow.AddDays(-2)
        );
        failedRun.CompleteFailed(
            DateTimeOffset.UtcNow.AddHours(-30),
            "Timed out",
            IngestionRunStatuses.FailedRecoverable,
            1,
            1,
            1,
            1,
            1,
            1
        );
        await _dbContext.IngestionRuns.AddAsync(failedRun);
        await _dbContext.StagedVulnerabilities.AddAsync(
            StagedVulnerability.Create(
                failedRun.Id,
                _tenantId,
                "test-source",
                "CVE-FAILED-1",
                "Failed vuln",
                Severity.Low,
                System.Text.Json.JsonSerializer.Serialize(
                    new IngestionResult(
                        "CVE-FAILED-1",
                        "Failed vuln",
                        string.Empty,
                        Severity.Low,
                        null,
                        null,
                        null,
                        []
                    )
                ),
                DateTimeOffset.UtcNow.AddHours(-30)
            )
        );
        await _dbContext.IngestionCheckpoints.AddAsync(
            IngestionCheckpoint.Start(
                failedRun.Id,
                _tenantId,
                "test-source",
                "vulnerability-staging",
                DateTimeOffset.UtcNow.AddHours(-30)
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

        (await _dbContext.IngestionRuns.IgnoreQueryFilters().AnyAsync(item => item.Id == failedRun.Id))
            .Should()
            .BeFalse();
        (
            await _dbContext
                .StagedVulnerabilities.IgnoreQueryFilters()
                .AnyAsync(item => item.IngestionRunId == failedRun.Id)
        )
            .Should()
            .BeFalse();
        (
            await _dbContext
                .IngestionCheckpoints.IgnoreQueryFilters()
                .AnyAsync(item => item.IngestionRunId == failedRun.Id)
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
        var vuln = VulnerabilityDefinition.Create(
            "CVE-2025-0004",
            "Will Resolve",
            "Desc",
            Severity.Low,
            "TestSource"
        );
        await _dbContext.VulnerabilityDefinitions.AddAsync(vuln);
        var tenantVulnerability = await CreateTenantVulnerabilityAsync(vuln);

        var asset = Asset.Create(
            _tenantId,
            "ASSET-4",
            AssetType.Device,
            "Server4",
            Criticality.Low
        );
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var va = VulnerabilityAsset.Create(
            tenantVulnerability.Id,
            asset.Id,
            DateTimeOffset.UtcNow
        );
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerability.Id,
            asset.Id,
            1,
            va.DetectedDate
        );
        episode.MarkMissing();
        await _dbContext.VulnerabilityAssets.AddAsync(va);
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(episode);

        var task = RemediationTask.Create(
            tenantVulnerability.Id,
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
            .TenantVulnerabilities.IgnoreQueryFilters()
            .FirstAsync(v => v.Id == tenantVulnerability.Id);
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
                    "aad-device-1",
                    "rbac-group-1",
                    "Tier 0 Servers"
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
        asset.DeviceGroupId.Should().Be("rbac-group-1");
        asset.DeviceGroupName.Should().Be("Tier 0 Servers");
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
    public async Task ProcessResultsAsync_CreatesSoftwareVulnerabilityMatches_FromDefenderDirectCorrelation()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var snapshot = new IngestionAssetInventorySnapshot(
            [
                new IngestionAsset("machine-1", "server01.contoso.local", AssetType.Device),
                new IngestionAsset(
                    "software-1",
                    "7-Zip 9.20",
                    AssetType.Software,
                    Metadata: """{"name":"7-Zip","vendor":"7-zip","version":"9.20"}"""
                ),
            ],
            [new("machine-1", "software-1", observedAt)]
        );

        await _service.ProcessAssetsAsync(_tenantId, snapshot, CancellationToken.None);

        await _service.ProcessResultsAsync(
            _tenantId,
            "MicrosoftDefender",
            [
                new IngestionResult(
                    "CVE-2026-0001",
                    "7-Zip issue",
                    "Desc",
                    Severity.High,
                    8.1m,
                    "CVSS:3.1/AV:N",
                    DateTimeOffset.UtcNow,
                    [
                        new IngestionAffectedAsset(
                            "machine-1",
                            "server01.contoso.local",
                            AssetType.Device,
                            "7-zip",
                            "7zip",
                            "9.20"
                        ),
                    ],
                    "7-zip",
                    "7zip",
                    "9.20",
                    Sources: ["MicrosoftDefender"]
                ),
            ],
            CancellationToken.None
        );

        var match = await _dbContext
            .SoftwareVulnerabilityMatches.IgnoreQueryFilters()
            .Include(item => item.VulnerabilityDefinition)
            .Include(item => item.SoftwareAsset)
            .SingleAsync();

        match.MatchMethod.Should().Be(SoftwareVulnerabilityMatchMethod.DefenderDirect);
        match.Confidence.Should().Be(MatchConfidence.High);
        match.VulnerabilityDefinition.ExternalId.Should().Be("CVE-2026-0001");
        match.SoftwareAsset.ExternalId.Should().Be("software-1");
        match.Evidence.Should().Contain("defender-direct");
    }

    [Fact]
    public async Task ProcessResultsAsync_CreatesSoftwareVulnerabilityMatches_ForMultipleObservedVersions()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var snapshot = new IngestionAssetInventorySnapshot(
            [
                new IngestionAsset("machine-1", "reader-1", AssetType.Device),
                new IngestionAsset("machine-2", "reader-2", AssetType.Device),
                new IngestionAsset(
                    "software-1",
                    "Acrobat Reader DC 2025.1.20813.0",
                    AssetType.Software,
                    Metadata: """{"name":"acrobat_reader_dc","vendor":"adobe","version":"2025.1.20813.0"}"""
                ),
                new IngestionAsset(
                    "software-2",
                    "Acrobat Reader DC 2025.1.20814.0",
                    AssetType.Software,
                    Metadata: """{"name":"acrobat_reader_dc","vendor":"adobe","version":"2025.1.20814.0"}"""
                ),
            ],
            [new("machine-1", "software-1", observedAt), new("machine-2", "software-2", observedAt)]
        );

        await _service.ProcessAssetsAsync(_tenantId, snapshot, CancellationToken.None);

        await _service.ProcessResultsAsync(
            _tenantId,
            "MicrosoftDefender",
            [
                new IngestionResult(
                    "CVE-2025-64899",
                    "Acrobat Reader issue",
                    "Desc",
                    Severity.High,
                    8.1m,
                    "CVSS:3.1/AV:N",
                    DateTimeOffset.UtcNow,
                    [
                        new IngestionAffectedAsset(
                            "machine-1",
                            "reader-1",
                            AssetType.Device,
                            "adobe",
                            "acrobat_reader_dc",
                            "2025.1.20813.0"
                        ),
                        new IngestionAffectedAsset(
                            "machine-2",
                            "reader-2",
                            AssetType.Device,
                            "adobe",
                            "acrobat_reader_dc",
                            "2025.1.20814.0"
                        ),
                    ],
                    "adobe",
                    "acrobat_reader_dc",
                    "2025.1.20813.0",
                    Sources: ["MicrosoftDefender"]
                ),
            ],
            CancellationToken.None
        );

        var matches = await _dbContext
            .SoftwareVulnerabilityMatches.IgnoreQueryFilters()
            .Include(item => item.SoftwareAsset)
            .Where(item => item.VulnerabilityDefinition.ExternalId == "CVE-2025-64899")
            .OrderBy(item => item.SoftwareAsset.ExternalId)
            .ToListAsync();

        matches.Should().HaveCount(2);
        matches.Select(item => item.SoftwareAsset.ExternalId).Should().Equal("software-1", "software-2");
        matches.Should().OnlyContain(item =>
            item.MatchMethod == SoftwareVulnerabilityMatchMethod.DefenderDirect
            && item.Confidence == MatchConfidence.High
        );
    }

    [Fact]
    public async Task ProcessResultsAsync_CreatesSoftwareVulnerabilityMatches_FromCpeAffectedSoftwareAndAutoBinding()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var snapshot = new IngestionAssetInventorySnapshot(
            [
                new IngestionAsset("machine-1", "server01.contoso.local", AssetType.Device),
                new IngestionAsset(
                    "7-zip-_-7-zip",
                    "7-Zip 9.20",
                    AssetType.Software,
                    Metadata: """{"name":"7-Zip","vendor":"7-zip","version":"9.20"}"""
                ),
            ],
            [new("machine-1", "7-zip-_-7-zip", observedAt)]
        );

        await _service.ProcessAssetsAsync(_tenantId, snapshot, CancellationToken.None);

        await _service.ProcessResultsAsync(
            _tenantId,
            "NVD",
            [
                new IngestionResult(
                    "CVE-2026-0002",
                    "7-Zip overflow",
                    "Desc",
                    Severity.High,
                    8.4m,
                    "CVSS:3.1/AV:N",
                    DateTimeOffset.UtcNow,
                    [
                        new IngestionAffectedAsset(
                            "machine-1",
                            "server01.contoso.local",
                            AssetType.Device
                        ),
                    ],
                    AffectedSoftware:
                    [
                        new IngestionAffectedSoftware(
                            true,
                            "cpe:2.3:a:7-zip:7zip:9.20:*:*:*:*:*:*:*",
                            null,
                            null,
                            null,
                            null
                        ),
                    ],
                    Sources: ["NVD"]
                ),
            ],
            CancellationToken.None
        );

        var match = await _dbContext
            .SoftwareVulnerabilityMatches.IgnoreQueryFilters()
            .Include(item => item.VulnerabilityDefinition)
            .Include(item => item.SoftwareAsset)
            .SingleAsync();

        match.MatchMethod.Should().Be(SoftwareVulnerabilityMatchMethod.CpeBinding);
        match.Confidence.Should().Be(MatchConfidence.High);
        match.VulnerabilityDefinition.ExternalId.Should().Be("CVE-2026-0002");
        match.SoftwareAsset.ExternalId.Should().Be("7-zip-_-7-zip");
        match.Evidence.Should().Contain("cpe-binding");
        match.Evidence.Should().Contain("cpe:2.3:a:7-zip:7zip:9.20");

        var bindings = await _dbContext.SoftwareCpeBindings.IgnoreQueryFilters().ToListAsync();
        bindings.Should().BeEmpty();
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

    [Theory]
    [InlineData("first-miss")]
    [InlineData("second-miss")]
    [InlineData("reappear-before-second-miss")]
    [InlineData("reappear-after-resolution")]
    public async Task ProcessResultsAsync_RecurringEpisodeLifecycle_HandlesExpectedScenario(
        string scenario
    )
    {
        var vulnerability = VulnerabilityDefinition.Create(
            $"CVE-2025-{scenario}",
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
        await _dbContext.VulnerabilityDefinitions.AddAsync(vulnerability);
        var tenantVulnerability = await CreateTenantVulnerabilityAsync(vulnerability);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        await SeedRecurringEpisodeScenarioAsync(scenario, tenantVulnerability, asset);

        var results = scenario switch
        {
            "reappear-before-second-miss" or "reappear-after-resolution" => CreateRecurringReappearanceResult(
                vulnerability.ExternalId
            ),
            _ => [],
        };

        await _service.ProcessResultsAsync(_tenantId, "TestSource", results, CancellationToken.None);

        var episodes = await _dbContext
            .VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .OrderBy(current => current.EpisodeNumber)
            .ToListAsync();
        var updatedProjection = await _dbContext
            .VulnerabilityAssets.IgnoreQueryFilters()
            .FirstAsync();
        var updatedVulnerability = await _dbContext
            .TenantVulnerabilities.IgnoreQueryFilters()
            .FirstAsync();

        switch (scenario)
        {
            case "first-miss":
                episodes.Should().HaveCount(1);
                episodes[0].Status.Should().Be(VulnerabilityStatus.Open);
                episodes[0].MissingSyncCount.Should().Be(1);
                episodes[0].ResolvedAt.Should().BeNull();
                updatedProjection.Status.Should().Be(VulnerabilityStatus.Open);
                updatedVulnerability.Status.Should().Be(VulnerabilityStatus.Open);
                break;
            case "second-miss":
                episodes.Should().HaveCount(1);
                episodes[0].Status.Should().Be(VulnerabilityStatus.Resolved);
                episodes[0].MissingSyncCount.Should().Be(2);
                episodes[0].ResolvedAt.Should().NotBeNull();
                updatedProjection.Status.Should().Be(VulnerabilityStatus.Resolved);
                updatedProjection.ResolvedDate.Should().NotBeNull();
                updatedVulnerability.Status.Should().Be(VulnerabilityStatus.Resolved);
                break;
            case "reappear-before-second-miss":
                episodes.Should().HaveCount(1);
                episodes[0].EpisodeNumber.Should().Be(1);
                episodes[0].MissingSyncCount.Should().Be(0);
                episodes[0].Status.Should().Be(VulnerabilityStatus.Open);
                updatedProjection.Status.Should().Be(VulnerabilityStatus.Open);
                updatedVulnerability.Status.Should().Be(VulnerabilityStatus.Open);
                break;
            case "reappear-after-resolution":
                episodes.Should().HaveCount(2);
                episodes[0].Status.Should().Be(VulnerabilityStatus.Resolved);
                episodes[1].EpisodeNumber.Should().Be(2);
                episodes[1].Status.Should().Be(VulnerabilityStatus.Open);
                updatedProjection.Status.Should().Be(VulnerabilityStatus.Open);
                updatedProjection.ResolvedDate.Should().BeNull();
                updatedVulnerability.Status.Should().Be(VulnerabilityStatus.Open);
                break;
            default:
                throw new InvalidOperationException($"Unknown test scenario '{scenario}'.");
        }
    }

    [Fact]
    public async Task RunIngestionAsync_WhenNvdConfigured_QueuesEnrichmentJobsForMergedVulnerabilities()
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

        var service = new IngestionService(
            _dbContext,
            [source],
            new EnrichmentJobEnqueuer(_dbContext, Substitute.For<ILogger<EnrichmentJobEnqueuer>>()),
            new VulnerabilityAssessmentService(_dbContext, new EnvironmentalSeverityCalculator()),
            new SoftwareVulnerabilityMatchService(
                _dbContext,
                new NormalizedSoftwareProjectionService(
                    _dbContext,
                    new NormalizedSoftwareResolver(_dbContext)
                )
            ),
            new NormalizedSoftwareProjectionService(
                _dbContext,
                new NormalizedSoftwareResolver(_dbContext)
            ),
            new RemediationTaskProjectionService(_dbContext, new SlaService()),
            new StagedVulnerabilityMergeService(
                _dbContext,
                CreateDbContextFactory(),
                new VulnerabilityAssessmentService(
                    _dbContext,
                    new EnvironmentalSeverityCalculator()
                ),
                new RemediationTaskProjectionService(_dbContext, new SlaService()),
                new IngestionStateCache()
            ),
            new StagedAssetMergeService(_dbContext),
            Substitute.For<ILogger<IngestionService>>()
        );

        await service.RunIngestionAsync(_tenantId, CancellationToken.None);

        var vulnerability = await _dbContext
            .VulnerabilityDefinitions.IgnoreQueryFilters()
            .SingleAsync(item => item.ExternalId == "CVE-2026-1234");
        var enrichmentJob = await _dbContext
            .EnrichmentJobs.IgnoreQueryFilters()
            .SingleAsync(item => item.TargetId == vulnerability.Id);

        vulnerability.ProductVendor.Should().Be("Contoso");
        vulnerability.ProductName.Should().Be("Contoso Agent");
        vulnerability.ProductVersion.Should().Be("1.0");
        enrichmentJob.SourceKey.Should().Be("nvd");
        enrichmentJob.TargetModel.Should().Be(EnrichmentTargetModel.Vulnerability);
        enrichmentJob.ExternalKey.Should().Be("CVE-2026-1234");
        enrichmentJob.Status.Should().Be(EnrichmentJobStatus.Pending);
    }

    private async Task SeedRecurringEpisodeScenarioAsync(
        string scenario,
        TenantVulnerability tenantVulnerability,
        Asset asset
    )
    {
        var detectedAt = scenario == "reappear-after-resolution"
            ? DateTimeOffset.UtcNow.AddDays(-1)
            : DateTimeOffset.UtcNow.AddHours(-2);
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerability.Id,
            asset.Id,
            1,
            detectedAt
        );
        var projection = VulnerabilityAsset.Create(tenantVulnerability.Id, asset.Id, detectedAt);

        switch (scenario)
        {
            case "first-miss":
                break;
            case "second-miss":
            case "reappear-before-second-miss":
                episode.MarkMissing();
                break;
            case "reappear-after-resolution":
            {
                var resolvedAt = DateTimeOffset.UtcNow.AddHours(-2);
                episode.MarkMissing();
                episode.MarkMissing();
                episode.Resolve(resolvedAt);
                projection.Resolve(resolvedAt);
                tenantVulnerability.UpdateStatus(VulnerabilityStatus.Resolved, resolvedAt);
                break;
            }
            default:
                throw new InvalidOperationException(
                    $"Unknown recurring episode test scenario '{scenario}'."
                );
        }

        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(episode);
        await _dbContext.VulnerabilityAssets.AddAsync(projection);
        await _dbContext.SaveChangesAsync();
    }

    private static List<IngestionResult> CreateRecurringReappearanceResult(string externalId) =>
        [
            new(
                externalId,
                "Recurring vulnerability",
                "Desc",
                Severity.High,
                8.0m,
                null,
                null,
                [new IngestionAffectedAsset("asset-1", "Server1", AssetType.Device)]
            ),
        ];

    public void Dispose() => _dbContext.Dispose();

    private static IServiceProvider BuildServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }

    private async Task<TenantVulnerability> CreateTenantVulnerabilityAsync(
        VulnerabilityDefinition vulnerability,
        VulnerabilityStatus status = VulnerabilityStatus.Open,
        string sourceKey = "TestSource"
    )
    {
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            vulnerability.Id,
            status,
            DateTimeOffset.UtcNow,
            sourceKey
        );

        await _dbContext.TenantVulnerabilities.AddAsync(tenantVulnerability);
        return tenantVulnerability;
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

    private IDbContextFactory<PatchHoundDbContext> CreateDbContextFactory()
    {
        var factory = Substitute.For<IDbContextFactory<PatchHoundDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_dbContext);
        return factory;
    }
}
