# Enrichment Change Log Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a reusable enrichment provenance log that records old/new source-driven entity changes and exposes them in a vulnerability detail sheet.

**Architecture:** Add a dedicated `EnrichmentChangeLog` entity with global-or-tenant scope, a writer service for meaningful field comparisons, a generic API endpoint, and a reusable React sheet. The first capture point is Defender vulnerability enrichment for global vulnerability fields: CVSS score, CVSS vector, vendor severity, and published date.

**Tech Stack:** .NET/EF Core/PostgreSQL, xUnit/FluentAssertions/NSubstitute, React 19/TanStack Start/TanStack Query, Zod, Vitest/Testing Library, Radix sheet/Tailwind.

---

## File Map

Backend files to create:

- `src/PatchHound.Core/Enums/EnrichmentChangeScope.cs`: `Global` and `Tenant` scope enum.
- `src/PatchHound.Core/Enums/EnrichmentChangeValueKind.cs`: scalar value kind enum used by persisted rows and DTOs.
- `src/PatchHound.Core/Entities/EnrichmentChangeLog.cs`: canonical provenance entity with factory methods.
- `src/PatchHound.Core/Interfaces/IEnrichmentChangeLogWriter.cs`: writer contract and change input record.
- `src/PatchHound.Infrastructure/Data/Configurations/EnrichmentChangeLogConfiguration.cs`: EF mapping and indexes.
- `src/PatchHound.Infrastructure/Services/EnrichmentChangeLogWriter.cs`: comparison/serialization implementation.
- `src/PatchHound.Api/Models/EnrichmentChanges/EnrichmentChangeDtos.cs`: query and response DTOs.
- `src/PatchHound.Api/Controllers/EnrichmentChangesController.cs`: generic read endpoint.
- `tests/PatchHound.Tests/Infrastructure/Services/EnrichmentChangeLogWriterTests.cs`: writer tests.
- `tests/PatchHound.Tests/Api/EnrichmentChangesControllerTests.cs`: API tests.

Backend files to modify:

- `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`: add `DbSet<EnrichmentChangeLog>`.
- `src/PatchHound.Infrastructure/Services/DefenderVulnerabilityEnrichmentRunner.cs`: capture before/after values and write global vulnerability changes.
- `src/PatchHound.Api/Program.cs`: register `IEnrichmentChangeLogWriter`.
- `tests/PatchHound.Tests/Infrastructure/Services/DefenderVulnerabilityEnrichmentRunnerTests.cs`: add provenance assertions and fake scope registration.
- `src/PatchHound.Infrastructure/Migrations/*`: generated EF migration.

Frontend files to create:

- `frontend/src/api/enrichment-changes.schemas.ts`: Zod schemas and inferred types.
- `frontend/src/api/enrichment-changes.functions.ts`: TanStack server function for the API.
- `frontend/src/components/features/enrichment/EnrichmentChangesSheet.tsx`: reusable sheet.
- `frontend/src/components/features/enrichment/EnrichmentChangesSheet.test.tsx`: sheet rendering/filter tests.

Frontend files to modify:

- `frontend/src/components/features/vulnerabilities/VulnerabilityDetail.tsx`: add the sheet action.
- `frontend/src/components/features/vulnerabilities/VulnerabilityDetail.test.tsx`: assert the action is present.

---

### Task 1: Backend Model And Migration

**Files:**
- Create: `src/PatchHound.Core/Enums/EnrichmentChangeScope.cs`
- Create: `src/PatchHound.Core/Enums/EnrichmentChangeValueKind.cs`
- Create: `src/PatchHound.Core/Entities/EnrichmentChangeLog.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/EnrichmentChangeLogConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Create: EF migration under `src/PatchHound.Infrastructure/Migrations/`

- [ ] **Step 1: Run impact analysis before editing `PatchHoundDbContext`**

Run:

```bash
npx gitnexus impact --target PatchHoundDbContext --direction upstream
```

Expected: report direct callers/importers. If risk is HIGH or CRITICAL, stop and report before editing.

- [ ] **Step 2: Add scope enum**

Create `src/PatchHound.Core/Enums/EnrichmentChangeScope.cs`:

```csharp
namespace PatchHound.Core.Enums;

public enum EnrichmentChangeScope
{
    Global,
    Tenant,
}
```

- [ ] **Step 3: Add value kind enum**

Create `src/PatchHound.Core/Enums/EnrichmentChangeValueKind.cs`:

```csharp
namespace PatchHound.Core.Enums;

