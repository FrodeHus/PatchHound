# Issue #76 — PostgreSQL-native ingestion & merge pipeline optimizations

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-row EF loops in the ingestion → merge → exposure → episode → projection pipeline with PostgreSQL-native set operations (UPSERT, temp tables, CTEs, run-scoped queries) behind a bulk-write seam, cutting DB round-trips from O(n) to O(1) per stage.

**Architecture:** Introduce a bulk-write seam (`IBulkExposureWriter`, `IBulkDeviceMergeWriter`, `IBulkEpisodeWriter`, `IBulkSoftwareProjectionWriter`) implemented in `PatchHound.Infrastructure` against PostgreSQL using `ExecuteSqlRawAsync` + Npgsql `COPY`. EF entities remain authoritative for reads and non-bulk writes; the seam owns hot-path writes. A new `LastSeenRunId` column on `DeviceVulnerabilityExposures` lets episode sync scope to the delta of a single run. All raw-SQL paths are validated by new Testcontainers Postgres tests (the existing test suite uses `UseInMemoryDatabase`, which cannot execute `ON CONFLICT`/CTEs).

**Tech Stack:** .NET 10, EF Core 9 (Npgsql provider), PostgreSQL 16, xunit + FluentAssertions + NSubstitute + Testcontainers.PostgreSql 4.11.

