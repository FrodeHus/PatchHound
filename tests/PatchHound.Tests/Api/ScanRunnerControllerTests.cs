using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PatchHound.Api.Auth;
using PatchHound.Api.Controllers;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.Tests.TestData;
using Xunit;

namespace PatchHound.Tests.Api;

public class ScanRunnerControllerTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private ScanRunnerController _sut = null!;
    private ISecretStore _secretStore = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private ScanRunner _runner = null!;
    private ConnectionProfile _conn = null!;
    private ScanProfile _profile = null!;
    private Device _device = null!;

    public async ValueTask InitializeAsync()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));

        _runner = ScanRunner.Create(_tenantId, "runner-1", "test", "hash");
        _conn = ConnectionProfile.Create(_tenantId, "conn-1", "", "host.example.com", 22, "admin", "password", "tenants/t/auth-scan-connections/c", null);
        _db.ScanRunners.Add(_runner);
        _db.ConnectionProfiles.Add(_conn);

        _profile = ScanProfile.Create(_tenantId, "profile-1", "", "0 * * * *", _conn.Id, _runner.Id, true);
        _db.ScanProfiles.Add(_profile);

        var tool = ScanningTool.Create(_tenantId, "tool-1", "", "python", "/usr/bin/python3", 300, "NormalizedSoftware");
        var version = ScanningToolVersion.Create(tool.Id, 1, "import json; print(json.dumps({'software':[]}))", Guid.NewGuid());
        tool.SetCurrentVersion(version.Id);
        _db.ScanningTools.Add(tool);
        _db.ScanningToolVersions.Add(version);
        _db.ScanProfileTools.Add(ScanProfileTool.Create(_profile.Id, tool.Id, 0));

        var sourceSystem = SourceSystem.Create("authenticated-scan", "Authenticated Scan");
        _db.SourceSystems.Add(sourceSystem);
        await _db.SaveChangesAsync();
        _device = Device.Create(_tenantId, sourceSystem.Id, "ext-device-1", "device-1", Criticality.Medium);
        _db.Devices.Add(_device);
        _db.DeviceScanProfileAssignments.Add(DeviceScanProfileAssignment.Create(_tenantId, _device.Id, _profile.Id, null));
        await _db.SaveChangesAsync();

        _secretStore = Substitute.For<ISecretStore>();
        _secretStore.GetSecretAsync(Arg.Any<string>(), "password", Arg.Any<CancellationToken>())
            .Returns("s3cret");

        var softwareResolver = new SoftwareProductResolver(_db);
        var stagedDeviceMerge = new StagedDeviceMergeService(_db, softwareResolver, new InMemoryBulkDeviceMergeWriter(_db));
        var projectionService = new NormalizedSoftwareProjectionService(_db);
        var validator = new AuthenticatedScanOutputValidator();
        var ingestionService = new AuthenticatedScanIngestionService(_db, validator, stagedDeviceMerge, projectionService, new InMemoryIngestionBulkWriter(_db));
        var completionService = new ScanRunCompletionService(_db);

        _sut = new ScanRunnerController(_db, _secretStore, ingestionService, completionService);
        SetRunnerClaims(_runner.Id, _tenantId);
    }

    public ValueTask DisposeAsync() { _db.Dispose(); return ValueTask.CompletedTask; }

    private void SetRunnerClaims(Guid runnerId, Guid tenantId)
    {
        var claims = new[]
        {
            new Claim(ScanRunnerBearerHandler.RunnerIdClaim, runnerId.ToString()),
            new Claim(ScanRunnerBearerHandler.TenantIdClaim, tenantId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, ScanRunnerBearerHandler.SchemeName);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    [Fact]
    public async Task Heartbeat_updates_runner()
    {
        var result = await _sut.Heartbeat(
            new ScanRunnerController.HeartbeatRequest("2.1.0", "runner-host"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var runner = await _db.ScanRunners.SingleAsync(r => r.Id == _runner.Id);
        Assert.Equal("2.1.0", runner.Version);
        Assert.NotNull(runner.LastSeenAt);
    }

    [Fact]
    public async Task GetNextJob_returns_204_when_no_pending_jobs()
    {
        var result = await _sut.GetNextJob(CancellationToken.None);
        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task GetNextJob_dispatches_and_returns_job_payload()
    {
        // Create a run with a pending job
        var dispatcher = new ScanJobDispatcher(_db);
        var runId = await dispatcher.StartRunAsync(_profile.Id, "manual", null, CancellationToken.None);

        var result = await _sut.GetNextJob(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);

        // Verify job is now dispatched
        var job = await _db.ScanJobs.SingleAsync(j => j.RunId == runId);
        Assert.Equal(ScanJobStatuses.Dispatched, job.Status);
        Assert.NotNull(job.LeaseExpiresAt);
    }

    [Fact]
    public async Task GetNextJob_returns_503_when_secret_store_fails()
    {
        _secretStore.GetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("OpenBao unavailable"));

        var dispatcher = new ScanJobDispatcher(_db);
        await dispatcher.StartRunAsync(_profile.Id, "manual", null, CancellationToken.None);

        var result = await _sut.GetNextJob(CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, statusResult.StatusCode);

        // Job should still be pending
        var job = await _db.ScanJobs.SingleAsync();
        Assert.Equal(ScanJobStatuses.Pending, job.Status);
    }

    [Fact]
    public async Task JobHeartbeat_extends_lease()
    {
        var dispatcher = new ScanJobDispatcher(_db);
        var runId = await dispatcher.StartRunAsync(_profile.Id, "manual", null, CancellationToken.None);
        var job = await _db.ScanJobs.SingleAsync(j => j.RunId == runId);
        job.Dispatch(DateTimeOffset.UtcNow.AddMinutes(10));
        await _db.SaveChangesAsync();

        var originalLease = job.LeaseExpiresAt;
        var result = await _sut.JobHeartbeat(job.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var updated = await _db.ScanJobs.SingleAsync(j => j.Id == job.Id);
        Assert.True(updated.LeaseExpiresAt > originalLease);
    }

    [Fact]
    public async Task PostResult_succeeded_triggers_ingestion_and_completion()
    {
        var dispatcher = new ScanJobDispatcher(_db);
        var runId = await dispatcher.StartRunAsync(_profile.Id, "manual", null, CancellationToken.None);
        var job = await _db.ScanJobs.SingleAsync(j => j.RunId == runId);
        job.Dispatch(DateTimeOffset.UtcNow.AddMinutes(10));
        await _db.SaveChangesAsync();

        var result = await _sut.PostResult(job.Id,
            new ScanRunnerController.PostResultRequest(
                "Succeeded",
                """{"software":[{"name":"nginx","version":"1.24.0"}]}""",
                "",
                null),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);

        var completedJob = await _db.ScanJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(ScanJobStatuses.Succeeded, completedJob.Status);
        Assert.Equal(1, completedJob.EntriesIngested);

        // Run should be completed since it only had one job
        var run = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(AuthenticatedScanRunStatuses.Succeeded, run.Status);
    }

    [Fact]
    public async Task PostResult_failed_marks_job_and_completes_run()
    {
        var dispatcher = new ScanJobDispatcher(_db);
        var runId = await dispatcher.StartRunAsync(_profile.Id, "manual", null, CancellationToken.None);
        var job = await _db.ScanJobs.SingleAsync(j => j.RunId == runId);
        job.Dispatch(DateTimeOffset.UtcNow.AddMinutes(10));
        await _db.SaveChangesAsync();

        var result = await _sut.PostResult(job.Id,
            new ScanRunnerController.PostResultRequest("Failed", "", "ssh error", "Connection refused"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var completedJob = await _db.ScanJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(ScanJobStatuses.Failed, completedJob.Status);

        var run = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(AuthenticatedScanRunStatuses.Failed, run.Status);
    }

    [Fact]
    public async Task PostResult_rejects_oversized_stdout()
    {
        var dispatcher = new ScanJobDispatcher(_db);
        var runId = await dispatcher.StartRunAsync(_profile.Id, "manual", null, CancellationToken.None);
        var job = await _db.ScanJobs.SingleAsync(j => j.RunId == runId);
        job.Dispatch(DateTimeOffset.UtcNow.AddMinutes(10));
        await _db.SaveChangesAsync();

        var oversizedStdout = new string('x', 2 * 1024 * 1024 + 1);

        var result = await _sut.PostResult(job.Id,
            new ScanRunnerController.PostResultRequest("Succeeded", oversizedStdout, "", null),
            CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(413, statusResult.StatusCode);
    }
}
