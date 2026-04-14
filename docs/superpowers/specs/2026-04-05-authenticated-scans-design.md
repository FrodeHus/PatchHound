# Authenticated Scans — Design Spec

**Date:** 2026-04-05
**Status:** Approved (pending user review of this document)
**Scope:** v1 of authenticated scanning feature — SSH-based, on-prem runner, tenant-scoped, single supported output model (`DetectedSoftware`).

---

## 1. Purpose

PatchHound needs to connect directly to individual hosts to perform scans for data that Defender does not surface (e.g. locally-installed software on Linux). This spec defines the configuration surface, execution pipeline, data flow, and admin UX for authenticated scans.

---

## 2. High-level decisions (confirmed)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Hybrid topology** — central SaaS + on-prem `PatchHound.ScanRunner` binary | Customer hosts are not reachable from central; runner lives inside customer network. |
| 2 | **Pull-based protocol** — runner polls HTTPS for jobs | Firewall-friendly (outbound HTTPS only); matches existing worker patterns; scan jobs not latency-sensitive. |
| 3 | **Credentials in central OpenBao**, delivered JIT per job | Matches existing secret posture; simplest admin UX. |
| 4 | **Tenant-scoped everything**, tenant Admin role gates access | Consistent with existing admin entities (`AssetRule`, `TenantSourceConfiguration`). |
| 5 | **Reuse staging+merge pipeline** with per-source precedence (last-writer-wins per source) | Auth-scan data flows into canonical inventory, participates in vuln matching + risk scoring automatically. |
| 6 | **Cron schedule + manual trigger** (empty schedule = manual-only) | Consistent with existing `SyncSchedule`; supports on-demand runs from the Sources admin tab. |
| 7 | **Admin pre-creates runner**, UI shows bearer secret once | Named runners visible before they connect; matches mental model. |
| 8 | SFTP upload + file + cleanup; per-tool timeout (default 300s); bounded parallel execution (default 10); **JSON on stdout required** | Most flexible, safest, minimal forensic footprint. |
| 9 | Runner assignment **on scan profile** (single runner per profile) | Explicit, no mystery about which runner hits which host; no multi-runner load balancing in v1. |
| 10 | **SSH everywhere** (Windows via OpenSSH); **script versioning with 10-version rolling window** | Uniform protocol; auditability without unbounded storage. |

---

## 3. Architecture

```
┌─────────────── CENTRAL (SaaS) ────────────────┐        ┌──────── ON-PREM ─────────┐
│  PatchHound.Api                               │        │   PatchHound.ScanRunner  │
│   ├─ ScanProfilesController                   │        │   (new dotnet binary)    │
│   ├─ ScanningToolsController                  │        │                          │
│   ├─ ConnectionProfilesController             │        │   - Pulls jobs HTTPS     │
│   ├─ ScanRunnersController (enroll, heartbeat)│  HTTPS │   - SSH.NET client       │
│   └─ ScanRunnerJobsController (pull/result) ◄─┼────────┤   - Executes scripts     │
│                                               │        │   - Posts results back   │
│  PatchHound.Core — new entities               │        │   - Heartbeats           │
│                                               │        └──────────────────────────┘
│  PatchHound.Infrastructure                    │                    │ SSH
│   ├─ Secrets/OpenBao (existing)               │                    ▼
│   └─ AuthenticatedScans/                      │        ┌──────── TARGET HOSTS ────┐
│      ├─ ScanSchedulerWorker (cron tick)       │        │  /tmp/ph-<id>.py etc.    │
│      ├─ ScanJobDispatcher                     │        │  stdout → JSON          │
│      └─ AuthenticatedScanIngestionService     │        └──────────────────────────┘
│                                               │
│  PatchHound.Worker (existing process host)    │
│   └─ hosts ScanSchedulerWorker                │
└───────────────────────────────────────────────┘
```

**Boundaries:**

