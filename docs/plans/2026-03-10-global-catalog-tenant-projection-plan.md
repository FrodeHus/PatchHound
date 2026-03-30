# Global Catalog And Tenant Projection Plan

## Goal

Redesign the data model around a clean split between:

- global catalog data
- tenant-scoped projection, observation, and workflow data

The database is treated as greenfield. Backward compatibility is not a goal. As each migration slice lands, obsolete code, DTOs, routes, and files should be removed rather than preserved behind shims.

## Agreed decisions

- `TenantSoftware` exists only when software is observed in a tenant.
- canonical CPE binding is fully global.
- the vulnerability list shows only definitions that have a tenant projection.
- the software list shows only tenant-observed software.
- tasks and risk acceptances anchor on `TenantVulnerabilityId`.
- source aliases are globally unique by `(SourceSystem, ExternalSoftwareId)`.
- software versions remain on installation rows for now.
- global software-to-vulnerability correlation should be derived first and only persisted later if query cost requires it.
- the frontend remains tenant-operational, with global definition panels embedded in tenant views.
- obsolete code/files should be deleted as soon as the replacement path is live.

## Design rule

If data would still be true with zero tenants, it belongs in the global catalog.

If data answers "what does this tenant see, decide, or act on?", it belongs in tenant projections or tenant workflow.

## Entity classification

### Current-state classification table

| Entity | Current role | Target role | Notes |
| --- | --- | --- | --- |
| `Vulnerability` | Mixed canonical + tenant | Delete or reduce to staging-only transitional use during implementation, then remove | Still duplicates canonical fields and leaks into tenant reads. Should not survive final model. |
| `VulnerabilityAffectedSoftware` | Tenant-scoped via `Vulnerability` | Global catalog | NVD-derived. Move under `VulnerabilityDefinition`. |
| `VulnerabilityReference` | Tenant-scoped via `Vulnerability` | Global catalog | NVD-derived. Move under `VulnerabilityDefinition`. |
| `VulnerabilityAsset` | Tenant relationship | Tenant relationship | Anchor on `TenantVulnerabilityId`. |
| `VulnerabilityAssetEpisode` | Tenant relationship | Tenant relationship | Anchor on `TenantVulnerabilityId`. |
| `VulnerabilityAssetAssessment` | Tenant relationship | Tenant relationship | Anchor on `TenantVulnerabilityId`. |
| `NormalizedSoftware` | Tenant-scoped today | Global catalog | Should lose `TenantId`. |
| `NormalizedSoftwareAlias` | Tenant-scoped today | Global catalog | Should lose `TenantId`; unique on `(SourceSystem, ExternalSoftwareId)`. |
| `NormalizedSoftwareInstallation` | Tenant relationship/projection hybrid | Replace with `TenantSoftware` + tenant relationship rows | Current shape mixes tenant projection with install-derived state. |
| `NormalizedSoftwareVulnerabilityProjection` | Tenant-scoped derived projection | Tenant-scoped derived projection or delete if no longer needed | Keep only if query cost justifies persisted projection. |
| `SoftwareCpeBinding` | Tenant-scoped today | Global catalog | NVD/CPE-derived canonical mapping should be global. |
| `SoftwareVulnerabilityMatch` | Tenant-scoped correlation | Tenant-scoped correlation | Keep tenant-scoped, keyed canonically to global definitions. |
| `DeviceSoftwareInstallation` | Tenant relationship | Tenant relationship | Should ultimately anchor on `TenantSoftwareId`. |
| `DeviceSoftwareInstallationEpisode` | Tenant relationship | Tenant relationship | Should ultimately anchor on `TenantSoftwareId`. |
| `Asset` | Tenant relationship root | Tenant relationship root | Stays tenant-scoped. |
| `AssetSecurityProfile` | Tenant workflow/config | Tenant workflow/config | Stays tenant-scoped. |
| `OrganizationalSeverity` | Tenant workflow | Tenant workflow | Anchor on `TenantVulnerabilityId`. |
| `AIReport` | Tenant workflow | Tenant workflow | Anchor on `TenantVulnerabilityId`. |
| `Comment` | Tenant workflow | Tenant workflow | Vulnerability comments use `TenantVulnerability`. |
| `AuditLogEntry` | Tenant workflow | Tenant workflow | Vulnerability audit uses `TenantVulnerability`. |
| `RemediationTask` | Tenant workflow | Tenant workflow | Anchor on `TenantVulnerabilityId` plus `AssetId` where needed. |
| `RiskAcceptance` | Tenant workflow | Tenant workflow | Anchor on `TenantVulnerabilityId`. |
| `TenantSourceConfiguration` | Tenant config | Tenant config | Stays tenant-scoped. |
| `TenantSlaConfiguration` | Tenant config | Tenant config | Stays tenant-scoped. |
| `IngestionRun` | Tenant operational | Tenant operational | Stays tenant-scoped. |
| `StagedVulnerability` | Tenant staging | Tenant staging | Stays tenant-scoped staging. |
| `StagedVulnerabilityExposure` | Tenant staging | Tenant staging | Stays tenant-scoped staging. |
| `StagedAsset` | Tenant staging | Tenant staging | Stays tenant-scoped staging. |
| `StagedDeviceSoftwareInstallation` | Tenant staging | Tenant staging | Stays tenant-scoped staging. |
| `EnrichmentJob` | Tenant operational | Tenant operational | Stays tenant-scoped if enrichment is tenant-triggered. |
| `EnrichmentRun` | Tenant operational | Tenant operational | Stays tenant-scoped if enrichment is tenant-triggered. |
| `EnrichmentSourceConfiguration` | Tenant/admin config today | Re-evaluate | If source enablement is tenant-specific, split from any global source catalog. |
| `Tenant` | Tenant root | Tenant root | Stays as-is. |
| `User` | Global identity | Global identity | No change. |
| `UserTenantRole` | Tenant access | Tenant access | No change. |
| `Team` | Tenant workflow/config | Tenant workflow/config | No change. |
| `TeamMember` | Tenant workflow/config | Tenant workflow/config | No change. |
| `Notification` | Tenant workflow | Tenant workflow | No change. |

