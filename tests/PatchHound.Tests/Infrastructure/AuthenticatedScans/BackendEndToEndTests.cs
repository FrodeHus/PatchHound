using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;
using Xunit;

namespace PatchHound.Tests.Infrastructure.AuthenticatedScans;

public class BackendEndToEndTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Full_backend_flow_dispatches_and_ingests_software()
    {
        // Arrange: seed device asset
        var device = Asset.Create(_tenantId, "ext-host-1", AssetType.Device, "host-1", Criticality.Medium);
        _db.Assets.Add(device);

        var conn = ConnectionProfile.Create(_tenantId, "conn", "", "host.example.com", 22, "user", "password", "secret/path", null);
        var runner = ScanRunner.Create(_tenantId, "runner", "", "hash");
        var tool = ScanningTool.Create(_tenantId, "tool", "", "python", "/usr/bin/python3", 300, "NormalizedSoftware");
        _db.ConnectionProfiles.Add(conn);
        _db.ScanRunners.Add(runner);
        _db.ScanningTools.Add(tool);
        await _db.SaveChangesAsync();

        // Publish a tool version
        var versionStore = new ScanningToolVersionStore(_db);
        await versionStore.PublishNewVersionAsync(tool.Id, "print('hello')", Guid.NewGuid(), CancellationToken.None);

        // Create scan profile with tool assignment and device assignment
        var profile = ScanProfile.Create(_tenantId, "profile", "", "0 * * * *", conn.Id, runner.Id, true);
        _db.ScanProfiles.Add(profile);
        _db.ScanProfileTools.Add(ScanProfileTool.Create(profile.Id, tool.Id, 0));
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, device.Id, profile.Id, null));
        await _db.SaveChangesAsync();

        // Act 1: dispatcher creates run + jobs
        var dispatcher = new ScanJobDispatcher(_db);
        var runId = await dispatcher.StartRunAsync(profile.Id, "manual", Guid.NewGuid(), CancellationToken.None);

        var run = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(AuthenticatedScanRunStatuses.Running, run.Status);
        Assert.Equal(1, run.TotalDevices);

        var job = await _db.ScanJobs.SingleAsync(j => j.RunId == runId);
        Assert.Equal(device.Id, job.AssetId);
        Assert.Equal(ScanJobStatuses.Pending, job.Status);

        // Act 2: simulate runner posting results — ingestion service processes them
        var rawOutput = """{"software":[{"name":"nginx","vendor":"nginx","version":"1.24.0"},{"name":"openssl","version":"3.0.2"}]}""";

        var stagedAssetMerge = new StagedAssetMergeService(_db);
        var resolver = new NormalizedSoftwareResolver(_db);
        var projectionService = new NormalizedSoftwareProjectionService(_db, resolver);
        var validator = new AuthenticatedScanOutputValidator();
        var ingestionService = new AuthenticatedScanIngestionService(_db, validator, stagedAssetMerge, projectionService);

        await ingestionService.ProcessJobResultAsync(job.Id, rawOutput, "", CancellationToken.None);

        // Assert: job succeeded
        var completedJob = await _db.ScanJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(ScanJobStatuses.Succeeded, completedJob.Status);
        Assert.Equal(2, completedJob.EntriesIngested);

        // Assert: result stored
        Assert.True(await _db.ScanJobResults.AnyAsync(r => r.ScanJobId == job.Id));

        // Assert: software assets created via staging pipeline
        var softwareAssets = await _db.Assets
            .Where(a => a.TenantId == _tenantId && a.AssetType == AssetType.Software && a.SourceKey == "authenticated-scan")
            .ToListAsync();
        Assert.Equal(2, softwareAssets.Count);
    }
}
