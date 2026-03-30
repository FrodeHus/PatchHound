# Ingestion Hardening Plan

## Goal

Fix the current ingestion weaknesses identified in code review:

- committed staged batches must survive recoverable retries in the same run
- merge must stop materializing whole staged datasets in memory
- Defender ingestion should have one authoritative batching implementation
- batch cursor phases should reflect only real external API work
- operator-visible failure reasons should stay useful without overexposing config details

This plan builds on [2026-03-12-batched-resumable-ingestion-plan.md](/Users/frode.hus/src/github.com/frodehus/PatchHound/docs/plans/2026-03-12-batched-resumable-ingestion-plan.md) and focuses on hardening and simplification, not on adding new ingestion capabilities.

## Findings To Fix

### 1. Same-run retries discard committed staged work

In [IngestionService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Services/IngestionService.cs), the retry loop currently clears staged rows for the same `IngestionRun` after the first failed attempt.

Effect:

- a recoverable mid-run failure can lose already committed batches
- checkpoint durability is partially defeated

### 2. Merge still reads whole staged datasets into memory

Both merge services currently load all staged rows for a run before processing:

- [StagedAssetMergeService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs)
- [StagedVulnerabilityMergeService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs)

Effect:

- memory and query pressure still scale with total staged run size
- batching helps fetch durability, but not merge scalability

### 3. Defender ingestion has duplicated one-shot and batched paths

[DefenderVulnerabilitySource.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/VulnerabilitySources/DefenderVulnerabilitySource.cs) still carries both full-fetch and batch/resume implementations.

Effect:

- maintenance cost is higher than necessary
- fixes can land in one path and miss the other

### 4. Batch cursor model still contains stale phases

The Defender vulnerability batch state still carries a `Detail` phase even though per-CVE detail fan-out was removed.

Effect:

- resume semantics are harder to reason about
- cursor payload is more complex than the actual runtime flow

### 5. Failure text is useful but should be normalized

Current failure classification is directionally correct, but some operator-facing messages still echo low-level configuration state too precisely.

Effect:

- trusted admins get good detail
- broader operational surfaces may receive more environment/config state than needed

## Target State

### Retry semantics

- recoverable retries inside the same `IngestionRun` keep staged rows and checkpoints
- `Start fresh` remains the only explicit data reset path
- scheduled purge still removes stale failed runs after 24 hours

### Merge semantics

- staged rows are read incrementally from the database
- chunking happens at the query boundary, not after `ToListAsync()`
- merge remains idempotent per `IngestionRunId`

### Defender source shape

- only the batch-oriented implementation remains authoritative
- one-shot methods either delegate to batching or are removed
- cursor phases map only to actual API work:
  - `Machines`
  - `SoftwareInventory`
  - `VulnerabilityPage`

### Failure surface

- low-level exception details remain in logs
- persisted operator-facing status text is normalized by class:
  - throttled
  - timeout
  - recoverable external failure
  - terminal configuration/auth failure
  - merge/data integrity failure

## Implementation Plan

### PR 1: Preserve committed batches across retries

Change:

- remove automatic staged-data clearing from in-process retry handling in [IngestionService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Services/IngestionService.cs)
- only clear staged data when:
  - the operator explicitly starts fresh
  - stale failed runs are purged

Tests:

- retry after a recoverable exception keeps prior staged rows
- retry resumes from checkpoint instead of replaying already committed batches

### PR 2: Simplify Defender source to one path

Change:

- make one-shot fetch methods delegate to batch paths or remove them
- remove dead/stale cursor phases, especially vulnerability `Detail`
- keep one cursor contract per phase

Tests:

- batch cursor resume still works across all Defender phases
- no behavior depends on removed one-shot methods

### PR 3: Stream staged merge inputs

Change:

- refactor [StagedAssetMergeService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs) to page staged assets and links by stable ordering
- refactor [StagedVulnerabilityMergeService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs) to page staged vulnerabilities similarly

Recommended pattern:

- query one ordered window of staged rows
- process and save
- query next window

Tests:

- multi-page staged runs merge all rows correctly
- merge behavior remains idempotent for the same `IngestionRunId`

### PR 4: Normalize operator-visible failure messages

Change:

- tighten failure text generation in [IngestionService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Services/IngestionService.cs)
- keep detailed exception context in logs
- store cleaner status text on `IngestionRun` and source runtime state

Tests:

- auth/config failures remain terminal
- throttling and timeout remain recoverable
- persisted reason text is stable and non-leaky

### PR 5: Split orchestration into smaller collaborators

Change:

- extract:
  - run lifecycle and lease handling
  - checkpoint persistence
  - batch staging coordination
  - merge coordination
  - failure classification

Goal:

- shrink [IngestionService.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Infrastructure/Services/IngestionService.cs)
- keep it as orchestration only

This is last because correctness and simplification should be locked first.

## Definition Of Done

- recoverable retries do not discard committed staged batches
- staged merge no longer loads the entire run into memory up front
- Defender ingestion has one authoritative path
- batch cursor phases correspond only to real external work
- operator-facing failure reasons are actionable and normalized

## Out Of Scope

- adding new ingestion sources
- redesigning the source admin UI beyond status text already in place
- changing the 24-hour stale failed-run purge policy
