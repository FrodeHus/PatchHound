# Data Model Canonical Cleanup — Phase 6 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reset the EF Core migration history to a single `Initial` migration generated against the final canonical entity set, verify the application boots and tests pass against a freshly-created database, and replace the historical `docs/data-model-refactor.md` with a concise reference document describing the final model.

**Architecture:** Phase 6 is purely housekeeping. No entity shape changes, no service rewrites, no controller changes, no frontend changes. The only code-adjacent work is: (a) deleting every file under `src/PatchHound.Infrastructure/Data/Migrations/`, (b) running `dotnet ef migrations add Initial` so EF generates both a fresh migration and a fresh `PatchHoundDbContextModelSnapshot.cs` against the entity set that Phases 1–5 have left behind, (c) confirming the backend + frontend suites are still green when run against a database built from scratch via `dotnet ef database update`. The documentation work is: archive the 1743-line historical doc to `docs/superpowers/archive/`, then rewrite `docs/data-model-refactor.md` as a ≤200-line reference describing the final model, the tenant isolation rules from §4.10, and pointers to canonical services.

**Tech Stack:** .NET 10 / EF Core 10 / PostgreSQL / xUnit / React + Vitest.

**Prerequisites:**
- Phases 1–5 merged to `main`.
- The workspace builds clean and `dotnet test` / `npm test` are green on `main`.
- The abandoned branch `data-model-refactor-clean-reset` still exists locally or on `origin/` — it holds the 1743-line historical `docs/data-model-refactor.md` that Phase 6 archives. (If the branch has been pruned, fall back to `git log --all --diff-filter=A -- docs/data-model-refactor.md` to find the commit SHA the file was added in, and `git show <sha>:docs/data-model-refactor.md` to retrieve it.)
- PostgreSQL is reachable from the dev machine at `localhost:5432` and the `postgres` superuser can create/drop the `patchhound` database. The connection string in `src/PatchHound.Api/appsettings.Development.json` points at the same instance.
- `dotnet-ef` tool is installed globally or restored as a local tool. If not, `dotnet tool install --global dotnet-ef` first.

**Scope note:** This plan touches zero `src/PatchHound.Core/` entity files and zero `src/PatchHound.Api` / `src/PatchHound.Infrastructure` service files. If a task below would require an entity-shape change to succeed, the fix belongs in an earlier phase, not here. Stop and escalate if that happens.

---

## Preflight

- [ ] **P1: Confirm Phases 1–5 merged**

Run:
```bash
git log --oneline main -30 | grep -E "canonical-cleanup-phase-[1-5]"
```
Expected: five distinct commits, one per phase. If any are missing, stop and merge them first.

- [ ] **P2: Cut Phase 6 branch**

```bash
git checkout main && git pull
git checkout -b data-model-canonical-cleanup-phase-6
```

- [ ] **P3: Baseline green build on `main` state**

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx
(cd frontend && npm run typecheck && npm test -- --run)
```
Expected: all green. If any are red on fresh Phase 5 `main`, stop and fix `main` first — Phase 6 cannot interpret a red baseline as "migration reset regression."

- [ ] **P4: Confirm `dotnet-ef` is available**

```bash
dotnet ef --version
```
Expected: a version number is printed (EF 9.x). If the command is not found, run `dotnet tool install --global dotnet-ef` and re-run the check.

- [ ] **P5: Confirm the historical doc can be retrieved**

```bash
git show data-model-refactor-clean-reset:docs/data-model-refactor.md | wc -l
```
Expected: around 1743 lines. If the branch is gone, fall back to:
```bash
git log --all --oneline --diff-filter=A -- docs/data-model-refactor.md
```
Note the commit SHA — later tasks will reference it instead of the branch name. Record the SHA or branch name in the PR description under "historical doc source".

- [ ] **P6: Snapshot the current migration folder count**

```bash
ls src/PatchHound.Infrastructure/Data/Migrations/ | wc -l
```
Expected: a non-zero count (roughly 180+ if each migration has both a `.cs` and a `.Designer.cs` companion plus the `PatchHoundDbContextModelSnapshot.cs`). Record the exact count in the PR description so the reviewer can verify the delete + regenerate swing.

- [ ] **P7: Verify PostgreSQL reachable**

```bash
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "SELECT 1;"
```
Expected: `1` returned. If this fails, start the dev database before proceeding.

---

### Task 1: Archive the historical data-model-refactor doc

**Files:**
- Create: `docs/superpowers/archive/2026-04-10-data-model-refactor-history.md`

- [ ] **Step 1: Create the archive directory**

```bash
mkdir -p docs/superpowers/archive
```

- [ ] **Step 2: Copy the historical document out of the abandoned branch**

```bash
git show data-model-refactor-clean-reset:docs/data-model-refactor.md \
  > docs/superpowers/archive/2026-04-10-data-model-refactor-history.md
