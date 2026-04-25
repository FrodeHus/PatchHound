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

public class ScanRunCompletionServiceTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private ScanRunCompletionService _sut = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private ScanProfile _profile = null!;

    public async ValueTask InitializeAsync()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));

        var conn = ConnectionProfile.Create(_tenantId, "c", "", "h", 22, "u", "password", "p", null);
        var runner = ScanRunner.Create(_tenantId, "r", "", "hash");
        _db.ConnectionProfiles.Add(conn);
        _db.ScanRunners.Add(runner);
        _profile = ScanProfile.Create(_tenantId, "p", "", "", conn.Id, runner.Id, true);
        _db.ScanProfiles.Add(_profile);
        await _db.SaveChangesAsync();

        _sut = new ScanRunCompletionService(_db);
    }

    public ValueTask DisposeAsync() { _db.Dispose(); return ValueTask.CompletedTask; }

    private AuthenticatedScanRun CreateRun()
    {
        var run = AuthenticatedScanRun.Start(_tenantId, _profile.Id, "manual", null, DateTimeOffset.UtcNow);
        _db.AuthenticatedScanRuns.Add(run);
        return run;
    }

    private ScanJob CreateJob(Guid runId, Guid runnerId)
    {
        var device = Device.Create(_tenantId, Guid.NewGuid(), $"ext-{Guid.NewGuid()}", "host", Criticality.Medium);
        _db.Devices.Add(device);
        var job = ScanJob.Create(_tenantId, runId, runnerId, device.Id, _profile.ConnectionProfileId, "[]");
        _db.ScanJobs.Add(job);
        return job;
    }

    [Fact]
    public async Task TryCompleteRunAsync_all_succeeded_marks_run_succeeded()
    {
        var run = CreateRun();
        var job1 = CreateJob(run.Id, _profile.ScanRunnerId);
        var job2 = CreateJob(run.Id, _profile.ScanRunnerId);
        run.MarkRunning(2);
        await _db.SaveChangesAsync();

        job1.CompleteSucceeded(100, 0, 5, DateTimeOffset.UtcNow);
        job2.CompleteSucceeded(200, 0, 3, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync();

        await _sut.TryCompleteRunAsync(run.Id, CancellationToken.None);

        var completed = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == run.Id);
        Assert.Equal(AuthenticatedScanRunStatuses.Succeeded, completed.Status);
        Assert.Equal(2, completed.SucceededCount);
        Assert.Equal(0, completed.FailedCount);
        Assert.Equal(8, completed.EntriesIngested);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task TryCompleteRunAsync_mixed_results_marks_partially_failed()
    {
        var run = CreateRun();
        var job1 = CreateJob(run.Id, _profile.ScanRunnerId);
        var job2 = CreateJob(run.Id, _profile.ScanRunnerId);
        run.MarkRunning(2);
        await _db.SaveChangesAsync();

        job1.CompleteSucceeded(100, 0, 5, DateTimeOffset.UtcNow);
        job2.CompleteFailed(ScanJobStatuses.Failed, "error", DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync();

        await _sut.TryCompleteRunAsync(run.Id, CancellationToken.None);

        var completed = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == run.Id);
        Assert.Equal(AuthenticatedScanRunStatuses.PartiallyFailed, completed.Status);
        Assert.Equal(1, completed.SucceededCount);
        Assert.Equal(1, completed.FailedCount);
        Assert.Equal(5, completed.EntriesIngested);
    }

    [Fact]
    public async Task TryCompleteRunAsync_all_failed_marks_run_failed()
    {
        var run = CreateRun();
        var job1 = CreateJob(run.Id, _profile.ScanRunnerId);
        run.MarkRunning(1);
        await _db.SaveChangesAsync();

        job1.CompleteFailed(ScanJobStatuses.Failed, "error", DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync();

        await _sut.TryCompleteRunAsync(run.Id, CancellationToken.None);

        var completed = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == run.Id);
        Assert.Equal(AuthenticatedScanRunStatuses.Failed, completed.Status);
    }

    [Fact]
    public async Task TryCompleteRunAsync_non_terminal_jobs_does_nothing()
    {
        var run = CreateRun();
        var job1 = CreateJob(run.Id, _profile.ScanRunnerId);
        var job2 = CreateJob(run.Id, _profile.ScanRunnerId);
        run.MarkRunning(2);
        await _db.SaveChangesAsync();

        job1.CompleteSucceeded(100, 0, 5, DateTimeOffset.UtcNow);
        // job2 still Pending
        await _db.SaveChangesAsync();

        await _sut.TryCompleteRunAsync(run.Id, CancellationToken.None);

        var r = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == run.Id);
        Assert.Equal(AuthenticatedScanRunStatuses.Running, r.Status);
        Assert.Null(r.CompletedAt);
    }

    [Fact]
    public async Task TryCompleteRunAsync_sums_entries_from_succeeded_jobs_only()
    {
        var run = CreateRun();
        var job1 = CreateJob(run.Id, _profile.ScanRunnerId);
        var job2 = CreateJob(run.Id, _profile.ScanRunnerId);
        run.MarkRunning(2);
        await _db.SaveChangesAsync();

        job1.CompleteSucceeded(100, 0, 10, DateTimeOffset.UtcNow);
        job2.CompleteFailed(ScanJobStatuses.TimedOut, "timeout", DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync();

        await _sut.TryCompleteRunAsync(run.Id, CancellationToken.None);

        var completed = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == run.Id);
        Assert.Equal(10, completed.EntriesIngested);
    }
}
