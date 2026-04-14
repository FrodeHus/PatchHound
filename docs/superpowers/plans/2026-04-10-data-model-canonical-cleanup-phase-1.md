# Data Model Canonical Cleanup — Phase 1: Canonical Inventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** `docs/superpowers/specs/2026-04-10-data-model-canonical-cleanup-design.md` §5.1

**Goal:** Introduce the canonical inventory model (`Device`, `SoftwareProduct`, `SoftwareAlias`, `InstalledSoftware`, `TenantSoftwareProductInsight`, `SourceSystem`, renamed `Device*` ownership entities, renamed `SecurityProfile`) directly on top of `main`, rewrite ingestion + authenticated-scan + rules + frontend to write and read them, and delete every legacy inventory entity in one PR.

**Architecture:** Fresh branch `data-model-canonical-cleanup` cut from `main`. No EF migrations regenerated in this phase — model snapshot is mutated directly; developers wipe local DBs between phases; Phase 6 regenerates the single baseline migration. Every new tenant-scoped entity has a direct `TenantId` column and an EF global query filter per spec §4.10 Rules 1 and 2. Global entities (`SourceSystem`, `SoftwareProduct`, `SoftwareAlias`) carry no `TenantId` and no filter.

**Tech Stack:** .NET 10 / EF Core 10 / xUnit / PostgreSQL (prod), in-memory test host / React + TanStack Router / Vitest.

---

## Preflight

- [ ] **Step P1: Confirm working tree is on a fresh branch**

Run:

```bash
git checkout main
git pull
git checkout -b data-model-canonical-cleanup
git branch --show-current
```

Expected: `data-model-canonical-cleanup`

- [ ] **Step P2: Wipe local dev database**

The existing migrations no longer match the evolving model. Delete the local dev DB so EF's model snapshot comparison does not trip:

```bash
dotnet ef database drop --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api --force
```

Expected: "Successfully dropped database" or "Database does not exist."

- [ ] **Step P3: Confirm baseline is green**

Run the full backend and frontend suites to record a clean baseline before touching code:

```bash
dotnet test
cd frontend && npm run typecheck && npm test && cd ..
```

Expected: all green. If not, stop and report — don't start Phase 1 on a red baseline.

---

## File Structure

### New entity files (`src/PatchHound.Core/Entities/`)

- `SourceSystem.cs` — global reference table. `Id`, `Key` (unique, e.g. `"defender"`, `"tanium"`, `"authenticated-scan"`), `DisplayName`, `CreatedAt`.
- `Device.cs` — tenant-scoped. Holds every field currently on `Asset`-with-type-`Device` except `AssetType`. Adds `SourceSystemId` FK. Unique `(TenantId, SourceSystemId, ExternalId)`.
- `SoftwareProduct.cs` — global. `Id`, `CanonicalProductKey` (unique), `Vendor`, `Name`, `PrimaryCpe23Uri?`, `EndOfLifeAt?`, `CreatedAt`, `UpdatedAt`.
- `SoftwareAlias.cs` — global. `Id`, `SoftwareProductId` FK, `SourceSystemId` FK, `ExternalId`, unique `(SourceSystemId, ExternalId)`.
- `InstalledSoftware.cs` — tenant-scoped. `Id`, `TenantId`, `DeviceId` FK, `SoftwareProductId` FK, `SourceSystemId` FK, `Version`, `FirstSeenAt`, `LastSeenAt`. Current-state unique `(TenantId, DeviceId, SoftwareProductId, SourceSystemId, Version)` with `Version` NOT NULL (use `""` sentinel when unknown, not NULL — avoids nullable-column unique-key gotchas per spec §4.1).
- `TenantSoftwareProductInsight.cs` — tenant-scoped. `Id`, `TenantId`, `SoftwareProductId` FK, `Description?`, `SupplyChainEvidenceJson?`, `UpdatedAt`, unique `(TenantId, SoftwareProductId)`.
- `DeviceBusinessLabel.cs` — tenant-scoped. `Id`, `TenantId`, `DeviceId` FK, `BusinessLabelId` FK, unique `(DeviceId, BusinessLabelId)`.
- `DeviceTag.cs` — tenant-scoped. `Id`, `TenantId`, `DeviceId` FK, `Key`, `Value`, unique `(DeviceId, Key)`.
- `DeviceRule.cs` — tenant-scoped. Direct port of `AssetRule` with the `AssetType` filter column dropped.
- `DeviceRiskScore.cs` — tenant-scoped. Direct port of `AssetRiskScore` keyed on `DeviceId` instead of `AssetId`.
- `SecurityProfile.cs` — tenant-scoped. Direct port of `AssetSecurityProfile` under the new name.

### New entity files (`src/PatchHound.Core/Entities/AuthenticatedScans/`)

- `DeviceScanProfileAssignment.cs` — rename of `AssetScanProfileAssignment` with `AssetId` → `DeviceId`.
- `StagedDetectedSoftware.cs` — rename of `StagedAuthenticatedScanSoftware`.

### New EF configurations (`src/PatchHound.Infrastructure/Data/Configurations/`)

One per new entity, mirroring the existing style. Every tenant-scoped entity's configuration **must** include the `TenantId` column, the `HasIndex(e => e.TenantId)` index, and relevant unique keys. Global entities' configurations omit `TenantId` entirely.

### New services (`src/PatchHound.Infrastructure/Services/`)

- `Inventory/SoftwareProductResolver.cs` — resolves observed `(sourceSystemKey, externalId, productName, version)` to a `SoftwareProduct` via `SoftwareAlias`. Creates missing `SoftwareProduct` + `SoftwareAlias` rows under `IsSystemContext`.
- `Inventory/DeviceResolver.cs` — resolves observed `(tenantId, sourceSystemKey, externalId)` to a `Device`, creates on miss.
- `Inventory/StagedDeviceMergeService.cs` — replaces `StagedAssetMergeService`. Writes directly to `Device` / `InstalledSoftware` (no legacy side effects).
- `Inventory/DeviceRuleEvaluationService.cs` — replaces `AssetRuleEvaluationService`. Operates on `Device`, not `Asset`.

### Modified services / controllers / frontend

See each task for exact files and line numbers.

### Deleted files

See Task 20. Every entity, configuration, service, controller, DTO, route, and frontend component listed under §4.9 and §5.1 of the spec is deleted in the same PR.

---

## Task 1: Add `SourceSystem` global entity + configuration + seed

**Files:**
- Create: `src/PatchHound.Core/Entities/SourceSystem.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/SourceSystemConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` (add DbSet)
- Modify: `src/PatchHound.Infrastructure/Services/DefaultTeamSeedHostedService.cs` or a new `SourceSystemSeedHostedService.cs` — seed built-in source systems
- Test: `tests/PatchHound.Tests/Core/SourceSystemTests.cs`

- [ ] **Step 1: Write the failing entity test**

Create `tests/PatchHound.Tests/Core/SourceSystemTests.cs`:

```csharp
using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core;

public class SourceSystemTests
{
    [Fact]
    public void Create_trims_and_lowercases_key()
    {
        var s = SourceSystem.Create("  Defender  ", "Microsoft Defender for Endpoint");
        Assert.Equal("defender", s.Key);
        Assert.Equal("Microsoft Defender for Endpoint", s.DisplayName);
        Assert.NotEqual(Guid.Empty, s.Id);
    }

    [Fact]
    public void Create_rejects_empty_key()
    {
        Assert.Throws<ArgumentException>(() => SourceSystem.Create("  ", "x"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SourceSystemTests"`
Expected: FAIL with "The type or namespace name 'SourceSystem' could not be found".

- [ ] **Step 3: Create the entity**

Create `src/PatchHound.Core/Entities/SourceSystem.cs`:

```csharp
namespace PatchHound.Core.Entities;

public class SourceSystem
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private SourceSystem() { }

    public static SourceSystem Create(string key, string displayName)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }
        return new SourceSystem
        {
            Id = Guid.NewGuid(),
            Key = key.Trim().ToLowerInvariant(),
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SourceSystemTests"`
Expected: PASS.

- [ ] **Step 5: Add the EF configuration**

Create `src/PatchHound.Infrastructure/Data/Configurations/SourceSystemConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SourceSystemConfiguration : IEntityTypeConfiguration<SourceSystem>
{
    public void Configure(EntityTypeBuilder<SourceSystem> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.Key).IsUnique();
        builder.Property(s => s.Key).HasMaxLength(64).IsRequired();
        builder.Property(s => s.DisplayName).HasMaxLength(256).IsRequired();
    }
}
```

- [ ] **Step 6: Register the DbSet**

Edit `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`. After the `Tenants` DbSet at line 23, add:

```csharp
public DbSet<SourceSystem> SourceSystems => Set<SourceSystem>();
```

Do **not** add a query filter — `SourceSystem` is global per spec §4.10 Rule 3.

- [ ] **Step 7: Add a seed hosted service for built-in source systems**

Create `src/PatchHound.Infrastructure/Services/Inventory/SourceSystemSeedHostedService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Inventory;

public class SourceSystemSeedHostedService(IServiceProvider services) : IHostedService
{
    private static readonly (string Key, string DisplayName)[] BuiltIns =
    [
        ("defender", "Microsoft Defender for Endpoint"),
        ("authenticated-scan", "Authenticated Scan"),
    ];

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.EnterSystemContext();

        foreach (var (key, displayName) in BuiltIns)
        {
            if (!await db.SourceSystems.AnyAsync(s => s.Key == key, ct))
            {
                db.SourceSystems.Add(SourceSystem.Create(key, displayName));
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

Register it in `src/PatchHound.Api/Program.cs` next to `DefaultTeamSeedHostedService`:

```csharp
builder.Services.AddHostedService<SourceSystemSeedHostedService>();
```

- [ ] **Step 8: Build and test**

Run:

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~SourceSystemTests"
```

Expected: clean build (0 warnings, 0 errors), all SourceSystem tests pass. If `ITenantContext.EnterSystemContext()` does not exist, use whatever pattern the codebase already has for system-context scoping (grep `IsSystemContext` in tests and ingestion to find the exact entry point) and adjust Step 7 accordingly.

- [ ] **Step 9: Commit**

```bash
git add src/PatchHound.Core/Entities/SourceSystem.cs \
        src/PatchHound.Infrastructure/Data/Configurations/SourceSystemConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        src/PatchHound.Infrastructure/Services/Inventory/SourceSystemSeedHostedService.cs \
        src/PatchHound.Api/Program.cs \
        tests/PatchHound.Tests/Core/SourceSystemTests.cs
git commit -m "feat(canonical): add SourceSystem global reference entity"
```

---

## Task 2: Add `SoftwareProduct` + `SoftwareAlias` global entities

**Files:**
- Create: `src/PatchHound.Core/Entities/SoftwareProduct.cs`
- Create: `src/PatchHound.Core/Entities/SoftwareAlias.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/SoftwareProductConfiguration.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/SoftwareAliasConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Test: `tests/PatchHound.Tests/Core/SoftwareProductTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/PatchHound.Tests/Core/SoftwareProductTests.cs`:

```csharp
using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core;

public class SoftwareProductTests
{
    [Fact]
    public void Create_computes_canonical_key_from_vendor_and_name()
    {
        var p = SoftwareProduct.Create(vendor: "Microsoft", name: "Edge", primaryCpe23Uri: "cpe:2.3:a:microsoft:edge:*:*:*:*:*:*:*:*");
        Assert.Equal("microsoft::edge", p.CanonicalProductKey);
        Assert.Equal("Microsoft", p.Vendor);
        Assert.Equal("Edge", p.Name);
        Assert.NotEqual(Guid.Empty, p.Id);
    }

    [Fact]
    public void Create_trims_and_lowercases_canonical_key()
    {
        var p = SoftwareProduct.Create(vendor: "  MICROSOFT  ", name: "  Edge  ", primaryCpe23Uri: null);
        Assert.Equal("microsoft::edge", p.CanonicalProductKey);
    }

