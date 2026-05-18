# Greenfield Ingestion Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild ingestion around typed run-scoped observations, set-based PostgreSQL merges, and delta-driven downstream work so a tenant with 4,300 devices, 9,000 vulnerabilities, and 230,000 software observations completes in minutes instead of hours.

**Architecture:** Treat source payloads as append-only observations loaded with `COPY`, then update compact current-state tables through SQL merge services. Split source software identity, canonical software product, and software release so 230,000 observed source rows do not become 230,000 remediation products unless they are genuinely distinct products. Make exposure, episode, projection, and enrichment phases operate on run-scoped touched IDs instead of full-tenant scans.

**Tech Stack:** .NET 10, EF Core with Npgsql, PostgreSQL 16, Npgsql binary `COPY`, xunit, FluentAssertions, Testcontainers.PostgreSql.

---

## Non-Goals

- No legacy schema compatibility.
- No migration of existing production data.
- No support for the old polymorphic `StagedDevices` ingestion path after cutover.
- No EF row-object loops on the hot path.

## Target Stage Budgets

For the guiding workload:

- Device and software observation load: under 2 minutes.
- Vulnerability and source exposure load: under 2 minutes.
- Current-state merge: under 5 minutes.
- Exposure state and episode sync: under 5 minutes.
- Software projection and enrichment enqueue: under 3 minutes.
- Total ingestion: under 20 minutes on local Docker Postgres; lower on production-class Postgres.

---

## File Structure

Create focused files rather than expanding `IngestionService.cs` further.

- Create `src/PatchHound.Core/Entities/Ingestion/SoftwareSourceIdentity.cs`
- Create `src/PatchHound.Core/Entities/Ingestion/SoftwareRelease.cs`
- Create `src/PatchHound.Core/Entities/Ingestion/RawDeviceObservation.cs`
- Create `src/PatchHound.Core/Entities/Ingestion/RawSoftwareObservation.cs`
- Create `src/PatchHound.Core/Entities/Ingestion/RawInstallationObservation.cs`
- Create `src/PatchHound.Core/Entities/Ingestion/RawVulnerabilityObservation.cs`
- Create `src/PatchHound.Core/Entities/Ingestion/RawExposureObservation.cs`
- Create `src/PatchHound.Core/Entities/Ingestion/IngestionRunDelta.cs`
- Create matching EF configurations under `src/PatchHound.Infrastructure/Data/Configurations/Ingestion/`
- Create `src/PatchHound.Core/Interfaces/IObservationBulkLoader.cs`
- Create `src/PatchHound.Core/Interfaces/IIngestionStateMerger.cs`
- Create `src/PatchHound.Core/Interfaces/IExposureStateMerger.cs`
- Create `src/PatchHound.Core/Interfaces/IEpisodeStateMerger.cs`
- Create `src/PatchHound.Core/Interfaces/IIncrementalSoftwareProjectionWriter.cs`
- Create PostgreSQL implementations under `src/PatchHound.Infrastructure/Services/IngestionV2/`
- Create tests under `tests/PatchHound.Tests/Infrastructure/IngestionV2/`
- Modify `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Modify `src/PatchHound.Infrastructure/DependencyInjection.cs`
- Replace orchestration in `src/PatchHound.Infrastructure/Services/IngestionService.cs` only after the new path has end-to-end tests.

---

## Task 1: Add Typed Observation Schema

**Files:**
- Create entity files under `src/PatchHound.Core/Entities/Ingestion/`
- Create EF configurations under `src/PatchHound.Infrastructure/Data/Configurations/Ingestion/`
- Modify `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Create tests in `tests/PatchHound.Tests/Infrastructure/IngestionV2/IngestionV2SchemaTests.cs`

- [ ] **Step 1: Write the failing schema test**

