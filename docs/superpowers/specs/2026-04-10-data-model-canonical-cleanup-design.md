# Data Model Canonical Cleanup — Design

**Date:** 2026-04-10
**Status:** Approved design, ready for implementation planning
**Supersedes:** the in-flight §21.2/§21.3 work tracked in `docs/data-model-refactor.md`

## 1. Context

PatchHound has been migrating from a legacy data model (`Asset`, `NormalizedSoftware*`, `TenantSoftware*`, `VulnerabilityDefinition*`, `VulnerabilityAsset*`) to a canonical model (`Device`, `SoftwareProduct`, `InstalledSoftware`, `Vulnerability`, `VulnerabilityApplicability`, `DeviceVulnerabilityExposure`, `ExposureEpisode`, `ExposureAssessment`, `RemediationCase`). The in-progress branch `data-model-refactor-clean-reset` introduced most of those canonical entities and then spent its last ~10 commits layering compatibility bridges — "canonical-first, legacy-fallback" reads on roughly 40+ query paths across every remediation, risk, dashboard, and software service. Each bridge exists only to preserve legacy DTO shapes, legacy URLs, or legacy ID continuity.

The constraints that made those bridges necessary no longer apply:

- The database can be treated as greenfield. A single new baseline migration is acceptable; no data preservation is required.
- Legacy URLs (`/api/software/{tenantSoftwareId}`, `/software/{tenantSoftwareId}/remediation`, `/assets/{assetId}`) can break. The product is still in testing; no user deep links, bookmarks, or email deep-links need to survive.
- DTO shapes can drop `tenantSoftwareId`, `softwareAssetId`, `normalizedSoftwareId` fields.
- Risk score numbers may shift slightly when the aggregation source changes from legacy projections to canonical exposure reads.
- Rewriting canonical code that already exists on the abandoned branch is acceptable if it produces a cleaner final model.

Given those freedoms, the cleanup is not "finish the migration as-planned." The cleanup is a fresh ground-up introduction of the canonical model alongside deletion of every legacy entity, with no compatibility bridges. The existing `data-model-refactor-clean-reset` branch is abandoned in favor of a new branch cut from `main`.

## 2. Goals

- Introduce the canonical data model directly on top of `main`, without dual-write, without legacy fallback.
- Delete the legacy data model entirely.
- Enforce strict tenant isolation on every tenant-scoped entity; keep global reference data global.
- Unify device representation under one `Device` entity. `Asset` goes away.
- Unify software representation under `SoftwareProduct` + `SoftwareAlias` + `InstalledSoftware`. The tenant-scoped `TenantSoftware` layer goes away.
- Unify vulnerability representation under `Vulnerability` + `VulnerabilityApplicability` + `DeviceVulnerabilityExposure` + `ExposureEpisode` + `ExposureAssessment`. The `VulnerabilityDefinition*` / `VulnerabilityAsset*` chain goes away.
- Re-anchor every remediation process record on `RemediationCase`. `TenantSoftwareId` and `SoftwareAssetId` columns on remediation tables go away.
- Regenerate a single clean baseline migration at the end.
- Rewrite `docs/data-model-refactor.md` as a short reference of the final model; archive the milestone history.

## 3. Non-goals

- Runner transport protocol changes.
- Tenant authorization model changes.
- AI provider strategy changes.
- Workflow engine internals.
- Adding new features (remediation scopes beyond `SoftwareProductId`, software-scoped rule engine, cross-source device correlation, tenant-level vulnerability severity overrides). Those are deferred to their own work.

## 4. Target data model

### 4.1 Inventory / identity

| Entity | Tenant scope | Notes |
| --- | --- | --- |
| `SourceSystem` | Global | Reference data (e.g. "defender", "tanium"). No `TenantId`. |
| `Device` | Tenant-scoped | Direct `TenantId`. Unique on `(TenantId, SourceSystemId, ExternalId)`. Absorbs every field on `Asset`-with-type-`Device`. |
| `SoftwareProduct` | Global | Canonical product identity. No `TenantId`. Unique on `CanonicalProductKey`. Carries `PrimaryCpe23Uri`. |
| `SoftwareAlias` | Global | Source-observed product names/IDs mapped back to `SoftwareProduct`. Unique on `(SourceSystemId, ExternalId)`. |
| `InstalledSoftware` | Tenant-scoped | Direct `TenantId`. Fact: this `Device` has this `SoftwareProduct` at this version from this `SourceSystem`. Current-state unique key does not depend on nullable columns. |
| `TenantSoftwareProductInsight` | Tenant-scoped | Direct `TenantId`. Tenant-scoped description and supply-chain evidence per `SoftwareProduct`. Keeps global product fields clean. |

