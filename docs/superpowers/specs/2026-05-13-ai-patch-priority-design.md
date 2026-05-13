# AI Patch Priority Assessment — Design Spec

**Date:** 2026-05-13  
**Issue:** #72  
**Status:** Approved

---

## Summary

Replace the existing remediation AI draft system (`RemediationAiJob` / `RemediationAiWorker`) with a unified patch-priority assessment pipeline. New critical vulnerabilities are assessed automatically when a tenant first encounters them. Security analysts can manually trigger an assessment for any severity. The structured result is displayed on the remediation case detail page, with an emergency-patch alert ribbon and notifications to managers when warranted.

---

## What Is Removed

- `RemediationAiJob` entity + EF configuration + DB migration (down)
- `RemediationAiJobService`
- `RemediationAiWorker` (background service in `PatchHound.Worker`)
- `RemediationAiJobStatus` enum
- `RemediationDecisionQueryService.GenerateAndStoreAiDraftsAsync` and any callers
- Frontend: generic AI draft text panel on remediation case detail

---

## Data Model

### `VulnerabilityAssessmentJob` (global, per CVE)

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `VulnerabilityId` | `Guid` | FK → `Vulnerability.Id`; unique index — one job record per CVE, upserted in place |
| `TriggerTenantId` | `Guid` | Tenant whose AI profile is used for this run |
| `Status` | `VulnerabilityAssessmentJobStatus` | Pending / Running / Succeeded / Failed |
| `RequestedAt` | `DateTimeOffset` | When the job was created or last reset |
| `StartedAt` | `DateTimeOffset?` | Set when worker claims the job |
| `CompletedAt` | `DateTimeOffset?` | Set on success or failure |
| `Error` | `string` | Empty on success; failure message otherwise |

No `TenantId` — the job is global. `TriggerTenantId` is used only to resolve the AI profile.

A DB unique index on `VulnerabilityId` enforces one job record per CVE. The upsert logic (see Triggers) always updates in place rather than inserting a new row.

### `VulnerabilityPatchAssessment` (global, per CVE)

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `VulnerabilityId` | `Guid` | FK → `Vulnerability.Id`; unique |
| `Recommendation` | `string` | Free-text recommendation |
| `Confidence` | `string` | e.g. "High", "Medium", "Low" |
| `Summary` | `string` | Short justification |
| `UrgencyTier` | `string` | One of: `emergency`, `as_soon_as_possible`, `normal_patch_window`, `low_priority` |
| `UrgencyTargetSla` | `string` | e.g. "24 hours", "7 days" |
| `UrgencyReason` | `string` | Why this tier was assigned |
| `SimilarVulnerabilities` | `string` | JSON array of objects |
| `CompensatingControlsUntilPatched` | `string` | JSON array of strings |
| `References` | `string` | JSON array of URL strings |
| `AssessedAt` | `DateTimeOffset` | When the assessment was stored |
| `AiProfileName` | `string` | Name of AI profile used, for audit |
| `RawOutput` | `string?` | Raw model response, nullable, for debugging |

Unique index on `VulnerabilityId`.

### `VulnerabilityAssessmentJobStatus` enum

```csharp
public enum VulnerabilityAssessmentJobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
}
```

---

## Triggers

### Auto — ingestion pipeline

Called from `IngestionService` after `RunExposureDerivationAsync` completes for a tenant.

Steps:
1. Query for all `Vulnerability` records with `VendorSeverity == Critical` that have at least one `Open` `DeviceVulnerabilityExposure` for this tenant.
2. Exclude any that already have a `VulnerabilityPatchAssessment`.
3. Exclude any that have a `VulnerabilityAssessmentJob` with status `Pending` or `Running`.
4. Check whether the tenant has an active AI profile (`TenantAiConfigurationResolver.ResolveDefaultAsync`). If not, skip entirely — do not block ingestion.
5. For each remaining vulnerability, upsert a job:
   - If a `Failed` job exists for the vulnerability, reset it to `Pending` and update `TriggerTenantId` + `RequestedAt`.
   - Otherwise, insert a new `Pending` job.

This is **idempotent**: re-running ingestion for the same tenant will not duplicate jobs.

### Late-discovery notification (assessment already exists)

After the idempotency check above, for any critical vulnerability that already has a `VulnerabilityPatchAssessment` with `UrgencyTier == "emergency"`, immediately send emergency notifications to this tenant's `SecurityManager` and `TechnicalManager` users (see Notifications). This covers tenants that discover a CVE after another tenant already paid for the AI run.

To avoid re-notifying the same tenant on every ingestion cycle, check whether a `NewCriticalVuln` notification for this `(userId, VulnerabilityId)` pair was already sent before dispatching.

### Manual — analyst trigger

**Endpoint:** `POST /api/vulnerabilities/{id}/assessment`  
**Authorization:** `SecurityAnalyst` role or higher  
**Response:** `202 Accepted`

Logic:
- If a `Running` job exists → return `409 Conflict` with message "Assessment already in progress."
- If a `Pending` or `Succeeded` job exists, or a `VulnerabilityPatchAssessment` exists → reset/replace: set job to `Pending`, clear the existing assessment if any, update `RequestedAt` and `TriggerTenantId` to the requesting user's tenant.
- If no job exists → insert a new `Pending` job.

---

## Worker

`VulnerabilityAssessmentWorker` in `PatchHound.Worker`. 10-second poll interval.