```

If `data-model-refactor-clean-reset` is gone, use the commit SHA recorded in P5 instead:
```bash
git show <sha>:docs/data-model-refactor.md \
  > docs/superpowers/archive/2026-04-10-data-model-refactor-history.md
```

- [ ] **Step 3: Verify the archive is non-empty and has the expected shape**

```bash
wc -l docs/superpowers/archive/2026-04-10-data-model-refactor-history.md
head -5 docs/superpowers/archive/2026-04-10-data-model-refactor-history.md
```
Expected: around 1743 lines. Head should show the original title/heading of the historical doc — confirm it looks like the old data model refactor narrative and not some unrelated file.

- [ ] **Step 4: Prepend an archive banner**

Open `docs/superpowers/archive/2026-04-10-data-model-refactor-history.md` and insert the following block at the very top of the file (before the existing first line):

```markdown
> **ARCHIVED 2026-04-10.** This document is the historical working draft that
> preceded the canonical data model cleanup refactor. It is preserved verbatim
> for future archaeology. **Do not treat any statement in this file as current
> architectural guidance.** The authoritative reference is
> `docs/data-model-refactor.md` (≤200 lines) which describes the final model
> shipped at the end of the 6-phase canonical cleanup. The design rationale
> for the cleanup itself lives at
> `docs/superpowers/specs/2026-04-10-data-model-canonical-cleanup-design.md`.

---

```

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/archive/2026-04-10-data-model-refactor-history.md
git commit -m "docs: archive historical data-model-refactor.md as pre-cleanup reference"
```

---

### Task 2: Delete every existing EF Core migration file

**Files:**
- Delete: all files under `src/PatchHound.Infrastructure/Data/Migrations/`

> **Scope guard.** Only `src/PatchHound.Infrastructure/Data/Migrations/` is touched. Do **not** delete anything under `src/PatchHound.Infrastructure/Data/` that is not in the `Migrations/` subdirectory (the `DesignTimeDbContextFactory.cs`, `PatchHoundDbContext.cs`, and any seed/config files live as siblings and must survive). Do **not** delete any `*.csproj` reference — the folder is included by glob, so removing the files alone is sufficient.

- [ ] **Step 1: List what is about to be deleted**

```bash
ls src/PatchHound.Infrastructure/Data/Migrations/
```
Expected: a list ending with `PatchHoundDbContextModelSnapshot.cs` and including dozens of `YYYYMMDDHHMMSS_*.cs` and matching `*.Designer.cs` files. Copy this list into the PR description under "deleted migration files" for the reviewer.

- [ ] **Step 2: Delete the migration files**

```bash
rm -rf src/PatchHound.Infrastructure/Data/Migrations/
```

- [ ] **Step 3: Verify the folder is gone**

```bash
ls src/PatchHound.Infrastructure/Data/ | grep -i Migrations || echo "ok: Migrations/ absent"
```
Expected: `ok: Migrations/ absent`.

- [ ] **Step 4: Verify the project still builds (without migrations)**

```bash
dotnet build PatchHound.slnx
```
Expected: clean build, 0 warnings, 0 errors. If the build fails because some code explicitly referenced a migration class by name (e.g. a test that `new`-s a migration type), that is a pre-existing leak that should be fixed in a tiny follow-up commit before proceeding — do not work around it by recreating migration files.

- [ ] **Step 5: Commit the deletion**

```bash
git add -A src/PatchHound.Infrastructure/Data/Migrations/
git commit -m "chore(db): delete legacy migrations prior to canonical baseline reset"
```

Note: `git add -A` on a removed directory records the deletions. Verify with `git status` first that only files under `src/PatchHound.Infrastructure/Data/Migrations/` are staged.

---

