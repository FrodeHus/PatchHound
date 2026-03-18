# Ingestion Pipeline Performance Optimization

## Problem

The ingestion pipeline is extremely slow. A code review identified 17 performance issues across `StagedVulnerabilityMergeService`, `StagedAssetMergeService`, and `IngestionService`. The dominant cost is repeated DB round-trips: `LoadChunkStateAsync` issues 7 sequential queries per subchunk, reconciliation uses non-sargable `LIKE` filters, and asset tag sync has an N+1 pattern.

## Decisions

- **Approach:** Redis as chunk-state cache + pure DB/query fixes (Approach B)
- **Scale target:** Medium (1–10k devices, 5–50k vulnerabilities per tenant)
- **Redis role:** Ephemeral ingestion-run cache, not a durable store. PostgreSQL remains source of truth.
- **Fallback:** If Redis is unavailable, parallel DB queries via `IDbContextFactory`.

---

## 1. Redis Infrastructure & Cache Layer

### New dependency

`StackExchange.Redis` added to `PatchHound.Infrastructure`. Redis 7 added to `docker-compose.yml`.

### New service: `IngestionStateCache`

Wraps Redis and provides typed access to ingestion run state. Scoped to a single tenant+run, using key prefix `ingestion:{tenantId}:{runId}:`.

### Pre-warm phase

At the start of `StagedVulnerabilityMergeService.ProcessAsync`, before the chunk loop, load the full tenant state into Redis in bulk:

| Key pattern | Value |
|---|---|
| `tv:{externalId}` | TenantVulnerability (ID, Status, DefinitionId) |
| `vd:{externalId}` | VulnerabilityDefinition (ID, ExternalId, Source, Severity, etc.) |
| `asset:{externalId}` | Asset (ID, ExternalId) |
| `va:{tvId}:{assetId}` | VulnerabilityAsset existence |
| `ep:{tvId}:{assetId}` | Open episode (ID, EpisodeNumber, Status) |
| `epmax:{tvId}:{assetId}` | Max episode number |
| `assess:{tvId}:{assetId}` | Latest assessment |

Replaces 7 queries in `LoadChunkStateAsync` with Redis `MGET`/`HGETALL` — sub-millisecond per lookup.

### Write-through

When `UpsertVulnerabilityAsync` creates/updates entities, it also writes the updated state to Redis. Keeps subsequent subchunks consistent without re-querying the DB.

### Cleanup

At ingestion end (in `IngestionService` finalization), delete all keys under the `ingestion:{tenantId}:{runId}:*` prefix.

### Fallback

If Redis is unavailable, fall back to the current DB-based `LoadChunkStateAsync` with parallel queries via `IDbContextFactory`. The cache is a performance optimization, not a correctness requirement.

---

## 2. Pure DB/Query Fixes (Issues 1–7)

### Issue 1 — Remove double `LoadChunkStateAsync`

Delete the outer `LoadChunkStateAsync` call. The exposure counts needed for `LimitChunkByExposureCount` can be derived from a lightweight `COUNT` query on `StagedVulnerabilityExposures` grouped by `StagedVulnerabilityId`.

### Issue 2 — Replace `Source.Contains()` with equality

Add a `SourceKey` column to `TenantVulnerabilities` (denormalized from `VulnerabilityDefinition.Source`). Set during upsert. Reconciliation queries filter `tv.SourceKey == sourceName` instead of joining through VulnerabilityDefinitions with `LIKE`. Add index on `(TenantId, SourceKey, Status)`.

### Issue 3 — Batch-load AssetTags

Before the asset chunk loop, load all `AssetTags` for the chunk's asset IDs in one query into a `Dictionary<Guid, List<AssetTag>>`. `SyncDefenderTagsAsync` reads from the dictionary instead of querying per asset.

### Issue 4 — Parallel queries in LoadChunkStateAsync (fallback)

For the DB fallback path (when Redis is unavailable), use `IDbContextFactory` to create 7 short-lived contexts and run all queries via `Task.WhenAll`.

### Issue 5 — Fix keyset pagination bug (correctness)

Set `lastProcessedId` to the last ID of the *trimmed* chunk (after `LimitChunkByExposureCount`), not the full chunk. Prevents silently skipped vulnerabilities.

### Issue 6 — Merge redundant episode queries

Fetch open episodes once and derive max episode numbers in-memory. Remove the separate `GroupBy/Max` query. Applies to both `StagedAssetMergeService` and `LoadChunkStateAsync` fallback.

### Issue 7 — `ExecuteUpdateAsync` for status updates

