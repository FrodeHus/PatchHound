# Batched Resumable Ingestion Plan

## Goal

Change ingestion from a single long-running fetch-and-merge operation into a resumable batch pipeline that:

- stages source data in committed batches
- survives worker/API restart without losing already fetched work
- resumes the same `IngestionRun`
- merges into canonical tables only after full staging completes

This plan assumes the current database is not greenfield. The change must be additive and migration-safe.

## Current State

Today, ingestion in [IngestionService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Services/IngestionService.cs):

- acquires an `IngestionRun`
- fetches a full asset snapshot from `IAssetInventorySource`
- stages the full asset snapshot
- merges staged assets immediately
- fetches a full vulnerability snapshot from `IVulnerabilitySource`
- stages the full vulnerability snapshot
- merges staged vulnerabilities immediately
- runs matching, projections, and enrichment enqueue
- marks the run succeeded or failed

This has two weaknesses:

1. Long-running source fetch is not durable
- if the worker restarts during `FetchAssetsAsync()` or `FetchVulnerabilitiesAsync()`, already fetched source data is lost

2. Staging is not resumable
- staged rows are tied to the run, but there is no durable batch cursor/checkpoint
- a failed run starts over from scratch

## Target Model

Split ingestion into two explicit phases:

1. `Fetch + stage in committed batches`
2. `Merge staged snapshot into canonical tables`

The durable restart boundary is between committed staging batches, not inside a provider fetch loop.

## Core Flow

### 1. Start or resume `IngestionRun`

Use one `IngestionRun` until the run completes or fails terminally.

Possible run statuses:

- `Running`
- `Staged`
- `Completed`
- `FailedRecoverable`
- `FailedTerminal`

Recommended rule:

- if the latest run for `(tenantId, sourceKey)` is `Running` or `FailedRecoverable`, resume it
- if the latest run is `Staged`, retry merge only
- create a new run only when there is no resumable run

### 2. Load or initialize checkpoint

Add a durable checkpoint per run and phase:

- `AssetInventory`
- `Vulnerabilities`
- future source-specific phases if needed

Each checkpoint stores:

- `IngestionRunId`
- `Phase`
- `BatchNumber`
- `CursorJson`
- `LastCommittedAt`
- `RecordsCommitted`
- `Status`

### 3. Fetch next batch

Provider contract becomes batch-oriented:

```csharp
Task<SourceBatchResult<TItem>> FetchBatchAsync(
    Guid tenantId,
    string? cursorJson,
    int batchSize,
    CancellationToken ct
)
```

Response:

- `Items`
- `NextCursorJson`
- `IsComplete`
- optional provider diagnostics

Cursor remains provider-specific and opaque to the core ingestion service.

### 4. Stage batch and checkpoint atomically

Each batch must commit in one DB transaction:

- upsert staged rows for the batch
- stamp rows with `IngestionRunId`
- stamp rows with `BatchNumber`
- update checkpoint cursor and counters
- append an optional `IngestionBatch` audit row

If the process crashes after commit:

- batch data is durable
- checkpoint is durable
- resume starts at the next batch

If the process crashes before commit:

- nothing from that batch is visible
- resume re-fetches that same batch safely

### 5. Continue until source is fully staged

When provider returns `IsComplete = true`:

- mark the phase complete
- if all phases are complete, set `IngestionRun.Status = "Staged"`

### 6. Merge staged snapshot

Only after full staging completes:

- run `StagedAssetMergeService`
- run `StagedVulnerabilityMergeService`
- run dependent projection/matching/enrichment steps

Merge reads only staged rows for the current `IngestionRunId`.

If merge fails:

- keep run in `Staged` or `FailedRecoverable`
- retry merge later without re-fetching source data

## Data Model Changes

### New entity: `IngestionCheckpoint`

Suggested shape:

```csharp
public class IngestionCheckpoint
{
    public Guid Id { get; private set; }
    public Guid IngestionRunId { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; }
    public string Phase { get; private set; }
    public int BatchNumber { get; private set; }
    public string CursorJson { get; private set; }
    public int RecordsCommitted { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset LastCommittedAt { get; private set; }
}
```

Recommended uniqueness:

- `(IngestionRunId, Phase)`

### Optional new entity: `IngestionBatch`

Useful for admin visibility and debugging.

Suggested shape:

- `IngestionRunId`
- `Phase`
- `BatchNumber`
- `RecordsCommitted`
- `CursorAfterJson`
- `CommittedAt`
- `DurationMs`
- `Status`
- `Error`

Recommended uniqueness:

- `(IngestionRunId, Phase, BatchNumber)`

### Extend staged tables

Add to:

- [StagedAsset.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Core/Entities/StagedAsset.cs)
- [StagedDeviceSoftwareInstallation.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Core/Entities/StagedDeviceSoftwareInstallation.cs)
- [StagedVulnerability.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Core/Entities/StagedVulnerability.cs)
- [StagedVulnerabilityExposure.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Core/Entities/StagedVulnerabilityExposure.cs)

New columns:

- `BatchNumber`

Existing `IngestionRunId` should remain the main snapshot boundary.

Recommended uniqueness:

- source-natural key + `IngestionRunId`

