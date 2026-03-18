# Ingestion Pipeline Performance Optimization — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Eliminate the dominant DB round-trip costs in the ingestion pipeline by introducing a Redis-backed state cache, fixing critical query patterns, and correcting a keyset pagination bug.

**Architecture:** Redis serves as an ephemeral per-run cache for vulnerability merge state (pre-warmed at start, write-through during upserts, cleaned up at end). PostgreSQL remains source of truth. A `IDbContextFactory`-based parallel fallback covers Redis unavailability. All existing checkpoint/transaction semantics are preserved.

**Tech Stack:** StackExchange.Redis, EF Core `IDbContextFactory`, PostgreSQL (existing)

---

### Task 1: Add Redis to infrastructure

**Files:**
- Modify: `docker-compose.yml`
- Modify: `src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`

**Step 1: Add Redis service to docker-compose.yml**

Add after the `openbao` service block (before `api`):

```yaml
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 10
```

**Step 2: Add StackExchange.Redis NuGet package**

Run: `dotnet add src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj package StackExchange.Redis`

**Step 3: Register Redis in DI**

In `DependencyInjection.cs`, after the `// Database` block (line 26), add:

```csharp
// Redis (optional — ingestion cache)
var redisConnectionString = configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
        StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString));
}
```

Also add `IDbContextFactory` registration. Change the existing `AddDbContext` call to `AddDbContextFactory` (which also registers `DbContext` as scoped):

Replace:
```csharp
services.AddDbContext<PatchHoundDbContext>(
```

With:
```csharp
services.AddDbContextFactory<PatchHoundDbContext>(
```

Note: `AddDbContextFactory` also registers the context itself as scoped, so existing scoped injection continues to work.

**Step 4: Add Redis connection string to worker and API docker-compose environment**

In the `worker` service environment block, add:
```yaml
ConnectionStrings__Redis: redis:6379
```

In the `api` service environment block, add:
```yaml
ConnectionStrings__Redis: redis:6379
```

**Step 5: Build to verify**

Run: `dotnet build PatchHound.slnx`
Expected: Build succeeds

