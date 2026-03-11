# Ingestion Scalability And Safety Plan

Date: 2026-03-08

## Goal

Make ingestion handle thousands of incoming vulnerability, asset, and software records efficiently and safely without:

- long-lived EF tracked graphs,
- repeated enrichment for the same vulnerability,
- large all-or-nothing transactions,
- concurrency failures during worker sync,
- duplicate or inconsistent remediation projections.

This plan is for the current PatchHound architecture:

- `DefenderVulnerabilitySource` fetches machine inventory and machine vulnerabilities,
- `NvdVulnerabilityEnricher` enriches CVEs,
- `IngestionService` currently merges directly into normalized tables,
- `IngestionWorker` polls and invokes ingestion per tenant/source.

## Current Issues

### 1. Row-by-row EF merge

`IngestionService` currently:

- loads existing entities one by one,
- mutates aggregates inside one DbContext,
- updates current-state and history in the same pass,
- creates/removes related child rows in the same transaction.

This is manageable for small syncs, but not for large Defender datasets.

### 2. Repeated logical work

Even after current dedupe improvements:

- the same vulnerability may still be touched many times during merge,
- the same asset and link rows are resolved repeatedly,
- current-state tables and history tables are both updated inside the same entity loop.

### 3. Large tracked graphs

The DbContext can accumulate:

- vulnerabilities,
- assets,
- vulnerability links,
- episode rows,
- software installation rows,
- remediation tasks,
- assessments.

That increases:

- memory pressure,
- change detection cost,
- stale entity risk,
- concurrency failures.

### 4. Long transactions

`ProcessResultsAsync` and `ProcessAssetsAsync` do too much per run:

- create/update normalized definitions,
- create/update links,
- reconcile missing rows,
- update episodes,
- close tasks,
- open tasks,
- write assessments.

This creates a wider failure surface than necessary.

### 5. Concurrency risk

The worker can still hit `DbUpdateConcurrencyException` because multiple updates touch the same logical data during one run or across overlapping admin/runtime updates.

Retry helps, but it is not the fix.

## Design Principles

### 1. Normalize once, merge in batches

Fetch and normalize external data first. Only after that should the system touch the database.

### 2. Separate current state from history

Do not mix:

- “what is true now”
- “what changed over time”

in the same per-row mutation loop.

### 3. Small commits, no giant unit of work

Each ingestion phase should commit independently.

### 4. Idempotent merge steps

If a batch or whole run retries, it must not duplicate rows or create duplicate episodes/tasks.

### 5. One active ingestion run per tenant/source

Manual sync must not race a scheduled sync for the same `(tenant, source)`.

## Target Architecture

### Phase A: Fetch

Source clients fetch all pages from Defender.

Outputs:

- raw machine vulnerability records,
- raw machine inventory records,
- raw machine software inventory records.

### Phase B: Normalize

Convert raw source rows into flat normalized DTOs:

- `NormalizedVulnerabilityDefinition`
- `NormalizedAssetDefinition`
- `NormalizedVulnerabilityExposure`
- `NormalizedDeviceSoftwareInstallation`

At this stage:

- one vulnerability definition per distinct `(tenantId, externalId)`,
- one asset definition per distinct `(tenantId, externalAssetId)`,
- one exposure per distinct `(vulnerabilityExternalId, assetExternalId)`,
- one device-software install per distinct `(deviceExternalId, softwareExternalId)`.

### Phase C: Enrich

Enrich only distinct vulnerabilities that support enrichment.

For NVD:

- dedupe by CVE before making requests,
- keep one in-memory cache per ingestion run,
- enrich in chunks,
- obey NVD request limits.

### Phase D: Stage

Write the normalized snapshot into staging tables before merging into normalized domain tables.

Recommended new tables:

- `IngestionRuns`
- `StagedVulnerabilities`
- `StagedAssets`
- `StagedVulnerabilityExposures`
- `StagedDeviceSoftwareInstallations`

Each row should include:

- `IngestionRunId`
- tenant/source identity
- source keys / external IDs
- normalized fields
- timestamps

### Phase E: Merge Current State

Merge staging rows into current-state tables in chunks:

- `Vulnerability`
- `Asset`
- `VulnerabilityAsset`
- `DeviceSoftwareInstallation`

This should use set-based operations where possible.

### Phase F: Reconcile History

After current state has been updated for the run:

- open missing vulnerability episodes,
- close missing vulnerability episodes after debounce rules,
- open missing software install episodes,
- close missing software install episodes after debounce rules.

This step should operate from the full staged snapshot for the run, not from per-item mutation flow.

### Phase G: Project Operational State

After merge and reconciliation:

- create missing remediation tasks,
- close resolved remediation tasks,
- update assessments,
- update source/enrichment runtime status.

This becomes a projection step, not part of the main merge logic.

## New Data Model

### 1. IngestionRun

Add `IngestionRun`:

- `Id`
- `TenantId`
- `SourceKey`
- `StartedAt`
- `CompletedAt`
- `Status`
- `FetchedRowCount`
- `NormalizedVulnerabilityCount`
- `NormalizedAssetCount`
- `NormalizedExposureCount`
- `NormalizedSoftwareInstallCount`
- `Error`

Purpose:

- run visibility,
- audit/debugging,
- idempotent staging root,
- partial failure diagnostics.

### 2. Staging tables

Add:

- `StagedVulnerability`
- `StagedAsset`
- `StagedVulnerabilityExposure`
- `StagedDeviceSoftwareInstallation`

All keyed by `IngestionRunId` plus a natural uniqueness key.

