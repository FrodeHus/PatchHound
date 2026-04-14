# Data Model Canonical Cleanup — Phase 2: Canonical Vulnerability Knowledge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the legacy `VulnerabilityDefinition*` / `TenantVulnerability` / `VulnerabilityThreatAssessment` chain with canonical global `Vulnerability`, `VulnerabilityReference`, `VulnerabilityApplicability`, and `ThreatAssessment` entities, rewrite vulnerability ingestion and the read controller, and delete the entire legacy vulnerability-knowledge + exposure-asset chain.

**Architecture:** All four canonical entities are **global** (no `TenantId`, no EF query filter). Vulnerability ingestion (`DefenderVulnerabilitySource`, `NvdVulnerabilityEnrichmentRunner`, `DefenderVulnerabilityEnrichmentRunner`) resolves vulnerabilities via a new `VulnerabilityResolver` service that upserts by `(Source, ExternalId)` and writes `Vulnerability` + `VulnerabilityReference` + `VulnerabilityApplicability` rows. Threat assessment writes go through a rewritten `ThreatAssessmentService` that upserts `ThreatAssessment` by `VulnerabilityId`. All global writes run under `IsSystemContext = true`. Vulnerability list/detail endpoints return global canonical data with **empty `AffectedDevices`/`ActiveExposures` sections** (a clearly-marked state to be populated in Phase 3).

**Tech Stack:** .NET 10, EF Core 10, xUnit, PostgreSQL, React, TanStack Router, Vitest.

**Scope extension (vs spec §5.2):** The spec lists `VulnerabilityAsset`, `VulnerabilityAssetEpisode`, `VulnerabilityAssetAssessment`, `VulnerabilityEpisodeRiskAssessment`, and `SoftwareVulnerabilityMatch` under Phase 3 deletions. Those rows FK into `TenantVulnerabilityId` / `VulnerabilityDefinitionId`, so deleting `TenantVulnerability` in Phase 2 forces their removal in the same phase. Phase 3 then introduces `DeviceVulnerabilityExposure`, `ExposureEpisode`, `ExposureAssessment` from a clean slate — it does not need to delete anything. This is documented explicitly in the Phase 2 PR description (§Task 17).

---

## Preflight

- [ ] **Step P1: Verify Phase 1 has merged to `main`**

Run: `git log --oneline main -20`
Expected: see a merge commit for Phase 1 ("data-model-canonical-cleanup phase 1: canonical inventory"). Phase 2 branches from that `main`. If Phase 1 is not merged, stop and escalate.

- [ ] **Step P2: Create Phase 2 branch from `main`**

Run:
```bash
git checkout main
git pull origin main
git checkout -b data-model-canonical-cleanup-phase-2
```
Expected: `Switched to a new branch 'data-model-canonical-cleanup-phase-2'`.

- [ ] **Step P3: Drop local dev database**

Run:
```bash
PGPASSWORD=patchhound psql -h localhost -U patchhound -d postgres -c "DROP DATABASE IF EXISTS patchhound;"
PGPASSWORD=patchhound psql -h localhost -U patchhound -d postgres -c "CREATE DATABASE patchhound;"
```
Expected: `DROP DATABASE` + `CREATE DATABASE`. (Same rationale as Phase 1: no migrations until Phase 6; test runs use an in-memory or per-test-run provider.)

- [ ] **Step P4: Baseline build + test**

Run: `dotnet build && dotnet test`
Expected: clean.

Run: `cd web && npm run typecheck && npm test -- --run && cd ..`
Expected: clean.

---

## Task 1: Add `Vulnerability` global entity + configuration

**Files:**
- Create: `src/PatchHound.Core/Entities/Vulnerability.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` (add `DbSet<Vulnerability>`, do **not** add a query filter)
- Test: `tests/PatchHound.Core.Tests/Entities/VulnerabilityTests.cs`

- [x] **Step 1: Write the failing test**

```csharp
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Core.Tests.Entities;

public class VulnerabilityTests
{
    [Fact]
    public void Create_sets_identity_and_defaults()
    {
        var v = Vulnerability.Create(
            source: "microsoft-defender",
            externalId: "CVE-2026-12345",
            title: "Sample CVE",
            description: "Sample description",
            vendorSeverity: Severity.High,
            cvssScore: 7.8m,
            cvssVector: "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
            publishedDate: new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));

        Assert.NotEqual(Guid.Empty, v.Id);
        Assert.Equal("CVE-2026-12345", v.ExternalId);
        Assert.Equal("microsoft-defender", v.Source);
        Assert.Equal(Severity.High, v.VendorSeverity);
        Assert.Equal(7.8m, v.CvssScore);
        Assert.NotEqual(default, v.CreatedAt);
        Assert.Equal(v.CreatedAt, v.UpdatedAt);
    }

    [Fact]
    public void Update_mutates_mutable_fields_and_bumps_UpdatedAt()
    {
        var v = Vulnerability.Create("nvd", "CVE-2026-99999", "old", "old", Severity.Low, 1.0m, "vec", DateTimeOffset.UtcNow);
        var original = v.UpdatedAt;

        Thread.Sleep(5);
        v.Update(
            title: "new",
            description: "new",
            vendorSeverity: Severity.Critical,
            cvssScore: 9.8m,
            cvssVector: "new-vector",
            publishedDate: DateTimeOffset.UtcNow);

        Assert.Equal("new", v.Title);
        Assert.Equal(Severity.Critical, v.VendorSeverity);
        Assert.Equal(9.8m, v.CvssScore);
        Assert.True(v.UpdatedAt > original);
    }
}
```

Run: `dotnet test tests/PatchHound.Core.Tests/PatchHound.Core.Tests.csproj --filter "FullyQualifiedName~VulnerabilityTests" -v minimal`
Expected: FAIL (`Vulnerability` type does not exist).

- [x] **Step 2: Write the entity**

Create `src/PatchHound.Core/Entities/Vulnerability.cs`:

```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class Vulnerability
{
    public Guid Id { get; private set; }
    public string Source { get; private set; } = null!;
    public string ExternalId { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public Severity VendorSeverity { get; private set; }
    public decimal? CvssScore { get; private set; }
    public string? CvssVector { get; private set; }
    public DateTimeOffset? PublishedDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Vulnerability() { }

    public static Vulnerability Create(
        string source,
        string externalId,
        string title,
        string description,
        Severity vendorSeverity,
        decimal? cvssScore,
        string? cvssVector,
        DateTimeOffset? publishedDate)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("source required", nameof(source));
        if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("externalId required", nameof(externalId));
        var now = DateTimeOffset.UtcNow;
        return new Vulnerability
        {
            Id = Guid.NewGuid(),
            Source = source.Trim(),
            ExternalId = externalId.Trim(),
            Title = title ?? string.Empty,
            Description = description ?? string.Empty,
            VendorSeverity = vendorSeverity,
            CvssScore = cvssScore,
            CvssVector = cvssVector,
            PublishedDate = publishedDate,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string title,
        string description,
        Severity vendorSeverity,
        decimal? cvssScore,
        string? cvssVector,
        DateTimeOffset? publishedDate)
    {
        Title = title ?? string.Empty;
        Description = description ?? string.Empty;
        VendorSeverity = vendorSeverity;
        CvssScore = cvssScore;
        CvssVector = cvssVector;
        PublishedDate = publishedDate;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

- [x] **Step 3: Run tests — expect pass**

Run: `dotnet test tests/PatchHound.Core.Tests/PatchHound.Core.Tests.csproj --filter "FullyQualifiedName~VulnerabilityTests" -v minimal`
Expected: PASS.

- [x] **Step 4: Add EF configuration**

Create `src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class VulnerabilityConfiguration : IEntityTypeConfiguration<Vulnerability>
{
    public void Configure(EntityTypeBuilder<Vulnerability> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Source).IsRequired().HasMaxLength(64);
        builder.Property(v => v.ExternalId).IsRequired().HasMaxLength(128);
        builder.Property(v => v.Title).IsRequired().HasMaxLength(512);
        builder.Property(v => v.Description).IsRequired();
        builder.Property(v => v.VendorSeverity).HasConversion<string>().HasMaxLength(32);
        builder.Property(v => v.CvssScore).HasColumnType("numeric(4,2)");
        builder.Property(v => v.CvssVector).HasMaxLength(256);

        builder.HasIndex(v => new { v.Source, v.ExternalId }).IsUnique();
        builder.HasIndex(v => v.ExternalId);
    }
}
```

- [x] **Step 5: Register `DbSet<Vulnerability>` in `PatchHoundDbContext`**

In `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`, add in the canonical vulnerability region (above the legacy `VulnerabilityDefinitions` DbSet for now; the legacy set is removed in Task 16):

```csharp
public DbSet<Vulnerability> Vulnerabilities => Set<Vulnerability>();
```

Do **not** add `modelBuilder.Entity<Vulnerability>().HasQueryFilter(...)` — `Vulnerability` is global per spec §4.10 Rule 3.

Run: `dotnet build`
Expected: 0 errors, 0 warnings.

- [x] **Step 6: Commit**

```bash
git add src/PatchHound.Core/Entities/Vulnerability.cs \
        src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Core.Tests/Entities/VulnerabilityTests.cs
git commit -m "feat(phase-2): add canonical Vulnerability entity (global)"
```

---

## Task 2: Add `VulnerabilityReference` global entity

**Files:**
- Create: `src/PatchHound.Core/Entities/VulnerabilityReference.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityReferenceConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Test: `tests/PatchHound.Core.Tests/Entities/VulnerabilityReferenceTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using PatchHound.Core.Entities;

namespace PatchHound.Core.Tests.Entities;

public class VulnerabilityReferenceTests
{
    [Fact]
    public void Create_trims_and_normalizes_tags()
    {
        var vulnId = Guid.NewGuid();
        var r = VulnerabilityReference.Create(
            vulnId, "https://example.com/advisory ", "nvd", new[] { "advisory", " patch " });

        Assert.Equal(vulnId, r.VulnerabilityId);
        Assert.Equal("https://example.com/advisory", r.Url);
        Assert.Equal("nvd", r.Source);
        Assert.Contains("advisory", r.GetTags());
        Assert.Contains("patch", r.GetTags());
    }
}
```

Run: `dotnet test --filter "FullyQualifiedName~VulnerabilityReferenceTests"` — FAIL.

- [ ] **Step 2: Create entity**

```csharp
namespace PatchHound.Core.Entities;

public class VulnerabilityReference
{
    public Guid Id { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public string Url { get; private set; } = null!;
    public string Source { get; private set; } = null!;
    public string Tags { get; private set; } = string.Empty;

    public Vulnerability Vulnerability { get; private set; } = null!;

    private VulnerabilityReference() { }

    public static VulnerabilityReference Create(
        Guid vulnerabilityId,
        string url,
        string source,
        IReadOnlyList<string> tags)
    {
        if (vulnerabilityId == Guid.Empty) throw new ArgumentException(nameof(vulnerabilityId));
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException(nameof(url));
        return new VulnerabilityReference
        {
            Id = Guid.NewGuid(),
            VulnerabilityId = vulnerabilityId,
            Url = url.Trim(),
            Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source.Trim(),
            Tags = SerializeTags(tags),
        };
    }

    public IReadOnlyList<string> GetTags() =>
        Tags.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string SerializeTags(IReadOnlyList<string> tags) =>
        string.Join("|",
            tags.Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
}
```

- [ ] **Step 3: Test passes**

Run: `dotnet test --filter "FullyQualifiedName~VulnerabilityReferenceTests"` — PASS.

- [ ] **Step 4: EF configuration**

`src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityReferenceConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class VulnerabilityReferenceConfiguration : IEntityTypeConfiguration<VulnerabilityReference>
{
    public void Configure(EntityTypeBuilder<VulnerabilityReference> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Url).IsRequired().HasMaxLength(1024);
        builder.Property(r => r.Source).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Tags).HasMaxLength(512);

        builder.HasOne(r => r.Vulnerability)
            .WithMany()
            .HasForeignKey(r => r.VulnerabilityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.VulnerabilityId, r.Url }).IsUnique();
    }
}
```

