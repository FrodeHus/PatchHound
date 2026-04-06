# Authenticated Scans Plan 2: Scheduler, Runner API & Run Completion

## Goal

Add server-side orchestration for authenticated scans: a scheduler worker that evaluates cron schedules and sweeps stale jobs, runner-facing API endpoints for job pulling and result posting, and run completion detection.

## Scope

- `ScanSchedulerWorker` hosted service in `PatchHound.Worker`
- `ScanRunnerController` in `PatchHound.Api` with custom `ScanRunnerBearer` auth scheme
- `ScanRunCompletionService` for finalizing runs when all jobs are terminal
- Tests for all components

**Out of scope:** PatchHound.ScanRunner binary (Plan 3), UI (Plan 4).

---

## 1. ScanRunnerBearer Authentication

Custom `AuthenticationHandler<AuthenticationSchemeOptions>` registered as scheme `"ScanRunnerBearer"`.

**Flow:**
1. Extract bearer token from `Authorization` header
2. Compute `SHA-256(token)`, look up `ScanRunner` by `SecretHash`
3. Fail if runner not found, disabled, or tenant mismatch
4. Set claims `runner_id` and `tenant_id` on the principal

Registered in DI alongside existing auth. Only applies to `/api/scan-runner/*` endpoints. Admin-facing `ScanRunnersController` keeps its existing auth.

---

## 2. ScanRunnerController

Route: `/api/scan-runner`
Auth: `[Authorize(AuthenticationSchemes = "ScanRunnerBearer")]`

### POST /heartbeat

- Body: `{ version, hostname }`
- Updates `ScanRunner.LastSeenAt` and `Version`
- Returns `200 { ok: true }`

### GET /jobs/next

- Finds oldest `Pending` job where `ScanRunnerId` matches this runner
- If none: returns `204`
- Otherwise:
  - Dispatches the job: `Status = Dispatched`, `LeaseExpiresAt = now + 10min`, `AttemptCount++`
  - Fetches credentials from OpenBao via `ISecretStore.GetSecretAsync` at the connection profile's `SecretRef`. Returns `503` if OpenBao unavailable (job stays `Pending`).
  - Loads tool versions from `ScanningToolVersionIdsJson`
  - Returns `200` with payload:

```json
{
  "jobId": "guid",
  "assetId": "guid",
  "hostTarget": {
    "host": "string",
    "port": 22,
    "username": "string",
    "authMethod": "password|privateKey"
  },
  "credentials": {
    "password": "string?",
    "privateKey": "string?",
    "passphrase": "string?"
  },
  "hostKeyFingerprint": "string?",
  "tools": [
    {
      "id": "guid",
      "name": "string",
      "scriptType": "python|bash|powershell",
      "interpreterPath": "string",
      "timeoutSeconds": 300,
      "scriptContent": "string",
      "outputModel": "NormalizedSoftware"
    }
  ],
  "leaseExpiresAt": "iso8601"
}
```

### POST /jobs/{jobId}/heartbeat

- Verifies job belongs to this runner
- Extends `LeaseExpiresAt` by 10 minutes
- Returns `200 { ok: true }`

### POST /jobs/{jobId}/result

- Body: `{ status: "Succeeded"|"Failed"|"TimedOut", stdout, stderr, errorMessage? }`
- Size caps: stdout 2MB, stderr 256KB. Returns `413` if exceeded.
- If `Succeeded`: invokes `AuthenticatedScanIngestionService.ProcessJobResultAsync`
- Marks job terminal via `CompleteSucceeded` or `CompleteFailed`
- Triggers `ScanRunCompletionService.TryCompleteRunAsync(job.RunId)`
- Returns `200 { ok: true }`

---

## 3. ScanSchedulerWorker

Hosted service in `PatchHound.Worker`. Uses `PeriodicTimer` with 60-second interval. Uses `IServiceScopeFactory` for per-tick scoped DbContext. Each tick performs three operations, each wrapped in try/catch:

### Cron evaluation