### Task 3: Regenerate a single `Initial` migration against the canonical model

**Files:**
- Create: `src/PatchHound.Infrastructure/Data/Migrations/<timestamp>_Initial.cs`
- Create: `src/PatchHound.Infrastructure/Data/Migrations/<timestamp>_Initial.Designer.cs`
- Create: `src/PatchHound.Infrastructure/Data/Migrations/PatchHoundDbContextModelSnapshot.cs`

- [ ] **Step 1: Generate the baseline migration**

```bash
dotnet ef migrations add Initial \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api \
  --context PatchHoundDbContext \
  --output-dir Data/Migrations
```
Expected output from EF: "Done. To undo this action, use 'ef migrations remove'." Three new files land under `src/PatchHound.Infrastructure/Data/Migrations/`.

- [ ] **Step 2: Verify the three new files exist**

```bash
ls src/PatchHound.Infrastructure/Data/Migrations/
```
Expected: exactly three files — `<timestamp>_Initial.cs`, `<timestamp>_Initial.Designer.cs`, and `PatchHoundDbContextModelSnapshot.cs`. If there are more than three files, something misfired — delete the folder again and re-run Step 1.

- [ ] **Step 3: Scan the generated migration for legacy table names**

The generated migration is the ground truth for what the final model actually contains. It must not mention any of the tables deleted during Phases 1–5:

```bash
grep -E "Asset|TenantSoftware|NormalizedSoftware|VulnerabilityDefinition|TenantVulnerability|VulnerabilityAsset" \
  src/PatchHound.Infrastructure/Data/Migrations/*.cs \
  | grep -v -E "DeviceVulnerabilityExposure|SoftwareAlias|StagedDeviceSoftwareInstallation|VulnerabilityApplicability|VulnerabilityReference|AssetTag|AssetRule"
```
Expected: empty output. (The exclusions above are legitimate tokens from canonical entity names that happen to share a substring. Adjust only if a legitimate canonical entity introduced in Phases 1–5 also matches — in that case, add it to the `grep -v` list and document why in the PR description.)

If the grep returns any legacy entity name, **stop.** The entity was not actually deleted in the earlier phase that claimed to delete it. Escalate with the entity name and the earlier phase PR — this is an earlier-phase bug that must be fixed at its source, not papered over by hand-editing the generated migration.

- [ ] **Step 4: Scan the migration for the canonical entity set**

Every entity introduced or retained by Phases 1–5 must appear in the new baseline. The grep below is a smoke test, not an exhaustive audit:

```bash
for t in Devices SoftwareProducts SoftwareAliases InstalledSoftwares TenantSoftwareProductInsights \
         DeviceBusinessLabels DeviceRiskScores DeviceRules DeviceTags SecurityProfiles \
         Vulnerabilities VulnerabilityReferences VulnerabilityApplicabilities ThreatAssessments \
         DeviceVulnerabilityExposures ExposureEpisodes ExposureAssessments \
         RemediationCases RemediationWorkflows RemediationDecisions PatchingTasks ApprovalTasks \
         RiskAcceptances AnalystRecommendations AIReports RemediationAiJobs SoftwareDescriptionJobs \
         SoftwareRiskScores TeamRiskScores DeviceGroupRiskScores TenantRiskScoreSnapshots; do
  if ! grep -q "\"$t\"" src/PatchHound.Infrastructure/Data/Migrations/*_Initial.cs; then
    echo "MISSING: $t"
  fi
done
```
Expected: no `MISSING:` lines. Table names are EF's default pluralization — if a specific entity uses `HasTable("...")` to override, adjust the grep to match the override. If a name is genuinely missing, the entity was never registered as a `DbSet<T>` on `PatchHoundDbContext` and Phase 6 has uncovered a pre-existing bug that belongs back in the phase that introduced that entity.

- [ ] **Step 5: Build with the new migration in place**

```bash
dotnet build PatchHound.slnx
```
Expected: clean build, 0 warnings, 0 errors.

- [ ] **Step 6: Commit the regenerated baseline**

```bash
git add src/PatchHound.Infrastructure/Data/Migrations/
git commit -m "feat(db): regenerate single Initial migration against canonical model"
```

---

### Task 4: Verify the database boots cleanly from scratch

**Files:** (none modified; verification only)

- [ ] **Step 1: Drop and recreate the dev database**