Replace the entity-loading loop in `UpdateSourceVulnerabilityStatusesAsync` with two `ExecuteUpdateAsync` calls: one setting `Status = Open` for IDs with open episodes, one setting `Status = Resolved` for the rest.

---

## 3. Medium-Impact Fixes

### Issue 8 — Pre-load security profiles

Bulk-load all `AssetSecurityProfile` records referenced by chunk assets during pre-warm (or fallback). Populate `securityProfilesById` before the inner loop.

### Issue 11 — Materialize ID sets before LINQ predicates

Extract `staleInstallations.Select(x => x.DeviceAssetId).ToList()` and `.SoftwareAssetId` into local `List<Guid>` variables before passing to EF LINQ `Where` clauses.

### Issue 14 — Eliminate double JSON deserialization

Deserialize `StagedVulnerabilityExposure.PayloadJson` once at the chunk level and pass deserialized objects through to subchunks.

### Issues 9, 10, 12, 13

Minor round-trip reductions applied opportunistically: combine status update calls, derive episode max from open episodes, merge COUNT queries.

---

## 4. Database Migration

One EF migration covering:

| Change | Details |
|---|---|
| New column | `TenantVulnerabilities.SourceKey` (varchar 64, nullable) |
| Backfill | `UPDATE TenantVulnerabilities tv SET SourceKey = vd.Source FROM VulnerabilityDefinitions vd WHERE tv.VulnerabilityDefinitionId = vd.Id` |
| New index | `TenantVulnerabilities(TenantId, SourceKey, Status)` |
| New index | `VulnerabilityDefinitions(Source)` |
| Alter index | `AssetTags(AssetId, Tag)` — add `Source` as included column |

---

## 5. End-to-End Data Flow

```
IngestionWorker (unchanged)
  ↓
IngestionService.RunIngestionAsync
  ↓
  ├── [STAGING PHASE] (unchanged)
  │
  ├── [MERGING PHASE]
  │   ├── IngestionStateCache.PreWarmAsync(tenantId, runId)
  │   │     → 7 bulk queries → Redis MSET (one-time cost)
  │   │
  │   ├── StagedVulnerabilityMergeService.ProcessAsync
  │   │     → Chunk loop: lightweight exposure COUNT (not full LoadChunkState)
  │   │     → Subchunk loop: Redis MGET for state (sub-ms)
  │   │     → Upsert + write-through to Redis
  │   │     → SaveChanges + ChangeTracker.Clear per subchunk (unchanged)
  │   │     → Reconciliation: uses SourceKey equality (indexed)
  │   │
  │   └── StagedAssetMergeService.ProcessAsync
  │         → Batch-load AssetTags per chunk (1 query, not N)
  │         → Single episode query (no redundant max query)
  │         → Materialized ID sets in LINQ predicates
  │
  └── [FINALIZATION]
      ├── IngestionStateCache.CleanupAsync(tenantId, runId)
      └── (rest unchanged)
```

### Invariants preserved

- Checkpoint-based resumability — Redis is warm-on-demand, not a durable store
- Transactional subchunk boundaries — PostgreSQL remains source of truth
- Tenant isolation — Redis keys prefixed per tenant+run
- Graceful degradation — Redis down → parallel DB fallback

### Expected impact (medium scale)

| Area | Before | After |
|---|---|---|
| LoadChunkStateAsync | ~700 DB round-trips | 0 (Redis) or ~100 (parallel fallback) |
| Asset tag sync | ~10,000 queries | ~20 (one per chunk) |
| Reconciliation | Full table scan (LIKE) | Indexed equality lookup |
| Status updates | Entity-load loop | 2 SQL statements |
| Keyset pagination | Silently skips vulns (bug) | Correct cursor advancement |

---

## Summary of Changes

| Layer | Change |
|---|---|
| docker-compose.yml | Add Redis 7 service |
| PatchHound.Infrastructure.csproj | Add StackExchange.Redis |
| IngestionStateCache (new) | Redis cache wrapper with pre-warm, write-through, cleanup, fallback |
| StagedVulnerabilityMergeService | Remove double LoadChunkState, use cache, fix keyset bug, merge episode queries, ExecuteUpdateAsync for status |
| StagedAssetMergeService | Batch-load tags, merge episode queries, materialize ID sets |
| TenantVulnerability entity | Add SourceKey property |
| EF Migration | Add SourceKey column + backfill + new indexes |
| DI registration | Register IngestionStateCache, IConnectionMultiplexer, IDbContextFactory |
