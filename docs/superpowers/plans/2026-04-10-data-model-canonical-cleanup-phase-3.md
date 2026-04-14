# Data Model Canonical Cleanup — Phase 3: Canonical Exposure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce canonical tenant-scoped `DeviceVulnerabilityExposure`, `ExposureEpisode`, and `ExposureAssessment` entities; derive exposures from `InstalledSoftware` × `VulnerabilityApplicability`; compute environmental severity via `SecurityProfile`; fill in the vulnerability list/detail "affected devices" section left empty by Phase 2.

**Architecture:** Three tenant-scoped entities with direct `TenantId` columns and mandatory EF query filters (spec §4.10 Rules 1, 2). An `ExposureDerivationService` walks each tenant's `InstalledSoftware` rows, matches each installed product against `VulnerabilityApplicability` (product-keyed first, CPE-keyed fallback), and upserts `DeviceVulnerabilityExposure` rows. `ExposureEpisodeService` manages the open/reopen/resolve lifecycle per `(DeviceId, VulnerabilityId)`. `ExposureAssessmentService` computes environmental CVSS via `Device.SecurityProfileId → SecurityProfile` modifiers using the existing `EnvironmentalSeverityCalculator`. All writes run under tenant context (not system-context) because every row carries `TenantId`.

**Tech Stack:** .NET 10, EF Core 10, xUnit, PostgreSQL, React, TanStack Router, Vitest.

**Note on Phase 2 cleanup:** Phase 2 already deleted the legacy `VulnerabilityAsset*`/`VulnerabilityEpisodeRiskAssessment` chain. Phase 3 therefore introduces the canonical exposure entities from a clean slate — there are **no legacy deletions** in Phase 3.

---

## Preflight

- [ ] **Step P1: Verify Phase 2 merged**

Run: `git log --oneline main -20`
Expected: Phase 2 merge commit present.

- [ ] **Step P2: Create Phase 3 branch**

```bash
git checkout main
git pull origin main
git checkout -b data-model-canonical-cleanup-phase-3
```

- [ ] **Step P3: Drop + recreate dev database**

```bash
PGPASSWORD=patchhound psql -h localhost -U patchhound -d postgres -c "DROP DATABASE IF EXISTS patchhound;"
PGPASSWORD=patchhound psql -h localhost -U patchhound -d postgres -c "CREATE DATABASE patchhound;"
```

- [ ] **Step P4: Baseline green**

```bash
dotnet build && dotnet test
cd web && npm run typecheck && npm test -- --run && cd ..
```

---

## Task 1: Add `DeviceVulnerabilityExposure` tenant-scoped entity

**Files:**
- Create: `src/PatchHound.Core/Entities/DeviceVulnerabilityExposure.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/DeviceVulnerabilityExposureConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` (DbSet + query filter)
- Test: `tests/PatchHound.Core.Tests/Entities/DeviceVulnerabilityExposureTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Core.Tests.Entities;

public class DeviceVulnerabilityExposureTests
{
    [Fact]
    public void Observe_sets_identity_tenant_and_firstseen()
    {
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var vulnId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var installedId = Guid.NewGuid();

        var e = DeviceVulnerabilityExposure.Observe(
            tenantId, deviceId, vulnId, productId, installedId,
            matchedVersion: "1.2.3", matchSource: ExposureMatchSource.Product,
            observedAt: new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.NotEqual(Guid.Empty, e.Id);
        Assert.Equal(tenantId, e.TenantId);
        Assert.Equal(deviceId, e.DeviceId);
        Assert.Equal(vulnId, e.VulnerabilityId);
        Assert.Equal(productId, e.SoftwareProductId);
        Assert.Equal(installedId, e.InstalledSoftwareId);
        Assert.Equal("1.2.3", e.MatchedVersion);
        Assert.Equal(ExposureMatchSource.Product, e.MatchSource);
        Assert.Equal(ExposureStatus.Open, e.Status);
        Assert.Equal(new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero), e.FirstObservedAt);
        Assert.Equal(e.FirstObservedAt, e.LastObservedAt);
    }

    [Fact]
    public void Reobserve_bumps_LastObservedAt_but_not_FirstObservedAt()
    {
        var e = DeviceVulnerabilityExposure.Observe(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            "1.0", ExposureMatchSource.Cpe, new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        var first = e.FirstObservedAt;

        e.Reobserve(new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(first, e.FirstObservedAt);
        Assert.Equal(new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.Zero), e.LastObservedAt);
    }

    [Fact]
    public void Resolve_sets_status_resolved_and_ResolvedAt()
    {
        var e = DeviceVulnerabilityExposure.Observe(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            "1.0", ExposureMatchSource.Cpe, DateTimeOffset.UtcNow);

        e.Resolve(new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(ExposureStatus.Resolved, e.Status);
        Assert.Equal(new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero), e.ResolvedAt);
    }

    [Fact]
    public void Reopen_clears_ResolvedAt_and_flips_back_to_Open()
    {
        var e = DeviceVulnerabilityExposure.Observe(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            "1.0", ExposureMatchSource.Cpe, DateTimeOffset.UtcNow);
        e.Resolve(DateTimeOffset.UtcNow);

        e.Reopen(new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(ExposureStatus.Open, e.Status);
        Assert.Null(e.ResolvedAt);
        Assert.Equal(new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero), e.LastObservedAt);
    }
}
```

- [ ] **Step 2: Add enums**

In `src/PatchHound.Core/Enums/` add:

```csharp
namespace PatchHound.Core.Enums;

public enum ExposureStatus
{
    Open = 0,
    Resolved = 1,
}

public enum ExposureMatchSource
{
    Product = 0,  // Matched via VulnerabilityApplicability.SoftwareProductId
    Cpe = 1,      // Matched via VulnerabilityApplicability.CpeCriteria fallback
}
```

Put them in `ExposureStatus.cs` and `ExposureMatchSource.cs` files respectively.

- [ ] **Step 3: Create entity**

```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class DeviceVulnerabilityExposure
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public Guid? SoftwareProductId { get; private set; }
    public Guid InstalledSoftwareId { get; private set; }
    public string MatchedVersion { get; private set; } = null!;
    public ExposureMatchSource MatchSource { get; private set; }
    public ExposureStatus Status { get; private set; }
    public DateTimeOffset FirstObservedAt { get; private set; }
    public DateTimeOffset LastObservedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public Device Device { get; private set; } = null!;
    public Vulnerability Vulnerability { get; private set; } = null!;
    public SoftwareProduct? SoftwareProduct { get; private set; }
    public InstalledSoftware InstalledSoftware { get; private set; } = null!;

    private DeviceVulnerabilityExposure() { }

    public static DeviceVulnerabilityExposure Observe(
        Guid tenantId,
        Guid deviceId,
        Guid vulnerabilityId,
        Guid? softwareProductId,
        Guid installedSoftwareId,
        string matchedVersion,
        ExposureMatchSource matchSource,
        DateTimeOffset observedAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException(nameof(tenantId));
        if (deviceId == Guid.Empty) throw new ArgumentException(nameof(deviceId));
        if (vulnerabilityId == Guid.Empty) throw new ArgumentException(nameof(vulnerabilityId));
        if (installedSoftwareId == Guid.Empty) throw new ArgumentException(nameof(installedSoftwareId));

        return new DeviceVulnerabilityExposure
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = deviceId,
            VulnerabilityId = vulnerabilityId,
            SoftwareProductId = softwareProductId,
            InstalledSoftwareId = installedSoftwareId,
            MatchedVersion = matchedVersion ?? string.Empty,
            MatchSource = matchSource,
            Status = ExposureStatus.Open,
            FirstObservedAt = observedAt,
            LastObservedAt = observedAt,
            ResolvedAt = null,
        };
    }

    public void Reobserve(DateTimeOffset observedAt)
    {
        LastObservedAt = observedAt;
        if (Status == ExposureStatus.Resolved)
        {
            Status = ExposureStatus.Open;
            ResolvedAt = null;
        }
    }

    public void Resolve(DateTimeOffset resolvedAt)
    {
        Status = ExposureStatus.Resolved;
        ResolvedAt = resolvedAt;
    }

    public void Reopen(DateTimeOffset observedAt)
    {
        Status = ExposureStatus.Open;
        ResolvedAt = null;
        LastObservedAt = observedAt;
    }
}
```

- [ ] **Step 4: Tests pass**

Run: `dotnet test --filter "FullyQualifiedName~DeviceVulnerabilityExposureTests"` — PASS.

- [ ] **Step 5: EF configuration with query filter**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class DeviceVulnerabilityExposureConfiguration : IEntityTypeConfiguration<DeviceVulnerabilityExposure>
{
    public void Configure(EntityTypeBuilder<DeviceVulnerabilityExposure> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.MatchedVersion).IsRequired().HasMaxLength(128);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.MatchSource).HasConversion<string>().HasMaxLength(16);

        builder.HasOne(e => e.Device)
            .WithMany()
            .HasForeignKey(e => e.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Vulnerability)
            .WithMany()
            .HasForeignKey(e => e.VulnerabilityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SoftwareProduct)
            .WithMany()
            .HasForeignKey(e => e.SoftwareProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.InstalledSoftware)
            .WithMany()
            .HasForeignKey(e => e.InstalledSoftwareId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.DeviceId, e.VulnerabilityId }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.VulnerabilityId });
        builder.HasIndex(e => new { e.TenantId, e.Status });
    }
}
```

- [ ] **Step 6: Register DbSet + query filter**

In `PatchHoundDbContext`:

```csharp
public DbSet<DeviceVulnerabilityExposure> DeviceVulnerabilityExposures => Set<DeviceVulnerabilityExposure>();
```

In `OnModelCreating`, add (near other canonical tenant filters):

```csharp
modelBuilder.Entity<DeviceVulnerabilityExposure>()
    .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