```bash
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS patchhound;"
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "CREATE DATABASE patchhound;"
```

- [ ] **Step 2: Apply the Initial migration from empty**

```bash
dotnet ef database update \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api \
  --context PatchHoundDbContext
```
Expected: EF prints `Applying migration '<timestamp>_Initial'.` followed by `Done.` No errors, no warnings about pending changes, no "model differs from snapshot" messages.

- [ ] **Step 3: Sanity-check the resulting schema**

```bash
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -d patchhound -c "\dt" | wc -l
```
Expected: a table count consistent with the canonical entity set (roughly 35–45 tables depending on join tables). Record the exact number in the PR description.

Then, explicitly confirm none of the legacy tables exist:
```bash
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -d patchhound -c "\dt" \
  | grep -E "assets|tenant_software|normalized_software|vulnerability_definition|tenant_vulnerabilities|vulnerability_assets" \
  || echo "ok: no legacy tables"
```
Expected: `ok: no legacy tables`.

- [ ] **Step 4: Confirm `__EFMigrationsHistory` shows exactly one row**

```bash
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -d patchhound \
  -c "SELECT migration_id FROM \"__EFMigrationsHistory\";"
```
Expected: exactly one row, ending in `_Initial`. If there are zero or more than one, something is wrong — do not proceed.

- [ ] **Step 5: Confirm EF sees the model as up-to-date**

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api \
  --context PatchHoundDbContext
```
Expected: "No changes have been made to the model since the last migration." (exit code 0). If EF reports pending changes, the snapshot and the live model disagree — re-run Task 3 Step 1 (which will refuse because a migration already exists) or, more commonly, fix the disagreement by deleting the freshly generated migration and re-running `migrations add Initial` after investigating why the second run sees changes.

---

### Task 5: Run the full backend test suite against the fresh baseline

**Files:** (none modified; verification only)

- [ ] **Step 1: Run the full backend test suite**

```bash
dotnet test PatchHound.slnx
```
Expected: green. If any test fails, inspect whether the failure is (a) a test that leaked shared database state from a previous Phase 5 run, or (b) a real regression against the new baseline.

For (a): ensure the test fixtures clean up after themselves (`Respawner`, `DatabaseFixture`, or per-test-run containers). Re-run in isolation:
```bash
dotnet test tests/PatchHound.Tests/Infrastructure
dotnet test tests/PatchHound.Tests/Api
```

For (b): stop and escalate. A red backend test against the canonical baseline means Phase 6 has uncovered an earlier-phase correctness bug — the fix belongs in that earlier phase's branch or a targeted hotfix, not in Phase 6.

- [ ] **Step 2: Run the frontend test suite**

```bash
cd frontend && npm run typecheck && npm test -- --run
```
Expected: green. Frontend does not touch migrations directly, so a failure here is almost certainly unrelated to the reset; investigate and fix before merging.

- [ ] **Step 3: Commit nothing yet**

No code changes were made in Task 5 — it is a verification gate. Proceed to Task 6.

---

### Task 6: Author the new `docs/data-model-refactor.md` reference

**Files:**
- Create: `docs/data-model-refactor.md`

The rewritten document is a **reference**, not a history. It describes the final canonical model, the tenant isolation rules, and pointers to the services. It must be ≤200 lines total. Use tables aggressively. Do not include migration history, "why we did this", rationale, or decision logs — those live in the spec and the archived historical doc.

- [ ] **Step 1: Create the reference doc**

Create `docs/data-model-refactor.md` with the following content verbatim:

```markdown
# Data Model Reference

> **Scope.** This document is the current, authoritative reference for the
> canonical PatchHound data model. It replaces the 1743-line historical
> working document (archived at
> `docs/superpowers/archive/2026-04-10-data-model-refactor-history.md`). The
> design rationale for the refactor that produced this model lives in
> `docs/superpowers/specs/2026-04-10-data-model-canonical-cleanup-design.md`.

## 1. Model overview

The model is partitioned into four domains:

| Domain | Authoritative entities | Scope |
| --- | --- | --- |
| Inventory | `Device`, `InstalledSoftware`, `TenantSoftwareProductInsight` | Tenant-scoped |
| Software identity | `SoftwareProduct`, `SoftwareAlias`, `SourceSystem` | Global |
| Vulnerability knowledge | `Vulnerability`, `VulnerabilityReference`, `VulnerabilityApplicability`, `ThreatAssessment` | Global |
| Exposure + remediation | `DeviceVulnerabilityExposure`, `ExposureEpisode`, `ExposureAssessment`, `RemediationCase`, `RemediationWorkflow`, `RemediationDecision`, `PatchingTask`, `ApprovalTask`, `RiskAcceptance`, `AnalystRecommendation`, `AIReport`, `RemediationAiJob`, `SoftwareDescriptionJob` | Tenant-scoped |
| Risk scoring | `DeviceRiskScore`, `SoftwareRiskScore`, `TeamRiskScore`, `DeviceGroupRiskScore`, `TenantRiskScoreSnapshot` | Tenant-scoped |
| Device policy | `DeviceBusinessLabel`, `DeviceRule`, `DeviceTag`, `SecurityProfile` | Tenant-scoped |

Globals are writable only by system-context ingestion
(`IsSystemContext = true`). Every tenant-scoped entity carries a direct
`TenantId` column and an EF Core global query filter of the form
`HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId))`.

## 2. Key identity and scope keys

| Entity | Primary identity | Notes |
| --- | --- | --- |
| `Device` | `(TenantId, SourceSystemId, ExternalId)` | Source-owned. No cross-source correlation. |
| `SoftwareProduct` | canonical CPE / normalized vendor+product | Global. One row per real-world product. |
| `SoftwareAlias` | `(SourceSystemId, ExternalId)` → `SoftwareProductId` | Global mapping from source IDs to products. |
| `InstalledSoftware` | `(TenantId, DeviceId, SoftwareProductId)` | Tenant-scoped installation record. |
| `Vulnerability` | canonical CVE / source ID | Global. |
| `VulnerabilityApplicability` | `(VulnerabilityId, SoftwareProductId, version predicate)` | Global. Joins vuln knowledge to product identity. |
| `DeviceVulnerabilityExposure` | `(TenantId, DeviceId, VulnerabilityId)` | Tenant-scoped. Exists iff a live `InstalledSoftware` on `DeviceId` matches a `VulnerabilityApplicability` for `VulnerabilityId`. |
| `ExposureEpisode` | `(ExposureId, OpenedAt)` | Tracks open → resolve → reopen lifecycle. |
| `ExposureAssessment` | per exposure, carries env-CVSS from `SecurityProfile` | Tenant-scoped. |
| `RemediationCase` | `(TenantId, SoftwareProductId)` | Aggregate root for all remediation process records on a product. Stable across snapshot churn. |
| `DeviceRiskScore` | `DeviceId` | Tenant-scoped via `Device`. |
| `SoftwareRiskScore` | `(TenantId, SoftwareProductId)` | Tenant-scoped. |

## 3. Tenant isolation rules

These rules are the hard invariants of the model. Every new query, service,
and entity must satisfy all of them. They are lifted verbatim from §4.10 of
the design spec.

1. **Direct `TenantId` on every tenant-scoped entity.** No transitive-only
   scoping. Even rows whose tenant owner is reachable through a foreign key
   carry their own `TenantId` column.
2. **Mandatory EF Core global query filter** on every tenant-scoped entity:
   `HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId))`.
3. **Global entities carry no `TenantId` and no tenant filter.** Only the
   entities listed in §1 as "Global" are exempt.
4. **`TenantId` is always derived from `TenantContext.CurrentTenantId` at
   write time.** Services never accept `tenantId` from request bodies.
5. **`IgnoreQueryFilters()` is banned outside explicitly system-context code
   paths.** Uses must be justified in the PR description.
6. **Joins between tenant-scoped and global rows never reveal cross-tenant
   state.** Global lookups return only publicly-derivable data.
7. **Tenant-specific product context lives on
   `TenantSoftwareProductInsight`,** never on `SoftwareProduct`.
8. **Cross-entity foreign keys within the tenant domain must agree on
   `TenantId`.** E.g. `RemediationCase.TenantId` must equal the
   `Device.TenantId` of every `DeviceVulnerabilityExposure` the case covers.
9. **Global entity writes come from system-context ingestion only.** A
   normal authenticated request cannot create a `SoftwareProduct`,
   `Vulnerability`, `VulnerabilityApplicability`, `VulnerabilityReference`,
   `ThreatAssessment`, or `SourceSystem` row.