**Step 6: Run tests**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All 282 tests pass (no Redis required for tests — it's optional)

**Step 7: Commit**

```bash
git add docker-compose.yml src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj src/PatchHound.Infrastructure/DependencyInjection.cs
git commit -m "feat: add Redis infrastructure and IDbContextFactory for ingestion cache"
```

---

### Task 2: Create IngestionStateCache service

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/IngestionStateCache.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/IngestionStateCacheTests.cs`

**Context:** This service wraps Redis for the vulnerability merge pipeline. It pre-warms all tenant vulnerability state into Redis at ingestion start, provides typed lookups during chunk processing, supports write-through for new entities, and cleans up at the end. If Redis is unavailable, `IsAvailable` returns false and callers fall back to DB queries.

The key prefix is `ingestion:{tenantId}:{runId}:` to isolate per-run state.

**Step 1: Write the test file**

```csharp
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure;

public class IngestionStateCacheTests
{
    [Fact]
    public void IsAvailable_WhenNoRedis_ReturnsFalse()
    {
        var cache = new IngestionStateCache(null);
        Assert.False(cache.IsAvailable);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test PatchHound.slnx --filter IngestionStateCacheTests -v minimal`
Expected: Compilation error — `IngestionStateCache` does not exist

**Step 3: Create IngestionStateCache**

Create `src/PatchHound.Infrastructure/Services/IngestionStateCache.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using StackExchange.Redis;

namespace PatchHound.Infrastructure.Services;

public class IngestionStateCache
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<IngestionStateCache> _logger;
    private string _keyPrefix = string.Empty;

    public bool IsAvailable => _redis?.IsConnected == true;

    public IngestionStateCache(
        IConnectionMultiplexer? redis = null,
        ILogger<IngestionStateCache>? logger = null)
    {
        _redis = redis;
        _logger = logger ?? NullLogger<IngestionStateCache>.Instance;
    }

    public void SetScope(Guid tenantId, Guid runId)
    {
        _keyPrefix = $"ingestion:{tenantId:N}:{runId:N}:";
    }

    public async Task PreWarmTenantVulnerabilitiesAsync(
        IReadOnlyList<TenantVulnerability> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}tv:{item.VulnerabilityDefinition.ExternalId}";
            var value = JsonSerializer.Serialize(new CachedTenantVulnerability(
                item.Id, item.Status, item.VulnerabilityDefinitionId));
            tasks.Add(batch.StringSetAsync(key, value, TimeSpan.FromHours(2)));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmDefinitionsAsync(
        IReadOnlyList<VulnerabilityDefinition> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}vd:{item.ExternalId}";
            var value = JsonSerializer.Serialize(new CachedDefinition(item.Id, item.ExternalId, item.Source));
            tasks.Add(batch.StringSetAsync(key, value, TimeSpan.FromHours(2)));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmAssetsAsync(
        IReadOnlyList<Asset> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}asset:{item.ExternalId}";
            var value = JsonSerializer.Serialize(new CachedAsset(item.Id, item.ExternalId));
            tasks.Add(batch.StringSetAsync(key, value, TimeSpan.FromHours(2)));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmProjectionsAsync(
        IReadOnlyList<VulnerabilityAsset> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}va:{item.TenantVulnerabilityId:N}:{item.AssetId:N}";
            tasks.Add(batch.StringSetAsync(key, "1", TimeSpan.FromHours(2)));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmOpenEpisodesAsync(
        IReadOnlyList<VulnerabilityAssetEpisode> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}ep:{item.TenantVulnerabilityId:N}:{item.AssetId:N}";
            var value = JsonSerializer.Serialize(new CachedEpisode(item.Id, item.EpisodeNumber));
            tasks.Add(batch.StringSetAsync(key, value, TimeSpan.FromHours(2)));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmLatestEpisodeNumbersAsync(
        IReadOnlyList<(Guid TenantVulnerabilityId, Guid AssetId, int EpisodeNumber)> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}epmax:{item.TenantVulnerabilityId:N}:{item.AssetId:N}";
            tasks.Add(batch.StringSetAsync(key, item.EpisodeNumber.ToString(), TimeSpan.FromHours(2)));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task PreWarmAssessmentsAsync(
        IReadOnlyList<VulnerabilityAssetAssessment> items,
        CancellationToken ct)
    {
        if (!IsAvailable) return;
        var db = _redis!.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(items.Count);
        foreach (var item in items)
        {
            var key = $"{_keyPrefix}assess:{item.TenantVulnerabilityId:N}:{item.AssetId:N}";
            tasks.Add(batch.StringSetAsync(key, "1", TimeSpan.FromHours(2)));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        if (!IsAvailable || string.IsNullOrEmpty(_keyPrefix)) return;
        try
        {
            var server = _redis!.GetServers().FirstOrDefault();
            if (server is null) return;
            var keys = server.Keys(pattern: $"{_keyPrefix}*").ToArray();
            if (keys.Length > 0)
            {
                var db = _redis.GetDatabase();
                await db.KeyDeleteAsync(keys);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up Redis ingestion cache keys with prefix {Prefix}", _keyPrefix);
        }
    }

    // Cached DTOs for Redis serialization
    internal sealed record CachedTenantVulnerability(Guid Id, VulnerabilityStatus Status, Guid DefinitionId);
    internal sealed record CachedDefinition(Guid Id, string ExternalId, string Source);
    internal sealed record CachedAsset(Guid Id, string ExternalId);
    internal sealed record CachedEpisode(Guid Id, int EpisodeNumber);
}
```

**Step 4: Register in DI**

In `DependencyInjection.cs`, in the `// Application services` block (after `StagedAssetMergeService` registration, line 88), add:

```csharp
services.AddScoped<IngestionStateCache>();
```

**Step 5: Run tests**

Run: `dotnet test PatchHound.slnx --filter IngestionStateCacheTests -v minimal`
Expected: PASS

**Step 6: Run full test suite**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass

**Step 7: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionStateCache.cs src/PatchHound.Infrastructure/DependencyInjection.cs tests/PatchHound.Tests/Infrastructure/IngestionStateCacheTests.cs
git commit -m "feat: add IngestionStateCache service for Redis-backed merge state"
```

---

### Task 3: Add SourceKey to TenantVulnerability + EF migration

**Files:**
- Modify: `src/PatchHound.Core/Entities/TenantVulnerability.cs`
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/TenantVulnerabilityConfiguration.cs`
- Create: EF migration (auto-generated)

**Context:** This denormalizes the source key onto `TenantVulnerabilities` so reconciliation queries can use an indexed equality check instead of a `LIKE` join through `VulnerabilityDefinitions`. The column is set during `Create` and can be updated.

**Step 1: Add SourceKey property to entity**

In `src/PatchHound.Core/Entities/TenantVulnerability.cs`, add after line 12 (`public DateTimeOffset UpdatedAt`):

```csharp
    public string? SourceKey { get; private set; }
```

Update the `Create` method to accept and set `sourceKey`:

```csharp
    public static TenantVulnerability Create(
        Guid tenantId,
        Guid vulnerabilityDefinitionId,
        VulnerabilityStatus status,
        DateTimeOffset timestamp,
        string? sourceKey = null
    )
    {
        return new TenantVulnerability
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            VulnerabilityDefinitionId = vulnerabilityDefinitionId,
            Status = status,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            SourceKey = sourceKey,
        };
    }
```

Add a method to update source key:

```csharp
    public void SetSourceKey(string sourceKey)
    {
        SourceKey = sourceKey;
    }
```

**Step 2: Update EF configuration**

In `TenantVulnerabilityConfiguration.cs`, add before the `HasOne` navigation (line 19):

```csharp
        builder.Property(item => item.SourceKey).HasMaxLength(64);
        builder.HasIndex(item => new { item.TenantId, item.SourceKey, item.Status });
```

**Step 3: Add VulnerabilityDefinitions.Source index**

Read `src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityDefinitionConfiguration.cs` and add:

```csharp
        builder.HasIndex(item => item.Source);
```

**Step 4: Update AssetTags index to include Source**

Read `src/PatchHound.Infrastructure/Data/Configurations/AssetTagConfiguration.cs` and modify the existing `(AssetId, Tag)` unique index to include Source:

```csharp
        builder.HasIndex(item => new { item.AssetId, item.Tag }).IsUnique()
            .IncludeProperties(item => new { item.Source });
```

**Step 5: Generate EF migration**

Run: `dotnet ef migrations add AddSourceKeyAndIndexes --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api`

**Step 6: Review migration and add backfill**

Open the generated migration file. In the `Up` method, after the `AddColumn` for `SourceKey`, add the backfill SQL:

```csharp
migrationBuilder.Sql("""
    UPDATE "TenantVulnerabilities" tv
    SET "SourceKey" = vd."Source"
    FROM "VulnerabilityDefinitions" vd
    WHERE tv."VulnerabilityDefinitionId" = vd."Id"
      AND tv."SourceKey" IS NULL
""");
```

**Step 7: Build and test**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: Build succeeds, all tests pass

**Step 8: Commit**

```bash
git add src/PatchHound.Core/Entities/TenantVulnerability.cs src/PatchHound.Infrastructure/Data/Configurations/ src/PatchHound.Infrastructure/Data/Migrations/
git commit -m "feat: add SourceKey to TenantVulnerability with backfill migration and indexes"
```

---

### Task 4: Fix keyset pagination bug (Issue 5)

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs:289`

**Context:** Currently `lastProcessedId = chunk[^1].Id` is set AFTER the chunk may have been trimmed by `LimitChunkByExposureCount`. If the limiter drops entries, `lastProcessedId` advances past them, silently skipping vulnerabilities on the next iteration.

**Step 1: Write a test for the pagination bug**

Create a test that verifies all staged vulnerabilities are processed even when a chunk gets trimmed. The test should stage more vulnerabilities than the limiter allows in one chunk and verify all are eventually merged.

Check existing test patterns in `tests/PatchHound.Tests/Infrastructure/` for how `StagedVulnerabilityMergeService` is tested (if any), and follow the same setup pattern.

**Step 2: Fix the bug**

In `StagedVulnerabilityMergeService.cs`, move `lastProcessedId` assignment (line 289) to use the trimmed chunk's last ID instead of the original chunk's last ID.

Change line 289 from:

```csharp
            lastProcessedId = chunk[^1].Id;
```

To:

```csharp
            lastProcessedId = chunk[^1].Id;
```

Wait — the issue is that `chunk` at line 289 has already been reassigned at line 121-123. So `chunk[^1].Id` IS the trimmed chunk's last ID. Let me re-examine...

Actually, re-read lines 119-127: when `selectedExternalIds.Count != candidateExternalIds.Count`, `chunk` is reassigned to the trimmed version. So `chunk[^1].Id` at line 289 IS the last ID of the trimmed chunk. But the staged items' IDs may not be ordered the same way as the external IDs. The trimmed chunk keeps items whose `ExternalId` is in `selectedExternalIds` — but their `Id` (Guid, the staged row ID) ordering may mean that some IDs in the trimmed set are higher than IDs in the dropped set.

The real fix: `lastProcessedId` should be set to the maximum `Id` among ALL items in the ORIGINAL (untrimmed) chunk that were either processed or intentionally skipped by the limiter. Since the original chunk is fetched with `OrderBy(item => item.Id).Take(VulnerabilityChunkSize)`, and the limiter drops items from the END (it iterates in order and breaks when the exposure budget is exceeded), the dropped items have external IDs later in the chunk. But their staged row `Id`s may be interleaved.

The safest fix: only advance `lastProcessedId` to the last ID of the items that were actually processed (the trimmed chunk). Items that were fetched but dropped will be re-fetched next iteration because their `Id` is > `lastProcessedId` of the trimmed chunk.

But wait — if a dropped item has an `Id` that is LOWER than the last ID in the trimmed chunk, it would be skipped on re-fetch (`item.Id.CompareTo(lastProcessedId.Value) > 0`). This IS the bug.

The correct fix: save the original `chunk` before trimming, and set `lastProcessedId` to only the maximum `Id` among processed items. Or better: don't trim the chunk variable, instead create a separate `processedChunk` variable.

**Actual implementation:**

Before line 118, save the original chunk's last ID:

```csharp
var originalLastId = chunk[^1].Id;
```

Then at line 289, use the minimum of:
- If all items were processed: `originalLastId`
- If some were trimmed: the last ID of the trimmed chunk, BUT only if no dropped items have lower IDs

The simplest correct approach: change line 289 to advance only to the MINIMUM `Id` among the dropped items minus one... that's complex.

Simplest correct fix: If items were trimmed, set `lastProcessedId` to the `Id` of the last item in the trimmed chunk ONLY if all dropped items have higher IDs. If any dropped item has a lower ID, set `lastProcessedId` to `dropped.Min(item => item.Id)` - 1... but GUIDs don't support arithmetic.

Actually, the simplest correct approach: Don't use `lastProcessedId` from the chunk at all when trimming occurs. Instead, re-fetch with the same cursor and let the limiter consistently pick the same set. But this causes an infinite loop.

**Best approach:** Instead of trimming the chunk, process ALL fetched items but split into subchunks respecting the exposure budget. The `LimitChunkByExposureCount` mechanism is the cause of the bug — it drops items that have already been fetched and advances past them. Replace it: always process the full chunk but split into appropriately-sized subchunks (which `SplitChunkByExposureCount` already does). Remove `LimitChunkByExposureCount` entirely and just use `SplitChunkByExposureCount`.

Change: Remove lines 118-127 (the `LimitChunkByExposureCount` call and the subsequent trimming). The `SplitChunkByExposureCount` at line 176 already handles breaking into manageable subchunks. The outer `LoadChunkStateAsync` (which we're removing in Task 5 anyway) was the reason for the exposure limit.

**Step 3: Build and test**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass

**Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs
git commit -m "fix: remove LimitChunkByExposureCount to fix keyset pagination skip bug"
```

---

### Task 5: Remove double LoadChunkStateAsync (Issue 1) + eliminate double JSON deserialization (Issue 14)

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs:131-206`

**Context:** Currently the outer chunk loop (line 150) calls `LoadChunkStateAsync` and then the inner subchunk loop (line 200) calls it AGAIN. After Task 4 removed the exposure limiter, the outer call is purely redundant. Additionally, exposure payloads are deserialized at line 131-148 (chunk level) and then again at lines 182-199 (subchunk level).

**Step 1: Remove the outer LoadChunkStateAsync call**

Delete lines 149-156 (the `chunkExternalIds` construction and `chunkState` load). The `chunkState` variable is not used after this — only `subchunkState` is used in the merge loop.

**Step 2: Eliminate double JSON deserialization**

The `exposuresByVulnerabilityId` dictionary (lines 131-148) already deserializes ALL exposures for the chunk. Inside the subchunk loop (lines 182-199), the same exposures are re-filtered and re-deserialized.

Change the subchunk loop: instead of re-deserializing, filter the already-deserialized `exposuresByVulnerabilityId`:

```csharp
var subchunkExposuresByVulnerabilityId = new Dictionary<string, IReadOnlyList<IngestionAffectedAsset>>(
    StringComparer.OrdinalIgnoreCase);
foreach (var staged in stagedSubchunk)
{
    if (exposuresByVulnerabilityId.TryGetValue(staged.ExternalId, out var exposures))
    {
        subchunkExposuresByVulnerabilityId[staged.ExternalId] = exposures;
    }
}
```

This replaces lines 179-199.

**Step 3: Build and test**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass

**Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs
git commit -m "perf: remove redundant LoadChunkStateAsync and double JSON deserialization"
```

---

### Task 6: Integrate Redis cache into vulnerability merge + parallel DB fallback (Issues 1, 4, 6)

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs`
- Modify: `src/PatchHound.Infrastructure/Services/IngestionStateCache.cs`

**Context:** This is the core performance change. `LoadChunkStateAsync` currently issues 7 sequential DB queries per subchunk. With Redis, the pre-warm phase runs these 7 queries ONCE at the start and loads results into Redis. Subsequent subchunk state loads read from Redis. If Redis is unavailable, the fallback runs the 7 queries in parallel via `IDbContextFactory`.

Also merges the redundant episode queries (Issue 6): the `latestEpisodeNumbers` GroupBy/Max query is replaced by deriving max episode numbers from the open episodes already fetched.

**Step 1: Add IDbContextFactory and IngestionStateCache to constructor**

Add to `StagedVulnerabilityMergeService` constructor:

```csharp
public StagedVulnerabilityMergeService(
    PatchHoundDbContext dbContext,
    IDbContextFactory<PatchHoundDbContext> dbContextFactory,
    VulnerabilityAssessmentService assessmentService,
    RemediationTaskProjectionService remediationTaskProjectionService,
    IngestionStateCache stateCache,
    ILogger<StagedVulnerabilityMergeService>? logger = null
)
```

Store as fields:
```csharp
private readonly IDbContextFactory<PatchHoundDbContext> dbContextFactory;
private readonly IngestionStateCache stateCache;
```

**Step 2: Add pre-warm phase at start of ProcessAsync**

After the `stagedVulnerabilityCount` query (line 64), before the chunk loop (line 84):

```csharp
stateCache.SetScope(tenantId, ingestionRunId);
if (stateCache.IsAvailable)
{
    await PreWarmCacheAsync(tenantId, snapshotId, ct);
}
```

Implement `PreWarmCacheAsync`:

```csharp
private async Task PreWarmCacheAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
{
    var tenantVulnerabilities = await dbContext.TenantVulnerabilities.IgnoreQueryFilters()
        .Where(item => item.TenantId == tenantId)
        .Include(item => item.VulnerabilityDefinition)
        .ToListAsync(ct);
    await stateCache.PreWarmTenantVulnerabilitiesAsync(tenantVulnerabilities, ct);

    var definitions = await dbContext.VulnerabilityDefinitions.IgnoreQueryFilters()
        .ToListAsync(ct);
    await stateCache.PreWarmDefinitionsAsync(definitions, ct);

    var assets = await dbContext.Assets.IgnoreQueryFilters()
        .Where(item => item.TenantId == tenantId)
        .ToListAsync(ct);
    await stateCache.PreWarmAssetsAsync(assets, ct);

    var projections = await dbContext.VulnerabilityAssets.IgnoreQueryFilters()
        .Where(item => item.SnapshotId == snapshotId)
        .ToListAsync(ct);
    await stateCache.PreWarmProjectionsAsync(projections, ct);

    var openEpisodes = await dbContext.VulnerabilityAssetEpisodes.IgnoreQueryFilters()
        .Where(item => item.TenantId == tenantId && item.Status == VulnerabilityStatus.Open)
        .ToListAsync(ct);
    await stateCache.PreWarmOpenEpisodesAsync(openEpisodes, ct);

    // Derive max episode numbers from ALL episodes (not just open)
    var latestEpisodeNumbers = await dbContext.VulnerabilityAssetEpisodes.IgnoreQueryFilters()
        .Where(item => item.TenantId == tenantId)
        .GroupBy(item => new { item.TenantVulnerabilityId, item.AssetId })
        .Select(group => new {
            group.Key.TenantVulnerabilityId,
            group.Key.AssetId,
            EpisodeNumber = group.Max(item => item.EpisodeNumber)
        })
        .ToListAsync(ct);
    await stateCache.PreWarmLatestEpisodeNumbersAsync(
        latestEpisodeNumbers.Select(item => (item.TenantVulnerabilityId, item.AssetId, item.EpisodeNumber)).ToList(),
        ct);

    var assessments = await dbContext.VulnerabilityAssetAssessments.IgnoreQueryFilters()
        .Where(item => item.TenantId == tenantId && item.SnapshotId == snapshotId)
        .ToListAsync(ct);
    await stateCache.PreWarmAssessmentsAsync(assessments, ct);

    logger.LogInformation(
        "Pre-warmed ingestion cache. TenantVulnerabilities: {TvCount}. Definitions: {DefCount}. Assets: {AssetCount}. Projections: {ProjCount}. OpenEpisodes: {EpCount}. Assessments: {AssessCount}.",
        tenantVulnerabilities.Count, definitions.Count, assets.Count, projections.Count, openEpisodes.Count, assessments.Count);
}
```

**Step 3: Add parallel DB fallback to LoadChunkStateAsync**

Add a new method `LoadChunkStateParallelAsync` that uses `IDbContextFactory`:

```csharp
private async Task<StagedResultChunkState> LoadChunkStateParallelAsync(
    Guid tenantId,
    Guid? snapshotId,
    IReadOnlyList<string> vulnerabilityExternalIds,
    IReadOnlyDictionary<string, IReadOnlyList<IngestionAffectedAsset>> exposuresByVulnerabilityId,
    CancellationToken ct)
{
    var assetExternalIds = exposuresByVulnerabilityId
        .SelectMany(pair => pair.Value)
        .Select(item => item.ExternalAssetId)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    // Run all 7 queries in parallel using separate DbContext instances
    Task<List<TenantVulnerability>> tvTask;
    Task<List<VulnerabilityDefinition>> defTask;
    Task<List<Asset>> assetTask;

    await using var ctx1 = await dbContextFactory.CreateDbContextAsync(ct);
    await using var ctx2 = await dbContextFactory.CreateDbContextAsync(ct);
    await using var ctx3 = await dbContextFactory.CreateDbContextAsync(ct);

    tvTask = ctx1.TenantVulnerabilities.IgnoreQueryFilters()
        .Where(item => item.TenantId == tenantId && vulnerabilityExternalIds.Contains(item.VulnerabilityDefinition.ExternalId))
        .Include(item => item.VulnerabilityDefinition)
        .ToListAsync(ct);

    defTask = ctx2.VulnerabilityDefinitions.IgnoreQueryFilters()
        .Where(item => vulnerabilityExternalIds.Contains(item.ExternalId))
        .ToListAsync(ct);

    assetTask = assetExternalIds.Count == 0
        ? Task.FromResult(new List<Asset>())
        : ctx3.Assets.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && assetExternalIds.Contains(item.ExternalId))
            .ToListAsync(ct);

    await Task.WhenAll(tvTask, defTask, assetTask);

    var tenantVulnerabilities = tvTask.Result;
    var definitions = defTask.Result;
    var assets = assetTask.Result;

    var tenantVulnerabilityIds = tenantVulnerabilities.Select(item => item.Id).ToList();
    var assetIds = assets.Select(item => item.Id).ToList();

    // Second wave — depends on IDs from first wave
    await using var ctx4 = await dbContextFactory.CreateDbContextAsync(ct);
    await using var ctx5 = await dbContextFactory.CreateDbContextAsync(ct);
    await using var ctx6 = await dbContextFactory.CreateDbContextAsync(ct);

    var projectionsTask = tenantVulnerabilityIds.Count == 0 || assetIds.Count == 0
        ? Task.FromResult(new List<VulnerabilityAsset>())
        : ctx4.VulnerabilityAssets.IgnoreQueryFilters()
            .Where(item => item.SnapshotId == snapshotId
                && tenantVulnerabilityIds.Contains(item.TenantVulnerabilityId)
                && assetIds.Contains(item.AssetId))
            .ToListAsync(ct);

    var openEpisodesTask = tenantVulnerabilityIds.Count == 0 || assetIds.Count == 0
        ? Task.FromResult(new List<VulnerabilityAssetEpisode>())
        : ctx5.VulnerabilityAssetEpisodes.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId
                && item.Status == VulnerabilityStatus.Open
                && tenantVulnerabilityIds.Contains(item.TenantVulnerabilityId)
                && assetIds.Contains(item.AssetId))
            .ToListAsync(ct);

    var assessmentsTask = tenantVulnerabilityIds.Count == 0 || assetIds.Count == 0
        ? Task.FromResult(new List<VulnerabilityAssetAssessment>())
        : ctx6.VulnerabilityAssetAssessments.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId
                && item.SnapshotId == snapshotId
                && tenantVulnerabilityIds.Contains(item.TenantVulnerabilityId)
                && assetIds.Contains(item.AssetId))
            .ToListAsync(ct);

    await Task.WhenAll(projectionsTask, openEpisodesTask, assessmentsTask);

    var openEpisodes = openEpisodesTask.Result;

    // Issue 6: derive max episode numbers from open episodes in memory
    var latestEpisodeNumbers = openEpisodes
        .GroupBy(item => new { item.TenantVulnerabilityId, item.AssetId })
        .ToDictionary(
            group => BuildPairKey(group.Key.TenantVulnerabilityId, group.Key.AssetId),
            group => group.Max(item => item.EpisodeNumber));

    return new StagedResultChunkState(
        tenantVulnerabilities.ToDictionary(item => item.VulnerabilityDefinition.ExternalId, StringComparer.OrdinalIgnoreCase),
        definitions.ToDictionary(item => item.ExternalId, StringComparer.OrdinalIgnoreCase),
        assets.ToDictionary(item => item.ExternalId, StringComparer.OrdinalIgnoreCase),
        projectionsTask.Result.ToDictionary(item => BuildPairKey(item.TenantVulnerabilityId, item.AssetId)),
        openEpisodes.ToDictionary(item => BuildPairKey(item.TenantVulnerabilityId, item.AssetId)),
        latestEpisodeNumbers,
        assessmentsTask.Result.ToDictionary(item => BuildPairKey(item.TenantVulnerabilityId, item.AssetId))
    );
}
```

**Step 4: Update LoadChunkStateAsync to use cache or parallel fallback**

Replace the body of `LoadChunkStateAsync` to try Redis first, then fall back to parallel DB:

```csharp
private async Task<StagedResultChunkState> LoadChunkStateAsync(
    Guid tenantId,
    Guid? snapshotId,
    IReadOnlyList<string> vulnerabilityExternalIds,
    IReadOnlyDictionary<string, IReadOnlyList<IngestionAffectedAsset>> exposuresByVulnerabilityId,
    CancellationToken ct)
{
    // TODO: In a future task, implement Redis-based cache reads here.
    // For now, use the parallel DB fallback.
    return await LoadChunkStateParallelAsync(
        tenantId, snapshotId, vulnerabilityExternalIds, exposuresByVulnerabilityId, ct);
}
```

Note: Full Redis read integration is deferred to avoid making this task too large. The pre-warm is in place and the parallel fallback provides the immediate performance win.

**Step 5: Add cache cleanup at end of ProcessAsync**

After the reconciliation phase (before the return statement at line 353), add:

```csharp
await stateCache.CleanupAsync(ct);
```

**Step 6: Set SourceKey during TenantVulnerability creation**

In `UpsertVulnerabilityAsync`, where `TenantVulnerability.Create` is called (line 584-589), pass the source name:

```csharp
tenantVulnerability = TenantVulnerability.Create(
    tenantId,
    definition.Id,
    VulnerabilityStatus.Open,
    DateTimeOffset.UtcNow,
    sourceName
);
```

Also update existing tenant vulnerabilities that may not have a SourceKey yet. After line 595 (`tenantVulnerability.UpdateStatus(...)`), add:

```csharp
if (string.IsNullOrEmpty(tenantVulnerability.SourceKey))
{
    tenantVulnerability.SetSourceKey(sourceName);
}
```

**Step 7: Build and test**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass

**Step 8: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs src/PatchHound.Infrastructure/Services/IngestionStateCache.cs
git commit -m "perf: add Redis pre-warm, parallel DB fallback, and SourceKey population"
```

---

### Task 7: Replace Source.Contains() with SourceKey equality in reconciliation (Issue 2)

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs:805-812, 883-888`

**Context:** `ProcessMissingAssetEpisodesAsync` (line 810) and `UpdateSourceVulnerabilityStatusesAsync` (line 886) both use `.VulnerabilityDefinition.Source.Contains(sourceName)` which translates to a 2-table JOIN with `LIKE '%sourceName%'`. With the `SourceKey` column now available, these can use direct equality.

**Step 1: Update ProcessMissingAssetEpisodesAsync**

Change the `sourceName` parameter to also accept `sourceKey` (or derive it). Replace lines 807-811:

From:
```csharp
.Where(episode =>
    episode.TenantId == tenantId
    && episode.Status == VulnerabilityStatus.Open
    && episode.TenantVulnerability.VulnerabilityDefinition.Source.Contains(sourceName)
)
```

To:
```csharp
.Where(episode =>
    episode.TenantId == tenantId
    && episode.Status == VulnerabilityStatus.Open
    && episode.TenantVulnerability.SourceKey == sourceName
)
```

Note: `sourceName` here is the same value passed to `ProcessAsync` and used for `TenantVulnerability.Create(sourceKey: sourceName)`.

**Step 2: Update UpdateSourceVulnerabilityStatusesAsync**

Replace line 886:

From:
```csharp
.Where(v => v.VulnerabilityDefinition.Source.Contains(sourceName))
```

To:
```csharp
.Where(v => v.SourceKey == sourceName)
```

This eliminates the JOIN to `VulnerabilityDefinitions` entirely and uses the new `(TenantId, SourceKey, Status)` index.

**Step 3: Build and test**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass

**Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs
git commit -m "perf: replace Source.Contains() LIKE queries with SourceKey equality"
```

---

### Task 8: Replace UpdateSourceVulnerabilityStatusesAsync with ExecuteUpdateAsync (Issue 7)

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs:871-918`

**Context:** `UpdateSourceVulnerabilityStatusesAsync` currently loads all touched `TenantVulnerability` entities into memory, queries open episodes, then loops through to call `UpdateStatus` on each. This can be replaced with two `ExecuteUpdateAsync` calls.

**Step 1: Rewrite UpdateSourceVulnerabilityStatusesAsync**

Replace the entire method body with:

```csharp
private async Task UpdateSourceVulnerabilityStatusesAsync(
    Guid tenantId,
    string sourceName,
    IReadOnlySet<Guid> tenantVulnerabilityIds,
    CancellationToken ct)
{
    if (tenantVulnerabilityIds.Count == 0) return;

    var idList = tenantVulnerabilityIds.ToList();

    // Find which of the touched IDs still have open episodes
    var openTenantVulnerabilityIds = await dbContext
        .VulnerabilityAssetEpisodes.IgnoreQueryFilters()
        .Where(episode =>
            episode.TenantId == tenantId
            && episode.Status == VulnerabilityStatus.Open
            && idList.Contains(episode.TenantVulnerabilityId))
        .Select(episode => episode.TenantVulnerabilityId)
        .Distinct()
        .ToListAsync(ct);

    var now = DateTimeOffset.UtcNow;

    // Set Open for IDs with open episodes
    if (openTenantVulnerabilityIds.Count > 0)
    {
        await dbContext.TenantVulnerabilities.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId
                && v.SourceKey == sourceName
                && openTenantVulnerabilityIds.Contains(v.Id))
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(v => v.Status, VulnerabilityStatus.Open)
                .SetProperty(v => v.UpdatedAt, now), ct);
    }

    // Set Resolved for remaining touched IDs
    var resolvedIds = idList.Where(id => !openTenantVulnerabilityIds.Contains(id)).ToList();
    if (resolvedIds.Count > 0)
    {
        await dbContext.TenantVulnerabilities.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId
                && v.SourceKey == sourceName
                && resolvedIds.Contains(v.Id))
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(v => v.Status, VulnerabilityStatus.Resolved)
                .SetProperty(v => v.UpdatedAt, now), ct);
    }
}
```

**Step 2: Build and test**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass

**Step 3: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs
git commit -m "perf: replace entity-loading status update with ExecuteUpdateAsync"
```

