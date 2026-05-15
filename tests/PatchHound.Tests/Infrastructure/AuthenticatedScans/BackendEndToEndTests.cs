using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.Tests.TestData;
using Xunit;

namespace PatchHound.Tests.Infrastructure.AuthenticatedScans;

public class BackendEndToEndTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ValueTask InitializeAsync()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Full_backend_flow_dispatches_and_ingests_software()
    {
        // Arrange: seed device
        var sourceSystem = SourceSystem.Create("authenticated-scan", "Authenticated Scan");
        _db.SourceSystems.Add(sourceSystem);
        await _db.SaveChangesAsync();
        var device = Device.Create(_tenantId, sourceSystem.Id, "ext-host-1", "host-1", Criticality.Medium);
        _db.Devices.Add(device);

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
        _db.DeviceScanProfileAssignments.Add(DeviceScanProfileAssignment.Create(_tenantId, device.Id, profile.Id, null));
        await _db.SaveChangesAsync();

        // Act 1: dispatcher creates run + jobs
        var dispatcher = new ScanJobDispatcher(_db);
        var runId = await dispatcher.StartRunAsync(profile.Id, "manual", Guid.NewGuid(), CancellationToken.None);

        var run = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(AuthenticatedScanRunStatuses.Running, run.Status);
        Assert.Equal(1, run.TotalDevices);

        var job = await _db.ScanJobs.SingleAsync(j => j.RunId == runId);
        Assert.Equal(device.Id, job.DeviceId);
        Assert.Equal(ScanJobStatuses.Pending, job.Status);

        // Act 2: simulate runner posting results — ingestion service processes them
        var rawOutput = """{"software":[{"name":"nginx","vendor":"nginx","version":"1.24.0"},{"name":"openssl","version":"3.0.2"}]}""";

        var softwareResolver = new SoftwareProductResolver(_db);
        var stagedDeviceMerge = new StagedDeviceMergeService(_db, softwareResolver, new InMemoryBulkDeviceMergeWriter(_db));
        var projectionService = new NormalizedSoftwareProjectionService(_db);
        var validator = new AuthenticatedScanOutputValidator();
        var ingestionService = new AuthenticatedScanIngestionService(_db, validator, stagedDeviceMerge, projectionService, new InMemoryIngestionBulkWriter(_db));

        await ingestionService.ProcessJobResultAsync(job.Id, rawOutput, "", CancellationToken.None);

        // Assert: job succeeded
        var completedJob = await _db.ScanJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(ScanJobStatuses.Succeeded, completedJob.Status);
        Assert.Equal(2, completedJob.EntriesIngested);

        // Assert: result stored
        Assert.True(await _db.ScanJobResults.AnyAsync(r => r.ScanJobId == job.Id));

        // Assert: staged rows are cleaned up after merge (no orphaned staging rows)
        var stagedSoftware = await _db.StagedDevices.IgnoreQueryFilters()
            .Where(s => s.TenantId == _tenantId && s.AssetType == AssetType.Software && s.SourceKey == "authenticated-scan")
            .ToListAsync();
        Assert.Empty(stagedSoftware);
    }
}
