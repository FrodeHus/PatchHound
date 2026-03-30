# Normalized Software Detail Implementation Plan

**Date:** 2026-03-10
**Status:** Proposed
**Depends on:** `2026-03-10-normalized-software-detail-plan.md`

## Goal

Translate the normalized software detail design into an execution sequence that fits the current PatchHound codebase and can be implemented incrementally without destabilizing existing asset and vulnerability flows.

This plan is intentionally task-oriented.

## Current code anchors

Existing backend primitives already in place:

- `Asset` with `AssetType.Software`
- `DeviceSoftwareInstallation`
- `DeviceSoftwareInstallationEpisode`
- `SoftwareCpeBinding`
- `SoftwareVulnerabilityMatch`
- `VulnerabilityAffectedSoftware`

Existing frontend anchors already in place:

- software-specific sections in asset detail views
- software inventory in device detail
- software vulnerability evidence in software asset detail

Current limitation:

- all software analysis is still anchored to a software asset row, not a normalized software identity.

## Delivery strategy

Build this in four implementation slices:

1. normalized software identity and alias persistence
2. derived projections and query APIs
3. dedicated frontend route and cohort-first UI
4. navigation and integration from existing views

## Slice 1: Backend identity model

### 1.1 Add entities

Add new entities in `src/PatchHound.Core/Entities`:

- `NormalizedSoftware`
- `NormalizedSoftwareAlias`
- `NormalizedSoftwareInstallation`
- `NormalizedSoftwareVulnerabilityProjection`

Suggested first-pass shape:

#### `NormalizedSoftware`

- `Id`
- `TenantId`
- `CanonicalName`
- `CanonicalVendor`
- `CanonicalProductKey`
- `PrimaryCpe23Uri`
- `NormalizationMethod`
- `Confidence`
- `LastEvaluatedAt`
- `CreatedAt`
- `UpdatedAt`

#### `NormalizedSoftwareAlias`

- `Id`
- `TenantId`
- `NormalizedSoftwareId`
- `SourceSystem`
- `ExternalSoftwareId`
- `RawName`
- `RawVendor`
- `RawVersion`
- `AliasConfidence`
- `MatchReason`
- `CreatedAt`
- `UpdatedAt`

#### `NormalizedSoftwareInstallation`

- `Id`
- `TenantId`
- `NormalizedSoftwareId`
- `SoftwareAssetId`
- `DeviceAssetId`
- `SourceSystem`
- `DetectedVersion`
- `FirstSeenAt`
- `LastSeenAt`
- `RemovedAt`
- `IsActive`
- `CurrentEpisodeNumber`

#### `NormalizedSoftwareVulnerabilityProjection`

- `Id`
- `TenantId`
- `NormalizedSoftwareId`
- `VulnerabilityId`
- `BestMatchMethod`
- `BestConfidence`
- `AffectedInstallCount`
- `AffectedDeviceCount`
- `AffectedVersionCount`
- `FirstSeenAt`
- `LastSeenAt`
- `ResolvedAt`
- `EvidenceJson`

### 1.2 Add enums

Add new enums in `src/PatchHound.Core/Enums`:

- `SoftwareIdentitySourceSystem`
- `SoftwareNormalizationMethod`
- `SoftwareNormalizationConfidence`

Keep names explicit and reusable by API models later.

### 1.3 Add EF configurations

Add configurations in `src/PatchHound.Infrastructure/Data/Configurations`:

- `NormalizedSoftwareConfiguration`
- `NormalizedSoftwareAliasConfiguration`
- `NormalizedSoftwareInstallationConfiguration`
- `NormalizedSoftwareVulnerabilityProjectionConfiguration`

Required indices:

- `NormalizedSoftware`: `(TenantId, CanonicalProductKey)`
- `NormalizedSoftwareAlias`: unique `(TenantId, SourceSystem, ExternalSoftwareId)`
- `NormalizedSoftwareInstallation`: `(TenantId, NormalizedSoftwareId, DetectedVersion, LastSeenAt)`
- `NormalizedSoftwareVulnerabilityProjection`: `(TenantId, NormalizedSoftwareId, VulnerabilityId)`

### 1.4 Register DbSets and tenant query filters

