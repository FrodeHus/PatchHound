# Data Model Canonical Cleanup — Phase 7b Plan

> **Purpose:** finish the canonical cleanup that Phase 7 (PR #32) did not complete. Remove the last legacy entities (`TenantSoftware`, `NormalizedSoftwareAlias`, `NormalizedSoftwareInstallation`), rewrite the five remaining read-side stubs, drop the last legacy test factory, regenerate the `Initial` migration one final time against a fully canonical model, and close issue #17.

**Context:** Phase 7 (PR #32) landed the `SoftwareController` canonical rewrite plus 9 supporting changes and deleted `AssetRiskScore`, `TenantSoftwareRiskScore`, and `NormalizedSoftware` entities. The regenerated `Initial` migration still creates `NormalizedSoftwareAliases`, `NormalizedSoftwareInstallations`, `TenantSoftware`, and `Assets` tables because those entities were not deleted. Five read surfaces (`DashboardController`, `AdvancedToolExecutionService`, `CommentsController`, `NotificationsController`, `TenantVulnerabilityGraphFactory`) still contain legacy stubs or string-literal references.

**Tech stack:** .NET 10 / EF Core 10 / xUnit / React + Vitest.

**Closes:** issue #17.

---

## Preflight

- [ ] **P1: Phase 7 merged to main.** `git log main` shows PR #32 commits.
- [ ] **P2: Branch cut.** `git checkout main && git pull && git checkout -b data-model-canonical-cleanup-phase-7b`.
- [ ] **P3: Baseline green build.** `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx && (cd frontend && npm run lint && npm run typecheck && npm test -- --run)`.
- [ ] **P4: Legacy inventory.** Capture starting grep output, paste into PR body once opened:
  ```bash
  grep -rn 'AssetRiskScore\b\|TenantSoftwareRiskScore\b\|NormalizedSoftware\b\|NormalizedSoftwareAlias\b\|NormalizedSoftwareInstallation\b\|TenantVulnerability\b\|VulnerabilityAsset\b\|TenantSoftware\b\|\bAssets\b' src/ tests/ frontend/src \
    | grep -v '\bAssets/\b' | grep -v 'Migrations/'
  ```

---

## Task 1: Classify remaining `TenantSoftware` + `NormalizedSoftwareAlias` + `NormalizedSoftwareInstallation` references

**Why:** these three entities are still in `PatchHoundDbContext`. Need to decide per entity whether they're (a) actually dead and deletable, or (b) still load-bearing for some canonical read.

**Steps:**
- [ ] For each entity, run `gitnexus_impact({target: "<Entity>", direction: "upstream"})` or `grep -rn "<Entity>" src/ tests/` and list direct consumers.
- [ ] Classify each consumer: (a) canonical equivalent already exists, legacy call can be deleted; (b) canonical equivalent missing, must be added first; (c) alias/installation data is genuinely needed by `TenantSoftwareAliasResolver` / `NormalizedSoftwareResolver` — keep entity, rename to canonical (`SoftwareProductAlias`, `SoftwareProductInstallation`) so naming matches model.
- [ ] Record the classification in the PR body. Rest of the plan branches on this:
  - If all three are (a) deletable: Tasks 2–3 delete them.
  - If any is (c) rename-only: Task 2 renames instead of deletes; schema stays but naming becomes canonical.

Do not commit yet — output drives Task 2.

---

## Task 2: Delete or rename `TenantSoftware`, `NormalizedSoftwareAlias`, `NormalizedSoftwareInstallation`

**Per Task 1 classification:**

**If delete path:**
- [ ] Delete `src/PatchHound.Core/Entities/TenantSoftware.cs`, `NormalizedSoftwareAlias.cs`, `NormalizedSoftwareInstallation.cs` (only the ones classified deletable).
- [ ] Delete matching `*Configuration.cs` in `Infrastructure/Data/Configurations/`.
- [ ] Remove `DbSet<>` + any `modelBuilder.Entity<>()` calls in `PatchHoundDbContext.cs`.
- [ ] Delete `TenantSoftwareAliasResolver.cs` and/or `NormalizedSoftwareResolver.cs` if they become vestigial (check consumers first).
- [ ] Commit: `refactor(phase-7b): delete legacy TenantSoftware + NormalizedSoftware alias/installation entities`.

**If rename path (per entity):**
- [ ] Use `gitnexus_rename` dry-run first.
- [ ] Rename entity + configuration + DbSet property.
- [ ] Update all consumers and the `Initial` migration (Task 6 handles regeneration).
- [ ] Commit: `refactor(phase-7b): rename <OldName> to <NewName> for canonical naming`.

---

## Task 3: Rewrite `DashboardController` vulnerability-count + trend stubs

**File:** `src/PatchHound.Api/Controllers/DashboardController.cs` (lines ~60, ~810)

**Why:** two stubs still return empty vulnerability counts and empty trend data — Phase-2-era debt never closed.

**Steps:**
- [ ] Identify the DTO fields each stub feeds. Confirm canonical sources: `DeviceVulnerabilityExposure` for counts, `ExposureEpisode` for trend windows, `Vulnerability.VendorSeverity` for severity grouping.
- [ ] Rewrite vulnerability-count stub (L60) to count `DeviceVulnerabilityExposures` grouped by `Vulnerability.VendorSeverity`, filtered by `ExposureStatus.Open` and tenant.
- [ ] Rewrite trend stub (L810) to read from `ExposureEpisodes` windowed by `FirstSeenAt`/`ClosedAt`, producing per-day open counts over the request window.
- [ ] Add controller test if coverage is absent. Reuse `CanonicalSeed` from Phase 5.
- [ ] Commit: `refactor(phase-7b): rewrite DashboardController vulnerability counts and trends against canonical exposure rows`.

---

## Task 4: Rewrite `AdvancedToolExecutionService` context stub

**File:** `src/PatchHound.Infrastructure/Services/AdvancedToolExecutionService.cs:244`

**Why:** *"Phase-2: VulnerabilityAsset + SoftwareVulnerabilityMatch deleted. Return empty context."* — still returns empty.

**Steps:**
- [ ] Identify what context the caller expects (tool-execution context around a vulnerability).
- [ ] Source from `DeviceVulnerabilityExposure` joined to `ExposureAssessment` (for environmental CVSS) and `Device`/`SoftwareProduct` (for subject).
- [ ] If a tool's prompt/context shape changed, update the tool registration accordingly.
- [ ] Commit: `refactor(phase-7b): rewire AdvancedToolExecutionService context against canonical exposures`.

---

## Task 5: `CommentsController` + `NotificationsController` legacy string-literal routing

**Files:**
- `src/PatchHound.Api/Controllers/CommentsController.cs:21, 82`
- `src/PatchHound.Api/Controllers/NotificationsController.cs:46`

**Why:** both use `"TenantVulnerability"` as a `RelatedEntityType` discriminator. `WorkNotesController` already does this and it was deemed acceptable (routes to canonical `Vulnerabilities` on read). These two should match that pattern or be renamed consistently.

**Decision matrix (pick one and document in commit):**
- **A. Keep literal, route to canonical:** same as WorkNotesController — literal stays for historical rows, read-side joins to canonical `Vulnerability`. Lowest risk, no data migration.
- **B. Data migration:** rename literal to `"Vulnerability"` in existing rows via a `UpdateRelatedEntityTypeLiterals` migration + update controllers. Cleaner but requires schema work.

**Steps:**
- [ ] Pick A unless there's a concrete reason to migrate data.
- [ ] Make CommentsController + NotificationsController match WorkNotesController's pattern exactly (same switch/dictionary).
- [ ] Commit: `refactor(phase-7b): unify RelatedEntityType literal routing across controllers`.

---

## Task 6: Delete `TenantVulnerabilityGraphFactory`

**File:** `tests/PatchHound.Tests/TestData/TenantVulnerabilityGraphFactory.cs`

**Steps:**
- [ ] Confirm no tests still call it (`grep -rn TenantVulnerabilityGraphFactory tests/`).
- [ ] Delete the file.
- [ ] If consumers remain, rewrite them against `CanonicalSeed` first.
- [ ] Commit: `test(phase-7b): drop TenantVulnerabilityGraphFactory`.

---

## Task 7: Regenerate `Initial` migration against fully canonical model

**Why:** after Tasks 2–6 the DbContext model contains only canonical entities. The `Initial` migration must be regenerated so the baseline is truly canonical.

**Steps:**
- [ ] Delete `src/PatchHound.Infrastructure/Migrations/20260414165217_Initial.cs` + `.Designer.cs` + `PatchHoundDbContextModelSnapshot.cs`.
- [ ] `dotnet ef migrations add Initial --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api`.
- [ ] Inspect generated migration. **Must not contain:**
  - `AssetRiskScores`, `TenantSoftwareRiskScores`
  - `NormalizedSoftware`, `NormalizedSoftwareAliases`, `NormalizedSoftwareInstallations` (unless renamed per Task 2 — then their new names are expected)
  - `TenantSoftware` (unless kept per Task 1)
  - `TenantVulnerabilities`, `VulnerabilityAssets`, `SoftwareVulnerabilityMatches`
  - `Assets` legacy table (superseded by `Devices` since Phase 1)
- [ ] **Must contain:** `Devices`, `SoftwareProducts`, `InstalledSoftware`, `Vulnerabilities`, `DeviceVulnerabilityExposures`, `ExposureAssessments`, `ExposureEpisodes`, `RemediationCases`, `DeviceRiskScores`, `SoftwareRiskScores`, `TeamRiskScores`, `DeviceGroupRiskScores`, `TenantRiskScoreSnapshots`.
- [ ] Wipe dev DB and verify boot:
  ```bash
  PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS patchhound;"
  PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "CREATE DATABASE patchhound;"
  dotnet ef database update --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api
  ```
- [ ] Commit: `feat(db): regenerate fully canonical Initial migration`.

---

## Task 8: Update `docs/data-model-refactor.md`

**Steps:**
- [ ] Remove any stale references to entities deleted in this phase.
- [ ] Add a paragraph noting the `Initial` migration was regenerated in Phase 7b as the final canonical baseline.
- [ ] Commit: `docs(phase-7b): final canonical reference update`.

---

## Task 9: Final legacy-reference grep sweep

**Steps:**
- [ ] Run:
  ```bash
  grep -rn 'AssetRiskScore\b\|TenantSoftwareRiskScore\b\|NormalizedSoftware\b\|TenantVulnerability\b\|VulnerabilityAsset\b\|NormalizedSoftwareVulnerabilityProjection\|Phase-2:\|Phase 4 debt\|disabled during canonical migration' src/ tests/ frontend/src
  ```
- [ ] Expected: zero hits except:
  - Inside `docs/superpowers/archive/`
  - `"TenantVulnerability"` string literals routing to canonical `Vulnerabilities` (if path A chosen in Task 5) — document as acceptable
- [ ] If any unexpected hit remains, fix or explicitly justify in PR body.

---

## Task 10: Full green build + tests

- [ ] `dotnet build PatchHound.slnx`
- [ ] `dotnet test PatchHound.slnx`
- [ ] `(cd frontend && npm run lint && npm run typecheck && npm test -- --run)`

---

## Task 11: Open PR with `Closes #17`

**Steps:**
- [ ] `git push -u origin data-model-canonical-cleanup-phase-7b`
- [ ] Open PR titled `Phase 7b: final canonical cleanup (closes #17)`.
- [ ] Body includes:
  - Per-commit diff summary
  - Task 1 classification table (what was deleted vs renamed vs kept)
  - Legacy-reference grep before/after
  - New `Initial` migration table list for reviewer audit
  - `Closes #17`
- [ ] After merge, delete the branch. Issue #17 should auto-close.

---

## Risk notes

- **Task 1 drives everything.** If `TenantSoftware` or `NormalizedSoftwareAlias` turn out to be load-bearing for supply-chain enrichment or alias resolution, deleting them without a canonical replacement will break those flows. Read the resolvers (`TenantSoftwareAliasResolver`, `NormalizedSoftwareResolver`) carefully before committing to the delete path.
- **Task 7 migration regeneration** assumes pre-release (no production data). If that's no longer true, fail the task and plan a data-preserving schema change instead.
- **Task 2/3/4 ordering matters:** don't delete entities until all read-surface consumers (Tasks 3–5) no longer need them. Easiest order: 3 → 4 → 5 → 6 → 2 → 7.
- **CI gating:** frontend typecheck is the canary for DTO shape changes in Task 3 — run it after every controller rewrite, not only at the end.