### 4.2 Vulnerability knowledge

| Entity | Tenant scope | Notes |
| --- | --- | --- |
| `Vulnerability` | Global | CVE/advisory record. No `TenantId`. |
| `VulnerabilityReference` | Global | External links. |
| `VulnerabilityApplicability` | Global | "This vuln affects this product/version range", keyed by `SoftwareProductId` or CPE criteria. |
| `ThreatAssessment` | Global | Global threat intel / EPSS / KEV. |

### 4.3 Exposure (always tenant-scoped)

| Entity | Tenant scope | Notes |
| --- | --- | --- |
| `DeviceVulnerabilityExposure` | Tenant-scoped | Direct `TenantId`. Current-state row: this `Device` is currently exposed to this `Vulnerability`. |
| `ExposureEpisode` | Tenant-scoped | Direct `TenantId`. Open/reopen/resolve history for an exposure. |
| `ExposureAssessment` | Tenant-scoped | Direct `TenantId`. Contextualized score. Carries `SecurityProfileId` so environmental CVSS modifiers are honored. |

`VulnerabilitySeverityOverride` as a tenant-level override table was in the original §10.1 plan but will **not** be created. Environmental severity stays driven by `SecurityProfile` through `ExposureAssessment`. See §7 (verification gates). The existing `RemediationDecisionVulnerabilityOverride` is a different, per-decision concept and stays (see §4.6).

### 4.4 Device ownership & attributes

All tenant-scoped with direct `TenantId`. Renamed from `Asset*` because they are device-only:

