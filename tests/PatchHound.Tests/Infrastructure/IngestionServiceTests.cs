using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

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
            .Options;

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(tenantContext));

        _source = Substitute.For<IVulnerabilitySource>();
        _source.SourceName.Returns("TestSource");

        var logger = Substitute.For<ILogger<IngestionService>>();
        _service = new IngestionService(_dbContext, new[] { _source }, logger);
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
    public async Task ResolvedVulnerability_MarksVulnerabilityAssetResolved()
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
        await _dbContext.VulnerabilityAssets.AddAsync(va);

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

    public void Dispose() => _dbContext.Dispose();

    private static IServiceProvider BuildServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }
}
