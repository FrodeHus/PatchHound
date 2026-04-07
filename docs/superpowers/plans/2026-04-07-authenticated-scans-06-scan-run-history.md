# Authenticated Scans — Plan 6: Scan Run History & Report Dialog

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose scan run history to admins via a new backend controller and a new "Authenticated Scans" view on the Sources admin page, including a run report dialog showing per-host success/failure details.

**Architecture:** New `AuthenticatedScanRunsController` (C#) with list + detail endpoints → Zod schemas + server functions (TS) → `ScanRunHistoryTab` data table + `ScanRunReportDialog` modal, wired into the existing `/admin/sources` route as a third view toggle.

**Tech Stack:** ASP.NET Core (controller, EF Core, InMemory DB tests), TanStack Start (server functions, router), React Query, shadcn/ui (DataTable, Dialog, Badge, Tabs), Zod

---

## File Structure

### Backend (C#)

| Action | File | Responsibility |
|--------|------|---------------|
| Create | `src/PatchHound.Api/Controllers/AuthenticatedScanRunsController.cs` | Admin API: list runs (paged, filterable by profileId), get run detail with job summaries |
| Create | `tests/PatchHound.Tests/Api/AuthenticatedScanRunsControllerTests.cs` | Integration tests for both endpoints |

### Frontend (TypeScript/React)

| Action | File | Responsibility |
|--------|------|---------------|
| Modify | `frontend/src/api/authenticated-scans.schemas.ts` | Add `authenticatedScanRunSchema`, `scanJobSummarySchema`, `scanJobValidationIssueSchema`, paged wrappers |
| Modify | `frontend/src/api/authenticated-scans.functions.ts` | Add `fetchScanRuns`, `fetchScanRunDetail` server functions |
| Create | `frontend/src/components/features/admin/scan-runs/ScanRunHistoryTab.tsx` | Data table: profile name, trigger kind, started, completed, status, devices, entries, actions |
| Create | `frontend/src/components/features/admin/scan-runs/ScanRunReportDialog.tsx` | Two-table modal: successful hosts (expandable) + failed hosts with error details + validation issues |
| Modify | `frontend/src/routes/_authed/admin/sources.tsx` | Add "Authenticated Scans" view toggle, fetch run data when active |

---

### Task 1: Backend controller — list runs endpoint

**Files:**
- Create: `src/PatchHound.Api/Controllers/AuthenticatedScanRunsController.cs`

- [ ] **Step 1: Write the failing test for List endpoint**

Create test file `tests/PatchHound.Tests/Api/AuthenticatedScanRunsControllerTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AuthenticatedScanRunsControllerTests.List_returns_paged_runs_for_tenant" 2>&1`
Expected: FAIL — `AuthenticatedScanRunsController` does not exist

- [ ] **Step 3: Implement the controller with List endpoint**

Create `src/PatchHound.Api/Controllers/AuthenticatedScanRunsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/authenticated-scan-runs")]
[Authorize(Policy = Policies.ManageAuthenticatedScans)]
public class AuthenticatedScanRunsController(
    PatchHoundDbContext db,
    ITenantContext tenantContext) : ControllerBase
{
    public record ScanRunListDto(
        Guid Id, Guid ScanProfileId, string ProfileName,
        string TriggerKind, Guid? TriggeredByUserId,
        DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        string Status, int TotalDevices,
        int SucceededCount, int FailedCount, int EntriesIngested);

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ScanRunListDto>>> List(
        [FromQuery] Guid tenantId,
        [FromQuery] Guid? profileId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (!tenantContext.HasAccessToTenant(tenantId)) return Forbid();

        var query = db.AuthenticatedScanRuns.AsNoTracking()
            .Where(r => r.TenantId == tenantId);

        if (profileId.HasValue)
            query = query.Where(r => r.ScanProfileId == profileId.Value);

        var total = await query.CountAsync(ct);

        var runs = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var profileIds = runs.Select(r => r.ScanProfileId).Distinct().ToList();
        var profileNames = await db.ScanProfiles.AsNoTracking()
            .Where(p => profileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var items = runs.Select(r => new ScanRunListDto(
            r.Id, r.ScanProfileId,
            profileNames.GetValueOrDefault(r.ScanProfileId, "—"),
            r.TriggerKind, r.TriggeredByUserId,
            r.StartedAt, r.CompletedAt, r.Status,
            r.TotalDevices, r.SucceededCount, r.FailedCount,
            r.EntriesIngested)).ToList();

        return new PagedResponse<ScanRunListDto>(items, total, pagination.Page, pagination.BoundedPageSize);
    }
}
```

- [ ] **Step 4: Run all three List tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AuthenticatedScanRunsControllerTests.List" 2>&1`
Expected: 3 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Api/Controllers/AuthenticatedScanRunsController.cs tests/PatchHound.Tests/Api/AuthenticatedScanRunsControllerTests.cs
git commit -m "feat: add AuthenticatedScanRunsController with paged list endpoint"
```

---

### Task 2: Backend controller — run detail endpoint

**Files:**
- Modify: `src/PatchHound.Api/Controllers/AuthenticatedScanRunsController.cs`
- Modify: `tests/PatchHound.Tests/Api/AuthenticatedScanRunsControllerTests.cs`

- [ ] **Step 1: Write the failing test for GetDetail endpoint**

Add to `AuthenticatedScanRunsControllerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AuthenticatedScanRunsControllerTests.GetDetail_returns_run" 2>&1`
Expected: FAIL — `GetDetail` method does not exist

- [ ] **Step 3: Add DTOs and GetDetail endpoint to controller**

Add to `AuthenticatedScanRunsController.cs` inside the class, below the existing `ScanRunListDto`:

```csharp
public record ScanRunDetailDto(
    Guid Id, Guid ScanProfileId, string ProfileName,
    string TriggerKind, Guid? TriggeredByUserId,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    string Status, int TotalDevices,
    int SucceededCount, int FailedCount, int EntriesIngested,
    List<ScanJobSummaryDto> Jobs);

public record ScanJobSummaryDto(
    Guid Id, Guid AssetId, string AssetName,
    string Status, int AttemptCount,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    string ErrorMessage, int EntriesIngested,
    List<ValidationIssueDto> ValidationIssues);

public record ValidationIssueDto(string FieldPath, string Message, int EntryIndex);

[HttpGet("{id:guid}")]
public async Task<ActionResult<ScanRunDetailDto>> GetDetail(Guid id, CancellationToken ct)
{
    var run = await db.AuthenticatedScanRuns.AsNoTracking()
        .FirstOrDefaultAsync(r => r.Id == id, ct);
    if (run is null) return NotFound();
    if (!tenantContext.HasAccessToTenant(run.TenantId)) return Forbid();

    var profileName = await db.ScanProfiles.AsNoTracking()
        .Where(p => p.Id == run.ScanProfileId)
        .Select(p => p.Name)
        .FirstOrDefaultAsync(ct) ?? "—";

    var jobs = await db.ScanJobs.AsNoTracking()
        .Where(j => j.RunId == id)
        .OrderBy(j => j.StartedAt)
        .ToListAsync(ct);

    var assetIds = jobs.Select(j => j.AssetId).Distinct().ToList();
    var assetNames = await db.Assets.AsNoTracking()
        .Where(a => assetIds.Contains(a.Id))
        .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

    var jobIds = jobs.Select(j => j.Id).ToList();
    var issues = await db.ScanJobValidationIssues.AsNoTracking()
        .Where(v => jobIds.Contains(v.ScanJobId))
        .ToListAsync(ct);
    var issuesByJob = issues
        .GroupBy(v => v.ScanJobId)
        .ToDictionary(g => g.Key, g => g.Select(v =>
            new ValidationIssueDto(v.FieldPath, v.Message, v.EntryIndex)).ToList());

    var jobDtos = jobs.Select(j => new ScanJobSummaryDto(
        j.Id, j.AssetId,
        assetNames.GetValueOrDefault(j.AssetId, "—"),
        j.Status, j.AttemptCount,
        j.StartedAt, j.CompletedAt,
        j.ErrorMessage, j.EntriesIngested,
        issuesByJob.GetValueOrDefault(j.Id, []))).ToList();

    return new ScanRunDetailDto(
        run.Id, run.ScanProfileId, profileName,
        run.TriggerKind, run.TriggeredByUserId,
        run.StartedAt, run.CompletedAt, run.Status,
        run.TotalDevices, run.SucceededCount, run.FailedCount,
        run.EntriesIngested, jobDtos);
}
```

- [ ] **Step 4: Run all GetDetail tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AuthenticatedScanRunsControllerTests.GetDetail" 2>&1`
Expected: 4 tests PASS

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test --nologo 2>&1`
Expected: All tests PASS (no regressions)

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Api/Controllers/AuthenticatedScanRunsController.cs tests/PatchHound.Tests/Api/AuthenticatedScanRunsControllerTests.cs
git commit -m "feat: add scan run detail endpoint with job summaries and validation issues"
```

---

### Task 3: Frontend Zod schemas for scan runs

**Files:**
- Modify: `frontend/src/api/authenticated-scans.schemas.ts`

- [ ] **Step 1: Add scan run schemas**

Append to `frontend/src/api/authenticated-scans.schemas.ts` before the final line:

```typescript
// --- Scan Runs ---

export const scanRunSchema = z.object({
  id: z.string().uuid(),
  scanProfileId: z.string().uuid(),
  profileName: z.string(),
  triggerKind: z.string(),
  triggeredByUserId: z.string().uuid().nullable(),
  startedAt: isoDateTimeSchema,
  completedAt: nullableIsoDateTimeSchema,
  status: z.string(),
  totalDevices: z.number(),
  succeededCount: z.number(),
  failedCount: z.number(),
  entriesIngested: z.number(),
})

export const pagedScanRunsSchema = pagedResponseMetaSchema.extend({
  items: z.array(scanRunSchema),
})

export const validationIssueSchema = z.object({
  fieldPath: z.string(),
  message: z.string(),
  entryIndex: z.number(),
})

export const scanJobSummarySchema = z.object({
  id: z.string().uuid(),
  assetId: z.string().uuid(),
  assetName: z.string(),
  status: z.string(),
  attemptCount: z.number(),
  startedAt: nullableIsoDateTimeSchema,
  completedAt: nullableIsoDateTimeSchema,
  errorMessage: z.string(),
  entriesIngested: z.number(),
  validationIssues: z.array(validationIssueSchema),
})

export const scanRunDetailSchema = scanRunSchema.extend({
  jobs: z.array(scanJobSummarySchema),
})

export type ScanRun = z.infer<typeof scanRunSchema>
export type PagedScanRuns = z.infer<typeof pagedScanRunsSchema>
export type ScanJobSummary = z.infer<typeof scanJobSummarySchema>
export type ScanRunDetail = z.infer<typeof scanRunDetailSchema>
export type ValidationIssue = z.infer<typeof validationIssueSchema>
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1`
Expected: Clean (no errors)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/authenticated-scans.schemas.ts
git commit -m "feat: add Zod schemas for scan runs, job summaries, and validation issues"
```

---

### Task 4: Frontend server functions for scan runs

**Files:**
- Modify: `frontend/src/api/authenticated-scans.functions.ts`

- [ ] **Step 1: Add server functions**

Add these imports at the top of `authenticated-scans.functions.ts`, alongside the existing schema imports:

```typescript
import {
  // ... existing imports ...
  pagedScanRunsSchema,
  scanRunDetailSchema,
} from './authenticated-scans.schemas'
```

Then append after the `// ─── Scan Runners ───` section (at the end of the file):

```typescript
// ─── Scan Runs ───

export const fetchScanRuns = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      profileId: z.string().uuid().optional(),
      page: z.number().optional(),
      pageSize: z.number().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    return pagedScanRunsSchema.parse(await apiGet(`/authenticated-scan-runs?${params}`, context))
  })

export const fetchScanRunDetail = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    return scanRunDetailSchema.parse(await apiGet(`/authenticated-scan-runs/${id}`, context))
  })
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1`
Expected: Clean (no errors)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/authenticated-scans.functions.ts
git commit -m "feat: add server functions for fetching scan runs and run detail"
```

---

### Task 5: ScanRunReportDialog component

**Files:**
- Create: `frontend/src/components/features/admin/scan-runs/ScanRunReportDialog.tsx`

- [ ] **Step 1: Create the report dialog**

```tsx
import { useQuery } from '@tanstack/react-query'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { useState } from 'react'
import { fetchScanRunDetail } from '@/api/authenticated-scans.functions'
import type { ScanJobSummary, ScanRunDetail } from '@/api/authenticated-scans.schemas'
import { formatDateTime } from '@/lib/formatting'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

type Props = {
  runId: string | null
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function ScanRunReportDialog({ runId, open, onOpenChange }: Props) {
  const query = useQuery({
    queryKey: ['scan-run-detail', runId],
    queryFn: () => fetchScanRunDetail({ data: { id: runId! } }),
    enabled: open && Boolean(runId),
  })

  const detail = query.data

  const succeeded = detail?.jobs.filter((j) => j.status === 'Succeeded') ?? []
  const failed = detail?.jobs.filter((j) => j.status !== 'Succeeded') ?? []

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl max-h-[80vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>
            Scan Run Report
            {detail ? ` — ${detail.profileName}` : ''}
          </DialogTitle>
          <DialogDescription>
            {detail
              ? `${detail.triggerKind} run started ${formatDateTime(detail.startedAt)}`
              : 'Loading...'}
          </DialogDescription>
        </DialogHeader>

        {query.isLoading ? (
          <p className="py-4 text-sm text-muted-foreground">Loading run details...</p>
        ) : query.isError ? (
          <p className="py-4 text-sm text-destructive">Failed to load run details.</p>
        ) : detail ? (
          <div className="space-y-6">
            <RunSummaryBar detail={detail} />

            {succeeded.length > 0 && (
              <SucceededHostsSection jobs={succeeded} />
            )}

            {failed.length > 0 && (
              <FailedHostsSection jobs={failed} />
            )}

            {detail.jobs.length === 0 && (
              <p className="text-sm text-muted-foreground">No jobs were created for this run.</p>
            )}
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  )
}

function RunSummaryBar({ detail }: { detail: ScanRunDetail }) {
  return (
    <div className="flex flex-wrap gap-3">
      <Badge variant={statusVariant(detail.status)}>{detail.status}</Badge>
      <span className="text-sm text-muted-foreground">
        {detail.totalDevices} devices &middot; {detail.succeededCount} succeeded &middot;{' '}
        {detail.failedCount} failed &middot; {detail.entriesIngested} entries ingested
      </span>
      {detail.completedAt && (
        <span className="text-sm text-muted-foreground">
          Completed {formatDateTime(detail.completedAt)}
        </span>
      )}
    </div>
  )
}

function SucceededHostsSection({ jobs }: { jobs: ScanJobSummary[] }) {
  const [expanded, setExpanded] = useState(false)

  return (
    <div>
      <button
        type="button"
        className="flex items-center gap-1 text-sm font-medium"
        onClick={() => setExpanded(!expanded)}
      >
        {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        Successful hosts ({jobs.length})
      </button>
      {expanded && (
        <Table className="mt-2">
          <TableHeader>
            <TableRow>
              <TableHead>Host</TableHead>
              <TableHead>Entries</TableHead>
              <TableHead>Completed</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {jobs.map((job) => (
              <TableRow key={job.id}>
                <TableCell className="font-mono text-sm">{job.assetName}</TableCell>
                <TableCell>{job.entriesIngested}</TableCell>
                <TableCell>{job.completedAt ? formatDateTime(job.completedAt) : '—'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  )
}

function FailedHostsSection({ jobs }: { jobs: ScanJobSummary[] }) {
  return (
    <div>
      <p className="text-sm font-medium text-destructive">Failed hosts ({jobs.length})</p>
      <Table className="mt-2">
        <TableHeader>
          <TableRow>
            <TableHead>Host</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Error</TableHead>
            <TableHead>Attempts</TableHead>
            <TableHead>Duration</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {jobs.map((job) => (
            <>
              <TableRow key={job.id}>
                <TableCell className="font-mono text-sm">{job.assetName}</TableCell>
                <TableCell>
                  <Badge variant="destructive" className="text-[10px]">{job.status}</Badge>
                </TableCell>
                <TableCell className="max-w-[200px] truncate text-sm">{job.errorMessage || '—'}</TableCell>
                <TableCell>{job.attemptCount}</TableCell>
                <TableCell>
                  {job.startedAt && job.completedAt
                    ? `${Math.round((new Date(job.completedAt).getTime() - new Date(job.startedAt).getTime()) / 1000)}s`
                    : '—'}
                </TableCell>
              </TableRow>
              {job.validationIssues.length > 0 && (
                <TableRow key={`${job.id}-issues`}>
                  <TableCell colSpan={5} className="bg-muted/30 py-2">
                    <p className="mb-1 text-xs font-medium text-muted-foreground">
                      Validation issues ({job.validationIssues.length})
                    </p>
                    <div className="space-y-0.5">
                      {job.validationIssues.map((issue, i) => (
                        <p key={i} className="font-mono text-xs text-muted-foreground">
                          [{issue.entryIndex}] <span className="text-foreground">{issue.fieldPath}</span>: {issue.message}
                        </p>
                      ))}
                    </div>
                  </TableCell>
                </TableRow>
              )}
            </>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function statusVariant(status: string): 'default' | 'destructive' | 'secondary' | 'outline' {
  switch (status) {
    case 'Succeeded':
      return 'default'
    case 'Failed':
      return 'destructive'
    case 'PartiallyFailed':
      return 'outline'
    default:
      return 'secondary'
  }
}
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1`
Expected: Clean (no errors). If `Table`/`TableRow`/etc. components don't exist, check `frontend/src/components/ui/table.tsx` — they are standard shadcn/ui components and should already be present.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/admin/scan-runs/ScanRunReportDialog.tsx
git commit -m "feat: add ScanRunReportDialog with succeeded/failed host tables and validation issues"
```

---

### Task 6: ScanRunHistoryTab component

**Files:**
- Create: `frontend/src/components/features/admin/scan-runs/ScanRunHistoryTab.tsx`

- [ ] **Step 1: Create the data table component**

```tsx
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { Eye, Play } from 'lucide-react'
import { toast } from 'sonner'
import { useMutation } from '@tanstack/react-query'
import { fetchScanRuns, triggerScanRun } from '@/api/authenticated-scans.functions'
import type { PagedScanRuns, ScanRun } from '@/api/authenticated-scans.schemas'
import { formatDateTime } from '@/lib/formatting'
import { ScanRunReportDialog } from './ScanRunReportDialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { PaginationControls } from '@/components/ui/pagination-controls'

type Props = {
  initialData: PagedScanRuns
  page: number
  pageSize: number
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
}

export function ScanRunHistoryTab({ initialData, page, pageSize, onPageChange, onPageSizeChange }: Props) {
  const [reportRunId, setReportRunId] = useState<string | null>(null)

  const query = useQuery({
    queryKey: ['scan-runs', page, pageSize],
    queryFn: () => fetchScanRuns({ data: { page, pageSize } }),
    initialData,
  })

  const triggerMutation = useMutation({
    mutationFn: triggerScanRun,
    onSuccess: () => toast.success('Scan run triggered'),
    onError: () => toast.error('Failed to trigger scan run'),
  })

  const columns: ColumnDef<ScanRun>[] = [
    {
      accessorKey: 'profileName',
      header: 'Profile',
    },
    {
      accessorKey: 'triggerKind',
      header: 'Trigger',
      cell: ({ row }) => (
        <Badge variant="outline" className="text-[10px]">
          {row.original.triggerKind}
        </Badge>
      ),
    },
    {
      accessorKey: 'startedAt',
      header: 'Started',
      cell: ({ row }) => formatDateTime(row.original.startedAt),
    },
    {
      accessorKey: 'completedAt',
      header: 'Completed',
      cell: ({ row }) =>
        row.original.completedAt ? formatDateTime(row.original.completedAt) : (
          <span className="text-muted-foreground">Running...</span>
        ),
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ row }) => <StatusBadge status={row.original.status} />,
    },
    {
      id: 'devices',
      header: 'Devices',
      cell: ({ row }) => (
        <span className="tabular-nums">
          {row.original.succeededCount}/{row.original.totalDevices}
        </span>
      ),
    },
    {
      accessorKey: 'entriesIngested',
      header: 'Entries',
      cell: ({ row }) => (
        <span className="tabular-nums">{row.original.entriesIngested}</span>
      ),
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <div className="flex gap-1">
          <Button
            size="sm"
            variant="ghost"
            title="Re-trigger this profile"
            onClick={() =>
              triggerMutation.mutate({ data: { id: row.original.scanProfileId } })
            }
            disabled={triggerMutation.isPending}
          >
            <Play className="h-3 w-3" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            title="View report"
            onClick={() => setReportRunId(row.original.id)}
          >
            <Eye className="h-3 w-3" />
          </Button>
        </div>
      ),
    },
  ]

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle>Scan Run History</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable columns={columns} data={query.data?.items ?? []} />
          <PaginationControls
            page={page}
            pageSize={pageSize}
            totalCount={query.data?.totalCount ?? 0}
            totalPages={query.data?.totalPages ?? 0}
            onPageChange={onPageChange}
            onPageSizeChange={onPageSizeChange}
          />
        </CardContent>
      </Card>

      <ScanRunReportDialog
        runId={reportRunId}
        open={Boolean(reportRunId)}
        onOpenChange={(open) => { if (!open) setReportRunId(null) }}
      />
    </>
  )
}

function StatusBadge({ status }: { status: string }) {
  switch (status) {
    case 'Succeeded':
      return <Badge variant="default" className="bg-green-600">Succeeded</Badge>
    case 'Failed':
      return <Badge variant="destructive">Failed</Badge>
    case 'PartiallyFailed':
      return <Badge variant="outline" className="border-amber-500 text-amber-600">Partial</Badge>
    case 'Running':
      return <Badge variant="secondary">Running</Badge>
    case 'Queued':
      return <Badge variant="secondary">Queued</Badge>
    default:
      return <Badge variant="secondary">{status}</Badge>
  }
}
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1`
Expected: Clean (no errors)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/admin/scan-runs/ScanRunHistoryTab.tsx
git commit -m "feat: add ScanRunHistoryTab with data table and report dialog trigger"
```

---

### Task 7: Wire into Sources admin route

**Files:**
- Modify: `frontend/src/routes/_authed/admin/sources.tsx`

This task adds "Authenticated Scans" as a third view toggle on the Sources admin page. When active, it fetches paged scan runs and renders the `ScanRunHistoryTab`.

- [ ] **Step 1: Update the route search schema**

In `sources.tsx`, update the `validateSearch` to accept the new view and pagination params:

Change:
```typescript
validateSearch: z.object({
    activeView: z.enum(['tenant', 'global-enrichment']).optional(),
    mode: z.enum(['edit', 'history']).optional(),
    sourceKey: z.string().optional(),
  }),
```

To:
```typescript
validateSearch: z.object({
    activeView: z.enum(['tenant', 'global-enrichment', 'authenticated-scans']).optional(),
    mode: z.enum(['edit', 'history']).optional(),
    sourceKey: z.string().optional(),
    page: z.coerce.number().optional().default(1),
    pageSize: z.coerce.number().optional().default(25),
  }),
```

- [ ] **Step 2: Add the view toggle button and tab content**

Add imports at the top:
```typescript
import { ScanRunHistoryTab } from '@/components/features/admin/scan-runs/ScanRunHistoryTab'
import { fetchScanRuns } from '@/api/authenticated-scans.functions'
```

Add the query inside `SourcesAdministrationPage`, alongside the existing queries:
```typescript
const scanRunsQuery = useQuery({
    queryKey: ['scan-runs', search.page, search.pageSize],
    queryFn: () => fetchScanRuns({ data: { page: search.page, pageSize: search.pageSize } }),
    enabled: activeView === 'authenticated-scans',
  })
```

Update the `hasGlobalEnrichmentAccess` check — the "Authenticated Scans" view should be visible to GlobalAdmin and CustomerAdmin:
```typescript
const canManageScans = (user.activeRoles ?? []).some((r) =>
    r === 'GlobalAdmin' || r === 'CustomerAdmin'
  )
```

Add a third button inside the view toggle `<div>`:
```tsx
{canManageScans && (
  <button
    type="button"
    className={viewToggleClassName(activeView === "authenticated-scans")}
    onClick={() => {
      void navigate({
        to: '/admin/sources',
        search: {
          activeView: 'authenticated-scans',
          page: 1,
          pageSize: search.pageSize,
        },
      })
    }}
  >
    Authenticated Scans
  </button>
)}
```

**Important:** The toggle group should render when either `canManageEnrichment` or `canManageScans` is true. Update the conditional rendering from `{canManageEnrichment ? (` to `{(canManageEnrichment || canManageScans) ? (`. Also, wrap the existing "Tenant Sources" and "Global Enrichment" buttons individually — show "Global Enrichment" only when `canManageEnrichment`.

Add the content section before the closing `</section>`:
```tsx
{activeView === "authenticated-scans" && canManageScans && scanRunsQuery.data ? (
  <ScanRunHistoryTab
    initialData={scanRunsQuery.data}
    page={search.page}
    pageSize={search.pageSize}
    onPageChange={(p) => navigate({ search: { ...search, page: p } })}
    onPageSizeChange={(ps) => navigate({ search: { ...search, page: 1, pageSize: ps } })}
  />
) : activeView === "authenticated-scans" && canManageScans ? (
  <Card className="rounded-2xl">
    <CardContent className="py-8 text-sm text-muted-foreground">
      Loading scan run history...
    </CardContent>
  </Card>
) : null}
```

- [ ] **Step 3: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1`
Expected: Clean (no errors)

- [ ] **Step 4: Verify frontend builds**

Run: `cd frontend && npm run build 2>&1`
Expected: Build succeeds

- [ ] **Step 5: Commit**

```bash
git add frontend/src/routes/_authed/admin/sources.tsx
git commit -m "feat: add Authenticated Scans view toggle to Sources admin page"
```

---

### Task 8: Full build verification

**Files:** None (verification only)

- [ ] **Step 1: TypeScript check**

Run: `cd frontend && npx tsc --noEmit 2>&1`
Expected: Clean

- [ ] **Step 2: Frontend build**

Run: `cd frontend && npm run build 2>&1`
Expected: Build succeeds

- [ ] **Step 3: Backend test suite**

Run: `dotnet test --nologo 2>&1`
Expected: All tests PASS (including new `AuthenticatedScanRunsControllerTests`)

- [ ] **Step 4: Commit (if any uncommitted changes)**

Only commit if previous tasks left unstaged changes. Otherwise, skip.

---

## Self-Review Checklist

**Spec coverage:**
- Section 8.3 (Sources admin "Authenticated Scans" tab): Covered by Tasks 5-7. Data table with Profile, trigger kind, started, completed, status, devices, entries, actions (trigger + view report). Run report dialog with succeeded hosts (expandable) and failed hosts (error, attempts, duration, validation issues grouped per host).
- "Show expected output JSON" button: Deferred (nice-to-have, not in spec §8.3).

**Placeholder scan:** No TBD/TODO items. All code blocks are complete.

**Type consistency:** `ScanRunListDto` ↔ `scanRunSchema`, `ScanRunDetailDto` ↔ `scanRunDetailSchema`, `ScanJobSummaryDto` ↔ `scanJobSummarySchema`, `ValidationIssueDto` ↔ `validationIssueSchema` — all field names match between C# records and Zod schemas (camelCase on both sides since ASP.NET uses `System.Text.Json` which camelCases by default).