```

- [ ] **Step 7: Build + commit**

```bash
dotnet build
git add src/PatchHound.Core/Entities/DeviceVulnerabilityExposure.cs \
        src/PatchHound.Core/Enums/ExposureStatus.cs \
        src/PatchHound.Core/Enums/ExposureMatchSource.cs \
        src/PatchHound.Infrastructure/Data/Configurations/DeviceVulnerabilityExposureConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Core.Tests/Entities/DeviceVulnerabilityExposureTests.cs
git commit -m "feat(phase-3): add DeviceVulnerabilityExposure tenant-scoped entity"
```

---

## Task 2: Add `ExposureEpisode` tenant-scoped entity

An episode captures a single Open → Resolved interval for an exposure. When an exposure is reopened, a new episode row is created.

**Files:**
- Create: `src/PatchHound.Core/Entities/ExposureEpisode.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/ExposureEpisodeConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Test: `tests/PatchHound.Core.Tests/Entities/ExposureEpisodeTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using PatchHound.Core.Entities;

namespace PatchHound.Core.Tests.Entities;

public class ExposureEpisodeTests
{
    [Fact]
    public void Open_creates_first_episode()
    {
        var tenantId = Guid.NewGuid();
        var exposureId = Guid.NewGuid();
        var openedAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        var ep = ExposureEpisode.Open(tenantId, exposureId, episodeNumber: 1, openedAt);

        Assert.Equal(tenantId, ep.TenantId);
        Assert.Equal(exposureId, ep.DeviceVulnerabilityExposureId);
        Assert.Equal(1, ep.EpisodeNumber);
        Assert.Equal(openedAt, ep.OpenedAt);
        Assert.Null(ep.ClosedAt);
    }

    [Fact]
    public void Close_sets_ClosedAt()
    {
        var ep = ExposureEpisode.Open(Guid.NewGuid(), Guid.NewGuid(), 1, new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        var closedAt = new DateTimeOffset(2026, 4, 5, 0, 0, 0, TimeSpan.Zero);

        ep.Close(closedAt);

        Assert.Equal(closedAt, ep.ClosedAt);
    }

    [Fact]
    public void Close_on_already_closed_episode_throws()
    {
        var ep = ExposureEpisode.Open(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow);
        ep.Close(DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => ep.Close(DateTimeOffset.UtcNow));
    }
}
```

- [ ] **Step 2: Create entity**

```csharp
namespace PatchHound.Core.Entities;

public class ExposureEpisode
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceVulnerabilityExposureId { get; private set; }
    public int EpisodeNumber { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public DeviceVulnerabilityExposure Exposure { get; private set; } = null!;

    private ExposureEpisode() { }

    public static ExposureEpisode Open(Guid tenantId, Guid exposureId, int episodeNumber, DateTimeOffset openedAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException(nameof(tenantId));
        if (exposureId == Guid.Empty) throw new ArgumentException(nameof(exposureId));
        if (episodeNumber < 1) throw new ArgumentOutOfRangeException(nameof(episodeNumber));

        return new ExposureEpisode
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceVulnerabilityExposureId = exposureId,
            EpisodeNumber = episodeNumber,
            OpenedAt = openedAt,
            ClosedAt = null,
        };
    }

    public void Close(DateTimeOffset closedAt)
    {
        if (ClosedAt is not null) throw new InvalidOperationException("Episode already closed.");
        ClosedAt = closedAt;
    }
}
```

- [ ] **Step 3: Test passes**

- [ ] **Step 4: EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ExposureEpisodeConfiguration : IEntityTypeConfiguration<ExposureEpisode>
{
    public void Configure(EntityTypeBuilder<ExposureEpisode> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasOne(e => e.Exposure)
            .WithMany()
            .HasForeignKey(e => e.DeviceVulnerabilityExposureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TenantId, e.DeviceVulnerabilityExposureId, e.EpisodeNumber }).IsUnique();
    }
}
```

Register DbSet + query filter in `PatchHoundDbContext`:

```csharp
public DbSet<ExposureEpisode> ExposureEpisodes => Set<ExposureEpisode>();

modelBuilder.Entity<ExposureEpisode>()
    .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
```

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Core/Entities/ExposureEpisode.cs \
        src/PatchHound.Infrastructure/Data/Configurations/ExposureEpisodeConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Core.Tests/Entities/ExposureEpisodeTests.cs
git commit -m "feat(phase-3): add ExposureEpisode tenant-scoped entity"
```

---

## Task 3: Add `ExposureAssessment` tenant-scoped entity

Environmental-severity assessment for a single exposure, keyed by `DeviceVulnerabilityExposureId`. Carries the `SecurityProfileId` that produced the environmental modifiers (spec §4.3).

**Files:**
- Create: `src/PatchHound.Core/Entities/ExposureAssessment.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/ExposureAssessmentConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Test: `tests/PatchHound.Core.Tests/Entities/ExposureAssessmentTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using PatchHound.Core.Entities;

namespace PatchHound.Core.Tests.Entities;

public class ExposureAssessmentTests
{
    [Fact]
    public void Create_captures_score_reason_and_profile()
    {
        var tenantId = Guid.NewGuid();
        var exposureId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        var a = ExposureAssessment.Create(
            tenantId, exposureId, profileId,
            baseCvss: 7.5m,
            environmentalCvss: 8.2m,
            reason: "InternetFacing + HighConfidentiality",
            calculatedAt: new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(tenantId, a.TenantId);
        Assert.Equal(exposureId, a.DeviceVulnerabilityExposureId);
        Assert.Equal(profileId, a.SecurityProfileId);
        Assert.Equal(7.5m, a.BaseCvss);
        Assert.Equal(8.2m, a.EnvironmentalCvss);
        Assert.Equal(8.2m, a.Score);
        Assert.Equal("InternetFacing + HighConfidentiality", a.Reason);
        Assert.Equal(new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero), a.CalculatedAt);
    }

    [Fact]
    public void Update_refreshes_score_and_CalculatedAt()
    {
        var a = ExposureAssessment.Create(Guid.NewGuid(), Guid.NewGuid(), null, 5m, 5m, "base", DateTimeOffset.UtcNow);
        var original = a.CalculatedAt;
        Thread.Sleep(5);
        a.Update(baseCvss: 5m, environmentalCvss: 7.1m, reason: "updated", calculatedAt: DateTimeOffset.UtcNow);
        Assert.Equal(7.1m, a.EnvironmentalCvss);
        Assert.Equal("updated", a.Reason);
        Assert.True(a.CalculatedAt > original);
    }
}
```

- [ ] **Step 2: Create entity**

```csharp
namespace PatchHound.Core.Entities;

public class ExposureAssessment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceVulnerabilityExposureId { get; private set; }
    public Guid? SecurityProfileId { get; private set; }
    public decimal BaseCvss { get; private set; }
    public decimal EnvironmentalCvss { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset CalculatedAt { get; private set; }

    public decimal Score => EnvironmentalCvss;

    public DeviceVulnerabilityExposure Exposure { get; private set; } = null!;
    public SecurityProfile? SecurityProfile { get; private set; }

    private ExposureAssessment() { }

    public static ExposureAssessment Create(
        Guid tenantId,
        Guid exposureId,
        Guid? securityProfileId,
        decimal baseCvss,
        decimal environmentalCvss,
        string reason,
        DateTimeOffset calculatedAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException(nameof(tenantId));
        if (exposureId == Guid.Empty) throw new ArgumentException(nameof(exposureId));
        return new ExposureAssessment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceVulnerabilityExposureId = exposureId,
            SecurityProfileId = securityProfileId,
            BaseCvss = baseCvss,
            EnvironmentalCvss = environmentalCvss,
            Reason = reason ?? string.Empty,
            CalculatedAt = calculatedAt,
        };
    }

    public void Update(decimal baseCvss, decimal environmentalCvss, string reason, DateTimeOffset calculatedAt)
    {
        BaseCvss = baseCvss;
        EnvironmentalCvss = environmentalCvss;
        Reason = reason ?? string.Empty;
        CalculatedAt = calculatedAt;
    }
}
```

- [ ] **Step 3: Test passes**

- [ ] **Step 4: EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ExposureAssessmentConfiguration : IEntityTypeConfiguration<ExposureAssessment>
{
    public void Configure(EntityTypeBuilder<ExposureAssessment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.BaseCvss).HasColumnType("numeric(4,2)");
        builder.Property(a => a.EnvironmentalCvss).HasColumnType("numeric(4,2)");
        builder.Property(a => a.Reason).HasMaxLength(512);

        builder.HasOne(a => a.Exposure)
            .WithMany()
            .HasForeignKey(a => a.DeviceVulnerabilityExposureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.SecurityProfile)
            .WithMany()
            .HasForeignKey(a => a.SecurityProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(a => a.Score); // computed property
        builder.HasIndex(a => new { a.TenantId, a.DeviceVulnerabilityExposureId }).IsUnique();
    }
}
```

