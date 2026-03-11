# Enrichment Worker Plan

## Goal

Move enrichment out of the ingestion path and into a dedicated worker that:

- uses enabled global enrichment sources,
- upserts the data models those sources enrich,
- processes work slowly and predictably,
- avoids overwhelming external APIs,
- keeps enrichment durable, observable, and retryable.

The first concrete target is:

- `NVD -> Vulnerability`

Future targets should fit the same architecture:

- `Source -> TargetModel`

Examples:

- `NVD -> Vulnerability`
- future `VendorAdvisory -> SoftwareAsset`
- future `PackageAdvisory -> Dependency`

## Why a separate worker

Current state:

- enrichment is invoked inline from ingestion in `IngestionService`
- sources like NVD often require one HTTP request per item
- the best operational behavior is not high throughput, but controlled steady progress

Problems with inline enrichment:

- source sync latency increases with enrichment latency
- external throttling directly impacts ingestion runs
- retries and backoff are mixed into the ingestion transaction flow
- re-enrichment policy is hard to control independently of ingestion

Desired state:

- ingestion persists normalized data and enqueues enrichment work
- enrichment runs independently and updates the normalized model later
- rate limiting and retry policy are source-specific and explicit

## Design principles

1. Slow and steady beats fast and throttled.
2. External-source enrichment must be durable and resumable.
3. Queue work by model item, not by batch result payload.
4. Use leases so only one worker processes a job at a time.
5. Keep source-specific throttling outside the core ingestion loop.
6. Upsert enrichments with patch semantics, not destructive overwrite semantics.
7. Every source should report recent runs, queue depth, and failure state.

## Recommended architecture

### 1. Dedicated worker

Add a hosted service:

- `EnrichmentWorker`

Responsibilities:

- poll enabled enrichment sources,
- lease a small number of due jobs,
- process them with source-specific runners,
- persist run summaries and job status,
- honor source throttling and backoff,
- never process large uncontrolled batches.

Suggested polling interval:

- every `30 seconds`

### 2. Durable queue

Add two database-backed entities:

- `EnrichmentJob`
- `EnrichmentRun`

#### EnrichmentJob

Represents one enrichable model item for one source.

Suggested fields:

- `Id`
- `SourceKey`
- `TargetModel`
- `TargetId`
- `ExternalKey`
- `Priority`
- `Status`
  - `Pending`
  - `Running`
  - `Succeeded`
  - `SucceededNoData`
  - `Failed`
  - `RetryScheduled`
  - `Skipped`
- `Attempts`
- `NextAttemptAt`
- `LastStartedAt`
- `LastCompletedAt`
- `LeaseExpiresAt`
- `LeaseOwner`
- `LastError`
- `CreatedAt`
- `UpdatedAt`

Recommended unique index:

- `(SourceKey, TargetModel, TargetId)`

This keeps the queue deduplicated per source and target item.

#### EnrichmentRun

Represents one worker execution slice for one source.

Suggested fields:

- `Id`
- `SourceKey`
- `StartedAt`
- `CompletedAt`
- `Status`
- `JobsClaimed`
- `JobsSucceeded`
- `JobsNoData`
- `JobsFailed`
- `JobsRetried`
- `LastError`

## Target model support

Do not make the queue vulnerability-specific.

Use an enum such as:

- `EnrichmentTargetModel`
  - `Vulnerability`
  - `Asset`
  - `SoftwareAsset`

The first implemented path is:

- `NVD + Vulnerability`

This keeps the worker reusable for future enrichment sources.

## Source runner abstraction

Replace inline enrichers with source-specific job runners.

Suggested interface:

```csharp
public interface IEnrichmentSourceRunner
{
    string SourceKey { get; }
    IReadOnlyList<EnrichmentTargetModel> SupportedTargetModels { get; }

    Task<EnrichmentJobResult> EnrichAsync(EnrichmentJob job, CancellationToken ct);
}
```

Suggested result:

- `Succeeded`
- `SucceededNoData`
- `RetryScheduled`
- `Failed`
- counters or metadata if useful

First implementation:

- `NvdVulnerabilityEnrichmentRunner`

Responsibilities:

- load the vulnerability by `TargetId`
- derive the external CVE ID from normalized vulnerability `ExternalId`
- call NVD once for that CVE
- upsert:
  - description
  - CVSS score
  - CVSS vector
  - published date
  - references
  - affected software definitions
- update `Source` / `Sources` contribution
- return no-data when NVD has no record

## Queue population