Add `public DbSet<VulnerabilityReference> VulnerabilityReferences => Set<VulnerabilityReference>();` to `PatchHoundDbContext` (no query filter).

Run: `dotnet build` — clean.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Core/Entities/VulnerabilityReference.cs \
        src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityReferenceConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Core.Tests/Entities/VulnerabilityReferenceTests.cs
git commit -m "feat(phase-2): add canonical VulnerabilityReference entity (global)"
```

---

## Task 3: Add `VulnerabilityApplicability` global entity

Applicability represents "this vulnerability affects this product", keyed by `SoftwareProductId` (introduced in Phase 1) or by CPE criteria. Version ranges follow CPE semantics (`VersionStartIncluding`, `VersionStartExcluding`, `VersionEndIncluding`, `VersionEndExcluding`).

**Files:**
- Create: `src/PatchHound.Core/Entities/VulnerabilityApplicability.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityApplicabilityConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Test: `tests/PatchHound.Core.Tests/Entities/VulnerabilityApplicabilityTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using PatchHound.Core.Entities;

namespace PatchHound.Core.Tests.Entities;

public class VulnerabilityApplicabilityTests
{
    [Fact]
    public void Create_keyed_on_software_product_is_valid()
    {
        var vulnId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var a = VulnerabilityApplicability.Create(
            vulnerabilityId: vulnId,
            softwareProductId: productId,
            cpeCriteria: null,
            vulnerable: true,
            versionStartIncluding: "1.0.0",
            versionStartExcluding: null,
            versionEndIncluding: null,
            versionEndExcluding: "2.0.0");

        Assert.Equal(vulnId, a.VulnerabilityId);
        Assert.Equal(productId, a.SoftwareProductId);
        Assert.Null(a.CpeCriteria);
        Assert.True(a.Vulnerable);
        Assert.Equal("1.0.0", a.VersionStartIncluding);
        Assert.Equal("2.0.0", a.VersionEndExcluding);
    }

    [Fact]
    public void Create_keyed_on_cpe_criteria_is_valid()
    {
        var vulnId = Guid.NewGuid();
        var a = VulnerabilityApplicability.Create(
            vulnerabilityId: vulnId,
            softwareProductId: null,
            cpeCriteria: "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*",
            vulnerable: true,
            versionStartIncluding: null,
            versionStartExcluding: null,
            versionEndIncluding: "3.0.0",
            versionEndExcluding: null);

        Assert.Null(a.SoftwareProductId);
        Assert.Equal("cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*", a.CpeCriteria);
    }

    [Fact]
    public void Create_requires_product_or_cpe()
    {
        Assert.Throws<ArgumentException>(() => VulnerabilityApplicability.Create(
            Guid.NewGuid(), null, null, true, null, null, null, null));
    }
}
```

Run: FAIL.

- [ ] **Step 2: Create entity**

```csharp
namespace PatchHound.Core.Entities;

public class VulnerabilityApplicability
{
    public Guid Id { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public Guid? SoftwareProductId { get; private set; }
    public string? CpeCriteria { get; private set; }
    public bool Vulnerable { get; private set; }
    public string? VersionStartIncluding { get; private set; }
    public string? VersionStartExcluding { get; private set; }
    public string? VersionEndIncluding { get; private set; }
    public string? VersionEndExcluding { get; private set; }

    public Vulnerability Vulnerability { get; private set; } = null!;
    public SoftwareProduct? SoftwareProduct { get; private set; }

    private VulnerabilityApplicability() { }

    public static VulnerabilityApplicability Create(
        Guid vulnerabilityId,
        Guid? softwareProductId,
        string? cpeCriteria,
        bool vulnerable,
        string? versionStartIncluding,
        string? versionStartExcluding,
        string? versionEndIncluding,
        string? versionEndExcluding)
    {
        if (vulnerabilityId == Guid.Empty) throw new ArgumentException(nameof(vulnerabilityId));
        if (softwareProductId is null && string.IsNullOrWhiteSpace(cpeCriteria))
            throw new ArgumentException("Either softwareProductId or cpeCriteria must be provided");

        return new VulnerabilityApplicability
        {
            Id = Guid.NewGuid(),
            VulnerabilityId = vulnerabilityId,
            SoftwareProductId = softwareProductId,
            CpeCriteria = string.IsNullOrWhiteSpace(cpeCriteria) ? null : cpeCriteria.Trim(),
            Vulnerable = vulnerable,
            VersionStartIncluding = versionStartIncluding,
            VersionStartExcluding = versionStartExcluding,
            VersionEndIncluding = versionEndIncluding,
            VersionEndExcluding = versionEndExcluding,
        };
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

public class VulnerabilityApplicabilityConfiguration : IEntityTypeConfiguration<VulnerabilityApplicability>
{
    public void Configure(EntityTypeBuilder<VulnerabilityApplicability> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.CpeCriteria).HasMaxLength(512);
        builder.Property(a => a.VersionStartIncluding).HasMaxLength(128);
        builder.Property(a => a.VersionStartExcluding).HasMaxLength(128);
        builder.Property(a => a.VersionEndIncluding).HasMaxLength(128);
        builder.Property(a => a.VersionEndExcluding).HasMaxLength(128);

        builder.HasOne(a => a.Vulnerability)
            .WithMany()
            .HasForeignKey(a => a.VulnerabilityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.SoftwareProduct)
            .WithMany()
            .HasForeignKey(a => a.SoftwareProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.VulnerabilityId, a.SoftwareProductId });
        builder.HasIndex(a => new { a.VulnerabilityId, a.CpeCriteria });
    }
}
```

Add `public DbSet<VulnerabilityApplicability> VulnerabilityApplicabilities => Set<VulnerabilityApplicability>();` to `PatchHoundDbContext` (no query filter).

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Core/Entities/VulnerabilityApplicability.cs \
        src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityApplicabilityConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Core.Tests/Entities/VulnerabilityApplicabilityTests.cs
git commit -m "feat(phase-2): add canonical VulnerabilityApplicability entity (global)"
```

---

## Task 4: Add `ThreatAssessment` global entity

**Files:**
- Create: `src/PatchHound.Core/Entities/ThreatAssessment.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/ThreatAssessmentConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Test: `tests/PatchHound.Core.Tests/Entities/ThreatAssessmentTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using PatchHound.Core.Entities;

namespace PatchHound.Core.Tests.Entities;

public class ThreatAssessmentTests
{
    [Fact]
    public void Create_sets_all_factors_and_timestamps()
    {
        var vulnId = Guid.NewGuid();
        var a = ThreatAssessment.Create(
            vulnerabilityId: vulnId,
            threatScore: 8.1m,
            technicalScore: 7.5m,
            exploitLikelihoodScore: 0.7m,
            threatActivityScore: 6.2m,
            epssScore: 0.33m,
            knownExploited: true,
            publicExploit: true,
            activeAlert: false,
            hasRansomwareAssociation: true,
            hasMalwareAssociation: false,
            factorsJson: "[]",
            calculationVersion: "v2");

        Assert.Equal(vulnId, a.VulnerabilityId);
        Assert.Equal(8.1m, a.ThreatScore);
        Assert.True(a.KnownExploited);
        Assert.False(a.ActiveAlert);
        Assert.Equal("v2", a.CalculationVersion);
        Assert.NotEqual(default, a.CalculatedAt);
    }

    [Fact]
    public void Update_refreshes_factors_and_CalculatedAt()
    {
        var a = ThreatAssessment.Create(Guid.NewGuid(), 1, 1, 0, 1, null, false, false, false, false, false, "[]", "v1");
        var original = a.CalculatedAt;
        Thread.Sleep(5);
        a.Update(9, 9, 1, 9, 0.99m, true, true, true, true, true, "[\"kev\"]", "v2");
        Assert.Equal(9, a.ThreatScore);
        Assert.True(a.CalculatedAt > original);
    }
}
```

- [ ] **Step 2: Create entity** (copy shape from `VulnerabilityThreatAssessment` but FK is `VulnerabilityId`, not `VulnerabilityDefinitionId`)

```csharp
namespace PatchHound.Core.Entities;

public class ThreatAssessment
{
    public Guid Id { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public decimal ThreatScore { get; private set; }
    public decimal TechnicalScore { get; private set; }
    public decimal ExploitLikelihoodScore { get; private set; }
    public decimal ThreatActivityScore { get; private set; }
    public decimal? EpssScore { get; private set; }
    public bool KnownExploited { get; private set; }
    public bool PublicExploit { get; private set; }
    public bool ActiveAlert { get; private set; }
    public bool HasRansomwareAssociation { get; private set; }
    public bool HasMalwareAssociation { get; private set; }
    public DateTimeOffset? DefenderLastRefreshedAt { get; private set; }
    public string FactorsJson { get; private set; } = "[]";
    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }

    public Vulnerability Vulnerability { get; private set; } = null!;

    private ThreatAssessment() { }

    public static ThreatAssessment Create(
        Guid vulnerabilityId,
        decimal threatScore,
        decimal technicalScore,
        decimal exploitLikelihoodScore,
        decimal threatActivityScore,
        decimal? epssScore,
        bool knownExploited,
        bool publicExploit,
        bool activeAlert,
        bool hasRansomwareAssociation,
        bool hasMalwareAssociation,
        string factorsJson,
        string calculationVersion)
    {
        return new ThreatAssessment
        {
            Id = Guid.NewGuid(),
            VulnerabilityId = vulnerabilityId,
            ThreatScore = threatScore,
            TechnicalScore = technicalScore,
            ExploitLikelihoodScore = exploitLikelihoodScore,
            ThreatActivityScore = threatActivityScore,
            EpssScore = epssScore,
            KnownExploited = knownExploited,
            PublicExploit = publicExploit,
            ActiveAlert = activeAlert,
            HasRansomwareAssociation = hasRansomwareAssociation,
            HasMalwareAssociation = hasMalwareAssociation,
            FactorsJson = factorsJson,
            CalculationVersion = calculationVersion,
            CalculatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        decimal threatScore,
        decimal technicalScore,
        decimal exploitLikelihoodScore,
        decimal threatActivityScore,
        decimal? epssScore,
        bool knownExploited,
        bool publicExploit,
        bool activeAlert,
        bool hasRansomwareAssociation,
        bool hasMalwareAssociation,
        string factorsJson,
        string calculationVersion)
    {
        ThreatScore = threatScore;
        TechnicalScore = technicalScore;
        ExploitLikelihoodScore = exploitLikelihoodScore;
        ThreatActivityScore = threatActivityScore;
        EpssScore = epssScore;
        KnownExploited = knownExploited;
        PublicExploit = publicExploit;
        ActiveAlert = activeAlert;
        HasRansomwareAssociation = hasRansomwareAssociation;
        HasMalwareAssociation = hasMalwareAssociation;
        FactorsJson = factorsJson;
        CalculationVersion = calculationVersion;
        CalculatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkDefenderRefreshed(DateTimeOffset refreshedAt) =>
        DefenderLastRefreshedAt = refreshedAt;
}
```

- [ ] **Step 3: Test passes**

- [ ] **Step 4: EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ThreatAssessmentConfiguration : IEntityTypeConfiguration<ThreatAssessment>
{
    public void Configure(EntityTypeBuilder<ThreatAssessment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ThreatScore).HasColumnType("numeric(4,2)");
        builder.Property(a => a.TechnicalScore).HasColumnType("numeric(4,2)");
        builder.Property(a => a.ExploitLikelihoodScore).HasColumnType("numeric(4,3)");
        builder.Property(a => a.ThreatActivityScore).HasColumnType("numeric(4,2)");
        builder.Property(a => a.EpssScore).HasColumnType("numeric(5,4)");
        builder.Property(a => a.FactorsJson).IsRequired();
        builder.Property(a => a.CalculationVersion).IsRequired().HasMaxLength(32);

        builder.HasOne(a => a.Vulnerability)
            .WithMany()
            .HasForeignKey(a => a.VulnerabilityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.VulnerabilityId).IsUnique();
    }
}
```

Add `public DbSet<ThreatAssessment> ThreatAssessments => Set<ThreatAssessment>();` (no query filter).

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Core/Entities/ThreatAssessment.cs \
        src/PatchHound.Infrastructure/Data/Configurations/ThreatAssessmentConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Core.Tests/Entities/ThreatAssessmentTests.cs
git commit -m "feat(phase-2): add canonical ThreatAssessment entity (global)"
```