- **Central Api** owns configuration, job queue, results, ingestion, UI. Never talks SSH.
- **ScanRunner** is a small on-prem binary. Only HTTPS (to central) + SSH (to target hosts). No DB, no UI; state = a local config file (tenant id, runner id, bearer secret, central URL).
- **`ScanSchedulerWorker`** lives in the existing `PatchHound.Worker` process — reuses worker host, DI, EF context, OpenBao access.
- **Job queue** is a DB-backed `ScanJob` table keyed by runner id.

---

## 4. Data model

All entities are tenant-scoped (`TenantId` column + tenant filter). Follow existing encapsulation pattern (private setters, static `Create`, update methods, EF configuration under `Infrastructure/Data/Configurations`).

### 4.1 Configuration entities

**`ConnectionProfile`** — how to connect to a host.

| Column | Type | Notes |
|---|---|---|
| Id | Guid, PK | |
| TenantId | Guid | |
| Name | string | |
| Description | string | |
| Kind | string | `"ssh"` in v1 (generic for expansion) |
| SshHost | string | |
| SshPort | int | default 22 |
| SshUsername | string | |
| AuthMethod | string | `"password"` \| `"privateKey"` |
| SecretRef | string | OpenBao path; plaintext never stored in DB |
| HostKeyFingerprint | string? | optional, TOFU-pinned |
| CreatedAt / UpdatedAt | DateTimeOffset | |

**`ScanningTool`** — a configured script that produces structured output.

| Column | Type | Notes |
|---|---|---|
| Id | Guid, PK | |
| TenantId | Guid | |
| Name / Description | string | |
| ScriptType | string | `"python"` \| `"bash"` \| `"powershell"` |
| InterpreterPath | string | e.g. `/usr/bin/python3` |
| TimeoutSeconds | int | default 300 |
| OutputModel | string | `"DetectedSoftware"` in v1 |
| CurrentVersionId | Guid? | FK → `ScanningToolVersion` |
| CreatedAt / UpdatedAt | DateTimeOffset | |

**`ScanningToolVersion`** — immutable snapshot of script text. Retain newest 10 per tool.

| Column | Type | Notes |
|---|---|---|
| Id | Guid, PK | |
| ScanningToolId | Guid, FK | |
| VersionNumber | int | auto-increment per tool |
| ScriptContent | text | |
| EditedBy | Guid (UserId) | |
| EditedAt | DateTimeOffset | |

**`ScanProfile`** — a bundle of tools + schedule + connection + runner.

| Column | Type | Notes |
|---|---|---|
| Id | Guid, PK | |
| TenantId | Guid | |
| Name / Description | string | |
| CronSchedule | string | `""` means manual-only |
| ConnectionProfileId | Guid, FK | |
| ScanRunnerId | Guid, FK | |
| Enabled | bool | |
| ManualRequestedAt | DateTimeOffset? | |
| LastRunStartedAt | DateTimeOffset? | |
| CreatedAt / UpdatedAt | DateTimeOffset | |

**`ScanProfileTool`** (join) — tools assigned to a profile with execution order.

| ScanProfileId | ScanningToolId | ExecutionOrder | PK=(ScanProfileId, ScanningToolId) |
|---|---|---|---|

**`ScanRunner`** — on-prem runner identity.

| Column | Type | Notes |
|---|---|---|
| Id | Guid, PK | |
| TenantId | Guid | |
| Name / Description | string | |
| SecretHash | string | SHA-256 of bearer token |
| LastSeenAt | DateTimeOffset? | |
| Version | string | runner build info |
| Enabled | bool | |
| CreatedAt / UpdatedAt | DateTimeOffset | |

**`DeviceScanProfileAssignment`** — written by device-rule evaluation.

| TenantId | DeviceId | ScanProfileId | AssignedByRuleId | AssignedAt | PK=(DeviceId, ScanProfileId) |
|---|---|---|---|---|---|

### 4.2 Run / execution entities

**`AuthenticatedScanRun`** — one firing of a scan profile.