Ingestion should stop calling enrichers inline.

Instead:

- when a normalized vulnerability is created or updated,
- and its `ExternalId` looks enrichable by an enabled source,
- enqueue or refresh an `EnrichmentJob`

Recommended behavior for vulnerability ingestion:

- enqueue one NVD job per distinct vulnerability
- do not enqueue duplicates if a pending/running job already exists
- if a succeeded job exists but the vulnerability is still incomplete, refresh it

Queue service suggestion:

- `EnrichmentQueueService`

Responsibilities:

- deduplicate jobs,
- set initial priority,
- refresh `NextAttemptAt`,
- avoid hot-loop reenqueues.

## Throttling and retry policy

Throttling must be source-specific and explicit.

### NVD

Official guidance:

- with API key: `50 requests in a rolling 30 second window`
- without API key: `5 requests in a rolling 30 second window`
- NVD also recommends spacing requests for automated use

Sources:

- `https://nvd.nist.gov/developers/start-here`
- `https://nvd.nist.gov/developers/request-an-api-key`

Recommended PatchHound policy:

- with key:
  - concurrency `1`
  - minimum delay `750ms` to `1000ms`
- without key:
  - concurrency `1`
  - minimum delay `6s`

This is intentionally conservative.

Additional rules:

- honor `Retry-After` on `429`
- if `429` is received, stop claiming more jobs for that source in the current cycle
- reschedule failed jobs with exponential backoff

Suggested backoff:

- attempt 1: `+1 minute`
- attempt 2: `+5 minutes`
- attempt 3: `+15 minutes`
- attempt 4+: `+1 hour`

### Why Polly alone is not enough

HTTP retry policies are still useful, but they do not replace scheduling policy.

Use both:

- Polly for transient HTTP behavior
- worker-level throttling for source request cadence

## Upsert semantics

Enrichment workers should use patch semantics.

For NVD vulnerability enrichment:

Prefer:

- fill missing or weaker fields
- overwrite source-owned collections where NVD is authoritative

Recommended ownership:

- authoritative for:
  - references
  - affected software definitions
- fill-if-better:
  - description
  - CVSS score
  - CVSS vector
  - published date

This avoids destructive regressions where lower-quality data overwrites better data.

## Freshness rules

Avoid enriching the same item every cycle.

Suggested vulnerability freshness policy:

- enqueue on first create
- enqueue if critical enrichable fields are missing
- enqueue on manual refresh request
- enqueue on periodic stale threshold, e.g. every `7 days`
- enqueue if a source implementation version changes

## Worker loop behavior

Per cycle:

1. load enabled enrichment sources
2. for each source:
   - skip if credentials missing
   - skip if source lease already active
   - acquire source run lease
   - claim a small batch of due jobs
   - process jobs one by one with source-specific delay
   - persist statuses and run counters
   - release source lease

Recommended batch size:

- NVD: `5-10 jobs per cycle`

This keeps the worker visibly making progress without burst pressure.

## Observability

Expose:

- recent enrichment runs
- pending jobs per source
- failed jobs per source
- oldest pending job age
- last `429`
- last successful run
- last error

These should eventually be shown in the admin sources UI next to global enrichment sources.

## Data model interactions

Keep clear separation:

- `VulnerabilityAffectedSoftware`
  - source-side applicability definitions
- `SoftwareVulnerabilityMatch`
  - tenant-specific derived applicability

The enrichment worker updates the source-side model.
Matching services consume that model to update tenant-specific derived matches.

## Implementation phases

### Phase 1

Add durable worker foundation:

- `EnrichmentJob`
- `EnrichmentRun`
- migration
- `EnrichmentWorker`
- source leasing

### Phase 2

Replace inline NVD enrichment:

- add `EnrichmentQueueService`
- queue vulnerability jobs during ingestion
- add `NvdVulnerabilityEnrichmentRunner`
- stop calling `IVulnerabilityEnricher` from `IngestionService`

### Phase 3

Add UI and observability:

- admin source run history for enrichment
- queue depth and failure counts
- retry state visibility

### Phase 4

Expand target-model support:

- software-asset enrichment
- future advisory/package sources

## Recommended first implementation slice

Implement:

1. `EnrichmentJob`
2. `EnrichmentRun`
3. `EnrichmentWorker`
4. `NvdVulnerabilityEnrichmentRunner`
5. queue creation from vulnerability ingestion
6. removal of inline NVD enrichment from ingestion

That is the smallest slice that delivers the intended operational behavior.