public enum EnrichmentChangeValueKind
{
    Null,
    String,
    Number,
    Boolean,
    DateTime,
    Enum,
    Object,
    Array,
}
```

- [ ] **Step 4: Add `EnrichmentChangeLog` entity**

Create `src/PatchHound.Core/Entities/EnrichmentChangeLog.cs`:

```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class EnrichmentChangeLog
{
    public const int EntityTypeMaxLength = 128;
    public const int SourceKeyMaxLength = 128;
    public const int FieldPathMaxLength = 256;
    public const int DisplayNameMaxLength = 256;
    public const int ChangeReasonMaxLength = 1024;

    public Guid Id { get; private set; }
    public EnrichmentChangeScope Scope { get; private set; }
    public Guid? TenantId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public string SourceKey { get; private set; } = null!;
    public Guid? EnrichmentRunId { get; private set; }
    public Guid? EnrichmentJobId { get; private set; }
    public string FieldPath { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? OldValueJson { get; private set; }
    public string? NewValueJson { get; private set; }
    public EnrichmentChangeValueKind ValueKind { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public string? ChangeReason { get; private set; }
    public decimal? Confidence { get; private set; }

    private EnrichmentChangeLog() { }

    public static EnrichmentChangeLog Create(
        EnrichmentChangeScope scope,
        Guid? tenantId,
        string entityType,
        Guid entityId,
        string sourceKey,
        Guid? enrichmentRunId,
        Guid? enrichmentJobId,
        string fieldPath,
        string displayName,
        string? oldValueJson,
        string? newValueJson,
        EnrichmentChangeValueKind valueKind,
        DateTimeOffset changedAt,
        string? changeReason = null,
        decimal? confidence = null)
    {
        if (scope == EnrichmentChangeScope.Global && tenantId is not null)
            throw new ArgumentException("Global enrichment changes cannot have a tenant id.", nameof(tenantId));
        if (scope == EnrichmentChangeScope.Tenant && tenantId is null)
            throw new ArgumentException("Tenant enrichment changes require a tenant id.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("entityType required", nameof(entityType));
        if (entityId == Guid.Empty) throw new ArgumentException("entityId required", nameof(entityId));
        if (string.IsNullOrWhiteSpace(sourceKey)) throw new ArgumentException("sourceKey required", nameof(sourceKey));
        if (string.IsNullOrWhiteSpace(fieldPath)) throw new ArgumentException("fieldPath required", nameof(fieldPath));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("displayName required", nameof(displayName));
        ValidateMaxLength(entityType, EntityTypeMaxLength, nameof(entityType));
        ValidateMaxLength(sourceKey, SourceKeyMaxLength, nameof(sourceKey));
        ValidateMaxLength(fieldPath, FieldPathMaxLength, nameof(fieldPath));
        ValidateMaxLength(displayName, DisplayNameMaxLength, nameof(displayName));
        if (!string.IsNullOrWhiteSpace(changeReason))
            ValidateMaxLength(changeReason, ChangeReasonMaxLength, nameof(changeReason));

        return new EnrichmentChangeLog
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            TenantId = tenantId,
            EntityType = entityType.Trim(),
            EntityId = entityId,
            SourceKey = sourceKey.Trim().ToLowerInvariant(),
            EnrichmentRunId = enrichmentRunId,
            EnrichmentJobId = enrichmentJobId,
            FieldPath = fieldPath.Trim(),
            DisplayName = displayName.Trim(),
            OldValueJson = oldValueJson,
            NewValueJson = newValueJson,
            ValueKind = valueKind,
            ChangedAt = changedAt,
            ChangeReason = string.IsNullOrWhiteSpace(changeReason) ? null : changeReason.Trim(),
            Confidence = confidence,
        };
    }

    private static void ValidateMaxLength(string value, int maxLength, string parameterName)
    {
        if (value.Trim().Length > maxLength)
            throw new ArgumentException($"{parameterName} must be {maxLength} characters or fewer.", parameterName);
    }
}
```

- [ ] **Step 5: Configure EF mapping**

Create `src/PatchHound.Infrastructure/Data/Configurations/EnrichmentChangeLogConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class EnrichmentChangeLogConfiguration : IEntityTypeConfiguration<EnrichmentChangeLog>
{
    public void Configure(EntityTypeBuilder<EnrichmentChangeLog> builder)
    {
        builder.ToTable("EnrichmentChangeLog");

        builder.HasKey(change => change.Id);

        builder.HasIndex(change => new
        {
            change.Scope,
            change.TenantId,
            change.EntityType,
            change.EntityId,
            change.ChangedAt,
        });
        builder.HasIndex(change => new { change.Scope, change.TenantId, change.SourceKey, change.ChangedAt });
        builder.HasIndex(change => change.EnrichmentRunId);
        builder.HasIndex(change => change.EnrichmentJobId);

        builder.Property(change => change.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(change => change.EntityType).HasMaxLength(EnrichmentChangeLog.EntityTypeMaxLength).IsRequired();
        builder.Property(change => change.SourceKey).HasMaxLength(EnrichmentChangeLog.SourceKeyMaxLength).IsRequired();
        builder.Property(change => change.FieldPath).HasMaxLength(EnrichmentChangeLog.FieldPathMaxLength).IsRequired();
        builder.Property(change => change.DisplayName).HasMaxLength(EnrichmentChangeLog.DisplayNameMaxLength).IsRequired();
        builder.Property(change => change.OldValueJson).HasColumnType("text");
        builder.Property(change => change.NewValueJson).HasColumnType("text");
        builder.Property(change => change.ValueKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(change => change.ChangeReason).HasMaxLength(EnrichmentChangeLog.ChangeReasonMaxLength);
        builder.Property(change => change.Confidence).HasPrecision(6, 4);
    }
}
```

- [ ] **Step 6: Add DbSet**

Modify `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` near `EnrichmentRuns`:

```csharp
public DbSet<EnrichmentJob> EnrichmentJobs => Set<EnrichmentJob>();
public DbSet<EnrichmentRun> EnrichmentRuns => Set<EnrichmentRun>();
public DbSet<EnrichmentChangeLog> EnrichmentChangeLogs => Set<EnrichmentChangeLog>();
```

- [ ] **Step 7: Generate migration**

Run:

```bash
dotnet ef migrations add AddEnrichmentChangeLog --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api
```

Expected: migration creates `EnrichmentChangeLog` table with the configured columns and indexes.

- [ ] **Step 8: Build**

Run:

```bash
dotnet build PatchHound.slnx
```

Expected: build succeeds.

- [ ] **Step 9: Commit**

Run:

```bash
git add src/PatchHound.Core/Enums/EnrichmentChangeScope.cs src/PatchHound.Core/Enums/EnrichmentChangeValueKind.cs src/PatchHound.Core/Entities/EnrichmentChangeLog.cs src/PatchHound.Infrastructure/Data/Configurations/EnrichmentChangeLogConfiguration.cs src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs src/PatchHound.Infrastructure/Migrations
git commit -m "feat: add enrichment change log model"
```

---

### Task 2: Writer Service

**Files:**
- Create: `src/PatchHound.Core/Interfaces/IEnrichmentChangeLogWriter.cs`
- Create: `src/PatchHound.Infrastructure/Services/EnrichmentChangeLogWriter.cs`
- Modify: `src/PatchHound.Api/Program.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/Services/EnrichmentChangeLogWriterTests.cs`

- [ ] **Step 1: Run impact analysis before editing DI registration**

Run:

```bash
npx gitnexus impact --target Program.cs --direction upstream
```

Expected: low or medium risk. If risk is HIGH or CRITICAL, stop and report before editing.

- [ ] **Step 2: Write failing writer tests**

Create `tests/PatchHound.Tests/Infrastructure/Services/EnrichmentChangeLogWriterTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure.Services;

public class EnrichmentChangeLogWriterTests : IDisposable
{
    private readonly PatchHound.Core.Interfaces.ITenantContext _tenantContext;
    private readonly PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public EnrichmentChangeLogWriterTests()
    {
        _tenantContext = NSubstitute.Substitute.For<PatchHound.Core.Interfaces.ITenantContext>();
        _tenantContext.AccessibleTenantIds.Returns(Array.Empty<Guid>());

        var options = new DbContextOptionsBuilder<PatchHound.Infrastructure.Data.PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new PatchHound.Infrastructure.Data.PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext));
    }

    [Fact]
    public async Task WriteScalarChangesAsync_writes_global_row_for_changed_number()
    {
        var writer = new EnrichmentChangeLogWriter(_db, NullLogger<EnrichmentChangeLogWriter>.Instance);
        var entityId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        await writer.WriteScalarChangesAsync(
            new EnrichmentChangeSet(
                EnrichmentChangeScope.Global,
                TenantId: null,
                EntityType: "Vulnerability",
                EntityId: entityId,
                SourceKey: "Defender",
                EnrichmentRunId: runId,
                EnrichmentJobId: jobId,
                ChangedAt: new DateTimeOffset(2026, 5, 20, 9, 14, 0, TimeSpan.Zero)),
            new[]
            {
                EnrichmentScalarChange.Number("CvssScore", "CVSS score", 7.5m, 8.8m),
            },
            CancellationToken.None);

        var row = await _db.EnrichmentChangeLogs.SingleAsync();
        row.Scope.Should().Be(EnrichmentChangeScope.Global);
        row.TenantId.Should().BeNull();
        row.EntityType.Should().Be("Vulnerability");
        row.EntityId.Should().Be(entityId);
        row.SourceKey.Should().Be("defender");
        row.EnrichmentRunId.Should().Be(runId);
        row.EnrichmentJobId.Should().Be(jobId);
        row.FieldPath.Should().Be("CvssScore");
        row.DisplayName.Should().Be("CVSS score");
        row.OldValueJson.Should().Be("7.5");
        row.NewValueJson.Should().Be("8.8");
        row.ValueKind.Should().Be(EnrichmentChangeValueKind.Number);
    }

    [Fact]
    public async Task WriteScalarChangesAsync_skips_semantically_equal_numbers()
    {
        var writer = new EnrichmentChangeLogWriter(_db, NullLogger<EnrichmentChangeLogWriter>.Instance);

        await writer.WriteScalarChangesAsync(
            new EnrichmentChangeSet(
                EnrichmentChangeScope.Global,
                TenantId: null,
                EntityType: "Vulnerability",
                EntityId: Guid.NewGuid(),
                SourceKey: "nvd",
                EnrichmentRunId: null,
                EnrichmentJobId: null,
                ChangedAt: DateTimeOffset.UtcNow),
            new[]
            {
                EnrichmentScalarChange.Number("CvssScore", "CVSS score", 7.50m, 7.5m),
            },
            CancellationToken.None);

        (await _db.EnrichmentChangeLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task WriteScalarChangesAsync_writes_tenant_row_for_tenant_scope()
    {
        var writer = new EnrichmentChangeLogWriter(_db, NullLogger<EnrichmentChangeLogWriter>.Instance);
        var tenantId = Guid.NewGuid();

        await writer.WriteScalarChangesAsync(
            new EnrichmentChangeSet(
                EnrichmentChangeScope.Tenant,
                TenantId: tenantId,
                EntityType: "SoftwareProduct",
                EntityId: Guid.NewGuid(),
                SourceKey: "endoflife",
                EnrichmentRunId: null,
                EnrichmentJobId: null,
                ChangedAt: DateTimeOffset.UtcNow),
            new[]
            {
                EnrichmentScalarChange.String("LifecycleStatus", "Lifecycle status", "Active", "EOL"),
            },
            CancellationToken.None);

        var row = await _db.EnrichmentChangeLogs.SingleAsync();
        row.Scope.Should().Be(EnrichmentChangeScope.Tenant);
        row.TenantId.Should().Be(tenantId);
        row.OldValueJson.Should().Be("\"Active\"");
        row.NewValueJson.Should().Be("\"EOL\"");
        row.ValueKind.Should().Be(EnrichmentChangeValueKind.String);
    }

    public void Dispose() => _db.Dispose();
}
```

- [ ] **Step 3: Run tests to verify failure**

Run:

```bash
dotnet test PatchHound.slnx --filter "FullyQualifiedName~EnrichmentChangeLogWriterTests" -v minimal
```

Expected: FAIL because `IEnrichmentChangeLogWriter`, `EnrichmentChangeSet`, `EnrichmentScalarChange`, and `EnrichmentChangeLogWriter` do not exist.

- [ ] **Step 4: Add writer contract**

Create `src/PatchHound.Core/Interfaces/IEnrichmentChangeLogWriter.cs`:

```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Interfaces;

public interface IEnrichmentChangeLogWriter
{
    Task WriteScalarChangesAsync(
        EnrichmentChangeSet changeSet,
        IReadOnlyCollection<EnrichmentScalarChange> changes,
        CancellationToken ct);
}

public sealed record EnrichmentChangeSet(
    EnrichmentChangeScope Scope,
    Guid? TenantId,
    string EntityType,
    Guid EntityId,
    string SourceKey,
    Guid? EnrichmentRunId,
    Guid? EnrichmentJobId,
    DateTimeOffset ChangedAt);

public sealed record EnrichmentScalarChange(
    string FieldPath,
    string DisplayName,
    object? OldValue,
    object? NewValue,
    EnrichmentChangeValueKind ValueKind,
    string? ChangeReason = null,
    decimal? Confidence = null)
{
    public static EnrichmentScalarChange Number(string fieldPath, string displayName, decimal? oldValue, decimal? newValue) =>
        new(fieldPath, displayName, oldValue, newValue, EnrichmentChangeValueKind.Number);

    public static EnrichmentScalarChange String(string fieldPath, string displayName, string? oldValue, string? newValue) =>
        new(fieldPath, displayName, oldValue, newValue, EnrichmentChangeValueKind.String);

    public static EnrichmentScalarChange Enum<T>(string fieldPath, string displayName, T oldValue, T newValue)
        where T : struct, System.Enum =>
        new(fieldPath, displayName, oldValue.ToString(), newValue.ToString(), EnrichmentChangeValueKind.Enum);

    public static EnrichmentScalarChange DateTime(string fieldPath, string displayName, DateTimeOffset? oldValue, DateTimeOffset? newValue) =>
        new(fieldPath, displayName, oldValue, newValue, EnrichmentChangeValueKind.DateTime);
}
```

- [ ] **Step 5: Implement writer**

Create `src/PatchHound.Infrastructure/Services/EnrichmentChangeLogWriter.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class EnrichmentChangeLogWriter(
    PatchHoundDbContext db,
    ILogger<EnrichmentChangeLogWriter> logger
) : IEnrichmentChangeLogWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteScalarChangesAsync(
        EnrichmentChangeSet changeSet,
        IReadOnlyCollection<EnrichmentScalarChange> changes,
        CancellationToken ct)
    {
        foreach (var change in changes)
        {
            if (AreEqual(change.OldValue, change.NewValue, change.ValueKind))
            {
                continue;
            }

            db.EnrichmentChangeLogs.Add(
                EnrichmentChangeLog.Create(
                    changeSet.Scope,
                    changeSet.TenantId,
                    changeSet.EntityType,
                    changeSet.EntityId,
                    changeSet.SourceKey,
                    changeSet.EnrichmentRunId,
                    changeSet.EnrichmentJobId,
                    change.FieldPath,
                    change.DisplayName,
                    Serialize(change.OldValue),
                    Serialize(change.NewValue),
                    change.ValueKind,
                    changeSet.ChangedAt,
                    change.ChangeReason,
                    change.Confidence));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed writing enrichment change log rows for {EntityType} {EntityId} from source {SourceKey}.",
                changeSet.EntityType,
                changeSet.EntityId,
                changeSet.SourceKey);

            foreach (var entry in db.ChangeTracker.Entries<EnrichmentChangeLog>()
                .Where(entry => entry.State == Microsoft.EntityFrameworkCore.EntityState.Added)
                .ToList())
            {
                entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            }
        }
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, SerializerOptions);

    private static bool AreEqual(object? oldValue, object? newValue, EnrichmentChangeValueKind valueKind)
    {
        if (oldValue is null && newValue is null) return true;
        if (oldValue is null || newValue is null) return false;

        return valueKind switch
        {
            EnrichmentChangeValueKind.Number => Convert.ToDecimal(oldValue) == Convert.ToDecimal(newValue),
            EnrichmentChangeValueKind.DateTime => NormalizeDateTime(oldValue) == NormalizeDateTime(newValue),
            _ => string.Equals(Serialize(oldValue), Serialize(newValue), StringComparison.Ordinal),
        };
    }

    private static DateTimeOffset NormalizeDateTime(object value) =>
        value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(dt).ToUniversalTime(),
            _ => DateTimeOffset.Parse(value.ToString()!).ToUniversalTime(),
        };
}
```

- [ ] **Step 6: Register writer in DI**

Modify `src/PatchHound.Api/Program.cs` near other infrastructure services:

```csharp
builder.Services.AddScoped<IEnrichmentChangeLogWriter, EnrichmentChangeLogWriter>();
```

- [ ] **Step 7: Run writer tests**

Run:

```bash
dotnet test PatchHound.slnx --filter "FullyQualifiedName~EnrichmentChangeLogWriterTests" -v minimal
```

Expected: PASS.

- [ ] **Step 8: Commit**

Run:

```bash
git add src/PatchHound.Core/Interfaces/IEnrichmentChangeLogWriter.cs src/PatchHound.Infrastructure/Services/EnrichmentChangeLogWriter.cs src/PatchHound.Api/Program.cs tests/PatchHound.Tests/Infrastructure/Services/EnrichmentChangeLogWriterTests.cs
git commit -m "feat: add enrichment change writer"
```

---

### Task 3: Capture Defender Vulnerability Changes

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/DefenderVulnerabilityEnrichmentRunner.cs`
- Modify: `tests/PatchHound.Tests/Infrastructure/Services/DefenderVulnerabilityEnrichmentRunnerTests.cs`

- [ ] **Step 1: Run impact analysis before editing runner**

Run:

```bash
npx gitnexus impact --target DefenderVulnerabilityEnrichmentRunner --direction upstream
```

Expected: review direct callers and tests. If risk is HIGH or CRITICAL, stop and report before editing.

- [ ] **Step 2: Add failing enrichment provenance assertion**

Modify `ExecuteAsync_returns_Succeeded_and_writes_canonical_rows` in `tests/PatchHound.Tests/Infrastructure/Services/DefenderVulnerabilityEnrichmentRunnerTests.cs` to start with an existing score and assert change rows:

```csharp
var vuln = Vulnerability.Create(
    "microsoft-defender", "CVE-2026-9001", "placeholder", string.Empty,
    Severity.Medium, 7.5m, "old-vector", null);
```

Add after the canonical row assertions:

```csharp
var changes = await db.EnrichmentChangeLogs
    .OrderBy(change => change.FieldPath)
    .ToListAsync();

changes.Should().Contain(change =>
    change.Scope == EnrichmentChangeScope.Global
    && change.TenantId == null
    && change.EntityType == nameof(Vulnerability)
    && change.EntityId == vuln.Id
    && change.SourceKey == EnrichmentSourceCatalog.DefenderSourceKey
    && change.FieldPath == "CvssScore"
    && change.OldValueJson == "7.5"
    && change.NewValueJson == "8.5");

changes.Should().Contain(change =>
    change.FieldPath == "VendorSeverity"
    && change.OldValueJson == "\"Medium\""
    && change.NewValueJson == "\"High\"");
```

- [ ] **Step 3: Update fake scope to provide writer**

In `FakeScopeFactory.GetService`, add:

```csharp
if (serviceType == typeof(IEnrichmentChangeLogWriter))
    return new EnrichmentChangeLogWriter(_db, NullLogger<EnrichmentChangeLogWriter>.Instance);
```

- [ ] **Step 4: Run test to verify failure**

Run:

```bash
dotnet test PatchHound.slnx --filter "FullyQualifiedName~DefenderVulnerabilityEnrichmentRunnerTests.ExecuteAsync_returns_Succeeded_and_writes_canonical_rows" -v minimal
```

Expected: FAIL because no change rows are written.

- [ ] **Step 5: Capture before/after values in runner**

Modify `src/PatchHound.Infrastructure/Services/DefenderVulnerabilityEnrichmentRunner.cs`:

Add service resolution:

```csharp
var changeWriter = scope.ServiceProvider.GetRequiredService<IEnrichmentChangeLogWriter>();
```

Before `resolver.ResolveAsync(resolveInput, ct);`, capture:

```csharp
var before = VulnerabilityEnrichmentSnapshot.From(vulnerability);
```

After `var resolved = await resolver.ResolveAsync(resolveInput, ct);`, write:

```csharp
var after = VulnerabilityEnrichmentSnapshot.From(resolved);
await changeWriter.WriteScalarChangesAsync(
    new EnrichmentChangeSet(
        EnrichmentChangeScope.Global,
        TenantId: null,
        EntityType: nameof(Vulnerability),
        EntityId: resolved.Id,
        SourceKey: EnrichmentSourceCatalog.DefenderSourceKey,
        EnrichmentRunId: null,
        EnrichmentJobId: job.Id,
        ChangedAt: DateTimeOffset.UtcNow),
    before.CompareTo(after),
    ct);
```

Add private record inside `DefenderVulnerabilityEnrichmentRunner`:

```csharp
private sealed record VulnerabilityEnrichmentSnapshot(
    Severity VendorSeverity,
    decimal? CvssScore,
    string? CvssVector,
    DateTimeOffset? PublishedDate)
{
    public static VulnerabilityEnrichmentSnapshot From(Vulnerability vulnerability) =>
        new(
            vulnerability.VendorSeverity,
            vulnerability.CvssScore,
            vulnerability.CvssVector,
            vulnerability.PublishedDate);

    public IReadOnlyCollection<EnrichmentScalarChange> CompareTo(VulnerabilityEnrichmentSnapshot after) =>
        new[]
        {
            EnrichmentScalarChange.Enum("VendorSeverity", "Vendor severity", VendorSeverity, after.VendorSeverity),
            EnrichmentScalarChange.Number("CvssScore", "CVSS score", CvssScore, after.CvssScore),
            EnrichmentScalarChange.String("CvssVector", "CVSS vector", CvssVector, after.CvssVector),
            EnrichmentScalarChange.DateTime("PublishedDate", "Published date", PublishedDate, after.PublishedDate),
        };
}
```

- [ ] **Step 6: Run runner tests**

Run:

```bash
dotnet test PatchHound.slnx --filter "FullyQualifiedName~DefenderVulnerabilityEnrichmentRunnerTests" -v minimal
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```bash
git add src/PatchHound.Infrastructure/Services/DefenderVulnerabilityEnrichmentRunner.cs tests/PatchHound.Tests/Infrastructure/Services/DefenderVulnerabilityEnrichmentRunnerTests.cs
git commit -m "feat: record vulnerability enrichment changes"
```

---

### Task 4: Enrichment Changes API

**Files:**
- Create: `src/PatchHound.Api/Models/EnrichmentChanges/EnrichmentChangeDtos.cs`
- Create: `src/PatchHound.Api/Controllers/EnrichmentChangesController.cs`
- Test: `tests/PatchHound.Tests/Api/EnrichmentChangesControllerTests.cs`

- [ ] **Step 1: Run impact analysis before adding controller**

Run:

```bash
npx gitnexus impact --target AuditLogController --direction upstream
```

Expected: use the output as a reference for nearby API patterns. No edits to `AuditLogController` are needed.

- [ ] **Step 2: Write failing API tests**

Create `tests/PatchHound.Tests/Api/EnrichmentChangesControllerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.EnrichmentChanges;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class EnrichmentChangesControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;

    public EnrichmentChangesControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(_tenantContext));
    }

    [Fact]
    public async Task List_returns_global_and_active_tenant_rows()
    {
        var entityId = Guid.NewGuid();
        _dbContext.EnrichmentChangeLogs.Add(EnrichmentChangeLog.Create(
            EnrichmentChangeScope.Global,
            null,
            "Vulnerability",
            entityId,
            EnrichmentSourceCatalog.DefenderSourceKey,
            null,
            Guid.NewGuid(),
            "CvssScore",
            "CVSS score",
            "7.5",
            "8.8",
            EnrichmentChangeValueKind.Number,
            DateTimeOffset.UtcNow));
        _dbContext.EnrichmentChangeLogs.Add(EnrichmentChangeLog.Create(
            EnrichmentChangeScope.Tenant,
            _tenantId,
            "Vulnerability",
            entityId,
            "tenant-source",
            null,
            null,
            "TenantNote",
            "Tenant note",
            "\"old\"",
            "\"new\"",
            EnrichmentChangeValueKind.String,
            DateTimeOffset.UtcNow.AddMinutes(-1)));
        _dbContext.EnrichmentChangeLogs.Add(EnrichmentChangeLog.Create(
            EnrichmentChangeScope.Tenant,
            Guid.NewGuid(),
            "Vulnerability",
            entityId,
            "other-tenant",
            null,
            null,
            "TenantNote",
            "Tenant note",
            "\"old\"",
            "\"new\"",
            EnrichmentChangeValueKind.String,
            DateTimeOffset.UtcNow.AddMinutes(-2)));
        await _dbContext.SaveChangesAsync();

        var controller = new EnrichmentChangesController(_dbContext, _tenantContext);
        var action = await controller.List(
            new EnrichmentChangeFilterQuery("Vulnerability", entityId, null, null, null, null),
            new PaginationQuery(1, 10),
            CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<PagedResponse<EnrichmentChangeDto>>().Subject;
        payload.Items.Should().HaveCount(2);
        payload.Items.Should().Contain(item => item.Scope == "Global" && item.TenantId == null);
        payload.Items.Should().Contain(item => item.Scope == "Tenant" && item.TenantId == _tenantId);
        payload.Items.Should().OnlyContain(item => item.SourceKey != "other-tenant");
    }

    [Fact]
    public async Task List_requires_active_tenant_for_tenant_scoped_visibility()
    {
        _tenantContext.CurrentTenantId.Returns((Guid?)null);
        var controller = new EnrichmentChangesController(_dbContext, _tenantContext);

        var action = await controller.List(
            new EnrichmentChangeFilterQuery("Vulnerability", Guid.NewGuid(), null, null, null, null),
            new PaginationQuery(1, 10),
            CancellationToken.None);

        action.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    public void Dispose() => _dbContext.Dispose();
}
```

- [ ] **Step 3: Run API tests to verify failure**

Run:

```bash
dotnet test PatchHound.slnx --filter "FullyQualifiedName~EnrichmentChangesControllerTests" -v minimal
```

Expected: FAIL because API DTOs/controller do not exist.

- [ ] **Step 4: Add DTOs**

Create `src/PatchHound.Api/Models/EnrichmentChanges/EnrichmentChangeDtos.cs`:

```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Api.Models.EnrichmentChanges;

public record EnrichmentChangeFilterQuery(
    string EntityType,
    Guid EntityId,
    string? SourceKey,
    string? FieldPath,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate);

public record EnrichmentChangeDto(
    Guid Id,
    string Scope,
    Guid? TenantId,
    string EntityType,
    Guid EntityId,
    string SourceKey,
    string? SourceDisplayName,
    string FieldPath,
    string DisplayName,
    object? OldValue,
    object? NewValue,
    string ValueKind,
    DateTimeOffset ChangedAt,
    Guid? EnrichmentRunId,
    Guid? EnrichmentJobId,
    string? ChangeReason,
    decimal? Confidence);
```

- [ ] **Step 5: Add controller**

Create `src/PatchHound.Api/Controllers/EnrichmentChangesController.cs`:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.EnrichmentChanges;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/enrichment-changes")]
[Authorize]
public class EnrichmentChangesController(
    PatchHoundDbContext dbContext,
    ITenantContext tenantContext
) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<EnrichmentChangeDto>>> List(
        [FromQuery] EnrichmentChangeFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        if (filter.EntityId == Guid.Empty || string.IsNullOrWhiteSpace(filter.EntityType))
            return BadRequest(new ProblemDetails { Title = "Entity type and entity id are required." });

        var query = dbContext.EnrichmentChangeLogs.AsNoTracking()
            .Where(change =>
                change.EntityType == filter.EntityType
                && change.EntityId == filter.EntityId
                && (change.Scope == EnrichmentChangeScope.Global
                    || (change.Scope == EnrichmentChangeScope.Tenant && change.TenantId == currentTenantId)));

        if (!string.IsNullOrWhiteSpace(filter.SourceKey))
            query = query.Where(change => change.SourceKey == filter.SourceKey.Trim().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(filter.FieldPath))
            query = query.Where(change => change.FieldPath == filter.FieldPath);
        if (filter.FromDate.HasValue)
            query = query.Where(change => change.ChangedAt >= filter.FromDate.Value);
        if (filter.ToDate.HasValue)
            query = query.Where(change => change.ChangedAt <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(ct);
        var entries = await query
            .OrderByDescending(change => change.ChangedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var items = entries.Select(change => new EnrichmentChangeDto(
            change.Id,
            change.Scope.ToString(),
            change.TenantId,
            change.EntityType,
            change.EntityId,
            change.SourceKey,
            ResolveSourceDisplayName(change.SourceKey),
            change.FieldPath,
            change.DisplayName,
            ParseValue(change.OldValueJson),
            ParseValue(change.NewValueJson),
            change.ValueKind.ToString(),
            change.ChangedAt,
            change.EnrichmentRunId,
            change.EnrichmentJobId,
            change.ChangeReason,
            change.Confidence)).ToList();

        return Ok(new PagedResponse<EnrichmentChangeDto>(
            items,
            totalCount,
            pagination.Page,
            pagination.BoundedPageSize));
    }

    private static string? ResolveSourceDisplayName(string sourceKey) =>
        string.Equals(sourceKey, EnrichmentSourceCatalog.DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
            ? "Microsoft Defender"
            : string.Equals(sourceKey, EnrichmentSourceCatalog.NvdSourceKey, StringComparison.OrdinalIgnoreCase)
                ? "NVD API"
                : string.Equals(sourceKey, EnrichmentSourceCatalog.EndOfLifeSourceKey, StringComparison.OrdinalIgnoreCase)
                    ? "Software End of Life"
                    : string.Equals(sourceKey, EnrichmentSourceCatalog.SupplyChainSourceKey, StringComparison.OrdinalIgnoreCase)
                        ? "Supply Chain Evidence"
                        : null;

    private static object? ParseValue(string? raw)
    {
        if (raw is null) return null;

        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.ValueKind switch
        {
            JsonValueKind.String => doc.RootElement.GetString(),
            JsonValueKind.Number when doc.RootElement.TryGetDecimal(out var value) => value,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => doc.RootElement.Clone().ToString(),
        };
    }
}
```

- [ ] **Step 6: Run API tests**

Run:

```bash
dotnet test PatchHound.slnx --filter "FullyQualifiedName~EnrichmentChangesControllerTests" -v minimal
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```bash
git add src/PatchHound.Api/Models/EnrichmentChanges/EnrichmentChangeDtos.cs src/PatchHound.Api/Controllers/EnrichmentChangesController.cs tests/PatchHound.Tests/Api/EnrichmentChangesControllerTests.cs
git commit -m "feat: expose enrichment changes api"
```

---

### Task 5: Frontend API Function

**Files:**
- Create: `frontend/src/api/enrichment-changes.schemas.ts`
- Create: `frontend/src/api/enrichment-changes.functions.ts`

- [ ] **Step 1: Add schema**

Create `frontend/src/api/enrichment-changes.schemas.ts`:

```ts
import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

export const enrichmentChangeValueSchema = z.union([
  z.string(),
  z.number(),
  z.boolean(),
  z.record(z.string(), z.unknown()),
  z.array(z.unknown()),
  z.null(),
])

export const enrichmentChangeSchema = z.object({
  id: z.string().uuid(),
  scope: z.enum(['Global', 'Tenant']),
  tenantId: z.string().uuid().nullable(),
  entityType: z.string(),
  entityId: z.string().uuid(),
  sourceKey: z.string(),
  sourceDisplayName: z.string().nullable(),
  fieldPath: z.string(),
  displayName: z.string(),
  oldValue: enrichmentChangeValueSchema,
  newValue: enrichmentChangeValueSchema,
  valueKind: z.string(),
  changedAt: isoDateTimeSchema,
  enrichmentRunId: z.string().uuid().nullable(),
  enrichmentJobId: z.string().uuid().nullable(),
  changeReason: z.string().nullable(),
  confidence: z.number().nullable(),
})

export const pagedEnrichmentChangeSchema = pagedResponseMetaSchema.extend({
  items: z.array(enrichmentChangeSchema),
})

export type EnrichmentChange = z.infer<typeof enrichmentChangeSchema>
export type PagedEnrichmentChanges = z.infer<typeof pagedEnrichmentChangeSchema>
```

- [ ] **Step 2: Add server function**

Create `frontend/src/api/enrichment-changes.functions.ts`:

```ts
import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { apiGet } from '@/server/api'
import { authMiddleware } from '@/server/middleware'
import { pagedEnrichmentChangeSchema } from './enrichment-changes.schemas'

export const fetchEnrichmentChanges = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({
    entityType: z.string(),
    entityId: z.string().uuid(),
    sourceKey: z.string().optional(),
    fieldPath: z.string().optional(),
    page: z.number().optional(),
    pageSize: z.number().optional(),
  }))
  .handler(async ({ context, data }) => {
    const params = new URLSearchParams({
      entityType: data.entityType,
      entityId: data.entityId,
      page: String(data.page ?? 1),
      pageSize: String(data.pageSize ?? 20),
    })

    if (data.sourceKey) params.set('sourceKey', data.sourceKey)
    if (data.fieldPath) params.set('fieldPath', data.fieldPath)

    const response = await apiGet(`/enrichment-changes?${params.toString()}`, context)
    return pagedEnrichmentChangeSchema.parse(response)
  })