| New | Old | Tenant scope |
| --- | --- | --- |
| `DeviceBusinessLabel` | `AssetBusinessLabel` | Via parent `Device`, plus own `TenantId` column for query filter safety |
| `DeviceRiskScore` | `AssetRiskScore` | Direct `TenantId` |
| `DeviceRule` | `AssetRule` | Direct `TenantId` |
| `DeviceTag` | `AssetTag` | Via parent `Device`, plus own `TenantId` column for query filter safety |
| `SecurityProfile` | `AssetSecurityProfile` | Direct `TenantId` (it's a tenant policy, not an asset) |

`BusinessLabel` (the definition of a label) stays unchanged — tenant-scoped.

A future `SoftwareProductRule` for software-scoped rules is **deferred**. Not built until it is actually needed.

### 4.5 Risk scoring

All rebuildable from canonical exposures. All tenant-scoped with direct `TenantId`:

- `DeviceRiskScore` — keyed on `Device.Id`, carries `TenantId`.
- `SoftwareRiskScore` — keyed on `(TenantId, SoftwareProductId)`. Software risk is per-tenant because different tenants have different install bases.
- `TeamRiskScore` — direct `TenantId`.
- `DeviceGroupRiskScore` — direct `TenantId`.
- `TenantRiskScoreSnapshot` — direct `TenantId`.

Exact numbers may shift from today's values because the aggregation source changes. Accepted.

### 4.6 Remediation

All tenant-scoped with direct `TenantId`:

| Entity | Tenant scope | Notes |
| --- | --- | --- |
| `RemediationCase` | Direct `TenantId` | Aggregate root. **Requires non-null `SoftwareProductId`.** No observed-product fallback. Scope is `(TenantId, SoftwareProductId)`, optionally narrowed by version. |
| `RemediationWorkflow` | Direct `TenantId` | `RemediationCaseId` required. `TenantSoftwareId` column dropped. |
| `RemediationDecision` | Direct `TenantId` | `RemediationCaseId` required. `TenantSoftwareId`, `SoftwareAssetId` dropped. |
| `PatchingTask` | Direct `TenantId` | `RemediationCaseId` required. `TenantSoftwareId`, `SoftwareAssetId` dropped. |
| `ApprovalTask` | Direct `TenantId` | References `RemediationDecisionId`; case reachable through the decision. |
| `RemediationDecisionVulnerabilityOverride` | Direct `TenantId` | Per-decision override of severity for a specific vulnerability inside a workflow. Target is the canonical exposure. |
| `RiskAcceptance`, `AnalystRecommendation`, `AIReport`, `RemediationAiJob`, `SoftwareDescriptionJob`, `RemediationWorkflowStageRecord` | Direct `TenantId` | Re-anchored on `RemediationCase` where they referenced legacy software IDs. `SoftwareDescriptionJob` targets `SoftwareProductId` directly. |

### 4.7 Authenticated scans

All tenant-scoped with direct `TenantId`. Renamed to canonical naming:

`ConnectionProfile`, `ScanningTool`, `ScanningToolVersion`, `ScanProfile`, `ScanProfileTool`, `DeviceScanProfileAssignment` (from `AssetScanProfileAssignment`), `ScanRunner`, `AuthenticatedScanRun`, `ScanJob` (with `DeviceId`, not `AssetId`), `ScanJobResult`, `ScanJobValidationIssue`, `StagedDetectedSoftware` (from `StagedAuthenticatedScanSoftware`).

### 4.8 Ingestion / operational

All tenant-scoped unless noted:

`IngestionRun`, `IngestionCheckpoint`, `EnrichmentJob`, `EnrichmentRun`, `EnrichmentSourceConfiguration`, `TenantSourceConfiguration`, `TenantSlaConfiguration`, `TenantAiProfile`, `SentinelConnectorConfiguration`, `Tenant` (self-filtered), `User` (cross-cutting; filtered via `UserTenantRole`), `UserTenantRole`, `Team`, `TeamMember`, `TeamMembershipRule`, `BusinessLabel`, `OrganizationalSeverity`, `Notification`, `Comment`, `AuditLogEntry`, `AdvancedTool`, `WorkflowDefinition`, `WorkflowInstance`, `WorkflowNodeExecution`, `WorkflowAction`.

Staged observation tables (`StagedVulnerability`, `StagedVulnerabilityExposure`, `StagedDetectedSoftware`) are tenant-scoped with direct `TenantId`.

### 4.9 Deleted entities

**Inventory legacy:**
- `Asset`
- `AssetBusinessLabel`, `AssetRiskScore`, `AssetRule`, `AssetTag` — renamed to `Device*`
- `AssetSecurityProfile` — renamed to `SecurityProfile`
- `AssetType` enum — `CloudResource` was aspirational/unused, `Software`/`Device` collapse into their canonical homes
- `StagedAsset` — renamed to `StagedDevice`
- `StagedDeviceSoftwareInstallation` — replaced by staged detected-software flow

**Software legacy:**
- `TenantSoftware`, `TenantSoftwareRiskScore`
- `NormalizedSoftware`, `NormalizedSoftwareAlias`, `NormalizedSoftwareInstallation`, `NormalizedSoftwareVulnerabilityProjection`
- `DeviceSoftwareInstallation`, `DeviceSoftwareInstallationEpisode`
- `SoftwareCpeBinding`
- `SoftwareVulnerabilityMatch`

**Vulnerability legacy:**
- `VulnerabilityDefinition`, `VulnerabilityDefinitionReference`, `VulnerabilityDefinitionAffectedSoftware`
- `TenantVulnerability`
- `VulnerabilityThreatAssessment`
- `VulnerabilityAsset`, `VulnerabilityAssetEpisode`, `VulnerabilityAssetAssessment`
- `VulnerabilityEpisodeRiskAssessment`

**Operational legacy:**
- `IngestionSnapshot` — temporal markers come from `IngestionRun` instead

**Compatibility / legacy services (none of these are introduced from main; they are named here so implementers know not to re-create them):**
- `LegacySoftwareCompatibilitySyncService`
- `NormalizedSoftwareProjectionService`
- `NormalizedSoftwareResolver`
- Any "canonical-first, legacy-fallback" dual read path

### 4.10 Tenant isolation rules

Tenant separation is a hard invariant of this refactor. The rules below apply to every entity and every query added during phases 1–6.

**Rule 1 — Every tenant-scoped entity carries its own `TenantId` column.**
No transitive-only tenant scoping. Even entities whose "natural" tenant owner is reachable through a foreign key (e.g. `InstalledSoftware.DeviceId → Device.TenantId`) carry their own `TenantId` column. This eliminates the risk that an EF global filter silently fails because the join was not materialized. It also makes cross-tenant queries trivially grepable.

**Rule 2 — Every tenant-scoped entity has an EF Core global query filter.**
`modelBuilder.Entity<X>().HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId))` for every tenant-scoped entity, matching the pattern already used in `PatchHoundDbContext` for legacy tables. The filter is registered during phase 1 EF configuration reviews; every new entity introduced in later phases must add its filter in the same commit as the entity class.

**Rule 3 — Global entities carry no `TenantId` and no tenant filter.**
Only the entities listed as "Global" in §4.1 and §4.2 are exempt. These hold publicly-derivable data (CVE definitions, product identity, source system catalog, applicability rules). They are readable by every authenticated context. They are writable only by system-context operations (ingestion service with `IsSystemContext = true`).

**Rule 4 — `TenantId` on tenant-scoped rows is always derived from `TenantContext.CurrentTenantId` at write time.**
No controller or service accepts `tenantId` as a request-body field for create/update operations. `TenantId` is stamped by the service layer from the authenticated context. Scan runners authenticating via `ScanRunnerBearer` stamp `TenantId` from the runner's `tenant_id` claim, never from the payload.

**Rule 5 — `IgnoreQueryFilters()` is banned outside system-context code paths.**
Uses of `IgnoreQueryFilters()` must appear only inside services explicitly marked as system-context (ingestion, cross-tenant admin housekeeping, migration health checks). Any such call is reviewed by the implementer in the phase PR description and justified. A grep in each phase exit check ensures new `IgnoreQueryFilters()` calls do not appear in request-scoped controllers/services.

**Rule 6 — Writes that link a tenant-scoped row to a global row never reveal cross-tenant state.**
Example: when creating an `InstalledSoftware` row, the service resolves the `SoftwareProduct` (global) by canonical key and the `Device` (tenant-scoped) by `(TenantId, SourceSystemId, ExternalId)`. The `Device` lookup is tenant-filtered. The `SoftwareProduct` lookup is unfiltered but returns only public product identity data. There is no code path where resolving a `SoftwareProduct` leaks that a different tenant has installed it.

**Rule 7 — Tenant-specific product context lives on `TenantSoftwareProductInsight`, never on `SoftwareProduct`.**
`SoftwareProduct.Description`, AI-generated summaries, supply-chain evidence, and any other tenant-contextual evidence go into `TenantSoftwareProductInsight` (tenant-scoped). The global `SoftwareProduct` row carries only lifecycle metadata that is truly global (product name, vendor, primary CPE, end-of-life dates).

**Rule 8 — Foreign keys from tenant-scoped rows to tenant-scoped rows must belong to the same tenant.**
Example: `RemediationCase.TenantId` must equal the `Device.TenantId` of every `DeviceVulnerabilityExposure` the case covers. This is enforced in service-layer invariants and covered by tests. No row may be orphaned or cross-tenant-joined.

**Rule 9 — Global entity writes come from system-context ingestion only.**
A normal user request (even a GlobalAdmin) cannot directly create a `SoftwareProduct`, `Vulnerability`, `VulnerabilityApplicability`, `VulnerabilityReference`, `ThreatAssessment`, or `SourceSystem` row. Those are written by ingestion/enrichment services running with `IsSystemContext = true`. Admin UI surfaces that *appear* to edit these values (e.g. a curated product description) actually write to `TenantSoftwareProductInsight` or similar tenant-scoped shadow tables.

**Rule 10 — Verification is not optional.**
Every phase PR must include (a) a per-entity scope table in the PR description listing new entities and their `TenantId` status, and (b) an end-to-end isolation test that asserts tenant A cannot observe tenant B's data through any new query path added by the phase. See §7.

## 5. Approach

Fresh branch `data-model-canonical-cleanup` cut from the tip of `main`. The existing `data-model-refactor-clean-reset` branch is abandoned wholesale. Canonical entities, services, and migrations that exist on that branch are **intentionally rewritten** on the new branch — the user has approved this because the final model is cleaner when built from scratch under the rules in §4.10 than when layered on top of accumulated compatibility bridges.

Six phases. **One PR per phase.** Every phase exits on a fully green build and test run. No phase leaves a dual-write or fallback path. Every phase is a vertical slice of one domain: introduce the canonical entities for that domain, rewrite every consumer, delete the legacy entities for that domain, all in the same PR.

**Migration strategy across phases.** No EF Core migrations are generated during phases 1–5. Phases 1–5 mutate entities, `DbSet`s, EF configurations, and model snapshot state directly; the existing migration files under `src/PatchHound.Infrastructure/Migrations/` are left untouched and will not match the evolving model. Developers wipe their local dev database between phases instead of running migrations. Tests run against an in-memory or per-test-run provider, so CI is unaffected. Phase 6 is the **only** phase that touches migrations: it deletes every existing migration file, regenerates a single `Initial` migration against the final entity set, and verifies the resulting database boots cleanly.

### 5.1 Phase 1 — Canonical inventory + delete inventory legacy

**Introduce:** `SourceSystem` (global), `Device`, `SoftwareProduct` (global), `SoftwareAlias` (global), `InstalledSoftware`, `TenantSoftwareProductInsight`, `DeviceBusinessLabel`, `DeviceRiskScore`, `DeviceRule`, `DeviceTag`, `SecurityProfile`. Add EF configurations with tenant query filters per §4.10 Rule 2 on all tenant-scoped entities. Global entities carry no filter.

**Authenticated-scan renames:** `AssetScanProfileAssignment` → `DeviceScanProfileAssignment`, `ScanJob.AssetId` → `DeviceId`, `StagedAuthenticatedScanSoftware` → `StagedDetectedSoftware`. Scan runner payloads use `deviceId`/`deviceName`.

**Rewrite:** Defender/general ingestion (`IngestionService`, `StagedAssetMergeService` → `StagedDeviceMergeService`) and authenticated-scan ingestion to write canonical entities only. Asset rules evaluation → device rules. Security profile assignment → direct `Device.SecurityProfileId`. Risk scoring baseline keys off `Device.Id`.

**Frontend:** `AssetsController` → `DevicesController`, `/api/assets` → `/api/devices`, `/assets` route → `/devices` route. Asset rule admin UI → device rule admin UI. Asset business label/tag admin → device equivalents.

**Delete:** `Asset`, `AssetType` enum, `StagedAsset`, `StagedDeviceSoftwareInstallation`, `AssetBusinessLabel`, `AssetRiskScore`, `AssetRule`, `AssetTag`, `AssetSecurityProfile`, `TenantSoftware`, `TenantSoftwareRiskScore`, `NormalizedSoftware`, `NormalizedSoftwareAlias`, `NormalizedSoftwareInstallation`, `NormalizedSoftwareVulnerabilityProjection`, `DeviceSoftwareInstallation`, `DeviceSoftwareInstallationEpisode`, `SoftwareCpeBinding`, `StagedAssetMergeService` (replaced), `NormalizedSoftwareProjectionService`, `NormalizedSoftwareResolver`. Any "canonical-first, legacy-fallback" pattern is simply not created in this phase.

**Exit:** `Asset*`, `TenantSoftware*`, `NormalizedSoftware*`, `SoftwareCpeBinding`, `DeviceSoftwareInstallation*` gone from the workspace. Ingestion writes only canonical. No `tenantSoftwareId` or `softwareAssetId` references anywhere. Frontend `/devices` works. Tenant isolation audit in PR description lists the 11 new entities with their scope. Tenant isolation end-to-end test passes (see §7). `dotnet test` and `npm test` green.

### 5.2 Phase 2 — Canonical vulnerability knowledge + delete vuln legacy

**Introduce:** `Vulnerability`, `VulnerabilityReference`, `VulnerabilityApplicability`, `ThreatAssessment`. All global; no tenant filters. Writes come from ingestion with `IsSystemContext = true`.

**Rewrite:** Vulnerability ingestion (`DefenderVulnerabilitySource` and any other vulnerability source) writes `Vulnerability`, `VulnerabilityReference`, `VulnerabilityApplicability`, `ThreatAssessment` only. `VulnerabilitiesController` and `VulnerabilityDetailQueryService` query canonical and join through `DeviceVulnerabilityExposure` (introduced in Phase 3) once Phase 3 lands — until then, vulnerability read endpoints return global vulnerability data without per-tenant exposure counts. This temporary gap is acceptable because Phase 3 lands in the same merge train.

Actually: to keep Phase 2 exit criteria green without depending on Phase 3, vulnerability list/detail endpoints in Phase 2 return global vulnerability data plus an empty "affected devices" section (explicitly marked as "populated after exposure phase"). Phase 3 fills that section. The frontend behind the feature shows an empty state during the brief window between Phase 2 and Phase 3.

**Delete:** `VulnerabilityDefinition`, `VulnerabilityDefinitionReference`, `VulnerabilityDefinitionAffectedSoftware`, `TenantVulnerability`, `VulnerabilityThreatAssessment`.

**Exit:** `VulnerabilityDefinition*`, `TenantVulnerability`, `VulnerabilityThreatAssessment` gone. Vulnerability ingestion writes canonical. Global-entity write audit in PR description confirms no controller accepts vulnerability creation from non-system context. `dotnet test` and `npm test` green.

### 5.3 Phase 3 — Canonical exposure + delete exposure legacy

**Introduce:** `DeviceVulnerabilityExposure`, `ExposureEpisode`, `ExposureAssessment`. All tenant-scoped with direct `TenantId` and EF global filter.

**Rewrite:** Exposure derivation service reads `InstalledSoftware` and joins to `VulnerabilityApplicability` (through `SoftwareProductId` first, CPE fallback second) to produce `DeviceVulnerabilityExposure` rows. Lifecycle (open/reopen/resolve) attached to `ExposureEpisode`. `ExposureAssessment` computes environmental severity via `Device.SecurityProfileId` → `SecurityProfile` modifiers.

Fill in the Phase 2 gap: vulnerability list/detail now populates "affected devices" and "active exposures" counts from `DeviceVulnerabilityExposure`.

**Hard gate:** a dedicated test seeds (a) a `SecurityProfile` with non-default environmental CVSS modifiers, (b) a `Device` with that profile assigned, (c) an `InstalledSoftware` + matching `VulnerabilityApplicability`, then runs the exposure derivation + assessment services and asserts that:
- `ExposureAssessment.SecurityProfileId` equals the seeded profile
- `ExposureAssessment.Score` is not equal to the base CVSS from `Vulnerability`
- `ExposureAssessment.Reason` references the environmental modifier

No legacy deletions merge until that test exists and is green.

**Delete:** `VulnerabilityAsset`, `VulnerabilityAssetEpisode`, `VulnerabilityAssetAssessment`, `VulnerabilityEpisodeRiskAssessment`, `SoftwareVulnerabilityMatch`, `SoftwareVulnerabilityMatchService`.

**Exit:** Legacy exposure chain gone. Env-severity hard-gate test green. Tenant isolation test extended: tenant A cannot see tenant B's exposures, episodes, or assessments. `dotnet test` and `npm test` green.

### 5.4 Phase 4 — RemediationCase + case-first remediation

**Introduce:** `RemediationCase` (tenant-scoped, requires non-null `SoftwareProductId`).

**Rewrite:** `RemediationWorkflow`, `RemediationDecision`, `PatchingTask`, `ApprovalTask` use `RemediationCaseId` as the scope key. `RiskAcceptance`, `AnalystRecommendation`, `AIReport`, `RemediationAiJob`, `SoftwareDescriptionJob`, `RemediationWorkflowStageRecord` re-anchored: each either references `RemediationCaseId` or, for `SoftwareDescriptionJob`, references `SoftwareProductId` directly. Drop `TenantSoftwareId` and `SoftwareAssetId` columns from every remediation table.

**API:** Delete `/api/remediation/{workflowId}` and tenant-software-scoped remediation routes. Only `/api/remediation/cases/{caseId}/*` exists. Decision, approval, patching-task, AI report, supply-chain, description generation, work notes — all case-first.

**Frontend:** Remediation routes use case IDs. Remediation workbench, decision context, approval task detail, patching task detail, dashboard remediation deep links all use case IDs.

**Exit:** No `TenantSoftwareId`/`SoftwareAssetId` references in any remediation service, sidecar record, controller, DTO, or frontend component. `/api/remediation` surface is case-first only. Tenant isolation test extended: a case in tenant A cannot be addressed by a tenant B user, and remediation actions inside the case write `TenantId` from the authenticated context, never the payload. `dotnet test` and `npm test` green.

### 5.5 Phase 5 — Risk scoring + dashboard + email notifications

**Rewrite:** `RiskScoreService` computes `DeviceRiskScore`, `SoftwareRiskScore` (keyed `(TenantId, SoftwareProductId)`), `TeamRiskScore`, `DeviceGroupRiskScore`, `TenantRiskScoreSnapshot` from canonical `DeviceVulnerabilityExposure` and `ExposureAssessment` rows. Dashboard queries (owner, security manager, technical manager summaries) read canonical. Email notifications (`EmailNotificationService`) derive severity, device counts, product names from canonical rows and case links.

**Delete:** Any remaining dual-path code in dashboards, risk scoring, and notifications. This phase is primarily a read-side cleanup after the write-side is already canonical from phases 1–4.

**Exit:** No remaining references to deleted legacy entities in any service. All read paths are single-path canonical. Tenant isolation test extended with dashboard and notification assertions. `dotnet test` and `npm test` green.

### 5.6 Phase 6 — Baseline migration reset + doc cleanup

Delete every file under `src/PatchHound.Infrastructure/Migrations/`. Generate one new `Initial` migration + model snapshot against the final entity set. Verify the resulting database boots cleanly, runs the full backend and frontend test suite green, and produces a usable `dotnet ef database update` from scratch.

Rewrite `docs/data-model-refactor.md` as a ≤200-line reference describing the final model, the tenant isolation rules from §4.10 of this spec, and pointers to the canonical services. Archive the 1739-line historical document under `docs/superpowers/archive/2026-04-10-data-model-refactor-history.md`.

**Exit:** Single `Initial` migration. Doc rewritten. Full backend + frontend suites green against a freshly-created database.

## 6. Per-phase exit criteria (apply to every phase)

- `dotnet build` clean (0 warnings, 0 errors)
- `dotnet test` green
- `npm run typecheck` clean (frontend)
- `npm test` green (frontend)
- Grep check: no references to deleted types, services, or routes remain in the workspace
- No new dual-path / fallback code introduced
- Phase PR description includes:
  - List of every entity, service, route, and DTO deleted in that phase
  - Tenant scope table for every new entity (name, direct `TenantId` yes/no, EF global filter yes/no, justification if global)
  - Tenant isolation test output showing the two-tenant assertion added by the phase

## 7. Verification gates (end-to-end)

**Tenant isolation gate (Phase 1, extended every phase thereafter):**
A single end-to-end test called `TenantIsolationEndToEndTests` is introduced in Phase 1 and grows with every subsequent phase. It seeds two tenants with disjoint data and asserts that, for every API surface touched by the phase, requests authenticated as tenant A cannot observe any tenant B row. Each phase adds its new entities and routes to the test. A phase PR does not merge if this test is missing the phase's assertions.

Minimum assertions the test covers by the end of Phase 5:
- `GET /api/devices` scoped to tenant A returns no tenant B devices
- `GET /api/software/canonical` returns global product rows but no tenant-scoped fields leak across tenants
- `GET /api/vulnerabilities` returns global CVE rows; exposure counts are scoped to tenant A devices only
- `GET /api/vulnerabilities/{id}` affected-devices section contains only tenant A devices
- `GET /api/remediation/cases/...` scoped to tenant A returns no tenant B cases
- `GET /api/dashboard/*` summaries for tenant A contain no tenant B metrics
- Email notification rendering pipelines do not join across tenants
- Risk score queries (`GET /api/risk-score/*`) for tenant A do not include tenant B data
- Scan runners for tenant A's runner cannot enqueue jobs targeting tenant B devices

**Env-severity gate (Phase 3):** the `SecurityProfile` → `ExposureAssessment` environmental severity test described in §5.3.

**Ingestion idempotency:** re-running ingestion against the same upstream observations does not create duplicate `Device`, `InstalledSoftware`, `Vulnerability`, or `DeviceVulnerabilityExposure` rows.

**Source-collision protection:** same external device ID from two different `SourceSystem`s produces two distinct `Device` rows.

**Software canonicalization determinism:** the same observed software from two sources resolves to the same `SoftwareProduct` via `SoftwareAlias`.

**Global-entity write protection:** an authenticated non-system request that attempts to write a `SoftwareProduct`, `Vulnerability`, `VulnerabilityApplicability`, `VulnerabilityReference`, `ThreatAssessment`, or `SourceSystem` row returns 403 or 404. Tested explicitly.

**Exposure lifecycle:** open → resolve when software is removed → reopen when software returns, covered by integration tests against canonical tables only.

**Remediation case stability:** a `RemediationCase` key `(TenantId, SoftwareProductId)` is stable across snapshot publish/discard cycles.

**Representative queries run without legacy joins:**
- list devices
- list software on a device
- list devices affected by a vulnerability
- list vulnerabilities affecting a software product
- list open remediation cases

## 8. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Phase 1 touches ~100+ files (inventory is wide); high merge conflict risk | One PR, no other branches parallel to Phase 1 |
| Rewriting canonical work that already exists on the abandoned branch is wasteful | Accepted by user; final model clarity outweighs the rewrite cost |
| Env-severity regression when introducing the canonical assessment path | Hard-gate test in Phase 3 blocks deletion of legacy assessment until canonical path is proven |
| Risk score numbers shift user-visible values | Accepted; documented in Phase 5 PR description |
| Cross-tenant data leak via a missed EF global filter | Rule 1 (direct `TenantId` on every tenant-scoped entity), Rule 2 (mandatory global filter), Rule 5 (ban on `IgnoreQueryFilters()`), and the per-phase tenant isolation test |
| Cross-tenant leak via a service that joins global and tenant-scoped rows carelessly | Rule 6 (joins never reveal cross-tenant state), plus isolation test coverage on every new query path |
| Rule 7 violated accidentally: AI writes tenant description onto global `SoftwareProduct` | Covered by the Phase 1 entity structure (`TenantSoftwareProductInsight` is the only writable description target) and by explicit test that asserts `SoftwareProduct.Description` is unchanged after a tenant AI description write |
| Vulnerability list endpoint returns empty "affected devices" between Phase 2 merge and Phase 3 merge | Explicitly called out in §5.2 as acceptable temporary state; Phase 3 PR must land before the next release cut |
| Migration regeneration forgets an entity | Phase 6 exit requires the env-severity test, tenant isolation test, and full test suite to pass on a fresh DB created from the new baseline |

## 9. Out of scope clarifications

- **`CloudResource` asset type:** aspirational, unused except for two incidental references. Deleted with `AssetType`. No replacement.
- **Cross-source device correlation:** explicitly out of scope. Canonical `Device` is source-owned by `(TenantId, SourceSystemId, ExternalId)`. If the same physical device is reported by two sources, there will be two `Device` rows. Merging them belongs to a future feature.
- **Software rule engine:** explicitly deferred. `DeviceRule` only for now.
- **Tenant-level vulnerability severity overrides:** dropped. If a use case surfaces later, add it through `SecurityProfile` environmental modifiers, not as a separate override table.
- **Sharing of `SoftwareAlias` across tenants:** `SoftwareAlias` is intentionally global. An alias is a `(SourceSystemId, ExternalId) → SoftwareProductId` mapping — the external ID identifies a product in the source system, not a tenant. Two tenants using the same source system benefit from the same alias resolution. This is not a tenant boundary violation because the alias carries no tenant-private data.

## 10. Handoff

After this spec is approved and committed:

1. Invoke `superpowers:writing-plans` to produce per-phase implementation plans. Each phase becomes its own plan file under `docs/superpowers/plans/2026-04-10-data-model-canonical-cleanup-phase-{N}.md`.
2. Plans contain exact file paths, code, test code, and commands as per the `writing-plans` skill contract.
3. Phase 1 plan is the first to execute; phases ship in order. Every phase's PR must show the tenant-scope audit table and the tenant isolation test diff per §7.