### Global catalog

- `VulnerabilityDefinition`
- `VulnerabilityDefinitionReference`
- `VulnerabilityDefinitionAffectedSoftware`
- `NormalizedSoftware`
- `NormalizedSoftwareAlias`
- canonical CPE entities and NVD-derived catalog data
- canonical software-to-CPE mapping

### Tenant projection

- `TenantVulnerability`
- `TenantSoftware`

### Tenant relationship and observation

- `VulnerabilityAsset`
- `VulnerabilityAssetEpisode`
- `VulnerabilityAssetAssessment`
- `DeviceSoftwareInstallation`
- `DeviceSoftwareInstallationEpisode`

### Tenant workflow

- `OrganizationalSeverity`
- `AIReport`
- `Comment`
- `AuditLogEntry`
- `RemediationTask`
- `RiskAcceptance`
- `TenantSourceConfiguration`
- `TenantSlaConfiguration`
- all tenant admin/role/team objects

## Target schema

### Vulnerabilities

#### Global

- `VulnerabilityDefinition`
  - `Id`
  - `ExternalId`
  - `Title`
  - `Description`
  - `VendorSeverity`
  - `CvssScore`
  - `CvssVector`
  - `PublishedDate`
  - `Source`

- `VulnerabilityDefinitionReference`
  - `VulnerabilityDefinitionId`
  - `Url`
  - `Source`
  - `Tags`

- `VulnerabilityDefinitionAffectedSoftware`
  - `VulnerabilityDefinitionId`
  - CPE / version-range fields

#### Tenant

- `TenantVulnerability`
  - `Id`
  - `TenantId`
  - `VulnerabilityDefinitionId`
  - `Status`
  - `FirstSeenAt`
  - `LastSeenAt`
  - `CreatedAt`
  - `UpdatedAt`

- `VulnerabilityAsset`
  - `TenantVulnerabilityId`
  - `AssetId`
  - `DetectedDate`
  - `ResolvedDate`
  - `Status`

- `VulnerabilityAssetEpisode`
  - `TenantVulnerabilityId`
  - `AssetId`
  - `EpisodeNumber`
  - `FirstSeenAt`
  - `LastSeenAt`
  - `ResolvedAt`
  - `Status`

- `VulnerabilityAssetAssessment`
  - `TenantVulnerabilityId`
  - `AssetId`
  - environmental severity fields

- `OrganizationalSeverity`
  - `TenantVulnerabilityId`

