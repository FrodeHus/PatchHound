# Authenticated Scans Plan 2: Scheduler, Runner API & Run Completion

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add server-side scan orchestration — a scheduler worker for cron-based dispatch and stale cleanup, runner-facing API endpoints for job pulling and result posting, and run completion detection.

**Architecture:** Three components across existing projects: `ScanSchedulerWorker` (BackgroundService in PatchHound.Worker), `ScanRunnerController` (API endpoints behind custom ScanRunnerBearer auth scheme), and `ScanRunCompletionService` (shared logic for finalizing runs). The runner authenticates via SHA-256 hashed bearer tokens. Credentials are fetched JIT from OpenBao during job dispatch.

**Tech Stack:** .NET 10, ASP.NET Core custom AuthenticationHandler, EF Core, Cronos (cron parsing), xUnit + NSubstitute

---

## File Map

| File | Responsibility |
|------|---------------|
| `src/PatchHound.Api/Auth/ScanRunnerBearerHandler.cs` | Custom AuthenticationHandler — hashes bearer token, looks up runner, sets claims |
| `src/PatchHound.Api/Controllers/ScanRunnerController.cs` | Runner-facing endpoints: heartbeat, pull job, job heartbeat, post result |
| `src/PatchHound.Infrastructure/AuthenticatedScans/ScanRunCompletionService.cs` | Finalizes runs when all jobs are terminal |
| `src/PatchHound.Worker/ScanSchedulerWorker.cs` | 60s tick: cron evaluation, stale sweep |
| `src/PatchHound.Api/Auth/Policies.cs` | (modify) — no changes needed, runner uses its own scheme |
| `src/PatchHound.Api/Program.cs` | (modify) — register ScanRunnerBearer auth scheme |
| `src/PatchHound.Infrastructure/DependencyInjection.cs` | (modify) — register ScanRunCompletionService |
| `src/PatchHound.Worker/Program.cs` | (modify) — register ScanSchedulerWorker |
| `src/PatchHound.Worker/PatchHound.Worker.csproj` | (modify) — add Cronos package |
| `tests/.../ScanRunCompletionServiceTests.cs` | Tests for run completion logic |
| `tests/.../ScanSchedulerWorkerTests.cs` | Tests for cron eval + stale sweep |
| `tests/.../ScanRunnerControllerTests.cs` | Tests for runner API endpoints |

---

## Task 1: Add Cronos package

**Files:**
- Modify: `src/PatchHound.Worker/PatchHound.Worker.csproj`

- [ ] **Step 1: Add Cronos package reference**

```bash
dotnet add src/PatchHound.Worker/PatchHound.Worker.csproj package Cronos
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Worker/PatchHound.Worker.csproj
git commit -m "chore: add Cronos package for cron schedule parsing"
```

---

## Task 2: ScanRunCompletionService with tests (TDD)

