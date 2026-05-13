# Ingestion Pipeline Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `IngestionService.cs` (3121 lines) and its collaborators to eliminate magic strings, consolidate in-memory workarounds, split the monolith into focused classes, and fix several correctness bugs — without breaking existing functionality.

**Architecture:** `IngestionService` is decomposed into four focused collaborators (`IngestionLeaseManager`, `IngestionCheckpointWriter`, `IngestionStagingPipeline`, `IngestionSnapshotLifecycle`), reducing it to a pure orchestrator. An `IIngestionBulkWriter` adapter replaces 12 `IsInMemoryProvider()` branches. All checkpoint/status string literals are replaced with typed constants.

**Tech Stack:** .NET 10 / C#, EF Core 9 (PostgreSQL + InMemory), xunit + FluentAssertions + NSubstitute, Testcontainers.PostgreSql

**Branch:** `refactor/ingestion-pipeline`

---

## Progress Tracker

| Phase | Status |
|-------|--------|
| Phase 0: Constants | ☐ Not started |
| Phase 1: Decompose IngestionService | ☐ Not started |
| Phase 2: Collapse constructors + IIngestionBulkWriter | ☐ Not started |
| Phase 3: Critical fixes | ☐ Not started |
| Phase 4: Important fixes | ☐ Not started |
| Phase 5: Minor fixes | ☐ Not started |

---

## File Map

**Create:**
- `src/PatchHound.Infrastructure/Services/CheckpointConstants.cs` — `CheckpointPhases` and `CheckpointStatuses` static classes
- `src/PatchHound.Infrastructure/Services/IngestionLeaseManager.cs` — lease acquire/release/abort, run status updates (~350 lines)
- `src/PatchHound.Infrastructure/Services/IngestionCheckpointWriter.cs` — checkpoint read/commit, artifact cleanup (~150 lines)
- `src/PatchHound.Infrastructure/Services/IngestionStagingPipeline.cs` — asset + vulnerability staging, progress tracking (~600 lines)
- `src/PatchHound.Infrastructure/Services/IngestionSnapshotLifecycle.cs` — snapshot get-or-create, publish, discard, cleanup (~300 lines)
- `src/PatchHound.Infrastructure/Services/IIngestionBulkWriter.cs` — interface for ExecuteUpdate/ExecuteDelete wrappers
- `src/PatchHound.Infrastructure/Services/PostgresIngestionBulkWriter.cs` — real implementation using `ExecuteUpdateAsync`/`ExecuteDeleteAsync`
- `src/PatchHound.Infrastructure/Services/InMemoryIngestionBulkWriter.cs` — in-memory workaround implementation
- `tests/PatchHound.Tests/Infrastructure/Services/IngestionLeaseManagerTests.cs`
- `tests/PatchHound.Tests/Infrastructure/Services/IngestionCheckpointWriterTests.cs`
- `tests/PatchHound.Tests/Infrastructure/Services/IngestionStagingPipelineTests.cs`

**Modify:**
- `src/PatchHound.Infrastructure/Services/IngestionService.cs` — shrink to orchestrator; collapse constructors; remove dead code
- `src/PatchHound.Worker/IngestionWorker.cs` — remove duplicate `IsDue`, fix hardcoded `"Failed"` string
- `src/PatchHound.Infrastructure/AuthenticatedScans/AuthenticatedScanIngestionService.cs` — create real `IngestionRun`, delete staged rows after merge
- `src/PatchHound.Infrastructure/Services/StagedDeviceMergeService.cs` — fix N+1 SELECT (device pre-load + installed software pre-load)
- `src/PatchHound.Infrastructure/Services/ExposureDerivationService.cs` — already bounded; verify
- `src/PatchHound.Infrastructure/Tenants/IngestionScheduleEvaluator.cs` — add overload accepting primitive fields for IngestionWorker use
- `src/PatchHound.Core/Interfaces/IVulnerabilitySource.cs` — remove unused `FetchCanonicalVulnerabilitiesAsync`
- `src/PatchHound.Infrastructure/DependencyInjection.cs` — register new collaborator classes

---

## Phase 0: Extract Checkpoint Constants

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/CheckpointConstants.cs`
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`
- Modify: `src/PatchHound.Worker/IngestionWorker.cs`

### Task 0.1: Create constants file

- [ ] **Step 1: Create `CheckpointConstants.cs`**

```csharp
namespace PatchHound.Infrastructure.Services;

internal static class CheckpointPhases
{
    public const string AssetStaging = "asset-staging";
    public const string AssetMerge = "asset-merge";
    public const string VulnerabilityStaging = "vulnerability-staging";
    public const string VulnerabilityMerge = "vulnerability-merge";
    public const string CloudAppStaging = "cloud-app-staging";
}

internal static class CheckpointStatuses
{
    public const string Running = "Running";
    public const string Staged = "Staged";
    public const string Completed = "Completed";
}
```

- [ ] **Step 2: Replace magic strings in `IngestionService.cs`**

Find all occurrences with: `grep -n '"asset-staging"\|"asset-merge"\|"vulnerability-staging"\|"vulnerability-merge"\|"Completed"\|"Staged"\|"Running"' src/PatchHound.Infrastructure/Services/IngestionService.cs`

Replace each:
- `"asset-staging"` → `CheckpointPhases.AssetStaging` (lines ~253, ~2546)
- `"asset-merge"` → `CheckpointPhases.AssetMerge` (lines ~257, ~2597)
- `"vulnerability-staging"` → `CheckpointPhases.VulnerabilityStaging` (lines ~261, ~1974)
- `"vulnerability-merge"` → `CheckpointPhases.VulnerabilityMerge` (lines ~265, ~2089)
- `item.Status == "Completed"` → `item.Status == CheckpointStatuses.Completed` (line ~1800)
- `status: "Completed"` → `status: CheckpointStatuses.Completed` (all call-sites in CommitCheckpointAsync)
- `status: "Running"` → `status: CheckpointStatuses.Running`
- `status: "Staged"` → `status: CheckpointStatuses.Staged`

- [ ] **Step 3: Build and verify**

```bash
dotnet build PatchHound.slnx
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Run tests**

```bash
dotnet test PatchHound.slnx -v minimal
```
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/CheckpointConstants.cs \
        src/PatchHound.Infrastructure/Services/IngestionService.cs \
        src/PatchHound.Worker/IngestionWorker.cs
git commit -m "refactor: replace checkpoint phase/status magic strings with typed constants"
```

---

## Phase 1: Decompose IngestionService