| Column | Type | Notes |
|---|---|---|
| Id | Guid, PK | |
| TenantId | Guid | |
| ScanProfileId | Guid | |
| TriggerKind | string | `"scheduled"` \| `"manual"` |
| TriggeredByUserId | Guid? | |
| StartedAt / CompletedAt | DateTimeOffset(?) | |
| Status | string | `Queued` \| `Running` \| `Succeeded` \| `PartiallyFailed` \| `Failed` |
| TotalDevices / SucceededCount / FailedCount | int | |
| EntriesIngested | int | |

**`ScanJob`** — unit of work per target device.

| Column | Type | Notes |
|---|---|---|
| Id | Guid, PK | |
| TenantId | Guid | |
| RunId | Guid, FK → AuthenticatedScanRun | |
| ScanRunnerId | Guid, FK | |
| DeviceId | Guid, FK | target device |
| ConnectionProfileId | Guid, FK | snapshot at dispatch |
| ScanningToolVersionIds | json | array of tool version FKs, snapshotted at dispatch |
| Status | string | `Pending` \| `Dispatched` \| `Running` \| `Succeeded` \| `Failed` \| `TimedOut` |
| LeaseExpiresAt | DateTimeOffset? | |
| AttemptCount | int | default 0 |
| StartedAt / CompletedAt | DateTimeOffset(?) | |
| ErrorMessage | string | |
| StdoutBytes / StderrBytes | int | size only on the job itself |
| EntriesIngested | int | |

**`ScanJobResult`** — raw + parsed output.

| Id | ScanJobId | RawStdout (truncated) | RawStderr (truncated) | ParsedJson | CapturedAt |
|---|---|---|---|---|---|

**`ScanJobValidationIssue`** — per-entry ingestion issues.

| ScanJobId | FieldPath | Message | EntryIndex |
|---|---|---|---|

### 4.3 Enum + existing-model changes

- **New enum value** `SoftwareIdentitySourceSystem.AuthenticatedScan`.
- **New `DeviceRuleOperation` type** `"AssignScanProfile"` with parameters `{"scanProfileId": "<guid>"}`.

---

## 5. Runner protocol & enrollment

### 5.1 Enrollment

1. Admin creates a runner record in the UI: `POST /api/scan-runners` with name/description.
2. Api generates a random 32-byte bearer secret, stores `SHA-256(secret)` in `ScanRunner.SecretHash`, returns the plaintext once.
3. UI shows the secret once ("won't see this again") plus central Api URL, tenant id, runner id, and a ready-to-paste `runner.yaml`.
4. Admin deploys the runner binary with that config. Runner calls `POST /api/scan-runner/heartbeat` with `Authorization: Bearer <secret>`.
5. Api verifies hash → updates `LastSeenAt`, `Version`.
6. Rotation via `POST /api/scan-runners/{id}/rotate-secret`; old secret invalidated immediately.

### 5.2 HTTP endpoints (central Api)

```
POST /api/scan-runner/heartbeat
  body: { version, hostname }
  → 200 { ok: true }

GET  /api/scan-runner/jobs/next
  → 204 (empty) OR
  → 200 {
      jobId, deviceId,
      hostTarget:   { host, port, username, authMethod },
      credentials:  { password? | privateKey? | privateKeyPassphrase? },
      hostKeyFingerprint?: "SHA256:...",
      tools: [ { id, name, scriptType, interpreterPath,
                 timeoutSeconds, scriptContent, outputModel } ],
      leaseExpiresAt
    }

POST /api/scan-runner/jobs/{jobId}/heartbeat
  → renews lease (expected every 30s)

POST /api/scan-runner/jobs/{jobId}/result
  body: {
    status: "Succeeded"|"Failed"|"TimedOut",
    toolResults: [ { toolId, stdout, stderr, exitCode, durationMs, parsedEntries? } ],
    errorMessage?
  }
  → 200 { ok: true }
```

### 5.3 Credential delivery

Credentials are fetched JIT from OpenBao at `jobs/next` time, embedded in the 200 response over TLS, held in runner memory only for the job's lifetime, never persisted runner-side.

### 5.4 Leases & safety