- `AIReport`
  - `TenantVulnerabilityId`

- `Comment`
  - `EntityType = TenantVulnerability`
  - `EntityId = TenantVulnerabilityId`

- `AuditLogEntry`
  - `EntityType = TenantVulnerability`
  - `EntityId = TenantVulnerabilityId`

### Software

#### Global

- `NormalizedSoftware`
  - `Id`
  - canonical display name
  - canonical vendor
  - normalization confidence/method
  - canonical CPE binding

- `NormalizedSoftwareAlias`
  - `Id`
  - `NormalizedSoftwareId`
  - `SourceSystem`
  - `ExternalSoftwareId`
  - raw source identity fields
  - unique index on `(SourceSystem, ExternalSoftwareId)`

- global CPE catalog entities
- global NVD-derived software correlation inputs

#### Tenant

- `TenantSoftware`
  - `Id`
  - `TenantId`
  - `NormalizedSoftwareId`
  - `FirstSeenAt`
  - `LastSeenAt`
  - `ActiveInstallationCount`
  - `DeviceCount`

- `DeviceSoftwareInstallation`
  - `TenantSoftwareId`
  - `DeviceAssetId`
  - `SoftwareAssetId` if the raw software asset record remains
  - observed version
  - `ObservedAt`

- `DeviceSoftwareInstallationEpisode`
  - `TenantSoftwareId`
  - `DeviceAssetId`
  - observed version
  - `FirstSeenAt`
  - `LastSeenAt`
  - `RemovedAt`

## Current leaks to remove

These are the main current-state design violations to eliminate during Phase 1 and Phase 2:

- `NormalizedSoftware` and `NormalizedSoftwareAlias` are still tenant-filtered in `PatchHoundDbContext`.
- `SoftwareCpeBinding` is still tenant-filtered even though CPE/NVD data is global.
- `VulnerabilityAffectedSoftware` and `VulnerabilityReference` still hang off tenant `Vulnerability`.
- many reads still join through `Vulnerability` even when the canonical source is now `VulnerabilityDefinition`.
- tenant relationship entities still use names like `VulnerabilityId` even where the runtime meaning is now `TenantVulnerabilityId`.
- several older controllers still expose mixed canonical + tenant DTOs rather than a clean split.

## Phase 1 concrete checklist

1. Add missing global catalog entities that do not yet exist cleanly:
   - `VulnerabilityDefinition`
   - `TenantVulnerability`
   - `TenantSoftware`
   - global CPE catalog entity if `SoftwareCpeBinding` is not sufficient

2. Remove `TenantId` and tenant query filters from global catalog entities:
   - `NormalizedSoftware`
   - `NormalizedSoftwareAlias`
   - `SoftwareCpeBinding`

3. Move NVD-derived data off tenant entities:
   - `VulnerabilityAffectedSoftware` -> definition-owned
   - `VulnerabilityReference` -> definition-owned

4. Rename tenant relationship keys so the model reads correctly:
   - `VulnerabilityId` -> `TenantVulnerabilityId` where the referenced anchor is tenant-scoped
   - `NormalizedSoftwareId` -> `TenantSoftwareId` where the referenced anchor is tenant-scoped

5. Delete mixed-model files and code once replacements exist:
   - old controllers that still expose tenant `Vulnerability` as the main read model
   - DTOs that carry both legacy and new identities
   - dead query helpers after frontend route migration

## Query filter rules

- Global catalog entities must not have tenant query filters.
- Tenant projection/relationship/workflow entities must have tenant query filters.
- Global catalog reads may be joined into tenant reads, but catalog entities must never depend on tenant-owned rows to exist.

## API shape

### Vulnerabilities

#### Global with tenant overlay

- `GET /api/vulnerability-definitions`
  - returns only definitions with a projection in the active tenant
  - each row includes:
    - global definition summary
    - tenant overlay
    - `tenantVulnerabilityId`

- `GET /api/vulnerability-definitions/{id}`
  - global definition only

#### Tenant detail/workflow

- `GET /api/tenant-vulnerabilities/{id}`
- `PUT /api/tenant-vulnerabilities/{id}/organizational-severity`
- `POST /api/tenant-vulnerabilities/{id}/ai-report`
- `GET /api/tenant-vulnerabilities/{id}/comments`
- `POST /api/tenant-vulnerabilities/{id}/comments`
- `GET /api/tenant-vulnerabilities/{id}/timeline`