```csharp
[Collection(PostgresCollection.Name)]
public sealed class IngestionV2SchemaTests
{
    private readonly PostgresFixture _fx;

    public IngestionV2SchemaTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Observation_tables_have_expected_uniques()
    {
        await using var db = _fx.CreateDbContext();

        var indexes = await db.Database.SqlQueryRaw<string>("""
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN (
                'RawDeviceObservations',
                'RawSoftwareObservations',
                'RawInstallationObservations',
                'RawVulnerabilityObservations',
                'RawExposureObservations',
                'SoftwareSourceIdentities',
                'SoftwareReleases',
                'IngestionRunDeltas')
            ORDER BY indexname
            """).ToListAsync();

        indexes.Should().Contain("UX_RawDeviceObservations_Run_Source_ExternalId");
        indexes.Should().Contain("UX_RawSoftwareObservations_Run_Source_ExternalId");
        indexes.Should().Contain("UX_RawInstallationObservations_Run_Device_Software");
        indexes.Should().Contain("UX_RawVulnerabilityObservations_Run_Source_ExternalId");
        indexes.Should().Contain("UX_RawExposureObservations_Run_Device_Vulnerability_Software");
        indexes.Should().Contain("UX_SoftwareSourceIdentities_Source_ExternalId");
        indexes.Should().Contain("UX_SoftwareReleases_Product_Version");
        indexes.Should().Contain("UX_IngestionRunDeltas_Run_Kind_Id");
    }
}
```

- [ ] **Step 2: Run the failing test**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~IngestionV2SchemaTests -v minimal`

Expected: compile failure because the new entities and `DbSet`s do not exist.

- [ ] **Step 3: Add core entities**

Add immutable factory-style entities with explicit max-length validation:

- `SoftwareSourceIdentity`: `Id`, `SourceSystemId`, `ExternalId`, `ObservedVendor`, `ObservedName`, `ObservedVersion`, `CanonicalProductKey`, `SoftwareProductId`, `SoftwareReleaseId`, `FirstSeenAt`, `LastSeenAt`.
- `SoftwareRelease`: `Id`, `SoftwareProductId`, `NormalizedVersion`, `RawVersion`, `FirstSeenAt`, `LastSeenAt`.
- `RawDeviceObservation`: run-scoped device facts.
- `RawSoftwareObservation`: run-scoped source software facts.
- `RawInstallationObservation`: run-scoped device-to-source-software facts.
- `RawVulnerabilityObservation`: run-scoped vulnerability facts without affected asset arrays.
- `RawExposureObservation`: run-scoped direct source exposure facts.
- `IngestionRunDelta`: `RunId`, `TenantId`, `Kind`, `EntityId`.

Use entity factory methods named `Create(...)` and throw `ArgumentException` when external IDs, names, or versions exceed existing EF caps.

- [ ] **Step 4: Add EF configurations**

Configure the table names and indexes:

```csharp
builder.HasIndex(x => new { x.IngestionRunId, x.SourceSystemId, x.ExternalId })
    .IsUnique()
    .HasDatabaseName("UX_RawSoftwareObservations_Run_Source_ExternalId");

builder.HasIndex(x => new { x.SourceSystemId, x.ExternalId })
    .IsUnique()
    .HasDatabaseName("UX_SoftwareSourceIdentities_Source_ExternalId");

builder.HasIndex(x => new { x.RunId, x.Kind, x.EntityId })
    .IsUnique()
    .HasDatabaseName("UX_IngestionRunDeltas_Run_Kind_Id");