```
while not cancelled:
    claim oldest Pending job → set Running, save
    try:
        resolve AI profile for TriggerTenantId
        if no active profile → mark Failed("No active AI profile for tenant"), continue
        build prompt (inject vulnerability.ExternalId)
        call IAiReportProvider.GenerateTextAsync
        parse JSON response → VulnerabilityPatchAssessment fields
        if parse fails → mark Failed("Malformed AI response: <details>"), continue
        persist VulnerabilityPatchAssessment
        mark job Succeeded
        dispatch emergency notifications (see below)
    catch:
        mark Failed(ex.Message)
    delay 10s
```

**Prompt** (verbatim from issue, CVE ID injected):

```
As a security analyst responsible for prioritization of patching of vulnerabilities, {ExternalId} with regards to
how quickly it should be patched (emergency patch, as soon as possible, normal patch window or low priority).
Correlate with likelihood of exploitation based on previous similar vulnerabilities for the system/service related
to the vulnerability and history of exploits. Output as a structured JSON with clear recommendations,
justifications and references. The following properties must always be present: Recommendation, Confidence,
Summary, Urgency (with sub-fields tier, target SLA, reason), SimilarVulnerabilities,
CompensatingControlsUntilPatched, References
```

**JSON parsing:** The worker deserializes the model response into a typed DTO. Any missing required field causes the job to fail with a descriptive error so the issue can be debugged. Partial output is not persisted.

---

## Notifications

### Emergency patch — after assessment creation

After persisting a `VulnerabilityPatchAssessment` with `UrgencyTier == "emergency"`:

1. Find all tenants that have at least one `Open` `DeviceVulnerabilityExposure` for this vulnerability.
2. For each tenant, find all users with `RoleName.SecurityManager` or `RoleName.TechnicalManager` in `UserTenantRoles`.
3. Send `NotificationType.NewCriticalVuln` via `INotificationService.SendAsync`:
   - **Title:** `"Emergency patch required: {ExternalId}"`
   - **Body:** CVE ID, recommendation, confidence, urgency reason, target SLA, link to remediation case
   - **RelatedEntityType:** `"Vulnerability"`
   - **RelatedEntityId:** `VulnerabilityId`

### Late-discovery dedup

Before sending, check whether a `Notification` record with `Type == NewCriticalVuln` and `RelatedEntityId == VulnerabilityId` already exists for that `UserId`. Skip if found.

---

## API Changes

### New endpoint

`POST /api/vulnerabilities/{id}/assessment` — manual trigger (described above).

### Remediation case response DTO

Add an `Assessment` object to the remediation case detail response. The assessment is resolved by joining through the case's open exposures to their vulnerabilities, then to `VulnerabilityPatchAssessment`. If multiple vulnerabilities are in scope, include the highest-urgency assessment (emergency > asap > normal > low).

```json
{
  "assessment": {
    "recommendation": "...",
    "confidence": "High",
    "summary": "...",
    "urgency": {
      "tier": "emergency",
      "targetSla": "24 hours",
      "reason": "..."
    },
    "similarVulnerabilities": [...],
    "compensatingControlsUntilPatched": [...],
    "references": [...],
    "assessedAt": "2026-05-13T10:00:00Z",
    "aiProfileName": "...",
    "jobStatus": "Succeeded"
  }
}
```

`jobStatus` reflects the current `VulnerabilityAssessmentJob.Status` so the UI can show a loading state.

---

## Frontend

### Remediation case detail

- **Remove** the existing AI draft text panel.
- **Add** a structured "Patch Priority Assessment" card:
  - Recommendation + confidence badge
  - Urgency tier chip + target SLA + reason
  - Summary paragraph
  - Collapsible sections: Similar Vulnerabilities, Compensating Controls, References
  - Loading skeleton while `jobStatus` is `Pending` or `Running`
  - Error state with "Request new assessment" button if `jobStatus` is `Failed`
  - "Request assessment" button (analyst role) when no assessment exists yet

- **Emergency alert ribbon:** If `urgency.tier == "emergency"`, render a full-width red/orange ribbon at the top of the case detail:  
  `⚠ Emergency patch required — Target SLA: {targetSla}`

---

## Testing

| Scenario | Layer |
|---|---|
| Auto-trigger enqueues job for newly-observed critical CVE when active AI profile exists | Infrastructure (ingestion pipeline) |
| Auto-trigger skips if no active AI profile | Infrastructure |
| Auto-trigger skips if assessment already exists | Infrastructure |
| Auto-trigger skips if Pending/Running job exists | Infrastructure |
| Auto-trigger resets Failed job | Infrastructure |
| Worker persists assessment on valid AI response | Infrastructure / Worker |
| Worker marks job Failed on malformed AI JSON | Worker |
| Worker marks job Failed when AI profile unavailable at run time | Worker |
| Manual trigger returns 202 and creates job | Controller |
| Manual trigger returns 409 when job is Running | Controller |
| Emergency notifications sent to SecurityManager + TechnicalManager | Infrastructure |
| Late-discovery notification dedup prevents re-sending | Infrastructure |
| Remediation case DTO includes highest-urgency assessment | Controller / query |
| No remediation AI draft text in remediation case response | Contract test |

---

## Migration Notes

- EF migration to drop `RemediationAiJobs` table, add `VulnerabilityAssessmentJobs` and `VulnerabilityPatchAssessments` tables.
- Any `RemediationAiJob` rows in existing databases are abandoned — no data migration needed (draft text is not preserved by design).
