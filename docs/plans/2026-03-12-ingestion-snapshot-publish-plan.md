# Ingestion Snapshot Publish Plan

## Goal

Preserve the currently visible tenant state while a long ingestion merge is building the next state.

Today, the merge updates current rows in place. That means users can see:

- software vulnerability links temporarily disappear
- partially updated vulnerability state
- long-running `Merging` phases expose an inconsistent intermediate view

The target design is:

1. stage source data
2. build the next derived state in isolation
3. publish the new state atomically

Until publish completes, the UI continues to read the previous snapshot.

## Scope

This plan applies to tenant-visible derived ingestion state, not immutable source/catalog data.

Good first-snapshot candidates:

- `TenantSoftware`
- `NormalizedSoftwareInstallations`
- `SoftwareVulnerabilityMatch`
- `NormalizedSoftwareVulnerabilityProjection`
- `VulnerabilityAsset`
- `TenantVulnerability` current status if needed for consistency

Historical entities such as closed episodes should remain durable history, not be deleted/rebuilt as part of publishing unless necessary.

## Core Design

### 1. Snapshot identity

Introduce a snapshot concept for the tenant/source pair:

- `IngestionSnapshot`
  - `Id`
  - `TenantId`
  - `SourceKey`
  - `CreatedAt`
  - `Status`
    - `Building`
    - `Published`
    - `Discarded`
  - `IngestionRunId`

On the source configuration:

- `TenantSourceConfiguration.ActiveSnapshotId`
- `TenantSourceConfiguration.BuildingSnapshotId`

### 2. Build next state in isolation

During merge, write derived rows using `SnapshotId = BuildingSnapshotId`.

Current reads continue to use `ActiveSnapshotId`.

This prevents the current UI from observing the partially merged next state.

### 3. Atomic publish

When all build steps succeed:

- validate the building snapshot is complete
- publish in one transaction:
  - set `ActiveSnapshotId = BuildingSnapshotId`
  - clear `BuildingSnapshotId`
  - mark old snapshot superseded
  - mark new snapshot `Published`

### 4. Cleanup

Old snapshots are not deleted inline during publish.

Instead:

- retain the old snapshot briefly
- delete stale snapshots asynchronously after a retention window

That keeps publish small and safe.

## Table Strategy

### Phase 1: snapshot only the most volatile user-visible tables

Add `SnapshotId` to:

- `TenantSoftware`
- `NormalizedSoftwareInstallation`
- `SoftwareVulnerabilityMatch`
- `NormalizedSoftwareVulnerabilityProjection`

Reason:

- this is where users are currently seeing links disappear during merge
- these tables are derived and can be rebuilt safely

### Phase 2: snapshot current vulnerability exposure state

Add `SnapshotId` to:

- `VulnerabilityAsset`
- optionally `TenantVulnerability` current status

Keep episode history durable:

- `VulnerabilityAssetEpisode` should remain historical truth
- current open/active projection rows should become snapshot-switched

### Phase 3: evaluate whether `VulnerabilityAssetAssessment` should be snapshot-scoped

If assessments are tightly coupled to the current visible exposure set, they should move with the active snapshot as well.

## Read Model Changes

All tenant-visible queries must stop reading “current rows” implicitly and instead read:

- rows where `SnapshotId == ActiveSnapshotId`

Targets:

- dashboard
- asset detail related vulnerability/software blocks
- vulnerability detail correlated software
- software detail and software AI report payloads
- risk-change views where current exposure is involved

This is the most important correctness requirement after schema changes.

## Merge Flow

### Current flow

1. stage source data
2. merge into current derived rows
3. users see partial progress

### Target flow

1. stage source data
2. create or reuse `BuildingSnapshotId`
3. build snapshot-scoped derived rows
4. publish atomically
5. clean up superseded snapshots later

### Failure handling

If build fails:

- leave `ActiveSnapshotId` unchanged
- mark the building snapshot failed/discarded
- keep the current visible state intact

If ingestion is aborted:

- discard the building snapshot
- do not alter the active snapshot

This makes failure behavior much safer than the current in-place merge.

## Historical Episodes And Recurrence

The requirement to infer “now clean” from absence still stands.

Recommendation:

- keep historical `VulnerabilityAssetEpisode` and `DeviceSoftwareInstallationEpisode` rows durable
- build current active projections snapshot-scoped

That gives:

- stable recurrence history
- atomic switch for “currently open now” state

Users see the new current state only after publish, while history remains intact.

## Operational Status Model

Add explicit lifecycle states:

- `Staging`
- `BuildingSnapshot`
- `PublishPending`
- `Publishing`
- `Succeeded`
- `FailedRecoverable`
- `FailedTerminal`

This is clearer than using `Merging` for all post-stage work.

## UI Changes

Source status UI should show:

- current active snapshot age
- building snapshot in progress
- publish pending / publishing

Run history should show:

- snapshot build state
- whether a failed run touched only a building snapshot or the active snapshot

Important operator guarantee:

- “Current tenant view remains on the previous successful snapshot until publish completes.”

## Rollout Plan

### PR 1

Add snapshot entities and source configuration pointers:

- `IngestionSnapshot`
- `ActiveSnapshotId`
- `BuildingSnapshotId`

No behavior changes yet.

### PR 2

Snapshot software-derived state first:

- `TenantSoftware`
- `NormalizedSoftwareInstallation`
- `SoftwareVulnerabilityMatch`
- `NormalizedSoftwareVulnerabilityProjection`

Switch software reads to `ActiveSnapshotId`.

This should solve the most visible “links disappear during merge” problem first.

### PR 3

Snapshot current vulnerability exposure projections:

- `VulnerabilityAsset`
- optionally `TenantVulnerability`
- maybe `VulnerabilityAssetAssessment`

Keep episodes historical.

### PR 4

Add atomic publish transaction and delayed cleanup of superseded snapshots.

### PR 5

Update UI/runtime status language from `Merging` to `Building snapshot` / `Publishing`.

## Risks

### 1. Query complexity

Every read path must consistently filter on `ActiveSnapshotId`.

If one path forgets, the app will mix current and building state.

### 2. Storage growth

Keeping old snapshots temporarily increases row count.

Mitigation:

- cleanup job
- short retention for superseded snapshots

### 3. Partial rollout complexity

If only some derived tables are snapshot-scoped, reads must not combine snapshot-scoped and non-snapshot-scoped state incorrectly.

Mitigation:

- snapshot software state first as a coherent slice
- then move vulnerability projections as the next coherent slice

## Recommendation

Implement snapshot publish in phases, starting with software-derived state.

That gives the biggest user-visible correctness win fastest:

- software vulnerability links no longer disappear mid-merge
- current state remains stable while long ingestion runs build the next state