10. **Verification is not optional.** Every change must include a
    tenant-isolation assertion in `TenantIsolationEndToEndTests` covering
    any new query path it introduces.

## 4. Canonical service pointers

| Concern | Service | Path |
| --- | --- | --- |
| Ingestion (Defender + general) | `IngestionService`, `StagedDeviceMergeService` | `src/PatchHound.Infrastructure/Services/Ingestion/` |
| Authenticated scan ingestion | `AuthenticatedScanIngestionService` | `src/PatchHound.Infrastructure/Services/AuthenticatedScans/` |
| Software alias resolution | `SoftwareAliasResolver` | `src/PatchHound.Infrastructure/Services/Software/` |
| Exposure derivation | `ExposureDerivationService` | `src/PatchHound.Infrastructure/Services/Exposure/` |
| Environmental assessment | `ExposureAssessmentService` | `src/PatchHound.Infrastructure/Services/Exposure/` |
| Remediation case lifecycle | `RemediationCaseService` | `src/PatchHound.Infrastructure/Services/Remediation/` |
| Remediation workflow progression | `RemediationWorkflowService` | `src/PatchHound.Infrastructure/Services/Remediation/` |
| Remediation decision creation | `RemediationDecisionService` | `src/PatchHound.Infrastructure/Services/Remediation/` |
| Risk scoring (all levels) | `RiskScoreService` | `src/PatchHound.Infrastructure/Services/RiskScoreService.cs` |
| Dashboard read models | `DashboardQueryService` | `src/PatchHound.Api/Services/DashboardQueryService.cs` |
| Email notifications | `EmailNotificationService` | `src/PatchHound.Infrastructure/Services/EmailNotificationService.cs` |

All of the above services respect the tenant isolation rules in §3 and
depend only on the canonical entity set in §1–§2. There are no "canonical
first, legacy fallback" dual paths anywhere in the codebase — if you see
one, it is a bug.

## 5. Migrations

The database baseline is a single EF Core migration named `Initial`, generated
against the final canonical entity set and living at
`src/PatchHound.Infrastructure/Data/Migrations/<timestamp>_Initial.cs`. The
`PatchHoundDbContextModelSnapshot.cs` beside it is the authoritative record
of the model that `Initial` builds.

To create a fresh dev database:

```bash
dotnet ef database update \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api \
  --context PatchHoundDbContext
```

New schema changes go through the normal EF workflow (`dotnet ef migrations
add <name>`). Do **not** hand-edit `Initial` or its snapshot; generate a new
migration on top.

## 6. Testing gates

Every PR that touches the data model must keep these gates green:

- `TenantIsolationEndToEndTests` — two-tenant seed + per-endpoint assertions
  that tenant A cannot observe tenant B rows.
- Env-severity regression test — `SecurityProfile` environmental modifiers
  must reach `ExposureAssessment.Score`.
- Ingestion idempotency — re-running ingestion against the same observations
  produces no duplicates.
- Global-entity write protection — non-system requests get 403/404 when
  attempting to create global rows.
- Remediation case stability — `(TenantId, SoftwareProductId)` is stable
  across snapshot publish/discard cycles.

See §7 of the design spec for the full verification matrix.
```

- [ ] **Step 2: Verify the file is ≤200 lines**

```bash
wc -l docs/data-model-refactor.md
```
Expected: ≤200. If over, tighten §1–§6 until it fits — do not drop whole sections. The §3 isolation rules list is non-negotiable.

- [ ] **Step 3: Verify the file renders as valid markdown**

```bash
head -3 docs/data-model-refactor.md
```
Expected: heading `# Data Model Reference` on line 1 and the archive-pointer blockquote starting on line 3.

- [ ] **Step 4: Commit**

```bash
git add docs/data-model-refactor.md
git commit -m "docs: rewrite data-model-refactor.md as canonical reference"
```

---

### Task 7: Final legacy-reference grep sweep across the workspace

**Files:** (none modified; verification only)

Phase 6 is the last phase. By this point, every reference to any legacy entity, service, or route should be gone from the codebase. This task confirms that.

- [ ] **Step 1: Grep for legacy entity names in source**