- Jobs have a 10-minute lease at dispatch; runner heartbeats every 30s to renew.
- Expired lease → job returns to `Pending`, `AttemptCount++`, eligible for re-dispatch.
- After `AttemptCount == 3`, job is marked `Failed` with `"runner unreachable"`.
- Runner-side concurrency: at most M concurrent jobs (default 10, `runner.yaml` configurable).

### 5.5 Runner binary internals

- New project `PatchHound.ScanRunner`, single-file publish.
- Uses `SSH.NET` for SSH + SFTP.
- Loads `runner.yaml`, backoff retries on central HTTP errors, logs to stdout (systemd-friendly).
- Execution sequence: SFTP script → `<interpreter> <scriptpath>` → capture stdout/stderr → delete script. Timeout enforced via cancellation token + SSH channel close.

---

## 6. Scan orchestration

### 6.1 `ScanSchedulerWorker` (in `PatchHound.Worker`, 60s tick)

1. Load all `ScanProfile` where `Enabled = true` and (`CronSchedule != ""` or `ManualRequestedAt != null`).
2. For cron profiles: compute next-due time from `CronSchedule` and `LastRunStartedAt`. Skip if not due.
3. For each due profile (or with `ManualRequestedAt`): call `ScanJobDispatcher.StartRun(profileId, triggerKind)`.
4. **Stale-job sweep:** any `Pending` job whose parent `AuthenticatedScanRun.StartedAt` is >2 hours old → mark `Failed` with `"runner offline (never picked up)"`.

### 6.2 `ScanJobDispatcher.StartRun(profileId, triggerKind)`

1. Reject if profile has a non-terminal active run (409 Conflict for manual trigger).
2. Create `AuthenticatedScanRun` (`Status = Queued`, `StartedAt = now`).
3. Query `DeviceScanProfileAssignment` for all devices assigned to this profile.
4. Snapshot each `ScanningTool`'s current `CurrentVersionId`.
5. For each device, insert `ScanJob` (`Status = Pending`, `ScanRunnerId = profile.ScanRunnerId`, `ScanningToolVersionIds = snapshot`).
6. Set run `TotalDevices = count`, `Status = Running`. If `count == 0`, immediately mark `Succeeded`.
7. Commit transaction.

### 6.3 Manual trigger

`POST /api/scan-profiles/{id}/trigger-run` writes `ManualRequestedAt`. Scheduler picks up on next tick, `TriggerKind = "manual"`, `TriggeredByUserId = current user`.

### 6.4 Run completion

On `jobs/{id}/result`:

1. Mark the job terminal (`Succeeded` / `Failed` / `TimedOut`).
2. If `Succeeded`, invoke `AuthenticatedScanIngestionService.Ingest(jobId)` (see §7).
3. If all jobs for the parent run are now terminal:
   - All succeeded → `Run.Status = Succeeded`
   - Mix → `Run.Status = PartiallyFailed`
   - All failed → `Run.Status = Failed`
   - Set `CompletedAt`, sum `EntriesIngested`, `SucceededCount`, `FailedCount`.

### 6.5 Concurrency guardrails

- One active `AuthenticatedScanRun` per profile at a time.
- Runner-side concurrency capped by runner config.
- Optional central-side cap on `Dispatched` jobs per runner.

---

## 7. Ingestion (validate → stage → merge)

### 7.1 Tool output contract — `DetectedSoftware` model

Script must print to stdout a single JSON object:

```json
{
  "software": [
    {
      "canonicalName":       "nginx",              // required, string, non-empty
      "canonicalProductKey": "nginx:nginx",        // required, string, non-empty
      "detectedVersion":     "1.24.0",             // optional, string
      "canonicalVendor":     "F5 Networks",        // optional, string
      "category":            "web-server",         // optional, string
      "primaryCpe23Uri":     "cpe:2.3:a:..."       // optional, CPE 2.3
    }
  ]
}
```

The **"Show expected output JSON" button** in the Scanning Tool editor renders this with a legend (green = required, grey = optional) and inline descriptions, syntax-highlighted by CodeMirror's JSON language.

