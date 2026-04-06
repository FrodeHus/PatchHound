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

public class ScanJobDispatcherTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private ScanJobDispatcher _sut = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private ScanProfile _profile = null!;
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

        _runner = ScanRunner.Create(_tenantId, "runner-1", "test runner", "hash123");
        _conn = ConnectionProfile.Create(_tenantId, "conn-1", "test conn", "host.example.com", 22, "user", "password", "secret/path", null);
        _db.ScanRunners.Add(_runner);
        _db.ConnectionProfiles.Add(_conn);

        _profile = ScanProfile.Create(_tenantId, "profile-1", "test", "0 * * * *", _conn.Id, _runner.Id, true);
        _db.ScanProfiles.Add(_profile);

        // One tool with a current version
        var tool = ScanningTool.Create(_tenantId, "tool-1", "test tool", "python", "/usr/bin/python3", 300, "NormalizedSoftware");
        var version = ScanningToolVersion.Create(tool.Id, 1, "print('hello')", Guid.NewGuid());
        tool.SetCurrentVersion(version.Id);
        _db.ScanningTools.Add(tool);
        _db.ScanningToolVersions.Add(version);
        _db.ScanProfileTools.Add(ScanProfileTool.Create(_profile.Id, tool.Id, 0));

        await _db.SaveChangesAsync();
        _sut = new ScanJobDispatcher(_db);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> SeedDeviceAsync(string name)
    {
        var asset = Asset.Create(_tenantId, $"ext-{name}", AssetType.Device, name, Criticality.Medium);
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        return asset.Id;
    }

    [Fact]
    public async Task StartRun_creates_one_job_per_assigned_asset()
    {
        var a1 = await SeedDeviceAsync("host-1");
        var a2 = await SeedDeviceAsync("host-2");
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, a1, _profile.Id, null));
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, a2, _profile.Id, null));
        await _db.SaveChangesAsync();

        var runId = await _sut.StartRunAsync(_profile.Id, "scheduled", null, CancellationToken.None);

        var run = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(2, run.TotalDevices);
        Assert.Equal(AuthenticatedScanRunStatuses.Running, run.Status);
        Assert.Equal(2, await _db.ScanJobs.CountAsync(j => j.RunId == runId));
    }

    [Fact]
    public async Task StartRun_with_zero_assets_marks_run_succeeded_immediately()
    {
        var runId = await _sut.StartRunAsync(_profile.Id, "scheduled", null, CancellationToken.None);
        var run = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(AuthenticatedScanRunStatuses.Succeeded, run.Status);
        Assert.Equal(0, run.TotalDevices);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task StartRun_throws_when_profile_has_active_run()
    {
        var a1 = await SeedDeviceAsync("host-1");
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, a1, _profile.Id, null));
        await _db.SaveChangesAsync();

        await _sut.StartRunAsync(_profile.Id, "scheduled", null, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartRunAsync(_profile.Id, "manual", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task StartRun_snapshots_current_tool_version_ids_into_job()
    {
        var a1 = await SeedDeviceAsync("host-1");
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, a1, _profile.Id, null));
        await _db.SaveChangesAsync();

        var runId = await _sut.StartRunAsync(_profile.Id, "scheduled", null, CancellationToken.None);
        var job = await _db.ScanJobs.SingleAsync(j => j.RunId == runId);
        Assert.NotEqual("[]", job.ScanningToolVersionIdsJson);
        Assert.Contains("\"", job.ScanningToolVersionIdsJson);
    }
}
