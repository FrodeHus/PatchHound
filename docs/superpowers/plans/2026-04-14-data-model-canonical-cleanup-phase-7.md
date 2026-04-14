# Data Model Canonical Cleanup — Phase 7 Plan

> **Purpose:** finish the canonical cleanup that Phase 6 baseline did not complete. Remove every remaining legacy entity (`AssetRiskScore`, `TenantSoftwareRiskScore`, `NormalizedSoftware*`, `TenantVulnerability*` residue), rewrite the read surfaces that still reference them, regenerate the `Initial` migration so the canonical baseline actually describes the canonical model, and close issue #17.

**Context:** Phase 6 (PR #31) landed `docs/data-model-refactor.md` + a regenerated `Initial.cs` migration + deleted 96 historical migration files. It did not delete the leftover legacy entities or rewrite the read surfaces that still bind to them, so the "canonical baseline" migration creates `AssetRiskScores`, `TenantSoftwareRiskScores`, `NormalizedSoftware`, `NormalizedSoftwareAliases`, `NormalizedSoftwareInstallations`. Phase 7 finishes the job.

**Tech stack:** .NET 10 / EF Core 10 / xUnit / React + Vitest.

**Closes:** issue #17.

---

## Preflight

- [ ] **P1: Phase 6 merged to main.** Confirm `git log main` shows Phase 6 commits.
- [ ] **P2: Branch cut.** `git checkout main && git pull && git checkout -b data-model-canonical-cleanup-phase-7`.
- [ ] **P3: Baseline green build.** `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx && (cd frontend && npm run lint && npm run typecheck && npm test -- --run)`.
- [ ] **P4: Legacy inventory.** Run and commit the output as context (not a tracked file, just the report in the PR):
  ```bash
  grep -rln 'AssetRiskScore\b\|TenantSoftwareRiskScore\b\|NormalizedSoftware\b\|TenantVulnerability\b\|VulnerabilityAsset\b\|NormalizedSoftwareVulnerabilityProjection' src/ tests/ frontend/src
  ```
  Expected starting set: ~22 backend files + the entity/config files themselves.

---

## Task 1: Catalog the legacy surfaces consuming each entity

**Why:** each entity has its own callers; we need a precise list before deleting anything.

**Steps:**
- [ ] Open PR #31 review findings as reference.
- [ ] For each legacy entity, `grep -rn "<EntityName>" src/ tests/ frontend/src` and record the list in the PR description.
- [ ] Classify each hit as: (a) entity/config (will be deleted), (b) API surface needing canonical rewrite, (c) worker/service needing rewrite, (d) test needing rewrite or delete, (e) dead comment/log string (just delete).
- [ ] No commit yet — output drives Tasks 2–6.

---

## Task 2: Rewrite `SoftwareController` against canonical `SoftwareProduct`

**Why:** this controller is the single largest consumer of `NormalizedSoftware*`. 60+ lines of `item.NormalizedSoftware.*` field access at `src/PatchHound.Api/Controllers/SoftwareController.cs`.

**Steps:**
- [ ] Identify the DTO fields sourced from `NormalizedSoftware` (category, description, EOL fields, supply-chain fields). Confirm equivalents on canonical `SoftwareProduct` (or `SoftwareProductMetadata` if split).
- [ ] If `SoftwareProduct` lacks an equivalent field, decide per-field: (a) add to `SoftwareProduct` entity, (b) drop the field from the DTO (if deprecated), (c) move to a separate metadata entity. Default: add to `SoftwareProduct` to minimise DTO churn.
- [ ] Rewrite list + detail queries to read from `SoftwareProducts` and joined canonical exposure/device rows. Use patterns from Phase 5 `DashboardQueryService` rewrite.
- [ ] Update frontend schemas if DTO shape changes; run `npm run typecheck`.
- [ ] Add controller tests mirroring the Phase 5 `DashboardQueryServiceCanonicalTests` pattern.
- [ ] Commit: `refactor(phase-7): rewrite SoftwareController against canonical SoftwareProduct`.

---

## Task 3: Rewrite remaining read-side consumers of legacy entities

**Files expected (confirm in Task 1):**
- `src/PatchHound.Api/Controllers/DashboardController.cs`
- `src/PatchHound.Api/Controllers/CommentsController.cs`
- `src/PatchHound.Api/Controllers/NotificationsController.cs`
- `src/PatchHound.Api/Controllers/TenantsController.cs`
- `src/PatchHound.Api/Controllers/WorkNotesController.cs`
- `src/PatchHound.Api/Services/AssetDetailQueryService.cs`
- `src/PatchHound.Api/Services/RemediationDecisionQueryService.cs`
- `src/PatchHound.Infrastructure/Services/AdvancedToolExecutionService.cs`
- `src/PatchHound.Infrastructure/Data/AuditSaveChangesInterceptor.cs` (check if it's just a string literal)

**Steps:**
- [ ] For each file, replace legacy-entity reads with canonical equivalents (`Device`, `SoftwareProduct`, `InstalledSoftware`, `Vulnerability`, `DeviceVulnerabilityExposure`, `ExposureAssessment`, `ExposureEpisode`, `RemediationCase`).
- [ ] For notification/comment `RelatedEntityType == "TenantVulnerability"` string literals, decide: rename to `"Vulnerability"` with a data migration to update existing rows, or keep the literal for historical rows and route to canonical `Vulnerability` on read.
- [ ] Batch by surface: one commit per controller/service so the PR stays reviewable.
- [ ] Each commit: `refactor(phase-7): rewrite <surface> against canonical rows`.

---

## Task 4: Rewrite or delete legacy tests

**Files expected:**
- `tests/PatchHound.Tests/Infrastructure/RiskScoreServiceTests.cs`
- `tests/PatchHound.Tests/Api/TeamsControllerTests.cs`
- `tests/PatchHound.Tests/TestData/TenantVulnerabilityGraphFactory.cs`
- `tests/PatchHound.Tests/TestData/TenantSoftwareGraphFactory.cs`

**Steps:**
- [ ] For each test: if a canonical equivalent test already exists (e.g. `RiskScoreServiceCanonicalTests`), delete the legacy file.
- [ ] If no canonical equivalent exists, rewrite against canonical entities.
- [ ] Graph factories: replace with a canonical `CanonicalSeed` helper (already exists from Phase 5); extend it if coverage gaps appear.
- [ ] Commit: `test(phase-7): drop legacy test factories and tests`.

---

## Task 5: Delete legacy entities, configurations, and DbSets

**Entities to delete:**
- `src/PatchHound.Core/Entities/AssetRiskScore.cs`
- `src/PatchHound.Core/Entities/TenantSoftwareRiskScore.cs`
- Any remaining `NormalizedSoftware*` entities (verify list in Task 1).

**Configurations to delete:**
- `src/PatchHound.Infrastructure/Data/Configurations/AssetRiskScoreConfiguration.cs`
- `src/PatchHound.Infrastructure/Data/Configurations/TenantSoftwareRiskScoreConfiguration.cs`
- Any `NormalizedSoftware*Configuration.cs`.

**DbContext:**
- Remove corresponding `DbSet<>` properties and any `modelBuilder.Entity<>()` calls for these entities in `PatchHoundDbContext.cs`.

**Steps:**
- [ ] Delete files listed above (must come after Tasks 2–4 so nothing still binds to them).
- [ ] `dotnet build` must succeed with 0 errors before proceeding.
- [ ] Commit: `refactor(phase-7): delete legacy inventory and risk entities`.

---

## Task 6: Regenerate `Initial` migration

**Why:** after Task 5 the model no longer contains the legacy tables. The `Initial` migration needs to be regenerated so the canonical baseline actually is canonical.

**Steps:**
- [ ] Delete `src/PatchHound.Infrastructure/Data/Migrations/20260414125602_Initial.cs` + `.Designer.cs`.
- [ ] Delete `src/PatchHound.Infrastructure/Data/Migrations/PatchHoundDbContextModelSnapshot.cs`.
- [ ] `dotnet ef migrations add Initial --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api`.
- [ ] Inspect generated migration: **must not contain** `AssetRiskScores`, `TenantSoftwareRiskScores`, `NormalizedSoftware`, `NormalizedSoftwareAliases`, `NormalizedSoftwareInstallations`, `TenantVulnerabilities`, `VulnerabilityAssets`. **Must contain** canonical tables (`Devices`, `SoftwareProducts`, `InstalledSoftware`, `Vulnerabilities`, `DeviceVulnerabilityExposures`, `ExposureAssessments`, `ExposureEpisodes`, `RemediationCases`, `DeviceRiskScores`, `SoftwareRiskScores`, `TeamRiskScores`, `DeviceGroupRiskScores`, `TenantRiskScoreSnapshots`).
- [ ] Wipe dev database and verify EF can create it cleanly:
  ```bash
  PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS patchhound;"
  PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "CREATE DATABASE patchhound;"
  dotnet ef database update --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api
  ```
- [ ] Commit: `feat(db): regenerate canonical Initial migration (no legacy tables)`.

---

## Task 7: Update `docs/data-model-refactor.md`

**Why:** the Phase 6 doc describes the canonical model but was written while legacy entities still existed. Remove any stale references.

**Steps:**
- [ ] Re-read `docs/data-model-refactor.md`. Remove mentions of legacy entities except in the "historical" callout.
- [ ] Add a paragraph under "Migrations" noting the `Initial` migration was regenerated in Phase 7 after the final legacy entities were removed.
- [ ] Commit: `docs(phase-7): update canonical reference for final cleanup`.

---

## Task 8: Final legacy-reference grep sweep

**Steps:**
- [ ] Run:
  ```bash
  grep -rn 'AssetRiskScore\b\|TenantSoftwareRiskScore\b\|NormalizedSoftware\b\|TenantVulnerability\b\|VulnerabilityAsset\b\|NormalizedSoftwareVulnerabilityProjection\|Phase 4 debt\|disabled during canonical migration' src/ tests/ frontend/src
  ```
- [ ] Expected: **zero hits** except inside `docs/superpowers/archive/` and inside `RelatedEntityType` string literals that must stay for historical rows (if Task 3 decided to keep them — note as acceptable exception in PR body).
- [ ] If any unexpected hit remains, fix it or document it as an acceptable exception.

---

## Task 9: Final green build + test run

**Steps:**
- [ ] `dotnet build PatchHound.slnx`
- [ ] `dotnet test PatchHound.slnx`
- [ ] `(cd frontend && npm run lint && npm run typecheck && npm test -- --run)`
- [ ] All green. If anything is red, fix before opening PR.

---

## Task 10: Open PR + close issue #17

**Steps:**
- [ ] `git push -u origin data-model-canonical-cleanup-phase-7`
- [ ] Open PR titled `Phase 7: finish canonical cleanup (closes #17)`.
- [ ] Body includes:
  - Commit list with per-surface diff summary.
  - Legacy-reference grep sweep results (before/after).
  - Regenerated `Initial` migration table list for reviewer audit.
  - `Closes #17` (this one actually closes).
- [ ] After merge, delete the branch.

---

## Risk notes

- **Task 2 DTO shape changes** ripple to frontend. Land Task 2 and its frontend updates in the same commit; don't split.
- **Task 3 notification/comment string literals**: if historical rows reference `"TenantVulnerability"`, a read-side redirect is safer than a data migration. Whichever choice is made, document it in the commit body.
- **Task 6 migration regeneration** assumes no production data yet. If this assumption is wrong, abort and plan a data-preserving migration instead.
- **Task 5 must land after Tasks 2–4** — reverse order will break the build.

---

## PR #31 pre-merge update (one-liner, not part of Phase 7)

Before Phase 7 starts, update PR #31's body to acknowledge the scope split:
> This PR lands the docs + migration consolidation only. `AssetRiskScores`, `TenantSoftwareRiskScores`, and `NormalizedSoftware*` tables remain in the `Initial` migration because the entities and their read-surface consumers were not fully removed in prior phases. Phase 7 finishes the canonical cleanup and closes #17. See `docs/superpowers/plans/2026-04-14-data-model-canonical-cleanup-phase-7.md`.