```

- [ ] **Step 3: Run frontend typecheck**

Run:

```bash
cd frontend
npm run typecheck
```

Expected: PASS.

- [ ] **Step 4: Commit**

Run:

```bash
git add frontend/src/api/enrichment-changes.schemas.ts frontend/src/api/enrichment-changes.functions.ts
git commit -m "feat: add enrichment changes frontend api"
```

---

### Task 6: Reusable Enrichment Changes Sheet

**Files:**
- Create: `frontend/src/components/features/enrichment/EnrichmentChangesSheet.tsx`
- Test: `frontend/src/components/features/enrichment/EnrichmentChangesSheet.test.tsx`

- [ ] **Step 1: Write failing sheet tests**

Create `frontend/src/components/features/enrichment/EnrichmentChangesSheet.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { describe, expect, it, vi } from 'vitest'
import { fetchEnrichmentChanges } from '@/api/enrichment-changes.functions'
import { EnrichmentChangesSheet } from './EnrichmentChangesSheet'

vi.mock('@/api/enrichment-changes.functions', () => ({
  fetchEnrichmentChanges: vi.fn(async () => ({
    items: [
      {
        id: '11111111-1111-1111-1111-111111111111',
        scope: 'Global',
        tenantId: null,
        entityType: 'Vulnerability',
        entityId: '22222222-2222-2222-2222-222222222222',
        sourceKey: 'microsoft-defender',
        sourceDisplayName: 'Microsoft Defender',
        fieldPath: 'CvssScore',
        displayName: 'CVSS score',
        oldValue: 7.5,
        newValue: 8.8,
        valueKind: 'Number',
        changedAt: '2026-05-20T09:14:00Z',
        enrichmentRunId: null,
        enrichmentJobId: '33333333-3333-3333-3333-333333333333',
        changeReason: null,
        confidence: null,
      },
      {
        id: '44444444-4444-4444-4444-444444444444',
        scope: 'Global',
        tenantId: null,
        entityType: 'Vulnerability',
        entityId: '22222222-2222-2222-2222-222222222222',
        sourceKey: 'microsoft-defender',
        sourceDisplayName: 'Microsoft Defender',
        fieldPath: 'VendorSeverity',
        displayName: 'Vendor severity',
        oldValue: 'High',
        newValue: 'Critical',
        valueKind: 'Enum',
        changedAt: '2026-05-20T09:15:00Z',
        enrichmentRunId: null,
        enrichmentJobId: null,
        changeReason: null,
        confidence: null,
      },
    ],
    totalCount: 2,
    page: 1,
    pageSize: 20,
    totalPages: 1,
  })),
}))