1. Load all `ScanProfile` where `Enabled = true` and `CronSchedule != ""`
2. Parse cron with Cronos: `CronExpression.Parse(schedule)`
3. Compute next occurrence after `LastRunStartedAt` (or `CreatedAt` if never run)
4. If `nextOccurrence <= now`: call `ScanJobDispatcher.StartRunAsync(profileId, "scheduled", null)`

### Manual trigger pickup

Manual triggers are handled synchronously by `ScanProfilesController.Trigger` endpoint (already calls `ScanJobDispatcher.StartRunAsync` directly). The scheduler does not process manual triggers — they are dispatched immediately on the API call.

### Stale sweep

Two passes:

1. **Expired leases:** `Dispatched` jobs where `LeaseExpiresAt < now`
   - If `AttemptCount < 3`: return to `Pending` via `job.ReturnToPending("lease expired")`
   - If `AttemptCount >= 3`: mark `Failed` via `job.CompleteFailed("Failed", "runner unreachable after 3 attempts", now)`

2. **Abandoned pending jobs:** `Pending` jobs whose parent `AuthenticatedScanRun.StartedAt` is >2 hours ago
   - Mark `Failed` via `job.CompleteFailed("Failed", "runner offline (never picked up)", now)`

3. After marking jobs failed, call `ScanRunCompletionService.TryCompleteRunAsync` for each affected run.

---

## 4. ScanRunCompletionService

Single method: `TryCompleteRunAsync(Guid runId, CancellationToken ct)`

1. Load the run
2. Count jobs by status for this run
3. If any jobs are non-terminal (`Pending`, `Dispatched`, `Running`): no-op
4. If all terminal: compute `succeededCount`, `failedCount`, sum `EntriesIngested` from succeeded jobs
5. Call `run.Complete(succeeded, failed, entriesIngested, now)`
6. Save

Called from two places: `ScanRunnerController` (after result post) and `ScanSchedulerWorker` (after stale sweep).

---

## 5. New Dependencies

- **Cronos** NuGet package — added to `PatchHound.Worker` (or `PatchHound.Infrastructure` if scheduler logic lives there)

---

## 6. Testing

### ScanRunnerBearerHandler tests
- Valid token authenticates and sets claims
- Invalid/missing token returns 401
- Disabled runner returns 401

### ScanRunnerController tests
- Heartbeat updates runner LastSeenAt and Version
- Jobs/next dispatches job and returns full payload
- Jobs/next returns 204 when no pending jobs
- Jobs/next returns 503 when secret store fails (job stays Pending)
- Jobs/{id}/heartbeat extends lease
- Jobs/{id}/result with Succeeded triggers ingestion + completion
- Jobs/{id}/result with Failed marks job failed + triggers completion
- Jobs/{id}/result rejects oversized payloads with 413

### ScanSchedulerWorker tests
- Due cron profile gets dispatched
- Not-yet-due profile skipped
- Expired lease with attempts < 3 returns job to Pending
- Expired lease with attempts >= 3 marks job Failed
- Stale pending jobs marked Failed after 2 hours
- Stale sweep triggers run completion

### ScanRunCompletionService tests
- All jobs succeeded → run Succeeded
- Mixed results → run PartiallyFailed
- All jobs failed → run Failed
- Some non-terminal → no-op
- EntriesIngested summed from succeeded jobs

---

## 7. File Map

### New files
- `src/PatchHound.Api/Auth/ScanRunnerBearerHandler.cs`
- `src/PatchHound.Api/Controllers/ScanRunnerController.cs`
- `src/PatchHound.Infrastructure/AuthenticatedScans/ScanRunCompletionService.cs`
- `src/PatchHound.Worker/ScanSchedulerWorker.cs`
- `tests/PatchHound.Tests/Api/ScanRunnerControllerTests.cs`
- `tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/ScanRunCompletionServiceTests.cs`
- `tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/ScanSchedulerWorkerTests.cs`

### Modified files
- `src/PatchHound.Api/Program.cs` — register ScanRunnerBearer auth scheme
- `src/PatchHound.Infrastructure/DependencyInjection.cs` — register ScanRunCompletionService
- `src/PatchHound.Worker/Program.cs` — register ScanSchedulerWorker hosted service
- `src/PatchHound.Worker/PatchHound.Worker.csproj` — add Cronos package reference
