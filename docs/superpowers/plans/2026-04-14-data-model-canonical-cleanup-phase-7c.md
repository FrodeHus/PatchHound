# Data Model Canonical Cleanup — Phase 7c Plan

> **Purpose:** finish the canonical cleanup that Phase 7b (PR #33) left open. Delete the legacy `Asset` entity + 5 satellites, rewrite every consumer against canonical `Device`, align the last two `"TenantVulnerability"` string-literal controllers with `WorkNotesController`'s routing, close out residual Phase-2 stubs across dashboard/asset-detail/remediation-decision/software-projection, regenerate the `Initial` migration one final time against a truly canonical model, and close issue #17.

**Context:** Phase 7b (PR #33) renamed `TenantSoftware` → `SoftwareTenantRecord`, `NormalizedSoftwareAlias` → `SoftwareProductAlias`, `NormalizedSoftwareInstallation` → `SoftwareProductInstallation`, and rewrote several stubs. It did **not** delete the `Asset` entity or satellites, so the regenerated `Initial` migration still creates `Assets`, `AssetBusinessLabels`, `AssetRules`, `AssetSecurityProfiles`, `AssetTags`, `StagedAssets`. Two controllers still carry `"TenantVulnerability"` string literals that are not aligned with `WorkNotesController`'s pattern. ~15 Phase-2 stubs remain across 4 files.

**Tech stack:** .NET 10 / EF Core 10 / xUnit / React + Vitest.

**Closes:** issue #17.

---

## Preflight

- [ ] **P1: Phase 7b merged to main.** `git log main` shows PR #33 commits.
- [ ] **P2: Branch cut.** `git checkout main && git pull && git checkout -b data-model-canonical-cleanup-phase-7c`.
- [ ] **P3: Baseline green build.** `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx && (cd frontend && npm run lint && npm run typecheck && npm test -- --run)`.
- [ ] **P4: Starting inventory.** Capture for the PR body:
  ```bash
  grep -rn '\bAsset\b\|AssetBusinessLabel\|AssetRule\|AssetSecurityProfile\|AssetTag\|StagedAsset\|"TenantVulnerability"\|Phase-2:\|Phase 4 debt' src/ tests/ frontend/src \
    | grep -v 'Migrations/' | grep -v 'docs/superpowers/archive'
  ```

---

## Task 1: Classify all `Asset` + satellite consumers

**Why:** `Device` is the canonical entity but the legacy `Asset` graph is still referenced. Need per-consumer classification before deleting.

**Entities in scope:**
- `Asset`, `AssetBusinessLabel`, `AssetRule`, `AssetSecurityProfile`, `AssetTag`, `StagedAsset`

**Steps:**
- [ ] For each entity, run `gitnexus_impact({target: "<Entity>", direction: "upstream"})` and `grep -rn "<Entity>" src/ tests/`.
- [ ] Classify each hit: (a) canonical `Device`/`DeviceGroup`/`DeviceTag`/`DeviceRule` replacement exists — delete the legacy call; (b) canonical replacement missing — add it first; (c) genuinely dead code — delete.
- [ ] Record the table in the PR body. Tasks 2–3 branch on the output.

No commit yet.

---

## Task 2: Rewrite `Asset`-entity consumers against `Device`

**Why:** must land before Task 6 deletion or the build will break.

**Steps:**
- [ ] For each consumer identified in Task 1, replace reads/writes of `Assets`, `AssetBusinessLabels`, `AssetRules`, `AssetSecurityProfiles`, `AssetTags`, `StagedAssets` with the canonical `Devices`/`DeviceBusinessLabels`/`DeviceRules`/`DeviceSecurityProfiles`/`DeviceTags`/`StagedDevices` equivalents.
- [ ] Keep DTO shapes stable where frontend consumes them; run `npm run typecheck` after each controller change.
- [ ] One commit per surface: `refactor(phase-7c): rewrite <surface> against canonical Device`.

---

## Task 3: Rewrite residual Phase-2 stubs

**Files:**
- `src/PatchHound.Api/Controllers/DashboardController.cs` — vulnsByDeviceGroup stub + heatmap stub
- `src/PatchHound.Api/Services/AssetDetailQueryService.cs` — 5 Phase-2 stubs
- `src/PatchHound.Api/Services/RemediationDecisionQueryService.cs` — 7 Phase-2 stubs
- `src/PatchHound.Infrastructure/Services/NormalizedSoftwareProjectionService.cs` — Phase-2 stub

**Canonical sources:** `DeviceVulnerabilityExposure` (filtered on `Status == ExposureStatus.Open` and tenant), `ExposureAssessment` (for env CVSS), `ExposureEpisode` (for windowing/recurrence), `RemediationCase` (for case linkage), `Vulnerability.VendorSeverity` (for severity grouping).

**Steps:**
- [ ] For each stub, identify the DTO field(s) it feeds and the caller's expected shape.
- [ ] Rewrite against the canonical sources above. Match existing Phase 5 patterns in `DashboardQueryService`.
- [ ] After each file, confirm zero `Phase-2:` / `Phase 4 debt` comments remain in that file.
- [ ] Rename `AssetDetailQueryService` → `DeviceDetailQueryService` via `gitnexus_rename` (dry-run first). Same for `RemediationDecisionQueryService` if it still carries legacy naming (verify).
- [ ] Rename `NormalizedSoftwareProjectionService` → `SoftwareProductProjectionService` (gitnexus_rename, dry-run first). Delete if vestigial.
- [ ] Commits: `refactor(phase-7c): rewrite <file> against canonical exposures`.

---

## Task 4: Align `CommentsController` + `NotificationsController` string-literal routing

**Files:**
- `src/PatchHound.Api/Controllers/CommentsController.cs` (L21 map + L82 switch)
- `src/PatchHound.Api/Controllers/NotificationsController.cs:46`

**Decision:** Path A (keep `"TenantVulnerability"` literal for historical rows, route to canonical `Vulnerabilities` on read) — same as `WorkNotesController`. Data-migration path (B) is rejected unless Task 1 classification surfaces a concrete reason.

**Steps:**
- [ ] Re-read `WorkNotesController`'s routing switch/dictionary — the template.
- [ ] Make `CommentsController` and `NotificationsController` match exactly (same discriminator handling, same canonical target).
- [ ] Add/extend controller tests proving historical `"TenantVulnerability"` rows return the canonical `Vulnerability` payload.
- [ ] Commit: `refactor(phase-7c): unify RelatedEntityType literal routing across comment/notification controllers`.

---

## Task 5: Frontend alignment

**Why:** Task 2–3 DTO changes may ripple to frontend schemas.

**Steps:**
- [ ] `(cd frontend && npm run typecheck)` after each backend surface change (already covered in Tasks 2–3, but re-verify end-to-end here).
- [ ] Update any `asset`-named schemas/routes/components that Task 2 classified as canonical `device`. Use the Phase 1–6 `/assets` → `/devices` precedent.
- [ ] `(cd frontend && npm run lint && npm run typecheck && npm test -- --run)` green.
- [ ] Commit only if frontend changes beyond typecheck fallout: `refactor(phase-7c): align frontend <surface> with canonical Device rename`.

---

## Task 6: Delete `Asset` + satellite entities, configurations, DbSets

**Entities to delete:**
- `src/PatchHound.Core/Entities/Asset.cs`
- `src/PatchHound.Core/Entities/AssetBusinessLabel.cs`
- `src/PatchHound.Core/Entities/AssetRule.cs`
- `src/PatchHound.Core/Entities/AssetSecurityProfile.cs`
- `src/PatchHound.Core/Entities/AssetTag.cs`
- `src/PatchHound.Core/Entities/StagedAsset.cs`

**Configurations to delete:**
- `src/PatchHound.Infrastructure/Data/Configurations/Asset*Configuration.cs` + `StagedAssetConfiguration.cs`

**DbContext:**
- Remove every `DbSet<Asset…>` / `DbSet<StagedAsset>` in `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` (confirmed present at line ~58).
- Remove matching `modelBuilder.Entity<>()` calls and `HasQueryFilter` registrations.

**Steps:**
- [ ] Delete files above only after Tasks 2–5 land and `dotnet build` is green.
- [ ] `dotnet build` must be 0 errors before proceeding.
- [ ] Commit: `refactor(phase-7c): delete legacy Asset entity and satellites`.

---

## Task 7: Delete `TenantVulnerabilityGraphFactory` if still present

**Why:** Phase 7b plan listed this as residual work — verify.

**Steps:**
- [ ] `grep -rn TenantVulnerabilityGraphFactory tests/`.
- [ ] If present and unused, delete. Rewrite any remaining consumer against `CanonicalSeed`.
- [ ] Commit (only if changes): `test(phase-7c): drop TenantVulnerabilityGraphFactory`.

---

## Task 8: Regenerate `Initial` migration (final)

**Why:** after Tasks 2–7 the DbContext model is fully canonical. This is the last regeneration.

**Steps:**
- [ ] Delete the current `src/PatchHound.Infrastructure/Migrations/<timestamp>_Initial.cs` + `.Designer.cs` + `PatchHoundDbContextModelSnapshot.cs`.
- [ ] `dotnet ef migrations add Initial --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api`.
- [ ] Inspect the generated migration. **Must NOT contain:**
  - `Assets`, `AssetBusinessLabels`, `AssetRules`, `AssetSecurityProfiles`, `AssetTags`, `StagedAssets`
  - `AssetRiskScores`, `TenantSoftwareRiskScores`
  - `NormalizedSoftware` entity table
  - `TenantVulnerabilities`, `VulnerabilityAssets`, `SoftwareVulnerabilityMatches`
- [ ] **Must contain:** `Devices`, `DeviceBusinessLabels`, `DeviceRules`, `DeviceSecurityProfiles`, `DeviceTags`, `StagedDevices`, `SoftwareProducts`, `SoftwareProductAliases`, `SoftwareProductInstallations`, `SoftwareTenantRecords`, `InstalledSoftware`, `Vulnerabilities`, `DeviceVulnerabilityExposures`, `ExposureAssessments`, `ExposureEpisodes`, `RemediationCases`, `DeviceRiskScores`, `SoftwareRiskScores`, `TeamRiskScores`, `DeviceGroupRiskScores`, `TenantRiskScoreSnapshots`.
- [ ] Column audit: `MaxAssetRiskScore` column on `TeamRiskScores`/`DeviceGroupRiskScores` is the canonical field name — keep, but flag in PR body so reviewer isn't confused.
- [ ] Wipe dev DB and verify clean boot:
  ```bash
  PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS patchhound;"
  PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "CREATE DATABASE patchhound;"
  dotnet ef database update --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api
  ```
- [ ] Commit: `feat(db): regenerate fully canonical Initial migration (final)`.

---

## Task 9: Update `docs/data-model-refactor.md`

**Steps:**
- [ ] Remove any remaining references to `Asset`, `AssetRiskScore`, `TenantSoftwareRiskScore`, `NormalizedSoftware`, `TenantVulnerability` outside the historical callout.
- [ ] Add a paragraph noting the `Initial` migration was regenerated one final time in Phase 7c as the true canonical baseline.
- [ ] Commit: `docs(phase-7c): final canonical reference`.

---

## Task 10: Final legacy-reference grep sweep

**Steps:**
- [ ] Run:
  ```bash
  grep -rn '\bAsset\b\|AssetRiskScore\b\|TenantSoftwareRiskScore\b\|NormalizedSoftware\b\|VulnerabilityAsset\b\|NormalizedSoftwareVulnerabilityProjection\|Phase-2:\|Phase 4 debt\|disabled during canonical migration' src/ tests/ frontend/src \
    | grep -v 'Migrations/' | grep -v 'docs/superpowers/archive'
  ```
- [ ] Expected: zero hits except:
  - `MaxAssetRiskScore` canonical column name
  - `"TenantVulnerability"` string literals (routed to canonical `Vulnerabilities` per Task 4) — document as acceptable
  - Archived docs
- [ ] Any unexpected hit: fix in-place or justify explicitly in PR body.

---

## Task 11: Full green build + tests

- [ ] `dotnet build PatchHound.slnx`
- [ ] `dotnet test PatchHound.slnx`
- [ ] `(cd frontend && npm run lint && npm run typecheck && npm test -- --run)`

---

## Task 12: Open PR with `Closes #17`

**Steps:**
- [ ] `git push -u origin data-model-canonical-cleanup-phase-7c`
- [ ] Title: `Phase 7c: final canonical cleanup (closes #17)`.
- [ ] Body:
  - Task 1 classification table
  - Per-commit diff summary
  - Legacy-reference grep before/after (from P4 and Task 10)
  - Regenerated `Initial` table audit (not-present + present lists)
  - `Closes #17`
- [ ] After merge, delete branch. Issue #17 auto-closes.

---

## Risk notes

- **Task 1 drives Tasks 2–6.** Misclassifying a satellite as deletable when a canonical replacement is missing will break runtime behavior — impact-check every consumer before the delete commit.
- **Task 6 ordering:** must land after Tasks 2–5. Reverse order breaks the build.
- **Task 8 assumes pre-release** (no production data). If that's no longer true, stop and plan a data-preserving migration.
- **Frontend `/assets` surface** was renamed to `/devices` in commit `617816a`. Verify no Phase 7c backend change re-introduces the `asset` naming on the wire.
- **CI frontend typecheck** is the canary for DTO drift in Tasks 2–3 — run after every controller rewrite, not only at the end.