### 3. Ingestion lease / lock

Add `TenantSourceIngestionLease` or embed locking fields into `TenantSourceConfiguration`:

- `ActiveRunId`
- `LeaseAcquiredAt`
- `LeaseExpiresAt`

Only one worker execution may hold the lease for a `(tenant, source)`.

## Batch Strategy

### Recommended initial chunk sizes

- vulnerability enrichment batch: `50`
- vulnerability definition merge batch: `250`
- asset definition merge batch: `500`
- exposure merge batch: `1000`
- device-software merge batch: `1000`

These should be configurable later.

### EF rules

For any EF-based batch:

1. read a chunk
2. merge a chunk
3. `SaveChangesAsync`
4. `ChangeTracker.Clear()`

Never keep the whole run tracked.

## NVD Rate Limiting

Official NVD guidance:

- with API key: `50 requests in a rolling 30 second window`
- without API key: `5 requests in a rolling 30 second window`
- they also recommend sleeping about `6 seconds` between automated requests

Implementation recommendation:

### Minimum acceptable

- per-ingestion-run dedupe by CVE
- single outbound connection for NVD
- retry on `429` honoring `Retry-After`

### Better

Add a dedicated NVD limiter service:

- if API key exists: allow `50 / 30s`
- else allow `5 / 30s`
- serialize requests through the limiter

### Best

If both API and worker can call NVD:

- centralize NVD usage through the worker only, or
- use a distributed limiter if multiple processes may call NVD concurrently.

## Merge Strategy By Table

### Vulnerability

Input key:

- `(TenantId, ExternalId)`

Batch merge should:

- load existing IDs for the batch in one query,
- insert missing vulnerabilities in bulk,
- update changed fields in bulk or limited entity batches,
- replace references and affected-software rows in a controlled child-merge step.

### Asset

Input key:

- `(TenantId, ExternalId)`

Batch merge should:

- load existing asset IDs once per chunk,
- insert missing rows,
- update changed names/device fields only when values differ.

### VulnerabilityAsset

Input key:

- `(VulnerabilityId, AssetId)`

Batch merge should:

- create current projection rows for new exposures,
- reopen resolved rows if needed,
- not create history here.

### DeviceSoftwareInstallation

Input key:

- `(TenantId, DeviceAssetId, SoftwareAssetId)`

Batch merge should:

- upsert current installs from staged rows,
- missing-row reconciliation happens later.

### Episodes

Episodes should be reconciled from:

- current open rows,
- staged snapshot rows for the current run,
- debounce counters.

This should be a separate reconciliation service, not mixed into per-row upsert logic.

## Required Service Split

Current `IngestionService` should be decomposed into services like:

- `IngestionRunService`
- `IngestionNormalizationService`
- `VulnerabilityEnrichmentService`
- `AssetMergeService`
- `VulnerabilityMergeService`
- `ExposureMergeService`
- `SoftwareInstallationMergeService`
- `ExposureHistoryReconciliationService`
- `SoftwareHistoryReconciliationService`
- `RemediationProjectionService`

`IngestionService` should become an orchestrator, not the place where all merge logic lives.

## Rollout Plan

### Step 1: Add run tracking and source lease

Implement:

- `IngestionRun`
- source lease / one-active-run protection

Outcome:

- no overlapping worker runs for the same tenant/source,
- visibility into failed runs.

### Step 2: Normalize and dedupe before persistence

Implement:

- batch DTO normalization layer,
- full in-memory dedupe before EF merge,
- NVD dedupe stays in place.

Outcome:

- repeated rows from Defender stop driving repeated aggregate mutation.

### Step 3: Chunk current merge

Refactor current merge into chunked passes for:

- assets,
- vulnerabilities,
- exposures,
- device-software installs.

Outcome:

- smaller transactions,
- fewer tracked entities,
- lower memory use.

### Step 4: Move history reconciliation out of upsert loop

Implement dedicated reconciliation passes for:

- `VulnerabilityAssetEpisode`
- `DeviceSoftwareInstallationEpisode`

Outcome:

- simpler merge semantics,
- safer debounce handling,
- fewer mutation conflicts.

### Step 5: Add staging tables

Persist normalized snapshots to staging before merge.

Outcome:

- replay/debug value,
- deterministic merge source,
- safer retries.

### Step 6: Convert merge paths to set-based operations

Move hot paths away from row-by-row EF where possible.

PostgreSQL-first options:

- `ExecuteUpdateAsync`
- `ExecuteDeleteAsync`
- raw SQL for merge-heavy paths if needed

Outcome:

- higher throughput,
- fewer EF change tracker issues.

## Tests To Add

### Unit/integration

- duplicate Defender rows for the same CVE only create one normalized vulnerability merge operation
- duplicate Defender rows for the same device only create one asset upsert
- chunked merge handles `10k+` staged exposure rows without tracking the full set
- source lease prevents overlapping runs
- rerunning the same `IngestionRun` input is idempotent
- remediation projection does not duplicate tasks
- episode reconciliation still honors the 2-sync debounce

### Concurrency

- admin runtime/source config update during worker ingestion does not fail the run
- repeated worker retries on the same run do not duplicate data
- staged merge retry after partial failure is safe

## Immediate Next Slice

Highest-value next implementation slice:

1. add `IngestionRun`
2. enforce one active run per `(tenant, source)`
3. add normalization + dedupe layer before `ProcessAssetsAsync` / `ProcessResultsAsync`
4. chunk merge batches and clear change tracker between batches

This is the shortest path from the current system to materially lower conflict risk.
