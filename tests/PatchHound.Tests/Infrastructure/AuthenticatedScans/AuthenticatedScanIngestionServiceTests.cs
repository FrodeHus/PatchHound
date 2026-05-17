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

public class AuthenticatedScanIngestionServiceTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private AuthenticatedScanIngestionService _sut = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private ScanJob _job = null!;

    public async ValueTask InitializeAsync()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));

        // Seed minimal prerequisite data
        var conn = ConnectionProfile.Create(_tenantId, "c", "", "h", 22, "u", "password", "p", null);
        var runner = ScanRunner.Create(_tenantId, "r", "", "hash");
        _db.ConnectionProfiles.Add(conn);
        _db.ScanRunners.Add(runner);
        var profile = ScanProfile.Create(_tenantId, "p", "", "", conn.Id, runner.Id, true);
        _db.ScanProfiles.Add(profile);
        var device = Device.Create(_tenantId, Guid.NewGuid(), $"ext-{Guid.NewGuid()}", "host", Criticality.Medium);
        _db.Devices.Add(device);
        var run = AuthenticatedScanRun.Start(_tenantId, profile.Id, "manual", null, DateTimeOffset.UtcNow);
        _db.AuthenticatedScanRuns.Add(run);
        _job = ScanJob.Create(_tenantId, run.Id, runner.Id, device.Id, conn.Id, "[]");
        _db.ScanJobs.Add(_job);
        await _db.SaveChangesAsync();

        // Stub merge — no real merge logic needed for this test
        var mergeService = Substitute.For<IStagedDeviceMergeService>();
        mergeService
            .MergeAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new StagedDeviceMergeSummary(0, 0, 0, 0));

        // Use the in-memory bulk writer (load+remove+SaveChanges) since ExecuteDeleteAsync
        // is not supported by the InMemory EF provider.
        var bulkWriter = new InMemoryIngestionBulkWriter(_db);

        // The constructor is internal; accessible here via InternalsVisibleTo("PatchHound.Tests").
        _sut = new AuthenticatedScanIngestionService(
            _db,
            new AuthenticatedScanOutputValidator(),
            mergeService,
            new NormalizedSoftwareProjectionService(new InMemoryBulkSoftwareProjectionWriter(_db)),
            bulkWriter);
    }

    public ValueTask DisposeAsync() { _db.Dispose(); return ValueTask.CompletedTask; }

    [Fact]
    public async Task IngestAsync_AfterMerge_StagedDeviceRowsAreDeleted()
    {
        // Valid scan output with one software entry
        var stdout = """{"software":[{"name":"nginx","vendor":"nginx","version":"1.24.0"}]}""";

        await _sut.ProcessJobResultAsync(_job.Id, stdout, string.Empty, CancellationToken.None);

        var remainingStagedDevices = await _db.StagedDevices
            .IgnoreQueryFilters()
            .CountAsync();
        var remainingStagedLinks = await _db.StagedDeviceSoftwareInstallations
            .IgnoreQueryFilters()
            .CountAsync();

        Assert.Equal(0, remainingStagedDevices);
        Assert.Equal(0, remainingStagedLinks);
    }
}