That makes resumed batch staging idempotent for the same run.

## Provider Interface Changes

### Defender asset/software ingestion

Current [DefenderVulnerabilitySource.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/VulnerabilitySources/DefenderVulnerabilitySource.cs) currently returns full snapshots.

Move to batch fetch contracts:

- assets/machines by page
- software by page
- machine references by page or by chunked software set
- vulnerabilities by page

Recommended intermediate contract split:

- `IAssetInventorySourceBatchProvider`
- `IVulnerabilitySourceBatchProvider`

This avoids breaking non-batch sources immediately while still letting ingestion move to the new model.

### Batch result shape

Suggested model:

```csharp
public record SourceBatchResult<TItem>(
    IReadOnlyList<TItem> Items,
    string? NextCursorJson,
    bool IsComplete
);
```

## Ingestion Service Changes

### `IngestionService`

Refactor [IngestionService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Services/IngestionService.cs) into:

1. run acquisition / resume resolution
2. phase checkpoint loading
3. batch fetch loop
4. batch stage commit
5. full-run merge
6. completion/failure transitions

### New methods

Recommended seam methods:

- `ResolveOrCreateIngestionRunAsync`
- `LoadCheckpointAsync`
- `StageAssetBatchAsync`
- `StageVulnerabilityBatchAsync`
- `CommitCheckpointAsync`
- `ExecuteMergeForRunAsync`
- `ResumeIncompleteRunsAsync`

### Retry behavior

Keep retries at the batch level, not the entire run:

- transient fetch error retries current batch
- transient DB conflict retries current batch commit
- do not clear already committed staged data for the run

## Worker Changes

### `IngestionWorker`

Current [IngestionWorker.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Worker/IngestionWorker.cs) only starts due runs.

Add resumable-run processing:

1. load recoverable runs first
2. resume those before starting fresh scheduled/manual runs

Priority order:

- `Staged` runs needing merge retry
- `Running` / `FailedRecoverable` runs needing checkpoint resume
- new due runs

### Lease model

Keep one lease per `(tenantId, sourceKey)`.

Do not allow:

- one worker resuming a run while another starts a fresh run for the same source

## Merge Semantics

Merge remains snapshot-based:

- asset merge consumes staged asset rows for `IngestionRunId`
- vulnerability merge consumes staged vulnerability rows for `IngestionRunId`

Important rule:

- merge must not look at other runs’ staged rows

That makes rerunning merge safe and deterministic.

## Recovery Semantics

### Recoverable failure

Examples:

- provider timeout
- server restart
- transient DB error
- worker crash after batch 8 of 20

Behavior:

- keep run
- keep staged rows
- keep checkpoint
- resume same run

### Terminal failure

Examples:

- provider credentials invalid
- source schema irreparably changed
- deserialization bug that requires code change

Behavior:

- mark run `FailedTerminal`
- require operator action or new run after remediation

## Admin / Audit UX

Expose on source admin/audit pages:

- run status
- current phase
- batch number
- records staged
- last checkpoint time
- whether the run is resumable
- whether merge is pending

This should appear in:

- source list/detail
- ingestion history

Useful operator labels:

- `Running batch 6`
- `Resume pending`
- `Staged, merge pending`
- `Recoverable failure`

## Rollout Plan

### Phase 1: Schema and metadata

- add `IngestionCheckpoint`
- optionally add `IngestionBatch`
- add `BatchNumber` to staged tables
- additive migration only

### Phase 2: Batch provider seams

- introduce batch fetch contracts
- adapt Defender provider first
- keep existing snapshot methods temporarily if needed

### Phase 3: Batched staging

- refactor `IngestionService` to stage committed batches
- persist checkpoints
- stop clearing staged data on each retry

### Phase 4: Resume and recovery

- worker resumes incomplete runs
- merge retries from `Staged`

### Phase 5: Admin visibility

- add checkpoint/run state to source admin and audit surfaces

### Phase 6: Cleanup

- remove old full-snapshot ingestion path once batch providers are live
- delete dead retry/clear logic that assumes one-shot staging

## Testing Strategy

### Unit / service tests

Add tests for:

- checkpoint creation
- checkpoint resume
- idempotent batch restage for same run
- merge after full staging only
- staged run merge retry after failure

### Provider tests

Defender provider:

- batch cursor progression
- restart from cursor N resumes correctly
- 404/partial-page behavior still skips safely

### Worker tests

- recoverable run resumes before new run starts
- staged run retries merge without refetch

## Open Implementation Constraints

### Batch size tuning

Use configuration, not constants in code.

Suggested config:

- asset page size
- vulnerability page size
- software link chunk size

### Cursor storage

Store as opaque JSON text.

Do not try to normalize all providers to one relational cursor schema.

### Idempotency

If the same batch is retried:

- staged upserts must be safe
- checkpoint commit must not double-count incorrectly

## Recommended First PR

Start with infrastructure only:

1. add `IngestionCheckpoint`
2. add `BatchNumber` to staged tables
3. add run/checkpoint statuses and entities
4. no behavior change yet

Then follow with:

5. Defender batch provider
6. `IngestionService` batch resume refactor
7. worker resume logic