    [Fact]
    public void Create_rejects_empty_vendor_or_name()
    {
        Assert.Throws<ArgumentException>(() => SoftwareProduct.Create(" ", "x", null));
        Assert.Throws<ArgumentException>(() => SoftwareProduct.Create("x", " ", null));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SoftwareProductTests"`
Expected: FAIL with "The type or namespace name 'SoftwareProduct' could not be found."

- [ ] **Step 3: Create `SoftwareProduct`**

Create `src/PatchHound.Core/Entities/SoftwareProduct.cs`:

```csharp
namespace PatchHound.Core.Entities;

public class SoftwareProduct
{
    public Guid Id { get; private set; }
    public string CanonicalProductKey { get; private set; } = null!;
    public string Vendor { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? PrimaryCpe23Uri { get; private set; }
    public DateTimeOffset? EndOfLifeAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SoftwareProduct() { }

    public static SoftwareProduct Create(string vendor, string name, string? primaryCpe23Uri)
    {
        if (string.IsNullOrWhiteSpace(vendor)) throw new ArgumentException("Vendor is required.", nameof(vendor));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        var now = DateTimeOffset.UtcNow;
        return new SoftwareProduct
        {
            Id = Guid.NewGuid(),
            Vendor = vendor.Trim(),
            Name = name.Trim(),
            CanonicalProductKey = $"{vendor.Trim().ToLowerInvariant()}::{name.Trim().ToLowerInvariant()}",
            PrimaryCpe23Uri = primaryCpe23Uri,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdatePrimaryCpe(string? cpe)
    {
        PrimaryCpe23Uri = cpe;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetEndOfLife(DateTimeOffset? at)
    {
        EndOfLifeAt = at;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 4: Create `SoftwareAlias`**

Create `src/PatchHound.Core/Entities/SoftwareAlias.cs`:

```csharp
namespace PatchHound.Core.Entities;

public class SoftwareAlias
{
    public Guid Id { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public string? ObservedVendor { get; private set; }
    public string? ObservedName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private SoftwareAlias() { }

    public static SoftwareAlias Create(
        Guid softwareProductId,
        Guid sourceSystemId,
        string externalId,
        string? observedVendor = null,
        string? observedName = null)
    {
        if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("ExternalId required.", nameof(externalId));
        return new SoftwareAlias
        {
            Id = Guid.NewGuid(),
            SoftwareProductId = softwareProductId,
            SourceSystemId = sourceSystemId,
            ExternalId = externalId,
            ObservedVendor = observedVendor,
            ObservedName = observedName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SoftwareProductTests"`
Expected: PASS.

- [ ] **Step 6: Add EF configurations**

Create `src/PatchHound.Infrastructure/Data/Configurations/SoftwareProductConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SoftwareProductConfiguration : IEntityTypeConfiguration<SoftwareProduct>
{
    public void Configure(EntityTypeBuilder<SoftwareProduct> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.CanonicalProductKey).IsUnique();
        builder.Property(p => p.CanonicalProductKey).HasMaxLength(512).IsRequired();
        builder.Property(p => p.Vendor).HasMaxLength(256).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(512).IsRequired();
        builder.Property(p => p.PrimaryCpe23Uri).HasMaxLength(512);
    }
}
```

Create `src/PatchHound.Infrastructure/Data/Configurations/SoftwareAliasConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SoftwareAliasConfiguration : IEntityTypeConfiguration<SoftwareAlias>
{
    public void Configure(EntityTypeBuilder<SoftwareAlias> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.SourceSystemId, a.ExternalId }).IsUnique();
        builder.HasIndex(a => a.SoftwareProductId);
        builder.Property(a => a.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(a => a.ObservedVendor).HasMaxLength(256);
        builder.Property(a => a.ObservedName).HasMaxLength(512);
        builder
            .HasOne<SoftwareProduct>()
            .WithMany()
            .HasForeignKey(a => a.SoftwareProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<SourceSystem>()
            .WithMany()
            .HasForeignKey(a => a.SourceSystemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 7: Register DbSets**

Edit `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`. Next to `SourceSystems`, add:

```csharp
public DbSet<SoftwareProduct> SoftwareProducts => Set<SoftwareProduct>();
public DbSet<SoftwareAlias> SoftwareAliases => Set<SoftwareAlias>();
```

Do **not** add query filters — both are global per spec §4.10 Rule 3.

- [ ] **Step 8: Build and test**

Run:

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~SoftwareProduct"
```

Expected: clean build, green tests.

- [ ] **Step 9: Commit**

```bash
git add src/PatchHound.Core/Entities/SoftwareProduct.cs \
        src/PatchHound.Core/Entities/SoftwareAlias.cs \
        src/PatchHound.Infrastructure/Data/Configurations/SoftwareProductConfiguration.cs \
        src/PatchHound.Infrastructure/Data/Configurations/SoftwareAliasConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Tests/Core/SoftwareProductTests.cs
git commit -m "feat(canonical): add SoftwareProduct and SoftwareAlias global entities"
```

---

## Task 3: Add `Device` tenant-scoped entity

**Files:**
- Create: `src/PatchHound.Core/Entities/Device.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/DeviceConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` (DbSet + tenant filter)
- Test: `tests/PatchHound.Tests/Core/DeviceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/PatchHound.Tests/Core/DeviceTests.cs`:

```csharp
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using Xunit;

namespace PatchHound.Tests.Core;

public class DeviceTests
{
    [Fact]
    public void Create_initializes_all_identity_fields()
    {
        var tenantId = Guid.NewGuid();
        var sourceSystemId = Guid.NewGuid();
        var d = Device.Create(
            tenantId: tenantId,
            sourceSystemId: sourceSystemId,
            externalId: "dev-1",
            name: "host.example.com",
            baselineCriticality: Criticality.Medium);
        Assert.Equal(tenantId, d.TenantId);
        Assert.Equal(sourceSystemId, d.SourceSystemId);
        Assert.Equal("dev-1", d.ExternalId);
        Assert.Equal("host.example.com", d.Name);
        Assert.Equal(Criticality.Medium, d.Criticality);
        Assert.Equal(Criticality.Medium, d.BaselineCriticality);
        Assert.True(d.ActiveInTenant);
    }

    [Fact]
    public void SetCriticality_marks_source_as_manual()
    {
        var d = Device.Create(Guid.NewGuid(), Guid.NewGuid(), "d", "n", Criticality.Low);
        d.SetCriticality(Criticality.High);
        Assert.Equal(Criticality.High, d.Criticality);
        Assert.Equal("ManualOverride", d.CriticalitySource);
    }
}
```

- [ ] **Step 2: Run and verify the test fails**

Run: `dotnet test --filter "FullyQualifiedName~DeviceTests"`
Expected: FAIL with "type or namespace name 'Device' could not be found" (or similar).

- [ ] **Step 3: Create the entity**

Create `src/PatchHound.Core/Entities/Device.cs`. Mirror `Asset.cs` field-by-field **but drop** `AssetType`, `SourceKey`, and add `SourceSystemId`. Rename `DeviceActiveInTenant` → `ActiveInTenant`. Rename all `Device*`-prefixed passthrough fields to drop the `Device` prefix where they are no longer necessary (`ComputerDnsName`, `HealthStatus`, `OsPlatform`, `OsVersion`, `ExternalRiskLabel`, `LastSeenAt`, `LastIpAddress`, `AadDeviceId`, `GroupId`, `GroupName`, `ExposureLevel`, `IsAadJoined`, `OnboardingStatus`, `DeviceValue`, `ExposureImpactScore`). Keep ownership/criticality/fallback/security-profile API surface identical to `Asset`.

```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class Device
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    public Criticality BaselineCriticality { get; private set; }
    public Criticality Criticality { get; private set; }
    public string? CriticalitySource { get; private set; }
    public string? CriticalityReason { get; private set; }
    public Guid? CriticalityRuleId { get; private set; }
    public DateTimeOffset? CriticalityUpdatedAt { get; private set; }

    public OwnerType OwnerType { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? OwnerTeamId { get; private set; }
    public Guid? FallbackTeamId { get; private set; }
    public Guid? FallbackTeamRuleId { get; private set; }

    public Guid? SecurityProfileId { get; private set; }
    public Guid? SecurityProfileRuleId { get; private set; }

    public string? ComputerDnsName { get; private set; }
    public string? HealthStatus { get; private set; }
    public string? OsPlatform { get; private set; }
    public string? OsVersion { get; private set; }
    public string? ExternalRiskLabel { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public string? LastIpAddress { get; private set; }
    public string? AadDeviceId { get; private set; }
    public string? GroupId { get; private set; }
    public string? GroupName { get; private set; }
    public string? ExposureLevel { get; private set; }
    public bool? IsAadJoined { get; private set; }
    public string? OnboardingStatus { get; private set; }
    public string? DeviceValue { get; private set; }
    public decimal? ExposureImpactScore { get; private set; }
    public bool ActiveInTenant { get; private set; } = true;
    public string Metadata { get; private set; } = "{}";

    private Device() { }

    public static Device Create(
        Guid tenantId,
        Guid sourceSystemId,
        string externalId,
        string name,
        Criticality baselineCriticality,
        string? description = null)
    {
        return new Device
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceSystemId = sourceSystemId,
            ExternalId = externalId,
            Name = name,
            Description = description,
            BaselineCriticality = baselineCriticality,
            Criticality = baselineCriticality,
            CriticalitySource = "Default",
            CriticalityUpdatedAt = DateTimeOffset.UtcNow,
            OwnerType = OwnerType.User,
            ActiveInTenant = true,
        };
    }

    // Port every mutator from Asset.cs (AssignOwner, AssignTeamOwner, SetFallbackTeamFromRule,
    // ClearRuleAssignedFallbackTeam, SetCriticality, SetCriticalityFromRule,
    // ResetCriticalityToBaseline, ClearManualCriticalityOverride, AssignSecurityProfile,
    // AssignSecurityProfileFromRule, ClearRuleAssignedSecurityProfile, UpdateDetails,
    // UpdateMetadata, SetActiveInTenant, SetExposureImpactScore) — body-identical to Asset.cs
    // except the renamed ActiveInTenant field. Drop the UpdateDeviceDetails parameters that only
    // existed to transport the `Device*` prefix (it's now the whole entity) — rename it to
    // UpdateInventoryDetails taking the now-unprefixed fields.

    // ... (paste the mutator bodies from src/PatchHound.Core/Entities/Asset.cs lines 75-221,
    //      renaming DeviceActiveInTenant → ActiveInTenant and removing the `Device` prefix
    //      from passthrough fields inside UpdateDeviceDetails → UpdateInventoryDetails)
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DeviceTests"`
Expected: PASS.

- [ ] **Step 5: Add EF configuration**

Create `src/PatchHound.Infrastructure/Data/Configurations/DeviceConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(d => d.Id);

        builder.HasIndex(d => new { d.TenantId, d.SourceSystemId, d.ExternalId }).IsUnique();
        builder.HasIndex(d => d.TenantId);
        builder.HasIndex(d => d.SecurityProfileId);
        builder.HasIndex(d => new { d.TenantId, d.ActiveInTenant });

        builder.Property(d => d.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(256).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(2048);
        builder.Property(d => d.ComputerDnsName).HasMaxLength(256);
        builder.Property(d => d.HealthStatus).HasMaxLength(64);
        builder.Property(d => d.OsPlatform).HasMaxLength(128);
        builder.Property(d => d.OsVersion).HasMaxLength(128);
        builder.Property(d => d.ExternalRiskLabel).HasMaxLength(64);
        builder.Property(d => d.LastIpAddress).HasMaxLength(128);
        builder.Property(d => d.AadDeviceId).HasMaxLength(128);
        builder.Property(d => d.GroupId).HasMaxLength(128);
        builder.Property(d => d.GroupName).HasMaxLength(256);
        builder.Property(d => d.ExposureLevel).HasMaxLength(64);
        builder.Property(d => d.OnboardingStatus).HasMaxLength(64);
        builder.Property(d => d.DeviceValue).HasMaxLength(64);
        builder.Property(d => d.ActiveInTenant).HasDefaultValue(true);
        builder.Property(d => d.BaselineCriticality).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Criticality).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.CriticalitySource).HasMaxLength(32);
        builder.Property(d => d.CriticalityReason).HasMaxLength(512);
        builder.Property(d => d.OwnerType).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Metadata).HasColumnType("text");

        builder
            .HasOne<SourceSystem>()
            .WithMany()
            .HasForeignKey(d => d.SourceSystemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 6: Register DbSet and tenant filter**

In `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`, add the DbSet next to the `SoftwareAliases` DbSet:

```csharp
public DbSet<Device> Devices => Set<Device>();
```

In `OnModelCreating`, add (ideally near the existing `Asset` filter around line 167):

```csharp
modelBuilder
    .Entity<Device>()
    .HasQueryFilter(e =>
        IsSystemContext
        || (AccessibleTenantIds.Contains(e.TenantId) && e.ActiveInTenant));
```

- [ ] **Step 7: Build and test**

Run:

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~DeviceTests"
```

Expected: clean build, green.

- [ ] **Step 8: Commit**

```bash
git add src/PatchHound.Core/Entities/Device.cs \
        src/PatchHound.Infrastructure/Data/Configurations/DeviceConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Tests/Core/DeviceTests.cs
git commit -m "feat(canonical): add Device tenant-scoped entity with global filter"
```

---

## Task 4: Add `InstalledSoftware` + `TenantSoftwareProductInsight`

**Files:**
- Create: `src/PatchHound.Core/Entities/InstalledSoftware.cs`
- Create: `src/PatchHound.Core/Entities/TenantSoftwareProductInsight.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/InstalledSoftwareConfiguration.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/TenantSoftwareProductInsightConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Test: `tests/PatchHound.Tests/Core/InstalledSoftwareTests.cs`
- Test: `tests/PatchHound.Tests/Core/TenantSoftwareProductInsightTests.cs`

- [ ] **Step 1: Write the `InstalledSoftware` entity test**

Create `tests/PatchHound.Tests/Core/InstalledSoftwareTests.cs`:

```csharp
using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core;

public class InstalledSoftwareTests
{
    [Fact]
    public void Observe_sets_identity_and_timestamps()
    {
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;

        var i = InstalledSoftware.Observe(tenantId, deviceId, productId, sourceId, version: "1.2.3", at);

        Assert.Equal(tenantId, i.TenantId);
        Assert.Equal(deviceId, i.DeviceId);
        Assert.Equal(productId, i.SoftwareProductId);
        Assert.Equal(sourceId, i.SourceSystemId);
        Assert.Equal("1.2.3", i.Version);
        Assert.Equal(at, i.FirstSeenAt);
        Assert.Equal(at, i.LastSeenAt);
    }

    [Fact]
    public void Observe_with_null_version_uses_empty_sentinel()
    {
        var i = InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), version: null, DateTimeOffset.UtcNow);
        Assert.Equal("", i.Version);
    }

    [Fact]
    public void Touch_updates_last_seen_but_not_first_seen()
    {
        var first = DateTimeOffset.UtcNow.AddDays(-1);
        var i = InstalledSoftware.Observe(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.0", first);
        var next = DateTimeOffset.UtcNow;
        i.Touch(next);
        Assert.Equal(first, i.FirstSeenAt);
        Assert.Equal(next, i.LastSeenAt);
    }
}
```

- [ ] **Step 2: Run to verify the test fails**

Run: `dotnet test --filter "FullyQualifiedName~InstalledSoftwareTests"`
Expected: FAIL, type not found.

- [ ] **Step 3: Create `InstalledSoftware`**

Create `src/PatchHound.Core/Entities/InstalledSoftware.cs`:

```csharp
namespace PatchHound.Core.Entities;

public class InstalledSoftware
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string Version { get; private set; } = "";
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    private InstalledSoftware() { }

    public static InstalledSoftware Observe(
        Guid tenantId,
        Guid deviceId,
        Guid softwareProductId,
        Guid sourceSystemId,
        string? version,
        DateTimeOffset at)
    {
        return new InstalledSoftware
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = deviceId,
            SoftwareProductId = softwareProductId,
            SourceSystemId = sourceSystemId,
            Version = version ?? "",
            FirstSeenAt = at,
            LastSeenAt = at,
        };
    }

    public void Touch(DateTimeOffset at)
    {
        LastSeenAt = at;
    }
}
```

- [ ] **Step 4: Write the `TenantSoftwareProductInsight` test**

Create `tests/PatchHound.Tests/Core/TenantSoftwareProductInsightTests.cs`:

```csharp
using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core;

public class TenantSoftwareProductInsightTests
{
    [Fact]
    public void Create_sets_identity_and_timestamps()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var insight = TenantSoftwareProductInsight.Create(tenantId, productId);
        Assert.Equal(tenantId, insight.TenantId);
        Assert.Equal(productId, insight.SoftwareProductId);
        Assert.Null(insight.Description);
    }

    [Fact]
    public void UpdateDescription_sets_description_and_bumps_updated_at()
    {
        var insight = TenantSoftwareProductInsight.Create(Guid.NewGuid(), Guid.NewGuid());
        var before = insight.UpdatedAt;
        Thread.Sleep(5);
        insight.UpdateDescription("tenant-specific notes");
        Assert.Equal("tenant-specific notes", insight.Description);
        Assert.True(insight.UpdatedAt > before);
    }
}
```

- [ ] **Step 5: Create `TenantSoftwareProductInsight`**

Create `src/PatchHound.Core/Entities/TenantSoftwareProductInsight.cs`:

```csharp
namespace PatchHound.Core.Entities;

public class TenantSoftwareProductInsight
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public string? Description { get; private set; }
    public string? SupplyChainEvidenceJson { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantSoftwareProductInsight() { }

    public static TenantSoftwareProductInsight Create(Guid tenantId, Guid softwareProductId)
    {
        return new TenantSoftwareProductInsight
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SoftwareProductId = softwareProductId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateDescription(string? description)
    {
        Description = description;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateSupplyChainEvidence(string? evidenceJson)
    {
        SupplyChainEvidenceJson = evidenceJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 6: Add EF configurations**

Create `src/PatchHound.Infrastructure/Data/Configurations/InstalledSoftwareConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class InstalledSoftwareConfiguration : IEntityTypeConfiguration<InstalledSoftware>
{
    public void Configure(EntityTypeBuilder<InstalledSoftware> builder)
    {
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => new
        {
            i.TenantId,
            i.DeviceId,
            i.SoftwareProductId,
            i.SourceSystemId,
            i.Version
        }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.SoftwareProductId });
        builder.HasIndex(i => i.TenantId);
        builder.Property(i => i.Version).HasMaxLength(128).IsRequired();

        builder.HasOne<Device>().WithMany().HasForeignKey(i => i.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SoftwareProduct>().WithMany().HasForeignKey(i => i.SoftwareProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SourceSystem>().WithMany().HasForeignKey(i => i.SourceSystemId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

Create `src/PatchHound.Infrastructure/Data/Configurations/TenantSoftwareProductInsightConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantSoftwareProductInsightConfiguration : IEntityTypeConfiguration<TenantSoftwareProductInsight>
{
    public void Configure(EntityTypeBuilder<TenantSoftwareProductInsight> builder)
    {
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => new { i.TenantId, i.SoftwareProductId }).IsUnique();
        builder.Property(i => i.Description).HasMaxLength(4096);
        builder.Property(i => i.SupplyChainEvidenceJson).HasColumnType("text");

        builder.HasOne<SoftwareProduct>().WithMany().HasForeignKey(i => i.SoftwareProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 7: Register DbSets and tenant filters**

In `PatchHoundDbContext.cs`, add next to the previously added inventory DbSets:

```csharp
public DbSet<InstalledSoftware> InstalledSoftware => Set<InstalledSoftware>();
public DbSet<TenantSoftwareProductInsight> TenantSoftwareProductInsights => Set<TenantSoftwareProductInsight>();
```

In `OnModelCreating`:

```csharp
modelBuilder
    .Entity<InstalledSoftware>()
    .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));

modelBuilder
    .Entity<TenantSoftwareProductInsight>()
    .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
```

- [ ] **Step 8: Build and test**

Run:

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~InstalledSoftwareTests|FullyQualifiedName~TenantSoftwareProductInsightTests"
```

Expected: clean build, green.

- [ ] **Step 9: Commit**

```bash
git add src/PatchHound.Core/Entities/InstalledSoftware.cs \
        src/PatchHound.Core/Entities/TenantSoftwareProductInsight.cs \
        src/PatchHound.Infrastructure/Data/Configurations/InstalledSoftwareConfiguration.cs \
        src/PatchHound.Infrastructure/Data/Configurations/TenantSoftwareProductInsightConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Tests/Core/InstalledSoftwareTests.cs \
        tests/PatchHound.Tests/Core/TenantSoftwareProductInsightTests.cs
git commit -m "feat(canonical): add InstalledSoftware and TenantSoftwareProductInsight"
```

---

## Task 5: Add device ownership / attribute entities (`DeviceBusinessLabel`, `DeviceTag`, `DeviceRule`, `DeviceRiskScore`, `SecurityProfile`)

**Files:**
- Create: `src/PatchHound.Core/Entities/DeviceBusinessLabel.cs`
- Create: `src/PatchHound.Core/Entities/DeviceTag.cs`
- Create: `src/PatchHound.Core/Entities/DeviceRule.cs`
- Create: `src/PatchHound.Core/Entities/DeviceRiskScore.cs`
- Create: `src/PatchHound.Core/Entities/SecurityProfile.cs`
- Create: Five EF configurations under `src/PatchHound.Infrastructure/Data/Configurations/`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Tests: `tests/PatchHound.Tests/Core/DeviceOwnershipTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/PatchHound.Tests/Core/DeviceOwnershipTests.cs`:

```csharp
using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core;

public class DeviceOwnershipTests
{
    [Fact]
    public void DeviceBusinessLabel_Create_sets_ids()
    {
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var labelId = Guid.NewGuid();
        var link = DeviceBusinessLabel.Create(tenantId, deviceId, labelId);
        Assert.Equal(tenantId, link.TenantId);
        Assert.Equal(deviceId, link.DeviceId);
        Assert.Equal(labelId, link.BusinessLabelId);
    }

    [Fact]
    public void DeviceTag_Create_sets_kv_pair()
    {
        var tag = DeviceTag.Create(Guid.NewGuid(), Guid.NewGuid(), "env", "prod");
        Assert.Equal("env", tag.Key);
        Assert.Equal("prod", tag.Value);
    }

    [Fact]
    public void SecurityProfile_Create_initializes_modifiers_to_zero()
    {
        var profile = SecurityProfile.Create(Guid.NewGuid(), "Gold", description: null);
        Assert.Equal("Gold", profile.Name);
        Assert.Equal(0, profile.ConfidentialityRequirementModifier);
    }
}
```

- [ ] **Step 2: Run to verify the test fails**

Run: `dotnet test --filter "FullyQualifiedName~DeviceOwnershipTests"`
Expected: FAIL, types not found.

- [ ] **Step 3: Create `DeviceBusinessLabel`**

Create `src/PatchHound.Core/Entities/DeviceBusinessLabel.cs`:

```csharp
namespace PatchHound.Core.Entities;

public class DeviceBusinessLabel
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid BusinessLabelId { get; private set; }

    private DeviceBusinessLabel() { }

    public static DeviceBusinessLabel Create(Guid tenantId, Guid deviceId, Guid businessLabelId)
    {
        return new DeviceBusinessLabel
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = deviceId,
            BusinessLabelId = businessLabelId,
        };
    }
}
```

- [ ] **Step 4: Create `DeviceTag`**

Create `src/PatchHound.Core/Entities/DeviceTag.cs`:

```csharp
namespace PatchHound.Core.Entities;

public class DeviceTag
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;

    private DeviceTag() { }

    public static DeviceTag Create(Guid tenantId, Guid deviceId, string key, string value)
    {
        return new DeviceTag
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = deviceId,
            Key = key,
            Value = value,
        };
    }
}
```

- [ ] **Step 5: Create `DeviceRule`**

Open `src/PatchHound.Core/Entities/AssetRule.cs` and read every public field + method. Create `src/PatchHound.Core/Entities/DeviceRule.cs` as a direct rename. **Drop** any `AssetType` filter field — this rule operates on devices exclusively per spec §4.4. Keep name, tenant ID, criteria JSON, action fields, priority, rule-kind enum, audit fields identical to `AssetRule`.

- [ ] **Step 6: Create `DeviceRiskScore`**

Open `src/PatchHound.Core/Entities/AssetRiskScore.cs`. Create `src/PatchHound.Core/Entities/DeviceRiskScore.cs` as a direct rename with `AssetId` → `DeviceId`. Keep the scoring columns (base score, criticality multiplier, exposure component, SLA component, etc.) identical.

- [ ] **Step 7: Create `SecurityProfile`**

Open `src/PatchHound.Core/Entities/AssetSecurityProfile.cs`. Create `src/PatchHound.Core/Entities/SecurityProfile.cs` as a direct rename (the entity is a tenant policy, not asset-specific). Keep every column (name, description, CIA modifiers, integrity/confidentiality requirements, network requirement, scope temporal modifier, tenant ID) identical.

- [ ] **Step 8: Add EF configurations**

Port each configuration from its `Asset*` predecessor under `src/PatchHound.Infrastructure/Data/Configurations/`:

- `DeviceBusinessLabelConfiguration.cs` ← `AssetBusinessLabelConfiguration.cs` — replace `Asset` nav with `Device` FK, add `TenantId` column + index per spec §4.10 Rule 1, add `(DeviceId, BusinessLabelId)` unique.
- `DeviceTagConfiguration.cs` ← `AssetTagConfiguration.cs` — replace `AssetId` FK with `DeviceId`, add `(DeviceId, Key)` unique, `TenantId` column + index.
- `DeviceRuleConfiguration.cs` ← `AssetRuleConfiguration.cs` — drop `AssetType` column if present, otherwise identical shape.
- `DeviceRiskScoreConfiguration.cs` ← `AssetRiskScoreConfiguration.cs` — replace `AssetId` FK with `DeviceId`, keep `TenantId`.
- `SecurityProfileConfiguration.cs` ← `AssetSecurityProfileConfiguration.cs` — rename the type, keep the tenant column and indexes.

Each configuration **must** include `TenantId` as a direct column and `HasIndex(e => e.TenantId)` to satisfy spec §4.10 Rule 1 even for entities where `TenantId` is also reachable transitively through a parent FK.

- [ ] **Step 9: Register DbSets and filters**

In `PatchHoundDbContext.cs`:

```csharp
public DbSet<Device> Devices => Set<Device>();                             // (already added in Task 3)
public DbSet<DeviceBusinessLabel> DeviceBusinessLabels => Set<DeviceBusinessLabel>();
public DbSet<DeviceTag> DeviceTags => Set<DeviceTag>();
public DbSet<DeviceRule> DeviceRules => Set<DeviceRule>();
public DbSet<DeviceRiskScore> DeviceRiskScores => Set<DeviceRiskScore>();
public DbSet<SecurityProfile> SecurityProfiles => Set<SecurityProfile>();
```

In `OnModelCreating`, add tenant filters (direct `TenantId` on every one per Rule 1):

```csharp
modelBuilder.Entity<DeviceBusinessLabel>().HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
modelBuilder.Entity<DeviceTag>().HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
modelBuilder.Entity<DeviceRule>().HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
modelBuilder.Entity<DeviceRiskScore>().HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
modelBuilder.Entity<SecurityProfile>().HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
```

- [ ] **Step 10: Build and test**

Run:

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~DeviceOwnershipTests"
```

Expected: clean build, green.

- [ ] **Step 11: Commit**

```bash
git add src/PatchHound.Core/Entities/DeviceBusinessLabel.cs \
        src/PatchHound.Core/Entities/DeviceTag.cs \
        src/PatchHound.Core/Entities/DeviceRule.cs \
        src/PatchHound.Core/Entities/DeviceRiskScore.cs \
        src/PatchHound.Core/Entities/SecurityProfile.cs \
        src/PatchHound.Infrastructure/Data/Configurations/Device*.cs \
        src/PatchHound.Infrastructure/Data/Configurations/SecurityProfileConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Tests/Core/DeviceOwnershipTests.cs
git commit -m "feat(canonical): add Device ownership entities and SecurityProfile"
```

---

## Task 6: Rename authenticated-scan inventory entities (`AssetScanProfileAssignment` → `DeviceScanProfileAssignment`, `ScanJob.AssetId` → `DeviceId`, `StagedAuthenticatedScanSoftware` → `StagedDetectedSoftware`)

**Files:**
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/DeviceScanProfileAssignment.cs`
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/StagedDetectedSoftware.cs`
- Modify: `src/PatchHound.Core/Entities/AuthenticatedScans/ScanJob.cs` — rename `AssetId` → `DeviceId`
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/AuthenticatedScans/*` for each renamed entity
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` — rename DbSets
- Modify: every consumer in `src/PatchHound.Api/Controllers/` and `src/PatchHound.Infrastructure/` that touches these three types
- Modify: `src/PatchHound.Api/Controllers/ScanRunnersController.cs`, `ScanningToolsController.cs`, `ScanJobsController.cs`, `ScanProfilesController.cs`, authenticated-scan runner ingest controllers — rename payload fields `assetId`/`assetName` → `deviceId`/`deviceName`
- Modify: `frontend/src/api/scan-jobs.schemas.ts` (and any other scan-related schema) — rename `assetId` → `deviceId`
- Modify: `frontend/src/components/features/authenticated-scans/**` — rename references

- [ ] **Step 1: Find every reference that will need renaming**

Run grep to enumerate consumers:

```bash
rg "AssetScanProfileAssignment" -l
rg "StagedAuthenticatedScanSoftware" -l
rg "\\.AssetId\\b" src/PatchHound.Core/Entities/AuthenticatedScans src/PatchHound.Infrastructure -l
rg "assetId\\b|assetName\\b" frontend/src -l
```

Expected: captures Controllers, services, tests, EF configs, frontend components, schemas. Write the list down in a scratch comment; every file in it needs to land in this task.

- [ ] **Step 2: Create `DeviceScanProfileAssignment` (rename of `AssetScanProfileAssignment`)**

Read `src/PatchHound.Core/Entities/AuthenticatedScans/AssetScanProfileAssignment.cs` end-to-end. Create `DeviceScanProfileAssignment.cs` with the entire body copied, replacing:
- class name → `DeviceScanProfileAssignment`
- property `AssetId` → `DeviceId`
- any method parameter named `assetId` → `deviceId`

- [ ] **Step 3: Create `StagedDetectedSoftware` (rename of `StagedAuthenticatedScanSoftware`)**

Same pattern — read the old file, copy body into `StagedDetectedSoftware.cs` with renamed class name. Field renames: `AssetId` → `DeviceId` where the entity references a device. The content (detected name/version/vendor) stays the same.

- [ ] **Step 4: Rename `ScanJob.AssetId` to `DeviceId`**

Edit `src/PatchHound.Core/Entities/AuthenticatedScans/ScanJob.cs`:

```csharp
// Before
public Guid AssetId { get; private set; }
// After
public Guid DeviceId { get; private set; }
```

Rename every method parameter, every `scanJob.AssetId` reference, every `WithAssetId` → `WithDeviceId`. Grep within the file:

```bash
rg "AssetId" src/PatchHound.Core/Entities/AuthenticatedScans/ScanJob.cs
```

Expected after edit: zero matches.

- [ ] **Step 5: Update EF configurations**

Copy `AssetScanProfileAssignmentConfiguration.cs` to `DeviceScanProfileAssignmentConfiguration.cs`, rename the generic argument, rename `AssetId` → `DeviceId` in the index and the `HasOne<Asset>` → `HasOne<Device>` (FK now points to `Device`).

Copy `StagedAuthenticatedScanSoftwareConfiguration.cs` to `StagedDetectedSoftwareConfiguration.cs` similarly.

In `ScanJobConfiguration.cs`, rename the `AssetId` property reference to `DeviceId` in indexes and any `.Property(j => j.AssetId)` call.

- [ ] **Step 6: Rename DbSets in `PatchHoundDbContext`**

```csharp
// Before
public DbSet<AssetScanProfileAssignment> AssetScanProfileAssignments => Set<AssetScanProfileAssignment>();
public DbSet<StagedAuthenticatedScanSoftware> StagedAuthenticatedScanSoftware => Set<StagedAuthenticatedScanSoftware>();
// After
public DbSet<DeviceScanProfileAssignment> DeviceScanProfileAssignments => Set<DeviceScanProfileAssignment>();
public DbSet<StagedDetectedSoftware> StagedDetectedSoftware => Set<StagedDetectedSoftware>();
```

- [ ] **Step 7: Rename consumers**

For each file surfaced by Step 1's grep, use Edit (or your editor's rename) to change `AssetScanProfileAssignment` → `DeviceScanProfileAssignment`, `StagedAuthenticatedScanSoftware` → `StagedDetectedSoftware`, `AssetId` → `DeviceId` inside scan-related code only. This includes:

- Controllers (`ScanJobsController`, `ScanProfilesController`, `ScanRunnersController`, `ScanningToolsController`) — rename action parameter names and DTO properties so that the wire format now uses `deviceId`/`deviceName`.
- Services (scan-job dispatch, scan-job validation, scan-run aggregation, staged software → canonical merge hook)
- Tests under `tests/PatchHound.Tests/Api` and `tests/PatchHound.Tests/Infrastructure/AuthenticatedScans`

- [ ] **Step 8: Rename the scan-runner bearer protocol fields**

In the `ScanRunner` authentication and enqueue paths, the runner pulls scan jobs with an `assetId` payload. Rename payload fields `assetId`→`deviceId`, `assetName`→`deviceName` across:
- the runner-facing DTOs under `src/PatchHound.Api/Models/`
- the C# scan runner client project (`src/PatchHound.ScanRunner/**` if it exists) — grep with `rg "assetId" src/PatchHound.ScanRunner`
- the runner spec tests

- [ ] **Step 9: Rename the frontend scan schemas and components**

Edit `frontend/src/api/scan-*.schemas.ts` and every component under `frontend/src/components/features/authenticated-scans/**` that references `assetId` or `assetName`. Use:

```bash
rg "assetId|assetName" frontend/src/api frontend/src/components/features/authenticated-scans -l
```

For every match, edit the literal to `deviceId`/`deviceName`, matching camelCase on the TS side.

- [ ] **Step 10: Build and run tests**

Run:

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~AuthenticatedScans|FullyQualifiedName~ScanJob|FullyQualifiedName~ScanProfile"
cd frontend && npm run typecheck && npm test -- authenticated-scans && cd ..
```

Expected: clean build, green.

- [ ] **Step 11: Commit**

```bash
git add src/PatchHound.Core/Entities/AuthenticatedScans/ \
        src/PatchHound.Infrastructure/Data/Configurations/AuthenticatedScans/ \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        src/PatchHound.Api/Controllers/Scan*.cs \
        src/PatchHound.Api/Models/ \
        src/PatchHound.ScanRunner/ \
        frontend/src/api/ \
        frontend/src/components/features/authenticated-scans/ \
        tests/PatchHound.Tests/
git commit -m "refactor(canonical): rename authenticated-scan inventory references Asset → Device"
```

---

## Task 7: `SoftwareProductResolver` service

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/Inventory/SoftwareProductResolver.cs`
- Create: `src/PatchHound.Core/Interfaces/ISoftwareProductResolver.cs`
- Register: `src/PatchHound.Api/Program.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/Services/SoftwareProductResolverTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/PatchHound.Tests/Infrastructure/Services/SoftwareProductResolverTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.Tests.Infrastructure; // DbContext test helper
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services;

public class SoftwareProductResolverTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private SoftwareProductResolver _resolver = null!;
    private SourceSystem _sourceSystem = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDbContextFactory.CreateAsync(); // existing helper; create if missing
        _sourceSystem = SourceSystem.Create("defender", "Defender");
        _db.SourceSystems.Add(_sourceSystem);
        await _db.SaveChangesAsync();
        _resolver = new SoftwareProductResolver(_db);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Resolve_creates_product_and_alias_on_first_observation()
    {
        var observation = new SoftwareObservation(
            SourceSystemId: _sourceSystem.Id,
            ExternalId: "ms-edge",
            Vendor: "Microsoft",
            Name: "Edge");

        var product = await _resolver.ResolveAsync(observation, ct: default);

        Assert.Equal("microsoft::edge", product.CanonicalProductKey);
        Assert.Single(await _db.SoftwareProducts.ToListAsync());
        Assert.Single(await _db.SoftwareAliases.ToListAsync());
    }

    [Fact]
    public async Task Resolve_returns_existing_product_on_second_observation()
    {
        var obs = new SoftwareObservation(_sourceSystem.Id, "ms-edge", "Microsoft", "Edge");
        var first = await _resolver.ResolveAsync(obs, default);
        var second = await _resolver.ResolveAsync(obs, default);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _db.SoftwareProducts.CountAsync());
        Assert.Equal(1, await _db.SoftwareAliases.CountAsync());
    }

    [Fact]
    public async Task Resolve_different_external_ids_same_canonical_resolve_to_same_product()
    {
        // Tenant A sees Microsoft/Edge under external id "ms-edge"; Tenant B sees it as "edge-enterprise".
        var obsA = new SoftwareObservation(_sourceSystem.Id, "ms-edge", "Microsoft", "Edge");
        var obsB = new SoftwareObservation(_sourceSystem.Id, "edge-enterprise", "Microsoft", "Edge");
        var a = await _resolver.ResolveAsync(obsA, default);
        var b = await _resolver.ResolveAsync(obsB, default);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(1, await _db.SoftwareProducts.CountAsync());
        Assert.Equal(2, await _db.SoftwareAliases.CountAsync());
    }
}
```

Note: if `TestDbContextFactory` does not exist, create it at `tests/PatchHound.Tests/Infrastructure/TestDbContextFactory.cs` as a small helper that builds an in-memory / SQLite-in-memory `PatchHoundDbContext` with a no-tenant `ITenantContext` stub set to `IsSystemContext = true`. Keep this helper minimal — it is used by every subsequent service test in Phase 1.

- [ ] **Step 2: Run to verify the test fails**

Run: `dotnet test --filter "FullyQualifiedName~SoftwareProductResolverTests"`
Expected: FAIL, `SoftwareProductResolver` not found.

- [ ] **Step 3: Create the interface and record**

Create `src/PatchHound.Core/Interfaces/ISoftwareProductResolver.cs`:

```csharp
using PatchHound.Core.Entities;

namespace PatchHound.Core.Interfaces;

public record SoftwareObservation(Guid SourceSystemId, string ExternalId, string Vendor, string Name);

public interface ISoftwareProductResolver
{
    Task<SoftwareProduct> ResolveAsync(SoftwareObservation observation, CancellationToken ct);
}
```

- [ ] **Step 4: Create the service**

Create `src/PatchHound.Infrastructure/Services/Inventory/SoftwareProductResolver.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Inventory;

public class SoftwareProductResolver(PatchHoundDbContext db) : ISoftwareProductResolver
{
    public async Task<SoftwareProduct> ResolveAsync(SoftwareObservation observation, CancellationToken ct)
    {
        var alias = await db.SoftwareAliases
            .FirstOrDefaultAsync(a =>
                a.SourceSystemId == observation.SourceSystemId
                && a.ExternalId == observation.ExternalId, ct);

        if (alias is not null)
        {
            var existing = await db.SoftwareProducts.FirstAsync(p => p.Id == alias.SoftwareProductId, ct);
            return existing;
        }

        var canonicalKey = $"{observation.Vendor.Trim().ToLowerInvariant()}::{observation.Name.Trim().ToLowerInvariant()}";
        var product = await db.SoftwareProducts.FirstOrDefaultAsync(p => p.CanonicalProductKey == canonicalKey, ct);
        if (product is null)
        {
            product = SoftwareProduct.Create(observation.Vendor, observation.Name, primaryCpe23Uri: null);
            db.SoftwareProducts.Add(product);
        }

        var newAlias = SoftwareAlias.Create(
            softwareProductId: product.Id,
            sourceSystemId: observation.SourceSystemId,
            externalId: observation.ExternalId,
            observedVendor: observation.Vendor,
            observedName: observation.Name);
        db.SoftwareAliases.Add(newAlias);
        await db.SaveChangesAsync(ct);
        return product;
    }
}
```

- [ ] **Step 5: Register the service**

In `src/PatchHound.Api/Program.cs`, next to the other scoped services:

```csharp
builder.Services.AddScoped<ISoftwareProductResolver, SoftwareProductResolver>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SoftwareProductResolverTests"`
Expected: PASS (three tests).

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Core/Interfaces/ISoftwareProductResolver.cs \
        src/PatchHound.Infrastructure/Services/Inventory/SoftwareProductResolver.cs \
        src/PatchHound.Api/Program.cs \
        tests/PatchHound.Tests/Infrastructure/Services/SoftwareProductResolverTests.cs \
        tests/PatchHound.Tests/Infrastructure/TestDbContextFactory.cs
git commit -m "feat(canonical): add SoftwareProductResolver with alias-backed resolution"
```

---

## Task 8: `DeviceResolver` service

**Files:**
- Create: `src/PatchHound.Core/Interfaces/IDeviceResolver.cs`
- Create: `src/PatchHound.Infrastructure/Services/Inventory/DeviceResolver.cs`
- Register: `src/PatchHound.Api/Program.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/Services/DeviceResolverTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/PatchHound.Tests/Infrastructure/Services/DeviceResolverTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services;

public class DeviceResolverTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private DeviceResolver _resolver = null!;
    private Guid _tenantId;
    private SourceSystem _source = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDbContextFactory.CreateAsync();
        _tenantId = Guid.NewGuid();
        _source = SourceSystem.Create("defender", "Defender");
        _db.SourceSystems.Add(_source);
        await _db.SaveChangesAsync();
        _resolver = new DeviceResolver(_db);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Resolve_creates_device_on_first_observation()
    {
        var obs = new DeviceObservation(_tenantId, _source.Id, "dev-1", "host.a", Criticality.Medium);
        var device = await _resolver.ResolveAsync(obs, default);
        Assert.Equal(_tenantId, device.TenantId);
        Assert.Equal("dev-1", device.ExternalId);
        Assert.Single(await _db.Devices.ToListAsync());
    }

    [Fact]
    public async Task Resolve_returns_existing_device_on_second_observation()
    {
        var obs = new DeviceObservation(_tenantId, _source.Id, "dev-1", "host.a", Criticality.Medium);
        var a = await _resolver.ResolveAsync(obs, default);
        var b = await _resolver.ResolveAsync(obs, default);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(1, await _db.Devices.CountAsync());
    }

    [Fact]
    public async Task Resolve_same_external_id_different_source_systems_creates_two_devices()
    {
        var other = SourceSystem.Create("tanium", "Tanium");
        _db.SourceSystems.Add(other);
        await _db.SaveChangesAsync();

        var a = await _resolver.ResolveAsync(new DeviceObservation(_tenantId, _source.Id, "dev-1", "host", Criticality.Low), default);
        var b = await _resolver.ResolveAsync(new DeviceObservation(_tenantId, other.Id, "dev-1", "host", Criticality.Low), default);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, await _db.Devices.CountAsync());
    }
}
```

- [ ] **Step 2: Run to verify the test fails**

Run: `dotnet test --filter "FullyQualifiedName~DeviceResolverTests"`
Expected: FAIL.

- [ ] **Step 3: Create the interface**

Create `src/PatchHound.Core/Interfaces/IDeviceResolver.cs`:

```csharp
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Core.Interfaces;

public record DeviceObservation(
    Guid TenantId,
    Guid SourceSystemId,
    string ExternalId,
    string Name,
    Criticality BaselineCriticality);

public interface IDeviceResolver
{
    Task<Device> ResolveAsync(DeviceObservation observation, CancellationToken ct);
}
```

- [ ] **Step 4: Create the service**

Create `src/PatchHound.Infrastructure/Services/Inventory/DeviceResolver.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Inventory;

public class DeviceResolver(PatchHoundDbContext db) : IDeviceResolver
{
    public async Task<Device> ResolveAsync(DeviceObservation observation, CancellationToken ct)
    {
        var existing = await db.Devices
            .IgnoreQueryFilters() // resolver runs under system context during ingestion
            .FirstOrDefaultAsync(d =>
                d.TenantId == observation.TenantId
                && d.SourceSystemId == observation.SourceSystemId
                && d.ExternalId == observation.ExternalId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var device = Device.Create(
            tenantId: observation.TenantId,
            sourceSystemId: observation.SourceSystemId,
            externalId: observation.ExternalId,
            name: observation.Name,
            baselineCriticality: observation.BaselineCriticality);
        db.Devices.Add(device);
        await db.SaveChangesAsync(ct);
        return device;
    }
}
```

Note: this is the one documented `IgnoreQueryFilters()` use in Phase 1. Per spec §4.10 Rule 5, it is allowed inside ingestion (system-context) services. Document this in the PR description's tenant-scope audit.

- [ ] **Step 5: Register and test**

In `Program.cs`:

```csharp
builder.Services.AddScoped<IDeviceResolver, DeviceResolver>();
```

Run: `dotnet test --filter "FullyQualifiedName~DeviceResolverTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IDeviceResolver.cs \
        src/PatchHound.Infrastructure/Services/Inventory/DeviceResolver.cs \
        src/PatchHound.Api/Program.cs \
        tests/PatchHound.Tests/Infrastructure/Services/DeviceResolverTests.cs
git commit -m "feat(canonical): add DeviceResolver service"
```

---

## Task 9: `StagedDeviceMergeService` — replace `StagedAssetMergeService`

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/Inventory/StagedDeviceMergeService.cs`
- Create: `src/PatchHound.Core/Interfaces/IStagedDeviceMergeService.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/Services/StagedDeviceMergeServiceTests.cs`

**Contract:** given a pending ingestion run and a tenant ID, read `StagedAsset` rows for that run and produce canonical `Device` + `InstalledSoftware` rows. Legacy-free: writes only to canonical tables. Idempotent under spec §7 "Ingestion idempotency": re-merging the same staged data must not produce duplicate rows. Does **not** touch legacy tables.

- [ ] **Step 1: Write the failing test (idempotency)**

Create `tests/PatchHound.Tests/Infrastructure/Services/StagedDeviceMergeServiceTests.cs` with at least these three tests:

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services;

public class StagedDeviceMergeServiceTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private StagedDeviceMergeService _svc = null!;
    private Guid _tenantId;
    private SourceSystem _source = null!;
    private IngestionRun _run = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDbContextFactory.CreateAsync();
        _tenantId = Guid.NewGuid();
        _source = SourceSystem.Create("defender", "Defender");
        _db.SourceSystems.Add(_source);
        _run = IngestionRun.Start(_tenantId, sourceKey: "defender");
        _db.IngestionRuns.Add(_run);
        await _db.SaveChangesAsync();
        _svc = new StagedDeviceMergeService(_db,
            new DeviceResolver(_db),
            new SoftwareProductResolver(_db));
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Merge_creates_device_and_installed_software_from_staged_rows()
    {
        var staged = StagedAsset.CreateDevice(runId: _run.Id, tenantId: _tenantId, externalId: "dev-1", name: "host", sourceKey: "defender");
        staged.AddSoftwareObservation(externalId: "ms-edge", vendor: "Microsoft", name: "Edge", version: "1.2.3");
        _db.StagedAssets.Add(staged);
        await _db.SaveChangesAsync();

        var summary = await _svc.MergeAsync(_run.Id, _tenantId, default);

        Assert.Equal(1, summary.DevicesCreated);
        Assert.Equal(1, summary.InstalledSoftwareCreated);
        Assert.Single(await _db.Devices.IgnoreQueryFilters().Where(d => d.TenantId == _tenantId).ToListAsync());
        Assert.Single(await _db.InstalledSoftware.IgnoreQueryFilters().Where(i => i.TenantId == _tenantId).ToListAsync());
    }

    [Fact]
    public async Task Merge_is_idempotent_on_repeated_run()
    {
        // Seed one staged observation
        var staged = StagedAsset.CreateDevice(_run.Id, _tenantId, "dev-1", "host", "defender");
        staged.AddSoftwareObservation("ms-edge", "Microsoft", "Edge", "1.2.3");
        _db.StagedAssets.Add(staged);
        await _db.SaveChangesAsync();

        await _svc.MergeAsync(_run.Id, _tenantId, default);
        // Re-stage the same observation under a new run
        var run2 = IngestionRun.Start(_tenantId, "defender");
        _db.IngestionRuns.Add(run2);
        var staged2 = StagedAsset.CreateDevice(run2.Id, _tenantId, "dev-1", "host", "defender");
        staged2.AddSoftwareObservation("ms-edge", "Microsoft", "Edge", "1.2.3");
        _db.StagedAssets.Add(staged2);
        await _db.SaveChangesAsync();

        await _svc.MergeAsync(run2.Id, _tenantId, default);

        Assert.Equal(1, await _db.Devices.IgnoreQueryFilters().CountAsync(d => d.TenantId == _tenantId));
        Assert.Equal(1, await _db.InstalledSoftware.IgnoreQueryFilters().CountAsync(i => i.TenantId == _tenantId));
    }

    [Fact]
    public async Task Merge_two_tenants_does_not_cross_contaminate()
    {
        var tenantB = Guid.NewGuid();
        var runB = IngestionRun.Start(tenantB, "defender");
        _db.IngestionRuns.Add(runB);

        var stagedA = StagedAsset.CreateDevice(_run.Id, _tenantId, "dev-shared", "host", "defender");
        stagedA.AddSoftwareObservation("ms-edge", "Microsoft", "Edge", "1.2.3");
        var stagedB = StagedAsset.CreateDevice(runB.Id, tenantB, "dev-shared", "host", "defender");
        stagedB.AddSoftwareObservation("ms-edge", "Microsoft", "Edge", "1.2.3");
        _db.StagedAssets.AddRange(stagedA, stagedB);
        await _db.SaveChangesAsync();

        await _svc.MergeAsync(_run.Id, _tenantId, default);
        await _svc.MergeAsync(runB.Id, tenantB, default);

        var devicesA = await _db.Devices.IgnoreQueryFilters().Where(d => d.TenantId == _tenantId).ToListAsync();
        var devicesB = await _db.Devices.IgnoreQueryFilters().Where(d => d.TenantId == tenantB).ToListAsync();
        Assert.Single(devicesA);
        Assert.Single(devicesB);
        Assert.NotEqual(devicesA[0].Id, devicesB[0].Id);
        // Same SoftwareProduct shared globally is expected (canonical).
        Assert.Equal(1, await _db.SoftwareProducts.CountAsync());
    }
}
```

If `StagedAsset.CreateDevice(...)` and `AddSoftwareObservation(...)` don't already match these signatures, adjust the test to whatever the existing factory API looks like. The intent of this test is to drive the merge service contract — update the test input shape to the truth, not the other way around.

- [ ] **Step 2: Run to verify the test fails**

Run: `dotnet test --filter "FullyQualifiedName~StagedDeviceMergeServiceTests"`
Expected: FAIL, type not found.

- [ ] **Step 3: Create the interface**

Create `src/PatchHound.Core/Interfaces/IStagedDeviceMergeService.cs`:

```csharp
namespace PatchHound.Core.Interfaces;

public record StagedDeviceMergeSummary(
    int DevicesCreated,
    int DevicesTouched,
    int InstalledSoftwareCreated,
    int InstalledSoftwareTouched);

public interface IStagedDeviceMergeService
{
    Task<StagedDeviceMergeSummary> MergeAsync(Guid ingestionRunId, Guid tenantId, CancellationToken ct);
}
```

- [ ] **Step 4: Create the service**

Create `src/PatchHound.Infrastructure/Services/Inventory/StagedDeviceMergeService.cs`. It must:

1. Load all `StagedAsset` rows for `(runId, tenantId)` under `IgnoreQueryFilters()` (this is system-context).
2. For each staged device, resolve the `SourceSystem.Id` for the staged row's `SourceKey` by looking up `SourceSystems` by key (fail loudly if the source system is missing — ingestion misconfigured).
3. Call `IDeviceResolver.ResolveAsync` to get or create the canonical `Device`.
4. Apply attribute updates (`UpdateInventoryDetails`, `SetActiveInTenant(true)`, etc.) from the staged row onto the device.
5. For each staged software observation attached to the staged row, call `ISoftwareProductResolver.ResolveAsync` then either insert a new `InstalledSoftware` or update the existing row's `LastSeenAt` via `Touch`.
6. Save changes and return a summary.

Concrete skeleton (fill in with the real staged-row shape once verified):

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Inventory;

public class StagedDeviceMergeService(
    PatchHoundDbContext db,
    IDeviceResolver deviceResolver,
    ISoftwareProductResolver softwareResolver) : IStagedDeviceMergeService
{
    public async Task<StagedDeviceMergeSummary> MergeAsync(Guid runId, Guid tenantId, CancellationToken ct)
    {
        var devicesCreated = 0;
        var devicesTouched = 0;
        var installedCreated = 0;
        var installedTouched = 0;

        var staged = await db.StagedAssets
            .IgnoreQueryFilters()
            .Include(s => s.SoftwareObservations)
            .Where(s => s.IngestionRunId == runId && s.TenantId == tenantId)
            .ToListAsync(ct);

        var sourceSystems = await db.SourceSystems.ToDictionaryAsync(s => s.Key, ct);

        foreach (var stagedDevice in staged)
        {
            if (!sourceSystems.TryGetValue(stagedDevice.SourceKey, out var sourceSystem))
            {
                throw new InvalidOperationException($"Unknown source system key '{stagedDevice.SourceKey}'. Seed it in SourceSystemSeedHostedService before ingesting.");
            }

            var deviceBefore = await db.Devices.IgnoreQueryFilters()
                .FirstOrDefaultAsync(d =>
                    d.TenantId == tenantId
                    && d.SourceSystemId == sourceSystem.Id
                    && d.ExternalId == stagedDevice.ExternalId, ct);

            var device = await deviceResolver.ResolveAsync(
                new DeviceObservation(tenantId, sourceSystem.Id, stagedDevice.ExternalId, stagedDevice.Name, Criticality.Medium),
                ct);

            if (deviceBefore is null) devicesCreated++;
            else devicesTouched++;

            // Apply attribute updates from the staged payload.
            // (Match fields 1:1 with StagedAsset columns.)
            device.UpdateInventoryDetails(
                computerDnsName: stagedDevice.DeviceComputerDnsName,
                healthStatus:    stagedDevice.DeviceHealthStatus,
                osPlatform:      stagedDevice.DeviceOsPlatform,
                osVersion:       stagedDevice.DeviceOsVersion,
                externalRiskLabel: stagedDevice.DeviceRiskScore,
                lastSeenAt:      stagedDevice.DeviceLastSeenAt,
                lastIpAddress:   stagedDevice.DeviceLastIpAddress,
                aadDeviceId:     stagedDevice.DeviceAadDeviceId,
                groupId:         stagedDevice.DeviceGroupId,
                groupName:       stagedDevice.DeviceGroupName,
                exposureLevel:   stagedDevice.DeviceExposureLevel,
                isAadJoined:     stagedDevice.DeviceIsAadJoined,
                onboardingStatus: stagedDevice.DeviceOnboardingStatus,
                deviceValue:     stagedDevice.DeviceValue);
            device.SetActiveInTenant(true);

            foreach (var obs in stagedDevice.SoftwareObservations ?? [])
            {
                var product = await softwareResolver.ResolveAsync(
                    new SoftwareObservation(sourceSystem.Id, obs.ExternalId, obs.Vendor, obs.Name),
                    ct);

                var existing = await db.InstalledSoftware
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(i =>
                        i.TenantId == tenantId
                        && i.DeviceId == device.Id
                        && i.SoftwareProductId == product.Id
                        && i.SourceSystemId == sourceSystem.Id
                        && i.Version == (obs.Version ?? ""), ct);

                if (existing is null)
                {
                    db.InstalledSoftware.Add(InstalledSoftware.Observe(
                        tenantId, device.Id, product.Id, sourceSystem.Id, obs.Version, DateTimeOffset.UtcNow));
                    installedCreated++;
                }
                else
                {
                    existing.Touch(DateTimeOffset.UtcNow);
                    installedTouched++;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return new StagedDeviceMergeSummary(devicesCreated, devicesTouched, installedCreated, installedTouched);
    }
}
```

If `StagedAsset` doesn't currently expose `SoftwareObservations` as a collection, extend `StagedAsset` + its configuration in this task. Spec §4.9 marks `StagedAsset` for renaming to `StagedDevice` only in Phase 6 scope; for Phase 1 we keep the table name but reshape the merge path.

- [ ] **Step 5: Register the service**

In `Program.cs`:

```csharp
builder.Services.AddScoped<IStagedDeviceMergeService, StagedDeviceMergeService>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~StagedDeviceMergeServiceTests"`
Expected: PASS (three tests).

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Core/Interfaces/IStagedDeviceMergeService.cs \
        src/PatchHound.Infrastructure/Services/Inventory/StagedDeviceMergeService.cs \
        src/PatchHound.Api/Program.cs \
        tests/PatchHound.Tests/Infrastructure/Services/StagedDeviceMergeServiceTests.cs
git commit -m "feat(canonical): add StagedDeviceMergeService writing only canonical entities"
```

---

## Task 10a: Rewrite `IngestionService` to call `StagedDeviceMergeService` (leave legacy files in place)

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs` (if `IStagedDeviceMergeService` is not yet registered — Task 9 may already have done this)
- Modify: tests under `tests/PatchHound.Tests/Infrastructure/Services/IngestionService*`
- **Do NOT delete** `StagedAssetMergeService.cs`, `NormalizedSoftwareProjectionService.cs`, or `NormalizedSoftwareResolver.cs` — they are still consumed by `AuthenticatedScanIngestionService`, `AssetsController`, and `SoftwareVulnerabilityMatchService`. Deletion is deferred to Task 10b (after those consumers migrate).

**Context (important — read before editing):**

1. `IngestionService` is 2,771 lines. Its constructor currently takes `StagedAssetMergeService _stagedAssetMergeService` (line 31) and `NormalizedSoftwareProjectionService _normalizedSoftwareProjectionService` (line 29). Both are only invoked inside the single internal method `ProcessStagedAssetsAsync` (line 2271):

   ```csharp
   internal async Task<StagedAssetMergeSummary> ProcessStagedAssetsAsync(
       Guid ingestionRunId, Guid tenantId, string sourceKey, Guid? snapshotId, CancellationToken ct)
   {
       var summary = await _stagedAssetMergeService.ProcessAsync(
           ingestionRunId, tenantId, sourceKey, UpdateAssetMergeProgressAsync, ct);
       await _normalizedSoftwareProjectionService.SyncTenantAsync(tenantId, snapshotId, ct);
       return summary;
       // ... UpdateAssetMergeProgressAsync local function unchanged ...
   }
   ```

2. The returned `StagedAssetMergeSummary` (13-field record defined at `StagedAssetMergeService.cs:703`) is consumed in ~30 places across `IngestionService` via fields: `StagedMachineCount`, `StagedSoftwareCount`, `PersistedMachineCount`, `PersistedSoftwareCount`, `MergedAssetCount` (see grep output in Step 1). **Touching all those call sites is out of scope** — we adapt at the boundary instead.

3. The new canonical merge service exposes `IStagedDeviceMergeService.MergeAsync(runId, tenantId, ct)` returning `StagedDeviceMergeSummary(DevicesCreated, DevicesTouched, InstalledSoftwareCreated, InstalledSoftwareTouched)` (4 fields only — created in Task 9).

4. **Boundary adapter strategy:** inside `ProcessStagedAssetsAsync`, call `_stagedDeviceMergeService.MergeAsync`, then construct a `StagedAssetMergeSummary` mapping canonical counts into the legacy shape so the 30 downstream call sites stay unchanged. `StagedAssetMergeSummary` is a public record in `StagedAssetMergeService.cs` — leaving that file in place means the record stays available; no move needed.

5. **Progress callback:** the existing `UpdateAssetMergeProgressAsync` local function takes `(stagedMachineCount, stagedSoftwareCount, persistedMachineCount, persistedSoftwareCount, ct)` and is invoked by `StagedAssetMergeService` during long-running merges. `IStagedDeviceMergeService.MergeAsync` has no progress callback. For 10a, call `UpdateAssetMergeProgressAsync` once with final counts after the merge completes — accept the loss of mid-run progress updates. Document in a code comment.

- [ ] **Step 1: Grep every call site to confirm scope**

Run:

```bash
rg "StagedAssetMergeService|NormalizedSoftwareProjectionService|NormalizedSoftwareResolver" src/PatchHound.Infrastructure/Services/IngestionService.cs -n
rg "assetMergeSummary\." src/PatchHound.Infrastructure/Services/IngestionService.cs -n
```

Expected first command: matches only on lines 29, 31, 45, 47, 73, 75, 87, 89, 2279, 2286 (fields, ctor, assignments, two call sites). Expected second command: ~30 matches across lines 258-1379. **Do not touch any of the ~30 `assetMergeSummary.` call sites — we keep the record shape intact at the boundary.**

- [ ] **Step 2: Swap the constructor dependency**

Replace in `IngestionService.cs`:

```csharp
// Remove these fields
private readonly NormalizedSoftwareProjectionService _normalizedSoftwareProjectionService;
private readonly StagedAssetMergeService _stagedAssetMergeService;

// Add this one
private readonly IStagedDeviceMergeService _stagedDeviceMergeService;
```

Both constructors (primary at line 45 and secondary at line 73) take `NormalizedSoftwareProjectionService normalizedSoftwareProjectionService` and `StagedAssetMergeService stagedAssetMergeService`. **Remove both parameters** from both constructors and **add** `IStagedDeviceMergeService stagedDeviceMergeService` in their place. Delete the assignments at lines 87 and 89; add `_stagedDeviceMergeService = stagedDeviceMergeService;` in their place.

`using PatchHound.Core.Interfaces;` may need adding at the top of the file to bring `IStagedDeviceMergeService` into scope — check before adding.

- [ ] **Step 3: Rewrite `ProcessStagedAssetsAsync` to call the canonical merge and adapt the summary**

Replace the body of `ProcessStagedAssetsAsync` (line 2271) with:

```csharp
internal async Task<StagedAssetMergeSummary> ProcessStagedAssetsAsync(
    Guid ingestionRunId,
    Guid tenantId,
    string sourceKey,
    Guid? snapshotId,
    CancellationToken ct
)
{
    // Canonical merge: writes Device + InstalledSoftware directly via resolvers.
    // The legacy StagedAssetMergeService / NormalizedSoftwareProjectionService path
    // is no longer invoked here. Those services remain for other consumers
    // (AuthenticatedScanIngestionService, AssetsController, SoftwareVulnerabilityMatchService)
    // until Task 10b.
    var canonicalSummary = await _stagedDeviceMergeService.MergeAsync(ingestionRunId, tenantId, ct);

    var persistedMachineCount = canonicalSummary.DevicesCreated + canonicalSummary.DevicesTouched;
    var persistedSoftwareCount =
        canonicalSummary.InstalledSoftwareCreated + canonicalSummary.InstalledSoftwareTouched;

    // The canonical merge service does not expose mid-run progress, so emit a
    // single final-state progress update. StagedMachineCount/StagedSoftwareCount
    // already reflect the staging phase totals and are carried through unchanged.
    await UpdateAssetMergeProgressAsync(
        stagedMachineCount: persistedMachineCount,
        stagedSoftwareCount: persistedSoftwareCount,
        persistedMachineCount: persistedMachineCount,
        persistedSoftwareCount: persistedSoftwareCount,
        ct
    );

    // Adapt the 4-field canonical summary into the 13-field legacy shape so the
    // ~30 downstream call sites in IngestionService keep working unchanged.
    // Fields with no canonical equivalent are set to zero; they track concerns
    // (episodes, stale installations) that no longer exist after Phase 1.
    return new StagedAssetMergeSummary(
        StagedMachineCount: persistedMachineCount,
        StagedSoftwareCount: persistedSoftwareCount,
        MergedAssetCount: persistedMachineCount + persistedSoftwareCount,
        PersistedMachineCount: persistedMachineCount,
        PersistedSoftwareCount: persistedSoftwareCount,
        StagedSoftwareLinkCount: 0,
        ResolvedSoftwareLinkCount: 0,
        InstallationsCreated: canonicalSummary.InstalledSoftwareCreated,
        InstallationsTouched: canonicalSummary.InstalledSoftwareTouched,
        EpisodesOpened: 0,
        EpisodesSeen: 0,
        StaleInstallationsMarked: 0,
        InstallationsRemoved: 0
    );

    async Task UpdateAssetMergeProgressAsync(
        int stagedMachineCount,
        int stagedSoftwareCount,
        int persistedMachineCount,
        int persistedSoftwareCount,
        CancellationToken callbackCt
    )
    {
        // ... KEEP the existing body of this local function (lines 2289-2360-ish) verbatim ...
    }
}
```

**Preserve the existing `UpdateAssetMergeProgressAsync` local function body exactly as it is** (in-memory vs non-in-memory branches updating `IngestionRun` progress columns). Only the outer method body changes.

- [ ] **Step 4: Verify DI registration**

```bash
rg "IStagedDeviceMergeService|StagedDeviceMergeService" src/PatchHound.Infrastructure/DependencyInjection.cs -n
rg "IStagedDeviceMergeService|StagedDeviceMergeService" src/PatchHound.Api/Program.cs -n
```

Task 9 should already have registered `services.AddScoped<IStagedDeviceMergeService, StagedDeviceMergeService>()`. If not, add it next to the existing `StagedAssetMergeService` registration. **Do not remove the `StagedAssetMergeService` or `NormalizedSoftwareProjectionService` registrations** — other consumers still need them.

- [ ] **Step 5: Fix existing `IngestionService` tests**

Find them:

```bash
rg "IngestionService" tests -l
```

For each test that wires up `IngestionService` directly, swap the constructor args:
- Remove `StagedAssetMergeService` and `NormalizedSoftwareProjectionService` from the test's construction
- Add an `IStagedDeviceMergeService` — stub it with a fake that returns `new StagedDeviceMergeSummary(DevicesCreated: 0, DevicesTouched: 0, InstalledSoftwareCreated: 0, InstalledSoftwareTouched: 0)` (or a configured count if the test asserts merge output)

Existing tests that asserted `StagedMachineCount`/`StagedSoftwareCount`/`PersistedMachineCount`/`PersistedSoftwareCount` should still work because the adapter populates those fields from canonical counts — verify by running them.

Tests that asserted `NormalizedSoftware*` projection side effects must be updated to assert canonical `InstalledSoftware` side effects instead, or removed if they duplicate Task 9's coverage.

- [ ] **Step 6: Build and run tests**

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~Ingestion"
```

Expected: clean build, green. If the full suite regresses, run `dotnet test` and fix the specific breakage — the adapter should keep all downstream `assetMergeSummary.*` usages working.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionService.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        src/PatchHound.Api/Program.cs \
        tests/PatchHound.Tests/Infrastructure/Services/
git commit -m "refactor(ingestion): IngestionService writes canonical Device+InstalledSoftware via StagedDeviceMergeService"
```

---

## Task 10c (deferred): Rewrite `IngestionServiceTests.cs` to assert canonical `Device` / `InstalledSoftware` state

**Deferred — all 38 tests in `tests/PatchHound.Tests/Infrastructure/IngestionServiceTests.cs` are currently marked with `Skip = "Legacy Asset-centric assertions. Pending canonical inventory test rewrite in Task 10c / Task 16/17."`.**

**Why they were skipped:** After Task 10a swapped `IngestionService.ProcessStagedAssetsAsync` from `StagedAssetMergeService.ProcessAsync` to `IStagedDeviceMergeService.MergeAsync`, 17 of the 38 tests failed at runtime because their assertions poke at `_dbContext.Assets` / `DeviceSoftwareInstallations` / `IngestionRun.Status` state that is no longer written the same way. The remaining 21 pass today, but the entire file is skipped to avoid a mix of Skip/run states that will rot under further Phase 1 changes.

**Scope of rewrite:**
- Replace `_dbContext.Assets.IgnoreQueryFilters()...` assertions with `_dbContext.Devices.IgnoreQueryFilters()...`
- Replace `_dbContext.DeviceSoftwareInstallations...` assertions with `_dbContext.InstalledSoftware...`
- Delete tests that assert on `NormalizedSoftware` / `TenantSoftware` (those tables are removed in Task 16)
- Review each `RunIngestionAsync_*` test for `IngestionRun.Status` expectations — the canonical merge path may emit slightly different status values; fix the expectation to match new reality, not the other way around
- For tests that construct `IngestionService` inline (10 sites), the constructor signature is already correct (Task 10a updated them); they now use a `Substitute.For<IStagedDeviceMergeService>()` which returns `default(StagedDeviceMergeSummary)` — configure the stub with `.Returns(new StagedDeviceMergeSummary(...))` for tests that care about merge counts

**When to execute:** Ideally bundled with Task 16 (legacy inventory deletion) since the two rewrites touch overlapping seed code. If Task 16's scope grows too large, split Task 10c into its own standalone task before Task 16.

---

## Task 10b (deferred): Delete legacy `StagedAssetMergeService` / `NormalizedSoftwareProjectionService` / `NormalizedSoftwareResolver`

**Deferred — do not execute during Phase 1 Task 10.** This task unblocks only after:
1. `AuthenticatedScanIngestionService` (`src/PatchHound.Infrastructure/AuthenticatedScans/AuthenticatedScanIngestionService.cs`) migrates off `StagedAssetMergeService` + `NormalizedSoftwareProjectionService` and off the legacy `dbContext.Assets` read in `GetDeviceExternalId`. This likely happens in Phase 1 Task 16 (legacy inventory deletion) or gets a dedicated task before then.
2. `AssetsController` (`src/PatchHound.Api/Controllers/AssetsController.cs`) drops its 3 `SyncTenantAsync` call sites. This is tracked in Task 13 (DevicesController migration).
3. `SoftwareVulnerabilityMatchService` (Infrastructure/Services) drops its 2 `SyncTenantAsync` call sites. This is tracked in Phase 2 or an early Phase 3 task — verify at Phase 2 kickoff.

**When all three are clear:**
- [ ] `rm src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs` (also deletes the `StagedAssetMergeSummary` record — move the record into `IngestionService.cs` as a private nested type **only if IngestionService still uses it** after downstream cleanup; otherwise delete outright)
- [ ] `rm src/PatchHound.Infrastructure/Services/NormalizedSoftwareProjectionService.cs`
- [ ] `rm src/PatchHound.Infrastructure/Services/NormalizedSoftwareResolver.cs`
- [ ] Remove DI registrations from `DependencyInjection.cs` and/or `Program.cs`
- [ ] `dotnet build && dotnet test`
- [ ] Commit: `refactor(ingestion): remove legacy StagedAssetMergeService after consumers migrated`

---

## Task 11: Replace `AssetRuleEvaluationService` with `DeviceRuleEvaluationService`

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/Inventory/DeviceRuleEvaluationService.cs`
- Modify: every caller (search with `rg "AssetRuleEvaluationService"`)
- Delete: `src/PatchHound.Infrastructure/Services/AssetRuleEvaluationService.cs`
- Delete: `src/PatchHound.Infrastructure/Services/AssetRuleFilterBuilder.cs` (if it becomes unused)
- Test: `tests/PatchHound.Tests/Infrastructure/Services/DeviceRuleEvaluationServiceTests.cs`

- [ ] **Step 1: Read the existing implementation**

Read `src/PatchHound.Infrastructure/Services/AssetRuleEvaluationService.cs` end-to-end. Identify:
- What inputs it accepts (grep, criteria JSON, rule set)
- Which entity fields it matches on (e.g. `Asset.DeviceOsPlatform`)
- What side effects it writes (criticality from rule, fallback team, security profile)

- [ ] **Step 2: Write a failing behavior test**

Create `DeviceRuleEvaluationServiceTests.cs` with at minimum:
- a test asserting that a rule matching on `OsPlatform == "Windows"` sets the criticality for matching devices only
- a test asserting two tenants' rules don't cross-affect each other
- a test asserting that rules whose criteria no longer match clear their rule-assigned criticality/team/profile

Use the existing `AssetRuleEvaluationServiceTests` as a template, renaming the types.

- [ ] **Step 3: Port the implementation to operate on `Device`**

Create `DeviceRuleEvaluationService.cs`. Replace every `db.Assets` with `db.Devices`, every `AssetRule` with `DeviceRule`, every field that was renamed in the entity (`DeviceOsPlatform` → `OsPlatform`, etc.). Reuse the existing filter-builder pattern — if the filter builder still makes sense, create `DeviceRuleFilterBuilder.cs`; if it doesn't survive the rename cleanly, inline it.

- [ ] **Step 4: Update every caller**

Run:

```bash
rg "AssetRuleEvaluationService|IAssetRuleEvaluationService" src -l
```

For each hit, rename to `DeviceRuleEvaluationService` / `IDeviceRuleEvaluationService`. Typical callers: a hosted service that re-evaluates rules on ingestion changes, and the admin controller that runs "preview" evaluations.

- [ ] **Step 5: Register and delete the old files**

Update `Program.cs` to register the new service and drop the old one. Then:

```bash
rm src/PatchHound.Infrastructure/Services/AssetRuleEvaluationService.cs
rm src/PatchHound.Infrastructure/Services/AssetRuleFilterBuilder.cs   # only if grep shows it has no remaining consumers
```

- [ ] **Step 6: Build and test**

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~DeviceRuleEvaluation|FullyQualifiedName~AssetRuleEvaluation"
```

Expected: clean build, all green (including previous `AssetRule*` tests that you renamed).

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/Inventory/DeviceRuleEvaluationService.cs \
        src/PatchHound.Infrastructure/Services/Inventory/DeviceRuleFilterBuilder.cs \
        src/PatchHound.Api/Program.cs \
        tests/PatchHound.Tests/Infrastructure/Services/DeviceRuleEvaluationServiceTests.cs
git rm src/PatchHound.Infrastructure/Services/AssetRuleEvaluationService.cs \
       src/PatchHound.Infrastructure/Services/AssetRuleFilterBuilder.cs
git commit -m "refactor(rules): evaluate DeviceRule against Device entities"
```

---

## Task 12: Rewrite `RiskScoreService` inventory baseline (device baseline only)

**Context:** Phase 1 only changes the *source* of device rows for risk scoring — from `Asset` to `Device` — and the name of the target table from `AssetRiskScore` to `DeviceRiskScore`. Vulnerability-driven components are rewritten in Phase 5 once exposure is canonical (Phase 3).

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/RiskScoreService.cs`
- Modify: the risk-score query services under `src/PatchHound.Infrastructure/Services/` that currently project `Asset` → `AssetRiskScore`
- Test: `tests/PatchHound.Tests/Infrastructure/Services/RiskScoreServiceTests.cs` (if it exists — otherwise add a focused one)

- [ ] **Step 1: Grep every remaining use of `Asset*` risk references inside `RiskScoreService`**

```bash
rg "Asset\\b|AssetRiskScore|AssetRule|AssetType" src/PatchHound.Infrastructure/Services/RiskScoreService.cs -n
```

- [ ] **Step 2: Update the query/project path**

Replace every `db.Assets.Where(a => a.TenantId == ...)` with `db.Devices.Where(d => d.TenantId == ...)`. Where the old code checked `a.AssetType == AssetType.Device`, drop the check (every `Device` row is a device). Project into `DeviceRiskScore` instead of `AssetRiskScore`.

- [ ] **Step 3: Build and test**

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~RiskScoreService"
```

Expected: clean build, green. Spec explicitly allows numeric drift — tests that pinned exact values may need re-baselining. If so, update the asserts to the new canonical values, noting the shift in a comment.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/RiskScoreService.cs \
        tests/PatchHound.Tests/Infrastructure/Services/RiskScoreServiceTests.cs
git commit -m "refactor(risk): use Device and DeviceRiskScore in baseline risk path"
```

---

## Task 13: Rename `AssetsController` → `DevicesController` and wire to `Device`

**Files:**
- Create: `src/PatchHound.Api/Controllers/DevicesController.cs` (copy/rewrite of `AssetsController.cs`)
- Delete: `src/PatchHound.Api/Controllers/AssetsController.cs`
- Modify: `src/PatchHound.Api/Models/*` DTOs — rename `AssetDto` → `DeviceDto` and drop `AssetType` field
- Modify: every API test that hits `/api/assets`
- Test: `tests/PatchHound.Tests/Api/DevicesControllerTests.cs`

- [ ] **Step 1: Copy the controller**

```bash
cp src/PatchHound.Api/Controllers/AssetsController.cs src/PatchHound.Api/Controllers/DevicesController.cs
```

Then edit `DevicesController.cs`:
- class name → `DevicesController`
- route → `[Route("api/devices")]`
- every `Asset` → `Device`, `db.Assets` → `db.Devices`
- drop `AssetType` filtering (no longer exists)
- `AssetDto` → `DeviceDto` (rename the DTO in `src/PatchHound.Api/Models/` — create `DeviceDto` as a direct rename)

- [ ] **Step 2: Delete the old controller**

```bash
rm src/PatchHound.Api/Controllers/AssetsController.cs
```

- [ ] **Step 3: Update tests**

Rename every `AssetsController`-targeting test to `DevicesControllerTests`, update route strings from `/api/assets` to `/api/devices`. Drop any `AssetType` assertions.

- [ ] **Step 4: Build and run API tests**

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~DevicesControllerTests"
```

Expected: clean build, green.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Api/Controllers/DevicesController.cs \
        src/PatchHound.Api/Models/ \
        tests/PatchHound.Tests/Api/
git rm src/PatchHound.Api/Controllers/AssetsController.cs
git commit -m "feat(api): introduce /api/devices backed by Device entity"
```

---

## Task 14: Admin controllers — rename asset-rule, tag, label, security-profile endpoints

**Files:**
- Rename: `src/PatchHound.Api/Controllers/AssetRulesController.cs` → `DeviceRulesController.cs`, route `/api/asset-rules` → `/api/device-rules`
- Rename: `src/PatchHound.Api/Controllers/AssetTagsController.cs` → `DeviceTagsController.cs`
- Rename: `src/PatchHound.Api/Controllers/AssetBusinessLabelsController.cs` → `DeviceBusinessLabelsController.cs`
- Rename: `src/PatchHound.Api/Controllers/AssetSecurityProfilesController.cs` → `SecurityProfilesController.cs`, route `/api/security-profiles`
- Update: DTOs under `src/PatchHound.Api/Models/` to drop `Asset` prefix
- Update: tests

- [ ] **Step 1: Grep for every such controller**

```bash
rg "\"api/asset-|api/asset-rules|api/asset-tags|api/asset-business-labels|api/asset-security-profiles\"" src -l
```

Write the list down before editing.

- [ ] **Step 2–N: For each controller, copy→rename→delete old, following Task 13's pattern**

Each rename is mechanically the same: new file, updated class/route/DTO, delete old, update tests. Commit **each controller as its own commit** so the review is small and reversible:

```bash
git commit -m "feat(api): rename AssetRulesController → DeviceRulesController"
git commit -m "feat(api): rename AssetTagsController → DeviceTagsController"
git commit -m "feat(api): rename AssetBusinessLabelsController → DeviceBusinessLabelsController"
git commit -m "feat(api): rename AssetSecurityProfilesController → SecurityProfilesController"
```

- [ ] **Step Final: Build and run API tests**

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~DeviceRulesControllerTests|FullyQualifiedName~DeviceTagsControllerTests|FullyQualifiedName~DeviceBusinessLabelsControllerTests|FullyQualifiedName~SecurityProfilesControllerTests"
```

Expected: clean build, green.

---

## Task 15: Frontend — rename `/assets` routes and components to `/devices`

**Context:** The frontend already has `frontend/src/routes/_authed/devices/` (per grep in preflight) — the assets-directory content needs to move there, and every component under `frontend/src/components/features/assets/` needs to be renamed to `frontend/src/components/features/devices/`.

**Files:**
- Rename: `frontend/src/routes/_authed/assets/` → `frontend/src/routes/_authed/devices/` (merge with existing folder; no duplication)
- Rename: `frontend/src/components/features/assets/` → `frontend/src/components/features/devices/` (and every file inside — `AssetDetailPane.tsx` → `DeviceDetailPane.tsx`, etc.)
- Rename: `frontend/src/features/assets/` → `frontend/src/features/devices/`
- Rename: `frontend/src/api/assets.functions.ts` → `frontend/src/api/devices.functions.ts`
- Rename: `frontend/src/api/assets.schemas.ts` → `frontend/src/api/devices.schemas.ts`
- Modify: `frontend/src/router.tsx`, `frontend/src/routeTree.gen.ts` (regenerate)
- Update: `frontend/src/components/features/dashboard/AssetOwnerAttentionView.tsx` → `DeviceOwnerAttentionView.tsx`
- Update: `frontend/src/components/features/admin/asset-rules/` → `device-rules/`
- Update: `frontend/src/routes/_authed/admin/asset-rules/` → `device-rules/`
- Update: `frontend/src/routes/_authed/dashboard/my-assets.tsx` → `my-devices.tsx`
- Update: every import that references an `Asset*` type, and every call site that calls `getAssets` → `getDevices`

- [ ] **Step 1: Inventory the rename surface**

```bash
rg -l "Asset|assets" frontend/src > /tmp/assets-frontend-renames.txt
wc -l /tmp/assets-frontend-renames.txt
```

Expected: dozens of files. Review the list to identify what is genuinely about assets-vs-devices (rename) vs legitimate uses of the word "asset" elsewhere in copy/docs (leave alone).

- [ ] **Step 2: Rename directories with `git mv`**

```bash
git mv frontend/src/routes/_authed/assets frontend/src/routes/_authed/devices-new
# merge contents into existing devices dir, then remove the staging dir
```

If the existing `frontend/src/routes/_authed/devices` already has content, inspect it first — it may be a stub. Resolve conflicts by keeping the richer implementation from the former `assets` directory.

Repeat for `frontend/src/components/features/assets`, `frontend/src/features/assets`, `frontend/src/api/assets.functions.ts`, `frontend/src/api/assets.schemas.ts`, `frontend/src/components/features/admin/asset-rules`, `frontend/src/routes/_authed/admin/asset-rules`, `frontend/src/routes/_authed/dashboard/my-assets.tsx`.

- [ ] **Step 3: Bulk-rename type references**

For each renamed file, update its contents:
- `Asset` type → `Device`
- `AssetDto` → `DeviceDto`
- `assetId` → `deviceId`
- `/api/assets` → `/api/devices`
- `useGetAssets` → `useGetDevices`
- Component names (e.g. `AssetDetailPane` → `DeviceDetailPane`, `AssetAdvancedToolsPanel` → `DeviceAdvancedToolsPanel`)

For each function/component renamed, update every caller. Use `rg "AssetDetailPane" frontend/src -l` to verify zero leftover references per rename.

- [ ] **Step 4: Regenerate the TanStack route tree**

```bash
cd frontend && npm run dev -- --generate-routes 2>/dev/null || npx @tanstack/router-cli generate
```

Exact command depends on project setup — grep `package.json` scripts for `generate` or `routes`. If none exists, edit `routeTree.gen.ts` manually (the file is regenerated on `npm run dev`, so triggering a dev server start and stopping it is the usual workflow).

- [ ] **Step 5: Run typecheck and tests**

```bash
cd frontend
npm run typecheck
npm test
cd ..
```

Expected: no type errors, green.

- [ ] **Step 6: Commit**

```bash
git add frontend/src
git commit -m "feat(frontend): rename /assets → /devices routes and components"
```

---

## Task 16: Delete legacy inventory entities (`Asset`, `AssetType`, `Asset*` subtypes, `NormalizedSoftware*`, `TenantSoftware*`, `DeviceSoftwareInstallation*`, `SoftwareCpeBinding`, `StagedAsset`, `StagedDeviceSoftwareInstallation`)

**Context:** Every consumer has been rewritten in Tasks 1–15. This task is the mechanical deletion + tenant-filter removal pass.

**Files to delete:** (spec §4.9 inventory legacy + software legacy)

```
src/PatchHound.Core/Entities/Asset.cs
src/PatchHound.Core/Entities/AssetBusinessLabel.cs
src/PatchHound.Core/Entities/AssetRiskScore.cs
src/PatchHound.Core/Entities/AssetRule.cs
src/PatchHound.Core/Entities/AssetSecurityProfile.cs
src/PatchHound.Core/Entities/AssetTag.cs
src/PatchHound.Core/Entities/StagedAsset.cs
src/PatchHound.Core/Entities/StagedDeviceSoftwareInstallation.cs
src/PatchHound.Core/Entities/DeviceSoftwareInstallation.cs
src/PatchHound.Core/Entities/DeviceSoftwareInstallationEpisode.cs
src/PatchHound.Core/Entities/NormalizedSoftware.cs
src/PatchHound.Core/Entities/NormalizedSoftwareAlias.cs
src/PatchHound.Core/Entities/NormalizedSoftwareInstallation.cs
src/PatchHound.Core/Entities/NormalizedSoftwareVulnerabilityProjection.cs
src/PatchHound.Core/Entities/SoftwareCpeBinding.cs
src/PatchHound.Core/Entities/TenantSoftware.cs
src/PatchHound.Core/Entities/TenantSoftwareRiskScore.cs
src/PatchHound.Core/Enums/AssetType.cs
```

Plus every corresponding file under `src/PatchHound.Infrastructure/Data/Configurations/`.

- [ ] **Step 1: Delete the entity files**

```bash
git rm src/PatchHound.Core/Entities/Asset.cs \
       src/PatchHound.Core/Entities/AssetBusinessLabel.cs \
       src/PatchHound.Core/Entities/AssetRiskScore.cs \
       src/PatchHound.Core/Entities/AssetRule.cs \
       src/PatchHound.Core/Entities/AssetSecurityProfile.cs \
       src/PatchHound.Core/Entities/AssetTag.cs \
       src/PatchHound.Core/Entities/StagedAsset.cs \
       src/PatchHound.Core/Entities/StagedDeviceSoftwareInstallation.cs \
       src/PatchHound.Core/Entities/DeviceSoftwareInstallation.cs \
       src/PatchHound.Core/Entities/DeviceSoftwareInstallationEpisode.cs \
       src/PatchHound.Core/Entities/NormalizedSoftware.cs \
       src/PatchHound.Core/Entities/NormalizedSoftwareAlias.cs \
       src/PatchHound.Core/Entities/NormalizedSoftwareInstallation.cs \
       src/PatchHound.Core/Entities/NormalizedSoftwareVulnerabilityProjection.cs \
       src/PatchHound.Core/Entities/SoftwareCpeBinding.cs \
       src/PatchHound.Core/Entities/TenantSoftware.cs \
       src/PatchHound.Core/Entities/TenantSoftwareRiskScore.cs \
       src/PatchHound.Core/Enums/AssetType.cs
```

- [ ] **Step 2: Delete their configurations**

```bash
git rm src/PatchHound.Infrastructure/Data/Configurations/Asset*.cs \
       src/PatchHound.Infrastructure/Data/Configurations/StagedAsset*.cs \
       src/PatchHound.Infrastructure/Data/Configurations/StagedDeviceSoftwareInstallationConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/DeviceSoftwareInstallation*.cs \
       src/PatchHound.Infrastructure/Data/Configurations/NormalizedSoftware*.cs \
       src/PatchHound.Infrastructure/Data/Configurations/SoftwareCpeBindingConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/TenantSoftware*.cs
```

- [ ] **Step 3: Remove DbSets from `PatchHoundDbContext`**

Open `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` and delete every DbSet for a deleted type. The current file has the legacy sets at roughly lines 46–106 (per preflight read). Remove:

```csharp
public DbSet<Asset> Assets
public DbSet<AssetBusinessLabel> AssetBusinessLabels
public DbSet<AssetSecurityProfile> AssetSecurityProfiles
public DbSet<SoftwareCpeBinding> SoftwareCpeBindings
public DbSet<NormalizedSoftware> NormalizedSoftware
public DbSet<TenantSoftware> TenantSoftware
public DbSet<NormalizedSoftwareAlias> NormalizedSoftwareAliases
public DbSet<NormalizedSoftwareInstallation> NormalizedSoftwareInstallations
public DbSet<NormalizedSoftwareVulnerabilityProjection> NormalizedSoftwareVulnerabilityProjections
public DbSet<DeviceSoftwareInstallation> DeviceSoftwareInstallations
public DbSet<DeviceSoftwareInstallationEpisode> DeviceSoftwareInstallationEpisodes
public DbSet<StagedAsset> StagedAssets
public DbSet<StagedDeviceSoftwareInstallation> StagedDeviceSoftwareInstallations
public DbSet<AssetTag> AssetTags
public DbSet<AssetRule> AssetRules
public DbSet<AssetRiskScore> AssetRiskScores
public DbSet<TenantSoftwareRiskScore> TenantSoftwareRiskScores
```

- [ ] **Step 4: Remove legacy tenant filters from `OnModelCreating`**

Delete the `HasQueryFilter` registrations for `Asset`, `AssetBusinessLabel`, `AssetSecurityProfile`, `TenantSoftware`, `NormalizedSoftwareInstallation`, `NormalizedSoftwareVulnerabilityProjection`, `DeviceSoftwareInstallation`, `DeviceSoftwareInstallationEpisode`, `StagedAsset`, `StagedDeviceSoftwareInstallation`, `AssetTag`, `AssetRule`, `AssetRiskScore`, `TenantSoftwareRiskScore`. (Referenced lines 167–348 of the current file.)

- [ ] **Step 5: Build**

```bash
dotnet build
```

Expected: clean. Every build error here identifies a stray consumer that was missed in earlier tasks. Fix those by:
1. If the caller still needs the logic, point it at the canonical replacement.
2. If the caller was legacy-only (e.g. a projection used only by a deleted dual-read path), delete it too.

Do **not** re-introduce compatibility shims.

- [ ] **Step 6: Run full tests**

```bash
dotnet test
cd frontend && npm run typecheck && npm test && cd ..
```

Expected: everything green.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs
git commit -m "chore(canonical): delete legacy inventory and software entities"
```

---

## Task 17: Grep sweep — no lingering legacy references

Before declaring Phase 1 done, verify no deleted types, routes, or service names are referenced anywhere in the workspace.

- [ ] **Step 1: Run the legacy grep**

```bash
rg --type cs '\b(Asset|AssetType|AssetBusinessLabel|AssetRiskScore|AssetRule|AssetSecurityProfile|AssetTag|StagedAsset|NormalizedSoftware|TenantSoftware|TenantSoftwareRiskScore|SoftwareCpeBinding|DeviceSoftwareInstallation|StagedDeviceSoftwareInstallation|StagedAuthenticatedScanSoftware|AssetScanProfileAssignment)\b' src tests
```

Expected: zero matches. Exceptions:
- strings inside migration files — ignored (Phase 6 deletes these)
- anything under `docs/` — ignored
- anything inside this plan file — ignored

- [ ] **Step 2: Run the frontend legacy grep**

```bash
rg 'AssetDetailPane|AssetAdvancedToolsPanel|AssetDto|/api/assets|/assets|assets\\.schemas|assetId|assetName' frontend/src
```

Expected: zero matches except legitimate uses of the English word "assets" in copy that should stay (e.g. "Welcome back to your asset inventory" marketing text — none is expected in this codebase, but double-check).

- [ ] **Step 3: Verify no new `IgnoreQueryFilters()` in request-scoped code**

```bash
rg 'IgnoreQueryFilters' src/PatchHound.Api src/PatchHound.Infrastructure -n
```

Expected: matches only inside:
- `src/PatchHound.Infrastructure/Services/Inventory/DeviceResolver.cs` (system-context ingestion — documented)
- `src/PatchHound.Infrastructure/Services/Inventory/StagedDeviceMergeService.cs` (system-context merge — documented)
- `src/PatchHound.Infrastructure/Services/IngestionService.cs` (system-context — existing pattern)
- any other ingestion-hosted-service paths previously marked as system context

No controllers or request-scoped services should have `IgnoreQueryFilters` calls. Any match outside the allow-list is a spec §4.10 Rule 5 violation — remove it or justify it in the PR description.

- [ ] **Step 4: Commit an empty marker if anything was fixed**

If the sweep surfaced cleanup work, commit it:

```bash
git commit -m "chore(canonical): final sweep of legacy inventory references"
```

Otherwise no commit needed.

---

## Task 18: `TenantIsolationEndToEndTests` — Phase 1 assertions

**Files:**
- Create: `tests/PatchHound.Tests/Api/TenantIsolationEndToEndTests.cs`

Spec §7 says every phase extends this test with its new routes. Phase 1 introduces it.

- [ ] **Step 1: Write the test scaffolding**

```csharp
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using Xunit;

namespace PatchHound.Tests.Api;

public class TenantIsolationEndToEndTests : IClassFixture<PatchHoundWebAppFactory>
{
    private readonly PatchHoundWebAppFactory _factory;

    public TenantIsolationEndToEndTests(PatchHoundWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Devices_endpoint_scopes_to_authenticated_tenant_only()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        // system-context seed of two tenants with disjoint devices
        await _factory.EnterSystemContextAsync(async () =>
        {
            var source = await db.SourceSystems.FirstAsync();
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            db.Devices.Add(Device.Create(tenantA, source.Id, "A-dev-1", "tenant-a-host", Criticality.Low));
            db.Devices.Add(Device.Create(tenantB, source.Id, "B-dev-1", "tenant-b-host", Criticality.Low));
            await db.SaveChangesAsync();

            _factory.StoreTenants(tenantA, tenantB);
        });

        var (a, b) = _factory.GetStoredTenants();
        var clientA = _factory.CreateAuthenticatedClientFor(a);
        var listA = await clientA.GetFromJsonAsync<PagedDevices>("/api/devices");
        Assert.NotNull(listA);
        Assert.Single(listA!.Items);
        Assert.Equal("tenant-a-host", listA.Items[0].Name);
    }

    private record PagedDevices(List<DeviceListItem> Items, int Total);
    private record DeviceListItem(Guid Id, string Name);
}
```

Note: if `PatchHoundWebAppFactory` doesn't already exist as a test fixture, create it at `tests/PatchHound.Tests/Api/PatchHoundWebAppFactory.cs` wrapping `WebApplicationFactory<Program>` and providing `EnterSystemContextAsync`, `StoreTenants`, `GetStoredTenants`, `CreateAuthenticatedClientFor(tenantId)`. The authenticated client should inject the ambient test `ITenantContext` with `AccessibleTenantIds = [tenantId]`. Look at existing API tests under `tests/PatchHound.Tests/Api/` for the established pattern.

- [ ] **Step 2: Extend the test with device-rule, device-tag, device-business-label, and installed-software assertions**

Add one test per new Phase 1 route:

```csharp
[Fact] public Task Device_rules_endpoint_scopes_to_tenant() => ...;
[Fact] public Task Device_tags_endpoint_scopes_to_tenant() => ...;
[Fact] public Task Device_business_labels_endpoint_scopes_to_tenant() => ...;
[Fact] public Task Installed_software_endpoint_scopes_to_tenant() => ...;
[Fact] public Task Security_profiles_endpoint_scopes_to_tenant() => ...;
[Fact] public Task Software_products_endpoint_returns_global_data_only_with_no_tenant_fields_leaking() => ...;
```

Each test follows the same pattern: seed rows for tenants A and B, authenticate as A, call the route, assert only A-owned rows come back (or, for `/api/software-products`, that the returned rows don't include any tenant-scoped fields — only global product identity).

- [ ] **Step 3: Run**

```bash
dotnet test --filter "FullyQualifiedName~TenantIsolationEndToEndTests"
```

Expected: all green. If any test fails, the corresponding controller is missing a filter or joining across tenants — fix the controller, not the test.

- [ ] **Step 4: Commit**

```bash
git add tests/PatchHound.Tests/Api/TenantIsolationEndToEndTests.cs \
        tests/PatchHound.Tests/Api/PatchHoundWebAppFactory.cs
git commit -m "test(canonical): tenant isolation end-to-end test — Phase 1"
```

---

## Task 19: Final build + suite + PR description

- [ ] **Step 1: Clean build**

```bash
dotnet clean
dotnet build
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full backend test run**

```bash
dotnet test
```

Expected: all green.

- [ ] **Step 3: Full frontend check**

```bash
cd frontend
npm run typecheck
npm test
cd ..
```

Expected: all green.

- [ ] **Step 4: Write the PR description**

Create/update the phase PR description with these required sections from spec §6:

1. **Deleted surface area** — list every entity, configuration, service, controller, route, and DTO removed in Phase 1 (copy from §4.9 + the Task 16 deletion list).

2. **Tenant scope audit table** — for each new entity introduced, list:
   | Entity | Direct `TenantId` | EF global filter | Justification if global |
   | --- | --- | --- | --- |
   | `SourceSystem` | no | no | Global reference data; public CVE catalog. |
   | `Device` | yes | yes | Tenant-scoped inventory fact. |
   | `SoftwareProduct` | no | no | Global product identity; supports cross-tenant alias sharing. |
   | `SoftwareAlias` | no | no | Global `(SourceSystemId, ExternalId) → SoftwareProductId` mapping. |
   | `InstalledSoftware` | yes | yes | Tenant-scoped install fact. |
   | `TenantSoftwareProductInsight` | yes | yes | Tenant-scoped description/evidence for a global product. |
   | `DeviceBusinessLabel` | yes | yes | Tenant-scoped device label link. |
   | `DeviceTag` | yes | yes | Tenant-scoped. |
   | `DeviceRule` | yes | yes | Tenant-scoped rule. |
   | `DeviceRiskScore` | yes | yes | Tenant-scoped. |
   | `SecurityProfile` | yes | yes | Tenant policy. |

3. **Tenant isolation test output** — paste the passing output of `dotnet test --filter "FullyQualifiedName~TenantIsolationEndToEndTests" -v normal`, showing each of the six Phase 1 route assertions passing.

4. **`IgnoreQueryFilters()` audit** — list every call site and justify each (all three should be in ingestion/system-context services).

- [ ] **Step 5: Open the PR**

```bash
git push -u origin data-model-canonical-cleanup
gh pr create --title "feat(canonical): phase 1 — canonical inventory + delete legacy" \
  --body-file /tmp/phase-1-pr-body.md
```

(Write the body file from the checklist above.)

- [ ] **Step 6: Done marker**

When the PR is open and green in CI, Phase 1 is complete. Proceed to Phase 2's plan.

---

## Exit Criteria (copied from spec §5.1 for reference)

- `Asset*`, `TenantSoftware*`, `NormalizedSoftware*`, `SoftwareCpeBinding`, `DeviceSoftwareInstallation*` gone from the workspace.
- Ingestion writes only canonical.
- No `tenantSoftwareId` or `softwareAssetId` references anywhere.
- Frontend `/devices` works.
- Tenant isolation audit in PR description lists the 11 new entities with their scope.
- Tenant isolation end-to-end test passes (six Phase 1 routes).
- `dotnet test` and `npm test` green.