---

## Task 5: `VulnerabilityResolver` service

System-context service that upserts a `Vulnerability` by `(Source, ExternalId)`, replaces its references, and replaces its applicabilities. Called by ingestion under `IsSystemContext = true`.

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/VulnerabilityResolver.cs`
- Create: `src/PatchHound.Core/Models/VulnerabilityResolveInput.cs`
- Test: `tests/PatchHound.Infrastructure.Tests/Services/VulnerabilityResolverTests.cs`

- [ ] **Step 1: Define resolve input model**

`src/PatchHound.Core/Models/VulnerabilityResolveInput.cs`:

```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Models;

public record VulnerabilityResolveInput(
    string Source,
    string ExternalId,
    string Title,
    string Description,
    Severity VendorSeverity,
    decimal? CvssScore,
    string? CvssVector,
    DateTimeOffset? PublishedDate,
    IReadOnlyList<VulnerabilityReferenceInput> References,
    IReadOnlyList<VulnerabilityApplicabilityInput> Applicabilities);

public record VulnerabilityReferenceInput(string Url, string Source, IReadOnlyList<string> Tags);

public record VulnerabilityApplicabilityInput(
    Guid? SoftwareProductId,
    string? CpeCriteria,
    bool Vulnerable,
    string? VersionStartIncluding,
    string? VersionStartExcluding,
    string? VersionEndIncluding,
    string? VersionEndExcluding);
```

- [ ] **Step 2: Write failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure.Testing;

namespace PatchHound.Tests.Infrastructure.Services;

public class VulnerabilityResolverTests
{
    [Fact]
    public async Task Resolves_new_vulnerability_inserts_references_and_applicabilities()
    {
        await using var db = TestDbContextFactory.CreateSystemContext();
        var resolver = new VulnerabilityResolver(db);

        var input = new VulnerabilityResolveInput(
            Source: "nvd",
            ExternalId: "CVE-2026-0001",
            Title: "Example",
            Description: "desc",
            VendorSeverity: Severity.High,
            CvssScore: 7.5m,
            CvssVector: "vec",
            PublishedDate: DateTimeOffset.UtcNow,
            References: new[] {
                new VulnerabilityReferenceInput("https://example.com/a", "nvd", new[] { "advisory" })
            },
            Applicabilities: new[] {
                new VulnerabilityApplicabilityInput(null, "cpe:2.3:a:acme:*:*:*:*:*:*:*:*:*", true, null, null, "1.0.0", null)
            });

        var v = await resolver.ResolveAsync(input, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.NotEqual(Guid.Empty, v.Id);
        Assert.Equal("CVE-2026-0001", v.ExternalId);
        Assert.Single(await db.VulnerabilityReferences.ToListAsync());
        Assert.Single(await db.VulnerabilityApplicabilities.ToListAsync());
    }

    [Fact]
    public async Task Resolves_existing_vulnerability_updates_fields_and_replaces_children()
    {
        await using var db = TestDbContextFactory.CreateSystemContext();
        var resolver = new VulnerabilityResolver(db);

        var first = new VulnerabilityResolveInput(
            "nvd", "CVE-2026-0002", "old", "old", Severity.Low, 1m, "v1", DateTimeOffset.UtcNow,
            new[] { new VulnerabilityReferenceInput("https://old", "nvd", Array.Empty<string>()) },
            new[] { new VulnerabilityApplicabilityInput(null, "old-cpe", true, null, null, null, null) });
        await resolver.ResolveAsync(first, CancellationToken.None);
        await db.SaveChangesAsync();

        var second = new VulnerabilityResolveInput(
            "nvd", "CVE-2026-0002", "new", "new", Severity.Critical, 9.5m, "v2", DateTimeOffset.UtcNow,
            new[] { new VulnerabilityReferenceInput("https://new", "nvd", Array.Empty<string>()) },
            new[] { new VulnerabilityApplicabilityInput(null, "new-cpe", true, null, null, null, null) });
        var updated = await resolver.ResolveAsync(second, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal("new", updated.Title);
        Assert.Equal(Severity.Critical, updated.VendorSeverity);
        var refs = await db.VulnerabilityReferences.Where(r => r.VulnerabilityId == updated.Id).ToListAsync();
        Assert.Single(refs);
        Assert.Equal("https://new", refs[0].Url);
        var apps = await db.VulnerabilityApplicabilities.Where(a => a.VulnerabilityId == updated.Id).ToListAsync();
        Assert.Single(apps);
        Assert.Equal("new-cpe", apps[0].CpeCriteria);
    }
}
```

> **If `TestDbContextFactory` does not exist yet**, add it in `tests/PatchHound.Tests/Infrastructure/Testing/TestDbContextFactory.cs` — see Phase 1 Task 7 for the canonical implementation. `CreateSystemContext()` returns a `PatchHoundDbContext` with `IsSystemContext = true` against an in-memory provider.

Run: `dotnet test --filter "FullyQualifiedName~VulnerabilityResolverTests"` — FAIL.

