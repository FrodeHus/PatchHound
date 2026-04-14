# Phase 5 Gap-Closing Plan (PR #30 follow-up commits)

> Add commits to branch `data-model-canonical-cleanup-phase-5` to close gaps surfaced by post-PR review against the Phase 5 plan. Each task = one commit, message ending with `Refs #17` (non-closing).

**Scope:** what plan §5 expected but PR #30 did not deliver.

**Out of scope:** anything Phase 6 (baseline migration, docs).

---

## Preflight

- [ ] Confirm green starting point: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx && (cd frontend && npm run lint && npm run typecheck)`.

---

## Task G1: EF migration for `SoftwareRiskScore`

**Why:** entity + DbSet + tenant query filter were added in Phase 5 commit `3fa47e6` but no migration was generated. Schema drift on deploy.

**Steps:**
- [ ] Run `dotnet ef migrations add Phase5SoftwareRiskScore --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api`.
- [ ] Inspect generated `Up`/`Down`: must create `SoftwareRiskScores` table with PK `Id`, columns from entity, and unique index `(TenantId, SoftwareProductId)`. Confirm FK to `SoftwareProducts` is `OnDelete(Restrict)`.
- [ ] If migration also picks up unrelated drift, stop and surface.
- [ ] `dotnet build && dotnet test`.
- [ ] Commit: `feat(phase-5): add Phase5SoftwareRiskScore migration`.

---

## Task G2: Plan P5 stub-inventory note

**Why:** plan preflight P5 required a committed inventory of the stubs Phase 5 was filling in. Reviewer-facing artifact.

**Steps:**
- [ ] Create `docs/superpowers/plans/2026-04-10-phase-5-stub-inventory.md` listing the stub call sites Phase 5 (and gap-closing commits) replace, grouped by file.
- [ ] Plans are gitignored — instead, post the same content as a comment on PR #30 and skip the file. (Confirm with reviewer if the file is wanted; default = PR comment only.)

---

## Task G3: `DashboardQueryService` canonical rewrite (plan Task 4)

**Why:** plan Task 4 calls for `DashboardQueryService.GetRecurrenceDataAsync` and `BuildRiskChangeBriefAsync` to read from canonical `DeviceVulnerabilityExposure` / `ExposureEpisode` rows. Current file already reads from `ExposureEpisodes` and `DeviceVulnerabilityExposures` — verify against plan and remove any remaining tenant-software/asset-risk references.

**Steps:**
- [ ] Re-read `src/PatchHound.Api/Services/DashboardQueryService.cs` end-to-end.
- [ ] Diff each query against plan Task 4 expected shape.
- [ ] Rewrite gaps: case-id wiring on risk-change-brief items (`RemediationCaseId` per affected `SoftwareProductId`), recurrence per `DeviceVulnerabilityExposureId` (currently uses it but verify projection wires through to UI).
- [ ] Update DTOs in `Models/Dashboard/` if signature changes (carry `RemediationCaseId?` on risk-change items so UI can deep-link to `/remediation/cases/{caseId}`).
- [ ] If DTO changes, update frontend `dashboard.schemas.ts` + any consumer.
- [ ] Commit: `refactor(phase-5): rewrite DashboardQueryService against canonical rows`.

---

## Task G4: `DashboardQueryService` canonical tests (plan Task 3)

**Why:** plan Task 3 requires red-green test coverage on the new query service.

**Steps:**
- [ ] Add `tests/PatchHound.Tests/Api/Services/DashboardQueryServiceCanonicalTests.cs`.
- [ ] Cover: recurrence detection from `ExposureEpisodes.EpisodeNumber > 1`, risk-change-brief appeared/resolved windowing, case-id wiring on items.
- [ ] Use existing `CanonicalSeed` helper; extend it if needed.
- [ ] Commit: `test(phase-5): add DashboardQueryServiceCanonicalTests`.

---

## Task G5: Restore `VulnerabilitiesController.UpdateOrganizationalSeverity`

**Why:** still returns 409 with "disabled during canonical migration" text. Plan Phase-3-handoff item.

**Steps:**
- [ ] Confirm `OrganizationalSeverity` entity still exists and carries canonical `VulnerabilityId`.
- [ ] Replace the 409 stub at `src/PatchHound.Api/Controllers/VulnerabilitiesController.cs:152-158` with: load-or-create `OrganizationalSeverity` per `(TenantId, VulnerabilityId)`, set requested severity + audit fields, save, return 204.
- [ ] If a service exists for this, route through it instead of inline DbContext writes.
- [ ] Add controller test: PUT updates severity on existing record; PUT creates record when none exists; non-admin returns 403.
- [ ] Commit: `feat(phase-5): restore vulnerability organizational-severity endpoint`.

---

## Task G6: Restore `SoftwareDescriptionGenerationService`

**Why:** still has 1 stub from Phase 4 debt. Plan Phase-3-handoff item.

**Steps:**
- [ ] Open `src/PatchHound.Infrastructure/Services/SoftwareDescriptionGenerationService.cs`, locate the stub.
- [ ] Re-source the data from canonical exposure rows aggregated per `SoftwareProduct` (count of open exposures, distinct affected devices, top severity).
- [ ] If the original projection logic is gone, derive the same fields from `DeviceVulnerabilityExposure` joined to `ExposureAssessment.EnvironmentalCvss`.
- [ ] Commit: `feat(phase-5): rewire SoftwareDescriptionGenerationService against canonical exposures`.

---

## Task G7: Restore `ApprovalTaskQueryService` (7 stubs)

**Why:** seven inline `Phase 4 debt (#17)` markers in this file gate the approval-tasks list/detail surfaces. Plan Phase-3-handoff item.

**Steps:**
- [ ] Read the file; list each stub with the field/projection it replaces.
- [ ] For each stub, project from `Vulnerability` + `DeviceVulnerabilityExposure` (+ `ExposureAssessment` for env CVSS / `RemediationCase` for case linkage). Match the original DTO shape so frontend doesn't break.
- [ ] Confirm no remaining `Phase 4 debt` comments in file.
- [ ] Build + run any approval-task tests; add coverage if absent for the rewired projections.
- [ ] Commit: `refactor(phase-5): rewire ApprovalTaskQueryService against canonical exposures`.

---

## Task G8: Tenant-isolation read-side coverage (plan Task 9)

**Why:** plan Task 9 extends `TenantIsolationEndToEndTests` to assert canonical read paths cannot leak across tenants.

**Steps:**
- [ ] Locate existing `TenantIsolationEndToEndTests`.
- [ ] Add cases per plan: tenant-A risk score query returns no tenant-B device scores; dashboard summary scoped; email send path filters by tenant.
- [ ] Commit: `test(phase-5): tenant-isolation coverage for canonical read paths`.

---

## Task G9: Final grep sweep (plan Task 10)

**Steps:**
- [ ] `grep -rn "Phase 4 debt\|disabled during canonical migration\|TenantSoftwareRiskScores\|AssetRiskScores" src/ tests/` — expect zero hits.
- [ ] If any remain, file as Phase 6 follow-up or fix in-place.
- [ ] No commit unless additional fixes needed.

---

## Task G10: Final verification + PR update

**Steps:**
- [ ] `dotnet build PatchHound.slnx`
- [ ] `dotnet test PatchHound.slnx`
- [ ] `(cd frontend && npm run lint && npm run typecheck && npm test -- --run)`
- [ ] `git push`
- [ ] Update PR #30 description with checklist of gap commits added.

---

## Risk notes

- G3 has highest contract risk — DTO shape changes ripple to UI. Land G3 + G4 together.
- G5 endpoint will fail any frontend that has already adapted to the 409 (returning success now changes UX). Verify the UI re-enables the action correctly.
- G7's seven stubs are read-only; low risk but each projection needs spot-check against the DTO consumer.