**Context:** `IngestionService.cs` is 3121 lines. The goal is to extract four collaborators. `IngestionService` becomes an orchestrator that holds references to these collaborators and calls them. Each collaborator takes `PatchHoundDbContext` directly (already scoped). All four are registered as `AddScoped` in DI.

### Task 1.1: Extract `IngestionLeaseManager`

Lease manager owns: `TryAcquireIngestionRunAsync`, `ReleaseIngestionLeaseAsync`, `FinalizeAbortedRunIfPendingAsync`, `UpdateRuntimeStateAsync`, `UpdateIngestionRunStatusAsync`, `CompleteIngestionRunAsync`, `CleanupExpiredIngestionArtifactsAsync`, `ThrowIfAbortRequestedAsync`.

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/IngestionLeaseManager.cs`
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`

- [ ] **Step 1: Write a failing test for `IngestionLeaseManager`**

Create `tests/PatchHound.Tests/Infrastructure/Services/IngestionLeaseManagerTests.cs`:

```csharp
using FluentAssertions;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Infrastructure.Services;

public class IngestionLeaseManagerTests : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public IngestionLeaseManagerTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private IngestionLeaseManager CreateSut() => new(_db);

    [Fact]
    public async Task TryAcquireIngestionRunAsync_WhenNoActiveRun_ReturnsRun()
    {
        var sut = CreateSut();
        var result = await sut.TryAcquireIngestionRunAsync(_tenantId, "defender", CancellationToken.None);
        result.Should().NotBeNull();
        result!.Run.TenantId.Should().Be(_tenantId);
        result.Resumed.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireIngestionRunAsync_WhenAlreadyActive_ReturnsNull()
    {
        var sut = CreateSut();
        var first = await sut.TryAcquireIngestionRunAsync(_tenantId, "defender", CancellationToken.None);
        first.Should().NotBeNull();

        var second = await sut.TryAcquireIngestionRunAsync(_tenantId, "defender", CancellationToken.None);
        second.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test --filter "FullyQualifiedName~IngestionLeaseManagerTests" -v minimal
```
Expected: FAIL — `IngestionLeaseManager` does not exist yet.

- [ ] **Step 3: Create `IngestionLeaseManager.cs`**

Extract the following private methods from `IngestionService.cs` into `IngestionLeaseManager`. The class takes `PatchHoundDbContext` and `ILogger<IngestionLeaseManager>` via constructor.

Methods to move (check exact lines in file before moving):
- `TryAcquireIngestionRunAsync` (lines ~1096–1270)
- `ReleaseIngestionLeaseAsync` (lines ~1698–1739)
- `FinalizeAbortedRunIfPendingAsync` (lines ~1272–1354)
- `UpdateRuntimeStateAsync` (lines ~965–1055)
- `UpdateIngestionRunStatusAsync` (lines ~1073–1094)
- `CompleteIngestionRunAsync` (lines ~1371–1590)
- `CleanupExpiredIngestionArtifactsAsync` (lines ~1592–1696)
- `ThrowIfAbortRequestedAsync` (lines ~1356–1369)
- Private record `AcquiredIngestionRun` (line 41: `sealed record AcquiredIngestionRun(IngestionRun Run, bool Resumed)`)
- Constants: `LeaseDuration`, `IngestionArtifactRetention`, `FailedIngestionRetention`, `MaxPersistenceAttempts`

Skeleton:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class IngestionLeaseManager(
    PatchHoundDbContext dbContext,
    ILogger<IngestionLeaseManager> logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IngestionArtifactRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan FailedIngestionRetention = TimeSpan.FromHours(24);
    private const int MaxPersistenceAttempts = 2;

    public sealed record AcquiredIngestionRun(IngestionRun Run, bool Resumed);

    // Paste extracted methods here — keep signatures identical to IngestionService private methods
    // but change access modifier to `public` for methods called by IngestionService orchestrator.
}
```

- [ ] **Step 4: Update `IngestionService.cs` to delegate to `IngestionLeaseManager`**

Add `_leaseManager` field:
```csharp
private readonly IngestionLeaseManager _leaseManager;
```

In the canonical constructor (line ~135), add `IngestionLeaseManager leaseManager` parameter and assign `_leaseManager = leaseManager`.

Replace every call to the moved private methods with `_leaseManager.MethodName(...)`.

Remove the moved private methods from `IngestionService.cs`.

- [ ] **Step 5: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~IngestionLeaseManagerTests" -v minimal
dotnet test PatchHound.slnx -v minimal
```
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionLeaseManager.cs \
        src/PatchHound.Infrastructure/Services/IngestionService.cs
git commit -m "refactor: extract IngestionLeaseManager from IngestionService"
```

---

### Task 1.2: Extract `IngestionCheckpointWriter`

Checkpoint writer owns: `IsCheckpointCompletedAsync`, `GetCheckpointBatchNumberAsync`, `CommitCheckpointAsync`, `ClearStagedDataForRunAsync`.

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/IngestionCheckpointWriter.cs`
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`

- [ ] **Step 1: Write a failing test**

Create `tests/PatchHound.Tests/Infrastructure/Services/IngestionCheckpointWriterTests.cs`:

```csharp
using FluentAssertions;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Infrastructure.Services;