```

Also add lookup indexes for:

- `RawInstallationObservations(IngestionRunId, TenantId, DeviceExternalId)`
- `RawInstallationObservations(IngestionRunId, TenantId, SoftwareExternalId)`
- `RawExposureObservations(IngestionRunId, TenantId, DeviceExternalId)`
- `RawExposureObservations(IngestionRunId, TenantId, VulnerabilityExternalId)`
- `SoftwareSourceIdentities(SoftwareProductId)`
- `SoftwareReleases(SoftwareProductId, NormalizedVersion)`

- [ ] **Step 5: Wire DbContext**

Add `DbSet<>` properties in `PatchHoundDbContext` and apply configurations.

- [ ] **Step 6: Generate migration**

Ask the user to run:

```bash
dotnet ef migrations add GreenfieldIngestionV2 --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api
```

Do not run `dotnet ef` from an agent session.

- [ ] **Step 7: Run tests**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~IngestionV2SchemaTests -v minimal`

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/PatchHound.Core/Entities/Ingestion src/PatchHound.Infrastructure/Data tests/PatchHound.Tests/Infrastructure/IngestionV2
git commit -m "feat: add ingestion v2 observation schema"
```

---

## Task 2: Add Npgsql COPY Observation Loader

**Files:**
- Create `src/PatchHound.Core/Interfaces/IObservationBulkLoader.cs`
- Create `src/PatchHound.Infrastructure/Services/IngestionV2/PostgresObservationBulkLoader.cs`
- Modify `src/PatchHound.Infrastructure/DependencyInjection.cs`
- Test `tests/PatchHound.Tests/Infrastructure/IngestionV2/PostgresObservationBulkLoaderTests.cs`

- [ ] **Step 1: Write failing bulk-loader test**

```csharp
[Fact]
public async Task LoadAsync_copies_observations_and_dedupes_run_rows()
{
    await using var db = _fx.CreateDbContext();
    var tenantId = Guid.NewGuid();
    var runId = Guid.NewGuid();
    var sourceSystem = SourceSystem.Create("defender", "Defender");
    db.SourceSystems.Add(sourceSystem);
    await db.SaveChangesAsync();

    var loader = new PostgresObservationBulkLoader(db);
    var batch = IngestionObservationBatch.Create(
        tenantId,
        runId,
        sourceSystem.Id,
        devices: [DeviceObservationInput.Create("dev-1", "host-1", DateTimeOffset.UtcNow)],
        software: [SoftwareObservationInput.Create("sw-1", "Vendor", "Product", "1.0")],
        installations: [InstallationObservationInput.Create("dev-1", "sw-1", "1.0", DateTimeOffset.UtcNow)],
        vulnerabilities: [VulnerabilityObservationInput.Create("CVE-2026-0001", "Test vuln", Severity.High)],
        exposures: [ExposureObservationInput.Create("dev-1", "CVE-2026-0001", "sw-1", "1.0", DateTimeOffset.UtcNow)]);

    await loader.LoadAsync(batch, CancellationToken.None);
    await loader.LoadAsync(batch, CancellationToken.None);

    (await db.RawDeviceObservations.CountAsync()).Should().Be(1);
    (await db.RawSoftwareObservations.CountAsync()).Should().Be(1);
    (await db.RawInstallationObservations.CountAsync()).Should().Be(1);
    (await db.RawVulnerabilityObservations.CountAsync()).Should().Be(1);
    (await db.RawExposureObservations.CountAsync()).Should().Be(1);
}
```

- [ ] **Step 2: Run failing test**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~PostgresObservationBulkLoaderTests -v minimal`

Expected: compile failure for missing loader and input records.

- [ ] **Step 3: Add input records**

Create compact input records in `IObservationBulkLoader.cs`:

```csharp
public sealed record IngestionObservationBatch(
    Guid TenantId,
    Guid IngestionRunId,
    Guid SourceSystemId,
    IReadOnlyList<DeviceObservationInput> Devices,
    IReadOnlyList<SoftwareObservationInput> Software,
    IReadOnlyList<InstallationObservationInput> Installations,
    IReadOnlyList<VulnerabilityObservationInput> Vulnerabilities,
    IReadOnlyList<ExposureObservationInput> Exposures);
```

Include static `Create(...)` helpers on each input record to normalize empty strings and trim values.

- [ ] **Step 4: Implement COPY loader**

Implement one transaction per batch:

1. Create temp tables for each observation type.
2. Binary `COPY` inputs into temp tables.
3. Insert into raw observation tables with `ON CONFLICT DO UPDATE`.
4. Do not deserialize JSON.
5. Do not use EF `AddRange`.

- [ ] **Step 5: Register service**

Register `IObservationBulkLoader` as scoped in `DependencyInjection.cs`.

- [ ] **Step 6: Run tests**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~PostgresObservationBulkLoaderTests -v minimal`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IObservationBulkLoader.cs src/PatchHound.Infrastructure/Services/IngestionV2 src/PatchHound.Infrastructure/DependencyInjection.cs tests/PatchHound.Tests/Infrastructure/IngestionV2
git commit -m "feat: load ingestion observations with postgres copy"
```

---

## Task 3: Merge Source Software Identities and Releases

**Files:**
- Create `src/PatchHound.Core/Interfaces/IIngestionStateMerger.cs`
- Create `src/PatchHound.Infrastructure/Services/IngestionV2/PostgresIngestionStateMerger.cs`
- Test `tests/PatchHound.Tests/Infrastructure/IngestionV2/PostgresIngestionStateMergerSoftwareTests.cs`

