using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;
using Xunit;

namespace PatchHound.Tests.Api;

public class AuthenticatedScanRunsControllerTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private AuthenticatedScanRunsController _sut = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private ScanProfile _profile = null!;
    private AuthenticatedScanRun _completedRun = null!;

    public async Task InitializeAsync()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        tenantContext.HasAccessToTenant(_tenantId).Returns(true);
        tenantContext.HasAccessToTenant(_otherTenantId).Returns(false);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));

        var runner = ScanRunner.Create(_tenantId, "runner-1", "test", "hash");
        var conn = ConnectionProfile.Create(_tenantId, "conn-1", "", "host.example.com", 22, "admin", "password", "secrets/t/c", null);
        _db.ScanRunners.Add(runner);
        _db.ConnectionProfiles.Add(conn);

        _profile = ScanProfile.Create(_tenantId, "profile-1", "desc", "0 * * * *", conn.Id, runner.Id, true);
        _db.ScanProfiles.Add(_profile);

        // Create a completed run with one succeeded and one failed job
        _completedRun = AuthenticatedScanRun.Start(_tenantId, _profile.Id, "manual", null, DateTimeOffset.UtcNow.AddHours(-1));
        _completedRun.MarkRunning(2);
        _completedRun.Complete(1, 1, 5, DateTimeOffset.UtcNow);
        _db.AuthenticatedScanRuns.Add(_completedRun);

        var device1 = Asset.Create(_tenantId, "ext-1", AssetType.Device, "server-1", Criticality.Medium);
        var device2 = Asset.Create(_tenantId, "ext-2", AssetType.Device, "server-2", Criticality.Medium);
        _db.Assets.AddRange(device1, device2);

        var job1 = ScanJob.Create(_tenantId, _completedRun.Id, runner.Id, device1.Id, conn.Id, "[]");
        job1.Dispatch(DateTimeOffset.UtcNow.AddMinutes(10));
        job1.CompleteSucceeded(100, 0, 5, DateTimeOffset.UtcNow);

        var job2 = ScanJob.Create(_tenantId, _completedRun.Id, runner.Id, device2.Id, conn.Id, "[]");
        job2.Dispatch(DateTimeOffset.UtcNow.AddMinutes(10));
        job2.CompleteFailed("Failed", "Connection refused", DateTimeOffset.UtcNow);

        _db.ScanJobs.AddRange(job1, job2);

        var issue = ScanJobValidationIssue.Create(job1.Id, "software[0].version", "Version string too long", 0);
        _db.ScanJobValidationIssues.Add(issue);

        await _db.SaveChangesAsync();

        _sut = new AuthenticatedScanRunsController(_db, tenantContext);
    }

    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task List_returns_paged_runs_for_tenant()
    {
        var result = await _sut.List(_tenantId, null, new PaginationQuery(), CancellationToken.None);

        var okResult = Assert.IsType<ActionResult<PagedResponse<AuthenticatedScanRunsController.ScanRunListDto>>>(result);
        var response = okResult.Value!;
        Assert.Equal(1, response.TotalCount);
        var run = response.Items[0];
        Assert.Equal(_completedRun.Id, run.Id);
        Assert.Equal("profile-1", run.ProfileName);
        Assert.Equal("manual", run.TriggerKind);
        Assert.Equal(AuthenticatedScanRunStatuses.PartiallyFailed, run.Status);
        Assert.Equal(2, run.TotalDevices);
        Assert.Equal(1, run.SucceededCount);
        Assert.Equal(1, run.FailedCount);
        Assert.Equal(5, run.EntriesIngested);
    }

    [Fact]
    public async Task List_filters_by_profileId()
    {
        var result = await _sut.List(_tenantId, Guid.NewGuid(), new PaginationQuery(), CancellationToken.None);

        var response = result.Value!;
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task List_forbids_other_tenant()
    {
        var result = await _sut.List(_otherTenantId, null, new PaginationQuery(), CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetDetail_returns_run_with_job_summaries()
    {
        var result = await _sut.GetDetail(_completedRun.Id, CancellationToken.None);

        var okResult = Assert.IsType<ActionResult<AuthenticatedScanRunsController.ScanRunDetailDto>>(result);
        var detail = okResult.Value!;
        Assert.Equal(_completedRun.Id, detail.Id);
        Assert.Equal("profile-1", detail.ProfileName);
        Assert.Equal(2, detail.Jobs.Count);

        var succeeded = detail.Jobs.Single(j => j.Status == "Succeeded");
        Assert.Equal("server-1", succeeded.AssetName);
        Assert.Equal(5, succeeded.EntriesIngested);

        var failed = detail.Jobs.Single(j => j.Status == "Failed");
        Assert.Equal("server-2", failed.AssetName);
        Assert.Equal("Connection refused", failed.ErrorMessage);
    }

    [Fact]
    public async Task GetDetail_includes_validation_issues()
    {
        var result = await _sut.GetDetail(_completedRun.Id, CancellationToken.None);

        var detail = result.Value!;
        var jobWithIssues = detail.Jobs.Single(j => j.ValidationIssues.Count > 0);
        Assert.Single(jobWithIssues.ValidationIssues);
        Assert.Equal("software[0].version", jobWithIssues.ValidationIssues[0].FieldPath);
    }

    [Fact]
    public async Task GetDetail_returns_404_for_missing_run()
    {
        var result = await _sut.GetDetail(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetDetail_forbids_other_tenant_run()
    {
        // Create a run in another tenant
        var otherRun = AuthenticatedScanRun.Start(_otherTenantId, Guid.NewGuid(), "manual", null, DateTimeOffset.UtcNow);
        _db.AuthenticatedScanRuns.Add(otherRun);
        await _db.SaveChangesAsync();

        var result = await _sut.GetDetail(otherRun.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }
}