public class IngestionCheckpointWriterTests : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public IngestionCheckpointWriterTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private IngestionCheckpointWriter CreateSut() => new(_db);

    [Fact]
    public async Task IsCheckpointCompletedAsync_BeforeCommit_ReturnsFalse()
    {
        var sut = CreateSut();
        var result = await sut.IsCheckpointCompletedAsync(Guid.NewGuid(), CheckpointPhases.AssetStaging, CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CommitCheckpointAsync_ThenIsCompleted_ReturnsTrue()
    {
        var runId = Guid.NewGuid();
        var sut = CreateSut();
        await sut.CommitCheckpointAsync(runId, _tenantId, "defender", CheckpointPhases.AssetStaging, 1, null, 10, CheckpointStatuses.Completed, CancellationToken.None);
        var result = await sut.IsCheckpointCompletedAsync(runId, CheckpointPhases.AssetStaging, CancellationToken.None);
        result.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify failure**

```bash
dotnet test --filter "FullyQualifiedName~IngestionCheckpointWriterTests" -v minimal
```
Expected: FAIL.

- [ ] **Step 3: Create `IngestionCheckpointWriter.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class IngestionCheckpointWriter(PatchHoundDbContext dbContext)
{
    public async Task<bool> IsCheckpointCompletedAsync(Guid ingestionRunId, string phase, CancellationToken ct) { ... }
    public async Task<int> GetCheckpointBatchNumberAsync(Guid ingestionRunId, string phase, CancellationToken ct) { ... }
    public async Task CommitCheckpointAsync(Guid ingestionRunId, Guid tenantId, string sourceKey, string phase, int batchNumber, string? cursorJson, int recordsCommitted, string status, CancellationToken ct) { ... }
    public async Task ClearStagedDataForRunAsync(Guid ingestionRunId, CancellationToken ct) { ... }
}
```

Paste extracted implementations from `IngestionService.cs` lines ~1788–1860 and ~1741–1786.

- [ ] **Step 4: Update `IngestionService.cs` to delegate**

Add `_checkpointWriter` field + constructor param. Replace calls to these methods with `_checkpointWriter.MethodName(...)`. Remove moved methods.

- [ ] **Step 5: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~IngestionCheckpointWriterTests" -v minimal
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionCheckpointWriter.cs \
        src/PatchHound.Infrastructure/Services/IngestionService.cs \
        tests/PatchHound.Tests/Infrastructure/Services/IngestionCheckpointWriterTests.cs
git commit -m "refactor: extract IngestionCheckpointWriter from IngestionService"
```

---

### Task 1.3: Extract `IngestionStagingPipeline`

Staging pipeline owns: `StageAssetInventorySnapshotAsync`, `StageAssetBatchesAsync`, `ProcessAssetsAsync` (public internal), `StageVulnerabilitiesAsync`, `StageVulnerabilityBatchesAsync`, `EnqueueEnrichmentJobsForRunAsync`, `NormalizeResults`, `Chunk<T>`.

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/IngestionStagingPipeline.cs`
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`

- [ ] **Step 1: Write failing test**

Create `tests/PatchHound.Tests/Infrastructure/Services/IngestionStagingPipelineTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Infrastructure.Services;

public class IngestionStagingPipelineTests : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public IngestionStagingPipelineTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task StageVulnerabilitiesAsync_EmptyResults_StagedCountIsZero()
    {
        var pipeline = new IngestionStagingPipeline(_db, Substitute.For<EnrichmentJobEnqueuer>());
        var summary = await pipeline.StageVulnerabilitiesAsync(
            Guid.NewGuid(), _tenantId, "defender", [], 0, CancellationToken.None);
        summary.StagedCount.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

```bash
dotnet test --filter "FullyQualifiedName~IngestionStagingPipelineTests" -v minimal
```

- [ ] **Step 3: Create `IngestionStagingPipeline.cs`**

Extract from `IngestionService.cs`:
- `StageVulnerabilitiesAsync` (lines ~1862–1912)
- `StageVulnerabilityBatchesAsync` (lines ~1914–1994)
- `StageAssetInventorySnapshotAsync` (lines ~1996–2051)
- `StageAssetBatchesAsync` (lines ~2446–2565)
- `ProcessAssetsAsync` (lines ~2402–2444) — keep `internal` modifier
- `EnqueueEnrichmentJobsForRunAsync` (lines ~2053–2105)
- `NormalizeResults` (lines ~2859–2875)
- `Chunk<T>` (lines ~2956–2969)
- Constants `AssetBatchSize = 200`, `VulnerabilityBatchSize = 250`

Return types for staging methods need to expose the summary records. Move `AssetBatchStageSummary` (line ~927) here as well.

Constructor:
```csharp
public class IngestionStagingPipeline(
    PatchHoundDbContext dbContext,
    EnrichmentJobEnqueuer enrichmentJobEnqueuer,
    ILogger<IngestionStagingPipeline> logger)
```

- [ ] **Step 4: Update `IngestionService.cs` to delegate**

Add `_stagingPipeline` field. Wire constructor param. Replace all calls with `_stagingPipeline.MethodName(...)`. Remove moved methods.

- [ ] **Step 5: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~IngestionStagingPipelineTests" -v minimal
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionStagingPipeline.cs \
        src/PatchHound.Infrastructure/Services/IngestionService.cs \
        tests/PatchHound.Tests/Infrastructure/Services/IngestionStagingPipelineTests.cs
git commit -m "refactor: extract IngestionStagingPipeline from IngestionService"
```

---

### Task 1.4: Extract `IngestionSnapshotLifecycle`

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/IngestionSnapshotLifecycle.cs`
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`

- [ ] **Step 1: Create `IngestionSnapshotLifecycle.cs`**

Extract from `IngestionService.cs`:
- `SupportsSoftwareSnapshots` (line ~2671–2674) — static method
- `GetOrCreateBuildingSoftwareSnapshotAsync` (lines ~2676–2723)
- `PublishSnapshotAsync` (lines ~2724–2764)
- `DiscardBuildingSnapshotAsync` (lines ~2798–2826)
- `CleanupSnapshotDataAsync` (lines ~2830–2857)
- `RekeyTenantSoftwareReferencesAsync` — **do NOT extract**; this is removed in Phase 3

Constructor:
```csharp
public class IngestionSnapshotLifecycle(
    PatchHoundDbContext dbContext,
    ILogger<IngestionSnapshotLifecycle> logger)
```

In `PublishSnapshotAsync`, remove the call to `RekeyTenantSoftwareReferencesAsync` (it's dead code; the comment explains why — move the comment inline to the method body).

- [ ] **Step 2: Update `IngestionService.cs` to delegate**

Add `_snapshotLifecycle` field. Replace calls. Remove moved methods. Remove `RekeyTenantSoftwareReferencesAsync` entirely.

- [ ] **Step 3: Build and test**

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionSnapshotLifecycle.cs \
        src/PatchHound.Infrastructure/Services/IngestionService.cs
git commit -m "refactor: extract IngestionSnapshotLifecycle and remove dead RekeyTenantSoftwareReferences"
```

---

### Task 1.5: Register collaborators in DI

**Files:**
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Add registrations**

In `DependencyInjection.cs`, near line ~208 where `IngestionService` is registered, add:

```csharp
services.AddScoped<IngestionLeaseManager>();
services.AddScoped<IngestionCheckpointWriter>();
services.AddScoped<IngestionStagingPipeline>();
services.AddScoped<IngestionSnapshotLifecycle>();
```

- [ ] **Step 2: Update `IngestionService`'s canonical constructor**

The canonical constructor (was line ~135) now also takes the four collaborators as parameters. The shorter overload constructors (lines ~43, ~73, ~104) still need to chain to it — add `new IngestionLeaseManager(...)` instantiation OR switch all overloads to pass them via DI (see Phase 2 for full collapse). For now, just wire the canonical constructor and accept that test overloads still work via the shorter constructors which manually instantiate the collaborators.

The test constructors in `IngestionServicePhase3Tests.cs` use the short overload that doesn't take `VulnerabilityResolver`/`NormalizedSoftwareProjectionService?`/`RemediationDecisionService?`. They will need to pass through the collaborators. Check what the test helper `CreateSut()` looks like and update accordingly.

- [ ] **Step 3: Build and test**

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/DependencyInjection.cs \
        src/PatchHound.Infrastructure/Services/IngestionService.cs
git commit -m "refactor: register IngestionService collaborators in DI"
```

---

## Phase 2: Collapse Constructors and IIngestionBulkWriter

### Task 2.1: Introduce `IIngestionBulkWriter`

The 12 `IsInMemoryProvider()` branches all follow the same pattern: either use `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (PostgreSQL) or load-mutate-save (InMemory). An interface adapter hides this.

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/IIngestionBulkWriter.cs`
- Create: `src/PatchHound.Infrastructure/Services/PostgresIngestionBulkWriter.cs`
- Create: `src/PatchHound.Infrastructure/Services/InMemoryIngestionBulkWriter.cs`
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs` (and all collaborators that have `IsInMemoryProvider()` branches)
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Define `IIngestionBulkWriter`**

```csharp
namespace PatchHound.Infrastructure.Services;

/// <summary>
/// Wraps bulk EF Core operations that differ between PostgreSQL and the InMemory test provider.
/// Registered as the concrete implementation for the active DB provider.
/// </summary>
public interface IIngestionBulkWriter
{
    Task ExecuteUpdateIngestionRunAsync(Guid runId, Action<IngestionRun> mutate, CancellationToken ct);
    Task ExecuteUpdateTenantSourceAsync(Guid tenantId, string sourceKey, Guid runId, Action<TenantSourceConfiguration> mutate, CancellationToken ct);
    Task DeleteStagedDevicesForRunAsync(Guid ingestionRunId, CancellationToken ct);
    Task DeleteStagedVulnerabilitiesForRunAsync(Guid ingestionRunId, CancellationToken ct);
    Task DeleteStagedExposuresForRunAsync(Guid ingestionRunId, CancellationToken ct);
    Task DeleteStagedInstallationsForRunAsync(Guid ingestionRunId, CancellationToken ct);
    Task DeleteSnapshotInstallationsAsync(Guid snapshotId, CancellationToken ct);
    Task DeleteSnapshotTenantRecordsAsync(Guid snapshotId, CancellationToken ct);
    Task DeleteExpiredIngestionRunsAsync(DateTimeOffset expiredBefore, DateTimeOffset failedExpiredBefore, CancellationToken ct);
}
```

- [ ] **Step 2: Implement `PostgresIngestionBulkWriter`**

Uses `ExecuteUpdateAsync`/`ExecuteDeleteAsync` directly:

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class PostgresIngestionBulkWriter(PatchHoundDbContext dbContext) : IIngestionBulkWriter
{
    public async Task ExecuteUpdateIngestionRunAsync(Guid runId, Action<IngestionRun> mutate, CancellationToken ct)
    {
        var run = await dbContext.IngestionRuns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return;
        mutate(run);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteStagedDevicesForRunAsync(Guid ingestionRunId, CancellationToken ct)
    {
        await dbContext.StagedDevices.IgnoreQueryFilters()
            .Where(d => d.IngestionRunId == ingestionRunId)
            .ExecuteDeleteAsync(ct);
    }

    // ... implement all interface members — see existing IsInMemoryProvider branches in IngestionService.cs for the PostgreSQL implementations
}
```

- [ ] **Step 3: Implement `InMemoryIngestionBulkWriter`**

Uses load + mutate + SaveChanges pattern (copies from the InMemory branches in IngestionService):

```csharp
public class InMemoryIngestionBulkWriter(PatchHoundDbContext dbContext) : IIngestionBulkWriter
{
    // Load entities, mutate in memory, call SaveChangesAsync — exact copies of InMemory branches
}
```

- [ ] **Step 4: Register in DI**

In `DependencyInjection.cs`, after building the `ServiceProvider`, register the correct implementation based on the connection string / provider. Since provider is not known at registration time via attribute, use a factory:

```csharp
services.AddScoped<IIngestionBulkWriter>(sp =>
{
    var db = sp.GetRequiredService<PatchHoundDbContext>();
    return db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
        ? new InMemoryIngestionBulkWriter(db)
        : new PostgresIngestionBulkWriter(db);
});
```

- [ ] **Step 5: Replace `IsInMemoryProvider()` branches in all collaborators**

For each `IsInMemoryProvider()` block in `IngestionLeaseManager`, `IngestionCheckpointWriter`, `IngestionStagingPipeline`, `IngestionSnapshotLifecycle`, and `IngestionService`:

Replace:
```csharp
if (IsInMemoryProvider())
{
    // load-mutate-save
    return;
}
// ExecuteUpdateAsync / ExecuteDeleteAsync
```

With:
```csharp
await _bulkWriter.DeleteStagedDevicesForRunAsync(runId, ct); // (or appropriate method)
```

Each collaborator gets `IIngestionBulkWriter` injected via constructor.

Remove `IsInMemoryProvider()` method from `IngestionService` after all calls are replaced.

- [ ] **Step 6: Build and test**

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IIngestionBulkWriter.cs \
        src/PatchHound.Infrastructure/Services/PostgresIngestionBulkWriter.cs \
        src/PatchHound.Infrastructure/Services/InMemoryIngestionBulkWriter.cs \
        src/PatchHound.Infrastructure/Services/IngestionService.cs \
        src/PatchHound.Infrastructure/Services/IngestionLeaseManager.cs \
        src/PatchHound.Infrastructure/Services/IngestionCheckpointWriter.cs \
        src/PatchHound.Infrastructure/Services/IngestionStagingPipeline.cs \
        src/PatchHound.Infrastructure/Services/IngestionSnapshotLifecycle.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs
git commit -m "refactor: introduce IIngestionBulkWriter to eliminate IsInMemoryProvider branches"
```

---

### Task 2.2: Collapse IngestionService constructors

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`

Currently there are 4 constructor overloads (lines 43, 73, 104, 135). The canonical (line 135) is the real one; others chain to it with defaulted parameters. After Phase 1, the canonical constructor also takes the 4 collaborators. All the chaining constructors need `[ActivatorUtilitiesConstructor]` is not the right fix — the right fix is to add DI-friendly default values or collapse to one constructor.

Strategy: Keep the single canonical constructor and mark it `[ActivatorUtilitiesConstructor]`. The test-only overloads (3 shorter ones) that exist for test convenience should either be kept as `private` constructors chaining to the canonical, or test helpers should be updated to use the full constructor.

- [ ] **Step 1: Read the existing test helpers**

```bash
grep -rn "new IngestionService(" tests/
```

Note which constructor overloads are used in tests. The tests in `IngestionServicePhase3Tests.cs` likely use short overloads.

- [ ] **Step 2: Update test helpers to use the canonical constructor**

In `tests/PatchHound.Tests/Infrastructure/Services/IngestionServicePhase3Tests.cs`, find the `CreateSut()` or `new IngestionService(...)` calls and add the 4 collaborators:

```csharp
private IngestionService CreateSut() => new(
    _db,
    sources: [],
    enrichmentJobEnqueuer: new EnrichmentJobEnqueuer(_db),
    stagedDeviceMergeService: Substitute.For<IStagedDeviceMergeService>(),
    stagedCloudApplicationMergeService: Substitute.For<IStagedCloudApplicationMergeService>(),
    deviceRuleEvaluationService: Substitute.For<IDeviceRuleEvaluationService>(),
    exposureDerivationService: new ExposureDerivationService(_db),
    exposureEpisodeService: new ExposureEpisodeService(_db),
    exposureAssessmentService: Substitute.For<ExposureAssessmentService>(),
    riskScoreService: Substitute.For<RiskScoreService>(),
    vulnerabilityResolver: new VulnerabilityResolver(_db, NullLogger<VulnerabilityResolver>.Instance),
    normalizedSoftwareProjectionService: null,
    remediationDecisionService: null,
    leaseManager: new IngestionLeaseManager(_db, NullLogger<IngestionLeaseManager>.Instance),
    checkpointWriter: new IngestionCheckpointWriter(_db),
    stagingPipeline: new IngestionStagingPipeline(_db, new EnrichmentJobEnqueuer(_db), NullLogger<IngestionStagingPipeline>.Instance),
    snapshotLifecycle: new IngestionSnapshotLifecycle(_db, NullLogger<IngestionSnapshotLifecycle>.Instance),
    bulkWriter: new InMemoryIngestionBulkWriter(_db),
    logger: NullLogger<IngestionService>.Instance
);
```

- [ ] **Step 3: Remove the 3 shorter constructor overloads**

Delete the constructor overloads at lines ~43, ~73, ~104. Keep only the canonical constructor (was line ~135), now expanded. Add `[ActivatorUtilitiesConstructor]` attribute (from `Microsoft.Extensions.DependencyInjection`) to mark it as the DI constructor.

```csharp
[ActivatorUtilitiesConstructor]
public IngestionService(
    PatchHoundDbContext dbContext,
    IEnumerable<IIngestionSource> sources,
    EnrichmentJobEnqueuer enrichmentJobEnqueuer,
    IStagedDeviceMergeService stagedDeviceMergeService,
    IStagedCloudApplicationMergeService stagedCloudApplicationMergeService,
    IDeviceRuleEvaluationService deviceRuleEvaluationService,
    ExposureDerivationService exposureDerivationService,
    ExposureEpisodeService exposureEpisodeService,
    ExposureAssessmentService exposureAssessmentService,
    RiskScoreService riskScoreService,
    VulnerabilityResolver vulnerabilityResolver,
    NormalizedSoftwareProjectionService? normalizedSoftwareProjectionService,
    RemediationDecisionService? remediationDecisionService,
    IngestionLeaseManager leaseManager,
    IngestionCheckpointWriter checkpointWriter,
    IngestionStagingPipeline stagingPipeline,
    IngestionSnapshotLifecycle snapshotLifecycle,
    IIngestionBulkWriter bulkWriter,
    ILogger<IngestionService> logger)
{ ... }
```

- [ ] **Step 4: Build and test**

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionService.cs \
        tests/PatchHound.Tests/Infrastructure/Services/IngestionServicePhase3Tests.cs
git commit -m "refactor: collapse IngestionService to single canonical constructor"
```

---

## Phase 3: Critical Fixes

### Task 3.1: Remove dead `RekeyTenantSoftwareReferencesAsync` code

*(This was already done in Task 1.4 as part of extraction. Verify it's gone.)*

- [ ] **Step 1: Verify removal**

```bash
grep -n "RekeyTenantSoftwareReferences" src/PatchHound.Infrastructure/Services/IngestionSnapshotLifecycle.cs
grep -n "RekeyTenantSoftwareReferences" src/PatchHound.Infrastructure/Services/IngestionService.cs
```

Expected: no matches.

- [ ] **Step 2: Verify `PublishSnapshotAsync` has the inline comment**

Read `IngestionSnapshotLifecycle.cs` `PublishSnapshotAsync` method. It should contain:

```csharp
// RemediationCase is keyed by (TenantId, SoftwareProductId) and stable across snapshot
// rotations — no re-keying of downstream entities is required when the active snapshot changes.
```

If the comment is missing, add it.

---

### Task 3.2: Document `IngestionStateCache` scoped lifetime

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionStateCache.cs`

- [ ] **Step 1: Read `IngestionStateCache.cs`**

```bash
cat -n src/PatchHound.Infrastructure/Services/IngestionStateCache.cs
```

- [ ] **Step 2: Add XML doc comment to the class**

```csharp
/// <summary>
/// Scoped per-request ingestion state. Registered as AddScoped — one instance per DI scope,
/// not a singleton. Each IngestionService scope gets its own clean instance.
/// </summary>
public class IngestionStateCache
```

- [ ] **Step 3: Build and commit**

```bash
dotnet build PatchHound.slnx
git add src/PatchHound.Infrastructure/Services/IngestionStateCache.cs
git commit -m "docs: clarify IngestionStateCache scoped (not singleton) registration"
```

---

## Phase 4: Important Fixes

### Task 4.1: Delete `IngestionWorker.IsDue` — delegate to `IngestionScheduleEvaluator`

**Problem:** `IngestionWorker` has a private static `IsDue(ScheduledSource, DateTimeOffset)` (lines ~250–280) that duplicates `IngestionScheduleEvaluator.IsDue` but is MISSING the active-run guard check (`lastCompletedAt < lastStartedAt`). This means the worker can double-start a source that is still running.

**Files:**
- Modify: `src/PatchHound.Infrastructure/Tenants/IngestionScheduleEvaluator.cs`
- Modify: `src/PatchHound.Worker/IngestionWorker.cs`

- [ ] **Step 1: Write a failing test**

Add to `tests/PatchHound.Tests/Worker/IngestionWorkerTests.cs`:

```csharp
[Fact]
public void IsDue_WhenLastStartedAfterLastCompleted_ReturnsFalse()
{
    // active run guard: started > completed means currently running
    var source = new TenantSourceConfiguration(/* ... with appropriate values */);
    var now = DateTimeOffset.UtcNow;
    source.UpdateRuntime(null, now.AddMinutes(-1), now.AddMinutes(-10), now.AddMinutes(-10), "Succeeded", null);
    // lastStartedAt (now-1min) > lastCompletedAt (now-10min) → run is in progress
    var result = IngestionScheduleEvaluator.IsDue(source, now);
    result.Should().BeFalse();
}
```

Run: `dotnet test --filter "FullyQualifiedName~IngestionWorkerTests" -v minimal`

Note: if `TenantSourceConfiguration` is hard to construct outside infra tests, write this as an `IngestionScheduleEvaluatorTests` in the infra test project.

- [ ] **Step 2: Verify `IngestionScheduleEvaluator.IsDue` already has the active-run guard**

Read `src/PatchHound.Infrastructure/Tenants/IngestionScheduleEvaluator.cs` fully. The current implementation checks `source.LastStartedAt > source.LastCompletedAt` (active run guard). The worker's private `IsDue` does NOT.

- [ ] **Step 3: Add a primitive-field overload to `IngestionScheduleEvaluator`**

`IngestionWorker` builds `ScheduledSource` records from the DB, so it can pass individual fields:

```csharp
public static bool IsDue(
    string sourceKey,
    bool enabled,
    string? syncSchedule,
    DateTimeOffset? lastStartedAt,
    DateTimeOffset? lastCompletedAt,
    DateTimeOffset nowUtc)
{
    if (!enabled) return false;
    if (string.IsNullOrWhiteSpace(syncSchedule)) return false;
    if (!TenantSourceCatalog.SupportsScheduling(sourceKey)) return false;

    // Active run guard — same as entity overload
    if (lastStartedAt.HasValue && lastCompletedAt.HasValue && lastStartedAt > lastCompletedAt)
        return false;

    var lastRun = lastCompletedAt ?? (lastStartedAt.HasValue ? lastStartedAt.Value.AddYears(-1) : nowUtc.AddYears(-1));
    return CronExpression.Parse(syncSchedule).GetNextOccurrence(lastRun.UtcDateTime, TimeZoneInfo.Utc) <= nowUtc.UtcDateTime;
}
```

(Check `IngestionScheduleEvaluator.cs` to see what cron library is used — use the same one.)

- [ ] **Step 4: Update `IngestionWorker` to call `IngestionScheduleEvaluator.IsDue`**

In `IngestionWorker.cs`, find private static `IsDue(ScheduledSource source, DateTimeOffset nowUtc)` (lines ~250–280). Delete it. Find each call site and replace with:

```csharp
IngestionScheduleEvaluator.IsDue(
    source.SourceKey,
    source.Enabled,
    source.SyncSchedule,
    source.LastStartedAt,
    source.LastCompletedAt,
    nowUtc)
```

Delete the private `ScheduledSource` record if `SourceKey`, `Enabled`, etc. are its fields — or keep it if it's used for grouping, but the `IsDue` logic must go.

- [ ] **Step 5: Run tests**

```bash
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Tenants/IngestionScheduleEvaluator.cs \
        src/PatchHound.Worker/IngestionWorker.cs \
        tests/PatchHound.Tests/Worker/IngestionWorkerTests.cs
git commit -m "fix: remove duplicate IngestionWorker.IsDue, delegate to IngestionScheduleEvaluator with active-run guard"
```

---

### Task 4.2: Call `ClearStagedDataForRunAsync` on failure

**Problem:** `CompleteIngestionRunAsync` only calls `ClearStagedDataForRunAsync` on success (lines ~1450–1452 and ~1573–1575 in original file). Failed runs leave staged rows until `CleanupExpiredIngestionArtifactsAsync` runs (24h). This inflates storage and confuses monitoring.

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionLeaseManager.cs` (after Phase 1) OR `IngestionService.cs` (before Phase 1)

- [ ] **Step 1: Write a failing test**

Add to `IngestionLeaseManagerTests.cs` (or a new `IngestionServiceCleanupTests.cs`):

```csharp
[Fact]
public async Task CompleteIngestionRunAsync_OnFailure_ClearsStagedData()
{
    // Arrange: create an IngestionRun and add a StagedDevice for it
    var sut = CreateSut(); // or use full IngestionService
    var acquired = await sut.TryAcquireIngestionRunAsync(_tenantId, "defender", CancellationToken.None);
    var stagedDevice = new StagedDevice { IngestionRunId = acquired!.Run.Id, TenantId = _tenantId, /* minimal fields */ };
    _db.StagedDevices.Add(stagedDevice);
    await _db.SaveChangesAsync();

    // Act: complete as failure
    await sut.CompleteIngestionRunAsync(acquired.Run.Id, _tenantId, "defender", succeeded: false, error: "test", /* ... */, CancellationToken.None);

    // Assert: staged device is gone
    var remaining = await _db.StagedDevices.IgnoreQueryFilters()
        .Where(d => d.IngestionRunId == acquired.Run.Id).CountAsync();
    remaining.Should().Be(0);
}
```

- [ ] **Step 2: Fix `CompleteIngestionRunAsync`**

In the failure branch (both InMemory and PostgreSQL paths), add a call to `ClearStagedDataForRunAsync` after writing the final run status:

```csharp
// After: await _dbContext.SaveChangesAsync(ct); (failure branch)
await ClearStagedDataForRunAsync(runId, ct);
```

Do this in both the InMemory branch (currently ~line 1450) and the PostgreSQL `ExecuteUpdate` path (currently ~line 1573).

After Phase 1, this will be in `IngestionLeaseManager` and `IngestionCheckpointWriter` respectively.

- [ ] **Step 3: Run tests**

```bash
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionLeaseManager.cs
git commit -m "fix: clear staged data for failed ingestion runs, not just successful ones"
```

---

### Task 4.3: Fix `AuthenticatedScanIngestionService` orphaned staging rows

**Problem:** `AuthenticatedScanIngestionService` at line ~50 creates `var ingestionRunId = Guid.NewGuid()` without creating an `IngestionRun` entity. Staged rows reference this fake ID and are never cleaned up by `CleanupExpiredIngestionArtifactsAsync` (which scans real `IngestionRun` rows for expiry).

**Files:**
- Modify: `src/PatchHound.Infrastructure/AuthenticatedScans/AuthenticatedScanIngestionService.cs`

- [ ] **Step 1: Read the full file**

```bash
cat -n src/PatchHound.Infrastructure/AuthenticatedScans/AuthenticatedScanIngestionService.cs
```

- [ ] **Step 2: Write a failing test**

Add to a new file `tests/PatchHound.Tests/Infrastructure/AuthenticatedScanIngestionServiceTests.cs`:

```csharp
[Fact]
public async Task IngestAsync_AfterMerge_StagedRowsAreDeleted()
{
    // Arrange
    var svc = CreateSut();
    var scan = BuildTestScanResult();

    // Act
    await svc.IngestAsync(_tenantId, scan, CancellationToken.None);

    // Assert: no staged rows remain for this tenant
    var staged = await _db.StagedDevices.IgnoreQueryFilters()
        .Where(d => d.TenantId == _tenantId).CountAsync();
    staged.Should().Be(0);
}
```

- [ ] **Step 3: Fix `AuthenticatedScanIngestionService`**

After the call to `stagedDeviceMergeService.MergeAsync(...)` and `projectionService.SyncTenantAsync(...)`, delete the staged rows:

```csharp
await dbContext.StagedDevices.IgnoreQueryFilters()
    .Where(d => d.IngestionRunId == ingestionRunId)
    .ExecuteDeleteAsync(ct);
await dbContext.StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
    .Where(i => i.IngestionRunId == ingestionRunId)
    .ExecuteDeleteAsync(ct);
```

Use `IIngestionBulkWriter` if available by this phase; otherwise call `ExecuteDeleteAsync` directly (it's not used in tests, so no in-memory issue here — authenticated scans always run with real Postgres in production).

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~AuthenticatedScanIngestionServiceTests" -v minimal
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/AuthenticatedScans/AuthenticatedScanIngestionService.cs \
        tests/PatchHound.Tests/Infrastructure/AuthenticatedScanIngestionServiceTests.cs
git commit -m "fix: delete staged rows after AuthenticatedScanIngestionService merge to prevent orphans"
```

---

### Task 4.4: Fix `StagedDeviceMergeService` N+1 SELECT

**Problem:** In `StagedDeviceMergeService.cs`:
1. Line ~107–115: `db.Devices.FirstOrDefaultAsync(...)` inside `foreach (var stagedDevice in stagedDevices)` — N+1
2. Line ~226–236: `db.InstalledSoftware.FirstOrDefaultAsync(...)` inside software link loop — N+1

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/StagedDeviceMergeService.cs`

- [ ] **Step 1: Read the relevant sections**

```bash
sed -n '100,250p' src/PatchHound.Infrastructure/Services/StagedDeviceMergeService.cs
```

- [ ] **Step 2: Write a failing test (performance assertion)**

Add to `StagedDeviceMergeServiceTests.cs` — not a SQL query-count test (that's brittle), but a correctness test with multiple staged devices that exercises the code path:

```csharp
[Fact]
public async Task MergeAsync_TwoStagedDevices_BothMerged()
{
    // Arrange: seed 2 staged devices
    var runId = Guid.NewGuid();
    _db.StagedDevices.AddRange(
        BuildStagedDevice(runId, "device-001"),
        BuildStagedDevice(runId, "device-002"));
    await _db.SaveChangesAsync();

    // Act
    var sut = CreateSut();
    await sut.MergeAsync(_tenantId, runId, CancellationToken.None);

    // Assert
    var deviceCount = await _db.Devices.Where(d => d.TenantId == _tenantId).CountAsync();
    deviceCount.Should().Be(2);
}
```

- [ ] **Step 3: Pre-load devices before the loop**

Before the `foreach (var stagedDevice in stagedDevices)` loop, add:

```csharp
var stagedExternalIds = stagedDevices.Select(d => d.ExternalId).Distinct().ToList();
var existingDevices = await db.Devices
    .Where(d => d.TenantId == tenantId && stagedExternalIds.Contains(d.ExternalId))
    .ToDictionaryAsync(d => d.ExternalId, d => d, ct);
```

Replace `await db.Devices.FirstOrDefaultAsync(d => d.ExternalId == staged.ExternalId && d.TenantId == tenantId, ct)` with `existingDevices.TryGetValue(staged.ExternalId, out var existing) ? existing : null`.

After the loop, add newly created devices to `existingDevices` so later iterations find them (for duplicate staged entries with the same external ID).

- [ ] **Step 4: Pre-load InstalledSoftware before the software link loop**

After all devices are processed and saved, build a dictionary of installed software keyed by (DeviceId, CanonicalProductKey):

```csharp
var mergedDeviceIds = existingDevices.Values.Select(d => d.Id).ToList();
var installedSoftwareByKey = await db.InstalledSoftware
    .Where(i => i.TenantId == tenantId && mergedDeviceIds.Contains(i.DeviceId))
    .ToDictionaryAsync(i => (i.DeviceId, i.SoftwareProduct.CanonicalProductKey), i => i, ct);
```

Replace the `FirstOrDefaultAsync` inside the software link loop with a dictionary lookup.

- [ ] **Step 5: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~StagedDeviceMergeService" -v minimal
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/StagedDeviceMergeService.cs
git commit -m "fix: eliminate N+1 SELECT in StagedDeviceMergeService device and software lookups"
```

---

### Task 4.5: Bound `ProcessStagedResultsAsync` exposure load

**Problem:** Line ~2225–2228 in original `IngestionService.cs` (now in `IngestionStagingPipeline` after Phase 1):

```csharp
var existing = await _dbContext.DeviceVulnerabilityExposures
    .Where(e => e.TenantId == tenantId)
    .ToListAsync(ct);
```

Loads ALL exposures for the tenant. Should be bounded to the device IDs in the current run's staged exposures.

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionStagingPipeline.cs` (or `IngestionService.cs` before Phase 1)

- [ ] **Step 1: Bound the load**

After building `deviceIdByExternalId` (the dict mapping external IDs to Device GUIDs, already loaded in Step 2 of the method), bound the query:

```csharp
// Only load exposures for devices in this run — not all tenant exposures
var runDeviceIds = deviceIdByExternalId.Values.ToList();
var existing = await _dbContext.DeviceVulnerabilityExposures
    .Where(e => e.TenantId == tenantId && runDeviceIds.Contains(e.DeviceId))
    .ToListAsync(ct);
```

This is semantically correct: `ProcessStagedResultsAsync` only receives staged exposures for devices staged in this run. Exposures for other devices are not touched.

- [ ] **Step 2: Run tests**

```bash
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionStagingPipeline.cs
git commit -m "fix: bound ProcessStagedResultsAsync exposure load to current run's device IDs"
```

---

## Phase 5: Minor Fixes

### Task 5.1: Seal `IngestionTerminalException`

- [ ] **Step 1: Find and seal**

```bash
grep -rn "class IngestionTerminalException\|class IngestionAbortedException" src/
```

- [ ] **Step 2: Add `sealed` modifier**

```csharp
public sealed class IngestionTerminalException : Exception { ... }
public sealed class IngestionAbortedException : Exception { ... }
```

- [ ] **Step 3: Build and commit**

```bash
dotnet build PatchHound.slnx
git add <file(s)>
git commit -m "fix: seal IngestionTerminalException and IngestionAbortedException"
```

---

### Task 5.2: Fix hardcoded `"Failed"` in `IngestionWorker`

**Files:**
- Modify: `src/PatchHound.Worker/IngestionWorker.cs`

- [ ] **Step 1: Find and replace**

In `IngestionWorker.cs` line ~169:
```csharp
.SetProperty(item => item.LastStatus, "Failed")
```

Replace with:
```csharp
.SetProperty(item => item.LastStatus, IngestionRunStatuses.Failed)
```

Verify `IngestionRunStatuses.Failed` exists:
```bash
grep -rn "IngestionRunStatuses" src/PatchHound.Core/
grep -rn "IngestionRunStatuses" src/PatchHound.Infrastructure/
```

Add the required using if needed.

- [ ] **Step 2: Build and commit**

```bash
dotnet build PatchHound.slnx
git add src/PatchHound.Worker/IngestionWorker.cs
git commit -m "fix: use IngestionRunStatuses.Failed constant in IngestionWorker"
```

---

### Task 5.3: `RefreshDeviceActivityForTenantAsync` → `ExecuteUpdateAsync`

**Problem:** Lines ~904–925 load all tenant devices into memory to set `IsActive` flag. Should use `ExecuteUpdateAsync`.

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs` (stays in orchestrator or moves to a utility)

- [ ] **Step 1: Rewrite with `ExecuteUpdateAsync`**

```csharp
private async Task<int> RefreshDeviceActivityForTenantAsync(Guid tenantId, CancellationToken ct)
{
    var cutoff = DateTimeOffset.UtcNow.Subtract(DeviceInactiveThreshold);

    var deactivatedCount = await _dbContext.Devices
        .IgnoreQueryFilters()
        .Where(d => d.TenantId == tenantId && d.IsActive && (d.LastSeenAt == null || d.LastSeenAt < cutoff))
        .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, false), ct);

    await _dbContext.Devices
        .IgnoreQueryFilters()
        .Where(d => d.TenantId == tenantId && !d.IsActive && d.LastSeenAt != null && d.LastSeenAt >= cutoff)
        .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, true), ct);

    return deactivatedCount;
}
```

Note: This changes the return value to only count newly-deactivated devices. Verify the caller just logs this count; the semantic is the same.

- [ ] **Step 2: Handle in-memory provider**

If `IIngestionBulkWriter` is implemented by Phase 5, add `RefreshDeviceActivityAsync` to the interface. Otherwise, keep the old load-mutate-save as the InMemory path in an `if (IsInMemoryProvider())` guard (acceptable for a minor fix, full elimination comes from Phase 2).

- [ ] **Step 3: Build and test**

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionService.cs
git commit -m "perf: rewrite RefreshDeviceActivityForTenantAsync to use ExecuteUpdateAsync"
```

---

### Task 5.4: Remove `FetchCanonicalVulnerabilitiesAsync` from `IVulnerabilitySource`

**Problem:** `IVulnerabilitySource` declares `FetchCanonicalVulnerabilitiesAsync` which is never called from the ingestion pipeline. Only `DefenderVulnerabilitySource` implements it. Removing it from the interface decouples the abstraction from this dead call path.

**Files:**
- Modify: `src/PatchHound.Core/Interfaces/IVulnerabilitySource.cs`
- Modify: all implementors (verify with grep)

- [ ] **Step 1: Verify no callers**

```bash
grep -rn "FetchCanonicalVulnerabilitiesAsync" src/ tests/
```

Expected: only declaration in `IVulnerabilitySource.cs` and implementation in `DefenderVulnerabilitySource.cs`.

- [ ] **Step 2: Remove from interface**

Delete the `FetchCanonicalVulnerabilitiesAsync` declaration from `IVulnerabilitySource.cs`.

- [ ] **Step 3: Remove from implementors**

```bash
grep -rn "FetchCanonicalVulnerabilitiesAsync" src/
```

Remove the implementation from `DefenderVulnerabilitySource.cs` (lines ~101–178). If it is a large useful method, convert it to a `public` non-interface method on `DefenderVulnerabilitySource` directly for future use. If it's unused beyond the interface, delete it.

- [ ] **Step 4: Build and test**

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx -v minimal
```

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IVulnerabilitySource.cs \
        src/PatchHound.Infrastructure/Sources/DefenderVulnerabilitySource.cs
git commit -m "refactor: remove unused FetchCanonicalVulnerabilitiesAsync from IVulnerabilitySource"
```

---

## Self-Review Checklist

- [x] All phases have exact file paths
- [x] All test steps show actual test code
- [x] Phase 0 must complete before Phase 1 (constants are used in extracted classes)
- [x] Phase 1 must complete before Phase 2 (constructors collapse after extraction)
- [x] Phase 3 critical fix (dead code) is partially handled in Phase 1 Task 1.4
- [x] Phase 4 and Phase 5 are independent of each other and can be done in any order after Phase 1
- [x] No "TBD" placeholders
- [x] InMemory provider concern addressed in IIngestionBulkWriter (Phase 2)

## Dependency Order

```
Phase 0 → Phase 1 → Phase 2
                  → Phase 3 (concurrent with Phase 2)
                  → Phase 4 (after Phase 1, any order)
                  → Phase 5 (after Phase 1, any order)
```