- [ ] **Step 1: Write failing software merge test**

```csharp
[Fact]
public async Task MergeSoftwareAsync_collapses_many_source_ids_to_one_product_and_release()
{
    var ctx = await SeedRunWithSoftwareAsync(
        sourceSoftwareCount: 100,
        vendor: "Microsoft",
        name: "Edge",
        version: "126.0");

    var merger = new PostgresIngestionStateMerger(ctx.Db);
    await merger.MergeSoftwareAsync(ctx.TenantId, ctx.RunId, CancellationToken.None);

    (await ctx.Db.SoftwareSourceIdentities.CountAsync()).Should().Be(100);
    (await ctx.Db.SoftwareProducts.CountAsync()).Should().Be(1);
    (await ctx.Db.SoftwareReleases.CountAsync()).Should().Be(1);
    (await ctx.Db.IngestionRunDeltas.CountAsync(d => d.RunId == ctx.RunId && d.Kind == "SoftwareProduct")).Should().Be(1);
}
```

- [ ] **Step 2: Run failing test**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~PostgresIngestionStateMergerSoftwareTests -v minimal`

Expected: compile failure.

- [ ] **Step 3: Implement software merge SQL**

`MergeSoftwareAsync` must:

1. Upsert `SoftwareProducts` from distinct `CanonicalProductKey`.
2. Upsert `SoftwareReleases` from `(SoftwareProductId, NormalizedVersion)`.
3. Upsert `SoftwareSourceIdentities` from `(SourceSystemId, ExternalId)`.
4. Insert touched product IDs into `IngestionRunDeltas`.

Use `INSERT ... SELECT DISTINCT ... ON CONFLICT ... DO UPDATE`.

- [ ] **Step 4: Run focused tests**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~PostgresIngestionStateMergerSoftwareTests -v minimal`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IIngestionStateMerger.cs src/PatchHound.Infrastructure/Services/IngestionV2 tests/PatchHound.Tests/Infrastructure/IngestionV2
git commit -m "feat: merge source software identities incrementally"
```

---

## Task 4: Merge Devices and Installations From Observations

**Files:**
- Modify `PostgresIngestionStateMerger.cs`
- Test `tests/PatchHound.Tests/Infrastructure/IngestionV2/PostgresIngestionStateMergerInstallationTests.cs`

- [x] **Step 1: Write failing installation merge test**

```csharp
[Fact]
public async Task MergeInstallationsAsync_updates_only_current_run_devices_and_records_deltas()
{
    var ctx = await SeedRunWithDevicesSoftwareAndInstallationsAsync(deviceCount: 10, softwarePerDevice: 5);
    var merger = new PostgresIngestionStateMerger(ctx.Db);

    await merger.MergeSoftwareAsync(ctx.TenantId, ctx.RunId, CancellationToken.None);
    await merger.MergeDevicesAsync(ctx.TenantId, ctx.RunId, CancellationToken.None);
    await merger.MergeInstallationsAsync(ctx.TenantId, ctx.RunId, CancellationToken.None);

    (await ctx.Db.Devices.CountAsync(d => d.TenantId == ctx.TenantId)).Should().Be(10);
    (await ctx.Db.InstalledSoftware.CountAsync(i => i.TenantId == ctx.TenantId)).Should().Be(50);
    (await ctx.Db.IngestionRunDeltas.CountAsync(d => d.RunId == ctx.RunId && d.Kind == "Device")).Should().Be(10);
    (await ctx.Db.IngestionRunDeltas.CountAsync(d => d.RunId == ctx.RunId && d.Kind == "InstalledSoftware")).Should().Be(50);
}
```

- [x] **Step 2: Implement `MergeDevicesAsync`**

Use `RawDeviceObservations` as source and upsert `Devices` with `ON CONFLICT (TenantId, SourceSystemId, ExternalId)`.

- [x] **Step 3: Implement `MergeInstallationsAsync`**

Join raw installations to:

- current `Devices` by `(TenantId, SourceSystemId, DeviceExternalId)`,
- `SoftwareSourceIdentities` by `(SourceSystemId, SoftwareExternalId)`,
- `SoftwareReleases` by source identity release.

Upsert `InstalledSoftware`. Store `SoftwareSourceIdentityId` and `SoftwareReleaseId` if new columns are introduced; otherwise store the resolved product and version.

- [x] **Step 4: Implement stale installation resolution**

For devices touched in the run, mark prior installations from the same source inactive when absent from `RawInstallationObservations` for the run. Prefer an `IsActive` flag over deleting rows.

Implemented with the existing schema by treating `InstalledSoftware.LastSeenRunId` as the current-run marker; stale rows are not deleted and are not touched for the current run.

- [x] **Step 5: Run tests**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~PostgresIngestionStateMergerInstallationTests -v minimal`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionV2 tests/PatchHound.Tests/Infrastructure/IngestionV2
git commit -m "feat: merge devices and installations from observations"
```

---

## Task 5: Bulk Merge Vulnerabilities and Direct Exposure Facts

**Files:**
- Modify `PostgresIngestionStateMerger.cs`
- Create `src/PatchHound.Infrastructure/Services/IngestionV2/PostgresExposureStateMerger.cs`
- Test `tests/PatchHound.Tests/Infrastructure/IngestionV2/PostgresExposureStateMergerTests.cs`

- [ ] **Step 1: Write failing direct exposure test**

```csharp
[Fact]
public async Task MergeDirectExposuresAsync_opens_reobserves_and_resolves_by_run_delta()
{
    var ctx = await SeedTwoRunsWithOneResolvedExposureAsync();
    var stateMerger = new PostgresIngestionStateMerger(ctx.Db);
    var exposureMerger = new PostgresExposureStateMerger(ctx.Db);

    await stateMerger.MergeAllAsync(ctx.TenantId, ctx.FirstRunId, CancellationToken.None);
    await exposureMerger.MergeDirectExposuresAsync(ctx.TenantId, ctx.FirstRunId, CancellationToken.None);

    await stateMerger.MergeAllAsync(ctx.TenantId, ctx.SecondRunId, CancellationToken.None);
    await exposureMerger.MergeDirectExposuresAsync(ctx.TenantId, ctx.SecondRunId, CancellationToken.None);

    var resolved = await ctx.Db.DeviceVulnerabilityExposures.SingleAsync(e => e.VulnerabilityId == ctx.ResolvedVulnerabilityId);
    resolved.Status.Should().Be(ExposureStatus.Resolved);
}
```

- [ ] **Step 2: Implement vulnerability bulk merge**

Replace per-vulnerability resolver semantics with set-based SQL:

- Upsert `Vulnerabilities` from `RawVulnerabilityObservations`.
- Reconcile references and applicability rules in separate bulk SQL.
- Insert `Vulnerability` deltas.

- [ ] **Step 3: Implement direct exposure merge**

Upsert exposures from `RawExposureObservations` joined to current devices, vulnerabilities, and software identities. Conflict key remains `(TenantId, DeviceId, VulnerabilityId)` unless greenfield schema allows `(TenantId, DeviceId, VulnerabilityId, SoftwareSourceIdentityId)`.

- [ ] **Step 4: Resolve stale exposures by touched devices and source**

Resolve only open exposures for devices touched by the run and source system when no matching raw exposure exists in the current run.

- [ ] **Step 5: Run tests**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~PostgresExposureStateMergerTests -v minimal`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionV2 tests/PatchHound.Tests/Infrastructure/IngestionV2
git commit -m "feat: merge direct exposure facts set-based"
```

---

## Task 6: Replace Full-Tenant Exposure Derivation With Delta Derivation

**Files:**
- Create `src/PatchHound.Infrastructure/Services/IngestionV2/PostgresApplicabilityExposureDeriver.cs`
- Test `tests/PatchHound.Tests/Infrastructure/IngestionV2/PostgresApplicabilityExposureDeriverTests.cs`

- [ ] **Step 1: Write failing delta derivation test**

```csharp
[Fact]
public async Task DeriveAsync_scans_only_touched_products_and_vulnerabilities()
{
    var ctx = await SeedLargeTenantWithOneTouchedProductAsync(totalInstalledSoftware: 10000);
    var deriver = new PostgresApplicabilityExposureDeriver(ctx.Db);

    var result = await deriver.DeriveAsync(ctx.TenantId, ctx.RunId, CancellationToken.None);

    result.ScannedProductCount.Should().Be(1);
    result.Inserted + result.Reobserved.Should().Be(1);
}
```

- [ ] **Step 2: Implement touched-set derivation**

Derive exposure candidates from:

- products in `IngestionRunDeltas` for the run,
- vulnerabilities in `IngestionRunDeltas` for the run,
- devices touched in the run.

Do not scan every tenant install.

- [ ] **Step 3: Move version matching into SQL**

Add normalized version columns to `SoftwareReleases` and applicability bounds. Use a PostgreSQL function or generated sortable version key. The function must return `NULL` for non-comparable versions and fall back to direct source exposure facts for those rows.

- [ ] **Step 4: Run tests**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~PostgresApplicabilityExposureDeriverTests -v minimal`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionV2 tests/PatchHound.Tests/Infrastructure/IngestionV2
git commit -m "feat: derive applicability exposures from run deltas"
```

---

## Task 7: Make Episode Sync Set-Based

**Files:**
- Create `src/PatchHound.Core/Interfaces/IEpisodeStateMerger.cs`
- Create `src/PatchHound.Infrastructure/Services/IngestionV2/PostgresEpisodeStateMerger.cs`
- Test `tests/PatchHound.Tests/Infrastructure/IngestionV2/PostgresEpisodeStateMergerTests.cs`

- [ ] **Step 1: Write failing episode sync test**

```csharp
[Fact]
public async Task SyncAsync_opens_and_closes_episodes_without_loading_exposure_entities()
{
    var ctx = await SeedOpenAndResolvedExposuresForRunAsync();
    var merger = new PostgresEpisodeStateMerger(ctx.Db);

    await merger.SyncAsync(ctx.TenantId, ctx.RunId, ctx.Now, CancellationToken.None);

    var episodes = await ctx.Db.ExposureEpisodes.OrderBy(e => e.EpisodeNumber).ToListAsync();
    episodes.Should().Contain(e => e.ClosedAt == ctx.Now);
    episodes.Should().Contain(e => e.ClosedAt == null);
}
```

- [ ] **Step 2: Implement SQL episode open**

Insert a new episode for open exposures touched by the run where no open episode exists.

- [ ] **Step 3: Implement SQL episode close**

Close open episodes where the corresponding exposure was resolved by the run.

- [ ] **Step 4: Run tests**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~PostgresEpisodeStateMergerTests -v minimal`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IEpisodeStateMerger.cs src/PatchHound.Infrastructure/Services/IngestionV2 tests/PatchHound.Tests/Infrastructure/IngestionV2
git commit -m "feat: sync exposure episodes set-based"
```

---

## Task 8: Incremental Software Projection

**Files:**
- Create `src/PatchHound.Core/Interfaces/IIncrementalSoftwareProjectionWriter.cs`
- Create `src/PatchHound.Infrastructure/Services/IngestionV2/PostgresIncrementalSoftwareProjectionWriter.cs`
- Test `tests/PatchHound.Tests/Infrastructure/IngestionV2/PostgresIncrementalSoftwareProjectionWriterTests.cs`

- [ ] **Step 1: Write failing projection test**

```csharp
[Fact]
public async Task SyncAsync_updates_only_products_and_installations_touched_by_run()
{
    var ctx = await SeedProjectionTenantWithUntouchedRowsAsync();
    var writer = new PostgresIncrementalSoftwareProjectionWriter(ctx.Db);

    var result = await writer.SyncAsync(ctx.TenantId, ctx.RunId, CancellationToken.None);

    result.TouchedProductCount.Should().Be(ctx.ExpectedTouchedProductCount);
    result.TouchedInstallationCount.Should().Be(ctx.ExpectedTouchedInstallationCount);
    result.ScannedTenantInstallationCount.Should().BeLessThan(ctx.TotalTenantInstallationCount);
}
```

- [ ] **Step 2: Implement tenant software update from product deltas**

Update/insert `SoftwareTenantRecords` only for `SoftwareProduct` deltas from the run.

- [ ] **Step 3: Implement installation projection from installation deltas**

Update/insert/deactivate `SoftwareProductInstallations` only for `InstalledSoftware` deltas from the run.

- [ ] **Step 4: Run tests**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~PostgresIncrementalSoftwareProjectionWriterTests -v minimal`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IIncrementalSoftwareProjectionWriter.cs src/PatchHound.Infrastructure/Services/IngestionV2 tests/PatchHound.Tests/Infrastructure/IngestionV2
git commit -m "feat: project software incrementally"
```

---

## Task 9: Incremental Enrichment Queueing

**Files:**
- Create `src/PatchHound.Infrastructure/Services/IngestionV2/IngestionV2EnrichmentPlanner.cs`
- Test `tests/PatchHound.Tests/Infrastructure/IngestionV2/IngestionV2EnrichmentPlannerTests.cs`

- [ ] **Step 1: Write failing enrichment test**

```csharp
[Fact]
public async Task EnqueueAsync_enqueues_only_new_or_changed_run_delta_entities()
{
    var ctx = await SeedRunDeltasWithUntouchedSoftwareTenantRecordsAsync();
    var planner = new IngestionV2EnrichmentPlanner(ctx.Db, ctx.Enqueuer);

    await planner.EnqueueAsync(ctx.TenantId, ctx.RunId, CancellationToken.None);

    ctx.Enqueuer.SoftwareEndOfLifeJobCount.Should().Be(ctx.ExpectedTouchedSoftwareCount);
    ctx.Enqueuer.VulnerabilityJobCount.Should().Be(ctx.ExpectedTouchedVulnerabilityCount);
}
```

- [ ] **Step 2: Implement enrichment planner**

Read `IngestionRunDeltas` for kinds:

- `SoftwareProduct`
- `Vulnerability`

Only enqueue jobs when the entity was created or materially changed in the current run.

- [ ] **Step 3: Run tests**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~IngestionV2EnrichmentPlannerTests -v minimal`

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionV2 tests/PatchHound.Tests/Infrastructure/IngestionV2
git commit -m "feat: enqueue enrichment from ingestion deltas"
```

---

## Task 10: Add Ingestion V2 Orchestrator

**Files:**
- Create `src/PatchHound.Infrastructure/Services/IngestionV2/IngestionV2Service.cs`
- Modify `src/PatchHound.Infrastructure/DependencyInjection.cs`
- Test `tests/PatchHound.Tests/Infrastructure/IngestionV2/IngestionV2ServiceTests.cs`

- [ ] **Step 1: Write failing end-to-end test**

```csharp
[Fact]
public async Task RunAsync_executes_observation_load_merge_exposure_episode_projection_and_enrichment()
{
    var ctx = await CreateSyntheticRunAsync(devices: 100, softwarePerDevice: 20, vulnerabilities: 50);
    var service = ctx.ServiceProvider.GetRequiredService<IngestionV2Service>();

    var result = await service.RunAsync(ctx.Request, CancellationToken.None);

    result.DeviceCount.Should().Be(100);
    result.InstallationCount.Should().Be(2000);
    result.VulnerabilityCount.Should().Be(50);
    result.TotalElapsed.Should().BeLessThan(TimeSpan.FromMinutes(2));
}
```

- [ ] **Step 2: Implement orchestration**

The service order is:

1. `IObservationBulkLoader.LoadAsync`
2. `IIngestionStateMerger.MergeSoftwareAsync`
3. `IIngestionStateMerger.MergeDevicesAsync`
4. `IIngestionStateMerger.MergeInstallationsAsync`
5. `IIngestionStateMerger.MergeVulnerabilitiesAsync`
6. `IExposureStateMerger.MergeDirectExposuresAsync`
7. `PostgresApplicabilityExposureDeriver.DeriveAsync`
8. `IEpisodeStateMerger.SyncAsync`
9. `IIncrementalSoftwareProjectionWriter.SyncAsync`
10. `IngestionV2EnrichmentPlanner.EnqueueAsync`

- [ ] **Step 3: Emit stage timings**

Return and log per-stage elapsed time and row counts.

- [ ] **Step 4: Run tests**

Run: `dotnet test PatchHound.slnx --filter FullyQualifiedName~IngestionV2ServiceTests -v minimal`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionV2 src/PatchHound.Infrastructure/DependencyInjection.cs tests/PatchHound.Tests/Infrastructure/IngestionV2
git commit -m "feat: orchestrate ingestion v2 pipeline"
```