Register DbSet + filter in `PatchHoundDbContext`.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Core/Entities/ExposureAssessment.cs \
        src/PatchHound.Infrastructure/Data/Configurations/ExposureAssessmentConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Core.Tests/Entities/ExposureAssessmentTests.cs
git commit -m "feat(phase-3): add ExposureAssessment tenant-scoped entity"
```

---

## Task 4: `ExposureDerivationService` — match `InstalledSoftware` × `VulnerabilityApplicability`

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/ExposureDerivationService.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`
- Test: `tests/PatchHound.Infrastructure.Tests/Services/ExposureDerivationServiceTests.cs`

- [ ] **Step 1: Write failing test — product-keyed match**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure.Testing;

namespace PatchHound.Tests.Infrastructure.Services;

public class ExposureDerivationServiceTests
{
    [Fact]
    public async Task Derives_exposure_for_product_keyed_applicability()
    {
        await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);

        // Seed global: SoftwareProduct + Vulnerability + product-keyed applicability
        var product = SoftwareProduct.Create("acme:widget", "Widget", "Acme", null);
        var vuln = Vulnerability.Create("nvd", "CVE-2026-7001", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, softwareProductId: product.Id, cpeCriteria: null,
            vulnerable: true, null, null, null, null));

        // Seed tenant: Device + InstalledSoftware for the product
        var sourceSystem = SourceSystem.Create("test-source", "Test", true);
        db.SourceSystems.Add(sourceSystem);
        var device = Device.Create(tenantId, sourceSystem.Id, "dev-1", "Device 1");
        db.Devices.Add(device);
        var installed = InstalledSoftware.Observe(
            tenantId, device.Id, product.Id, sourceSystem.Id,
            observedVersion: "1.2.3",
            observedAt: DateTimeOffset.UtcNow);
        db.InstalledSoftware.Add(installed);
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);
        var result = await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();

        var exposures = await db.DeviceVulnerabilityExposures.ToListAsync();
        Assert.Single(exposures);
        Assert.Equal(device.Id, exposures[0].DeviceId);
        Assert.Equal(vuln.Id, exposures[0].VulnerabilityId);
        Assert.Equal(product.Id, exposures[0].SoftwareProductId);
        Assert.Equal(ExposureMatchSource.Product, exposures[0].MatchSource);
        Assert.Equal("1.2.3", exposures[0].MatchedVersion);
        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Reobserved);
        Assert.Equal(0, result.Resolved);
    }

    [Fact]
    public async Task Re_deriving_same_state_reobserves_without_duplicating()
    {
        await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
        SeedProductKeyedExposure(db, tenantId);
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow.AddMinutes(5), CancellationToken.None);
        await db.SaveChangesAsync();

        var exposures = await db.DeviceVulnerabilityExposures.ToListAsync();
        Assert.Single(exposures);
        Assert.True(exposures[0].LastObservedAt > exposures[0].FirstObservedAt);
    }

    [Fact]
    public async Task Resolves_exposure_when_InstalledSoftware_row_is_missing_on_next_derive()
    {
        await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var (product, vuln, device, installed) = SeedProductKeyedExposure(db, tenantId);
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();

        // Software uninstalled: remove InstalledSoftware row
        db.InstalledSoftware.Remove(installed);
        await db.SaveChangesAsync();

        var resolvedAt = DateTimeOffset.UtcNow.AddHours(1);
        await svc.DeriveForTenantAsync(tenantId, resolvedAt, CancellationToken.None);
        await db.SaveChangesAsync();

        var exposures = await db.DeviceVulnerabilityExposures.ToListAsync();
        Assert.Single(exposures);
        Assert.Equal(ExposureStatus.Resolved, exposures[0].Status);
        Assert.Equal(resolvedAt, exposures[0].ResolvedAt);
    }

    [Fact]
    public async Task CPE_fallback_match_for_applicability_without_SoftwareProductId()
    {
        await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var product = SoftwareProduct.Create("acme:widget", "Widget", "Acme", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        var vuln = Vulnerability.Create("nvd", "CVE-2026-7002", "t", "d", Severity.Critical, 9.5m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, softwareProductId: null,
            cpeCriteria: "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*",
            vulnerable: true, null, null, null, null));

        var src = SourceSystem.Create("test", "Test", true);
        db.SourceSystems.Add(src);
        var device = Device.Create(tenantId, src.Id, "dev-1", "Device");
        db.Devices.Add(device);
        db.InstalledSoftware.Add(InstalledSoftware.Observe(tenantId, device.Id, product.Id, src.Id, "1.0", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);
        await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();

        var e = await db.DeviceVulnerabilityExposures.SingleAsync();
        Assert.Equal(ExposureMatchSource.Cpe, e.MatchSource);
    }

    private static (SoftwareProduct, Vulnerability, Device, InstalledSoftware) SeedProductKeyedExposure(
        Data.PatchHoundDbContext db, Guid tenantId)
    {
        var product = SoftwareProduct.Create("acme:widget", "Widget", "Acme", null);
        var vuln = Vulnerability.Create("nvd", "CVE-2026-SEED", "t", "d", Severity.High, 7m, "v", DateTimeOffset.UtcNow);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(
            vuln.Id, product.Id, null, true, null, null, null, null));

        var src = SourceSystem.Create("test", "Test", true);
        db.SourceSystems.Add(src);
        var device = Device.Create(tenantId, src.Id, "dev-seed", "Seed");
        db.Devices.Add(device);
        var inst = InstalledSoftware.Observe(tenantId, device.Id, product.Id, src.Id, "1.2.3", DateTimeOffset.UtcNow);
        db.InstalledSoftware.Add(inst);
        return (product, vuln, device, inst);
    }
}
```

Run: FAIL (service not defined).

- [ ] **Step 2: Implement `ExposureDerivationService`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public record ExposureDerivationResult(int Inserted, int Reobserved, int Resolved);

public class ExposureDerivationService
{
    private readonly PatchHoundDbContext _db;
    private readonly ILogger<ExposureDerivationService> _logger;

    public ExposureDerivationService(PatchHoundDbContext db, ILogger<ExposureDerivationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ExposureDerivationResult> DeriveForTenantAsync(
        Guid tenantId, DateTimeOffset observedAt, CancellationToken ct)
    {
        // Step 1: load current InstalledSoftware for the tenant
        var installs = await _db.InstalledSoftware
            .Where(i => i.TenantId == tenantId)
            .Select(i => new
            {
                i.Id,
                i.DeviceId,
                i.SoftwareProductId,
                i.ObservedVersion,
                ProductCpe = i.SoftwareProduct.PrimaryCpe23Uri,
            })
            .ToListAsync(ct);

        if (installs.Count == 0)
        {
            return await ResolveStaleExposuresAsync(tenantId, Array.Empty<(Guid DeviceId, Guid VulnerabilityId)>(), observedAt, ct);
        }

        var productIds = installs.Select(i => i.SoftwareProductId).Distinct().ToList();

        // Step 2: all vulnerability applicabilities matching any installed product (by ID)
        var productApps = await _db.VulnerabilityApplicabilities
            .Where(a => a.SoftwareProductId != null && productIds.Contains(a.SoftwareProductId!.Value) && a.Vulnerable)
            .Select(a => new
            {
                a.Id, a.VulnerabilityId, a.SoftwareProductId,
                a.VersionStartIncluding, a.VersionStartExcluding, a.VersionEndIncluding, a.VersionEndExcluding,
            })
            .ToListAsync(ct);

        // Step 3: CPE fallback — applicabilities with no product ID but CPE criteria equal to product's primary CPE
        var cpeList = installs
            .Where(i => !string.IsNullOrWhiteSpace(i.ProductCpe))
            .Select(i => i.ProductCpe!)
            .Distinct()
            .ToList();

        var cpeApps = await _db.VulnerabilityApplicabilities
            .Where(a => a.SoftwareProductId == null
                     && a.CpeCriteria != null
                     && cpeList.Contains(a.CpeCriteria)
                     && a.Vulnerable)
            .Select(a => new
            {
                a.Id, a.VulnerabilityId, a.CpeCriteria,
                a.VersionStartIncluding, a.VersionStartExcluding, a.VersionEndIncluding, a.VersionEndExcluding,
            })
            .ToListAsync(ct);

        // Step 4: build the current (DeviceId, VulnerabilityId) set and track match metadata
        var currentExposures = new Dictionary<(Guid DeviceId, Guid VulnerabilityId), (Guid InstalledSoftwareId, Guid? ProductId, ExposureMatchSource Source, string MatchedVersion)>();

        foreach (var install in installs)
        {
            foreach (var app in productApps.Where(a => a.SoftwareProductId == install.SoftwareProductId))
            {
                if (!VersionRangeIncludes(install.ObservedVersion,
                    app.VersionStartIncluding, app.VersionStartExcluding,
                    app.VersionEndIncluding, app.VersionEndExcluding)) continue;

                var key = (install.DeviceId, app.VulnerabilityId);
                if (currentExposures.ContainsKey(key)) continue;
                currentExposures[key] = (install.Id, install.SoftwareProductId, ExposureMatchSource.Product, install.ObservedVersion ?? string.Empty);
            }

            if (install.ProductCpe is not null)
            {
                foreach (var app in cpeApps.Where(a => a.CpeCriteria == install.ProductCpe))
                {
                    if (!VersionRangeIncludes(install.ObservedVersion,
                        app.VersionStartIncluding, app.VersionStartExcluding,
                        app.VersionEndIncluding, app.VersionEndExcluding)) continue;

                    var key = (install.DeviceId, app.VulnerabilityId);
                    if (currentExposures.ContainsKey(key)) continue;
                    currentExposures[key] = (install.Id, install.SoftwareProductId, ExposureMatchSource.Cpe, install.ObservedVersion ?? string.Empty);
                }
            }
        }

        // Step 5: upsert each current exposure
        var existing = await _db.DeviceVulnerabilityExposures
            .Where(e => e.TenantId == tenantId)
            .ToDictionaryAsync(e => (e.DeviceId, e.VulnerabilityId), ct);

        int inserted = 0, reobserved = 0, resolved = 0;
        foreach (var kv in currentExposures)
        {
            if (existing.TryGetValue(kv.Key, out var e))
            {
                e.Reobserve(observedAt);
                reobserved++;
            }
            else
            {
                var fresh = DeviceVulnerabilityExposure.Observe(
                    tenantId, kv.Key.DeviceId, kv.Key.VulnerabilityId,
                    kv.Value.ProductId, kv.Value.InstalledSoftwareId,
                    kv.Value.MatchedVersion, kv.Value.Source, observedAt);
                _db.DeviceVulnerabilityExposures.Add(fresh);
                inserted++;
            }
        }

        // Step 6: resolve exposures no longer in the current set
        foreach (var kv in existing)
        {
            if (currentExposures.ContainsKey(kv.Key)) continue;
            if (kv.Value.Status == ExposureStatus.Resolved) continue;
            kv.Value.Resolve(observedAt);
            resolved++;
        }

        _logger.LogInformation(
            "Exposure derivation for tenant {TenantId}: inserted={Inserted} reobserved={Reobserved} resolved={Resolved}",
            tenantId, inserted, reobserved, resolved);

        return new ExposureDerivationResult(inserted, reobserved, resolved);
    }

    private Task<ExposureDerivationResult> ResolveStaleExposuresAsync(
        Guid tenantId, IReadOnlyCollection<(Guid, Guid)> current, DateTimeOffset observedAt, CancellationToken ct)
    {
        // Fallback path when tenant has no InstalledSoftware at all.
        return Task.FromResult(new ExposureDerivationResult(0, 0, 0));
    }

    /// <summary>
    /// CPE-style version range check. Returns true if <paramref name="observedVersion"/> falls inside
    /// the (start, end] range. Empty bounds are unbounded.
    /// </summary>
    private static bool VersionRangeIncludes(
        string? observedVersion,
        string? startIncluding, string? startExcluding,
        string? endIncluding, string? endExcluding)
    {
        if (string.IsNullOrWhiteSpace(observedVersion)) return true; // unknown version = always matches
        var observed = TryParseVersion(observedVersion);
        if (observed is null) return true;

        if (!string.IsNullOrWhiteSpace(startIncluding) && TryParseVersion(startIncluding) is Version si && observed < si) return false;
        if (!string.IsNullOrWhiteSpace(startExcluding) && TryParseVersion(startExcluding) is Version se && observed <= se) return false;
        if (!string.IsNullOrWhiteSpace(endIncluding) && TryParseVersion(endIncluding) is Version ei && observed > ei) return false;
        if (!string.IsNullOrWhiteSpace(endExcluding) && TryParseVersion(endExcluding) is Version ee && observed >= ee) return false;

        return true;
    }

    private static Version? TryParseVersion(string s) =>
        Version.TryParse(s.Trim(), out var v) ? v : null;
}
```

