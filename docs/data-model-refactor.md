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
`src/PatchHound.Infrastructure/Migrations/<timestamp>_Initial.cs`. The
`PatchHoundDbContextModelSnapshot.cs` beside it is the authoritative record
of the model that `Initial` builds.

> **Phase 7 (2026-04-14):** The `Initial` migration was regenerated after all
> remaining legacy entities (`AssetRiskScore`, `TenantSoftwareRiskScore`,
> `NormalizedSoftware`) were deleted and their consumers rewritten against the
> canonical model. The migration no longer contains `AssetRiskScores`,
> `TenantSoftwareRiskScores`, or `NormalizedSoftware` tables.
>
> **Phase 7b (2026-04-14):** The last three legacy-named entities were renamed
> to canonical names: `TenantSoftware` → `SoftwareTenantRecord` (table:
> `SoftwareTenantRecords`), `NormalizedSoftwareAlias` → `SoftwareProductAlias`
> (table: `SoftwareProductAliases`), and `NormalizedSoftwareInstallation` →
> `SoftwareProductInstallation` (table: `SoftwareProductInstallations`). The
> `Initial` migration was regenerated against the fully canonical model. This
> change closes issue #17 — the canonical baseline now truly is canonical.

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