---

## Task 11: Replace Benchmark With Realistic Cardinality Scenarios

**Files:**
- Modify `benchmarks/PatchHound.IngestionBenchmark/`
- Create `benchmarks/PatchHound.IngestionBenchmark/V2BenchmarkSeeder.cs`
- Create `benchmarks/PatchHound.IngestionBenchmark/V2BenchmarkRunner.cs`

- [ ] **Step 1: Add benchmark scenario**

Add a scenario named `defender-realistic` with:

- devices: `4300`
- software observations: `230000`
- vulnerabilities: `9000`
- direct exposure observations: configurable, default `800000`

- [ ] **Step 2: Print all stage timings**

Print:

- observation load
- software merge
- device merge
- installation merge
- vulnerability merge
- direct exposure merge
- applicability derivation
- episode sync
- software projection
- enrichment queue
- total

- [ ] **Step 3: Run small benchmark**

Run:

```bash
dotnet run --project benchmarks/PatchHound.IngestionBenchmark -- --pipeline=v2 --scenario=defender-realistic --devices=100 --software-total=5000 --vulnerabilities=500 --exposures=20000
```

Expected: completes and prints all stages.

- [ ] **Step 4: Run target benchmark**

Run:

```bash
dotnet run --project benchmarks/PatchHound.IngestionBenchmark -- --pipeline=v2 --scenario=defender-realistic --devices=4300 --software-total=230000 --vulnerabilities=9000 --exposures=800000
```