- [ ] **Step 3: Register in DI**

```csharp
services.AddScoped<ExposureDerivationService>();
```

- [ ] **Step 4: Tests pass**

Run: `dotnet test --filter "FullyQualifiedName~ExposureDerivationServiceTests"` — PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/ExposureDerivationService.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        tests/PatchHound.Infrastructure.Tests/Services/ExposureDerivationServiceTests.cs
git commit -m "feat(phase-3): add ExposureDerivationService with product + CPE matching"
```

---

## Task 5: `ExposureEpisodeService` — manage episode lifecycle

This service observes status transitions on `DeviceVulnerabilityExposure` and creates/closes `ExposureEpisode` rows. It is called immediately after `ExposureDerivationService` on the same SaveChanges cycle.

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/ExposureEpisodeService.cs`
- Test: `tests/PatchHound.Infrastructure.Tests/Services/ExposureEpisodeServiceTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[Fact]
public async Task Opens_episode_1_on_first_observation()
{
    await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
    var exposure = SeedOpenExposure(db, tenantId, DateTimeOffset.UtcNow);
    await db.SaveChangesAsync();

    var svc = new ExposureEpisodeService(db);
    await svc.SyncEpisodesForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
    await db.SaveChangesAsync();

    var episodes = await db.ExposureEpisodes.Where(e => e.DeviceVulnerabilityExposureId == exposure.Id).ToListAsync();
    Assert.Single(episodes);
    Assert.Equal(1, episodes[0].EpisodeNumber);
    Assert.Null(episodes[0].ClosedAt);
}

[Fact]
public async Task Closes_open_episode_when_exposure_resolves()
{
    await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
    var exposure = SeedOpenExposure(db, tenantId, DateTimeOffset.UtcNow);
    await db.SaveChangesAsync();

    var svc = new ExposureEpisodeService(db);
    await svc.SyncEpisodesForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
    await db.SaveChangesAsync();

    exposure.Resolve(new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero));
    await db.SaveChangesAsync();

    await svc.SyncEpisodesForTenantAsync(tenantId, new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
    await db.SaveChangesAsync();

    var episodes = await db.ExposureEpisodes.ToListAsync();
    Assert.Single(episodes);
    Assert.NotNull(episodes[0].ClosedAt);
    Assert.Equal(new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero), episodes[0].ClosedAt);
}

[Fact]
public async Task Opens_new_episode_when_exposure_reopens_after_resolve()
{
    await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
    var exposure = SeedOpenExposure(db, tenantId, new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
    await db.SaveChangesAsync();

    var svc = new ExposureEpisodeService(db);
    await svc.SyncEpisodesForTenantAsync(tenantId, new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
    await db.SaveChangesAsync();

    exposure.Resolve(new DateTimeOffset(2026, 4, 5, 0, 0, 0, TimeSpan.Zero));
    await db.SaveChangesAsync();
    await svc.SyncEpisodesForTenantAsync(tenantId, new DateTimeOffset(2026, 4, 5, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
    await db.SaveChangesAsync();

    exposure.Reobserve(new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero));
    await db.SaveChangesAsync();
    await svc.SyncEpisodesForTenantAsync(tenantId, new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
    await db.SaveChangesAsync();

    var episodes = await db.ExposureEpisodes.OrderBy(e => e.EpisodeNumber).ToListAsync();
    Assert.Equal(2, episodes.Count);
    Assert.Equal(1, episodes[0].EpisodeNumber);
    Assert.NotNull(episodes[0].ClosedAt);
    Assert.Equal(2, episodes[1].EpisodeNumber);
    Assert.Null(episodes[1].ClosedAt);
}
```

- [ ] **Step 2: Implement service**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class ExposureEpisodeService
{
    private readonly PatchHoundDbContext _db;

    public ExposureEpisodeService(PatchHoundDbContext db) { _db = db; }

    public async Task SyncEpisodesForTenantAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var exposures = await _db.DeviceVulnerabilityExposures
            .Where(e => e.TenantId == tenantId)
            .ToListAsync(ct);

        var exposureIds = exposures.Select(e => e.Id).ToList();
        var episodesByExposure = await _db.ExposureEpisodes
            .Where(e => exposureIds.Contains(e.DeviceVulnerabilityExposureId))
            .ToListAsync(ct);

        var lookup = episodesByExposure
            .GroupBy(e => e.DeviceVulnerabilityExposureId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.EpisodeNumber).ToList());

        foreach (var exposure in exposures)
        {
            var episodes = lookup.TryGetValue(exposure.Id, out var list) ? list : new List<ExposureEpisode>();
            var latest = episodes.FirstOrDefault();

            if (exposure.Status == ExposureStatus.Open)
            {
                // Need an open episode.
                if (latest is null || latest.ClosedAt is not null)
                {
                    var nextNumber = (latest?.EpisodeNumber ?? 0) + 1;
                    _db.ExposureEpisodes.Add(ExposureEpisode.Open(tenantId, exposure.Id, nextNumber, exposure.LastObservedAt));
                }
            }
            else // Resolved
            {
                if (latest is not null && latest.ClosedAt is null)
                {
                    latest.Close(exposure.ResolvedAt ?? now);
                }
            }
        }
    }
}
```

- [ ] **Step 3: Tests pass**

- [ ] **Step 4: Register in DI**

```csharp
services.AddScoped<ExposureEpisodeService>();
```

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/ExposureEpisodeService.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        tests/PatchHound.Infrastructure.Tests/Services/ExposureEpisodeServiceTests.cs
git commit -m "feat(phase-3): add ExposureEpisodeService with open/close/reopen lifecycle"
```

---

## Task 6: `ExposureAssessmentService` — environmental-severity computation

