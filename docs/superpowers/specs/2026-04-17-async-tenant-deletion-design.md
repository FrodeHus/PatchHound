# Async Tenant Deletion

## Problem

Deleting a tenant with a large dataset times out because the deletion runs synchronously in the HTTP request — ~35 sequential batch deletes plus OpenBao secret cleanup, all inline in `TenantsController.Delete`.

## Goals

- Return immediately to the caller; run deletion in the background.
- Block all access to the tenant the moment deletion is requested.
- Push a completion (or failure) notification to the user who initiated deletion.
- Survive API restarts: a deletion in progress resumes automatically.

---

## Data Model

### `Tenant` entity

Add `IsPendingDeletion bool` (default `false`). Set to `true` atomically with job creation when deletion is requested. This is the gate checked on every authenticated request scoped to that tenant.

### `TenantDeletionJob` entity (new)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `TenantId` | `Guid` | FK → Tenants (Restrict) |
| `RequestedByUserId` | `Guid` | Target for completion notification |
| `Status` | `TenantDeletionJobStatus` enum | `Pending / Running / Completed / Failed` |
| `CreatedAt` | `DateTimeOffset` | |
| `StartedAt` | `DateTimeOffset?` | Set when worker claims the job |
| `CompletedAt` | `DateTimeOffset?` | Set on terminal state |
| `Error` | `string?` | Last failure message |

One EF migration covers both the new column and the new table.

---

## API Changes

### `DELETE /api/tenants/{id}`

Replaces the synchronous deletion sequence with:

1. Validate tenant exists and caller has `ConfigureTenant` access.
2. Set `tenant.IsPendingDeletion = true`.
3. Write a `TenantDeletionJob` row (`Status = Pending`, `RequestedByUserId` from `ITenantContext.CurrentUserId`).
4. Save both changes in a single `SaveChangesAsync`.
5. Return `202 Accepted`.

If a `TenantDeletionJob` already exists for the tenant (e.g. a previous failed run), reset it to `Pending` and clear `Error` / `StartedAt` / `CompletedAt` rather than creating a duplicate.

### `TenantContextMiddleware`

After resolving the tenant, if `IsPendingDeletion == true`, short-circuit with:

```
HTTP 410 Gone
{ "errorCode": "tenant_pending_deletion" }
```

This fires for every authenticated API request scoped to that tenant, blocking all data access while deletion is in progress.

---

## Background Worker

### `TenantDeletionService` (new, `PatchHound.Infrastructure`)

The existing ~35-step deletion sequence extracted verbatim from `TenantsController` into a dedicated service. Accepts `(Guid tenantId, CancellationToken ct)`. Used exclusively by the worker. Makes the logic independently testable.

### `TenantDeletionWorker : BackgroundService` (new, `PatchHound.Api`)

Runs inside the API process (co-located with `ISecretStore` and `PatchHoundDbContext`).

**Startup recovery:** Before entering the poll loop, reset all `Running` jobs to `Pending`. A `Running` job at startup means the previous API instance crashed mid-deletion; the deletion sequence is idempotent (re-deleting already-deleted rows is a no-op), so restarting from the top is safe.

**Poll loop** (5-second interval):

1. Claim one `Pending` job atomically:
   ```sql
   UPDATE TenantDeletionJobs
   SET Status = 'Running', StartedAt = NOW()
   WHERE Id = (
       SELECT Id FROM TenantDeletionJobs
       WHERE Status = 'Pending'
       ORDER BY CreatedAt
       LIMIT 1
       FOR UPDATE SKIP LOCKED
   )
   RETURNING *
   ```
   EF equivalent: load with a row-level lock or use `ExecuteUpdateAsync` with a `WHERE Status = 'Pending'` filter and check rows-affected == 1. Only proceed if exactly one row was claimed.

2. Invoke `TenantDeletionService.DeleteAsync(job.TenantId, ct)`.

3. **On success:**
   - Mark job `Status = Completed`, `CompletedAt = now`.
   - Push `TenantDeleted` SSE event to the requesting user via `IEventPusher.PushAsync("TenantDeleted", new { tenantId }, userId: job.RequestedByUserId.ToString())`.

4. **On failure:**
   - Mark job `Status = Failed`, `Error = exception.Message`, `CompletedAt = now`.
   - Push `TenantDeletionFailed` SSE event to the requesting user.
   - Log the full exception.

Only one job is processed at a time. The worker does not parallelise across multiple pending jobs.

---

## Frontend

### SSE event types

Add `TenantDeleted` and `TenantDeletionFailed` to the `SSEEvent` union in `useSSE.ts`.

### Global 410 interceptor

In the shared API client (wherever `apiGet` / `apiPost` etc. are defined), detect responses with status `410` and `errorCode === "tenant_pending_deletion"`. Set a global `tenantPendingDeletion` flag (React context or equivalent) that triggers the unavailable dialog.

### Tenant-unavailable dialog

A non-dismissable modal shown when `tenantPendingDeletion` is set. Contains:
- Message: "This tenant is being deleted and is no longer available."
- The tenant switcher as the only interactive element.

Cleared when the user selects a different tenant.

### SSE listeners (mounted at app root)

`TenantDeleted`:
- If the event's `tenantId` matches the currently selected tenant → set `tenantPendingDeletion` to trigger the unavailable dialog.
- Otherwise → show a success toast "Tenant deleted successfully."
- In both cases → invalidate the tenant list query.

`TenantDeletionFailed`:
- Show an error toast "Tenant deletion failed. Please contact an administrator."
- Invalidate the tenant list query.

---

## Error Handling

| Scenario | Behaviour |
|---|---|
| API restarts while job is `Running` | Worker resets to `Pending` on startup; deletion resumes from the top |
| `TenantDeletionService` throws | Job marked `Failed`; failure SSE sent to requester; can retry via `DELETE` again |
| OpenBao unavailable during secret cleanup | Exception propagates, job marked `Failed` |
| User accesses pending-deletion tenant | `410 Gone` with `errorCode: tenant_pending_deletion`; frontend shows unavailable dialog |

---

## Out of Scope

- Progress reporting (percentage complete).
- Cancelling an in-progress deletion.
- Admin visibility into all pending deletion jobs.