Expected: total under 20 minutes locally. If not, capture top two slow stages and add an optimization task before cutover.

- [ ] **Step 5: Commit**

```bash
git add benchmarks/PatchHound.IngestionBenchmark
git commit -m "test: add ingestion v2 realistic benchmark"
```

---

## Task 12: Cut Over and Remove Legacy Hot Path

**Files:**
- Modify `src/PatchHound.Infrastructure/Services/IngestionService.cs`
- Modify `src/PatchHound.Infrastructure/DependencyInjection.cs`
- Delete or quarantine old staged merge services after tests pass
- Update docs

- [ ] **Step 1: Add integration test for production entry point**

Write a test proving the existing ingestion entry point uses `IngestionV2Service` and no longer calls:

- `StagedDeviceMergeService`
- `ExposureDerivationService` full-tenant path
- `NormalizedSoftwareProjectionService` full-tenant path

- [ ] **Step 2: Replace orchestration**

Route ingestion through `IngestionV2Service`.

- [ ] **Step 3: Remove legacy staging dependencies from DI**

Remove registrations that are no longer used by ingestion. Keep services only if other non-ingestion features still need them.

- [ ] **Step 4: Run full backend tests**

Run: `dotnet test PatchHound.slnx -v minimal`

Expected: PASS.

- [ ] **Step 5: Run target benchmark**