function renderSheet() {
  const queryClient = new QueryClient()
  render(
    <QueryClientProvider client={queryClient}>
      <EnrichmentChangesSheet
        entityType="Vulnerability"
        entityId="22222222-2222-2222-2222-222222222222"
        entityLabel="CVE-2026-1234"
      />
    </QueryClientProvider>,
  )
}

describe('EnrichmentChangesSheet', () => {
  it('opens and renders old and new values', async () => {
    renderSheet()

    fireEvent.click(screen.getByRole('button', { name: /Enrichment changes/i }))

    expect(await screen.findByText('CVSS score')).toBeInTheDocument()
    expect(screen.getByText('7.5')).toBeInTheDocument()
    expect(screen.getByText('8.8')).toBeInTheDocument()
    expect(screen.getByText('Microsoft Defender')).toBeInTheDocument()
    expect(screen.getByText('Vendor severity')).toBeInTheDocument()
  })

  it('filters by field', async () => {
    renderSheet()

    fireEvent.click(screen.getByRole('button', { name: /Enrichment changes/i }))
    fireEvent.change(await screen.findByLabelText(/Field/i), { target: { value: 'CvssScore' } })

    await waitFor(() => {
      expect(fetchEnrichmentChanges).toHaveBeenCalledWith({
        data: expect.objectContaining({ fieldPath: 'CvssScore' }),
      })
    })
  })
})
```

- [ ] **Step 2: Run sheet tests to verify failure**

Run:

```bash
cd frontend
npm test -- EnrichmentChangesSheet.test.tsx
```

Expected: FAIL because the component does not exist.

- [ ] **Step 3: Implement sheet**

Create `frontend/src/components/features/enrichment/EnrichmentChangesSheet.tsx`:

```tsx
import { useEffect, useMemo, useState } from 'react'
import { History } from 'lucide-react'
import { useMutation } from '@tanstack/react-query'
import { fetchEnrichmentChanges } from '@/api/enrichment-changes.functions'
import type { EnrichmentChange } from '@/api/enrichment-changes.schemas'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import { formatDateTime } from '@/lib/formatting'
import { cn } from '@/lib/utils'