**Files:**
- Create: `src/PatchHound.Infrastructure/AuthenticatedScans/ScanRunCompletionService.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/ScanRunCompletionServiceTests.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Write failing tests**

```csharp
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

    public async Task InitializeAsync()
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

    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    private AuthenticatedScanRun CreateRun()
    {
        var run = AuthenticatedScanRun.Start(_tenantId, _profile.Id, "manual", null, DateTimeOffset.UtcNow);
        _db.AuthenticatedScanRuns.Add(run);
        return run;
    }

    private ScanJob CreateJob(Guid runId, Guid runnerId)
    {
        var asset = Asset.Create(_tenantId, $"ext-{Guid.NewGuid()}", AssetType.Device, "host", Criticality.Medium);
        _db.Assets.Add(asset);
        var job = ScanJob.Create(_tenantId, runId, runnerId, asset.Id, _profile.ConnectionProfileId, "[]");
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
```

- [ ] **Step 2: Run tests — expected to fail**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanRunCompletionServiceTests`
Expected: FAIL (class doesn't exist).

- [ ] **Step 3: Implement ScanRunCompletionService**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class ScanRunCompletionService(PatchHoundDbContext db)
{
    private static readonly HashSet<string> TerminalStatuses =
    [
        ScanJobStatuses.Succeeded,
        ScanJobStatuses.Failed,
        ScanJobStatuses.TimedOut
    ];

    public async Task TryCompleteRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.AuthenticatedScanRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.CompletedAt.HasValue) return;

        var jobs = await db.ScanJobs.Where(j => j.RunId == runId).ToListAsync(ct);
        if (jobs.Count == 0) return;

        var allTerminal = jobs.All(j => TerminalStatuses.Contains(j.Status));
        if (!allTerminal) return;

        var succeeded = jobs.Count(j => j.Status == ScanJobStatuses.Succeeded);
        var failed = jobs.Count - succeeded;
        var entriesIngested = jobs
            .Where(j => j.Status == ScanJobStatuses.Succeeded)
            .Sum(j => j.EntriesIngested);

        run.Complete(succeeded, failed, entriesIngested, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Register in DI**

In `src/PatchHound.Infrastructure/DependencyInjection.cs`, after the `ScanJobDispatcher` registration, add:

```csharp
services.AddScoped<PatchHound.Infrastructure.AuthenticatedScans.ScanRunCompletionService>();
```

- [ ] **Step 5: Run tests — expected to pass**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanRunCompletionServiceTests`
Expected: all 5 pass.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/AuthenticatedScans/ScanRunCompletionService.cs \
  src/PatchHound.Infrastructure/DependencyInjection.cs \
  tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/ScanRunCompletionServiceTests.cs
git commit -m "feat: add ScanRunCompletionService with tests"
```

---

## Task 3: ScanRunnerBearer authentication handler

**Files:**
- Create: `src/PatchHound.Api/Auth/ScanRunnerBearerHandler.cs`
- Modify: `src/PatchHound.Api/Program.cs`

- [ ] **Step 1: Implement the handler**

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Auth;

public class ScanRunnerBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceProvider serviceProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ScanRunnerBearer";
    public const string RunnerIdClaim = "runner_id";
    public const string TenantIdClaim = "tenant_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.Fail("Empty bearer token");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        var runner = await db.ScanRunners
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.SecretHash == hash);

        if (runner is null)
        {
            return AuthenticateResult.Fail("Invalid bearer token");
        }

        if (!runner.Enabled)
        {
            return AuthenticateResult.Fail("Runner is disabled");
        }

        var claims = new[]
        {
            new Claim(RunnerIdClaim, runner.Id.ToString()),
            new Claim(TenantIdClaim, runner.TenantId.ToString()),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
```

- [ ] **Step 2: Register in Program.cs**

In `src/PatchHound.Api/Program.cs`, after the existing `AddAuthentication().AddMicrosoftIdentityWebApi(azureAdConfig)` line (line 29), add:

```csharp
builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, ScanRunnerBearerHandler>(
        ScanRunnerBearerHandler.SchemeName, _ => { });
```

Note: ASP.NET Core supports chaining `AddAuthentication()` calls — the second call adds an additional scheme without overriding the default.

- [ ] **Step 3: Build and verify**

Run: `dotnet build --nologo`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Api/Auth/ScanRunnerBearerHandler.cs src/PatchHound.Api/Program.cs
git commit -m "feat: add ScanRunnerBearer authentication handler"
```

---

## Task 4: ScanRunnerController with tests (TDD)

**Files:**
- Create: `src/PatchHound.Api/Controllers/ScanRunnerController.cs`
- Create: `tests/PatchHound.Tests/Api/ScanRunnerControllerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
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
    private Asset _device = null!;

    public async Task InitializeAsync()
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

        _device = Asset.Create(_tenantId, "ext-device-1", AssetType.Device, "device-1", Criticality.Medium);
        _db.Assets.Add(_device);
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, _device.Id, _profile.Id, null));
        await _db.SaveChangesAsync();

        _secretStore = Substitute.For<ISecretStore>();
        _secretStore.GetSecretAsync(Arg.Any<string>(), "password", Arg.Any<CancellationToken>())
            .Returns("s3cret");

        var stagedAssetMerge = new StagedAssetMergeService(_db);
        var resolver = new NormalizedSoftwareResolver(_db);
        var projectionService = new NormalizedSoftwareProjectionService(_db, resolver);
        var validator = new AuthenticatedScanOutputValidator();
        var ingestionService = new AuthenticatedScanIngestionService(_db, validator, stagedAssetMerge, projectionService);
        var completionService = new ScanRunCompletionService(_db);

        _sut = new ScanRunnerController(_db, _secretStore, ingestionService, completionService);
        SetRunnerClaims(_runner.Id, _tenantId);
    }

    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

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
```

- [ ] **Step 2: Run tests — expected to fail**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanRunnerControllerTests`
Expected: FAIL (class doesn't exist).

- [ ] **Step 3: Implement ScanRunnerController**

```csharp
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/scan-runner")]
[Authorize(AuthenticationSchemes = ScanRunnerBearerHandler.SchemeName)]
public class ScanRunnerController(
    PatchHoundDbContext db,
    ISecretStore secretStore,
    AuthenticatedScanIngestionService ingestionService,
    ScanRunCompletionService completionService) : ControllerBase
{
    private const int MaxStdoutBytes = 2 * 1024 * 1024;
    private const int MaxStderrBytes = 256 * 1024;

    public record HeartbeatRequest(string Version, string Hostname);
    public record PostResultRequest(string Status, string Stdout, string Stderr, string? ErrorMessage);

    public record JobPayload(
        Guid JobId, Guid AssetId,
        HostTarget HostTarget, Credentials Credentials,
        string? HostKeyFingerprint,
        List<ToolPayload> Tools,
        DateTimeOffset LeaseExpiresAt);

    public record HostTarget(string Host, int Port, string Username, string AuthMethod);
    public record Credentials(string? Password, string? PrivateKey, string? Passphrase);
    public record ToolPayload(
        Guid Id, string Name, string ScriptType, string InterpreterPath,
        int TimeoutSeconds, string ScriptContent, string OutputModel);

    private Guid GetRunnerId() =>
        Guid.Parse(User.FindFirstValue(ScanRunnerBearerHandler.RunnerIdClaim)!);
    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirstValue(ScanRunnerBearerHandler.TenantIdClaim)!);

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(
        [FromBody] HeartbeatRequest req, CancellationToken ct)
    {
        var runner = await db.ScanRunners.FirstOrDefaultAsync(
            r => r.Id == GetRunnerId(), ct);
        if (runner is null) return NotFound();

        runner.RecordHeartbeat(req.Version, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpGet("jobs/next")]
    public async Task<ActionResult<JobPayload>> GetNextJob(CancellationToken ct)
    {
        var runnerId = GetRunnerId();
        var tenantId = GetTenantId();

        var job = await db.ScanJobs
            .Where(j => j.TenantId == tenantId
                && j.ScanRunnerId == runnerId
                && j.Status == ScanJobStatuses.Pending)
            .OrderBy(j => j.Id)
            .FirstOrDefaultAsync(ct);

        if (job is null) return NoContent();

        // Load connection profile for credentials
        var connProfile = await db.ConnectionProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == job.ConnectionProfileId, ct);
        if (connProfile is null)
        {
            return StatusCode(500, new { error = "Connection profile not found" });
        }

        // Fetch credentials JIT from OpenBao
        string? password = null, privateKey = null, passphrase = null;
        try
        {
            if (connProfile.AuthMethod == "password")
            {
                password = await secretStore.GetSecretAsync(connProfile.SecretRef, "password", ct);
            }
            else if (connProfile.AuthMethod == "privateKey")
            {
                privateKey = await secretStore.GetSecretAsync(connProfile.SecretRef, "privateKey", ct);
                passphrase = await secretStore.GetSecretAsync(connProfile.SecretRef, "passphrase", ct);
            }
        }
        catch
        {
            return StatusCode(503, new { error = "Credential store unavailable" });
        }

        // Load tool versions
        var versionIds = JsonSerializer.Deserialize<List<Guid>>(job.ScanningToolVersionIdsJson) ?? [];
        var tools = await (
            from v in db.ScanningToolVersions.AsNoTracking()
            join t in db.ScanningTools.AsNoTracking() on v.ScanningToolId equals t.Id
            where versionIds.Contains(v.Id)
            select new ToolPayload(t.Id, t.Name, t.ScriptType, t.InterpreterPath,
                t.TimeoutSeconds, v.ScriptContent, t.OutputModel)
        ).ToListAsync(ct);

        // Dispatch the job
        var leaseExpiry = DateTimeOffset.UtcNow.AddMinutes(10);
        job.Dispatch(leaseExpiry);
        await db.SaveChangesAsync(ct);

        return Ok(new JobPayload(
            job.Id, job.AssetId,
            new HostTarget(connProfile.SshHost, connProfile.SshPort, connProfile.SshUsername, connProfile.AuthMethod),
            new Credentials(password, privateKey, passphrase),
            connProfile.HostKeyFingerprint,
            tools,
            leaseExpiry));
    }

    [HttpPost("jobs/{jobId:guid}/heartbeat")]
    public async Task<IActionResult> JobHeartbeat(Guid jobId, CancellationToken ct)
    {
        var job = await db.ScanJobs.FirstOrDefaultAsync(
            j => j.Id == jobId && j.ScanRunnerId == GetRunnerId(), ct);
        if (job is null) return NotFound();

        job.Dispatch(DateTimeOffset.UtcNow.AddMinutes(10));
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpPost("jobs/{jobId:guid}/result")]
    public async Task<IActionResult> PostResult(
        Guid jobId, [FromBody] PostResultRequest req, CancellationToken ct)
    {
        if (req.Stdout.Length > MaxStdoutBytes)
            return StatusCode(413, new { error = $"stdout exceeds {MaxStdoutBytes} bytes" });
        if (req.Stderr.Length > MaxStderrBytes)
            return StatusCode(413, new { error = $"stderr exceeds {MaxStderrBytes} bytes" });

        var job = await db.ScanJobs.FirstOrDefaultAsync(
            j => j.Id == jobId && j.ScanRunnerId == GetRunnerId(), ct);
        if (job is null) return NotFound();

        if (req.Status == "Succeeded")
        {
            await ingestionService.ProcessJobResultAsync(jobId, req.Stdout, req.Stderr, ct);
            // ProcessJobResultAsync already marks the job succeeded
        }
        else
        {
            job.CompleteFailed(req.Status, req.ErrorMessage ?? "Unknown error", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
        }

        await completionService.TryCompleteRunAsync(job.RunId, ct);
        return Ok(new { ok = true });
    }
}
```

- [ ] **Step 4: Run tests — expected to pass**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanRunnerControllerTests`
Expected: all 7 pass.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Api/Controllers/ScanRunnerController.cs \
  tests/PatchHound.Tests/Api/ScanRunnerControllerTests.cs
git commit -m "feat: add ScanRunnerController with runner-facing API endpoints"
```

---

## Task 5: ScanSchedulerWorker with tests (TDD)

**Files:**
- Create: `src/PatchHound.Worker/ScanSchedulerWorker.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/ScanSchedulerWorkerTests.cs`
- Modify: `src/PatchHound.Worker/Program.cs`

- [ ] **Step 1: Write failing tests**

The scheduler logic is testable by extracting the tick body into a separate method. Tests exercise the core logic, not the `BackgroundService` loop.

```csharp
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

        var device = Asset.Create(_tenantId, "ext-d1", AssetType.Device, "d1", Criticality.Medium);
        _db.Assets.Add(device);
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, device.Id, profile.Id, null));

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
        // Profile with hourly cron, last run 30 minutes ago
        var profile = ScanProfile.Create(_tenantId, "p2", "", "0 * * * *", _conn.Id, _runner.Id, true);
        _db.ScanProfiles.Add(profile);
        profile.RecordRunStarted(DateTimeOffset.UtcNow.AddMinutes(-30));
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

        var device = Asset.Create(_tenantId, "ext-d2", AssetType.Device, "d2", Criticality.Medium);
        _db.Assets.Add(device);
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

        var device = Asset.Create(_tenantId, "ext-d3", AssetType.Device, "d3", Criticality.Medium);
        _db.Assets.Add(device);
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

        var device = Asset.Create(_tenantId, "ext-d4", AssetType.Device, "d4", Criticality.Medium);
        _db.Assets.Add(device);
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
```

- [ ] **Step 2: Run tests — expected to fail**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanSchedulerWorkerTests`
Expected: FAIL.

- [ ] **Step 3: Implement ScanSchedulerTickHandler (testable logic)**

Create in `src/PatchHound.Infrastructure/AuthenticatedScans/ScanSchedulerTickHandler.cs`:

```csharp
using Cronos;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class ScanSchedulerTickHandler(
    PatchHoundDbContext db,
    ScanJobDispatcher dispatcher,
    ScanRunCompletionService completionService)
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan StalePendingThreshold = TimeSpan.FromHours(2);

    public async Task TickAsync(CancellationToken ct)
    {
        await EvaluateCronProfilesAsync(ct);
        await SweepExpiredLeasesAsync(ct);
        await SweepStalePendingJobsAsync(ct);
    }

    private async Task EvaluateCronProfilesAsync(CancellationToken ct)
    {
        var profiles = await db.ScanProfiles
            .Where(p => p.Enabled && p.CronSchedule != "")
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;

        foreach (var profile in profiles)
        {
            try
            {
                var cron = CronExpression.Parse(profile.CronSchedule);
                var baseline = profile.LastRunStartedAt ?? profile.CreatedAt;
                var nextDue = cron.GetNextOccurrence(baseline.UtcDateTime, TimeZoneInfo.Utc);

                if (nextDue.HasValue && nextDue.Value <= now.UtcDateTime)
                {
                    await dispatcher.StartRunAsync(profile.Id, "scheduled", null, ct);
                }
            }
            catch
            {
                // Invalid cron or dispatch failure — skip this profile
            }
        }
    }

    private async Task SweepExpiredLeasesAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredJobs = await db.ScanJobs
            .Where(j => j.Status == ScanJobStatuses.Dispatched
                && j.LeaseExpiresAt.HasValue
                && j.LeaseExpiresAt < now)
            .ToListAsync(ct);

        var affectedRunIds = new HashSet<Guid>();

        foreach (var job in expiredJobs)
        {
            if (job.AttemptCount >= MaxAttempts)
            {
                job.CompleteFailed(ScanJobStatuses.Failed,
                    "runner unreachable after 3 attempts", now);
                affectedRunIds.Add(job.RunId);
            }
            else
            {
                job.ReturnToPending("lease expired");
            }
        }

        if (expiredJobs.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        foreach (var runId in affectedRunIds)
        {
            await completionService.TryCompleteRunAsync(runId, ct);
        }
    }

    private async Task SweepStalePendingJobsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var threshold = now - StalePendingThreshold;

        var staleJobs = await (
            from job in db.ScanJobs
            join run in db.AuthenticatedScanRuns on job.RunId equals run.Id
            where job.Status == ScanJobStatuses.Pending
                && run.StartedAt < threshold
            select job
        ).ToListAsync(ct);

        var affectedRunIds = new HashSet<Guid>();

        foreach (var job in staleJobs)
        {
            job.CompleteFailed(ScanJobStatuses.Failed,
                "runner offline (never picked up)", now);
            affectedRunIds.Add(job.RunId);
        }

        if (staleJobs.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        foreach (var runId in affectedRunIds)
        {
            await completionService.TryCompleteRunAsync(runId, ct);
        }
    }
}
```

- [ ] **Step 4: Register tick handler in DI**

In `src/PatchHound.Infrastructure/DependencyInjection.cs`, after `ScanRunCompletionService`:

```csharp
services.AddScoped<PatchHound.Infrastructure.AuthenticatedScans.ScanSchedulerTickHandler>();
```

- [ ] **Step 5: Implement the BackgroundService wrapper**

Create `src/PatchHound.Worker/ScanSchedulerWorker.cs`:

```csharp
using PatchHound.Infrastructure.AuthenticatedScans;

namespace PatchHound.Worker;

public class ScanSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ScanSchedulerWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ScanSchedulerWorker started with {Interval}s interval", Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ScanSchedulerTickHandler>();
                await handler.TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during scan scheduler tick");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
```

- [ ] **Step 6: Register in Worker Program.cs**

In `src/PatchHound.Worker/Program.cs`, after the last `AddHostedService` line (line 25):

```csharp
builder.Services.AddHostedService<ScanSchedulerWorker>();
```

- [ ] **Step 7: Run tests — expected to pass**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanSchedulerWorkerTests`
Expected: all 5 pass.

- [ ] **Step 8: Full test suite**

Run: `dotnet test --nologo`
Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/PatchHound.Infrastructure/AuthenticatedScans/ScanSchedulerTickHandler.cs \
  src/PatchHound.Infrastructure/DependencyInjection.cs \
  src/PatchHound.Worker/ScanSchedulerWorker.cs \
  src/PatchHound.Worker/Program.cs \
  tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/ScanSchedulerWorkerTests.cs
git commit -m "feat: add ScanSchedulerWorker with cron evaluation and stale sweep"
```

---

## Task 6: Final build + test sweep

- [ ] **Step 1: Full solution build**

Run: `dotnet build --nologo`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 2: Full test run**

Run: `dotnet test --nologo`
Expected: all tests pass.

- [ ] **Step 3: Verify auth handler not applied to admin endpoints**

Confirm that `ScanRunnersController` (admin-facing, existing) uses `[Authorize(Policy = Policies.ManageAuthenticatedScans)]` and NOT the `ScanRunnerBearer` scheme. The new `ScanRunnerController` (runner-facing) uses `[Authorize(AuthenticationSchemes = ScanRunnerBearerHandler.SchemeName)]`. These are separate controllers at separate routes.

- [ ] **Step 4: Commit if any cleanups needed**

```bash
git commit --allow-empty -m "chore: Plan 2 scheduler + runner API complete"
```

---

## Self-Review

**Spec coverage:**
- §1 ScanRunnerBearer auth → Task 3
- §2 ScanRunnerController (heartbeat, jobs/next, jobs/{id}/heartbeat, jobs/{id}/result) → Task 4
- §3 ScanSchedulerWorker (cron eval, stale sweep) → Task 5
- §4 ScanRunCompletionService → Task 2
- §5 Cronos dependency → Task 1
- §6 Testing → Tasks 2, 4, 5
- Size caps (2MB stdout, 256KB stderr) → Task 4 (PostResult)
- Credential JIT fetch from OpenBao → Task 4 (GetNextJob)
- 503 on OpenBao failure → Task 4 (test + impl)
- Lease expiry sweep → Task 5
- Max 3 attempts → Task 5
- Stale pending 2h sweep → Task 5
- Run completion detection → Task 2, called from Tasks 4 and 5

**Type consistency check:** `ScanSchedulerTickHandler` used consistently. `ScanRunCompletionService.TryCompleteRunAsync` signature matches across all callers. `ScanRunnerController` record types used in both impl and tests.

**No placeholders found.**