The runner detects software and returns `DetectedSoftware` entries.
The central service persists those entries into canonical inventory entities:

- `SoftwareProduct`
- `InstalledSoftware`

### 7.2 `AuthenticatedScanIngestionService.Ingest(ScanJobId)`

1. Load job + `ScanJobResult.ParsedJson`.
2. **Validate**:
   - `software` must be an array (may be empty).
   - Each entry: `canonicalName` and `canonicalProductKey` required non-empty strings; optional fields are strings when present.
   - Soft limits: entry count ≤ 5000, string length ≤ 1024. Exceeded → entire job fails with explicit message.
3. Per-entry validation issues are recorded as `ScanJobValidationIssue` rows with `FieldPath`, `Message`, `EntryIndex`. Invalid entries **skipped**; valid entries still ingest.
4. ≥1 valid entry → stage + merge (§7.3); job → `Succeeded`, `EntriesIngested = validCount`. Issues still surface in the report.
5. 0 valid entries → job → `Failed`, `ErrorMessage = "all {N} entries failed validation"`.

### 7.3 Staging + merge

Write to **new dedicated table `StagedDetectedSoftware`** (isolated from Defender's snapshot-lifecycle staging). Merge logic shared with existing pipeline via a helper.

Key fields per staged entry:

- `TenantId`, `DeviceId` (the job target), `SourceKey = "AuthScan:<profileId>"`, `SourceSystem = SoftwareIdentitySourceSystem.AuthenticatedScan`, plus `canonical*` fields and `detectedVersion`.

Merge logic:

- **Upsert** `SoftwareProduct` by `CanonicalProductKey` (shared across sources).
- **Upsert** `InstalledSoftware` by `(DeviceId, SoftwareProductId, DetectedVersion, SourceSystem)`.
- **Deactivate** any `InstalledSoftware` rows for `(device, SourceSystem=AuthenticatedScan)` that were NOT in this run. Authoritative-for-source behavior, matches Defender's pattern.
- Optionally update `SoftwareAlias` if authenticated-scan-specific identifiers are introduced later.

**Precedence:** rows are source-owned at the installation level. Each source owns its own `InstalledSoftware` rows for a given device. Dashboards and vulnerability matching see the union of active installations across sources. Vulnerability matching joins through canonical `SoftwareProduct`.

---

## 8. Admin UI

### 8.1 Workbench page

Route: **`/admin/authenticated-scans`** — full-page workbench with tabs, deep-linkable via `?tab=`:

- `?tab=profiles` (default)
- `?tab=tools`
- `?tab=connections`
- `?tab=runners`

Gated on tenant Admin role. Matches existing `components.json` shadcn/ui patterns.

**Tab 1 — Scan Profiles (default):**
- Data table: Name, Schedule (cron → human), Runner, Connection, Tool count, Enabled, Last run status, Actions.
- Create/edit dialog: name, description, cron input with preview, runner dropdown, connection profile dropdown, multi-select tools with drag-to-reorder.
- Detail drawer: assigned devices (read-only, populated by device rules).

**Tab 2 — Scanning Tools:**
- Data table: Name, Script type, Output model, Current version, Last edited.
- Multi-step editor (matching Advanced Tool Workbench): name/description, script type, interpreter path, timeout, output model dropdown, **CodeMirror editor** with syntax highlighting per language, **"Show expected output JSON" button** → side panel with schema + required/optional legend.
- Version history panel: last 10 versions (EditedBy/EditedAt, view button).

**Tab 3 — Connection Profiles:**
- Data table: Name, Kind, Host, Username, Auth method.
- Create/edit dialog: name, kind=ssh, host, port, username, auth method radio (password/privateKey), secret fields (password OR private key textarea + optional passphrase), optional host key fingerprint.
- Secrets stored in OpenBao on save; `SecretRef` stored on entity. On edit: secrets shown as `••••••` with "Replace" button; never echoed back.

**Tab 4 — Scan Runners:**
- Data table: Name, Last seen, Version, Status (online if LastSeenAt < 2min).
- Create dialog: name, description → on save, modal shows bearer secret once + runner ID + central URL + ready-to-paste `runner.yaml`.
- Row action "Rotate secret" (shows new secret once).

### 8.2 Asset rule integration

In existing `/admin/asset-rules/` UI, add operation "Assign scan profile" with a scan-profile dropdown. Maps to `DeviceRuleOperation { Type="AssignScanProfile", Parameters={"scanProfileId":"..."} }`. The existing rule evaluation service writes `DeviceScanProfileAssignment` rows.

### 8.3 Sources admin — new "Authenticated Scans" tab

On `/admin/sources`, add a new tab "Authenticated Scans":

- Data table: Profile, Last start, Last stop, Status, Entries ingested, Host count, Actions (Trigger manual run, View report).
- **Run report dialog:** two data tables —
  - *Successful hosts* (count + expandable list).
  - *Failed hosts*: hostname, device id, error message, attempt count, duration. Groups validation issues per host (field path + message + entry index).

---

## 9. Security

- **Credentials** in OpenBao at `secrets/tenants/{tenantId}/auth-scan-connections/{profileId}`, never in DB. `ConnectionProfile.SecretRef` holds only the path. Delivered JIT over TLS, runner-memory-only.
- **Runner auth:** bearer secret, hashed SHA-256 in DB, shown once. Runner API scoped to its owning tenant; can only call `/api/scan-runner/*`.
- **Authorization:** all scan-config + scan-run endpoints require tenant Admin. Readers of the Sources admin "Authenticated Scans" tab also Admin.
- **Host-key pinning:** optional `HostKeyFingerprint` on ConnectionProfile. First successful scan records fingerprint (TOFU). Subsequent mismatches fail with clear error.
- **Script injection guardrails:** interpreter path + script content controlled by tenant admins only. Runner executes `<interpreter> <scriptpath>` (no shell interpolation of untrusted data). Script deleted post-execution.
- **Size caps:** stdout 2 MB, stderr 256 KB, entries ≤ 5000, string length ≤ 1024. Truncation flagged on result.
- **Audit:** all create/edit/delete + manual trigger logged via existing `AuditSaveChangesInterceptor` with `TriggeredByUserId`.
- **Secret rotation:** `POST /scan-runners/{id}/rotate-secret`, old secret immediately invalidated.

---

## 10. Testing strategy

Aligned with `docs/testing-conventions.md`.

**Unit tests:**
- `AuthenticatedScanIngestionService` — validation rules (required fields, type checks, size caps, per-entry issues), staging+merge, per-source precedence (Defender + AuthScan coexistence).
- `ScanJobDispatcher` — run creation, empty-device-set, no-duplicate-active-run guardrail, version snapshotting.
- `ScanSchedulerWorker` — cron due calculation, manual trigger pickup, stale-job sweeping.
- `ScriptVersionStore` — 10-version rolling window.
- `ScanRunnerAuth` — bearer hashing, rotation, tenant scoping.

**Integration tests (EF Core + real DB):**
- End-to-end: create profile → assign devices → trigger → mock runner pulls → posts result → verify ingestion, run completion, `InstalledSoftware` rows, coexistence with synthetic Defender rows.
- Partial-failure path → `Status=PartiallyFailed`, correct report data.
- Validation-failure path → issues recorded, partial ingest.
- Runner-offline path → sweeper marks jobs failed.

**Runner binary tests:** SSH execution via local `sshd` test container — script upload, timeout kill, cleanup, stdout capture.

**UI tests (Playwright/Vitest):** form validation, secret-once-visible modal, cron preview, schema-viewer button, manual trigger flow.

---

## 11. Out of scope for v1

- WinRM connection type (SSH only; Windows via OpenSSH).
- Runner tagging / device tagging / load-balancing across runners.
- Additional output models beyond `DetectedSoftware`.
- Script diff UI between versions (view-only list in v1).
- mTLS runner auth.
- Multi-tenant scan runners.