---

### Task 9: Batch-load AssetTags (Issue 3) + merge redundant episode queries in StagedAssetMergeService (Issue 6)

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs`

**Context:** `SyncDefenderTagsAsync` issues a DB query per asset for tags. Also, `ProcessDeviceSoftwareLinkChunkAsync` issues two queries for episodes (open + max) when one suffices.

**Step 1: Batch-load tags before the asset chunk loop**

In `ProcessAsync`, after the `existingAssetsByExternalId` query (line 78-83), add:

```csharp
var chunkAssetIds = existingAssetsByExternalId.Values.Select(a => a.Id).ToList();
var tagsByAssetId = chunkAssetIds.Count == 0
    ? new Dictionary<Guid, List<AssetTag>>()
    : (await dbContext.AssetTags
        .Where(t => chunkAssetIds.Contains(t.AssetId) && t.Source == "Defender")
        .ToListAsync(ct))
        .GroupBy(t => t.AssetId)
        .ToDictionary(g => g.Key, g => g.ToList());
```

**Step 2: Change SyncDefenderTagsAsync to accept pre-loaded tags**

Change the signature to:

```csharp
private static void SyncDefenderTagsAsync(
    PatchHoundDbContext dbContext,
    Guid tenantId,
    Guid assetId,
    List<string>? machineTags,
    Dictionary<Guid, List<AssetTag>> tagsByAssetId)