```bash
grep -rn -E "\b(TenantSoftware|SoftwareAsset|TenantVulnerability|VulnerabilityAsset|NormalizedSoftware|AssetRiskScore|TenantSoftwareRiskScore|VulnerabilityDefinition|DeviceSoftwareInstallation|SoftwareCpeBinding|StagedAsset|AssetBusinessLabel|AssetRule|AssetTag|AssetSecurityProfile|AssetType)\b" \
  src/ frontend/src/ \
  --include="*.cs" --include="*.ts" --include="*.tsx" 2>/dev/null \
  | grep -v "src/PatchHound.Infrastructure/Data/Migrations/" \
  | grep -v "docs/superpowers/archive/"
```
Expected: empty output.

If the grep returns anything, inspect each hit. Acceptable exceptions:
- String literals inside **tests** that describe the old behavior explicitly (e.g. `"asset"` as a route path that is being asserted 404). Rare; flag each in the PR description.
- None others. If a `.cs` or `.tsx` file still references a legacy type, it was missed in an earlier phase — escalate, fix at source, do not paper over.

- [ ] **Step 2: Grep for legacy API routes**

```bash
grep -rn -E "/api/assets|/api/software/[^/]+/remediation|/api/remediation/[^c]" \
  src/PatchHound.Api/ frontend/src/ \
  --include="*.cs" --include="*.ts" --include="*.tsx" 2>/dev/null
```
Expected: empty for `/api/assets` and for the software-scoped remediation route. The third pattern (`/api/remediation/[^c]`) is a fuzzy check to catch any leftover `/api/remediation/{workflowId}` style route that should have collapsed into `/api/remediation/cases/{caseId}` in Phase 4. Inspect each hit; expected to be empty.

- [ ] **Step 3: Grep for `IgnoreQueryFilters()` outside system-context paths**

```bash
grep -rn "IgnoreQueryFilters" src/ --include="*.cs" 2>/dev/null \
  | grep -v -E "Ingestion|SystemContext|HealthCheck|AdminHousekeeping"
```
Expected: empty. Per Rule 5, `IgnoreQueryFilters()` is banned outside explicitly system-context paths. Any hit is a tenant isolation risk and must be fixed before merging Phase 6.

- [ ] **Step 4: Grep for `TODO.*phase.[1-5]` markers**

```bash
grep -rn -E "TODO.*phase.?[1-5]|PHASE_[1-5]_STUB" src/ frontend/src/ 2>/dev/null
```
Expected: empty. Any hits indicate a Phase-N stub was never filled in.

- [ ] **Step 5: Confirm the Phase 5 stub-inventory note is obsolete and delete it**

```bash
ls docs/superpowers/plans/2026-04-10-phase-5-stub-inventory.md 2>/dev/null \
  && git rm docs/superpowers/plans/2026-04-10-phase-5-stub-inventory.md \
  || echo "ok: already absent"
```

If the note existed and was removed, commit it:
```bash
git commit -m "docs: remove phase-5 stub inventory now that phase 6 has landed"
```

---

### Task 8: Final green build + test run on fresh DB

**Files:** (none modified; verification only)

- [ ] **Step 1: Drop and recreate the dev database one more time**

```bash
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS patchhound;"
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "CREATE DATABASE patchhound;"
```

- [ ] **Step 2: Apply migrations**

```bash
dotnet ef database update \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api \
  --context PatchHoundDbContext
```
Expected: `Applying migration '<timestamp>_Initial'.` then `Done.`

- [ ] **Step 3: Full backend suite**

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx
```
Expected: 0 warnings, 0 errors, all tests green.

- [ ] **Step 4: Full frontend suite**

```bash
cd frontend && npm run typecheck && npm test -- --run
```
Expected: clean typecheck, all tests green.

- [ ] **Step 5: Smoke-test the API boots against the fresh DB**

```bash
dotnet run --project src/PatchHound.Api --no-build &
API_PID=$!
sleep 10
curl -sf http://localhost:5000/health || echo "API did not respond"
kill $API_PID
```
Expected: the `/health` endpoint responds successfully. If it does not, inspect the API startup logs for schema/model mismatches — a common failure mode is that a seed-on-startup routine is still trying to read a legacy table.

If there is no `/health` endpoint, substitute any anonymous GET that the API exposes (e.g. `/api/version` or the root `/`). Record which endpoint was used in the PR description.

---

### Task 9: Open the Phase 6 PR

**Files:** (none modified; PR body only)

- [ ] **Step 1: Push the branch**

```bash
git push -u origin data-model-canonical-cleanup-phase-6
```

- [ ] **Step 2: Open the PR**

Use `gh pr create` with the title `Data model canonical cleanup — Phase 6: baseline migration + docs` and the body below. Substitute the bracketed placeholders with the values recorded during the preflight and tasks.

```markdown
## Summary