Update [PatchHoundDbContext.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs):

- add `DbSet<>` properties
- add tenant query filters for all four new entities

### 1.5 Create migration

Add EF migration once the entities are stable.

Migration acceptance criteria:

- additive only
- no existing tables or relationships broken
- safe to deploy before the UI uses the new model

## Slice 2: Normalization and projection services

### 2.1 Add normalization service

Create a new service in `src/PatchHound.Infrastructure/Services`:

- `NormalizedSoftwareResolver`

Responsibilities:

- resolve an existing normalized software from source identity or CPE binding
- create a new normalized software when no strong match exists
- update or create alias rows
- compute confidence and method

Input sources in phase 1:

- software `Asset`
- `SoftwareCpeBinding`
- software metadata keys already carried in `Asset.Metadata`
- Defender `ExternalId`

Recommended API:

```csharp
Task<NormalizedSoftwareResolutionResult> ResolveSoftwareAssetAsync(
    Guid tenantId,
    Asset softwareAsset,
    SoftwareCpeBinding? cpeBinding,
    CancellationToken ct
)
```

### 2.2 Add installation projection service

Create:

- `NormalizedSoftwareProjectionService`

Responsibilities:

- materialize `NormalizedSoftwareInstallation`
- keep installation rows aligned with current `DeviceSoftwareInstallation` and `DeviceSoftwareInstallationEpisode`
- set:
  - version
  - active/removed state
  - first/last seen
  - source system

Initial projection source:

- existing Defender-backed software assets and installation episodes

### 2.3 Add vulnerability projection service

Extend the same service or add:

- `NormalizedSoftwareVulnerabilityProjectionService`

Responsibilities:

- aggregate `SoftwareVulnerabilityMatch` into normalized-software-level vulnerability summaries
- update affected install/device/version counts
- track first/last seen and resolution state

### 2.4 Trigger points

Phase 1 trigger points should be pragmatic:

- after ingestion merge updates software assets/installations
- after software CPE binding changes
- after software-vulnerability matches are recalculated

Do not block page delivery on a fully generic eventing pipeline.

The first implementation can be a direct recompute call from:

- software ingestion merge completion
- `assignSoftwareCpeBinding`
- software vulnerability match recalculation path

### 2.5 Backfill command

Add a one-time or repeatable backfill path.

Options:

- admin-only API endpoint
- worker command
- test-only hosted service hook

Recommendation:

- add an internal service method first,
- wire a temporary admin endpoint only if needed for rollout.

## Slice 3: API layer

### 3.1 Add API models

Add models in `src/PatchHound.Api/Models/Software`:

- `NormalizedSoftwareDetailDto`
- `NormalizedSoftwareVersionCohortDto`
- `NormalizedSoftwareInstallationDto`
- `NormalizedSoftwareVulnerabilityDto`
- `NormalizedSoftwareSourceAliasDto`

### 3.2 Add controller

Create:

- `SoftwareController`

Suggested endpoints:

- `GET /api/software/{id}`
- `GET /api/software/{id}/installations`
- `GET /api/software/{id}/vulnerabilities`

All endpoints must:

- honor tenant query filters
- return `Forbid()` if the user lacks access to the target tenant

### 3.3 Detail query behavior

`GET /api/software/{id}` should return:

- identity
- prevalence summary
- version cohorts
- source coverage summary
- top assignment groups / OS / criticality distributions
- light timeline summary

### 3.4 Cohort installation paging

`GET /api/software/{id}/installations` must require a version filter.

Recommended query params:

- `version`
- `page`
- `pageSize`
- later:
  - `vulnerableOnly`
  - `activeOnly`
  - `criticality`
  - `ownerType`

### 3.5 Frontend API wrappers

Add:

- `frontend/src/api/software.schemas.ts`
- `frontend/src/api/software.functions.ts`

Server functions:

- `fetchNormalizedSoftwareDetail`
- `fetchNormalizedSoftwareInstallations`
- `fetchNormalizedSoftwareVulnerabilities`

## Slice 4: Frontend route and components

### 4.1 Add route

Create:

- `frontend/src/routes/_authed/software/$id.tsx`

Route responsibilities:

- load detail summary
- manage selected version cohort
- manage installation pagination
- coordinate vulnerability filtering if added