Uses the **existing** `EnvironmentalSeverityCalculator` (renamed if necessary; it was left alive by Phase 1 as a pure function service). For each exposure, loads `Device.SecurityProfileId → SecurityProfile`, loads the vulnerability's base CVSS vector, computes the modified score, and upserts one `ExposureAssessment` row per exposure.

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/ExposureAssessmentService.cs`
- Test: `tests/PatchHound.Infrastructure.Tests/Services/ExposureAssessmentServiceTests.cs`

- [ ] **Step 1: Write failing test — assessment uses SecurityProfile modifiers**

```csharp
[Fact]
public async Task Assessment_uses_security_profile_environmental_modifiers()
{
    await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
    var profile = SecurityProfile.Create(
        tenantId, "Internet-facing",
        description: "critical perimeter",
        environmentClass: EnvironmentClass.Production,
        internetReachability: InternetReachability.Public,
        confidentialityRequirement: SecurityRequirementLevel.High,
        integrityRequirement: SecurityRequirementLevel.High,
        availabilityRequirement: SecurityRequirementLevel.High);
    db.SecurityProfiles.Add(profile);

    var src = SourceSystem.Create("test", "Test", true);
    db.SourceSystems.Add(src);
    var device = Device.Create(tenantId, src.Id, "dev-1", "Device");
    device.AssignSecurityProfile(profile.Id);
    db.Devices.Add(device);

    var product = SoftwareProduct.Create("acme:widget", "Widget", "Acme", null);
    db.SoftwareProducts.Add(product);
    var vuln = Vulnerability.Create("nvd", "CVE-2026-ENV", "t", "d", Severity.Medium,
        cvssScore: 5.0m,
        cvssVector: "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:L/I:L/A:L",
        publishedDate: DateTimeOffset.UtcNow);
    db.Vulnerabilities.Add(vuln);

    var installed = InstalledSoftware.Observe(tenantId, device.Id, product.Id, src.Id, "1.0", DateTimeOffset.UtcNow);
    db.InstalledSoftware.Add(installed);
    var exposure = DeviceVulnerabilityExposure.Observe(
        tenantId, device.Id, vuln.Id, product.Id, installed.Id, "1.0", ExposureMatchSource.Product, DateTimeOffset.UtcNow);
    db.DeviceVulnerabilityExposures.Add(exposure);
    await db.SaveChangesAsync();

    var svc = new ExposureAssessmentService(db, new EnvironmentalSeverityCalculator());
    await svc.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
    await db.SaveChangesAsync();

    var assessment = await db.ExposureAssessments.SingleAsync(a => a.DeviceVulnerabilityExposureId == exposure.Id);
    Assert.Equal(profile.Id, assessment.SecurityProfileId);
    Assert.Equal(5.0m, assessment.BaseCvss);
    Assert.NotEqual(5.0m, assessment.EnvironmentalCvss); // must differ: High CIA requirements + internet-facing
    Assert.Contains("Confidentiality", assessment.Reason, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Implement service**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class ExposureAssessmentService
{
    private readonly PatchHoundDbContext _db;
    private readonly EnvironmentalSeverityCalculator _calculator;

    public ExposureAssessmentService(PatchHoundDbContext db, EnvironmentalSeverityCalculator calculator)
    {
        _db = db;
        _calculator = calculator;
    }

    public async Task AssessForTenantAsync(Guid tenantId, DateTimeOffset calculatedAt, CancellationToken ct)
    {
        var exposures = await _db.DeviceVulnerabilityExposures
            .Where(e => e.TenantId == tenantId)
            .Include(e => e.Vulnerability)
            .Include(e => e.Device)
            .ToListAsync(ct);

        var profileIds = exposures.Where(e => e.Device.SecurityProfileId is not null)
                                  .Select(e => e.Device.SecurityProfileId!.Value).Distinct().ToList();
        var profiles = await _db.SecurityProfiles
            .Where(p => profileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var existing = await _db.ExposureAssessments
            .Where(a => a.TenantId == tenantId)
            .ToDictionaryAsync(a => a.DeviceVulnerabilityExposureId, ct);

        foreach (var exposure in exposures)
        {
            var baseCvss = exposure.Vulnerability.CvssScore ?? 0m;
            var baseVector = exposure.Vulnerability.CvssVector;

            SecurityProfile? profile = null;
            if (exposure.Device.SecurityProfileId is { } pid && profiles.TryGetValue(pid, out var p))
                profile = p;

            var (envCvss, reason) = _calculator.Compute(baseCvss, baseVector, profile);

            if (existing.TryGetValue(exposure.Id, out var a))
            {
                a.Update(baseCvss, envCvss, reason, calculatedAt);
            }
            else
            {
                _db.ExposureAssessments.Add(ExposureAssessment.Create(
                    tenantId, exposure.Id, profile?.Id, baseCvss, envCvss, reason, calculatedAt));
            }
        }
    }
}
```

The `EnvironmentalSeverityCalculator.Compute(baseCvss, baseVector, profile)` returns `(decimal envCvss, string reason)`. **If** the existing calculator does not yet expose this exact signature, add an adapter method on the existing class that wraps whatever current `CalculateModified(…)` API it exposes and returns a short reason string that names the dominant modifier (e.g. `"High confidentiality requirement + Network attack vector"`). Keep the existing API intact so Phase 5 callers are unaffected.

- [ ] **Step 3: Test passes**

Run: `dotnet test --filter "FullyQualifiedName~ExposureAssessmentServiceTests"` — PASS.

- [ ] **Step 4: Register in DI**

```csharp
services.AddScoped<ExposureAssessmentService>();
```

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/ExposureAssessmentService.cs \
        src/PatchHound.Infrastructure/Services/EnvironmentalSeverityCalculator.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        tests/PatchHound.Infrastructure.Tests/Services/ExposureAssessmentServiceTests.cs
git commit -m "feat(phase-3): add ExposureAssessmentService using SecurityProfile env modifiers"
```

---

## Task 7: **Hard-gate test** — environmental severity changes score non-trivially

Spec §5.3 / §7 require a dedicated test that proves the environmental CVSS path is live: the same vulnerability against devices with different `SecurityProfile`s must produce different `ExposureAssessment.Score` values, and the reason string must reference the dominant environmental modifier.

**Files:**
- Test: `tests/PatchHound.Infrastructure.Tests/Services/EnvironmentalSeverityHardGateTests.cs`

- [ ] **Step 1: Write the hard-gate test**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure.Testing;

namespace PatchHound.Tests.Infrastructure.Services;

public class EnvironmentalSeverityHardGateTests
{
    [Fact]
    public async Task Env_severity_differs_from_base_cvss_when_profile_raises_requirements()
    {
        await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);

        // Profile A: internet-facing, HIGH CIA requirements
        var profileA = SecurityProfile.Create(tenantId, "A", null,
            EnvironmentClass.Production, InternetReachability.Public,
            SecurityRequirementLevel.High, SecurityRequirementLevel.High, SecurityRequirementLevel.High);
        db.SecurityProfiles.Add(profileA);

        // Profile B: isolated, LOW CIA requirements
        var profileB = SecurityProfile.Create(tenantId, "B", null,
            EnvironmentClass.Development, InternetReachability.LocalOnly,
            SecurityRequirementLevel.Low, SecurityRequirementLevel.Low, SecurityRequirementLevel.Low);
        db.SecurityProfiles.Add(profileB);

        var src = SourceSystem.Create("t", "T", true);
        db.SourceSystems.Add(src);

        var deviceA = Device.Create(tenantId, src.Id, "dev-A", "Internet-facing");
        deviceA.AssignSecurityProfile(profileA.Id);
        var deviceB = Device.Create(tenantId, src.Id, "dev-B", "Isolated");
        deviceB.AssignSecurityProfile(profileB.Id);
        db.Devices.AddRange(deviceA, deviceB);

        var product = SoftwareProduct.Create("acme:widget", "Widget", "Acme", null);
        db.SoftwareProducts.Add(product);
        var vuln = Vulnerability.Create("nvd", "CVE-2026-GATE",
            "Env gate",
            "Env gate",
            Severity.Medium,
            cvssScore: 5.0m,
            cvssVector: "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:L/I:L/A:L",
            publishedDate: DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(vuln);

        var instA = InstalledSoftware.Observe(tenantId, deviceA.Id, product.Id, src.Id, "1.0", DateTimeOffset.UtcNow);
        var instB = InstalledSoftware.Observe(tenantId, deviceB.Id, product.Id, src.Id, "1.0", DateTimeOffset.UtcNow);
        db.InstalledSoftware.AddRange(instA, instB);

        var exposureA = DeviceVulnerabilityExposure.Observe(tenantId, deviceA.Id, vuln.Id, product.Id, instA.Id, "1.0", ExposureMatchSource.Product, DateTimeOffset.UtcNow);
        var exposureB = DeviceVulnerabilityExposure.Observe(tenantId, deviceB.Id, vuln.Id, product.Id, instB.Id, "1.0", ExposureMatchSource.Product, DateTimeOffset.UtcNow);
        db.DeviceVulnerabilityExposures.AddRange(exposureA, exposureB);
        await db.SaveChangesAsync();

        var svc = new ExposureAssessmentService(db, new EnvironmentalSeverityCalculator());
        await svc.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();

        var assessmentA = await db.ExposureAssessments.SingleAsync(a => a.DeviceVulnerabilityExposureId == exposureA.Id);
        var assessmentB = await db.ExposureAssessments.SingleAsync(a => a.DeviceVulnerabilityExposureId == exposureB.Id);

        // Hard gate 1: profile A must raise the environmental CVSS above the base CVSS.
        Assert.True(assessmentA.EnvironmentalCvss > assessmentA.BaseCvss,
            $"Profile A env CVSS {assessmentA.EnvironmentalCvss} must exceed base {assessmentA.BaseCvss}");

        // Hard gate 2: profile B must lower (or at least not raise) the environmental CVSS.
        Assert.True(assessmentB.EnvironmentalCvss <= assessmentB.BaseCvss,
            $"Profile B env CVSS {assessmentB.EnvironmentalCvss} must not exceed base {assessmentB.BaseCvss}");

        // Hard gate 3: the two assessments must not be equal.
        Assert.NotEqual(assessmentA.EnvironmentalCvss, assessmentB.EnvironmentalCvss);

        // Hard gate 4: the reason strings must reference the dominant modifier.
        Assert.False(string.IsNullOrWhiteSpace(assessmentA.Reason));
        Assert.False(string.IsNullOrWhiteSpace(assessmentB.Reason));
    }
}
```

- [ ] **Step 2: Run — PASS**

If the calculator does not currently raise env CVSS for High CIA requirements, fix the calculator — do not weaken the test. The hard gate is non-negotiable per spec §5.3.

- [ ] **Step 3: Commit**

```bash
git add tests/PatchHound.Infrastructure.Tests/Services/EnvironmentalSeverityHardGateTests.cs
git commit -m "test(phase-3): env-severity hard-gate test blocks Phase 3 completion"
```

---

## Task 8: Wire the derivation pipeline into `IngestionService`

After Phase 1's `IngestionService` writes `Device` + `InstalledSoftware`, and Phase 2's enrichment runners populate `Vulnerability`/`VulnerabilityApplicability`, the Phase 3 pipeline runs three services per tenant in sequence:

1. `ExposureDerivationService.DeriveForTenantAsync`
2. `ExposureEpisodeService.SyncEpisodesForTenantAsync`
3. `ExposureAssessmentService.AssessForTenantAsync`

All under the tenant's normal request context (not system context), because all three write tenant-scoped rows.

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`
- Test: `tests/PatchHound.Infrastructure.Tests/Services/IngestionServicePhase3Tests.cs`

- [ ] **Step 1: Write an integration test**

```csharp
[Fact]
public async Task IngestionService_run_end_to_end_produces_canonical_exposures_for_tenant()
{
    await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
    // Seed: one SoftwareProduct, one Vulnerability with applicability, one Device with InstalledSoftware
    SeedFullTenantInventoryAndVulnerability(db, tenantId);
    await db.SaveChangesAsync();

    var ingestion = new IngestionService(
        db,
        new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance),
        new ExposureEpisodeService(db),
        new ExposureAssessmentService(db, new EnvironmentalSeverityCalculator()),
        // ... other Phase 1 dependencies
        NullLogger<IngestionService>.Instance);

    await ingestion.RunExposureDerivationAsync(tenantId, CancellationToken.None);

    Assert.NotEmpty(await db.DeviceVulnerabilityExposures.ToListAsync());
    Assert.NotEmpty(await db.ExposureEpisodes.ToListAsync());
    Assert.NotEmpty(await db.ExposureAssessments.ToListAsync());
}
```

- [ ] **Step 2: Add `RunExposureDerivationAsync`**

In `IngestionService`, add (or rename the existing exposure hook):

```csharp
public async Task RunExposureDerivationAsync(Guid tenantId, CancellationToken ct)
{
    var now = DateTimeOffset.UtcNow;
    await _exposureDerivation.DeriveForTenantAsync(tenantId, now, ct);
    await _db.SaveChangesAsync(ct);
    await _exposureEpisodes.SyncEpisodesForTenantAsync(tenantId, now, ct);
    await _db.SaveChangesAsync(ct);
    await _exposureAssessments.AssessForTenantAsync(tenantId, now, ct);
    await _db.SaveChangesAsync(ct);
}
```

Call `RunExposureDerivationAsync` from the existing tenant-loop hook where legacy exposure derivation used to run (remove the legacy call in the same edit; it was removed in Phase 2 but the hook stub may still be there).

- [ ] **Step 3: Test passes**

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionService.cs \
        tests/PatchHound.Infrastructure.Tests/Services/IngestionServicePhase3Tests.cs
git commit -m "feat(phase-3): wire exposure derivation pipeline into IngestionService"
```

---

## Task 9: Populate vulnerability list + detail affected-devices sections

Replaces the Phase 2 empty stubs with real queries that read `DeviceVulnerabilityExposure` + `ExposureAssessment`.

**Files:**
- Modify: `src/PatchHound.Api/Services/VulnerabilityDetailQueryService.cs`
- Modify: `src/PatchHound.Api/Controllers/VulnerabilitiesController.cs`
- Modify: `src/PatchHound.Api/Models/Vulnerabilities/VulnerabilityDetailDto.cs` (add real shapes for affected devices)
- Modify: `src/PatchHound.Api/Models/Vulnerabilities/VulnerabilityDto.cs` (set `ExposureDataAvailable = true`)
- Test: `tests/PatchHound.Api.Tests/Services/VulnerabilityDetailQueryServicePhase3Tests.cs`

- [ ] **Step 1: Update DTOs**

Replace `IReadOnlyList<object> AffectedDevices` with:

```csharp
public record ExposureSectionDto(
    bool DataAvailable,
    string? DataAvailableReason,
    IReadOnlyList<AffectedDeviceDto> AffectedDevices,
    IReadOnlyList<ExposureEpisodeDto> ActiveEpisodes,
    IReadOnlyList<ExposureEpisodeDto> ResolvedEpisodes);

public record AffectedDeviceDto(
    Guid DeviceId,
    string DeviceName,
    Guid? SoftwareProductId,
    string? SoftwareProductName,
    string? MatchedVersion,
    string MatchSource,
    string Status,
    decimal? EnvironmentalCvss,
    string? EnvironmentalReason,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? ResolvedAt);

public record ExposureEpisodeDto(
    Guid EpisodeId, Guid ExposureId, int EpisodeNumber,
    DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt);
```

- [ ] **Step 2: Rewrite `VulnerabilityDetailQueryService.BuildAsync`**

```csharp
public async Task<VulnerabilityDetailDto?> BuildAsync(Guid tenantId, Guid vulnerabilityId, CancellationToken ct)
{
    var vuln = await _db.Vulnerabilities.AsNoTracking()
        .FirstOrDefaultAsync(v => v.Id == vulnerabilityId, ct);
    if (vuln is null) return null;

    var references = await _db.VulnerabilityReferences.AsNoTracking()
        .Where(r => r.VulnerabilityId == vulnerabilityId)
        .ToListAsync(ct);

    var applicabilities = await _db.VulnerabilityApplicabilities.AsNoTracking()
        .Where(a => a.VulnerabilityId == vulnerabilityId)
        .ToListAsync(ct);

    var productIds = applicabilities
        .Where(a => a.SoftwareProductId is not null)
        .Select(a => a.SoftwareProductId!.Value).ToList();
    var productNames = await _db.SoftwareProducts.AsNoTracking()
        .Where(p => productIds.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

    var threat = await _db.ThreatAssessments.AsNoTracking()
        .FirstOrDefaultAsync(t => t.VulnerabilityId == vulnerabilityId, ct);

    // Tenant-scoped exposure reads (query filter enforces tenant isolation).
    var exposures = await _db.DeviceVulnerabilityExposures.AsNoTracking()
        .Where(e => e.VulnerabilityId == vulnerabilityId)
        .Join(_db.Devices.AsNoTracking(), e => e.DeviceId, d => d.Id, (e, d) => new { e, Device = d })
        .ToListAsync(ct);

    var exposureIds = exposures.Select(x => x.e.Id).ToList();
    var assessments = await _db.ExposureAssessments.AsNoTracking()
        .Where(a => exposureIds.Contains(a.DeviceVulnerabilityExposureId))
        .ToDictionaryAsync(a => a.DeviceVulnerabilityExposureId, ct);
    var episodes = await _db.ExposureEpisodes.AsNoTracking()
        .Where(e => exposureIds.Contains(e.DeviceVulnerabilityExposureId))
        .ToListAsync(ct);

    var affected = exposures.Select(x =>
    {
        assessments.TryGetValue(x.e.Id, out var a);
        var productName = x.e.SoftwareProductId is { } pid && productNames.TryGetValue(pid, out var pn) ? pn : null;
        return new AffectedDeviceDto(
            x.Device.Id, x.Device.Name,
            x.e.SoftwareProductId, productName,
            x.e.MatchedVersion,
            x.e.MatchSource.ToString(),
            x.e.Status.ToString(),
            a?.EnvironmentalCvss,
            a?.Reason,
            x.e.FirstObservedAt, x.e.LastObservedAt, x.e.ResolvedAt);
    }).ToList();

    var activeEpisodes = episodes.Where(e => e.ClosedAt is null)
        .Select(e => new ExposureEpisodeDto(e.Id, e.DeviceVulnerabilityExposureId, e.EpisodeNumber, e.OpenedAt, e.ClosedAt)).ToList();
    var resolvedEpisodes = episodes.Where(e => e.ClosedAt is not null)
        .Select(e => new ExposureEpisodeDto(e.Id, e.DeviceVulnerabilityExposureId, e.EpisodeNumber, e.OpenedAt, e.ClosedAt)).ToList();

    return new VulnerabilityDetailDto(
        vuln.Id, vuln.ExternalId, vuln.Source, vuln.Title, vuln.Description,
        vuln.VendorSeverity.ToString(), vuln.CvssScore, vuln.CvssVector, vuln.PublishedDate,
        references.Select(r => new VulnerabilityReferenceDto(r.Url, r.Source, r.GetTags())).ToList(),
        applicabilities.Select(a => new VulnerabilityApplicabilityDto(
            a.SoftwareProductId,
            a.SoftwareProductId is not null && productNames.TryGetValue(a.SoftwareProductId.Value, out var n) ? n : null,
            a.CpeCriteria, a.Vulnerable,
            a.VersionStartIncluding, a.VersionStartExcluding,
            a.VersionEndIncluding, a.VersionEndExcluding)).ToList(),
        threat is null ? null : new ThreatAssessmentDto(threat.ThreatScore, threat.EpssScore, threat.KnownExploited, threat.PublicExploit, threat.ActiveAlert),
        new ExposureSectionDto(
            DataAvailable: true,
            DataAvailableReason: null,
            AffectedDevices: affected,
            ActiveEpisodes: activeEpisodes,
            ResolvedEpisodes: resolvedEpisodes));
}
```

- [ ] **Step 3: Controller list — populate `AffectedDeviceCount`**

In `VulnerabilitiesController.List`, the projected item becomes:

```csharp
.Select(v => new
{
    v.Id, v.ExternalId, v.Title,
    Severity = v.VendorSeverity.ToString(),
    v.Source, v.CvssScore, v.PublishedDate,
    Threat = _dbContext.ThreatAssessments
        .Where(a => a.VulnerabilityId == v.Id)
        .Select(a => new { a.ThreatScore, a.EpssScore, a.PublicExploit, a.KnownExploited, a.ActiveAlert })
        .FirstOrDefault(),
    AffectedDeviceCount = _dbContext.DeviceVulnerabilityExposures
        .Count(e => e.VulnerabilityId == v.Id && e.Status == ExposureStatus.Open),
})
```

and the DTO construction sets `ExposureDataAvailable: true`.

- [ ] **Step 4: Test**

```csharp
[Fact]
public async Task BuildAsync_returns_affected_devices_from_tenant_exposures()
{
    // Seed one vuln + one exposure for tenant A. Assert AffectedDevices has 1 entry with
    // DataAvailable == true and the device name populated.
}

[Fact]
public async Task BuildAsync_never_returns_other_tenants_exposures()
{
    // Seed tenant B exposure on the same global vulnerability.
    // Build with tenant A's context.
    // Assert the only AffectedDevices entry is tenant A's.
}
```

Run: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Api/Services/VulnerabilityDetailQueryService.cs \
        src/PatchHound.Api/Controllers/VulnerabilitiesController.cs \
        src/PatchHound.Api/Models/Vulnerabilities/VulnerabilityDetailDto.cs \
        src/PatchHound.Api/Models/Vulnerabilities/VulnerabilityDto.cs \
        tests/PatchHound.Api.Tests/Services/VulnerabilityDetailQueryServicePhase3Tests.cs
git commit -m "feat(phase-3): populate vulnerability affected-devices section from canonical exposures"
```

---

## Task 10: Device detail page — exposures tab

**Files:**
- Create: `src/PatchHound.Api/Controllers/DeviceExposuresController.cs` (or add endpoint to existing `DevicesController`)
- Modify: `web/src/routes/devices/$deviceId.tsx` — add `Exposures` tab
- Test: backend: `tests/PatchHound.Api.Tests/Controllers/DeviceExposuresControllerTests.cs`
- Test: frontend: `web/src/routes/devices/__tests__/DeviceDetail.exposures.test.tsx`

- [ ] **Step 1: Backend endpoint**

Add `GET /api/devices/{deviceId}/exposures` on `DevicesController` (preferred; avoid a new controller):

```csharp
[HttpGet("{deviceId:guid}/exposures")]
[Authorize(Policy = Policies.ViewDevices)]
public async Task<ActionResult<PagedResponse<DeviceExposureDto>>> ListExposures(
    Guid deviceId, [FromQuery] PaginationQuery pagination, CancellationToken ct)
{
    if (_tenantContext.CurrentTenantId is null)
        return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

    var device = await _db.Devices.AsNoTracking()
        .FirstOrDefaultAsync(d => d.Id == deviceId, ct);
    if (device is null) return NotFound();
    // tenant query filter already applied; if not visible, NotFound is correct.

    var query = _db.DeviceVulnerabilityExposures.AsNoTracking()
        .Where(e => e.DeviceId == deviceId);
    var total = await query.CountAsync(ct);

    var items = await query
        .OrderByDescending(e => e.Status == ExposureStatus.Open)
        .ThenByDescending(e => e.LastObservedAt)
        .Skip(pagination.Skip)
        .Take(pagination.BoundedPageSize)
        .Select(e => new DeviceExposureDto(
            e.Id,
            e.VulnerabilityId,
            e.Vulnerability.ExternalId,
            e.Vulnerability.Title,
            e.Vulnerability.VendorSeverity.ToString(),
            e.MatchedVersion,
            e.MatchSource.ToString(),
            e.Status.ToString(),
            e.FirstObservedAt,
            e.LastObservedAt,
            e.ResolvedAt,
            _db.ExposureAssessments
                .Where(a => a.DeviceVulnerabilityExposureId == e.Id)
                .Select(a => (decimal?)a.EnvironmentalCvss)
                .FirstOrDefault()))
        .ToListAsync(ct);

    return Ok(new PagedResponse<DeviceExposureDto>(items, total, pagination.Page, pagination.BoundedPageSize));
}
```

```csharp
public record DeviceExposureDto(
    Guid ExposureId,
    Guid VulnerabilityId,
    string ExternalId,
    string Title,
    string Severity,
    string MatchedVersion,
    string MatchSource,
    string Status,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? ResolvedAt,
    decimal? EnvironmentalCvss);
```

- [ ] **Step 2: Backend test**

```csharp
[Fact]
public async Task ListExposures_returns_tenant_scoped_exposures_for_device()
{
    // Seed two tenants each with one device and one exposure.
    // Assert tenant A sees only tenant A's exposure for tenant A's device.
    // Assert tenant A gets 404 when requesting tenant B's device ID.
}
```

- [ ] **Step 3: Frontend Exposures tab**

Add an `Exposures` tab on the device detail route. Render columns: CVE, Severity, Matched version, Status, Environmental CVSS, First observed, Last observed. Empty state: "No exposures observed for this device."

- [ ] **Step 4: Frontend test**

```ts
it('renders exposures list with environmental cvss column', async () => {
  mockApi({ items: [{ exposureId: 'e1', externalId: 'CVE-2026-1', title: 'T', severity: 'High', matchedVersion: '1.0', matchSource: 'Product', status: 'Open', environmentalCvss: 8.2, firstObservedAt: '...', lastObservedAt: '...' }] });
  render(<DeviceDetail id="dev-1" initialTab="exposures" />);
  expect(await screen.findByText('CVE-2026-1')).toBeInTheDocument();
  expect(screen.getByText('8.2')).toBeInTheDocument();
});
```

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Api/Controllers/DevicesController.cs \
        src/PatchHound.Api/Models/DeviceExposureDto.cs \
        web/src/routes/devices/$deviceId.tsx \
        web/src/routes/devices/__tests__/DeviceDetail.exposures.test.tsx \
        tests/PatchHound.Api.Tests/Controllers/DeviceExposuresControllerTests.cs
git commit -m "feat(phase-3): add device exposures tab and /api/devices/{id}/exposures endpoint"
```

---

## Task 11: Frontend — vulnerability list + detail show affected devices

**Files:**
- Modify: `web/src/routes/vulnerabilities/index.tsx`
- Modify: `web/src/routes/vulnerabilities/$vulnerabilityId.tsx`
- Modify: `web/src/api/types/vulnerabilities.ts`
- Test: `web/src/routes/vulnerabilities/__tests__/VulnerabilitiesList.phase3.test.tsx`
- Test: `web/src/routes/vulnerabilities/__tests__/VulnerabilityDetail.phase3.test.tsx`

- [ ] **Step 1: Remove the Phase 2 "Exposure data pending" badge**

List rows now unconditionally show `Affected: {affectedDeviceCount} devices`.

- [ ] **Step 2: Detail — populate Affected Devices tab**

The tab renders a table of `AffectedDeviceDto` items with columns: Device, Product, Matched version, Status, Environmental CVSS (reason on hover), First observed, Last observed. An `Episodes` sub-tab lists active and resolved episodes.

- [ ] **Step 3: Tests**

```ts
it('shows affected devices count when exposureDataAvailable is true', async () => { /* ... */ });
it('renders affected devices table in vulnerability detail', async () => { /* ... */ });
```

Run: `cd web && npm test -- --run vulnerabilities.phase3` — PASS.

- [ ] **Step 4: Commit**

```bash
git add web/src/routes/vulnerabilities/ web/src/api/types/vulnerabilities.ts
git commit -m "feat(phase-3): vulnerability UI renders affected devices and episodes"
```

---

## Task 12: Extend `TenantIsolationEndToEndTests` with Phase 3 assertions

**Files:**
- Modify: `tests/PatchHound.Api.Tests/EndToEnd/TenantIsolationEndToEndTests.cs`

- [ ] **Step 1: Add Phase 3 assertions**

```csharp
[Fact]
public async Task Phase3_vulnerability_detail_scopes_affected_devices_per_tenant()
{
    await using var app = await TestApiFactory.CreateAsync();

    // Seed a global vuln + two tenants each with their own devices + exposures on the same vuln.
    var vulnId = await SeedGlobalVulnerabilityAsync(app);
    await SeedTenantExposureAsync(app, TenantAId, vulnId, deviceName: "A1");
    await SeedTenantExposureAsync(app, TenantBId, vulnId, deviceName: "B1");

    var clientA = app.CreateAuthenticatedClient(TenantAId);
    var detailA = await clientA.GetFromJsonAsync<VulnerabilityDetailDto>($"/api/vulnerabilities/{vulnId}");
    Assert.Single(detailA!.Exposures.AffectedDevices);
    Assert.Equal("A1", detailA.Exposures.AffectedDevices[0].DeviceName);

    var clientB = app.CreateAuthenticatedClient(TenantBId);
    var detailB = await clientB.GetFromJsonAsync<VulnerabilityDetailDto>($"/api/vulnerabilities/{vulnId}");
    Assert.Single(detailB!.Exposures.AffectedDevices);
    Assert.Equal("B1", detailB.Exposures.AffectedDevices[0].DeviceName);
}

[Fact]
public async Task Phase3_device_exposures_endpoint_returns_404_for_cross_tenant_device()
{
    await using var app = await TestApiFactory.CreateAsync();
    var tenantBDeviceId = await SeedTenantDeviceAsync(app, TenantBId);
    var clientA = app.CreateAuthenticatedClient(TenantAId);
    var resp = await clientA.GetAsync($"/api/devices/{tenantBDeviceId}/exposures");
    Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
}

[Fact]
public async Task Phase3_ExposureEpisodes_and_ExposureAssessments_are_tenant_filtered_at_db_level()
{
    await using var app = await TestApiFactory.CreateAsync();
    // Seed tenant A + B exposures; use a request-scoped context for tenant A.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
    SetTenantContext(scope, TenantAId);

    var visibleExposures = await db.DeviceVulnerabilityExposures.ToListAsync();
    Assert.All(visibleExposures, e => Assert.Equal(TenantAId, e.TenantId));

    var visibleEpisodes = await db.ExposureEpisodes.ToListAsync();
    Assert.All(visibleEpisodes, e => Assert.Equal(TenantAId, e.TenantId));

    var visibleAssessments = await db.ExposureAssessments.ToListAsync();
    Assert.All(visibleAssessments, a => Assert.Equal(TenantAId, a.TenantId));
}
```

- [ ] **Step 2: Run — PASS**

- [ ] **Step 3: Commit**

```bash
git add tests/PatchHound.Api.Tests/EndToEnd/TenantIsolationEndToEndTests.cs
git commit -m "test(phase-3): extend tenant isolation e2e with canonical exposure assertions"
```

---

## Task 13: Ingestion idempotency + source collision tests (spec §7 gates)

**Files:**
- Test: `tests/PatchHound.Infrastructure.Tests/Services/ExposureIdempotencyTests.cs`

- [ ] **Step 1: Idempotency test**

```csharp
[Fact]
public async Task Re_running_ingestion_does_not_duplicate_exposures()
{
    await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
    // Seed one device, one installed, one applicable vulnerability
    // ...
    var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);

    await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
    await db.SaveChangesAsync();
    await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
    await db.SaveChangesAsync();
    await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
    await db.SaveChangesAsync();

    Assert.Single(await db.DeviceVulnerabilityExposures.ToListAsync());
}

[Fact]
public async Task Source_collision_same_external_id_different_source_produces_two_devices_and_two_exposures()
{
    await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
    var srcA = SourceSystem.Create("defender", "Defender", true);
    var srcB = SourceSystem.Create("tanium", "Tanium", true);
    db.SourceSystems.AddRange(srcA, srcB);

    var deviceA = Device.Create(tenantId, srcA.Id, "same-external-id", "Defender Device");
    var deviceB = Device.Create(tenantId, srcB.Id, "same-external-id", "Tanium Device");
    db.Devices.AddRange(deviceA, deviceB);

    var product = SoftwareProduct.Create("acme:widget", "Widget", "Acme", null);
    db.SoftwareProducts.Add(product);
    var vuln = Vulnerability.Create("nvd", "CVE-2026-COLL", "t", "d", Severity.High, 7m, "v", DateTimeOffset.UtcNow);
    db.Vulnerabilities.Add(vuln);
    db.VulnerabilityApplicabilities.Add(VulnerabilityApplicability.Create(vuln.Id, product.Id, null, true, null, null, null, null));

    db.InstalledSoftware.Add(InstalledSoftware.Observe(tenantId, deviceA.Id, product.Id, srcA.Id, "1.0", DateTimeOffset.UtcNow));
    db.InstalledSoftware.Add(InstalledSoftware.Observe(tenantId, deviceB.Id, product.Id, srcB.Id, "1.0", DateTimeOffset.UtcNow));
    await db.SaveChangesAsync();

    var svc = new ExposureDerivationService(db, NullLogger<ExposureDerivationService>.Instance);
    await svc.DeriveForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
    await db.SaveChangesAsync();

    var exposures = await db.DeviceVulnerabilityExposures.ToListAsync();
    Assert.Equal(2, exposures.Count);
    Assert.Equal(2, exposures.Select(e => e.DeviceId).Distinct().Count());
}
```

- [ ] **Step 2: Run — PASS**

- [ ] **Step 3: Commit**

```bash
git add tests/PatchHound.Infrastructure.Tests/Services/ExposureIdempotencyTests.cs
git commit -m "test(phase-3): exposure idempotency + source collision gates (spec §7)"
```

---

## Task 14: Grep sweep

- [ ] **Step 1: Ensure no legacy references survive**

```bash
grep -rn "VulnerabilityAsset" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "VulnerabilityEpisodeRiskAssessment" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "TenantVulnerabilityId" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "SoftwareVulnerabilityMatch" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "IgnoreQueryFilters" src/ --include="*.cs" | grep -v "Migrations/"
```

All expected to return zero hits (except `IgnoreQueryFilters` in the documented allow-list — Phase 3 adds no new entries).

- [ ] **Step 2: Verify no new `IgnoreQueryFilters()` in Phase 3 services**

The exposure services (derivation, episode, assessment) all run under tenant context and write tenant-scoped rows; they must **not** call `IgnoreQueryFilters()`. Confirm by grep above.

- [ ] **Step 3: If clean, no commit needed**

---

## Task 15: Final build, tests, PR

- [ ] **Step 1: Clean build + full suite green**

```bash
dotnet clean && dotnet build --no-incremental
dotnet test
cd web && npm run typecheck && npm test -- --run && cd ..
```

- [ ] **Step 2: Phase exit checklist**

- [ ] `dotnet build` clean
- [ ] `dotnet test` green
- [ ] `npm run typecheck` clean
- [ ] `npm test` green
- [ ] Env-severity hard-gate test (`EnvironmentalSeverityHardGateTests`) green
- [ ] Idempotency + source-collision tests green
- [ ] `TenantIsolationEndToEndTests` extended with 3 new exposure assertions
- [ ] Grep sweep clean
- [ ] PR description includes: "Entities added: `DeviceVulnerabilityExposure` (tenant-scoped, filter=yes), `ExposureEpisode` (tenant-scoped, filter=yes), `ExposureAssessment` (tenant-scoped, filter=yes)"
- [ ] PR description notes: "Phase 2 gap closed — vulnerability list/detail now returns `ExposureDataAvailable = true` and populates affected devices"
- [ ] PR description re-states the `IgnoreQueryFilters()` allow-list — unchanged from Phase 2

- [ ] **Step 3: PR**

```bash
git push -u origin data-model-canonical-cleanup-phase-3
gh pr create --title "Data model canonical cleanup — phase 3: canonical exposure" --body "$(cat <<'EOF'
## Summary
- Introduces `DeviceVulnerabilityExposure`, `ExposureEpisode`, and `ExposureAssessment` tenant-scoped entities.
- `ExposureDerivationService` matches `InstalledSoftware` × `VulnerabilityApplicability` (product-keyed first, CPE fallback).
- `ExposureEpisodeService` manages open/close/reopen lifecycle.
- `ExposureAssessmentService` computes environmental CVSS via `Device.SecurityProfileId → SecurityProfile`.
- Closes the Phase 2 "affected devices" gap in vulnerability list/detail.
- Adds the device exposures tab.

## Entities added (all tenant-scoped, direct `TenantId`, EF query filter yes)
| Entity | Rule | Justification |
|---|---|---|
| `DeviceVulnerabilityExposure` | §4.10 R1/R2 | Tenant exposure current-state row |
| `ExposureEpisode` | §4.10 R1/R2 | Tenant exposure lifecycle history |
| `ExposureAssessment` | §4.10 R1/R2 | Tenant environmental severity calculation |

## Hard gates
- `EnvironmentalSeverityHardGateTests` proves `SecurityProfile` modifiers move `ExposureAssessment.EnvironmentalCvss` away from the base CVSS — blocking.
- `ExposureIdempotencyTests` proves repeated derivation does not duplicate rows.
- Source-collision test proves two sources producing the same external device ID result in two `Device` + two `DeviceVulnerabilityExposure` rows.

## `IgnoreQueryFilters()` allow-list
Unchanged from Phase 2. Phase 3 services run under tenant context.

## Test plan
- [ ] `dotnet build` clean
- [ ] `dotnet test` full suite green (includes `EnvironmentalSeverityHardGateTests`)
- [ ] `cd web && npm run typecheck && npm test -- --run` green
- [ ] `TenantIsolationEndToEndTests` passes with Phase 3 assertions

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```
