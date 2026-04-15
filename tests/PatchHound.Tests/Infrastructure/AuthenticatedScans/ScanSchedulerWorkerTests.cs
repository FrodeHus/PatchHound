using Cronos;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;
using Xunit;

namespace PatchHound.Tests.Infrastructure.AuthenticatedScans;

public class ScanSchedulerWorkerTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private ScanRunner _runner = null!;
    private ConnectionProfile _conn = null!;

    public async Task InitializeAsync()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));

        _runner = ScanRunner.Create(_tenantId, "runner", "", "hash");
        _conn = ConnectionProfile.Create(_tenantId, "conn", "", "h", 22, "u", "password", "p", null);
        _db.ScanRunners.Add(_runner);
        _db.ConnectionProfiles.Add(_conn);

        var tool = ScanningTool.Create(_tenantId, "t", "", "python", "/usr/bin/python3", 300, "NormalizedSoftware");
        var version = ScanningToolVersion.Create(tool.Id, 1, "print('hi')", Guid.NewGuid());
        tool.SetCurrentVersion(version.Id);
        _db.ScanningTools.Add(tool);
        _db.ScanningToolVersions.Add(version);

        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task TickAsync_dispatches_due_cron_profile()
    {
        // Profile with "every minute" cron, last run 2 minutes ago
        var profile = ScanProfile.Create(_tenantId, "p", "", "* * * * *", _conn.Id, _runner.Id, true);
        _db.ScanProfiles.Add(profile);
        _db.ScanProfileTools.Add(ScanProfileTool.Create(profile.Id,
            (await _db.ScanningTools.FirstAsync()).Id, 0));

        var device = Device.Create(_tenantId, Guid.NewGuid(), "ext-d1", "d1", Criticality.Medium);
        _db.Devices.Add(device);
        _db.DeviceScanProfileAssignments.Add(DeviceScanProfileAssignment.Create(_tenantId, device.Id, profile.Id, null));

        // Force LastRunStartedAt to 2 minutes ago
        profile.RecordRunStarted(DateTimeOffset.UtcNow.AddMinutes(-2));
        await _db.SaveChangesAsync();

        var dispatcher = new ScanJobDispatcher(_db);
        var completionService = new ScanRunCompletionService(_db);
        var logic = new ScanSchedulerTickHandler(_db, dispatcher, completionService);

        await logic.TickAsync(CancellationToken.None);

        Assert.True(await _db.AuthenticatedScanRuns.AnyAsync(r => r.ScanProfileId == profile.Id));
        Assert.True(await _db.ScanJobs.AnyAsync());
    }

    [Fact]
    public async Task TickAsync_skips_not_yet_due_profile()
    {
        // Profile with hourly cron, last run just now — next occurrence ~60min away
        var profile = ScanProfile.Create(_tenantId, "p2", "", "0 * * * *", _conn.Id, _runner.Id, true);
        _db.ScanProfiles.Add(profile);
        profile.RecordRunStarted(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync();

        var dispatcher = new ScanJobDispatcher(_db);
        var completionService = new ScanRunCompletionService(_db);
        var logic = new ScanSchedulerTickHandler(_db, dispatcher, completionService);

        await logic.TickAsync(CancellationToken.None);

        Assert.False(await _db.AuthenticatedScanRuns.AnyAsync(r => r.ScanProfileId == profile.Id));
    }

    [Fact]
    public async Task TickAsync_sweeps_expired_lease_under_max_attempts()
    {
        var profile = ScanProfile.Create(_tenantId, "p3", "", "", _conn.Id, _runner.Id, true);
        _db.ScanProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var run = AuthenticatedScanRun.Start(_tenantId, profile.Id, "manual", null, DateTimeOffset.UtcNow);
        run.MarkRunning(1);
        _db.AuthenticatedScanRuns.Add(run);

        var device = Device.Create(_tenantId, Guid.NewGuid(), "ext-d2", "d2", Criticality.Medium);
        _db.Devices.Add(device);
        var job = ScanJob.Create(_tenantId, run.Id, _runner.Id, device.Id, _conn.Id, "[]");
        _db.ScanJobs.Add(job);
        await _db.SaveChangesAsync();

        // Simulate dispatched with expired lease
        job.Dispatch(DateTimeOffset.UtcNow.AddMinutes(-5)); // expired 5 min ago
        await _db.SaveChangesAsync();

        var dispatcher = new ScanJobDispatcher(_db);
        var completionService = new ScanRunCompletionService(_db);
        var logic = new ScanSchedulerTickHandler(_db, dispatcher, completionService);

        await logic.TickAsync(CancellationToken.None);

        var updated = await _db.ScanJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(ScanJobStatuses.Pending, updated.Status);
    }

    [Fact]
    public async Task TickAsync_marks_expired_lease_failed_after_max_attempts()
    {
        var profile = ScanProfile.Create(_tenantId, "p4", "", "", _conn.Id, _runner.Id, true);
        _db.ScanProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var run = AuthenticatedScanRun.Start(_tenantId, profile.Id, "manual", null, DateTimeOffset.UtcNow);
        run.MarkRunning(1);
        _db.AuthenticatedScanRuns.Add(run);

        var device = Device.Create(_tenantId, Guid.NewGuid(), "ext-d3", "d3", Criticality.Medium);
        _db.Devices.Add(device);
        var job = ScanJob.Create(_tenantId, run.Id, _runner.Id, device.Id, _conn.Id, "[]");
        _db.ScanJobs.Add(job);
        await _db.SaveChangesAsync();

        // Simulate 3 dispatch attempts all expired
        job.Dispatch(DateTimeOffset.UtcNow.AddMinutes(-1));
        job.ReturnToPending("expired");
        job.Dispatch(DateTimeOffset.UtcNow.AddMinutes(-1));
        job.ReturnToPending("expired");
        job.Dispatch(DateTimeOffset.UtcNow.AddMinutes(-5)); // 3rd attempt, now expired
        await _db.SaveChangesAsync();

        var dispatcher = new ScanJobDispatcher(_db);
        var completionService = new ScanRunCompletionService(_db);
        var logic = new ScanSchedulerTickHandler(_db, dispatcher, completionService);

        await logic.TickAsync(CancellationToken.None);

        var updated = await _db.ScanJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(ScanJobStatuses.Failed, updated.Status);
        Assert.Contains("unreachable", updated.ErrorMessage);

        // Run should be completed
        var completedRun = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == run.Id);
        Assert.Equal(AuthenticatedScanRunStatuses.Failed, completedRun.Status);
    }

    [Fact]
    public async Task TickAsync_sweeps_stale_pending_jobs()
    {
        var profile = ScanProfile.Create(_tenantId, "p5", "", "", _conn.Id, _runner.Id, true);
        _db.ScanProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var run = AuthenticatedScanRun.Start(_tenantId, profile.Id, "manual", null,
            DateTimeOffset.UtcNow.AddHours(-3)); // started 3 hours ago
        run.MarkRunning(1);
        _db.AuthenticatedScanRuns.Add(run);

        var device = Device.Create(_tenantId, Guid.NewGuid(), "ext-d4", "d4", Criticality.Medium);
        _db.Devices.Add(device);
        var job = ScanJob.Create(_tenantId, run.Id, _runner.Id, device.Id, _conn.Id, "[]");
        _db.ScanJobs.Add(job);
        await _db.SaveChangesAsync();

        var dispatcher = new ScanJobDispatcher(_db);
        var completionService = new ScanRunCompletionService(_db);
        var logic = new ScanSchedulerTickHandler(_db, dispatcher, completionService);

        await logic.TickAsync(CancellationToken.None);

        var updated = await _db.ScanJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(ScanJobStatuses.Failed, updated.Status);
        Assert.Contains("never picked up", updated.ErrorMessage);
    }
}