- [ ] **Step 3: Implement resolver**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class VulnerabilityResolver
{
    private readonly PatchHoundDbContext _db;

    public VulnerabilityResolver(PatchHoundDbContext db)
    {
        _db = db;
    }

    public async Task<Vulnerability> ResolveAsync(VulnerabilityResolveInput input, CancellationToken ct)
    {
        var existing = await _db.Vulnerabilities
            .FirstOrDefaultAsync(v => v.Source == input.Source && v.ExternalId == input.ExternalId, ct);

        Vulnerability vuln;
        if (existing is null)
        {
            vuln = Vulnerability.Create(
                input.Source, input.ExternalId, input.Title, input.Description,
                input.VendorSeverity, input.CvssScore, input.CvssVector, input.PublishedDate);
            _db.Vulnerabilities.Add(vuln);
        }
        else
        {
            existing.Update(input.Title, input.Description, input.VendorSeverity,
                input.CvssScore, input.CvssVector, input.PublishedDate);
            vuln = existing;
        }

        // Replace references (delete + insert)
        var existingRefs = await _db.VulnerabilityReferences
            .Where(r => r.VulnerabilityId == vuln.Id).ToListAsync(ct);
        _db.VulnerabilityReferences.RemoveRange(existingRefs);
        foreach (var r in input.References
                     .Where(r => !string.IsNullOrWhiteSpace(r.Url))
                     .DistinctBy(r => r.Url.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            _db.VulnerabilityReferences.Add(
                VulnerabilityReference.Create(vuln.Id, r.Url, r.Source, r.Tags));
        }

        // Replace applicabilities
        var existingApps = await _db.VulnerabilityApplicabilities
            .Where(a => a.VulnerabilityId == vuln.Id).ToListAsync(ct);
        _db.VulnerabilityApplicabilities.RemoveRange(existingApps);
        foreach (var a in input.Applicabilities)
        {
            _db.VulnerabilityApplicabilities.Add(
                VulnerabilityApplicability.Create(
                    vuln.Id, a.SoftwareProductId, a.CpeCriteria, a.Vulnerable,
                    a.VersionStartIncluding, a.VersionStartExcluding,
                    a.VersionEndIncluding, a.VersionEndExcluding));
        }

        return vuln;
    }
}
```

- [ ] **Step 4: Tests pass**

Run: `dotnet test --filter "FullyQualifiedName~VulnerabilityResolverTests"` — PASS.

- [ ] **Step 5: Register in DI**

In `src/PatchHound.Infrastructure/DependencyInjection.cs`, add:

```csharp
services.AddScoped<VulnerabilityResolver>();
```

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Core/Models/VulnerabilityResolveInput.cs \
        src/PatchHound.Infrastructure/Services/VulnerabilityResolver.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        tests/PatchHound.Infrastructure.Tests/Services/VulnerabilityResolverTests.cs
git commit -m "feat(phase-2): add VulnerabilityResolver system-context upsert service"
```

---

## Task 6: `ThreatAssessmentService` — upsert global threat assessment

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/ThreatAssessmentService.cs`
- Test: `tests/PatchHound.Infrastructure.Tests/Services/ThreatAssessmentServiceTests.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Write failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure.Testing;

namespace PatchHound.Tests.Infrastructure.Services;

public class ThreatAssessmentServiceTests
{
    [Fact]
    public async Task Upserts_new_assessment_for_vulnerability()
    {
        await using var db = TestDbContextFactory.CreateSystemContext();
        var v = Vulnerability.Create("nvd", "CVE-2026-1000", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(v);
        await db.SaveChangesAsync();

        var svc = new ThreatAssessmentService(db);
        var a = await svc.UpsertAsync(
            v.Id,
            threatScore: 7.8m, technicalScore: 7.5m, exploitLikelihoodScore: 0.6m, threatActivityScore: 6.0m,
            epssScore: 0.2m, knownExploited: false, publicExploit: true, activeAlert: false,
            hasRansomwareAssociation: false, hasMalwareAssociation: false,
            factorsJson: "[]", calculationVersion: "v1",
            ct: CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(v.Id, a.VulnerabilityId);
        Assert.Single(await db.ThreatAssessments.ToListAsync());
    }

    [Fact]
    public async Task Updates_existing_assessment_in_place()
    {
        await using var db = TestDbContextFactory.CreateSystemContext();
        var v = Vulnerability.Create("nvd", "CVE-2026-2000", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(v);
        await db.SaveChangesAsync();

        var svc = new ThreatAssessmentService(db);
        await svc.UpsertAsync(v.Id, 1, 1, 0.1m, 1, null, false, false, false, false, false, "[]", "v1", CancellationToken.None);
        await db.SaveChangesAsync();
        await svc.UpsertAsync(v.Id, 9, 9, 0.9m, 9, 0.8m, true, true, true, true, true, "[\"kev\"]", "v2", CancellationToken.None);
        await db.SaveChangesAsync();

        var assessments = await db.ThreatAssessments.ToListAsync();
        Assert.Single(assessments);
        Assert.Equal(9, assessments[0].ThreatScore);
        Assert.Equal("v2", assessments[0].CalculationVersion);
    }
}
```

- [ ] **Step 2: Implement service**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class ThreatAssessmentService
{
    private readonly PatchHoundDbContext _db;

    public ThreatAssessmentService(PatchHoundDbContext db)
    {
        _db = db;
    }

    public async Task<ThreatAssessment> UpsertAsync(
        Guid vulnerabilityId,
        decimal threatScore,
        decimal technicalScore,
        decimal exploitLikelihoodScore,
        decimal threatActivityScore,
        decimal? epssScore,
        bool knownExploited,
        bool publicExploit,
        bool activeAlert,
        bool hasRansomwareAssociation,
        bool hasMalwareAssociation,
        string factorsJson,
        string calculationVersion,
        CancellationToken ct)
    {
        var existing = await _db.ThreatAssessments
            .FirstOrDefaultAsync(a => a.VulnerabilityId == vulnerabilityId, ct);

        if (existing is null)
        {
            var a = ThreatAssessment.Create(
                vulnerabilityId, threatScore, technicalScore, exploitLikelihoodScore, threatActivityScore,
                epssScore, knownExploited, publicExploit, activeAlert,
                hasRansomwareAssociation, hasMalwareAssociation, factorsJson, calculationVersion);
            _db.ThreatAssessments.Add(a);
            return a;
        }

        existing.Update(
            threatScore, technicalScore, exploitLikelihoodScore, threatActivityScore,
            epssScore, knownExploited, publicExploit, activeAlert,
            hasRansomwareAssociation, hasMalwareAssociation, factorsJson, calculationVersion);
        return existing;
    }
}
```

- [ ] **Step 3: Tests pass**

- [ ] **Step 4: Register in DI**

Add `services.AddScoped<ThreatAssessmentService>();` to `DependencyInjection.cs`.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/ThreatAssessmentService.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs \
        tests/PatchHound.Infrastructure.Tests/Services/ThreatAssessmentServiceTests.cs
git commit -m "feat(phase-2): add ThreatAssessmentService upsert"
```

---

## Task 7: Rewrite `DefenderVulnerabilitySource` to produce canonical `VulnerabilityResolveInput`

Today `DefenderVulnerabilitySource` returns `IngestionResult` wrappers built around legacy `VulnerabilityDefinition` shapes. Rewrite to return a new `CanonicalVulnerabilityBatch` shape containing `VulnerabilityResolveInput` items plus per-vulnerability threat-assessment inputs.

**Files:**
- Create: `src/PatchHound.Core/Models/CanonicalVulnerabilityBatch.cs`
- Modify: `src/PatchHound.Core/Interfaces/IVulnerabilitySource.cs` (add canonical method; deprecate legacy method signature)
- Modify: `src/PatchHound.Infrastructure/VulnerabilitySources/DefenderVulnerabilitySource.cs`
- Test: `tests/PatchHound.Infrastructure.Tests/VulnerabilitySources/DefenderVulnerabilitySourceCanonicalTests.cs`

- [ ] **Step 1: Define canonical batch model**

```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Models;

public record CanonicalVulnerabilityBatch(
    IReadOnlyList<VulnerabilityResolveInput> Vulnerabilities,
    IReadOnlyList<CanonicalThreatAssessmentInput> ThreatAssessments);

public record CanonicalThreatAssessmentInput(
    string Source,
    string ExternalId,
    decimal ThreatScore,
    decimal TechnicalScore,
    decimal ExploitLikelihoodScore,
    decimal ThreatActivityScore,
    decimal? EpssScore,
    bool KnownExploited,
    bool PublicExploit,
    bool ActiveAlert,
    bool HasRansomwareAssociation,
    bool HasMalwareAssociation,
    string FactorsJson,
    string CalculationVersion);
```

- [ ] **Step 2: Extend `IVulnerabilitySource`**

In `src/PatchHound.Core/Interfaces/IVulnerabilitySource.cs`, add:

```csharp
Task<CanonicalVulnerabilityBatch> FetchCanonicalVulnerabilitiesAsync(Guid tenantId, CancellationToken ct);
```

and mark the legacy `FetchVulnerabilitiesAsync(...)` with `[Obsolete("Removed in Phase 2. Use FetchCanonicalVulnerabilitiesAsync.", error: true)]`. Every implementer must provide the canonical method.

- [ ] **Step 3: Write failing test**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Enums;
using PatchHound.Tests.Infrastructure.Testing;
using PatchHound.Infrastructure.VulnerabilitySources;

namespace PatchHound.Tests.Infrastructure.VulnerabilitySources;

public class DefenderVulnerabilitySourceCanonicalTests
{
    [Fact]
    public async Task FetchCanonicalVulnerabilitiesAsync_returns_batch_with_resolve_inputs()
    {
        var apiClient = FakeDefenderApiClient.WithSingleVulnerability("CVE-2026-0099", Severity.High, 8.5m);
        var source = new DefenderVulnerabilitySource(apiClient, FakeDefenderConfigProvider.Default(), NullLogger<DefenderVulnerabilitySource>.Instance);
        var batch = await source.FetchCanonicalVulnerabilitiesAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Single(batch.Vulnerabilities);
        Assert.Equal("CVE-2026-0099", batch.Vulnerabilities[0].ExternalId);
        Assert.Equal("microsoft-defender", batch.Vulnerabilities[0].Source);
    }
}
```

Implement the `FakeDefenderApiClient` / `FakeDefenderConfigProvider` helpers under `tests/PatchHound.Tests/Infrastructure/Testing/` if not present — simple stubs returning predefined payloads from the existing Defender DTOs.

- [ ] **Step 4: Rewrite `DefenderVulnerabilitySource.FetchCanonicalVulnerabilitiesAsync`**

Preserve all existing Defender payload parsing (CVE ID extraction, severity mapping, version range extraction) but emit `VulnerabilityResolveInput` / `CanonicalThreatAssessmentInput` directly instead of building `VulnerabilityDefinition` entities. Every applicability maps to `VulnerabilityApplicabilityInput` keyed by CPE string (Defender payloads don't carry `SoftwareProductId`; the NVD resolver will fill that in where possible, otherwise resolution happens in Phase 3 via CPE match fallback).

```csharp
public async Task<CanonicalVulnerabilityBatch> FetchCanonicalVulnerabilitiesAsync(
    Guid tenantId, CancellationToken ct)
{
    _logger.LogInformation(
        "Fetching canonical vulnerabilities from Microsoft Defender for tenant {TenantId}", tenantId);

    var configuration = await _configurationProvider.GetConfigurationAsync(tenantId, ct);
    if (configuration is null)
        return new CanonicalVulnerabilityBatch(Array.Empty<VulnerabilityResolveInput>(), Array.Empty<CanonicalThreatAssessmentInput>());

    DefenderMachineVulnerabilityResponse response;
    try
    {
        response = await _apiClient.GetMachineVulnerabilitiesAsync(configuration, ct);
    }
    catch (Exception ex) when (IsRecoverableTimeout(ex, ct))
    {
        _logger.LogWarning(ex, "Defender vulnerability fetch timed out for tenant {TenantId}", tenantId);
        return new CanonicalVulnerabilityBatch(Array.Empty<VulnerabilityResolveInput>(), Array.Empty<CanonicalThreatAssessmentInput>());
    }

    var vulns = new List<VulnerabilityResolveInput>();
    var assessments = new List<CanonicalThreatAssessmentInput>();

    foreach (var group in response.Value.Where(HasUsableCveId)
        .GroupBy(v => v.CveId.Trim(), StringComparer.OrdinalIgnoreCase))
    {
        var first = group.First();
        var severity = MapSeverity(first.Severity);
        var apps = group
            .Where(g => !string.IsNullOrWhiteSpace(g.ProductName))
            .Select(g => new VulnerabilityApplicabilityInput(
                SoftwareProductId: null,
                CpeCriteria: BuildCpeCriteria(g),
                Vulnerable: true,
                VersionStartIncluding: null,
                VersionStartExcluding: null,
                VersionEndIncluding: g.ProductVersion,
                VersionEndExcluding: null))
            .ToList();

        vulns.Add(new VulnerabilityResolveInput(
            Source: SourceKey,
            ExternalId: group.Key,
            Title: first.Title ?? group.Key,
            Description: first.Description ?? string.Empty,
            VendorSeverity: severity,
            CvssScore: first.CvssScore,
            CvssVector: first.CvssVector,
            PublishedDate: first.PublishedOn,
            References: Array.Empty<VulnerabilityReferenceInput>(),
            Applicabilities: apps));

        if (first.EpssScore is not null || first.PublicExploit || first.KnownExploited)
        {
            assessments.Add(new CanonicalThreatAssessmentInput(
                SourceKey, group.Key,
                ThreatScore: (decimal)(first.CvssScore ?? 0m),
                TechnicalScore: (decimal)(first.CvssScore ?? 0m),
                ExploitLikelihoodScore: (decimal)(first.EpssScore ?? 0m),
                ThreatActivityScore: first.KnownExploited ? 9m : 3m,
                EpssScore: first.EpssScore,
                KnownExploited: first.KnownExploited,
                PublicExploit: first.PublicExploit,
                ActiveAlert: false,
                HasRansomwareAssociation: false,
                HasMalwareAssociation: false,
                FactorsJson: "[]",
                CalculationVersion: "defender-v1"));
        }
    }

    return new CanonicalVulnerabilityBatch(vulns, assessments);
}

private static string BuildCpeCriteria(DefenderMachineVulnerabilityEntry entry) =>
    $"cpe:2.3:a:{(entry.ProductVendor ?? "*").ToLowerInvariant()}:{(entry.ProductName ?? "*").ToLowerInvariant()}:*:*:*:*:*:*:*:*";
```

> Reuse the existing `HasUsableCveId` / `IsRecoverableTimeout` / `MapSeverity` private helpers already in this file. Delete the legacy `FetchVulnerabilitiesAsync` body by replacing it with `throw new NotSupportedException("Removed in Phase 2. Use FetchCanonicalVulnerabilitiesAsync.");` (the interface-level `[Obsolete(error: true)]` causes callers to fail to compile).

- [ ] **Step 5: Tests pass**

Run: `dotnet test --filter "FullyQualifiedName~DefenderVulnerabilitySourceCanonicalTests"` — PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Core/Models/CanonicalVulnerabilityBatch.cs \
        src/PatchHound.Core/Interfaces/IVulnerabilitySource.cs \
        src/PatchHound.Infrastructure/VulnerabilitySources/DefenderVulnerabilitySource.cs \
        tests/PatchHound.Infrastructure.Tests/VulnerabilitySources/DefenderVulnerabilitySourceCanonicalTests.cs
git commit -m "feat(phase-2): DefenderVulnerabilitySource emits CanonicalVulnerabilityBatch"
```

---

## Task 8: Rewrite `NvdVulnerabilityEnrichmentRunner` and `DefenderVulnerabilityEnrichmentRunner` against canonical resolver

Both runners currently read legacy `VulnerabilityDefinition`/`VulnerabilityThreatAssessment` rows and mutate them. Rewrite them to:

1. Open a system-context `PatchHoundDbContext`
2. Fetch canonical batches via `IVulnerabilitySource.FetchCanonicalVulnerabilitiesAsync`
3. Call `VulnerabilityResolver.ResolveAsync` for each vulnerability
4. For NVD specifically: attempt to match each applicability's CPE against `SoftwareAlias` (`SourceSystemId='nvd'`) → `SoftwareProductId`, and set `VulnerabilityApplicabilityInput.SoftwareProductId` when a match is found (CPE string is retained as fallback)
5. Call `ThreatAssessmentService.UpsertAsync` for each canonical threat assessment input, mapping `(Source, ExternalId)` → `VulnerabilityId` via a single preloaded dictionary
6. `SaveChangesAsync`

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/DefenderVulnerabilityEnrichmentRunner.cs`
- Modify: `src/PatchHound.Infrastructure/Services/NvdVulnerabilityEnrichmentRunner.cs`
- Test: `tests/PatchHound.Infrastructure.Tests/Services/NvdVulnerabilityEnrichmentRunnerTests.cs`
- Test: `tests/PatchHound.Infrastructure.Tests/Services/DefenderVulnerabilityEnrichmentRunnerTests.cs`

- [ ] **Step 1: Write failing test for NVD runner (CPE→SoftwareProduct resolution)**

```csharp
[Fact]
public async Task NvdEnrichmentRunner_resolves_applicability_to_SoftwareProduct_when_alias_exists()
{
    await using var db = TestDbContextFactory.CreateSystemContext();

    // Arrange: seed a canonical SoftwareProduct and an NVD alias pointing to it
    var nvdSource = SourceSystem.Create("nvd", "NVD", true);
    db.SourceSystems.Add(nvdSource);
    var product = SoftwareProduct.Create("acme:widget", "Widget", "Acme", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
    db.SoftwareProducts.Add(product);
    var alias = SoftwareAlias.Create(nvdSource.Id, "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*", product.Id);
    db.SoftwareAliases.Add(alias);
    await db.SaveChangesAsync();

    // Arrange: fake NVD source returning a single vuln for acme:widget
    var fakeSource = new FakeNvdSource(new CanonicalVulnerabilityBatch(
        new[] {
            new VulnerabilityResolveInput(
                "nvd", "CVE-2026-5555", "t", "d", Severity.High, 7m, "v", DateTimeOffset.UtcNow,
                Array.Empty<VulnerabilityReferenceInput>(),
                new[] { new VulnerabilityApplicabilityInput(
                    SoftwareProductId: null,
                    CpeCriteria: "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*",
                    Vulnerable: true, null, null, "1.0", null) })
        },
        Array.Empty<CanonicalThreatAssessmentInput>()));

    var runner = new NvdVulnerabilityEnrichmentRunner(
        fakeSource,
        new VulnerabilityResolver(db),
        new ThreatAssessmentService(db),
        db,
        NullLogger<NvdVulnerabilityEnrichmentRunner>.Instance);

    await runner.RunAsync(CancellationToken.None);

    // Assert: the applicability row has SoftwareProductId populated
    var app = await db.VulnerabilityApplicabilities.SingleAsync();
    Assert.Equal(product.Id, app.SoftwareProductId);
    Assert.Equal("cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*", app.CpeCriteria);
}
```

- [ ] **Step 2: Rewrite NVD runner**

```csharp
public class NvdVulnerabilityEnrichmentRunner
{
    private readonly IVulnerabilitySource _source;
    private readonly VulnerabilityResolver _resolver;
    private readonly ThreatAssessmentService _threatService;
    private readonly PatchHoundDbContext _db;
    private readonly ILogger<NvdVulnerabilityEnrichmentRunner> _logger;

    public NvdVulnerabilityEnrichmentRunner(
        IVulnerabilitySource source,
        VulnerabilityResolver resolver,
        ThreatAssessmentService threatService,
        PatchHoundDbContext db,
        ILogger<NvdVulnerabilityEnrichmentRunner> logger)
    {
        _source = source;
        _resolver = resolver;
        _threatService = threatService;
        _db = db;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _db.SetSystemContext(true); // enables global writes per spec §4.10 Rule 9
        try
        {
            var batch = await _source.FetchCanonicalVulnerabilitiesAsync(Guid.Empty /*global source*/, ct);

            var nvdSourceSystemId = await _db.SourceSystems
                .Where(s => s.Key == "nvd")
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(ct);

            var aliasMap = nvdSourceSystemId is null
                ? new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
                : await _db.SoftwareAliases
                    .Where(a => a.SourceSystemId == nvdSourceSystemId)
                    .ToDictionaryAsync(a => a.ExternalId, a => a.SoftwareProductId, StringComparer.OrdinalIgnoreCase, ct);

            foreach (var vuln in batch.Vulnerabilities)
            {
                var resolved = new VulnerabilityResolveInput(
                    vuln.Source, vuln.ExternalId, vuln.Title, vuln.Description,
                    vuln.VendorSeverity, vuln.CvssScore, vuln.CvssVector, vuln.PublishedDate,
                    vuln.References,
                    vuln.Applicabilities.Select(a =>
                        a.SoftwareProductId is null
                            && a.CpeCriteria is not null
                            && aliasMap.TryGetValue(a.CpeCriteria, out var pid)
                        ? a with { SoftwareProductId = pid }
                        : a).ToList());

                await _resolver.ResolveAsync(resolved, ct);
            }

            // Persist vulns so threat-assessment lookups find them
            await _db.SaveChangesAsync(ct);

            var externalIdToVulnId = await _db.Vulnerabilities
                .Where(v => batch.ThreatAssessments.Select(t => t.ExternalId).Contains(v.ExternalId)
                         && batch.ThreatAssessments.Select(t => t.Source).Contains(v.Source))
                .ToDictionaryAsync(v => $"{v.Source}|{v.ExternalId}", v => v.Id, ct);

            foreach (var a in batch.ThreatAssessments)
            {
                if (!externalIdToVulnId.TryGetValue($"{a.Source}|{a.ExternalId}", out var vulnId)) continue;
                await _threatService.UpsertAsync(
                    vulnId, a.ThreatScore, a.TechnicalScore, a.ExploitLikelihoodScore, a.ThreatActivityScore,
                    a.EpssScore, a.KnownExploited, a.PublicExploit, a.ActiveAlert,
                    a.HasRansomwareAssociation, a.HasMalwareAssociation, a.FactorsJson, a.CalculationVersion, ct);
            }
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            _db.SetSystemContext(false);
        }
    }
}
```

> `PatchHoundDbContext.SetSystemContext(bool)` toggles the `IsSystemContext` flag introduced in Phase 1 Task 1. If the context does not yet expose a mutator, add one in `PatchHoundDbContext`: `public void SetSystemContext(bool value) => IsSystemContext = value;`.

- [ ] **Step 3: Rewrite Defender runner similarly**

Same structure, but Defender does not carry CPE aliases the same way — Defender applicabilities leave `SoftwareProductId` null and rely on Phase 3 CPE match fallback. Defender runner still upserts threat assessments.

- [ ] **Step 4: NVD test passes**

Run: `dotnet test --filter "FullyQualifiedName~NvdVulnerabilityEnrichmentRunnerTests"` — PASS.

- [ ] **Step 5: Defender runner test**

Analogous test: given a fake Defender canonical batch with two vulnerabilities, assert that two `Vulnerability` rows, their CPE-based applicabilities (`SoftwareProductId` null), and matching `ThreatAssessment` rows are written.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/NvdVulnerabilityEnrichmentRunner.cs \
        src/PatchHound.Infrastructure/Services/DefenderVulnerabilityEnrichmentRunner.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        tests/PatchHound.Infrastructure.Tests/Services/NvdVulnerabilityEnrichmentRunnerTests.cs \
        tests/PatchHound.Infrastructure.Tests/Services/DefenderVulnerabilityEnrichmentRunnerTests.cs
git commit -m "feat(phase-2): rewrite vulnerability enrichment runners against canonical resolver"
```

---

## Task 9: Delete `StagedVulnerabilityMergeService`, `VulnerabilityAssessmentService`, `VulnerabilityThreatAssessmentService`, `SoftwareVulnerabilityMatchService`

All four services operate on legacy tables that are deleted in Task 16. Delete them now (before the entity deletion so the compiler errors surface in one batch).

**Files:**
- Delete: `src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs`
- Delete: `src/PatchHound.Infrastructure/Services/VulnerabilityAssessmentService.cs`
- Delete: `src/PatchHound.Infrastructure/Services/VulnerabilityThreatAssessmentService.cs`
- Delete: `src/PatchHound.Infrastructure/Services/SoftwareVulnerabilityMatchService.cs`
- Delete: `src/PatchHound.Infrastructure/Services/VulnerabilityEpisodeRiskAssessmentService.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs` (remove registrations)
- Delete: any tests that exercise the above services

- [ ] **Step 1: Delete the service files and their test files**

```bash
rm src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs
rm src/PatchHound.Infrastructure/Services/VulnerabilityAssessmentService.cs
rm src/PatchHound.Infrastructure/Services/VulnerabilityThreatAssessmentService.cs
rm src/PatchHound.Infrastructure/Services/SoftwareVulnerabilityMatchService.cs
rm src/PatchHound.Infrastructure/Services/VulnerabilityEpisodeRiskAssessmentService.cs
rm tests/PatchHound.Infrastructure.Tests/Services/StagedVulnerabilityMergeServiceTests.cs 2>/dev/null || true
rm tests/PatchHound.Infrastructure.Tests/Services/VulnerabilityAssessmentServiceTests.cs 2>/dev/null || true
rm tests/PatchHound.Infrastructure.Tests/Services/VulnerabilityThreatAssessmentServiceTests.cs 2>/dev/null || true
rm tests/PatchHound.Infrastructure.Tests/Services/SoftwareVulnerabilityMatchServiceTests.cs 2>/dev/null || true
rm tests/PatchHound.Infrastructure.Tests/Services/VulnerabilityEpisodeRiskAssessmentServiceTests.cs 2>/dev/null || true
```

- [ ] **Step 2: Remove DI registrations**

In `src/PatchHound.Infrastructure/DependencyInjection.cs`, remove every `AddScoped<...>` and `AddSingleton<...>` line that references the five deleted services.

- [ ] **Step 3: Build — expect errors**

Run: `dotnet build`
Expected: errors — every service/controller that still uses the deleted types. Tasks 10–12 clean those up.

- [ ] **Step 4: Commit (intentional broken state marker)**

```bash
git add -u
git commit -m "feat(phase-2): delete legacy vulnerability services (compile intentionally broken)"
```

---

## Task 10: Rewrite `VulnerabilityDetailQueryService` against canonical `Vulnerability`

**Files:**
- Modify: `src/PatchHound.Api/Services/VulnerabilityDetailQueryService.cs`
- Modify: `src/PatchHound.Api/Models/Vulnerabilities/VulnerabilityDetailDto.cs`
- Test: `tests/PatchHound.Api.Tests/Services/VulnerabilityDetailQueryServiceTests.cs`

The Phase 2 contract: `VulnerabilityDetailDto` is built from a canonical `Vulnerability` + its `VulnerabilityReference`s + its `VulnerabilityApplicability` rows + its `ThreatAssessment` (if any). The `AffectedDevices`, `ActiveExposures`, and `ResolvedExposures` fields are **always empty arrays in Phase 2** and are marked as such on the DTO with a `DataAvailable: false` flag.

- [ ] **Step 1: Update the DTO**

In `VulnerabilityDetailDto.cs`, add an explicit nested shape:

```csharp
public record VulnerabilityDetailDto(
    Guid Id,
    string ExternalId,
    string Source,
    string Title,
    string Description,
    string VendorSeverity,
    decimal? CvssScore,
    string? CvssVector,
    DateTimeOffset? PublishedDate,
    IReadOnlyList<VulnerabilityReferenceDto> References,
    IReadOnlyList<VulnerabilityApplicabilityDto> Applicabilities,
    ThreatAssessmentDto? ThreatAssessment,
    ExposureSectionDto Exposures);

public record VulnerabilityReferenceDto(string Url, string Source, IReadOnlyList<string> Tags);
public record VulnerabilityApplicabilityDto(Guid? SoftwareProductId, string? SoftwareProductName, string? CpeCriteria, bool Vulnerable, string? VersionStartIncluding, string? VersionStartExcluding, string? VersionEndIncluding, string? VersionEndExcluding);
public record ThreatAssessmentDto(decimal ThreatScore, decimal? EpssScore, bool KnownExploited, bool PublicExploit, bool ActiveAlert);
public record ExposureSectionDto(
    bool DataAvailable,
    string DataAvailableReason,
    IReadOnlyList<object> AffectedDevices,
    IReadOnlyList<object> ActiveExposures,
    IReadOnlyList<object> ResolvedExposures);
```

Phase 2 always returns:

```csharp
Exposures: new ExposureSectionDto(
    DataAvailable: false,
    DataAvailableReason: "Populated after the Phase 3 canonical exposure merge.",
    AffectedDevices: Array.Empty<object>(),
    ActiveExposures: Array.Empty<object>(),
    ResolvedExposures: Array.Empty<object>());
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
        .Select(a => a.SoftwareProductId!.Value)
        .ToList();
    var productNames = await _db.SoftwareProducts.AsNoTracking()
        .Where(p => productIds.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

    var threat = await _db.ThreatAssessments.AsNoTracking()
        .FirstOrDefaultAsync(t => t.VulnerabilityId == vulnerabilityId, ct);

    return new VulnerabilityDetailDto(
        vuln.Id,
        vuln.ExternalId,
        vuln.Source,
        vuln.Title,
        vuln.Description,
        vuln.VendorSeverity.ToString(),
        vuln.CvssScore,
        vuln.CvssVector,
        vuln.PublishedDate,
        references.Select(r => new VulnerabilityReferenceDto(r.Url, r.Source, r.GetTags())).ToList(),
        applicabilities.Select(a => new VulnerabilityApplicabilityDto(
            a.SoftwareProductId,
            a.SoftwareProductId is not null && productNames.TryGetValue(a.SoftwareProductId.Value, out var n) ? n : null,
            a.CpeCriteria, a.Vulnerable,
            a.VersionStartIncluding, a.VersionStartExcluding,
            a.VersionEndIncluding, a.VersionEndExcluding)).ToList(),
        threat is null ? null : new ThreatAssessmentDto(threat.ThreatScore, threat.EpssScore, threat.KnownExploited, threat.PublicExploit, threat.ActiveAlert),
        new ExposureSectionDto(
            DataAvailable: false,
            DataAvailableReason: "Populated after the Phase 3 canonical exposure merge.",
            AffectedDevices: Array.Empty<object>(),
            ActiveExposures: Array.Empty<object>(),
            ResolvedExposures: Array.Empty<object>()));
}
```

- [ ] **Step 3: Write test**

```csharp
[Fact]
public async Task BuildAsync_returns_canonical_detail_with_empty_exposure_section()
{
    await using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
    var vuln = Vulnerability.Create("nvd", "CVE-2026-3333", "Title", "Desc", Severity.High, 7.5m, "vec", DateTimeOffset.UtcNow);
    db.Vulnerabilities.Add(vuln);
    db.VulnerabilityReferences.Add(VulnerabilityReference.Create(vuln.Id, "https://example.com", "nvd", new[] { "advisory" }));
    await db.SaveChangesAsync();

    var svc = new VulnerabilityDetailQueryService(db);
    var dto = await svc.BuildAsync(tenantId, vuln.Id, CancellationToken.None);

    Assert.NotNull(dto);
    Assert.Equal("CVE-2026-3333", dto!.ExternalId);
    Assert.Single(dto.References);
    Assert.False(dto.Exposures.DataAvailable);
    Assert.Empty(dto.Exposures.AffectedDevices);
    Assert.Equal("Populated after the Phase 3 canonical exposure merge.", dto.Exposures.DataAvailableReason);
}
```

Run: PASS after rewrite.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Api/Services/VulnerabilityDetailQueryService.cs \
        src/PatchHound.Api/Models/Vulnerabilities/VulnerabilityDetailDto.cs \
        tests/PatchHound.Api.Tests/Services/VulnerabilityDetailQueryServiceTests.cs
git commit -m "feat(phase-2): rewrite VulnerabilityDetailQueryService against canonical Vulnerability"
```

---

## Task 11: Rewrite `VulnerabilitiesController` list endpoint against canonical `Vulnerability` + `ThreatAssessment`

**Files:**
- Modify: `src/PatchHound.Api/Controllers/VulnerabilitiesController.cs`
- Modify: `src/PatchHound.Api/Models/Vulnerabilities/VulnerabilityDto.cs`
- Modify: `src/PatchHound.Api/Models/Vulnerabilities/VulnerabilityFilterQuery.cs` (drop `PresentOnly` / `RecurrenceOnly` filters — they depend on legacy `VulnerabilityAssetEpisodes` and are restored in Phase 3)

The Phase 2 list endpoint:

- Scopes to the current tenant only via authorization/policy (the result set is global, but the policy gate is preserved)
- Queries `db.Vulnerabilities` (global, unfiltered)
- Left-joins `ThreatAssessments` (global, unfiltered) for EPSS / KEV / public-exploit flags
- Removes all joins to `TenantVulnerabilities`, `VulnerabilityAssets`, `VulnerabilityAssetEpisodes`, `VulnerabilityThreatAssessments`, `OrganizationalSeverities`
- Returns `AffectedAssetCount = 0` and `AdjustedSeverity = null` with a flag `ExposureDataAvailable = false`
- Removes the `id/organizational-severity` endpoint body until Phase 3 re-introduces tenant severity override via a separate mechanism; the endpoint itself stays registered but returns `409 Conflict { Title = "Organizational severity is disabled during canonical migration; restored in Phase 3." }`
- Removes the `id/ai-report` endpoint body for now and returns `409 Conflict` with the same message (AI report generation is rewritten against `RemediationCase` + `DeviceVulnerabilityExposure` in Phase 4; Phase 2 cannot produce a useful report without exposure data)

- [ ] **Step 1: Update `VulnerabilityDto`**

```csharp
public record VulnerabilityDto(
    Guid Id,
    string ExternalId,
    string Title,
    string VendorSeverity,
    string Source,
    decimal? CvssScore,
    DateTimeOffset? PublishedDate,
    bool ExposureDataAvailable,
    int AffectedDeviceCount,
    decimal? ThreatScore,
    decimal? EpssScore,
    bool PublicExploit,
    bool KnownExploited,
    bool ActiveAlert);
```

- [ ] **Step 2: Drop legacy fields from filter query**

In `VulnerabilityFilterQuery.cs`, delete `PresentOnly`, `RecurrenceOnly`, and `TenantId`. Keep `Severity`, `Status` (maps to nothing for now — ignored), `Source`, `Search`, `MinAgeDays`, `PublicExploitOnly`, `KnownExploitedOnly`, `ActiveAlertOnly`.

- [ ] **Step 3: Rewrite the `List` method**

```csharp
[HttpGet]
[Authorize(Policy = Policies.ViewVulnerabilities)]
public async Task<ActionResult<PagedResponse<VulnerabilityDto>>> List(
    [FromQuery] VulnerabilityFilterQuery filter,
    [FromQuery] PaginationQuery pagination,
    CancellationToken ct)
{
    if (_tenantContext.CurrentTenantId is null)
        return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

    var query = _dbContext.Vulnerabilities.AsNoTracking().AsQueryable();

    if (!string.IsNullOrEmpty(filter.Severity) && Enum.TryParse<Severity>(filter.Severity, out var severity))
        query = query.Where(v => v.VendorSeverity == severity);
    if (!string.IsNullOrEmpty(filter.Source))
        query = query.Where(v => v.Source.Contains(filter.Source));
    if (!string.IsNullOrEmpty(filter.Search))
        query = query.Where(v => v.Title.Contains(filter.Search) || v.ExternalId.Contains(filter.Search));
    if (filter.MinAgeDays.HasValue)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-filter.MinAgeDays.Value);
        query = query.Where(v => v.PublishedDate <= cutoff);
    }
    if (filter.PublicExploitOnly == true)
        query = query.Where(v => _dbContext.ThreatAssessments.Any(a => a.VulnerabilityId == v.Id && a.PublicExploit));
    if (filter.KnownExploitedOnly == true)
        query = query.Where(v => _dbContext.ThreatAssessments.Any(a => a.VulnerabilityId == v.Id && a.KnownExploited));
    if (filter.ActiveAlertOnly == true)
        query = query.Where(v => _dbContext.ThreatAssessments.Any(a => a.VulnerabilityId == v.Id && a.ActiveAlert));

    var total = await query.CountAsync(ct);

    var items = await query
        .OrderByDescending(v => v.CvssScore)
        .ThenByDescending(v => v.PublishedDate)
        .Skip(pagination.Skip)
        .Take(pagination.BoundedPageSize)
        .Select(v => new
        {
            v.Id,
            v.ExternalId,
            v.Title,
            Severity = v.VendorSeverity.ToString(),
            v.Source,
            v.CvssScore,
            v.PublishedDate,
            Threat = _dbContext.ThreatAssessments
                .Where(a => a.VulnerabilityId == v.Id)
                .Select(a => new { a.ThreatScore, a.EpssScore, a.PublicExploit, a.KnownExploited, a.ActiveAlert })
                .FirstOrDefault(),
        })
        .ToListAsync(ct);

    var dtos = items.Select(v => new VulnerabilityDto(
        v.Id, v.ExternalId, v.Title, v.Severity, v.Source, v.CvssScore, v.PublishedDate,
        ExposureDataAvailable: false,
        AffectedDeviceCount: 0,
        ThreatScore: v.Threat?.ThreatScore,
        EpssScore: v.Threat?.EpssScore,
        PublicExploit: v.Threat?.PublicExploit ?? false,
        KnownExploited: v.Threat?.KnownExploited ?? false,
        ActiveAlert: v.Threat?.ActiveAlert ?? false)).ToList();

    return Ok(new PagedResponse<VulnerabilityDto>(dtos, total, pagination.Page, pagination.BoundedPageSize));
}
```

- [ ] **Step 4: Stub legacy mutation endpoints**

```csharp
[HttpPut("{id:guid}/organizational-severity")]
[Authorize(Policy = Policies.AdjustSeverity)]
public IActionResult UpdateOrganizationalSeverity(Guid id, [FromBody] UpdateOrgSeverityRequest _) =>
    Conflict(new ProblemDetails { Title = "Organizational severity is disabled during canonical migration; restored in Phase 3." });

[HttpPost("{id:guid}/ai-report")]
[Authorize(Policy = Policies.GenerateAiReports)]
public IActionResult GenerateAiReport(Guid id, [FromBody] GenerateAiReportRequest _) =>
    Conflict(new ProblemDetails { Title = "AI report generation is disabled during canonical migration; restored in Phase 4 (case-first)." });
```

Remove `VulnerabilityService`, `AiReportService`, `TenantSnapshotResolver` constructor parameters — they are unused after this rewrite. Remove their `using` statements. The controller only needs `PatchHoundDbContext`, `ITenantContext`, and `VulnerabilityDetailQueryService`.

- [ ] **Step 5: Controller test**

```csharp
[Fact]
public async Task List_returns_canonical_vulns_with_ExposureDataAvailable_false()
{
    // Arrange in-memory dbcontext seeded with two vulnerabilities and one threat assessment
    // ... similar to Phase 1 controller tests
    // Assert response contains 2 items; each dto.ExposureDataAvailable == false; dto.AffectedDeviceCount == 0
}
```

Run: `dotnet test --filter "FullyQualifiedName~VulnerabilitiesControllerTests"` — PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Api/Controllers/VulnerabilitiesController.cs \
        src/PatchHound.Api/Models/Vulnerabilities/VulnerabilityDto.cs \
        src/PatchHound.Api/Models/Vulnerabilities/VulnerabilityFilterQuery.cs \
        tests/PatchHound.Api.Tests/Controllers/VulnerabilitiesControllerTests.cs
git commit -m "feat(phase-2): rewrite VulnerabilitiesController against canonical Vulnerability"
```

---

## Task 12: Delete legacy `VulnerabilityService`, `OrganizationalSeverity` writes through `VulnerabilityService`

The legacy `VulnerabilityService.UpdateOrganizationalSeverityAsync` writes to `OrganizationalSeverities` keyed on `TenantVulnerabilityId`, which is going away. Delete the method and the entire `VulnerabilityService` class (its only consumer was the organizational-severity endpoint which now returns 409).

**Files:**
- Delete: `src/PatchHound.Core/Services/VulnerabilityService.cs` (or wherever it lives)
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`
- Delete: related test files

- [ ] **Step 1: Confirm file location**

Run: `grep -rn "class VulnerabilityService" src/ --include="*.cs"`

- [ ] **Step 2: Delete file and registration**

```bash
rm src/PatchHound.Core/Services/VulnerabilityService.cs   # adjust path as needed
rm tests/**/VulnerabilityServiceTests.cs 2>/dev/null || true
```

Remove `services.AddScoped<VulnerabilityService>()` from DI.

- [ ] **Step 3: Commit**

```bash
git add -u
git commit -m "feat(phase-2): delete VulnerabilityService (replaced by canonical resolver)"
```

---

## Task 13: Frontend — handle empty exposure section in vulnerability list/detail

**Files:**
- Modify: `web/src/routes/vulnerabilities/index.tsx` (list view)
- Modify: `web/src/routes/vulnerabilities/$vulnerabilityId.tsx` (detail view)
- Modify: `web/src/api/types/vulnerabilities.ts` (add `ExposureDataAvailable`, remove `AffectedAssetCount` semantics in favor of `AffectedDeviceCount`)
- Test: `web/src/routes/vulnerabilities/__tests__/VulnerabilitiesList.test.tsx`
- Test: `web/src/routes/vulnerabilities/__tests__/VulnerabilityDetail.test.tsx`

- [ ] **Step 1: Update API types**

Match the backend DTO field names exactly (camelCase). Add `exposureDataAvailable: boolean`, `affectedDeviceCount: number`, and in detail: `exposures: { dataAvailable: boolean; dataAvailableReason: string; affectedDevices: unknown[]; activeExposures: unknown[]; resolvedExposures: unknown[]; }`.

- [ ] **Step 2: List view**

Where the current list shows "Affected: N assets", render one of:

- If `exposureDataAvailable === false`: show a small muted badge `Exposure data pending (Phase 3)` and omit the number.
- Otherwise: show `Affected: {affectedDeviceCount} devices`.

Remove any filter UI for "present only" / "recurrence only" (those filters were deleted server-side).

- [ ] **Step 3: Detail view**

Replace the "Affected Assets" and "Episodes" tabs with a single explanatory empty state when `exposures.dataAvailable === false`: a centered `Alert` component with the `dataAvailableReason` text. The "References", "Applicabilities", and "Threat Assessment" tabs render the new canonical data.

- [ ] **Step 4: Component tests**

```ts
it('shows "Exposure data pending" when exposureDataAvailable is false', async () => {
  mockApi({ items: [{ id: 'v1', externalId: 'CVE-2026-1111', exposureDataAvailable: false, affectedDeviceCount: 0, /* ... */ }] });
  render(<VulnerabilitiesList />);
  expect(await screen.findByText(/Exposure data pending/i)).toBeInTheDocument();
});

it('shows detail empty state when exposures.dataAvailable is false', async () => {
  mockApi({ id: 'v1', exposures: { dataAvailable: false, dataAvailableReason: 'Populated after the Phase 3 canonical exposure merge.', affectedDevices: [], activeExposures: [], resolvedExposures: [] } });
  render(<VulnerabilityDetail id="v1" />);
  expect(await screen.findByText(/Populated after the Phase 3/i)).toBeInTheDocument();
});
```

Run: `cd web && npm test -- --run vulnerabilities` — PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/routes/vulnerabilities/ web/src/api/types/vulnerabilities.ts
git commit -m "feat(phase-2): vulnerability UI handles empty exposure section"
```

---

## Task 14: Remove legacy references from `EmailNotificationService`, `RemediationDecisionService`, `PatchingTaskService`, `RiskScoreService`, `EnrichmentJobEnqueuer`, `IngestionStateCache`

Each of these files still references `TenantVulnerability`, `VulnerabilityDefinition`, `VulnerabilityAsset*`, or `VulnerabilityThreatAssessment`. Phase 2 strips those references to restore a green build. **Minimal** replacements only — do not re-add functionality that will be re-implemented in later phases.

**Guidance for each file:**

- `EmailNotificationService.cs` — Rewrite any query that pulls `TenantVulnerabilities.Include(v => v.VulnerabilityDefinition)` to query `Vulnerabilities` directly (scoped to the tenant via a follow-on join on `DeviceVulnerabilityExposure` in Phase 3). For Phase 2, if a notification can no longer be rendered without exposure data, guard the branch with `if (false)` and leave a TODO-anchor comment: `// phase-3: restore exposure-aware notification rendering`. Do not delete the notification type, just its unreachable body. **Tests:** update the existing notification tests to assert the Phase-2 fallback path renders a "no data yet" placeholder subject line.

- `RemediationDecisionService.cs`, `PatchingTaskService.cs` — These reference `TenantVulnerabilityId` columns on `RemediationDecision`/`PatchingTask`. Phase 2 does not yet drop those columns (Phase 4 does). For Phase 2, when legacy code reads `TenantVulnerabilityId`, replace the lookup with `null` and short-circuit the code path. Add a new `[Obsolete("Replaced in Phase 4 case-first remediation", error: false)]` on any public method that was exclusively a legacy lookup; leave its body as a throwing stub.

- `RiskScoreService.cs` — Replace `VulnerabilityThreatAssessment` joins with `ThreatAssessment` joins keyed on `VulnerabilityId`. Do not introduce canonical exposure reads yet — those come in Phase 5. For Phase 2, device risk scores continue to key off the Phase 1 inventory baseline (no vulnerability contribution). Add an explicit comment: `// phase-5: re-introduce vulnerability contribution from DeviceVulnerabilityExposure`.

- `EnrichmentJobEnqueuer.cs` — Wherever it enqueues enrichment for `VulnerabilityDefinitionId`, switch to `VulnerabilityId` (the canonical identifier). Wherever it filters by `TenantVulnerability.SourceKey`, filter by `Vulnerability.Source` on the global table.

- `IngestionStateCache.cs` — Any cache key that includes `VulnerabilityDefinitionId` switches to `VulnerabilityId`. Any entry keyed on `TenantVulnerabilityId` is deleted.

- [ ] **Step 1: Grep the legacy symbols file-by-file**

Run: `grep -n "TenantVulnerability\|VulnerabilityDefinition\|VulnerabilityThreatAssessment\|VulnerabilityAsset" src/PatchHound.Infrastructure/Services/EmailNotificationService.cs`

Repeat for each file listed above.

- [ ] **Step 2: Apply the rewrites per-file**

Work one file at a time. After each file compiles, run the narrow test filter for that service (e.g. `dotnet test --filter "FullyQualifiedName~EmailNotificationService"`) and confirm the test either passes or has been updated to reflect the Phase-2 fallback behaviour.

- [ ] **Step 3: Build — expect clean**

Run: `dotnet build`
Expected: 0 errors. Warnings are allowed only if they are `[Obsolete]` warnings on intentionally-stubbed methods; those are resolved in Phase 4.

- [ ] **Step 4: Commit**

```bash
git add -u
git commit -m "feat(phase-2): strip legacy vulnerability references from consumer services"
```

---

## Task 15: Add global-entity write-protection test

Spec §7 — "an authenticated non-system request that attempts to write a `SoftwareProduct`, `Vulnerability`, `VulnerabilityApplicability`, `VulnerabilityReference`, `ThreatAssessment`, or `SourceSystem` row returns 403 or 404. Tested explicitly."

**Files:**
- Test: `tests/PatchHound.Api.Tests/Security/GlobalEntityWriteProtectionTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.Api.Testing;

namespace PatchHound.Tests.Api.Security;

public class GlobalEntityWriteProtectionTests
{
    [Fact]
    public async Task Non_system_request_cannot_write_Vulnerability_directly()
    {
        await using var app = await TestApiFactory.CreateAsync();
        var client = app.CreateAuthenticatedClient("tenant-admin");

        // There is no endpoint that accepts vulnerability create payloads; verify that.
        var response = await client.PostAsJsonAsync("/api/vulnerabilities", new { externalId = "CVE-2026-EVIL" });
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.MethodNotAllowed ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 404/405/403, got {response.StatusCode}");
    }

    [Fact]
    public async Task Non_system_request_cannot_directly_insert_global_rows_via_any_controller()
    {
        await using var app = await TestApiFactory.CreateAsync();
        var client = app.CreateAuthenticatedClient("tenant-admin");

        foreach (var url in new[] {
            "/api/software-products",
            "/api/vulnerabilities",
            "/api/vulnerability-references",
            "/api/vulnerability-applicabilities",
            "/api/threat-assessments",
            "/api/source-systems"
        })
        {
            var response = await client.PostAsJsonAsync(url, new { name = "evil" });
            Assert.True(
                response.StatusCode == HttpStatusCode.NotFound ||
                response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                response.StatusCode == HttpStatusCode.Forbidden,
                $"{url} returned unexpected {response.StatusCode}");
        }
    }

    [Fact]
    public async Task System_context_DbContext_can_write_global_Vulnerability_row()
    {
        await using var app = await TestApiFactory.CreateAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        db.SetSystemContext(true);
        db.Vulnerabilities.Add(Vulnerability.Create("test", "CVE-2026-9999", "t", "d", PatchHound.Core.Enums.Severity.High, 7m, "v", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Vulnerabilities.CountAsync());
    }
}
```

- [ ] **Step 2: Run — PASS**

- [ ] **Step 3: Commit**

```bash
git add tests/PatchHound.Api.Tests/Security/GlobalEntityWriteProtectionTests.cs
git commit -m "test(phase-2): assert global entities cannot be written via request-scoped context"
```

---

## Task 16: Delete legacy vulnerability + exposure-asset entities, configurations, and DbSets

**Deletions (files):**

```
src/PatchHound.Core/Entities/VulnerabilityDefinition.cs
src/PatchHound.Core/Entities/VulnerabilityDefinitionReference.cs
src/PatchHound.Core/Entities/VulnerabilityDefinitionAffectedSoftware.cs
src/PatchHound.Core/Entities/TenantVulnerability.cs
src/PatchHound.Core/Entities/VulnerabilityThreatAssessment.cs
src/PatchHound.Core/Entities/VulnerabilityAsset.cs
src/PatchHound.Core/Entities/VulnerabilityAssetEpisode.cs
src/PatchHound.Core/Entities/VulnerabilityAssetAssessment.cs
src/PatchHound.Core/Entities/VulnerabilityEpisodeRiskAssessment.cs
src/PatchHound.Core/Entities/SoftwareVulnerabilityMatch.cs
src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityDefinitionConfiguration.cs
src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityDefinitionReferenceConfiguration.cs
src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityDefinitionAffectedSoftwareConfiguration.cs
src/PatchHound.Infrastructure/Data/Configurations/TenantVulnerabilityConfiguration.cs
src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityThreatAssessmentConfiguration.cs
src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityAssetConfiguration.cs
src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityAssetEpisodeConfiguration.cs
src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityAssetAssessmentConfiguration.cs
src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityEpisodeRiskAssessmentConfiguration.cs
src/PatchHound.Infrastructure/Data/Configurations/SoftwareVulnerabilityMatchConfiguration.cs
src/PatchHound.Infrastructure/Data/Configurations/NormalizedSoftwareVulnerabilityProjectionConfiguration.cs
```

**DbSets to remove from `PatchHoundDbContext`:**

- `VulnerabilityDefinitions`
- `VulnerabilityDefinitionReferences`
- `VulnerabilityDefinitionAffectedSoftware`
- `TenantVulnerabilities`
- `VulnerabilityThreatAssessments`
- `VulnerabilityAssets`
- `VulnerabilityAssetEpisodes`
- `VulnerabilityAssetAssessments`
- `VulnerabilityEpisodeRiskAssessments`
- `SoftwareVulnerabilityMatches`

**Query filters to remove from `PatchHoundDbContext.OnModelCreating`:**

- any `modelBuilder.Entity<TenantVulnerability>().HasQueryFilter(...)`
- any `modelBuilder.Entity<VulnerabilityAsset>().HasQueryFilter(...)`
- `VulnerabilityAssetEpisode`, `VulnerabilityAssetAssessment`, `VulnerabilityEpisodeRiskAssessment` filters

- [ ] **Step 1: Delete the entity and configuration files**

```bash
git rm src/PatchHound.Core/Entities/VulnerabilityDefinition.cs \
       src/PatchHound.Core/Entities/VulnerabilityDefinitionReference.cs \
       src/PatchHound.Core/Entities/VulnerabilityDefinitionAffectedSoftware.cs \
       src/PatchHound.Core/Entities/TenantVulnerability.cs \
       src/PatchHound.Core/Entities/VulnerabilityThreatAssessment.cs \
       src/PatchHound.Core/Entities/VulnerabilityAsset.cs \
       src/PatchHound.Core/Entities/VulnerabilityAssetEpisode.cs \
       src/PatchHound.Core/Entities/VulnerabilityAssetAssessment.cs \
       src/PatchHound.Core/Entities/VulnerabilityEpisodeRiskAssessment.cs \
       src/PatchHound.Core/Entities/SoftwareVulnerabilityMatch.cs \
       src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityDefinitionConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityDefinitionReferenceConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityDefinitionAffectedSoftwareConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/TenantVulnerabilityConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityThreatAssessmentConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityAssetConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityAssetEpisodeConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityAssetAssessmentConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityEpisodeRiskAssessmentConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/SoftwareVulnerabilityMatchConfiguration.cs \
       src/PatchHound.Infrastructure/Data/Configurations/NormalizedSoftwareVulnerabilityProjectionConfiguration.cs
```

- [ ] **Step 2: Remove DbSet declarations and query filters from `PatchHoundDbContext.cs`**

Edit `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`:

- Remove the ten legacy DbSet lines listed above.
- Remove every `modelBuilder.Entity<LegacyType>()...` block that references the deleted types (query filters, `HasMany(...)` on legacy navigations, etc.).
- Remove any `OrganizationalSeverity.TenantVulnerabilityId` FK registration. Change `OrganizationalSeverity` configuration to FK `VulnerabilityId` (column rename via EF model — no migration since Phase 2 does not generate migrations).

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: clean. If any file still references the deleted types, fix them now (they should have been handled in Task 14, but the delete surface forces a final pass).

- [ ] **Step 4: Commit**

```bash
git add -u
git commit -m "feat(phase-2): delete legacy vulnerability + exposure-asset entities and configurations"
```

---

## Task 17: Grep sweep — no legacy vulnerability references remain

- [ ] **Step 1: Run these greps; each must return zero matches (excluding `docs/` archives and `tests/fixtures/`)**

```bash
grep -rn "VulnerabilityDefinition" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "TenantVulnerability" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "VulnerabilityThreatAssessment" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "VulnerabilityAsset" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "VulnerabilityEpisodeRiskAssessment" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "SoftwareVulnerabilityMatch" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "StagedVulnerabilityMergeService" src/ --include="*.cs" ; echo "---"
grep -rn "NormalizedSoftwareVulnerabilityProjection" src/ --include="*.cs" | grep -v "Migrations/" ; echo "---"
grep -rn "tenantVulnerabilityId" web/src --include="*.ts" --include="*.tsx" ; echo "---"
grep -rn "vulnerabilityDefinitionId" web/src --include="*.ts" --include="*.tsx" ; echo "---"
```

All expected to return zero hits.

- [ ] **Step 2: Check `IgnoreQueryFilters` usage**

```bash
grep -rn "IgnoreQueryFilters" src/ --include="*.cs" | grep -v "Migrations/"
```

Expected: only inside files under `src/PatchHound.Infrastructure/Services/` that are explicitly system-context (`VulnerabilityResolver`, `ThreatAssessmentService`, `NvdVulnerabilityEnrichmentRunner`, `DefenderVulnerabilityEnrichmentRunner`, `IngestionService`, `StagedDeviceMergeService`, `DeviceResolver` from Phase 1). Any hit outside that allow-list is a spec §4.10 Rule 5 violation and must be fixed.

- [ ] **Step 3: Record the allow-list in the Phase 2 PR description**

Append the allow-list to the PR description under a "§4.10 Rule 5 — `IgnoreQueryFilters()` allow-list" section so the spec-reviewer can verify it.

- [ ] **Step 4: Commit (if any cleanup was needed)**

If the greps returned unexpected hits that required fixes, commit them with `chore(phase-2): remove lingering legacy vulnerability references`.

---

## Task 18: Extend `TenantIsolationEndToEndTests` with Phase 2 assertions

**Files:**
- Modify: `tests/PatchHound.Api.Tests/EndToEnd/TenantIsolationEndToEndTests.cs`

Phase 2 does not introduce new tenant-scoped entities (all four new entities are global). The tenant isolation test extension focuses on:

1. `GET /api/vulnerabilities` for tenant A and tenant B both return the **same** global vulnerability rows (no leak either direction; this proves global data is not wrongly tenant-filtered).
2. `GET /api/vulnerabilities/{id}` returns the same canonical detail for tenant A and tenant B, and both see `exposures.dataAvailable === false` (no cross-tenant affected-devices leak).
3. A direct `PatchHoundDbContext` write of a `Vulnerability` row **from a request-scoped (non-system) context** throws or fails silently — add an explicit test asserting the write does not appear in the database (the DI wiring must not set `IsSystemContext = true` outside system-context services).

- [ ] **Step 1: Add assertions**

```csharp
[Fact]
public async Task Phase2_vulnerabilities_list_is_global_and_consistent_across_tenants()
{
    await using var app = await TestApiFactory.CreateAsync();
    // Seed one global vulnerability via system-context
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        db.SetSystemContext(true);
        db.Vulnerabilities.Add(Vulnerability.Create("nvd", "CVE-2026-ABCD", "Shared", "Shared", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        db.SetSystemContext(false);
    }

    var tenantAClient = app.CreateAuthenticatedClient(TenantAId);
    var tenantBClient = app.CreateAuthenticatedClient(TenantBId);

    var listA = await tenantAClient.GetFromJsonAsync<PagedResponse<VulnerabilityDto>>("/api/vulnerabilities");
    var listB = await tenantBClient.GetFromJsonAsync<PagedResponse<VulnerabilityDto>>("/api/vulnerabilities");

    Assert.Equal(1, listA!.Items.Count);
    Assert.Equal(1, listB!.Items.Count);
    Assert.Equal("CVE-2026-ABCD", listA.Items[0].ExternalId);
    Assert.Equal("CVE-2026-ABCD", listB.Items[0].ExternalId);
    Assert.False(listA.Items[0].ExposureDataAvailable);
    Assert.False(listB.Items[0].ExposureDataAvailable);
}

[Fact]
public async Task Phase2_vulnerability_detail_is_global_and_exposes_no_cross_tenant_data()
{
    // ... seed one vulnerability + one reference; assert both tenants get the same detail
    // with exposures.dataAvailable == false
}

[Fact]
public async Task Phase2_request_scoped_context_does_not_leak_system_context_flag()
{
    await using var app = await TestApiFactory.CreateAsync();
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
    Assert.False(db.IsSystemContext);   // default is false
}
```

- [ ] **Step 2: Run — PASS**

- [ ] **Step 3: Commit**

```bash
git add tests/PatchHound.Api.Tests/EndToEnd/TenantIsolationEndToEndTests.cs
git commit -m "test(phase-2): extend tenant isolation e2e with canonical vulnerability assertions"
```

---

## Task 19: Final build, test suite, and PR description

- [ ] **Step 1: Clean build + full test pass**

```bash
dotnet clean && dotnet build --no-incremental
dotnet test
cd web && npm run typecheck && npm test -- --run && cd ..
```

All green.

- [ ] **Step 2: Per-phase exit checklist (spec §6)**

- [ ] `dotnet build` clean (0 warnings, 0 errors)
- [ ] `dotnet test` green
- [ ] `npm run typecheck` clean
- [ ] `npm test` green
- [ ] Grep sweep (Task 17) clean
- [ ] No new dual-path / fallback code introduced
- [ ] PR description section: "Entities deleted" lists all 10 legacy entities + 11 configuration files
- [ ] PR description section: "Global entities added" lists `Vulnerability`, `VulnerabilityReference`, `VulnerabilityApplicability`, `ThreatAssessment` with `TenantId=none, QueryFilter=none, Justification="Public CVE/applicability/threat reference data (spec §4.10 Rule 3)"`
- [ ] PR description section: "IgnoreQueryFilters allow-list" lists every service file where `IgnoreQueryFilters()` is called and its justification
- [ ] PR description section: "Exposure gap" explicitly states that vulnerability list/detail returns `ExposureDataAvailable=false` until Phase 3 merges and that `/api/vulnerabilities/{id}/organizational-severity` and `/api/vulnerabilities/{id}/ai-report` return `409 Conflict` with a migration-in-progress message
- [ ] PR description section: "Phase 2 scope extension" calls out that `VulnerabilityAsset*`, `VulnerabilityEpisodeRiskAssessment`, and `SoftwareVulnerabilityMatch*` were deleted in Phase 2 rather than Phase 3 because they FK into `TenantVulnerability`; Phase 3 will therefore introduce `DeviceVulnerabilityExposure` et al. from a clean slate

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin data-model-canonical-cleanup-phase-2
gh pr create --title "Data model canonical cleanup — phase 2: canonical vulnerability knowledge" --body "$(cat <<'EOF'
## Summary
- Introduces canonical global vulnerability knowledge: `Vulnerability`, `VulnerabilityReference`, `VulnerabilityApplicability`, `ThreatAssessment`.
- Rewrites `DefenderVulnerabilitySource`, `NvdVulnerabilityEnrichmentRunner`, `DefenderVulnerabilityEnrichmentRunner`, and `VulnerabilitiesController`/`VulnerabilityDetailQueryService` against canonical tables.
- Deletes the entire `VulnerabilityDefinition*`, `TenantVulnerability`, `VulnerabilityThreatAssessment`, `VulnerabilityAsset*`, `VulnerabilityEpisodeRiskAssessment`, `SoftwareVulnerabilityMatch*`, and `NormalizedSoftwareVulnerabilityProjection*` chain.
- Vulnerability list/detail returns canonical data with `ExposureDataAvailable=false`; exposure data is restored in Phase 3.

## Entities deleted
- `VulnerabilityDefinition`, `VulnerabilityDefinitionReference`, `VulnerabilityDefinitionAffectedSoftware`
- `TenantVulnerability`
- `VulnerabilityThreatAssessment`
- `VulnerabilityAsset`, `VulnerabilityAssetEpisode`, `VulnerabilityAssetAssessment`
- `VulnerabilityEpisodeRiskAssessment`
- `SoftwareVulnerabilityMatch`
- Plus their EF configurations.

## Global entities added (all no-TenantId, no-QueryFilter)
| Entity | Justification |
|---|---|
| `Vulnerability` | Public CVE record; spec §4.10 Rule 3 |
| `VulnerabilityReference` | Public advisory link; spec §4.10 Rule 3 |
| `VulnerabilityApplicability` | Public product-affectedness mapping; spec §4.10 Rule 3 |
| `ThreatAssessment` | Public EPSS/KEV/threat intel; spec §4.10 Rule 3 |

## `IgnoreQueryFilters()` allow-list
- `VulnerabilityResolver` (system-context upsert)
- `ThreatAssessmentService` (system-context upsert)
- `NvdVulnerabilityEnrichmentRunner`, `DefenderVulnerabilityEnrichmentRunner` (system-context enrichment)
- `IngestionService`, `StagedDeviceMergeService`, `DeviceResolver` (inherited from Phase 1)

## Scope extension vs spec §5.2
Phase 2 deletes `VulnerabilityAsset*`, `VulnerabilityEpisodeRiskAssessment`, and `SoftwareVulnerabilityMatch*` (which spec §5.3 listed under Phase 3) because those rows FK into `TenantVulnerability`, which is a Phase 2 deletion target. Phase 3 therefore introduces `DeviceVulnerabilityExposure`, `ExposureEpisode`, `ExposureAssessment` from a clean slate.

## Exposure gap (acceptable per spec §5.2)
- `GET /api/vulnerabilities` returns `ExposureDataAvailable: false, AffectedDeviceCount: 0`
- `GET /api/vulnerabilities/{id}` returns `exposures.dataAvailable: false, dataAvailableReason: "Populated after the Phase 3 canonical exposure merge."`
- `PUT /api/vulnerabilities/{id}/organizational-severity` and `POST /api/vulnerabilities/{id}/ai-report` return `409 Conflict` during the Phase 2 → Phase 3 window.

## Test plan
- [ ] `dotnet build` clean
- [ ] `dotnet test` full suite green
- [ ] `cd web && npm run typecheck && npm test -- --run` green
- [ ] `TenantIsolationEndToEndTests` extended with Phase 2 assertions (global-consistency across tenants)
- [ ] `GlobalEntityWriteProtectionTests` asserts no public endpoint accepts `Vulnerability`/`SoftwareProduct`/`ThreatAssessment`/`SourceSystem` writes

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 4: Mark Phase 2 complete in `docs/data-model-refactor.md` once the PR merges** (Phase 6 will rewrite this doc entirely — for now just append one line: "Phase 2 merged YYYY-MM-DD").