Run the realistic target benchmark from Task 11.

Expected: under 20 minutes locally.

- [ ] **Step 6: Commit**

```bash
git add src tests docs benchmarks
git commit -m "feat: cut over ingestion to v2 pipeline"
```

---

## Performance Verification Checklist

Before merging:

- [ ] `dotnet build PatchHound.slnx` passes.
- [ ] `dotnet test PatchHound.slnx -v minimal` passes.
- [ ] Realistic benchmark completes under target.
- [ ] Each SQL-heavy stage has row counts and elapsed timings.
- [ ] `EXPLAIN (ANALYZE, BUFFERS)` has been captured for any stage over 60 seconds.
- [ ] No hot-path stage loads all tenant `InstalledSoftware`, all tenant `SoftwareTenantRecords`, or all tenant `DeviceVulnerabilityExposures` unless that table is already constrained by run deltas.
- [ ] `gitnexus_detect_changes(scope: "all")` reports only expected ingestion, schema, benchmark, and test areas.

## Rollout Notes

Because this is greenfield and does not preserve legacy compatibility, rollout should be branch-level rather than feature-flag-level:

1. Finish all tasks on a dedicated branch.
2. Recreate local/dev databases from scratch.
3. Run the realistic benchmark.
4. Run one real Defender ingestion against a disposable tenant.
5. Only then remove old migrations or squash schema if desired.