### Software

#### Global with tenant overlay

- `GET /api/software-definitions`
  - returns only software with a `TenantSoftware` projection in the active tenant
  - each row includes:
    - global normalized software summary
    - tenant prevalence overlay
    - `tenantSoftwareId`

- `GET /api/software-definitions/{id}`
  - global normalized software only

#### Tenant detail/workflow

- `GET /api/tenant-software/{id}`
- `GET /api/tenant-software/{id}/installations`
- `GET /api/tenant-software/{id}/vulnerabilities`

## Frontend design

### Vulnerability UI

- list route uses global definition rows with tenant overlay
- detail route uses `tenantVulnerabilityId`
- description, references, affected software, CVSS, and source data come from the global definition
- comments, timeline, assets, org severity, AI report, and recurrence come from `TenantVulnerability`

### Software UI

- list route shows tenant-observed software only
- detail route should use `tenantSoftwareId`
- identity, vendor, aliases, and CPE come from global normalized software
- prevalence, versions, devices, and active tenant vulnerabilities come from `TenantSoftware` and installation rows

### Query keys

- global keys for definition/catalog reads
- tenant-scoped keys for overlays and tenant detail/workflow reads

## Other tenant-agnostic data candidates

These should stay global or be introduced globally if materialized later:

- NVD-derived CPE catalog entries
- global software-to-CPE mapping
- NVD-derived affected-software rules
- vulnerability references
- normalized software aliases
- canonical vendor/product dictionaries if introduced later

These should stay tenant-scoped:

- assets
- installations
- tenant vulnerability status
- comments
- audit
- severity overrides
- AI reports
- remediation tasks
- risk acceptances
- source enablement and schedules

## Implementation phases

### Phase 1: Classification and schema cleanup

- classify every current entity into catalog, tenant projection, tenant relationship, or tenant workflow
- remove `TenantId` from any entity that should be global
- add missing tenant anchor entities where needed, especially `TenantSoftware`
- delete obsolete entities/fields that duplicate canonical data in tenant rows

Delete in this phase:

- any compatibility-only DTO fields
- any code paths keeping both legacy and new IDs alive
- any query filters on global catalog entities

### Phase 2: Vulnerability model cleanup

- make `TenantVulnerability` the only tenant anchor for vulnerability operations
- re-key all exposure, episode, assessment, comment, audit, AI report, task, and risk acceptance paths to `TenantVulnerabilityId`
- remove all runtime dependence on the old tenant-specific `Vulnerability` entity for UI or workflow

Delete in this phase:

- legacy `VulnerabilitiesController` once all reads are switched
- any DTOs or frontend code that use legacy vulnerability IDs

### Phase 3: Software model cleanup

- introduce `TenantSoftware`
- re-key device installation and software detail paths to `TenantSoftwareId`
- keep `NormalizedSoftware` and aliases global
- keep canonical CPE mapping global

Delete in this phase:

- any tenant-local software identity fallbacks
- any duplicate software identity/CPE fields persisted on tenant rows

### Phase 4: Ingestion rewrite

- ingest global definitions first
- upsert tenant projections second
- write tenant relationship rows third
- ensure Defender/NVD ingestion writes only to the correct layer

Delete in this phase:

- any remaining mixed write path that updates canonical and tenant data in the same entity
- any stale staged-merge logic that still keys tenant relationships on global rows

### Phase 5: API rewrite

- publish only clean split endpoints
- remove old mixed endpoints
- remove compatibility response shapes

Delete in this phase:

- deprecated controllers
- deprecated DTOs
- old frontend server functions that target removed endpoints

### Phase 6: Frontend rewrite

- switch list/detail pages to the new API shape
- split query keys into global catalog and tenant projection concerns
- remove all compatibility branches

Delete in this phase:

- obsolete schemas
- old query keys
- dead route loaders and dead UI sections tied to removed endpoints

## Definition of done

- global catalog entities contain no tenant filters or tenant-owned workflow data
- tenant relationship/workflow entities reference tenant anchors, not global catalog rows
- frontend routes consume the split cleanly
- no compatibility DTOs or legacy route helpers remain
- dead files created by the old mixed model are removed as part of each phase, not later