- Deletes every file under `src/PatchHound.Infrastructure/Data/Migrations/`
  (previous count: **[P6 count]**) and regenerates a single `Initial`
  migration + model snapshot against the final canonical entity set.
- Verifies the database boots cleanly from empty via
  `dotnet ef database update` and that the full backend + frontend test
  suites pass against the freshly-created baseline.
- Archives the historical 1743-line `docs/data-model-refactor.md` to
  `docs/superpowers/archive/2026-04-10-data-model-refactor-history.md` and
  rewrites `docs/data-model-refactor.md` as a ≤200-line reference describing
  the final model, the tenant isolation rules from §4.10, and pointers to
  the canonical services.
- Completes the 6-phase canonical data model cleanup.

## Historical doc source

`[branch name or SHA recorded in preflight P5]`

## Migration swing

- Legacy migration files deleted: **[P6 count]**
- New migration files added: **3** (`<timestamp>_Initial.cs`,
  `<timestamp>_Initial.Designer.cs`, `PatchHoundDbContextModelSnapshot.cs`)
- Tables in the resulting schema: **[count from Task 4 Step 3]**
- `__EFMigrationsHistory` rows after apply: **1**

## Tenant scope audit

No new entities were introduced in this phase. The tenant scope table from
Phase 5 is unchanged.

## Legacy reference grep results

- Legacy entity name grep (Task 7 Step 1): empty.
- Legacy API route grep (Task 7 Step 2): empty.
- `IgnoreQueryFilters()` outside system-context grep (Task 7 Step 3): empty.
- `TODO.*phase.[1-5]` marker grep (Task 7 Step 4): empty.

## Verification matrix

- [x] `dotnet build` clean — 0 warnings, 0 errors
- [x] `dotnet test` green against fresh DB
- [x] `npm run typecheck` clean
- [x] `npm test` green
- [x] Fresh `dotnet ef database update` from empty PostgreSQL succeeds
- [x] `__EFMigrationsHistory` contains exactly one `Initial` row
- [x] API boots against fresh DB (health/smoke endpoint used: **[endpoint]**)
- [x] `TenantIsolationEndToEndTests` still green (inherited from Phase 5)
- [x] `docs/data-model-refactor.md` ≤200 lines
- [x] Archive at
      `docs/superpowers/archive/2026-04-10-data-model-refactor-history.md`
      carries the archive banner

## Out of scope

- No entity shape changes. No service rewrites. No controller or frontend
  changes. Phase 6 is pure housekeeping.
- If a Phase 6 step surfaced an earlier-phase bug (e.g. an entity missing
  from the baseline), that fix lives in its own targeted hotfix PR against
  the phase that introduced the bug, not in this PR.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

- [ ] **Step 3: Return the PR URL**

After `gh pr create` succeeds, print the PR URL back to the user and stop.

---

## Self-review checklist (run before handoff)

- [ ] Every task above has concrete file paths and exact commands.
- [ ] No task depends on a file, entity, or service that Phase 6 itself is meant to create.
- [ ] The reference doc content in Task 6 Step 1 is complete (no "TBD" or "add here").
- [ ] Task 2 (delete migrations) comes **before** Task 3 (regenerate), not after.
- [ ] Task 4 verifies the fresh DB boot **before** Task 5 runs the test suite — because a broken migration surfaces as a startup error, not as a test failure.
- [ ] Task 7 grep sweeps exclude `docs/superpowers/archive/` and the new `Migrations/` folder so historical references and the generated baseline do not spuriously fail the check.
- [ ] The PR body in Task 9 includes the tenant isolation audit gate from §4.10 Rule 10 (inherited from Phase 5; no new assertions required in Phase 6).
- [ ] No task silently hand-edits a generated migration file. Any disagreement between the snapshot and the live model must be resolved by regeneration, not by patching EF's output.