### 4.2 Add feature module

Create:

- `frontend/src/components/features/software/`

Suggested components:

- `SoftwareDetailPage.tsx`
- `SoftwareHeaderBand.tsx`
- `VersionPressureRail.tsx`
- `SoftwareCohortPanel.tsx`
- `SoftwareInstallationsTable.tsx`
- `SoftwareVulnerabilityPanel.tsx`
- `SoftwareIdentityRail.tsx`
- `SoftwareSourceCoveragePanel.tsx`

### 4.3 State model

Keep state local to the route initially:

- selected version cohort
- cohort page
- cohort page size
- optional vulnerable-only filter

Behavior:

- changing selected version resets cohort page to `1`
- default page size `25`
- default selected cohort:
  - highest active install count
  - tie-break on highest vulnerability count

### 4.4 Reuse existing patterns

Reuse:

- top-level page shell and card language from asset detail pages
- link styling and vulnerability row styling from current vulnerability/asset features
- CPE binding summary/editor concepts from the software asset detail

Do not reuse:

- asset sheet structure
- device-centric software inventory table as the primary page content

## Slice 5: Navigation integration

### 5.1 Software asset detail links

Update software sections in:

- [AssetDetailPageView.tsx](/Users/frode.hus/src/github.com/frodehus/PatchHound/frontend/src/components/features/assets/AssetDetailPageView.tsx)
- [AssetDetailPane.tsx](/Users/frode.hus/src/github.com/frodehus/PatchHound/frontend/src/components/features/assets/AssetDetailPane.tsx)

Add:

- `Open normalized software workspace`

Only show once a normalized software ID is available.

### 5.2 Device detail software inventory links

From device software inventory rows:

- link each installed software item to the normalized software detail page

### 5.3 Vulnerability detail matched software links

From vulnerability detail matched software:

- link affected software to normalized software detail when available

### 5.4 Asset list integration later

Do not block the first delivery on adding a full software search/list page.

## Task breakdown

### Backend

1. Add new normalized software entities and enums.
2. Add EF configurations, DbSets, and tenant filters.
3. Create migration.
4. Build `NormalizedSoftwareResolver`.
5. Build installation projection updater.
6. Build normalized software vulnerability projection updater.
7. Hook projection recompute into ingestion/CPE-binding/software-match paths.
8. Add software controller and DTOs.
9. Add tests for:
   - normalization grouping
   - alias reuse
   - cohort aggregation
   - paged installation queries
   - vulnerability aggregation

### Frontend

1. Add software API schemas and server functions.
2. Add `_authed/software/$id` route.
3. Build page shell and header band.
4. Build Version Pressure Rail.
5. Build selected-cohort paged installations table.
6. Build software vulnerability panel.
7. Build identity/source-evidence right rail.
8. Add links from software asset and vulnerability/device views.
9. Add frontend tests for:
   - cohort selection resets pagination
   - empty states
   - link rendering from existing views

## Recommended first PR sequence

### PR 1

- entities
- enums
- EF config
- migration

No UI.

### PR 2

- normalization resolver
- installation projection
- vulnerability projection
- backend tests

No UI yet.

### PR 3

- software controller
- frontend API schemas/functions
- route scaffolding with placeholder page

### PR 4

- full software detail UI
- version cohorts
- paged installations
- vulnerability panel

### PR 5

- links from asset/device/vulnerability detail surfaces

## Risks and constraints

### 1. Asset metadata quality

Current software asset metadata may not consistently expose vendor/version.

Mitigation:

- use CPE bindings when present
- keep heuristics conservative
- persist alias confidence

### 2. Projection churn

Projection recompute could become expensive if run on every small change.

Mitigation:

- recompute only affected normalized software IDs
- batch updates inside the same tenant where possible

### 3. Route discoverability

The page may exist before users can naturally navigate to it.

Mitigation:

- link from software asset detail early in rollout

## Success criteria

The first release is complete when:

- a normalized software ID exists and is stable,
- one tenant-scoped route can load a normalized software page,
- versions are shown as cohorts,
- one selected cohort has paged installation rows,
- known vulnerabilities aggregate at normalized-software level,
- existing software asset views can link into the new page.