type EnrichmentChangesSheetProps = {
  entityType: string
  entityId: string
  entityLabel: string
}

const vulnerabilityFields = [
  { value: '', label: 'All fields' },
  { value: 'CvssScore', label: 'CVSS score' },
  { value: 'CvssVector', label: 'CVSS vector' },
  { value: 'VendorSeverity', label: 'Vendor severity' },
  { value: 'PublishedDate', label: 'Published date' },
]

export function EnrichmentChangesSheet({ entityType, entityId, entityLabel }: EnrichmentChangesSheetProps) {
  const [open, setOpen] = useState(false)
  const [fieldPath, setFieldPath] = useState('')
  const changesMutation = useMutation({
    mutationFn: async (input: { fieldPath?: string }) =>
      fetchEnrichmentChanges({
        data: {
          entityType,
          entityId,
          fieldPath: input.fieldPath || undefined,
          page: 1,
          pageSize: 20,
        },
      }),
  })

  useEffect(() => {
    if (!open) return
    void changesMutation.mutateAsync({ fieldPath })
  }, [open, fieldPath])

  const changes = changesMutation.data?.items ?? []
  const fieldOptions = useMemo(
    () => entityType === 'Vulnerability' ? vulnerabilityFields : [{ value: '', label: 'All fields' }],
    [entityType],
  )

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button type="button" variant="outline" size="sm">
          <History className="size-4" />
          Enrichment changes
        </Button>
      </SheetTrigger>
      <SheetContent side="right" className="w-full overflow-y-auto border-l border-border/80 bg-background p-0 sm:max-w-2xl">
        <SheetHeader className="border-b border-border/70 bg-muted/20">
          <SheetTitle>Enrichment changes</SheetTitle>
          <SheetDescription>{entityLabel}</SheetDescription>
        </SheetHeader>

        <div className="space-y-4 p-6">
          <label className="block space-y-2 text-sm">
            <span className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">Field</span>
            <select
              aria-label="Field"
              value={fieldPath}
              onChange={(event) => setFieldPath(event.target.value)}
              className="h-9 w-full rounded-md border border-border bg-background px-3 text-sm"
            >
              {fieldOptions.map((option) => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </select>
          </label>

          {changesMutation.isPending ? (
            <p className="text-sm text-muted-foreground">Loading enrichment changes...</p>
          ) : null}

          {!changesMutation.isPending && changes.length === 0 ? (
            <div className="rounded-lg border border-dashed border-border/70 bg-background/30 px-4 py-8 text-sm text-muted-foreground">
              No enrichment changes recorded for this entity.
            </div>
          ) : null}

          <div className="space-y-3">
            {changes.map((change) => (
              <ChangeRow key={change.id} change={change} />
            ))}
          </div>
        </div>
      </SheetContent>
    </Sheet>
  )
}

function ChangeRow({ change }: { change: EnrichmentChange }) {
  return (
    <article className="rounded-lg border border-border bg-card p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-2">
          <h3 className="text-sm font-semibold text-foreground">{change.displayName}</h3>
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <ValueBadge value={change.oldValue} fieldPath={change.fieldPath} />
            <span className="text-muted-foreground">-&gt;</span>
            <ValueBadge value={change.newValue} fieldPath={change.fieldPath} />
          </div>
        </div>
        <Badge variant="outline">{change.scope}</Badge>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <span>{change.sourceDisplayName ?? change.sourceKey}</span>
        <span aria-hidden="true">·</span>
        <span>{formatDateTime(change.changedAt)}</span>
        {change.enrichmentJobId ? (
          <>
            <span aria-hidden="true">·</span>
            <span>Job {change.enrichmentJobId.slice(0, 8)}</span>
          </>
        ) : null}
      </div>
    </article>
  )
}

function ValueBadge({ value, fieldPath }: { value: unknown; fieldPath: string }) {
  const text = formatValue(value)
  return (
    <span
      className={cn(
        'inline-flex min-h-7 items-center rounded-md border border-border/70 bg-background px-2.5 py-1 text-xs font-medium text-foreground',
        fieldPath === 'VendorSeverity' && text === 'Critical' && 'border-destructive/30 bg-destructive/10 text-destructive',
      )}
    >
      {text}
    </span>
  )
}

function formatValue(value: unknown) {
  if (value === null || value === undefined || value === '') return 'None'
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') return String(value)
  return JSON.stringify(value)
}
```

- [ ] **Step 4: Run sheet tests**

Run:

```bash
cd frontend
npm test -- EnrichmentChangesSheet.test.tsx
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```bash
git add frontend/src/components/features/enrichment/EnrichmentChangesSheet.tsx frontend/src/components/features/enrichment/EnrichmentChangesSheet.test.tsx
git commit -m "feat: add enrichment changes sheet"
```

---

### Task 7: Attach Sheet To Vulnerability Detail

**Files:**
- Modify: `frontend/src/components/features/vulnerabilities/VulnerabilityDetail.tsx`
- Modify: `frontend/src/components/features/vulnerabilities/VulnerabilityDetail.test.tsx`

- [ ] **Step 1: Write failing detail test expectation**

Add mock:

```tsx
vi.mock('@/components/features/enrichment/EnrichmentChangesSheet', () => ({
  EnrichmentChangesSheet: ({ entityLabel }: { entityLabel: string }) => (
    <button type="button">Enrichment changes for {entityLabel}</button>
  ),
}))
```

Add assertion in `uses shared tab semantics for secondary content`:

```tsx
expect(screen.getByRole('button', { name: /Enrichment changes for CVE-2026-1234/i })).toBeInTheDocument()
```

- [ ] **Step 2: Run detail test to verify failure**

Run:

```bash
cd frontend
npm test -- VulnerabilityDetail.test.tsx
```

Expected: FAIL because the sheet is not rendered.

- [ ] **Step 3: Render the sheet**

Modify `frontend/src/components/features/vulnerabilities/VulnerabilityDetail.tsx` imports:

```tsx
import { EnrichmentChangesSheet } from '@/components/features/enrichment/EnrichmentChangesSheet'
```

Add next to `WorkNotesSheet`:

```tsx
<EnrichmentChangesSheet
  entityType="Vulnerability"
  entityId={vulnerability.id}
  entityLabel={vulnerability.externalId}
/>
```

- [ ] **Step 4: Run frontend tests**

Run:

```bash
cd frontend
npm test -- VulnerabilityDetail.test.tsx EnrichmentChangesSheet.test.tsx
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```bash
git add frontend/src/components/features/vulnerabilities/VulnerabilityDetail.tsx frontend/src/components/features/vulnerabilities/VulnerabilityDetail.test.tsx
git commit -m "feat: show enrichment changes on vulnerabilities"
```

---

### Task 8: Full Verification And Scope Review

**Files:**
- No planned source changes unless verification exposes a bug.

- [ ] **Step 1: Run backend tests**

Run:

```bash
dotnet test PatchHound.slnx -v minimal
```

Expected: PASS.

- [ ] **Step 2: Run frontend checks**

Run:

```bash
cd frontend
npm run lint
npm run typecheck
npm test
```

Expected: PASS.

- [ ] **Step 3: Run GitNexus changed-scope detection**

Run:

```bash
npx gitnexus detect_changes --scope all
```

Expected: changed symbols include the enrichment change log model, writer, Defender runner, API controller, and frontend sheet/detail components. Affected processes should match enrichment provenance and vulnerability detail UI only.

- [ ] **Step 4: Build backend**

Run:

```bash
dotnet build PatchHound.slnx
```

Expected: PASS.

- [ ] **Step 5: Final commit if verification fixes were needed**

If verification required fixes, stage the exact files changed by those fixes and commit them. For example, if frontend typecheck required a sheet type adjustment, run:

```bash
git add frontend/src/components/features/enrichment/EnrichmentChangesSheet.tsx
git commit -m "fix: stabilize enrichment change log"
```

If no fixes were needed, do not create an empty commit.