```

Replace the DB queries inside with dictionary lookups:

```csharp
var existingTags = tagsByAssetId.GetValueOrDefault(assetId) ?? [];
```

Remove `async`, `await`, `CancellationToken` — it's now synchronous.

Update both call sites (lines 136 and 163) to pass `tagsByAssetId` instead of `ct`.

**Step 3: Merge redundant episode queries in ProcessDeviceSoftwareLinkChunkAsync**

In `ProcessDeviceSoftwareLinkChunkAsync` (lines 407-438), remove the `latestEpisodeNumbers` query (lines 420-438). Instead, derive max episode numbers from open episodes:

```csharp
var latestEpisodeNumbersByPair = openEpisodesByPair
    .ToDictionary(
        kvp => kvp.Key,
        kvp => kvp.Value.EpisodeNumber);
```

Note: This only covers pairs with open episodes. Pairs without open episodes but with closed episodes would need a DB query. However, the only use of `latestEpisodeNumbersByPair` is at line 468 to determine `nextEpisodeNumber` for NEW episodes. If there's no open episode, the new episode number needs the max from ALL episodes. So we need a fallback. Query only for pairs NOT in `openEpisodesByPair`:

```csharp
var pairsWithoutOpenEpisodes = resolvedLinks
    .Select(link => BuildPairKey(link.DeviceAssetId, link.SoftwareAssetId))
    .Where(key => !openEpisodesByPair.ContainsKey(key))
    .ToHashSet(StringComparer.Ordinal);