**Source issue:** [#76 — Perf: PostgreSQL-native optimizations for ingestion staging and merge pipeline](https://github.com/frodehus/PatchHound/issues/76)

---

## Task ordering rationale

The issue lists 6 functional steps. To keep each commit independently deployable and testable, the plan front-loads two enablers:

- **Task 0** — Testcontainers Postgres fixture (required because raw-SQL paths can't run on InMemory).
- **Task 1** — `LastSeenRunId` migration (required by Steps 1, 3, 4, 6).

Then the six issue steps follow in the order the issue recommends, with the bulk-write seam introduced incrementally per step.

---

## Task 0: Testcontainers Postgres fixture

**Files:**
- Create: `tests/PatchHound.Tests/Infrastructure/PostgresFixture.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/PostgresCollection.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/PostgresFixtureSmokeTests.cs`

- [ ] **Step 1: Write the failing smoke test**

```csharp
// tests/PatchHound.Tests/Infrastructure/PostgresFixtureSmokeTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Data;
using Xunit;

namespace PatchHound.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public class PostgresFixtureSmokeTests
{
    private readonly PostgresFixture _fx;
    public PostgresFixtureSmokeTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Migrations_apply_and_DeviceVulnerabilityExposures_table_exists()
    {
        await using var db = _fx.CreateDbContext();
        await db.Database.MigrateAsync();

        var exists = await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM information_schema.tables WHERE table_name = 'DeviceVulnerabilityExposures'");
        exists.Should().BeGreaterOrEqualTo(0); // ExecuteSqlRaw returns rows affected; presence verified by no exception
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PatchHound.Tests/PatchHound.Tests.csproj --filter "FullyQualifiedName~PostgresFixtureSmokeTests" -v minimal`
Expected: FAIL — `PostgresFixture` and `PostgresCollection` types do not exist.

- [ ] **Step 3: Implement the fixture**

```csharp
// tests/PatchHound.Tests/Infrastructure/PostgresFixture.cs
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace PatchHound.Tests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public PatchHoundDbContext CreateDbContext(ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext ?? new StubTenantContext()));
    }

    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        // Truncate all tables except __EFMigrationsHistory; use TRUNCATE ... CASCADE for FK chains.
        await db.Database.ExecuteSqlRawAsync(@"
            DO $$
            DECLARE r record;
            BEGIN
                FOR r IN SELECT tablename FROM pg_tables
                         WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory'
                LOOP
                    EXECUTE 'TRUNCATE TABLE ""' || r.tablename || '"" CASCADE';
                END LOOP;
            END $$;");
    }
}
```

```csharp
// tests/PatchHound.Tests/Infrastructure/PostgresCollection.cs
using Xunit;

namespace PatchHound.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
```

`StubTenantContext` already exists for the in-memory tests — reuse it; if it isn't accessible from this namespace, add a minimal one mirroring the existing pattern (single-tenant Guid, no global filter bypass).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PatchHound.Tests/PatchHound.Tests.csproj --filter "FullyQualifiedName~PostgresFixtureSmokeTests" -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/PatchHound.Tests/Infrastructure/PostgresFixture.cs \
        tests/PatchHound.Tests/Infrastructure/PostgresCollection.cs \
        tests/PatchHound.Tests/Infrastructure/PostgresFixtureSmokeTests.cs
git commit -m "test: add Testcontainers Postgres fixture for raw-SQL path tests"
```

---

## Task 1: Add `LastSeenRunId` to `DeviceVulnerabilityExposure`

**Files:**
- Modify: `src/PatchHound.Core/Entities/DeviceVulnerabilityExposure.cs`
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/DeviceVulnerabilityExposureConfiguration.cs`
- Create: `src/PatchHound.Infrastructure/Migrations/<timestamp>_AddLastSeenRunIdToExposures.cs` (via `dotnet ef`)
- Create: `tests/PatchHound.Tests/Core/Entities/DeviceVulnerabilityExposureLastSeenRunIdTests.cs`

- [ ] **Step 1: Write the failing entity test**

```csharp
// tests/PatchHound.Tests/Core/Entities/DeviceVulnerabilityExposureLastSeenRunIdTests.cs
using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using Xunit;

namespace PatchHound.Tests.Core.Entities;

public class DeviceVulnerabilityExposureLastSeenRunIdTests
{
    [Fact]
    public void Observe_records_run_id()
    {
        var runId = Guid.NewGuid();
        var ex = DeviceVulnerabilityExposure.Observe(
            tenantId: Guid.NewGuid(), deviceId: Guid.NewGuid(), vulnerabilityId: Guid.NewGuid(),
            softwareProductId: null, installedSoftwareId: null, matchedVersion: "1.0",
            matchSource: ExposureMatchSource.Cpe, observedAt: DateTimeOffset.UtcNow,
            runId: runId);

        ex.LastSeenRunId.Should().Be(runId);
    }

    [Fact]
    public void Reobserve_updates_run_id()
    {
        var ex = DeviceVulnerabilityExposure.Observe(
            tenantId: Guid.NewGuid(), deviceId: Guid.NewGuid(), vulnerabilityId: Guid.NewGuid(),
            softwareProductId: null, installedSoftwareId: null, matchedVersion: "1.0",
            matchSource: ExposureMatchSource.Cpe, observedAt: DateTimeOffset.UtcNow,
            runId: Guid.NewGuid());

        var newRun = Guid.NewGuid();
        ex.Reobserve(DateTimeOffset.UtcNow, newRun);

        ex.LastSeenRunId.Should().Be(newRun);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DeviceVulnerabilityExposureLastSeenRunIdTests"`
Expected: FAIL — `runId` parameter and `LastSeenRunId` property do not exist.

- [ ] **Step 3: Add `LastSeenRunId` to the entity**

Edit `src/PatchHound.Core/Entities/DeviceVulnerabilityExposure.cs`:

```csharp
public Guid? LastSeenRunId { get; private set; }

public static DeviceVulnerabilityExposure Observe(
    Guid tenantId,
    Guid deviceId,
    Guid vulnerabilityId,
    Guid? softwareProductId,
    Guid? installedSoftwareId,
    string matchedVersion,
    ExposureMatchSource matchSource,
    DateTimeOffset observedAt,
    Guid runId)
{
    if (tenantId == Guid.Empty) throw new ArgumentException(nameof(tenantId));
    if (deviceId == Guid.Empty) throw new ArgumentException(nameof(deviceId));
    if (vulnerabilityId == Guid.Empty) throw new ArgumentException(nameof(vulnerabilityId));
    if (runId == Guid.Empty) throw new ArgumentException(nameof(runId));
    if (observedAt == default) throw new ArgumentException(nameof(observedAt));

    return new DeviceVulnerabilityExposure
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        DeviceId = deviceId,
        VulnerabilityId = vulnerabilityId,
        SoftwareProductId = softwareProductId,
        InstalledSoftwareId = installedSoftwareId,
        MatchedVersion = matchedVersion?.Trim() ?? string.Empty,
        MatchSource = matchSource,
        Status = ExposureStatus.Open,
        FirstObservedAt = observedAt,
        LastObservedAt = observedAt,
        ResolvedAt = null,
        LastSeenRunId = runId,
    };
}

public void Reobserve(DateTimeOffset observedAt, Guid runId)
{
    if (observedAt > LastObservedAt) LastObservedAt = observedAt;
    if (Status == ExposureStatus.Resolved) { Status = ExposureStatus.Open; ResolvedAt = null; }
    LastSeenRunId = runId;
}

public void Reopen(DateTimeOffset observedAt, Guid runId)
{
    Status = ExposureStatus.Open;
    ResolvedAt = null;
    LastObservedAt = observedAt;
    LastSeenRunId = runId;
}
```

Update all callers of `Observe` / `Reobserve` / `Reopen` to pass the run id. Use `gitnexus_impact({target: "DeviceVulnerabilityExposure.Observe", direction: "upstream"})` and `gitnexus_impact({target: "DeviceVulnerabilityExposure.Reobserve", direction: "upstream"})` to find every call site, then thread `runId` through. The known sites are `ExposureDerivationService.DeriveForTenantAsync` (line ~86) and `IngestionService.ProcessStagedResultsAsync`; the service signature gains a `Guid runId` parameter wherever `DeriveForTenantAsync` is called from (`IngestionService.RunExposureDerivationAsync`).

- [ ] **Step 4: Update EF configuration**

Edit `src/PatchHound.Infrastructure/Data/Configurations/DeviceVulnerabilityExposureConfiguration.cs`:

```csharp
builder.Property(x => x.LastSeenRunId);
builder.HasIndex(x => new { x.TenantId, x.LastSeenRunId });
```

- [ ] **Step 5: Generate the EF migration**

Run:
```bash
dotnet ef migrations add AddLastSeenRunIdToExposures \
  --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api
```

Inspect the generated migration — it should add a nullable `uuid` column and a composite index. No backfill is needed (column is nullable; the first ingestion run after deploy populates it).

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DeviceVulnerabilityExposureLastSeenRunIdTests"`
Expected: PASS.

Run: `dotnet build PatchHound.slnx`
Expected: zero errors (call-site updates compile).

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Core/Entities/DeviceVulnerabilityExposure.cs \
        src/PatchHound.Infrastructure/Data/Configurations/DeviceVulnerabilityExposureConfiguration.cs \
        src/PatchHound.Infrastructure/Migrations/ \
        src/PatchHound.Infrastructure/Services/ExposureDerivationService.cs \
        src/PatchHound.Infrastructure/Services/IngestionService.cs \
        tests/PatchHound.Tests/Core/Entities/DeviceVulnerabilityExposureLastSeenRunIdTests.cs
git commit -m "feat: track LastSeenRunId on DeviceVulnerabilityExposure"
```

---

## Task 2: Bulk-write seam — `IBulkExposureWriter` (Step 1 of the issue)

Replace the row-by-row exposure upsert in `IngestionService.ProcessStagedResultsAsync` and the per-row `db.DeviceVulnerabilityExposures.Add(...)` in `ExposureDerivationService.DeriveForTenantAsync` with a batched `INSERT ... ON CONFLICT DO UPDATE`.

**Files:**
- Create: `src/PatchHound.Core/Interfaces/IBulkExposureWriter.cs`
- Create: `src/PatchHound.Core/Models/ExposureUpsertRow.cs`
- Create: `src/PatchHound.Infrastructure/Services/Bulk/PostgresBulkExposureWriter.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/Bulk/PostgresBulkExposureWriterTests.cs`

- [ ] **Step 1: Define the seam interface and row DTO**

```csharp
// src/PatchHound.Core/Models/ExposureUpsertRow.cs
namespace PatchHound.Core.Models;

public sealed record ExposureUpsertRow(
    Guid TenantId,
    Guid DeviceId,
    Guid VulnerabilityId,
    Guid? SoftwareProductId,
    Guid? InstalledSoftwareId,
    string MatchedVersion,
    string MatchSource,   // "Product" | "Cpe"
    DateTimeOffset ObservedAt,
    Guid RunId);

public sealed record BulkExposureUpsertResult(int Inserted, int Reobserved);
```

```csharp
// src/PatchHound.Core/Interfaces/IBulkExposureWriter.cs
using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IBulkExposureWriter
{
    Task<BulkExposureUpsertResult> UpsertAsync(
        IReadOnlyCollection<ExposureUpsertRow> rows,
        CancellationToken ct);

    /// Resolves exposures for the tenant whose <c>LastSeenRunId</c> is not the
    /// given run id and whose status is Open. Returns the number of rows resolved.
    Task<int> ResolveStaleAsync(Guid tenantId, Guid runId, DateTimeOffset resolvedAt, CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing integration test**

```csharp
// tests/PatchHound.Tests/Infrastructure/Bulk/PostgresBulkExposureWriterTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Services.Bulk;
using PatchHound.Tests.Infrastructure;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Bulk;

[Collection(PostgresCollection.Name)]
public class PostgresBulkExposureWriterTests
{
    private readonly PostgresFixture _fx;
    public PostgresBulkExposureWriterTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task UpsertAsync_inserts_new_rows_and_reobserves_existing()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var (tenantId, deviceId, vulnId) = await SeedDeviceAndVuln(db);
        var writer = new PostgresBulkExposureWriter(db);

        var run1 = Guid.NewGuid();
        var observed = DateTimeOffset.UtcNow;
        var first = await writer.UpsertAsync(new[]
        {
            new ExposureUpsertRow(tenantId, deviceId, vulnId, null, null, "1.0", "Cpe", observed, run1),
        }, CancellationToken.None);

        first.Inserted.Should().Be(1);
        first.Reobserved.Should().Be(0);

        var run2 = Guid.NewGuid();
        var laterObserved = observed.AddMinutes(5);
        var second = await writer.UpsertAsync(new[]
        {
            new ExposureUpsertRow(tenantId, deviceId, vulnId, null, null, "1.0", "Cpe", laterObserved, run2),
        }, CancellationToken.None);

        second.Inserted.Should().Be(0);
        second.Reobserved.Should().Be(1);

        var stored = await db.DeviceVulnerabilityExposures.AsNoTracking().SingleAsync();
        stored.LastSeenRunId.Should().Be(run2);
        stored.LastObservedAt.Should().BeCloseTo(laterObserved, TimeSpan.FromSeconds(1));
        stored.Status.Should().Be(ExposureStatus.Open);
    }

    [Fact]
    public async Task ResolveStaleAsync_resolves_only_exposures_not_seen_in_current_run()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var (tenantId, deviceId, vulnA) = await SeedDeviceAndVuln(db);
        var vulnB = await SeedAnotherVuln(db);
        var writer = new PostgresBulkExposureWriter(db);

        var oldRun = Guid.NewGuid();
        await writer.UpsertAsync(new[]
        {
            new ExposureUpsertRow(tenantId, deviceId, vulnA, null, null, "1.0", "Cpe", DateTimeOffset.UtcNow, oldRun),
            new ExposureUpsertRow(tenantId, deviceId, vulnB, null, null, "1.0", "Cpe", DateTimeOffset.UtcNow, oldRun),
        }, CancellationToken.None);

        var newRun = Guid.NewGuid();
        await writer.UpsertAsync(new[]
        {
            new ExposureUpsertRow(tenantId, deviceId, vulnA, null, null, "1.0", "Cpe", DateTimeOffset.UtcNow, newRun),
        }, CancellationToken.None);

        var resolved = await writer.ResolveStaleAsync(tenantId, newRun, DateTimeOffset.UtcNow, CancellationToken.None);
        resolved.Should().Be(1);

        var byVuln = await db.DeviceVulnerabilityExposures.AsNoTracking()
            .ToDictionaryAsync(e => e.VulnerabilityId, e => e.Status);
        byVuln[vulnA].Should().Be(ExposureStatus.Open);
        byVuln[vulnB].Should().Be(ExposureStatus.Resolved);
    }

    // Helpers SeedDeviceAndVuln / SeedAnotherVuln create the minimum-valid Device,
    // Vulnerability, SourceSystem rows using the existing test data builders in
    // tests/PatchHound.Tests/TestData. Reuse those builders — do not hand-roll.
    private static Task<(Guid tenantId, Guid deviceId, Guid vulnId)> SeedDeviceAndVuln(PatchHoundDbContext db) => /* use TestData builders */ throw new NotImplementedException();
    private static Task<Guid> SeedAnotherVuln(PatchHoundDbContext db) => /* use TestData builders */ throw new NotImplementedException();
}
```

When wiring the seed helpers, mirror the patterns already in `tests/PatchHound.Tests/TestData/CanonicalSeed.cs` rather than creating new fixtures.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PostgresBulkExposureWriterTests"`
Expected: FAIL — `PostgresBulkExposureWriter` does not exist.

- [ ] **Step 4: Implement the writer**

```csharp
// src/PatchHound.Infrastructure/Services/Bulk/PostgresBulkExposureWriter.cs
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Bulk;

public sealed class PostgresBulkExposureWriter(PatchHoundDbContext db) : IBulkExposureWriter
{
    public async Task<BulkExposureUpsertResult> UpsertAsync(
        IReadOnlyCollection<ExposureUpsertRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0) return new BulkExposureUpsertResult(0, 0);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        // 1. COPY rows into a TEMP table.
        await using (var create = new NpgsqlCommand(@"
            CREATE TEMP TABLE IF NOT EXISTS _exposure_upsert (
                id uuid, tenant_id uuid, device_id uuid, vulnerability_id uuid,
                software_product_id uuid, installed_software_id uuid,
                matched_version text, match_source text, observed_at timestamptz,
                run_id uuid
            ) ON COMMIT DROP;
            TRUNCATE _exposure_upsert;", connection))
        {
            await create.ExecuteNonQueryAsync(ct);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY _exposure_upsert (id, tenant_id, device_id, vulnerability_id, software_product_id, installed_software_id, matched_version, match_source, observed_at, run_id) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var r in rows)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(r.TenantId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(r.DeviceId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(r.VulnerabilityId, NpgsqlDbType.Uuid, ct);
                if (r.SoftwareProductId is { } sp) await writer.WriteAsync(sp, NpgsqlDbType.Uuid, ct); else await writer.WriteNullAsync(ct);
                if (r.InstalledSoftwareId is { } isw) await writer.WriteAsync(isw, NpgsqlDbType.Uuid, ct); else await writer.WriteNullAsync(ct);
                await writer.WriteAsync(r.MatchedVersion, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(r.MatchSource, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(r.ObservedAt, NpgsqlDbType.TimestampTz, ct);
                await writer.WriteAsync(r.RunId, NpgsqlDbType.Uuid, ct);
            }
            await writer.CompleteAsync(ct);
        }

        // 2. Single set-based UPSERT. The xmax = 0 trick distinguishes inserts from updates.
        await using var merge = new NpgsqlCommand(@"
            WITH upsert AS (
                INSERT INTO ""DeviceVulnerabilityExposures""
                    (""Id"", ""TenantId"", ""DeviceId"", ""VulnerabilityId"",
                     ""SoftwareProductId"", ""InstalledSoftwareId"",
                     ""MatchedVersion"", ""MatchSource"", ""Status"",
                     ""FirstObservedAt"", ""LastObservedAt"", ""ResolvedAt"", ""LastSeenRunId"")
                SELECT id, tenant_id, device_id, vulnerability_id,
                       software_product_id, installed_software_id,
                       matched_version, match_source, 'Open',
                       observed_at, observed_at, NULL, run_id
                FROM _exposure_upsert
                ON CONFLICT (""TenantId"", ""DeviceId"", ""VulnerabilityId"")
                DO UPDATE SET
                    ""LastObservedAt"" = GREATEST(EXCLUDED.""LastObservedAt"", ""DeviceVulnerabilityExposures"".""LastObservedAt""),
                    ""Status""         = CASE WHEN ""DeviceVulnerabilityExposures"".""Status"" = 'Resolved'
                                              THEN 'Resolved'  -- respect direct-report resolution within the same cycle
                                              ELSE 'Open' END,
                    ""ResolvedAt""     = CASE WHEN ""DeviceVulnerabilityExposures"".""Status"" = 'Resolved'
                                              THEN ""DeviceVulnerabilityExposures"".""ResolvedAt""
                                              ELSE NULL END,
                    ""LastSeenRunId""  = EXCLUDED.""LastSeenRunId""
                RETURNING (xmax = 0) AS inserted
            )
            SELECT
                COUNT(*) FILTER (WHERE inserted) AS inserted_count,
                COUNT(*) FILTER (WHERE NOT inserted) AS updated_count
            FROM upsert;", connection);

        await using var reader = await merge.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var inserted = (int)(long)reader["inserted_count"];
        var updated = (int)(long)reader["updated_count"];
        return new BulkExposureUpsertResult(inserted, updated);
    }

    public async Task<int> ResolveStaleAsync(Guid tenantId, Guid runId, DateTimeOffset resolvedAt, CancellationToken ct)
    {
        return await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""DeviceVulnerabilityExposures""
            SET ""Status"" = 'Resolved', ""ResolvedAt"" = {resolvedAt}
            WHERE ""TenantId"" = {tenantId}
              AND ""Status"" = 'Open'
              AND (""LastSeenRunId"" IS DISTINCT FROM {runId})", ct);
    }
}
```

Register in DI (`src/PatchHound.Infrastructure/DependencyInjection.cs`):

```csharp
services.AddScoped<IBulkExposureWriter, PostgresBulkExposureWriter>();
```

The "respect direct-report resolution within the same cycle" branch preserves the existing behavior at `ExposureDerivationService.cs:74` where a direct-report source's resolution wins over derivation re-observation.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~PostgresBulkExposureWriterTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IBulkExposureWriter.cs \
        src/PatchHound.Core/Models/ExposureUpsertRow.cs \
        src/PatchHound.Infrastructure/Services/Bulk/PostgresBulkExposureWriter.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        tests/PatchHound.Tests/Infrastructure/Bulk/PostgresBulkExposureWriterTests.cs
git commit -m "feat: add PostgresBulkExposureWriter with batched UPSERT and run-scoped resolution"
```

---

## Task 3: Wire `IBulkExposureWriter` into `IngestionService.ProcessStagedResultsAsync`

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs` (lines around the existing exposure upsert loop near 1000–1100; locate via `grep -n "DeviceVulnerabilityExposures" src/PatchHound.Infrastructure/Services/IngestionService.cs` and run `gitnexus_impact({target: "ProcessStagedResultsAsync", direction: "upstream"})`)
- Modify: `tests/PatchHound.Tests/Infrastructure/Services/IngestionServicePhase3Tests.cs` (existing phase-3 tests must keep passing)
- Create: `tests/PatchHound.Tests/Infrastructure/Services/IngestionServiceBulkExposureTests.cs`

- [ ] **Step 1: Pre-edit impact check**

Run before any edit:
```bash
# in a terminal where the GitNexus MCP is configured
gitnexus_impact({target: "ProcessStagedResultsAsync", direction: "upstream"})
```
If the result is HIGH or CRITICAL, surface it to the user before continuing.

- [ ] **Step 2: Write the failing test**

```csharp
// tests/PatchHound.Tests/Infrastructure/Services/IngestionServiceBulkExposureTests.cs
[Fact]
public async Task ProcessStagedResultsAsync_uses_bulk_writer_to_upsert_exposures()
{
    // Arrange: NSubstitute IBulkExposureWriter; seed staged vulnerabilities + staged exposures
    // for a single device + single vuln using TestData builders.
    var bulkWriter = Substitute.For<IBulkExposureWriter>();
    bulkWriter.UpsertAsync(Arg.Any<IReadOnlyCollection<ExposureUpsertRow>>(), Arg.Any<CancellationToken>())
              .Returns(new BulkExposureUpsertResult(1, 0));

    // ... build IngestionService with bulkWriter injected ...

    await service.ProcessStagedResultsAsync(runId, tenantId, "defender", null, "Defender", CancellationToken.None);

    await bulkWriter.Received(1).UpsertAsync(
        Arg.Is<IReadOnlyCollection<ExposureUpsertRow>>(rows =>
            rows.Count == 1 && rows.First().RunId == runId),
        Arg.Any<CancellationToken>());
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~IngestionServiceBulkExposureTests"`
Expected: FAIL — service does not yet call `IBulkExposureWriter`.

- [ ] **Step 4: Refactor `ProcessStagedResultsAsync` to use the writer**

Inject `IBulkExposureWriter` via the constructor. Replace the per-row exposure upsert loop with:

```csharp
var rows = new List<ExposureUpsertRow>(stagedExposures.Count);
foreach (var staged in stagedExposures)
{
    if (!deviceIdByExternalId.TryGetValue(staged.AssetExternalId, out var deviceId)) continue;
    if (!vulnExternalIdToId.TryGetValue(staged.VulnerabilityExternalId, out var vulnId)) continue;

    var (productId, installedId) = installedByDeviceAndProduct
        .TryGetValue((deviceId, staged.CanonicalProductKey ?? string.Empty), out var hit)
        ? hit : (Guid?: null, Guid?: null);

    rows.Add(new ExposureUpsertRow(
        TenantId: tenantId,
        DeviceId: deviceId,
        VulnerabilityId: vulnId,
        SoftwareProductId: productId,
        InstalledSoftwareId: installedId,
        MatchedVersion: staged.MatchedVersion ?? string.Empty,
        MatchSource: nameof(ExposureMatchSource.Product),
        ObservedAt: now,
        RunId: ingestionRunId));
}

var bulkResult = await _bulkExposureWriter.UpsertAsync(rows, ct);
```

Remove the now-dead `db.DeviceVulnerabilityExposures.AddOrUpdate(...)`-style code and the explicit `SaveChangesAsync` that flushed it.

- [ ] **Step 5: Run all ingestion-related tests**

Run:
```
dotnet test --filter "FullyQualifiedName~IngestionService|FullyQualifiedName~IngestionStagingPipeline"
```
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionService.cs \
        tests/PatchHound.Tests/Infrastructure/Services/IngestionServiceBulkExposureTests.cs
git commit -m "refactor: route IngestionService exposure upserts through IBulkExposureWriter"
```

---

## Task 4: Bulk-write seam — `IBulkDeviceMergeWriter` (Step 2 of the issue)

Replace the per-device `SaveChangesAsync` and tracked-entity inserts in `StagedDeviceMergeService.MergeAsync` with a temp-table + set-based merge.

**Files:**
- Create: `src/PatchHound.Core/Interfaces/IBulkDeviceMergeWriter.cs`
- Create: `src/PatchHound.Core/Models/DeviceMergeRow.cs`, `InstalledSoftwareMergeRow.cs`
- Create: `src/PatchHound.Infrastructure/Services/Bulk/PostgresBulkDeviceMergeWriter.cs`
- Modify: `src/PatchHound.Infrastructure/Services/StagedDeviceMergeService.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/Bulk/PostgresBulkDeviceMergeWriterTests.cs`

- [ ] **Step 1: Define the seam**

```csharp
// src/PatchHound.Core/Models/DeviceMergeRow.cs
namespace PatchHound.Core.Models;

public sealed record DeviceMergeRow(
    Guid TenantId,
    Guid SourceSystemId,
    string ExternalId,
    string Name,
    string? ComputerDnsName,
    string? HealthStatus,
    string? OsPlatform,
    string? OsVersion,
    string? ExternalRiskLabel,
    DateTimeOffset? LastSeenAt,
    string? LastIpAddress,
    string? AadDeviceId,
    string? GroupId,
    string? GroupName,
    string? ExposureLevel,
    bool? IsAadJoined,
    string? OnboardingStatus,
    string? DeviceValue,
    bool IsActive);

public sealed record InstalledSoftwareMergeRow(
    Guid TenantId,
    Guid DeviceId,
    Guid SoftwareProductId,
    Guid SourceSystemId,
    string Version,
    DateTimeOffset ObservedAt);

public sealed record BulkDeviceMergeResult(int DevicesUpserted, int InstalledSoftwareUpserted);
```

```csharp
// src/PatchHound.Core/Interfaces/IBulkDeviceMergeWriter.cs
using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IBulkDeviceMergeWriter
{
    /// Returns the inserted/updated device IDs keyed by (SourceSystemId, ExternalId)
    /// so callers can resolve canonical IDs for downstream software-link processing.
    Task<IReadOnlyDictionary<(Guid SourceSystemId, string ExternalId), Guid>>
        UpsertDevicesAsync(IReadOnlyCollection<DeviceMergeRow> rows, CancellationToken ct);

    Task<int> UpsertInstalledSoftwareAsync(IReadOnlyCollection<InstalledSoftwareMergeRow> rows, CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing integration test**

```csharp
// tests/PatchHound.Tests/Infrastructure/Bulk/PostgresBulkDeviceMergeWriterTests.cs
[Fact]
public async Task UpsertDevicesAsync_inserts_then_updates_in_one_round_trip()
{
    await _fx.ResetAsync();
    await using var db = _fx.CreateDbContext();
    var sourceSystemId = await SeedSourceSystem(db, "defender");
    var tenantId = Guid.NewGuid();
    var writer = new PostgresBulkDeviceMergeWriter(db);

    var rows = new[]
    {
        new DeviceMergeRow(tenantId, sourceSystemId, "ext-1", "host-1", null, "Active",
            "Windows", "10", null, DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null, true),
        new DeviceMergeRow(tenantId, sourceSystemId, "ext-2", "host-2", null, "Active",
            "Linux", "Ubuntu 22.04", null, DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null, true),
    };

    var ids = await writer.UpsertDevicesAsync(rows, CancellationToken.None);
    ids.Should().HaveCount(2);

    var stored = await db.Devices.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    stored.Should().HaveCount(2);
    stored.Should().OnlyContain(d => d.IsActiveInTenant);

    // Second upsert with the same external IDs should update, not duplicate.
    var rerun = await writer.UpsertDevicesAsync(rows, CancellationToken.None);
    rerun.Should().HaveCount(2);
    (await db.Devices.IgnoreQueryFilters().CountAsync()).Should().Be(2);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PostgresBulkDeviceMergeWriterTests"`
Expected: FAIL.

- [ ] **Step 4: Implement the writer**

Pattern matches Task 2: `CREATE TEMP TABLE` for `_device_upsert` and `_installed_upsert`, `COPY ... FROM STDIN (FORMAT BINARY)`, then a single `INSERT ... ON CONFLICT (TenantId, SourceSystemId, ExternalId) DO UPDATE SET <all-inventory-fields>` with `RETURNING "Id", "SourceSystemId", "ExternalId"`. The returned rows populate the dictionary the interface returns.

Devices unique key per `DeviceConfiguration`: confirm via `grep -n "HasIndex\|IsUnique" src/PatchHound.Infrastructure/Data/Configurations/DeviceConfiguration.cs` before writing the `ON CONFLICT` clause — use whichever composite unique index exists, fail loudly if none matches.

- [ ] **Step 5: Refactor `StagedDeviceMergeService.MergeAsync` to use the writer**

Replace Pass 1's per-device loop body with: build a `List<DeviceMergeRow>` from `stagedDevices` (skipping stale+inactive new devices, exactly as the current code at line 122), call `UpsertDevicesAsync` once, then thread the returned `(SourceSystemId, ExternalId) → Guid` dictionary into Pass 2. Remove the mid-method `SaveChangesAsync` at line 198.

For Pass 2, build a `List<InstalledSoftwareMergeRow>` and call `UpsertInstalledSoftwareAsync` once. Resolve `SoftwareProductId` via the existing `ISoftwareProductResolver` (the resolver itself is N+1 today — that's addressed in Task 8; out of scope here, but if low-effort, batch the resolver's lookups in this task too).

Replace the final `SaveChangesAsync` at line 294 with nothing — the writer is the persistence boundary.

- [ ] **Step 6: Run merge-service tests**

Run: `dotnet test --filter "FullyQualifiedName~StagedDeviceMerge|FullyQualifiedName~IngestionStagingPipeline"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IBulkDeviceMergeWriter.cs \
        src/PatchHound.Core/Models/DeviceMergeRow.cs \
        src/PatchHound.Core/Models/InstalledSoftwareMergeRow.cs \
        src/PatchHound.Infrastructure/Services/Bulk/PostgresBulkDeviceMergeWriter.cs \
        src/PatchHound.Infrastructure/Services/StagedDeviceMergeService.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        tests/PatchHound.Tests/Infrastructure/Bulk/PostgresBulkDeviceMergeWriterTests.cs
git commit -m "feat: bulk device/installed-software merge via temp-table + ON CONFLICT"
```

---

## Task 5: CTE-based exposure derivation (Step 3 of the issue)

Replace the in-memory O(n²) cross-join in `ExposureDerivationService.DeriveForTenantAsync` (lines 20–101) with a single CTE that derives the active exposure set server-side and feeds the bulk writer.

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/ExposureDerivationService.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/Services/ExposureDerivationCteTests.cs`

- [ ] **Step 1: Pre-edit impact check**

```
gitnexus_impact({target: "ExposureDerivationService.DeriveForTenantAsync", direction: "upstream"})
```

- [ ] **Step 2: Write the failing test**

```csharp
// tests/PatchHound.Tests/Infrastructure/Services/ExposureDerivationCteTests.cs
[Fact]
public async Task DeriveForTenantAsync_uses_single_CTE_to_produce_exposure_rows()
{
    await _fx.ResetAsync();
    await using var db = _fx.CreateDbContext();

    // Seed: 1 tenant, 2 devices, 1 software product, 1 vulnerability with a matching applicability,
    // installed software linking device→product at a vulnerable version.
    var seed = await CanonicalSeed.SeedDerivationScenarioAsync(db); // add helper if missing

    var sut = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance,
        new PostgresBulkExposureWriter(db));

    var runId = Guid.NewGuid();
    var result = await sut.DeriveForTenantAsync(seed.TenantId, DateTimeOffset.UtcNow, runId, CancellationToken.None);

    result.Inserted.Should().Be(2); // one exposure per device
    (await db.DeviceVulnerabilityExposures.CountAsync()).Should().Be(2);
}

[Fact]
public async Task DeriveForTenantAsync_resolves_exposures_no_longer_active()
{
    // Run derivation once with both devices, then again with one device removed
    // from the install set, and assert the missing-device exposure is resolved.
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ExposureDerivationCteTests"`
Expected: FAIL — constructor signature and CTE behavior don't yet exist.

- [ ] **Step 4: Refactor `DeriveForTenantAsync`**

Replace the body with a single CTE query that returns `(DeviceId, VulnerabilityId, SoftwareProductId, InstalledSoftwareId, MatchedVersion, MatchSource)` rows for the active set, materialize into `ExposureUpsertRow`s, call `_bulk.UpsertAsync(rows, ct)`, then call `_bulk.ResolveStaleAsync(tenantId, runId, observedAt, ct)`.

```csharp
public async Task<ExposureDerivationResult> DeriveForTenantAsync(
    Guid tenantId,
    DateTimeOffset observedAt,
    Guid runId,
    CancellationToken ct)
{
    var rows = await db.Database.SqlQueryRaw<DerivedExposureRow>(@"
        WITH active_installs AS (
            SELECT i.""Id"" AS installed_software_id,
                   i.""DeviceId"" AS device_id,
                   i.""SoftwareProductId"" AS software_product_id,
                   i.""Version"" AS matched_version,
                   p.""PrimaryCpe23Uri"" AS product_cpe
            FROM ""InstalledSoftware"" i
            LEFT JOIN ""SoftwareProducts"" p ON p.""Id"" = i.""SoftwareProductId""
            WHERE i.""TenantId"" = {0}
        ),
        applicable AS (
            SELECT a.""VulnerabilityId"" AS vulnerability_id,
                   a.""SoftwareProductId"" AS software_product_id,
                   a.""CpeCriteria"" AS cpe_criteria,
                   a.""VersionStartIncluding"", a.""VersionStartExcluding"",
                   a.""VersionEndIncluding"", a.""VersionEndExcluding""
            FROM ""VulnerabilityApplicabilities"" a
            WHERE a.""Vulnerable"" = TRUE
        )
        SELECT ai.device_id, app.vulnerability_id, ai.software_product_id,
               ai.installed_software_id, ai.matched_version,
               CASE WHEN app.software_product_id IS NOT NULL THEN 'Product' ELSE 'Cpe' END AS match_source
        FROM active_installs ai
        JOIN applicable app
          ON (app.software_product_id = ai.software_product_id)
          OR (app.software_product_id IS NULL
              AND app.cpe_criteria IS NOT NULL
              AND lower(app.cpe_criteria) = lower(ai.product_cpe));", tenantId).ToListAsync(ct);

    // Version-range predicate is still applied client-side for now because PostgreSQL has no
    // built-in semver comparator. (Future: extract into a SQL function — see TODO in issue #76.)
    var filtered = rows.Where(r => ExposureDerivationService.VersionMatches(
        r.MatchedVersion, /* applicability fields from the row */)).ToList();

    var upsertRows = filtered.Select(r => new ExposureUpsertRow(
        TenantId: tenantId, DeviceId: r.DeviceId, VulnerabilityId: r.VulnerabilityId,
        SoftwareProductId: r.SoftwareProductId, InstalledSoftwareId: r.InstalledSoftwareId,
        MatchedVersion: r.MatchedVersion ?? string.Empty,
        MatchSource: r.MatchSource, ObservedAt: observedAt, RunId: runId)).ToList();

    var bulk = await _bulk.UpsertAsync(upsertRows, ct);
    var resolved = await _bulk.ResolveStaleAsync(tenantId, runId, observedAt, ct);

    return new ExposureDerivationResult(bulk.Inserted, bulk.Reobserved, resolved);
}
```

If during implementation the version-range predicate proves cheap to express in SQL (`numeric_version` comparison helpers exist for the dataset), inline it. Otherwise leave the client-side filter — it operates on the already-narrowed CTE output, so it is O(matches) not O(devices × vulns).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ExposureDerivation"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/ExposureDerivationService.cs \
        tests/PatchHound.Tests/Infrastructure/Services/ExposureDerivationCteTests.cs
git commit -m "refactor: derive exposures via single CTE + bulk writer instead of in-memory join"
```

---

## Task 6: Run-scoped episode sync (Step 4 of the issue)

Episode sync today loads every exposure for the tenant (line 12–14 of `ExposureEpisodeService.cs`). Scope it to exposures whose `LastSeenRunId == runId` OR whose status just transitioned to `Resolved` in this run (i.e., their `ResolvedAt >= run-start`).

**Files:**
- Modify: `src/PatchHound.Core/Interfaces/IExposureEpisodeService.cs` (add `runId` parameter — verify the interface file exists first; if not, change just the class)
- Modify: `src/PatchHound.Infrastructure/Services/ExposureEpisodeService.cs`
- Modify: callers (`IngestionService.RunExposureDerivationAsync` is the main one)
- Create: `tests/PatchHound.Tests/Infrastructure/Services/ExposureEpisodeServiceRunScopedTests.cs`

- [ ] **Step 1: Pre-edit impact check**

```
gitnexus_impact({target: "SyncEpisodesForTenantAsync", direction: "upstream"})
```

- [ ] **Step 2: Write the failing test**

```csharp
[Fact]
public async Task SyncEpisodesForTenantAsync_only_touches_run_scoped_exposures()
{
    await _fx.ResetAsync();
    await using var db = _fx.CreateDbContext();

    var (tenantId, runA, runB, exposureA, exposureB) = await CanonicalSeed.SeedTwoRunsAsync(db);
    // exposureA was seen in runA only; exposureB only in runB.

    var sut = new ExposureEpisodeService(db);
    await sut.SyncEpisodesForTenantAsync(tenantId, runB, DateTimeOffset.UtcNow, CancellationToken.None);

    var episodes = await db.ExposureEpisodes.AsNoTracking().ToListAsync();
    episodes.Should().ContainSingle()
        .Which.DeviceVulnerabilityExposureId.Should().Be(exposureB);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ExposureEpisodeServiceRunScopedTests"`
Expected: FAIL — `SyncEpisodesForTenantAsync` does not accept a `runId` parameter.

- [ ] **Step 4: Refactor the service**

```csharp
public async Task SyncEpisodesForTenantAsync(Guid tenantId, Guid runId, DateTimeOffset now, CancellationToken ct)
{
    var exposures = await db.DeviceVulnerabilityExposures
        .Where(e => e.TenantId == tenantId
                 && (e.LastSeenRunId == runId
                     || (e.Status == ExposureStatus.Resolved && e.ResolvedAt >= now.AddDays(-1)))) // resolved-this-run guard
        .ToListAsync(ct);
    // ... existing per-exposure logic unchanged ...
}
```

The `now.AddDays(-1)` guard is a safety net for resolved-this-run exposures whose `LastSeenRunId` still points at the prior run. If the codebase already tracks an `IngestionRun.StartedAt`, use that timestamp instead and remove the heuristic.

Update `IngestionService.RunExposureDerivationAsync` and any other caller to thread `runId`. Find call sites:

```bash
grep -rn "SyncEpisodesForTenantAsync" src/
```

- [ ] **Step 5: Batch-insert new episodes**

While here, replace the row-by-row `db.ExposureEpisodes.Add(...)` loop with a single `db.ExposureEpisodes.AddRange(...)` (EF still flushes them in one INSERT batch via Npgsql). No interface change required; this is purely an internal optimization.

- [ ] **Step 6: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~ExposureEpisodeService"`
Expected: PASS (existing `ExposureEpisodeServiceTests.cs` must be updated to pass `runId` — make the minimum diff there).

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/ExposureEpisodeService.cs \
        src/PatchHound.Infrastructure/Services/IngestionService.cs \
        tests/PatchHound.Tests/Infrastructure/Services/ExposureEpisodeServiceTests.cs \
        tests/PatchHound.Tests/Infrastructure/Services/ExposureEpisodeServiceRunScopedTests.cs
git commit -m "perf: scope episode sync to current run via LastSeenRunId"
```

---

## Task 7: Vulnerability reference reconciliation bulk path (Step 5 of the issue)

Replace the N+1 reference + applicability lookups inside the staged-vuln loop in `IngestionService.ProcessStagedResultsAsync` (the per-row `VulnerabilityResolver.ResolveAsync` call at line ~952). Introduce a bulk reference reconciler.

**Files:**
- Create: `src/PatchHound.Core/Interfaces/IBulkVulnerabilityReferenceWriter.cs`
- Create: `src/PatchHound.Core/Models/VulnerabilityReferenceUpsertRow.cs`, `ApplicabilityUpsertRow.cs`
- Create: `src/PatchHound.Infrastructure/Services/Bulk/PostgresBulkVulnerabilityReferenceWriter.cs`
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/Bulk/PostgresBulkVulnerabilityReferenceWriterTests.cs`

The issue suggests a stored procedure for this. **Don't use a stored procedure** — keep the SQL inline in C# (same temp-table + `INSERT ... ON CONFLICT` pattern as Task 2), because stored procedures fragment schema state across EF migrations and Postgres, and the win is identical. If the user explicitly wants a `CREATE FUNCTION`, add it as a follow-up.

- [ ] **Step 1: Define seam DTOs**

```csharp
// src/PatchHound.Core/Models/VulnerabilityReferenceUpsertRow.cs
namespace PatchHound.Core.Models;

public sealed record VulnerabilityReferenceUpsertRow(
    Guid VulnerabilityId,
    string Url,
    string? Source,
    string? Tags);

public sealed record ApplicabilityUpsertRow(
    Guid VulnerabilityId,
    Guid? SoftwareProductId,
    string? CpeCriteria,
    string? VersionStartIncluding,
    string? VersionStartExcluding,
    string? VersionEndIncluding,
    string? VersionEndExcluding,
    bool Vulnerable);
```

```csharp
// src/PatchHound.Core/Interfaces/IBulkVulnerabilityReferenceWriter.cs
public interface IBulkVulnerabilityReferenceWriter
{
    Task UpsertReferencesAsync(IReadOnlyCollection<VulnerabilityReferenceUpsertRow> rows, CancellationToken ct);
    Task ReplaceApplicabilitiesAsync(Guid vulnerabilityId, IReadOnlyCollection<ApplicabilityUpsertRow> rows, CancellationToken ct);
    Task ReplaceApplicabilitiesBulkAsync(IReadOnlyCollection<ApplicabilityUpsertRow> rows, CancellationToken ct);
}
```

The `ReplaceApplicabilitiesBulkAsync` overload deletes-then-inserts in a single transaction across many vulnerability IDs in one round trip.

- [ ] **Step 2: Write the failing test**

```csharp
[Fact]
public async Task UpsertReferencesAsync_inserts_new_and_skips_duplicates()
{
    await _fx.ResetAsync();
    await using var db = _fx.CreateDbContext();
    var vulnId = await CanonicalSeed.SeedVulnAsync(db);
    var writer = new PostgresBulkVulnerabilityReferenceWriter(db);

    await writer.UpsertReferencesAsync(new[]
    {
        new VulnerabilityReferenceUpsertRow(vulnId, "https://example.com/a", "vendor", null),
        new VulnerabilityReferenceUpsertRow(vulnId, "https://example.com/a", "vendor", null), // dup
        new VulnerabilityReferenceUpsertRow(vulnId, "https://example.com/b", "vendor", null),
    }, CancellationToken.None);

    (await db.VulnerabilityReferences.CountAsync(r => r.VulnerabilityId == vulnId))
        .Should().Be(2);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PostgresBulkVulnerabilityReferenceWriterTests"`
Expected: FAIL.

- [ ] **Step 4: Implement the writer**

Same temp-table + `COPY` + `INSERT ... ON CONFLICT (VulnerabilityId, Url) DO NOTHING` shape as Task 2. For `ReplaceApplicabilitiesBulkAsync`, use a single transaction: `DELETE FROM VulnerabilityApplicabilities WHERE VulnerabilityId = ANY(@ids)` then `COPY` + `INSERT` the new set.

Confirm the unique index on `VulnerabilityReferences` matches the `ON CONFLICT` target before writing the SQL:
```bash
grep -n "HasIndex\|IsUnique" src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityReferenceConfiguration.cs
```

- [ ] **Step 5: Refactor `ProcessStagedResultsAsync` to call the bulk writer once per run**

After Step 1's vulnerability upsert batch loop, build a single `List<VulnerabilityReferenceUpsertRow>` for the whole run and call `UpsertReferencesAsync` once. Same for applicabilities. The existing `VulnerabilityResolver.ResolveAsync` continues to upsert the `Vulnerability` row itself (out of scope for this task), but stops creating reference/applicability rows.

- [ ] **Step 6: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~VulnerabilityReference|FullyQualifiedName~IngestionService"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IBulkVulnerabilityReferenceWriter.cs \
        src/PatchHound.Core/Models/VulnerabilityReferenceUpsertRow.cs \
        src/PatchHound.Core/Models/ApplicabilityUpsertRow.cs \
        src/PatchHound.Infrastructure/Services/Bulk/PostgresBulkVulnerabilityReferenceWriter.cs \
        src/PatchHound.Infrastructure/Services/IngestionService.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        tests/PatchHound.Tests/Infrastructure/Bulk/PostgresBulkVulnerabilityReferenceWriterTests.cs
git commit -m "perf: bulk-upsert vulnerability references and applicabilities per run"
```

---

## Task 8: Batch `NormalizedSoftwareProjectionService` (Step 6 of the issue)

Replace the per-row `AddAsync` + dual `SaveChangesAsync` in `NormalizedSoftwareProjectionService.SyncTenantAsync` with a single `INSERT ... ON CONFLICT DO UPDATE` for `SoftwareTenantRecords` and another for `SoftwareProductInstallations`. Also eliminate the N+1 `SourceSystems` lookup inside `UpsertSoftwareInstallationsAsync` by joining server-side.

**Files:**
- Create: `src/PatchHound.Core/Interfaces/IBulkSoftwareProjectionWriter.cs`
- Create: `src/PatchHound.Infrastructure/Services/Bulk/PostgresBulkSoftwareProjectionWriter.cs`
- Modify: `src/PatchHound.Infrastructure/Services/NormalizedSoftwareProjectionService.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/Bulk/PostgresBulkSoftwareProjectionWriterTests.cs`

- [ ] **Step 1: Define the seam**

```csharp
public interface IBulkSoftwareProjectionWriter
{
    Task SyncTenantSoftwareAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct);
    Task SyncSoftwareInstallationsAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct);
}
```

Unlike Tasks 2/4/7, this seam takes no row collection — the source of truth is `InstalledSoftware`, already in the DB. The implementation expresses the entire projection as set-based SQL.

- [ ] **Step 2: Write the failing test**

```csharp
[Fact]
public async Task SyncTenantAsync_projects_install_set_into_tenant_software_and_installations_in_one_round_trip()
{
    await _fx.ResetAsync();
    await using var db = _fx.CreateDbContext();
    var (tenantId, productId) = await CanonicalSeed.SeedInstallsForProjectionAsync(db, deviceCount: 3);

    var sut = new NormalizedSoftwareProjectionService(db, new PostgresBulkSoftwareProjectionWriter(db));
    await sut.SyncTenantAsync(tenantId, snapshotId: null, CancellationToken.None);

    (await db.SoftwareTenantRecords.IgnoreQueryFilters().CountAsync(r => r.TenantId == tenantId))
        .Should().Be(1);
    (await db.SoftwareProductInstallations.IgnoreQueryFilters()
        .CountAsync(i => i.TenantId == tenantId && i.IsActive))
        .Should().Be(3);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PostgresBulkSoftwareProjectionWriterTests"`
Expected: FAIL.

- [ ] **Step 4: Implement the writer**

```sql
-- SyncTenantSoftwareAsync
INSERT INTO "SoftwareTenantRecords" ("Id", "TenantId", "SnapshotId", "SoftwareProductId", "FirstSeenAt", "LastSeenAt", ...)
SELECT gen_random_uuid(), i."TenantId", @snapshotId, i."SoftwareProductId",
       MIN(i."FirstSeenAt"), MAX(i."LastSeenAt"), ...
FROM "InstalledSoftware" i
WHERE i."TenantId" = @tenantId
GROUP BY i."TenantId", i."SoftwareProductId"
ON CONFLICT ("TenantId", "SnapshotId", "SoftwareProductId") DO UPDATE SET
    "FirstSeenAt" = LEAST(EXCLUDED."FirstSeenAt", "SoftwareTenantRecords"."FirstSeenAt"),
    "LastSeenAt"  = GREATEST(EXCLUDED."LastSeenAt", "SoftwareTenantRecords"."LastSeenAt");

-- Stale-row delete: tenant rows whose product no longer has any install
DELETE FROM "SoftwareTenantRecords" r
WHERE r."TenantId" = @tenantId AND r."SnapshotId" IS NOT DISTINCT FROM @snapshotId
  AND NOT EXISTS (SELECT 1 FROM "InstalledSoftware" i
                  WHERE i."TenantId" = r."TenantId" AND i."SoftwareProductId" = r."SoftwareProductId");
```

The `SyncSoftwareInstallationsAsync` variant joins `InstalledSoftware → SoftwareTenantRecords → SourceSystems` in a single CTE and `INSERT ... ON CONFLICT` over `SoftwareAssetId`. Confirm conflict-key columns from `SoftwareTenantRecordConfiguration` / `SoftwareProductInstallationConfiguration` before writing.

- [ ] **Step 5: Refactor `NormalizedSoftwareProjectionService` to delegate to the writer**

The service shrinks to:

```csharp
public async Task SyncTenantAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
{
    await _writer.SyncTenantSoftwareAsync(tenantId, snapshotId, ct);
    await _writer.SyncSoftwareInstallationsAsync(tenantId, snapshotId, ct);
}
```

No `dbContext.SaveChangesAsync` calls remain. Remove `ResolveSourceSystem` — the SQL JOIN replaces it.

- [ ] **Step 6: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~NormalizedSoftwareProjection|FullyQualifiedName~SoftwareProjection"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IBulkSoftwareProjectionWriter.cs \
        src/PatchHound.Infrastructure/Services/Bulk/PostgresBulkSoftwareProjectionWriter.cs \
        src/PatchHound.Infrastructure/Services/NormalizedSoftwareProjectionService.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        tests/PatchHound.Tests/Infrastructure/Bulk/PostgresBulkSoftwareProjectionWriterTests.cs
git commit -m "perf: project normalized software via set-based SQL instead of EF loops"
```

---

## Task 9: Full-pipeline integration test + final verification

**Files:**
- Create: `tests/PatchHound.Tests/Infrastructure/Services/IngestionPipelineE2EBulkPathTests.cs`

- [ ] **Step 1: Write an end-to-end test that exercises Tasks 2–8 together**

```csharp
[Collection(PostgresCollection.Name)]
public class IngestionPipelineE2EBulkPathTests
{
    [Fact]
    public async Task Full_run_with_500_devices_and_50_vulns_completes_and_produces_expected_state()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();

        // Seed staged tables with 500 devices × 50 vulns using existing TestData builders.
        await CanonicalSeed.SeedLargeIngestionRunAsync(db, deviceCount: 500, vulnCount: 50);

        var serviceProvider = BuildServiceProvider(db);
        var ingestion = serviceProvider.GetRequiredService<IngestionService>();

        var ok = await ingestion.RunIngestionAsync(tenantId, ct: CancellationToken.None);
        ok.Should().BeTrue();

        (await db.Devices.IgnoreQueryFilters().CountAsync()).Should().Be(500);
        (await db.DeviceVulnerabilityExposures.CountAsync()).Should().BeGreaterThan(0);
        (await db.ExposureEpisodes.CountAsync()).Should().BeGreaterThan(0);
    }
}
```

This test is the safety net for the seam-by-seam refactors. Keep the dataset small enough to run in <30s in CI; the perf gain is measurable in the existing benchmarks (or via `dotnet run --project src/PatchHound.Worker` against the test container if a benchmark harness is added later — out of scope here per user choice).

- [ ] **Step 2: Run the full test suite**

```bash
dotnet test PatchHound.slnx -v minimal
```
Expected: PASS across the suite.

- [ ] **Step 3: Run GitNexus change-scope check**

```
gitnexus_detect_changes({scope: "all"})
```
Confirm the affected scope matches: `IngestionService`, `StagedDeviceMergeService`, `ExposureDerivationService`, `ExposureEpisodeService`, `NormalizedSoftwareProjectionService`, the new `Bulk/` writers, and tests. Any unexpected files in the diff must be investigated before committing.

- [ ] **Step 4: Re-index GitNexus**

```bash
npx gitnexus analyze
```
(use `--embeddings` if `.gitnexus/meta.json` shows existing embeddings — see CLAUDE.md).

- [ ] **Step 5: Commit and open PR**

```bash
git add tests/PatchHound.Tests/Infrastructure/Services/IngestionPipelineE2EBulkPathTests.cs
git commit -m "test: end-to-end coverage of bulk-path ingestion pipeline"
```

Open the PR with a body that links #76 and summarizes which of the issue's six steps each commit implements (Task 2 → Step 1, Task 4 → Step 2, Task 5 → Step 3, Task 6 → Step 4, Task 7 → Step 5, Task 8 → Step 6).

---

## Task 10: Standalone ingestion benchmark tool

A runnable console app that seeds staged data at configurable scale, runs the ingestion pipeline against a fresh Testcontainers Postgres, and reports per-stage timings. **Not** invoked by `dotnet test` — runs only via explicit `dotnet run`.

**Files:**
- Create: `benchmarks/PatchHound.IngestionBenchmark/PatchHound.IngestionBenchmark.csproj`
- Create: `benchmarks/PatchHound.IngestionBenchmark/Program.cs`
- Create: `benchmarks/PatchHound.IngestionBenchmark/BenchmarkOptions.cs`
- Create: `benchmarks/PatchHound.IngestionBenchmark/BenchmarkSeeder.cs`
- Create: `benchmarks/PatchHound.IngestionBenchmark/StageTimings.cs`
- Create: `benchmarks/PatchHound.IngestionBenchmark/README.md`
- Modify: `PatchHound.slnx` (add the new project so `dotnet build PatchHound.slnx` still covers it, but the project itself is `<IsPackable>false</IsPackable>` and not referenced by Api/Worker)

- [ ] **Step 1: Create the project file**

```xml
<!-- benchmarks/PatchHound.IngestionBenchmark/PatchHound.IngestionBenchmark.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>PatchHound.IngestionBenchmark</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\PatchHound.Core\PatchHound.Core.csproj" />
    <ProjectReference Include="..\..\src\PatchHound.Infrastructure\PatchHound.Infrastructure.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="9.0.0" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.11.0" />
  </ItemGroup>
</Project>
```

Add the project to `PatchHound.slnx`. Verify versions of `Microsoft.Extensions.*` match the rest of the solution before committing — adjust if the solution is on a different minor.

- [ ] **Step 2: Define the CLI options model**

```csharp
// benchmarks/PatchHound.IngestionBenchmark/BenchmarkOptions.cs
namespace PatchHound.IngestionBenchmark;

public sealed record BenchmarkOptions(
    int TenantCount,
    int DevicesPerTenant,
    int VulnsPerDevice,
    int SoftwarePerDevice,
    int Runs)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        int Get(string key, int @default) =>
            args.FirstOrDefault(a => a.StartsWith($"--{key}=", StringComparison.OrdinalIgnoreCase)) is { } hit
                ? int.Parse(hit[(key.Length + 3)..])
                : @default;

        return new BenchmarkOptions(
            TenantCount:       Get("tenants", 1),
            DevicesPerTenant:  Get("devices", 100),
            VulnsPerDevice:    Get("vulns-per-device", 10),
            SoftwarePerDevice: Get("software-per-device", 5),
            Runs:              Get("runs", 1));
    }

    public int TotalDevices => TenantCount * DevicesPerTenant;
    public int TotalStagedExposures => TotalDevices * VulnsPerDevice;
}
```

Every flag has a default so the tool runs with `dotnet run` and no arguments — a smoke run, useful for verifying the harness works before scaling up.

- [ ] **Step 3: Define the per-stage timings record**

```csharp
// benchmarks/PatchHound.IngestionBenchmark/StageTimings.cs
namespace PatchHound.IngestionBenchmark;

public sealed record StageTimings(
    TimeSpan Staging,
    TimeSpan Merge,
    TimeSpan ExposureDerivation,
    TimeSpan EpisodeSync,
    TimeSpan SoftwareProjection,
    TimeSpan Total)
{
    public void PrintTo(TextWriter writer, int runIndex, BenchmarkOptions opts)
    {
        static string Fmt(TimeSpan t) => $"{t.TotalMilliseconds,9:N0} ms";
        writer.WriteLine($"Run #{runIndex + 1} of {opts.Runs}  (tenant=1/{opts.TenantCount})");
        writer.WriteLine($"  Staging              {Fmt(Staging)}");
        writer.WriteLine($"  Device merge         {Fmt(Merge)}");
        writer.WriteLine($"  Exposure derivation  {Fmt(ExposureDerivation)}");
        writer.WriteLine($"  Episode sync         {Fmt(EpisodeSync)}");
        writer.WriteLine($"  Software projection  {Fmt(SoftwareProjection)}");
        writer.WriteLine($"  ─────────────────────────────");
        writer.WriteLine($"  TOTAL                {Fmt(Total)}");
    }
}
```

Per-stage capture relies on `IngestionService` exposing timing hooks. If it doesn't already, the simplest approach is to wrap the existing stage entry points (`IngestionStagingPipeline`, `IStagedDeviceMergeService`, `ExposureDerivationService`, `ExposureEpisodeService`, `NormalizedSoftwareProjectionService`) with timing decorators registered in DI for the benchmark only — do not modify production services to emit telemetry just for the benchmark.

- [ ] **Step 4: Implement the seeder**

```csharp
// benchmarks/PatchHound.IngestionBenchmark/BenchmarkSeeder.cs
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Core.Entities;

namespace PatchHound.IngestionBenchmark;

public static class BenchmarkSeeder
{
    public static async Task SeedStagedRunAsync(
        PatchHoundDbContext db,
        Guid tenantId,
        Guid ingestionRunId,
        Guid sourceSystemId,
        BenchmarkOptions opts,
        CancellationToken ct)
    {
        // Bulk-insert staged devices, staged software, staged device-software links,
        // staged vulnerabilities, and staged exposures using Npgsql COPY against the
        // raw staging tables. Do NOT use EF tracked entities here — the benchmark
        // measures the pipeline, not the seeder.

        // Reuse the StagedDevice / StagedVulnerability schema from
        // src/PatchHound.Infrastructure/Data/Configurations/ — keep PayloadJson
        // minimal but valid for IngestionAsset / IngestionResult deserialization.

        // ... implementation: for each device, write
        //   - one StagedDevice (AssetType.Device)
        //   - opts.SoftwarePerDevice StagedDevice rows (AssetType.Software)
        //   - opts.SoftwarePerDevice StagedDeviceSoftwareInstallation rows
        //   - opts.VulnsPerDevice StagedVulnerability + StagedVulnerabilityExposure rows
    }
}
```

Implementation detail: keep `ExternalId` deterministic (`$"bench-device-{tenant}-{i}"`, `$"bench-vuln-{i}"`) so re-runs against the same DB exercise the UPSERT path rather than only inserts. This is what makes `--runs=N` meaningful.

- [ ] **Step 5: Implement the entrypoint**

```csharp
// benchmarks/PatchHound.IngestionBenchmark/Program.cs
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.IngestionBenchmark;
using Testcontainers.PostgreSql;

var opts = BenchmarkOptions.Parse(args);
Console.WriteLine($"PatchHound ingestion benchmark");
Console.WriteLine($"  Tenants: {opts.TenantCount}");
Console.WriteLine($"  Devices/tenant: {opts.DevicesPerTenant}");
Console.WriteLine($"  Vulns/device: {opts.VulnsPerDevice}");
Console.WriteLine($"  Software/device: {opts.SoftwarePerDevice}");
Console.WriteLine($"  Runs: {opts.Runs}");
Console.WriteLine($"  Total staged exposures/run: {opts.TotalStagedExposures:N0}");
Console.WriteLine();

await using var pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
Console.Write("Starting Postgres container... ");
var swContainer = Stopwatch.StartNew();
await pg.StartAsync();
Console.WriteLine($"done ({swContainer.ElapsedMilliseconds} ms)");

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddDbContext<PatchHoundDbContext>(o => o.UseNpgsql(pg.GetConnectionString()));
// Register the same services Api/Worker register. Easiest: call the existing
// PatchHound.Infrastructure.DependencyInjection.AddInfrastructure(...) extension
// with a benchmark-flavoured configuration.
services.AddPatchHoundInfrastructureForBenchmark(pg.GetConnectionString());

await using var root = services.BuildServiceProvider();
await using (var bootstrap = root.CreateAsyncScope())
{
    var db = bootstrap.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
    await db.Database.MigrateAsync();
}

var tenants = Enumerable.Range(0, opts.TenantCount).Select(_ => Guid.NewGuid()).ToList();

for (var runIdx = 0; runIdx < opts.Runs; runIdx++)
{
    foreach (var tenantId in tenants)
    {
        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var ingestion = scope.ServiceProvider.GetRequiredService<IngestionService>();

        var runId = Guid.NewGuid();
        var sourceSystemId = await EnsureSourceSystemAsync(db);

        var swSeed = Stopwatch.StartNew();
        await BenchmarkSeeder.SeedStagedRunAsync(db, tenantId, runId, sourceSystemId, opts, default);
        swSeed.Stop();

        // The timing decorators (registered in AddPatchHoundInfrastructureForBenchmark)
        // capture per-stage timings into a scope-resolved StageTimingsCollector.
        var collector = scope.ServiceProvider.GetRequiredService<StageTimingsCollector>();
        collector.Reset();

        var swTotal = Stopwatch.StartNew();
        var ok = await ingestion.RunIngestionAsync(tenantId, ct: default);
        swTotal.Stop();

        if (!ok) { Console.Error.WriteLine($"Run failed for tenant {tenantId}"); continue; }

        collector.Build(total: swTotal.Elapsed).PrintTo(Console.Out, runIdx, opts);
        Console.WriteLine($"  (seed: {swSeed.ElapsedMilliseconds} ms — excluded from totals)");
        Console.WriteLine();
    }
}
```

The `AddPatchHoundInfrastructureForBenchmark` extension and `StageTimingsCollector` are local to this project — they register the production services then wrap each stage entry point with a `TimingDecorator` that writes its elapsed time into the collector. Decorators are inert in production code.

`EnsureSourceSystemAsync` upserts a single `defender` source system row on first call (cache the ID for subsequent tenants).

- [ ] **Step 6: Write the README**

```markdown
# PatchHound ingestion benchmark

Spins up an ephemeral Postgres in Docker, seeds staged ingestion data at the
configured scale, runs `IngestionService.RunIngestionAsync`, and prints per-stage
timings. Not run by `dotnet test`.

## Run

    dotnet run --project benchmarks/PatchHound.IngestionBenchmark -- \
      --tenants=1 --devices=1000 --vulns-per-device=20 --software-per-device=5 --runs=3

## Flags

| Flag                     | Default | Meaning |
|--------------------------|---------|---------|
| `--tenants=N`            | 1       | Tenant count |
| `--devices=N`            | 100     | Devices per tenant |
| `--vulns-per-device=N`   | 10      | Staged vulnerabilities per device |
| `--software-per-device=N`| 5       | Installed software rows per device |
| `--runs=N`               | 1       | Repeat the ingestion N times against the same tenants (run 2+ exercises UPSERT / re-observe / resolve paths) |

## Requirements

- Docker (Testcontainers boots Postgres in a container).
- The Postgres image (`postgres:16-alpine`, ~80 MB) is pulled on first run.

## What is measured

Each run prints elapsed time for: Staging, Device merge, Exposure derivation,
Episode sync, Software projection, and Total. The seeder's own time is reported
separately and excluded from the totals.
```

- [ ] **Step 7: Smoke-test the benchmark**

Run with the smallest config to verify the harness works end-to-end:

```bash
dotnet run --project benchmarks/PatchHound.IngestionBenchmark -- \
  --tenants=1 --devices=5 --vulns-per-device=2 --software-per-device=1 --runs=2
```

Expected: container starts, migrations apply, two runs complete with per-stage timings, second run shows non-zero "reobserved" behavior (visible in the worker's log lines or by inspection of `DeviceVulnerabilityExposures.LastSeenRunId` if you switch the DB provisioning to a non-ephemeral string — out of scope here).

- [ ] **Step 8: Verify the benchmark does not run under `dotnet test`**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: no benchmark project appears in the test output. (Console-app projects are not test projects; xunit-only discovery is fine.) Also confirm CI workflows do not invoke `dotnet run` against this project.

- [ ] **Step 9: Commit**

```bash
git add benchmarks/PatchHound.IngestionBenchmark/ PatchHound.slnx
git commit -m "test: add standalone ingestion benchmark (Testcontainers-backed)"
```

---

## Spec coverage check

| Issue requirement | Task implementing it |
|---|---|
| 1 — UPSERT for `DeviceVulnerabilityExposures` | Task 2 + Task 3 |
| 2 — Temp-table + bulk insert for `StagedDeviceMergeService` | Task 4 |
| 3 — CTE-based exposure derivation | Task 5 |
| 4 — Run-scoped episode sync (needs `LastSeenRunId`) | Task 1 (schema) + Task 6 (logic) |
| 5 — Reference reconciliation (bulk, not stored proc per plan decision) | Task 7 |
| 6 — `NormalizedSoftwareProjectionService` batching | Task 8 |
| End-to-end safety net | Task 9 |
| New SQL paths validated against real Postgres | Task 0 (fixture) + per-task tests |
| Standalone perf benchmark (configurable scale, not in test suite) | Task 10 |