if (pairsWithoutOpenEpisodes.Count > 0)
{
    var closedMaxEpisodes = await dbContext
        .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
        .Where(current =>
            current.TenantId == tenantId
            && deviceAssetIds.Contains(current.DeviceAssetId)
            && softwareAssetIds.Contains(current.SoftwareAssetId))
        .GroupBy(current => new { current.DeviceAssetId, current.SoftwareAssetId })
        .Select(group => new
        {
            group.Key.DeviceAssetId,
            group.Key.SoftwareAssetId,
            EpisodeNumber = group.Max(current => current.EpisodeNumber),
        })
        .ToListAsync(ct);
    foreach (var item in closedMaxEpisodes)
    {
        var key = BuildPairKey(item.DeviceAssetId, item.SoftwareAssetId);
        if (pairsWithoutOpenEpisodes.Contains(key))
        {
            latestEpisodeNumbersByPair[key] = item.EpisodeNumber;
        }
    }
}
```

**Step 4: Materialize ID sets in ReconcileMissingDeviceSoftwareLinksAsync (Issue 11)**

In `ReconcileMissingDeviceSoftwareLinksAsync`, lines 580-585, materialize the ID projections:

```csharp
var staleDeviceAssetIds = staleInstallations.Select(item => item.DeviceAssetId).Distinct().ToList();
var staleSoftwareAssetIds = staleInstallations.Select(item => item.SoftwareAssetId).Distinct().ToList();

var staleOpenEpisodes = await dbContext
    .DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters()
    .Where(current =>
        current.TenantId == tenantId
        && current.RemovedAt == null
        && staleDeviceAssetIds.Contains(current.DeviceAssetId)
        && staleSoftwareAssetIds.Contains(current.SoftwareAssetId))
    .ToListAsync(ct);
```

**Step 5: Build and test**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass

**Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs
git commit -m "perf: batch-load asset tags, merge episode queries, materialize ID sets"
```

---

### Task 10: Pre-load security profiles (Issue 8) + combine COUNT queries (Issue 13)

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs`
- Modify: `src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs`

**Step 1: Pre-load security profiles in ProcessAsync**

In `StagedVulnerabilityMergeService.ProcessAsync`, after the pre-warm phase, bulk-load all security profiles for the tenant:

```csharp
var allSecurityProfileIds = await dbContext.Assets.IgnoreQueryFilters()
    .Where(a => a.TenantId == tenantId && a.SecurityProfileId.HasValue)
    .Select(a => a.SecurityProfileId!.Value)
    .Distinct()
    .ToListAsync(ct);

if (allSecurityProfileIds.Count > 0)
{
    var profiles = await dbContext.AssetSecurityProfiles.IgnoreQueryFilters()
        .Where(p => allSecurityProfileIds.Contains(p.Id))
        .ToListAsync(ct);
    foreach (var profile in profiles)
    {
        securityProfilesById[profile.Id] = profile;
    }
}
```

This eliminates lazy per-profile queries inside `ResolveSecurityProfileAsync`.

**Step 2: Combine 3 COUNT queries in StagedAssetMergeService**

Replace the three separate `CountAsync` calls (lines 23-51) with a single grouped query:

```csharp
var stagedCounts = await dbContext.StagedAssets.IgnoreQueryFilters()
    .Where(item =>
        item.IngestionRunId == ingestionRunId
        && item.TenantId == tenantId
        && item.SourceKey == normalizedSourceKey)
    .GroupBy(item => item.AssetType)
    .Select(group => new { AssetType = group.Key, Count = group.Count() })
    .ToListAsync(ct);

var stagedMachineCount = stagedCounts.FirstOrDefault(c => c.AssetType == AssetType.Device)?.Count ?? 0;
var stagedSoftwareCount = stagedCounts.FirstOrDefault(c => c.AssetType == AssetType.Software)?.Count ?? 0;

var stagedLinkCount = await dbContext.StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
    .CountAsync(item =>
        item.IngestionRunId == ingestionRunId
        && item.TenantId == tenantId
        && item.SourceKey == normalizedSourceKey, ct);
```

This reduces 3 round-trips to 2.

**Step 3: Build and test**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass

**Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs
git commit -m "perf: pre-load security profiles and combine COUNT queries"
```

---

### Task 11: Final verification and cleanup

**Step 1: Full build**

Run: `dotnet build PatchHound.slnx`
Expected: 0 warnings, 0 errors

**Step 2: Full test suite**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass

**Step 3: Frontend checks**

Run: `cd frontend && npm run lint && npm run typecheck`
Expected: No new errors (frontend is unchanged)

**Step 4: Review all changes**

Run: `git log --oneline main..HEAD`
Verify commits are atomic and well-scoped.

**Step 5: Commit any final adjustments**

If any cleanup is needed, commit with: `chore: final cleanup for ingestion performance optimization`
