# Data Model Canonical Cleanup — Phase 4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce `RemediationCase` as the tenant-scoped scope key for all remediation work and rewrite every remediation entity, service, controller, and frontend component to be case-first, dropping `TenantSoftwareId`/`SoftwareAssetId`/`TenantVulnerabilityId` from the remediation domain entirely.

**Architecture:** A `RemediationCase` is uniquely keyed by `(TenantId, SoftwareProductId)` — one case per tenant per product. Cases are created lazily from the remediation entry points (whenever a user opens decision context, a recommendation is filed, or a patching task is requested) via a `RemediationCaseService`. `RemediationWorkflow`, `RemediationDecision`, `PatchingTask`, `ApprovalTask`, `RiskAcceptance`, `AnalystRecommendation`, `AIReport`, `RemediationAiJob`, `RemediationDecisionVulnerabilityOverride`, and `RemediationWorkflowStageRecord` all re-anchor on `RemediationCaseId`. `SoftwareDescriptionJob` re-anchors on `SoftwareProductId` directly. The API surface collapses to `/api/remediation/cases/{caseId}/*` and the frontend routes follow. Spec §5.4.

**Tech Stack:** .NET 10 / EF Core 10 / xUnit / React + TanStack Router + Vitest. Same stack as Phases 1–3.

**Prerequisites:** Phase 3 is merged. `DeviceVulnerabilityExposure`, `ExposureEpisode`, `ExposureAssessment` exist. `SoftwareProduct` exists (Phase 1). `Vulnerability` exists (Phase 2). `TenantSoftware`, `Asset`/`SoftwareAsset`, `TenantVulnerability`, `NormalizedSoftware*` are all gone.

**Phase 3 handoff — dangling FKs Phase 4 must fix:** `RemediationDecisionVulnerabilityOverride`, `RiskAcceptance`, and `AnalystRecommendation` still carry a `TenantVulnerabilityId` column pointing at the deleted `TenantVulnerability` table. Phase 2/3 did **not** re-anchor these. Task 7 already covers `RiskAcceptance` + `AnalystRecommendation`; Task 7b (added here) handles `RemediationDecisionVulnerabilityOverride`. Scope assumptions in downstream tasks (e.g. Task 11) that the override "already points at canonical `Vulnerability.Id`" are stale — do not rely on them.

---

## Preflight

- [ ] **P1: Confirm Phase 3 merged**

Run:
```bash
git log --oneline main -30 | grep -i "phase.3\|canonical-cleanup-phase-3"
```
Expected: at least one commit. If nothing, STOP and wait for Phase 3.

- [ ] **P2: Cut Phase 4 branch**

Run:
```bash
git checkout main && git pull
git checkout -b data-model-canonical-cleanup-phase-4
```

- [ ] **P3: Wipe dev database**

Run:
```bash
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS patchhound;"
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "CREATE DATABASE patchhound;"
```
Expected: `DROP DATABASE` / `CREATE DATABASE`.

- [ ] **P4: Baseline green build**

Run:
```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx
cd frontend && npm run typecheck && npm test -- --run && cd ..
```
Expected: all green. If anything is red on a fresh Phase 3 main, STOP and fix main first.

---

### Task 1: `RemediationCase` entity

**Files:**
- Create: `src/PatchHound.Core/Entities/RemediationCase.cs`
- Create: `src/PatchHound.Core/Enums/RemediationCaseStatus.cs`
- Test: `tests/PatchHound.Tests/Core/Entities/RemediationCaseTests.cs`

- [ ] **Step 1: Write the failing entity test**

Create `tests/PatchHound.Tests/Core/Entities/RemediationCaseTests.cs`:
```csharp
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using Xunit;

namespace PatchHound.Tests.Core.Entities;

public class RemediationCaseTests
{
    [Fact]
    public void Create_sets_tenant_product_and_open_status()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var c = RemediationCase.Create(tenantId, productId);

        Assert.NotEqual(Guid.Empty, c.Id);
        Assert.Equal(tenantId, c.TenantId);
        Assert.Equal(productId, c.SoftwareProductId);
        Assert.Equal(RemediationCaseStatus.Open, c.Status);
        Assert.NotEqual(default, c.CreatedAt);
        Assert.Equal(c.CreatedAt, c.UpdatedAt);
        Assert.Null(c.ClosedAt);
    }

    [Fact]
    public void Create_rejects_empty_tenant()
    {
        Assert.Throws<ArgumentException>(() =>
            RemediationCase.Create(Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void Create_rejects_empty_product()
    {
        Assert.Throws<ArgumentException>(() =>
            RemediationCase.Create(Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void Close_sets_status_and_timestamp()
    {
        var c = RemediationCase.Create(Guid.NewGuid(), Guid.NewGuid());
        c.Close();
        Assert.Equal(RemediationCaseStatus.Closed, c.Status);
        Assert.NotNull(c.ClosedAt);
    }

    [Fact]
    public void Close_is_idempotent()
    {
        var c = RemediationCase.Create(Guid.NewGuid(), Guid.NewGuid());
        c.Close();
        var firstClosedAt = c.ClosedAt;
        c.Close();
        Assert.Equal(firstClosedAt, c.ClosedAt);
    }

    [Fact]
    public void Reopen_from_closed_clears_timestamp()
    {
        var c = RemediationCase.Create(Guid.NewGuid(), Guid.NewGuid());
        c.Close();
        c.Reopen();
        Assert.Equal(RemediationCaseStatus.Open, c.Status);
        Assert.Null(c.ClosedAt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~RemediationCaseTests"`
Expected: FAIL — `RemediationCase`, `RemediationCaseStatus` don't exist.

- [ ] **Step 3: Create the enum**

Create `src/PatchHound.Core/Enums/RemediationCaseStatus.cs`:
```csharp
namespace PatchHound.Core.Enums;

public enum RemediationCaseStatus
{
    Open = 0,
    Closed = 1,
}
```

- [ ] **Step 4: Create the entity**

Create `src/PatchHound.Core/Entities/RemediationCase.cs`:
```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RemediationCase
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public RemediationCaseStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public SoftwareProduct SoftwareProduct { get; private set; } = null!;

    private RemediationCase() { }

    public static RemediationCase Create(Guid tenantId, Guid softwareProductId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (softwareProductId == Guid.Empty)
            throw new ArgumentException("SoftwareProductId is required.", nameof(softwareProductId));

        var now = DateTimeOffset.UtcNow;
        return new RemediationCase
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SoftwareProductId = softwareProductId,
            Status = RemediationCaseStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Close()
    {
        if (Status == RemediationCaseStatus.Closed)
            return;
        Status = RemediationCaseStatus.Closed;
        ClosedAt = DateTimeOffset.UtcNow;
        UpdatedAt = ClosedAt.Value;
    }

    public void Reopen()
    {
        if (Status == RemediationCaseStatus.Open)
            return;
        Status = RemediationCaseStatus.Open;
        ClosedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~RemediationCaseTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Core/Entities/RemediationCase.cs src/PatchHound.Core/Enums/RemediationCaseStatus.cs tests/PatchHound.Tests/Core/Entities/RemediationCaseTests.cs
git commit -m "feat(phase-4): add RemediationCase entity keyed by (TenantId, SoftwareProductId)"
```

---

### Task 2: EF configuration + DbContext wiring for `RemediationCase`

**Files:**
- Create: `src/PatchHound.Infrastructure/Data/Configurations/RemediationCaseConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`

- [ ] **Step 1: Create the configuration**

Create `src/PatchHound.Infrastructure/Data/Configurations/RemediationCaseConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class RemediationCaseConfiguration : IEntityTypeConfiguration<RemediationCase>
{
    public void Configure(EntityTypeBuilder<RemediationCase> builder)
    {
        builder.ToTable("RemediationCases");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.SoftwareProductId).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.SoftwareProductId }).IsUnique();
        builder.HasIndex(c => c.TenantId);

        builder.HasOne(c => c.SoftwareProduct)
            .WithMany()
            .HasForeignKey(c => c.SoftwareProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 2: Add DbSet + query filter to DbContext**

In `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`, add `public DbSet<RemediationCase> RemediationCases => Set<RemediationCase>();` alongside the other remediation DbSets. In the `OnModelCreating` block where other tenant-scoped entities get their query filters, add:
```csharp
modelBuilder.Entity<RemediationCase>()
    .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
```

- [ ] **Step 3: Build**

Run: `dotnet build PatchHound.slnx`
Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/Configurations/RemediationCaseConfiguration.cs src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs
git commit -m "feat(phase-4): wire RemediationCase into EF context with tenant filter"
```

---

### Task 3: `RemediationCaseService` — lazy upsert by `(TenantId, SoftwareProductId)`

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/RemediationCaseService.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/Services/RemediationCaseServiceTests.cs`

- [ ] **Step 1: Write the failing service test**

Create `tests/PatchHound.Tests/Infrastructure/Services/RemediationCaseServiceTests.cs`:
```csharp
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure.TestInfrastructure;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services;

public class RemediationCaseServiceTests
{
    [Fact]
    public async Task GetOrCreate_returns_existing_case_for_same_tenant_and_product()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var productId = Guid.NewGuid();
        ctx.SoftwareProducts.Add(TestData.Product(productId));
        await ctx.SaveChangesAsync();

        var sut = new RemediationCaseService(ctx);
        var first = await sut.GetOrCreateAsync(tenantId, productId, CancellationToken.None);
        var second = await sut.GetOrCreateAsync(tenantId, productId, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, ctx.RemediationCases.Count());
    }

    [Fact]
    public async Task GetOrCreate_creates_separate_case_per_tenant()
    {
        using var ctx = TestDbContextFactory.CreateSystemContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var productId = Guid.NewGuid();
        ctx.SoftwareProducts.Add(TestData.Product(productId));
        await ctx.SaveChangesAsync();

        var sut = new RemediationCaseService(ctx);
        var a = await sut.GetOrCreateAsync(tenantA, productId, CancellationToken.None);
        var b = await sut.GetOrCreateAsync(tenantB, productId, CancellationToken.None);

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(tenantA, a.TenantId);
        Assert.Equal(tenantB, b.TenantId);
    }

    [Fact]
    public async Task GetOrCreate_rejects_unknown_product()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var sut = new RemediationCaseService(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.GetOrCreateAsync(tenantId, Guid.NewGuid(), CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~RemediationCaseServiceTests"`
Expected: FAIL — `RemediationCaseService` does not exist.

- [ ] **Step 3: Implement the service**

Create `src/PatchHound.Infrastructure/Services/RemediationCaseService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RemediationCaseService(PatchHoundDbContext db)
{
    public async Task<RemediationCase> GetOrCreateAsync(
        Guid tenantId,
        Guid softwareProductId,
        CancellationToken ct)
    {
        var existing = await db.RemediationCases
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId && c.SoftwareProductId == softwareProductId,
                ct);
        if (existing is not null)
            return existing;

        var productExists = await db.SoftwareProducts
            .AsNoTracking()
            .AnyAsync(p => p.Id == softwareProductId, ct);
        if (!productExists)
            throw new InvalidOperationException(
                $"SoftwareProduct {softwareProductId} does not exist; cannot create remediation case.");

        var created = RemediationCase.Create(tenantId, softwareProductId);
        db.RemediationCases.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<RemediationCase?> GetAsync(Guid caseId, CancellationToken ct)
    {
        return await db.RemediationCases.FirstOrDefaultAsync(c => c.Id == caseId, ct);
    }
}
```

- [ ] **Step 4: Register in DI**

In `src/PatchHound.Api/Program.cs` (or wherever infrastructure services are registered alongside `RemediationWorkflowService`), add:
```csharp
builder.Services.AddScoped<RemediationCaseService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~RemediationCaseServiceTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/RemediationCaseService.cs tests/PatchHound.Tests/Infrastructure/Services/RemediationCaseServiceTests.cs src/PatchHound.Api/Program.cs
git commit -m "feat(phase-4): add RemediationCaseService for lazy upsert by (tenant, product)"
```

---

### Task 4: Re-anchor `RemediationWorkflow` on `RemediationCaseId`

**Files:**
- Modify: `src/PatchHound.Core/Entities/RemediationWorkflow.cs`
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/RemediationWorkflowConfiguration.cs`
- Test: `tests/PatchHound.Tests/Core/Entities/RemediationWorkflowTests.cs` (new or extend)

- [ ] **Step 1: Write the failing entity test**

Create or extend `tests/PatchHound.Tests/Core/Entities/RemediationWorkflowTests.cs`:
```csharp
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using Xunit;

namespace PatchHound.Tests.Core.Entities;

public class RemediationWorkflowTests
{
    [Fact]
    public void Create_takes_tenant_case_and_owner_team()
    {
        var tenantId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        var w = RemediationWorkflow.Create(tenantId, caseId, teamId);

        Assert.Equal(tenantId, w.TenantId);
        Assert.Equal(caseId, w.RemediationCaseId);
        Assert.Equal(teamId, w.SoftwareOwnerTeamId);
        Assert.Equal(RemediationWorkflowStatus.Active, w.Status);
        Assert.Equal(RemediationWorkflowStage.SecurityAnalysis, w.CurrentStage);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~RemediationWorkflowTests"`
Expected: FAIL — `RemediationCaseId` does not exist on `RemediationWorkflow`.

- [ ] **Step 3: Rewrite the entity**

Replace the body of `src/PatchHound.Core/Entities/RemediationWorkflow.cs` with:
```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RemediationWorkflow
{
    private readonly List<RemediationWorkflowStageRecord> _stageRecords = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationCaseId { get; private set; }
    public Guid SoftwareOwnerTeamId { get; private set; }
    public Guid? RecurrenceSourceWorkflowId { get; private set; }
    public RemediationWorkflowStage CurrentStage { get; private set; }
    public RemediationWorkflowStatus Status { get; private set; }
    public RemediationOutcome? ProposedOutcome { get; private set; }
    public RemediationWorkflowPriority? Priority { get; private set; }
    public RemediationWorkflowApprovalMode ApprovalMode { get; private set; }
    public DateTimeOffset CurrentStageStartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public RemediationCase RemediationCase { get; private set; } = null!;
    public IReadOnlyCollection<RemediationWorkflowStageRecord> StageRecords => _stageRecords.AsReadOnly();

    private RemediationWorkflow() { }

    public static RemediationWorkflow Create(
        Guid tenantId,
        Guid remediationCaseId,
        Guid softwareOwnerTeamId,
        RemediationWorkflowStage initialStage = RemediationWorkflowStage.SecurityAnalysis,
        Guid? recurrenceSourceWorkflowId = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (remediationCaseId == Guid.Empty)
            throw new ArgumentException("RemediationCaseId is required.", nameof(remediationCaseId));

        var now = DateTimeOffset.UtcNow;
        return new RemediationWorkflow
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationCaseId = remediationCaseId,
            SoftwareOwnerTeamId = softwareOwnerTeamId,
            RecurrenceSourceWorkflowId = recurrenceSourceWorkflowId,
            CurrentStage = initialStage,
            Status = RemediationWorkflowStatus.Active,
            ApprovalMode = RemediationWorkflowApprovalMode.None,
            CurrentStageStartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetDecisionContext(
        RemediationOutcome? proposedOutcome,
        RemediationWorkflowPriority? priority,
        RemediationWorkflowApprovalMode approvalMode)
    {
        ProposedOutcome = proposedOutcome;
        Priority = priority;
        ApprovalMode = approvalMode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MoveToStage(RemediationWorkflowStage stage)
    {
        CurrentStage = stage;
        CurrentStageStartedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete()
    {
        Status = RemediationWorkflowStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        Status = RemediationWorkflowStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

Note: `ReassignTenantSoftware` is deleted entirely. A case is stable per `(TenantId, SoftwareProductId)`; reassignment no longer exists.

- [ ] **Step 4: Update EF configuration**

In `src/PatchHound.Infrastructure/Data/Configurations/RemediationWorkflowConfiguration.cs`:
- Remove the `TenantSoftwareId` property config and index.
- Add `builder.Property(w => w.RemediationCaseId).IsRequired();`
- Add `builder.HasOne(w => w.RemediationCase).WithMany().HasForeignKey(w => w.RemediationCaseId).OnDelete(DeleteBehavior.Restrict);`
- Add `builder.HasIndex(w => new { w.TenantId, w.RemediationCaseId, w.Status });`

- [ ] **Step 5: Run the entity test to verify it passes**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~RemediationWorkflowTests"`
Expected: PASS.

(The solution will not yet build because downstream services still pass `tenantSoftwareId`. That is fixed in later tasks. Skip the full build here.)

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Core/Entities/RemediationWorkflow.cs src/PatchHound.Infrastructure/Data/Configurations/RemediationWorkflowConfiguration.cs tests/PatchHound.Tests/Core/Entities/RemediationWorkflowTests.cs
git commit -m "refactor(phase-4): anchor RemediationWorkflow on RemediationCaseId"
```

---

### Task 5: Re-anchor `RemediationDecision` on `RemediationCaseId`

**Files:**
- Modify: `src/PatchHound.Core/Entities/RemediationDecision.cs`
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/RemediationDecisionConfiguration.cs`

- [ ] **Step 1: Rewrite the entity**

Replace `RemediationDecision` with:
```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RemediationDecision
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationCaseId { get; private set; }
    public Guid? RemediationWorkflowId { get; private set; }
    public RemediationOutcome Outcome { get; private set; }
    public DecisionApprovalStatus ApprovalStatus { get; private set; }
    public string Justification { get; private set; } = null!;
    public Guid DecidedBy { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? MaintenanceWindowDate { get; private set; }
    public DateTimeOffset? ExpiryDate { get; private set; }
    public DateTimeOffset? ReEvaluationDate { get; private set; }
    public DateTimeOffset? LastSlaNotifiedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public RemediationCase RemediationCase { get; private set; } = null!;
    public RemediationWorkflow? RemediationWorkflow { get; private set; }
    public ICollection<RemediationDecisionVulnerabilityOverride> VulnerabilityOverrides { get; private set; } = [];

    private static readonly RemediationOutcome[] OutcomesRequiringApproval =
    [
        RemediationOutcome.RiskAcceptance,
        RemediationOutcome.AlternateMitigation,
    ];

    private static readonly RemediationOutcome[] OutcomesRequiringJustification =
    [
        RemediationOutcome.RiskAcceptance,
        RemediationOutcome.AlternateMitigation,
        RemediationOutcome.PatchingDeferred,
    ];

    private RemediationDecision() { }

    public static RemediationDecision Create(
        Guid tenantId,
        Guid remediationCaseId,
        RemediationOutcome outcome,
        string? justification,
        Guid decidedBy,
        DecisionApprovalStatus? initialApprovalStatus = null,
        DateTimeOffset? expiryDate = null,
        DateTimeOffset? reEvaluationDate = null,
        DateTimeOffset? maintenanceWindowDate = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (remediationCaseId == Guid.Empty)
            throw new ArgumentException("RemediationCaseId is required.", nameof(remediationCaseId));

        if (OutcomesRequiringJustification.Contains(outcome) && string.IsNullOrWhiteSpace(justification))
            throw new ArgumentException($"Justification is required for {outcome}.");

        if (outcome == RemediationOutcome.PatchingDeferred && !reEvaluationDate.HasValue)
            throw new ArgumentException("Re-evaluation date is required for PatchingDeferred.");

        var now = DateTimeOffset.UtcNow;
        var requiresApproval = initialApprovalStatus.HasValue
            ? initialApprovalStatus.Value == DecisionApprovalStatus.PendingApproval
            : OutcomesRequiringApproval.Contains(outcome);
        var approvalStatus = initialApprovalStatus
            ?? (requiresApproval ? DecisionApprovalStatus.PendingApproval : DecisionApprovalStatus.Approved);

        return new RemediationDecision
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationCaseId = remediationCaseId,
            Outcome = outcome,
            ApprovalStatus = approvalStatus,
            Justification = justification ?? string.Empty,
            DecidedBy = decidedBy,
            DecidedAt = now,
            ApprovedBy = approvalStatus == DecisionApprovalStatus.Approved ? decidedBy : null,
            ApprovedAt = approvalStatus == DecisionApprovalStatus.Approved ? now : null,
            MaintenanceWindowDate = maintenanceWindowDate,
            ExpiryDate = expiryDate,
            ReEvaluationDate = reEvaluationDate,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Approve(Guid approvedBy)
    {
        if (ApprovalStatus != DecisionApprovalStatus.PendingApproval)
            throw new InvalidOperationException($"Cannot approve a decision with status {ApprovalStatus}.");
        ApprovedBy = approvedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        ApprovalStatus = DecisionApprovalStatus.Approved;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetMaintenanceWindowDate(DateTimeOffset? maintenanceWindowDate)
    {
        MaintenanceWindowDate = maintenanceWindowDate;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(Guid rejectedBy)
    {
        if (ApprovalStatus != DecisionApprovalStatus.PendingApproval)
            throw new InvalidOperationException($"Cannot reject a decision with status {ApprovalStatus}.");
        ApprovedBy = rejectedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        ApprovalStatus = DecisionApprovalStatus.Rejected;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Expire()
    {
        ApprovalStatus = DecisionApprovalStatus.Expired;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSlaNotified()
    {
        LastSlaNotifiedAt = DateTimeOffset.UtcNow;
    }

    public void AttachToWorkflow(Guid remediationWorkflowId)
    {
        RemediationWorkflowId = remediationWorkflowId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

Note: `SoftwareAsset`, `ReassignTenantSoftware`, `TenantSoftwareId`, `SoftwareAssetId` all removed.

- [ ] **Step 2: Update EF configuration**

In `RemediationDecisionConfiguration.cs`:
- Remove `TenantSoftwareId`, `SoftwareAssetId`, `SoftwareAsset` navigation config.
- Add `builder.Property(d => d.RemediationCaseId).IsRequired();`
- Add `builder.HasOne(d => d.RemediationCase).WithMany().HasForeignKey(d => d.RemediationCaseId).OnDelete(DeleteBehavior.Restrict);`
- Add index `builder.HasIndex(d => new { d.TenantId, d.RemediationCaseId, d.ApprovalStatus });`

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Core/Entities/RemediationDecision.cs src/PatchHound.Infrastructure/Data/Configurations/RemediationDecisionConfiguration.cs
git commit -m "refactor(phase-4): anchor RemediationDecision on RemediationCaseId"
```

---

### Task 6: Re-anchor `PatchingTask` on `RemediationCaseId`

**Files:**
- Modify: `src/PatchHound.Core/Entities/PatchingTask.cs`
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/PatchingTaskConfiguration.cs`

- [ ] **Step 1: Rewrite the entity**

Replace `PatchingTask` with:
```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class PatchingTask
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationCaseId { get; private set; }
    public Guid? RemediationWorkflowId { get; private set; }
    public Guid RemediationDecisionId { get; private set; }
    public Guid OwnerTeamId { get; private set; }
    public PatchingTaskStatus Status { get; private set; }
    public DateTimeOffset DueDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public RemediationCase RemediationCase { get; private set; } = null!;
    public RemediationDecision RemediationDecision { get; private set; } = null!;
    public RemediationWorkflow? RemediationWorkflow { get; private set; }

    private PatchingTask() { }

    public static PatchingTask Create(
        Guid tenantId,
        Guid remediationCaseId,
        Guid remediationDecisionId,
        Guid ownerTeamId,
        DateTimeOffset dueDate)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.");
        if (remediationCaseId == Guid.Empty) throw new ArgumentException("RemediationCaseId is required.");
        var now = DateTimeOffset.UtcNow;
        return new PatchingTask
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationCaseId = remediationCaseId,
            RemediationDecisionId = remediationDecisionId,
            OwnerTeamId = ownerTeamId,
            Status = PatchingTaskStatus.Pending,
            DueDate = dueDate,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Start()
    {
        Status = PatchingTaskStatus.InProgress;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete()
    {
        Status = PatchingTaskStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AttachToWorkflow(Guid remediationWorkflowId)
    {
        RemediationWorkflowId = remediationWorkflowId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 2: Update EF configuration**

In `PatchingTaskConfiguration.cs`:
- Remove `TenantSoftwareId`, `SoftwareAssetId` property config and any indexes on them.
- Add `builder.Property(t => t.RemediationCaseId).IsRequired();`
- Add `builder.HasOne(t => t.RemediationCase).WithMany().HasForeignKey(t => t.RemediationCaseId).OnDelete(DeleteBehavior.Restrict);`
- Add `builder.HasIndex(t => new { t.TenantId, t.RemediationCaseId, t.Status });`

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Core/Entities/PatchingTask.cs src/PatchHound.Infrastructure/Data/Configurations/PatchingTaskConfiguration.cs
git commit -m "refactor(phase-4): anchor PatchingTask on RemediationCaseId"
```

---

### Task 7: Re-anchor sidecar entities on `RemediationCaseId`

**Files:**
- Modify: `src/PatchHound.Core/Entities/ApprovalTask.cs`
- Modify: `src/PatchHound.Core/Entities/RiskAcceptance.cs`
- Modify: `src/PatchHound.Core/Entities/AnalystRecommendation.cs`
- Modify: `src/PatchHound.Core/Entities/AIReport.cs`
- Modify: `src/PatchHound.Core/Entities/RemediationAiJob.cs`
- Modify: Corresponding configurations under `src/PatchHound.Infrastructure/Data/Configurations/`

- [ ] **Step 1: `ApprovalTask` — add `RemediationCaseId`**

Add `public Guid RemediationCaseId { get; private set; }` to `ApprovalTask`. Change `Create` signature to:
```csharp
public static ApprovalTask Create(
    Guid tenantId,
    Guid remediationCaseId,
    Guid remediationDecisionId,
    RemediationOutcome outcome,
    ApprovalTaskStatus? initialStatus,
    DateTimeOffset expiresAt)
```
Set `RemediationCaseId = remediationCaseId` in the constructor. Update `ApprovalTaskConfiguration` to add `builder.Property(a => a.RemediationCaseId).IsRequired();` + `builder.HasIndex(a => new { a.TenantId, a.RemediationCaseId, a.Status });`.

- [ ] **Step 2: `RiskAcceptance` — replace `TenantVulnerabilityId` with `RemediationCaseId`**

Replace the class with:
```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RiskAcceptance
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationCaseId { get; private set; }
    public Guid? VulnerabilityId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public RiskAcceptanceStatus Status { get; private set; }
    public string Justification { get; private set; } = null!;
    public string? Conditions { get; private set; }
    public DateTimeOffset? ExpiryDate { get; private set; }
    public int? ReviewFrequency { get; private set; }
    public DateTimeOffset? NextReviewDate { get; private set; }

    public RemediationCase RemediationCase { get; private set; } = null!;

    private RiskAcceptance() { }

    public static RiskAcceptance Create(
        Guid tenantId,
        Guid remediationCaseId,
        Guid requestedBy,
        string justification,
        Guid? vulnerabilityId = null,
        string? conditions = null,
        DateTimeOffset? expiryDate = null,
        int? reviewFrequency = null,
        DateTimeOffset? nextReviewDate = null)
    {
        if (string.IsNullOrWhiteSpace(justification))
            throw new ArgumentException("Justification is required.", nameof(justification));

        return new RiskAcceptance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationCaseId = remediationCaseId,
            VulnerabilityId = vulnerabilityId,
            RequestedBy = requestedBy,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = RiskAcceptanceStatus.Pending,
            Justification = justification,
            Conditions = conditions,
            ExpiryDate = expiryDate,
            ReviewFrequency = reviewFrequency,
            NextReviewDate = nextReviewDate,
        };
    }

    public void Approve(Guid approvedBy, string? conditions = null, DateTimeOffset? expiryDate = null, int? reviewFrequency = null)
    {
        ApprovedBy = approvedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        Status = RiskAcceptanceStatus.Approved;
        if (conditions is not null) Conditions = conditions;
        if (expiryDate.HasValue) ExpiryDate = expiryDate;
        if (reviewFrequency.HasValue) ReviewFrequency = reviewFrequency;
    }

    public void Reject(Guid rejectedBy)
    {
        ApprovedBy = rejectedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        Status = RiskAcceptanceStatus.Rejected;
    }

    public void Expire() => Status = RiskAcceptanceStatus.Expired;
}
```

Update `RiskAcceptanceConfiguration.cs`: drop `TenantVulnerabilityId` / `AssetId` config, add `RemediationCaseId` required + FK + index `(TenantId, RemediationCaseId, Status)`.

- [ ] **Step 3: `AnalystRecommendation` — anchor on `RemediationCaseId`**

Replace `SoftwareAssetId` and `TenantVulnerabilityId` with `RemediationCaseId` (required) and `VulnerabilityId` (nullable, references canonical `Vulnerability`). New `Create`:
```csharp
public static AnalystRecommendation Create(
    Guid tenantId,
    Guid remediationCaseId,
    RemediationOutcome recommendedOutcome,
    string rationale,
    Guid analystId,
    Guid? vulnerabilityId = null,
    string? priorityOverride = null)
```
Update `Update` method signature symmetrically. Update configuration: remove `SoftwareAssetId`, `TenantVulnerabilityId`; add `RemediationCaseId` required + FK + index.

- [ ] **Step 4: `AIReport` — anchor on `RemediationCaseId`**

Replace `TenantVulnerabilityId` with `RemediationCaseId`. Add optional `VulnerabilityId` if the report is vulnerability-specific (nullable, references canonical `Vulnerability`). Drop the `TenantVulnerability` navigation property. New `Create`:
```csharp
public static AIReport Create(
    Guid tenantId,
    Guid remediationCaseId,
    Guid tenantAiProfileId,
    string content,
    string providerType,
    string profileName,
    string model,
    string systemPromptHash,
    decimal temperature,
    int maxOutputTokens,
    Guid generatedBy,
    Guid? vulnerabilityId = null)
```
Update `AIReportConfiguration` — drop `TenantVulnerability` FK; add `RemediationCaseId` required + FK + index `(TenantId, RemediationCaseId, GeneratedAt)`.

- [ ] **Step 5: `RemediationAiJob` — replace `TenantSoftwareId` with `RemediationCaseId`**

Replace `TenantSoftwareId` with `RemediationCaseId`. New `Create` and `Refresh` signatures take `remediationCaseId`. Update configuration: drop `TenantSoftwareId`; add `RemediationCaseId` + unique index `(TenantId, RemediationCaseId)`.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Core/Entities/ApprovalTask.cs src/PatchHound.Core/Entities/RiskAcceptance.cs src/PatchHound.Core/Entities/AnalystRecommendation.cs src/PatchHound.Core/Entities/AIReport.cs src/PatchHound.Core/Entities/RemediationAiJob.cs src/PatchHound.Infrastructure/Data/Configurations/ApprovalTaskConfiguration.cs src/PatchHound.Infrastructure/Data/Configurations/RiskAcceptanceConfiguration.cs src/PatchHound.Infrastructure/Data/Configurations/AnalystRecommendationConfiguration.cs src/PatchHound.Infrastructure/Data/Configurations/AIReportConfiguration.cs src/PatchHound.Infrastructure/Data/Configurations/RemediationAiJobConfiguration.cs
git commit -m "refactor(phase-4): anchor ApprovalTask/RiskAcceptance/AnalystRecommendation/AIReport/RemediationAiJob on RemediationCaseId"
```

---

### Task 7b: Re-anchor `RemediationDecisionVulnerabilityOverride` on canonical `VulnerabilityId`

**Context:** Phase 2/3 deleted the `TenantVulnerability` table but left `RemediationDecisionVulnerabilityOverride.TenantVulnerabilityId` in place as a dangling FK. This task replaces it with canonical `VulnerabilityId` pointing at the global `Vulnerability` table. Must land before Task 11 rewrites `AddVulnerabilityOverrideAsync`.

**Files:**
- Modify: `src/PatchHound.Core/Entities/RemediationDecisionVulnerabilityOverride.cs`
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/RemediationDecisionVulnerabilityOverrideConfiguration.cs`
- Add: EF migration `RemediationOverrideVulnerabilityReanchor`

**Steps:**

- [ ] **Step 1: Entity field rename**

Replace `public Guid TenantVulnerabilityId { get; private set; }` with `public Guid VulnerabilityId { get; private set; }`. Update `Create(...)` factory signature + body symmetrically. Add `public Vulnerability Vulnerability { get; private set; } = null!;` navigation (global entity, no tenant filter). Remove any residual `TenantVulnerability` navigation.

- [ ] **Step 2: Configuration**

In `RemediationDecisionVulnerabilityOverrideConfiguration.Configure`: drop the `TenantVulnerabilityId` index, add `builder.HasIndex(vo => new { vo.RemediationDecisionId, vo.VulnerabilityId }).IsUnique();` and `builder.HasOne(vo => vo.Vulnerability).WithMany().HasForeignKey(vo => vo.VulnerabilityId).OnDelete(DeleteBehavior.Restrict);`.

- [ ] **Step 3: EF migration**

```bash
dotnet ef migrations add RemediationOverrideVulnerabilityReanchor --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api
```

Review the generated migration: it must **rename** the column (not drop + add), preserving existing row data only if the old `TenantVulnerability.VulnerabilityId` mapping is still derivable. If it is not (TenantVulnerability rows were dropped in Phase 2 without backfill), the migration must:
1. Drop the `TenantVulnerabilityId` column.
2. Add `VulnerabilityId` nullable, then mark nullable=false after backfill from application state. Since the deleted-table rows are gone, existing override rows have no canonical target — **delete orphan override rows** in the migration's `Up` body before setting the column to non-nullable. Document the data loss in the migration file's comment header.

- [ ] **Step 4: Callers**

Update every caller of `RemediationDecisionVulnerabilityOverride.Create(...)` and every `vo.TenantVulnerabilityId` reference to use `VulnerabilityId`. Controllers (`RemediationDecisionsController`), query services (`RemediationDecisionQueryService`), services (`RemediationDecisionService`), and the DTO (`RemediationDecisionDto.VulnerabilityOverrideDto`) all change. DTO field name changes from `TenantVulnerabilityId` → `VulnerabilityId` — frontend consumers must follow (handled in Task 20).

- [ ] **Step 5: Build and commit**

```bash
dotnet build
git add -A
git commit -m "refactor(phase-4): re-anchor RemediationDecisionVulnerabilityOverride on canonical VulnerabilityId"
```

---

### Task 8: Re-anchor `SoftwareDescriptionJob` on `SoftwareProductId`

**Files:**
- Modify: `src/PatchHound.Core/Entities/SoftwareDescriptionJob.cs`
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/SoftwareDescriptionJobConfiguration.cs`

- [ ] **Step 1: Rewrite the entity**

Replace with:
```csharp
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class SoftwareDescriptionJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public Guid? TenantAiProfileId { get; private set; }
    public SoftwareDescriptionJobStatus Status { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SoftwareDescriptionJob() { }

    public static SoftwareDescriptionJob Create(
        Guid tenantId,
        Guid softwareProductId,
        Guid? tenantAiProfileId,
        DateTimeOffset requestedAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.");
        if (softwareProductId == Guid.Empty) throw new ArgumentException("SoftwareProductId is required.");
        return new SoftwareDescriptionJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SoftwareProductId = softwareProductId,
            TenantAiProfileId = tenantAiProfileId,
            Status = SoftwareDescriptionJobStatus.Pending,
            RequestedAt = requestedAt,
            UpdatedAt = requestedAt,
        };
    }

    public void Start(DateTimeOffset startedAt)
    {
        Status = SoftwareDescriptionJobStatus.Running;
        StartedAt = startedAt;
        Error = string.Empty;
        UpdatedAt = startedAt;
    }

    public void CompleteSucceeded(DateTimeOffset completedAt)
    {
        Status = SoftwareDescriptionJobStatus.Succeeded;
        CompletedAt = completedAt;
        Error = string.Empty;
        UpdatedAt = completedAt;
    }

    public void CompleteFailed(DateTimeOffset completedAt, string error)
    {
        Status = SoftwareDescriptionJobStatus.Failed;
        CompletedAt = completedAt;
        Error = error;
        UpdatedAt = completedAt;
    }
}
```

Note: `TenantSoftwareId` and `NormalizedSoftwareId` both removed. `TenantSoftwareProductInsight` is the tenant-scoped shadow for descriptions (Phase 1); this job writes *into* that insight table once complete — that wiring belongs in the service, not the entity.

- [ ] **Step 2: Update EF configuration**

In `SoftwareDescriptionJobConfiguration.cs`:
- Drop `TenantSoftwareId`, `NormalizedSoftwareId`.
- Add `builder.Property(j => j.SoftwareProductId).IsRequired();`
- Add `builder.HasIndex(j => new { j.TenantId, j.SoftwareProductId, j.Status });`
- No FK navigation to `SoftwareProduct` since the job references a global row — keep it as a bare column with an index.

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Core/Entities/SoftwareDescriptionJob.cs src/PatchHound.Infrastructure/Data/Configurations/SoftwareDescriptionJobConfiguration.cs
git commit -m "refactor(phase-4): anchor SoftwareDescriptionJob on SoftwareProductId"
```

---

### Task 9: Drop `RemediationWorkflowStageRecord` `TenantSoftwareId` fallout

**Files:**
- Modify: `src/PatchHound.Core/Entities/RemediationWorkflowStageRecord.cs` (verify it has no `TenantSoftware*` fields — only `RemediationWorkflowId`)
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/RemediationWorkflowStageRecordConfiguration.cs` (verify)

- [ ] **Step 1: Grep for any `TenantSoftware*` references in the file**

Run: `grep -n "TenantSoftware\|SoftwareAsset" src/PatchHound.Core/Entities/RemediationWorkflowStageRecord.cs`
Expected: no matches. If matches exist, delete them.

- [ ] **Step 2: Confirm config has no stale FKs**

Run: `grep -n "TenantSoftware\|SoftwareAsset" src/PatchHound.Infrastructure/Data/Configurations/RemediationWorkflowStageRecordConfiguration.cs`
Expected: no matches.

- [ ] **Step 3: If no changes, skip commit. Otherwise:**

```bash
git add src/PatchHound.Core/Entities/RemediationWorkflowStageRecord.cs src/PatchHound.Infrastructure/Data/Configurations/RemediationWorkflowStageRecordConfiguration.cs
git commit -m "refactor(phase-4): clean up RemediationWorkflowStageRecord"
```

---

### Task 10: Rewrite `RemediationWorkflowService` case-first

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/RemediationWorkflowService.cs`

- [ ] **Step 1: Replace `GetOrCreateActiveWorkflowAsync` signature**

Change every method that previously took `(tenantId, tenantSoftwareId)` to take `(tenantId, remediationCaseId)`. The owner team lookup now comes from `RemediationCase.SoftwareProduct` → software-team mapping (via `TenantSoftwareProductInsight` if present, otherwise fallback to the tenant's default team). Example signature:

```csharp
public async Task<RemediationWorkflow> GetOrCreateActiveWorkflowAsync(
    Guid tenantId,
    Guid remediationCaseId,
    CancellationToken ct)
{
    var existing = await dbContext.RemediationWorkflows
        .FirstOrDefaultAsync(w =>
            w.TenantId == tenantId
            && w.RemediationCaseId == remediationCaseId
            && w.Status == RemediationWorkflowStatus.Active, ct);
    if (existing is not null) return existing;

    var softwareOwnerTeamId = await ResolveSoftwareOwnerTeamIdAsync(tenantId, remediationCaseId, ct);
    var previousWorkflow = await dbContext.RemediationWorkflows.AsNoTracking()
        .Where(w => w.TenantId == tenantId
            && w.RemediationCaseId == remediationCaseId
            && w.Status != RemediationWorkflowStatus.Active
            && w.ProposedOutcome != null)
        .OrderByDescending(w => w.CreatedAt)
        .FirstOrDefaultAsync(ct);

    var isRecurrence = previousWorkflow is not null;
    var initialStage = isRecurrence
        ? RemediationWorkflowStage.Verification
        : RemediationWorkflowStage.SecurityAnalysis;
    var workflow = RemediationWorkflow.Create(
        tenantId, remediationCaseId, softwareOwnerTeamId, initialStage, previousWorkflow?.Id);
    // ... (carry over ProposedOutcome/Priority/ApprovalMode as before)
    dbContext.RemediationWorkflows.Add(workflow);
    // ... (stage record creation as before, unchanged)
    return workflow;
}
```

- [ ] **Step 2: Rewrite `ResolveSoftwareOwnerTeamIdAsync` to take `remediationCaseId`**

The method loads the `RemediationCase` to get `SoftwareProductId`, then resolves the software owner team via whatever ownership mapping exists (check `TenantSoftwareProductInsight` for an `OwnerTeamId` field, or the tenant default team). If Phase 1 didn't introduce a product-owner mapping, fall back to `tenant.DefaultSoftwareOwnerTeamId`. Leave a `// TODO(phase-5): revisit owner team assignment when dashboard rewrite lands` comment inline.

- [ ] **Step 3: Delete every remaining reference to `TenantSoftwareId` in this file**

Run: `grep -n "TenantSoftware\|tenantSoftware\|SoftwareAsset" src/PatchHound.Infrastructure/Services/RemediationWorkflowService.cs`
Expected: no matches.

- [ ] **Step 4: Build**

Run: `dotnet build PatchHound.slnx 2>&1 | grep -E "error CS" | head -20`
Expected: errors are now confined to callers of this service (next tasks handle them).

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/RemediationWorkflowService.cs
git commit -m "refactor(phase-4): rewrite RemediationWorkflowService case-first"
```

---

### Task 11: Rewrite `RemediationDecisionService` case-first

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/RemediationDecisionService.cs`

- [ ] **Step 1: Rename and rewrite `CreateDecisionForTenantSoftwareAsync` → `CreateDecisionForCaseAsync`**

New signature:
```csharp
public async Task<Result<RemediationDecision>> CreateDecisionForCaseAsync(
    Guid tenantId,
    Guid remediationCaseId,
    RemediationOutcome outcome,
    string? justification,
    Guid decidedBy,
    DateTimeOffset? expiryDate,
    DateTimeOffset? reEvaluationDate,
    CancellationToken ct)
```
Body: verify the case exists for the tenant, construct the decision via `RemediationDecision.Create(tenantId, remediationCaseId, outcome, ...)`, attach to workflow via `workflowService.AttachDecisionAsync(decision, ct)`. All `TenantSoftware` / `SoftwareAsset` lookups and reassignments are deleted.

- [ ] **Step 2: Rewrite `AddVulnerabilityOverrideAsync`**

The method already takes a `decisionId`; after Task 7b re-anchors the override on canonical `VulnerabilityId`, it no longer needs to resolve `TenantVulnerabilityId`. (The "re-anchored in Phase 2" claim in earlier plan drafts is stale — Phase 2 only deleted `TenantVulnerability`; the column rename/drop happens in Task 7b.) Signature:
```csharp
public async Task<Result<RemediationDecisionVulnerabilityOverride>> AddVulnerabilityOverrideAsync(
    Guid decisionId,
    Guid vulnerabilityId,
    RemediationOutcome outcome,
    string justification,
    CancellationToken ct)
```

- [ ] **Step 3: Rewrite `VerifyAndCarryForwardDecisionAsync` and `VerifyAndRequireNewDecisionAsync`**

Both methods now read the previous decision by `RemediationCaseId` instead of `TenantSoftwareId`. The verification flow is otherwise unchanged.

- [ ] **Step 4: Delete all `ReassignTenantSoftware*` methods**

Run: `grep -n "ReassignTenantSoftware\|TenantSoftware\|SoftwareAsset" src/PatchHound.Infrastructure/Services/RemediationDecisionService.cs`
Expected: no matches.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/RemediationDecisionService.cs
git commit -m "refactor(phase-4): rewrite RemediationDecisionService case-first"
```

---

### Task 12: Rewrite remaining remediation services case-first

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/RemediationAiJobService.cs`
- Modify: `src/PatchHound.Infrastructure/Services/AnalystRecommendationService.cs`
- Modify: `src/PatchHound.Infrastructure/Services/ApprovalTaskService.cs`
- Modify: `src/PatchHound.Infrastructure/Services/SoftwareDescriptionJobService.cs` (if present)

- [ ] **Step 1: `RemediationAiJobService.EnqueueAsync`**

Change signature from `(tenantId, tenantSoftwareId, inputHash, ct)` to `(tenantId, remediationCaseId, inputHash, ct)`. All queries that looked up the job by `TenantSoftwareId` now look up by `RemediationCaseId`.

- [ ] **Step 2: `AnalystRecommendationService.AddRecommendationForTenantSoftwareAsync` → `AddRecommendationForCaseAsync`**

New signature:
```csharp
public async Task<Result<AnalystRecommendation>> AddRecommendationForCaseAsync(
    Guid tenantId,
    Guid remediationCaseId,
    RemediationOutcome outcome,
    string rationale,
    Guid analystId,
    Guid? vulnerabilityId,
    string? priorityOverride,
    CancellationToken ct)
```
The body constructs via `AnalystRecommendation.Create(tenantId, remediationCaseId, ...)` and calls `workflowService.AttachRecommendationAsync(tenantId, remediationCaseId, recommendation, ct)`.

- [ ] **Step 3: `ApprovalTaskService` ensure all creation paths pass the case ID**

Any method that builds an `ApprovalTask` now reads the parent `RemediationDecision.RemediationCaseId` and forwards it to `ApprovalTask.Create`.

- [ ] **Step 4: `SoftwareDescriptionJobService` (if present)**

Enqueue signature becomes `(tenantId, softwareProductId, tenantAiProfileId, ct)`. On success, write the description into `TenantSoftwareProductInsight` (tenant-scoped) rather than any legacy software row.

- [ ] **Step 5: Grep sweep**

Run: `grep -rn "TenantSoftware\|SoftwareAsset" src/PatchHound.Infrastructure/Services/ | grep -v "\.cs.bak"`
Expected: no matches.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/RemediationAiJobService.cs src/PatchHound.Infrastructure/Services/AnalystRecommendationService.cs src/PatchHound.Infrastructure/Services/ApprovalTaskService.cs src/PatchHound.Infrastructure/Services/SoftwareDescriptionJobService.cs
git commit -m "refactor(phase-4): rewrite remaining remediation services case-first"
```

---

### Task 13: Rewrite `RemediationDecisionQueryService` case-first

**Files:**
- Modify: `src/PatchHound.Api/Services/RemediationDecisionQueryService.cs` (1882 lines)

This is the heaviest rewrite. The service currently has `BuildByTenantSoftwareAsync(tenantId, tenantSoftwareId, includeArchived, ct)` returning a `DecisionContextDto`. It joins across `TenantSoftware`, `TenantVulnerability`, `VulnerabilityAssetEpisode` (all deleted in earlier phases).

- [ ] **Step 1: Rename the entry point**

`BuildByTenantSoftwareAsync` → `BuildByCaseAsync(Guid tenantId, Guid remediationCaseId, bool includeArchived, CancellationToken ct)`.

- [ ] **Step 2: Rewrite the join pipeline to read from canonical**

The new pipeline:
1. Load `RemediationCase` by `(TenantId, Id)`; return null if missing.
2. Load `SoftwareProduct` by `case.SoftwareProductId`.
3. Load `InstalledSoftware` rows for the tenant + product via `InstalledSoftware.SoftwareProductId == case.SoftwareProductId && InstalledSoftware.TenantId == tenantId`.
4. Load `DeviceVulnerabilityExposure` rows for those `InstalledSoftwareId`s, join canonical `Vulnerability` and `ExposureAssessment` for severity and env score.
5. Load `RemediationWorkflow` + `RemediationDecision` + `ApprovalTask` + `AnalystRecommendation` + `AIReport` by `RemediationCaseId == caseId`.
6. Assemble into `DecisionContextDto`.

- [ ] **Step 3: Update `DecisionContextDto`**

Remove `TenantSoftwareId`, `SoftwareAssetId`, `TenantVulnerabilityId` fields from the DTO. Add `RemediationCaseId`, `SoftwareProductId`, `ProductName`, `Vendor`. Vulnerability list items carry `VulnerabilityId` (canonical) and `EnvironmentalCvss` from `ExposureAssessment`.

- [ ] **Step 4: Update any DTO sub-records**

Every nested DTO that referenced a legacy ID (`RemediationDecisionDto`, `AnalystRecommendationDto`, `VulnerabilityOverrideDto`, `ApprovalTaskDto`) updates to canonical IDs. `VulnerabilityOverrideDto` carries `VulnerabilityId`, `AnalystRecommendationDto` carries `VulnerabilityId`, etc.

- [ ] **Step 5: Grep sweep inside the file**

Run: `grep -n "TenantSoftware\|SoftwareAsset\|TenantVulnerability\|VulnerabilityAsset" src/PatchHound.Api/Services/RemediationDecisionQueryService.cs`
Expected: no matches.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Api/Services/RemediationDecisionQueryService.cs src/PatchHound.Api/Models/Decisions/
git commit -m "refactor(phase-4): rewrite RemediationDecisionQueryService to case-first canonical joins"
```

---

### Task 14: Rewrite `RemediationTaskQueryService` case-first

**Files:**
- Modify: `src/PatchHound.Api/Services/RemediationTaskQueryService.cs`
- Modify: `src/PatchHound.Api/Models/Remediation/*` (as needed)

- [ ] **Step 1: Replace `ListOpenTasksAsync` joins**

All joins against `TenantSoftware` / `Asset` replaced with joins against `RemediationCase` + `SoftwareProduct` + `InstalledSoftware` (for device counts) + `DeviceVulnerabilityExposure` (for exposure counts).

- [ ] **Step 2: Replace `ListTeamStatusesForSoftwareAsync(tenantId, tenantSoftwareId, ct)` → `ListTeamStatusesForCaseAsync(tenantId, remediationCaseId, ct)`**

Query joins `PatchingTask` → `RemediationCase` and groups by `OwnerTeamId`. The result DTO carries `RemediationCaseId`.

- [ ] **Step 3: Replace `CreateMissingTasksForSoftwareAsync(tenantId, tenantSoftwareId, userId, ct)` → `CreateMissingTasksForCaseAsync(tenantId, remediationCaseId, userId, ct)`**

Body loads the latest approved `RemediationDecision` for the case and creates `PatchingTask` rows per owner team via `PatchingTask.Create(tenantId, remediationCaseId, decisionId, ownerTeamId, dueDate)`.

- [ ] **Step 4: Update `RemediationTaskListItemDto`, `RemediationTaskTeamStatusDto`, `RemediationTaskCreateResultDto`**

Drop `TenantSoftwareId` / `SoftwareAssetId`. Add `RemediationCaseId`, `SoftwareProductId`, `ProductName`.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Api/Services/RemediationTaskQueryService.cs src/PatchHound.Api/Models/Remediation/
git commit -m "refactor(phase-4): rewrite RemediationTaskQueryService case-first"
```

---

### Task 15: Rewrite `RemediationWorkflowAuthorizationService` case-first

**Files:**
- Modify: `src/PatchHound.Api/Services/RemediationWorkflowAuthorizationService.cs`

- [ ] **Step 1: Change signatures**

Every `(tenantId, tenantSoftwareId, ...)` method becomes `(tenantId, remediationCaseId, ...)`. The authorization check loads the `RemediationCase`, then resolves the software owner team from the case and checks the current user's role/team membership.

- [ ] **Step 2: Grep sweep**

Run: `grep -n "TenantSoftware\|SoftwareAsset" src/PatchHound.Api/Services/RemediationWorkflowAuthorizationService.cs`
Expected: no matches.

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Api/Services/RemediationWorkflowAuthorizationService.cs
git commit -m "refactor(phase-4): rewrite RemediationWorkflowAuthorizationService case-first"
```

---

### Task 16: Rewrite `RemediationDecisionsController` to `/api/remediation/cases/{caseId}/*`

**Files:**
- Modify: `src/PatchHound.Api/Controllers/RemediationDecisionsController.cs`

- [ ] **Step 1: Change the route prefix**

Replace `[Route("api/software/{tenantSoftwareId:guid}/remediation")]` with `[Route("api/remediation/cases/{caseId:guid}")]`. Delete every cross-route `[HttpPost("/api/remediation/{workflowId:guid}/...")]` — replace with case-scoped routes.

- [ ] **Step 2: Rewrite each action**

Every action that took `Guid tenantSoftwareId` now takes `Guid caseId`. Controller body delegates to `queryService.BuildByCaseAsync(tenantId, caseId, ...)`, `decisionService.CreateDecisionForCaseAsync(tenantId, caseId, ...)`, etc. The AI summary endpoints read through the case, and `ai-summary/review` marks the tenant insight (not tenant software) as reviewed.

Example for `GetDecisionContext`:
```csharp
[HttpGet("decision-context")]
[Authorize(Policy = Policies.ViewVulnerabilities)]
public async Task<ActionResult<DecisionContextDto>> GetDecisionContext(Guid caseId, CancellationToken ct)
{
    if (tenantContext.CurrentTenantId is not Guid tenantId)
        return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

    var result = await queryService.BuildByCaseAsync(tenantId, caseId, false, ct);
    if (result is null)
        return NotFound();
    return Ok(result);
}
```

- [ ] **Step 3: Rewrite `EnsureWorkflow`**

New route `POST /api/remediation/cases/{caseId:guid}/workflow`. Body looks up the case, calls `workflowService.GetOrCreateActiveWorkflowAsync(tenantId, caseId, ct)`, returns `EnsureRemediationWorkflowResponse(workflow.Id)`.

- [ ] **Step 4: Delete every reference to `tenantSoftwareId`**

Run: `grep -n "tenantSoftwareId\|TenantSoftware\|SoftwareAsset" src/PatchHound.Api/Controllers/RemediationDecisionsController.cs`
Expected: no matches.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Api/Controllers/RemediationDecisionsController.cs
git commit -m "refactor(phase-4): collapse remediation decisions API to /api/remediation/cases/{caseId}/*"
```

---

### Task 17: Rewrite `RemediationTasksController` case-first

**Files:**
- Modify: `src/PatchHound.Api/Controllers/RemediationTasksController.cs`

- [ ] **Step 1: Change routes**

- `GET /api/remediation/tasks` — unchanged (global tenant-scoped list).
- `GET /api/remediation/cases/{caseId:guid}/team-statuses` — replaces `/api/remediation/tasks/software/{tenantSoftwareId}/team-statuses`.
- `POST /api/remediation/cases/{caseId:guid}/tasks` — replaces `/api/remediation/tasks/software/{tenantSoftwareId}`.

- [ ] **Step 2: Rewrite action bodies**

Delegate to `queryService.ListTeamStatusesForCaseAsync(tenantId, caseId, ct)` and `queryService.CreateMissingTasksForCaseAsync(tenantId, caseId, userId, ct)`.

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Api/Controllers/RemediationTasksController.cs
git commit -m "refactor(phase-4): rewrite RemediationTasksController case-first"
```

---

### Task 18: Add `RemediationCasesController` entry points

**Files:**
- Create: `src/PatchHound.Api/Controllers/RemediationCasesController.cs`
- Create: `src/PatchHound.Api/Models/Remediation/RemediationCaseDto.cs`

- [ ] **Step 1: Create the DTO**

```csharp
namespace PatchHound.Api.Models.Remediation;

public record RemediationCaseDto(
    Guid Id,
    Guid TenantId,
    Guid SoftwareProductId,
    string ProductName,
    string Vendor,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    int AffectedDeviceCount,
    int OpenExposureCount);
```

- [ ] **Step 2: Create the controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Remediation;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/remediation/cases")]
[Authorize]
public class RemediationCasesController(
    PatchHoundDbContext db,
    RemediationCaseService caseService,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<RemediationCaseDto>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var query = db.RemediationCases.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.UpdatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip(pagination.Skip).Take(pagination.BoundedPageSize)
            .Select(c => new RemediationCaseDto(
                c.Id, c.TenantId, c.SoftwareProductId,
                c.SoftwareProduct.Name, c.SoftwareProduct.Vendor,
                c.Status.ToString(), c.CreatedAt, c.UpdatedAt, c.ClosedAt,
                db.InstalledSoftware.Count(i =>
                    i.TenantId == tenantId && i.SoftwareProductId == c.SoftwareProductId),
                db.DeviceVulnerabilityExposures.Count(e =>
                    e.TenantId == tenantId
                    && e.SoftwareProductId == c.SoftwareProductId
                    && e.Status == Core.Enums.DeviceExposureStatus.Open)))
            .ToListAsync(ct);

        return new PagedResponse<RemediationCaseDto>(items, total, pagination.Page, pagination.BoundedPageSize);
    }

    [HttpGet("{caseId:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<RemediationCaseDto>> Get(Guid caseId, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var c = await db.RemediationCases.AsNoTracking()
            .Include(x => x.SoftwareProduct)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == caseId, ct);
        if (c is null) return NotFound();

        return new RemediationCaseDto(
            c.Id, c.TenantId, c.SoftwareProductId,
            c.SoftwareProduct.Name, c.SoftwareProduct.Vendor,
            c.Status.ToString(), c.CreatedAt, c.UpdatedAt, c.ClosedAt,
            await db.InstalledSoftware.CountAsync(i =>
                i.TenantId == tenantId && i.SoftwareProductId == c.SoftwareProductId, ct),
            await db.DeviceVulnerabilityExposures.CountAsync(e =>
                e.TenantId == tenantId
                && e.SoftwareProductId == c.SoftwareProductId
                && e.Status == Core.Enums.DeviceExposureStatus.Open, ct));
    }

    public record CreateCaseRequest(Guid SoftwareProductId);

    [HttpPost]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<RemediationCaseDto>> GetOrCreate(
        [FromBody] CreateCaseRequest req, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        try
        {
            var c = await caseService.GetOrCreateAsync(tenantId, req.SoftwareProductId, ct);
            return await Get(c.Id, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message });
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Api/Controllers/RemediationCasesController.cs src/PatchHound.Api/Models/Remediation/RemediationCaseDto.cs
git commit -m "feat(phase-4): add RemediationCasesController list/get/create endpoints"
```

---

### Task 19: Green the backend build

**Files:**
- Modify: any remaining file that the compiler flags.

- [ ] **Step 1: Run the build and collect errors**

Run: `dotnet build PatchHound.slnx 2>&1 | grep -E "error CS" > /tmp/phase4-build-errors.txt; wc -l /tmp/phase4-build-errors.txt`

- [ ] **Step 2: Fix each remaining call site**

Typical remaining fixes:
- Services still passing `tenantSoftwareId` to workflow/decision/task services — switch to a `RemediationCase` lookup via `RemediationCaseService.GetOrCreateAsync`.
- DTO mappers referencing dropped fields — remove.
- Test fixtures constructing `RemediationWorkflow`/`RemediationDecision`/`PatchingTask` with the old signatures — update to pass a `remediationCaseId`.

Work through the error list top to bottom. Rebuild after each cluster of fixes until `dotnet build` reports zero errors.

- [ ] **Step 3: Run backend tests**

Run: `dotnet test PatchHound.slnx`
Expected: all existing tests green (some will legitimately have been updated to use case IDs in their arrange step; any test that asserted on `TenantSoftwareId` should have its assertion rewritten to `RemediationCaseId`).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(phase-4): fix remaining compile and test failures after case-first rewrite"
```

---

### Task 20: Frontend — collapse remediation routes to case-first

**Files:**
- Delete: `frontend/src/routes/_authed/software/$id_.remediation.tsx`
- Delete: `frontend/src/routes/_authed/assets/$id_.remediation.tsx`
- Create: `frontend/src/routes/_authed/remediation/cases.$caseId.tsx`
- Modify: `frontend/src/routes/_authed/remediation/index.tsx`
- Modify: `frontend/src/routes/_authed/remediation/task.$id.tsx`
- Modify: `frontend/src/routes/_authed/remediation/tasks.tsx`
- Modify: every component under `frontend/src/components/features/remediation/*`
- Modify: any callers in `frontend/src/components/features/software/*`, `dashboard/*`, `assets/*`, `vulnerabilities/*` that navigate to remediation

- [ ] **Step 1: Create the case route file**

Create `frontend/src/routes/_authed/remediation/cases.$caseId.tsx`:
```tsx
import { createFileRoute } from '@tanstack/react-router';
import { RemediationWorkbench } from '@/components/features/remediation/RemediationWorkbench';

export const Route = createFileRoute('/_authed/remediation/cases/$caseId')({
  component: RemediationCaseRoute,
});

function RemediationCaseRoute() {
  const { caseId } = Route.useParams();
  return <RemediationWorkbench caseId={caseId} />;
}
```

- [ ] **Step 2: Rewrite `RemediationWorkbench` props**

Change `RemediationWorkbench` to accept `{ caseId: string }`. All `tenantSoftwareId` references inside the component and its children become `caseId`. The API calls change:
- `GET /api/software/${tenantSoftwareId}/remediation/decision-context` → `GET /api/remediation/cases/${caseId}/decision-context`
- `POST /api/software/${tenantSoftwareId}/remediation/workflow` → `POST /api/remediation/cases/${caseId}/workflow`
- `POST /api/remediation/${workflowId}/decision` — keep the route (workflow id lookup still works) or re-point it to `/api/remediation/cases/${caseId}/decision` if Task 16 renamed it. **Use the renamed route.**
- `POST /api/remediation/${workflowId}/analysis` → `POST /api/remediation/cases/${caseId}/analysis`
- `POST /api/remediation/${workflowId}/verification` → `POST /api/remediation/cases/${caseId}/verification`
- `POST /api/remediation/${workflowId}/approval` → `POST /api/remediation/cases/${caseId}/approval`
- `POST /api/software/${tenantSoftwareId}/remediation/ai-summary` → `POST /api/remediation/cases/${caseId}/ai-summary`
- `POST /api/software/${tenantSoftwareId}/remediation/ai-summary/review` → `POST /api/remediation/cases/${caseId}/ai-summary/review`
- `GET /api/software/${tenantSoftwareId}/remediation/audit-trail` → `GET /api/remediation/cases/${caseId}/audit-trail`
- `POST /api/remediation/tasks/software/${tenantSoftwareId}` → `POST /api/remediation/cases/${caseId}/tasks`
- `GET /api/remediation/tasks/software/${tenantSoftwareId}/team-statuses` → `GET /api/remediation/cases/${caseId}/team-statuses`

- [ ] **Step 3: Update navigation from software detail**

In `frontend/src/components/features/software/SoftwareDetailPage.tsx` and `SoftwareTable.tsx`, remediation deep links previously routed to `/software/$id/remediation`. Replace with a two-step flow: call `POST /api/remediation/cases` with `{ softwareProductId }` to get-or-create a case, then navigate to `/remediation/cases/$caseId`. A small hook `useOpenRemediationCase(productId)` in `frontend/src/hooks/useOpenRemediationCase.ts` encapsulates this.

- [ ] **Step 4: Update dashboard deep links**

In `frontend/src/components/features/dashboard/{AssetOwnerOverview,SecurityManagerOverview,TechnicalManagerOverview}.tsx`, every "open remediation" link that pointed at `/software/$id/remediation` goes through `useOpenRemediationCase`.

- [ ] **Step 5: Update `RemediationTaskWorkbench` / task detail**

`frontend/src/routes/_authed/remediation/task.$id.tsx` loads a `PatchingTask` by id. The DTO now carries `remediationCaseId` instead of `tenantSoftwareId`. The "open related case" button navigates to `/remediation/cases/$caseId`.

- [ ] **Step 6: Update remediation task list**

`frontend/src/routes/_authed/remediation/tasks.tsx` and `RemediationSummaryCards.tsx` render columns using `caseId` / `productName` — drop the `tenantSoftwareId` column.

- [ ] **Step 7: Delete the old routes**

```bash
rm frontend/src/routes/_authed/software/\$id_.remediation.tsx
rm frontend/src/routes/_authed/assets/\$id_.remediation.tsx
```

- [ ] **Step 8: Grep sweep**

Run: `grep -rn "tenantSoftwareId" frontend/src | head -20`
Expected: no matches (except possibly in soft-deleted test snapshot files — if any, delete them).

- [ ] **Step 9: Typecheck and test**

Run: `cd frontend && npm run typecheck && npm test -- --run`
Expected: both green.

- [ ] **Step 10: Commit**

```bash
cd ..
git add frontend/src/
git commit -m "refactor(phase-4): collapse remediation UI to /remediation/cases/\$caseId"
```

---

### Task 21: Extend `TenantIsolationEndToEndTests` with remediation-case assertions

**Files:**
- Modify: `tests/PatchHound.Tests/Api/Tests/TenantIsolationEndToEndTests.cs`

- [ ] **Step 1: Seed a remediation case per tenant**

In the two-tenant fixture, for each tenant seed:
- A `SoftwareProduct` (global — same product id is fine; cases are keyed by tenant + product).
- An `InstalledSoftware` row for that tenant's device.
- A `RemediationCase` for `(tenantId, productId)`.
- A `RemediationWorkflow`, `RemediationDecision` (approved), `PatchingTask`, `ApprovalTask`, `AnalystRecommendation`, `AIReport` anchored on the case.

- [ ] **Step 2: Add assertions**

```csharp
[Fact]
public async Task Tenant_A_cannot_see_tenant_B_remediation_cases()
{
    var tenantAClient = await CreateAuthenticatedClientAsync(TenantA);

    var listResponse = await tenantAClient.GetAsync("/api/remediation/cases?page=1&pageSize=100");
    listResponse.EnsureSuccessStatusCode();
    var page = await listResponse.Content.ReadFromJsonAsync<PagedResponse<RemediationCaseDto>>();
    Assert.NotNull(page);
    Assert.All(page!.Items, c => Assert.Equal(TenantA.Id, c.TenantId));
    Assert.DoesNotContain(page.Items, c => c.Id == TenantBSeed.RemediationCase.Id);
}

[Fact]
public async Task Tenant_A_cannot_open_tenant_B_case_by_id()
{
    var tenantAClient = await CreateAuthenticatedClientAsync(TenantA);
    var response = await tenantAClient.GetAsync(
        $"/api/remediation/cases/{TenantBSeed.RemediationCase.Id}");
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task Tenant_A_cannot_read_decision_context_for_tenant_B_case()
{
    var tenantAClient = await CreateAuthenticatedClientAsync(TenantA);
    var response = await tenantAClient.GetAsync(
        $"/api/remediation/cases/{TenantBSeed.RemediationCase.Id}/decision-context");
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task Tenant_A_cannot_file_a_decision_against_tenant_B_case()
{
    var tenantAClient = await CreateAuthenticatedClientAsync(TenantA);
    var body = new CreateDecisionRequest(
        Outcome: RemediationOutcome.ApprovedForPatching.ToString(),
        Justification: "xss",
        ExpiryDate: null,
        ReEvaluationDate: null);
    var response = await tenantAClient.PostAsJsonAsync(
        $"/api/remediation/cases/{TenantBSeed.RemediationCase.Id}/decision", body);
    Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
}

[Fact]
public async Task Decisions_created_by_tenant_A_carry_tenant_A_id_even_if_payload_lies()
{
    // Spec §4.10 Rule 4: TenantId is stamped from TenantContext, never from the payload.
    var tenantAClient = await CreateAuthenticatedClientAsync(TenantA);
    var body = new CreateDecisionRequest(
        Outcome: RemediationOutcome.ApprovedForPatching.ToString(),
        Justification: "ok",
        ExpiryDate: null,
        ReEvaluationDate: null);
    var response = await tenantAClient.PostAsJsonAsync(
        $"/api/remediation/cases/{TenantASeed.RemediationCase.Id}/decision", body);
    response.EnsureSuccessStatusCode();

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
    var systemDb = db.AsSystemContext();
    var decision = await systemDb.RemediationDecisions
        .OrderByDescending(d => d.CreatedAt)
        .FirstAsync();
    Assert.Equal(TenantA.Id, decision.TenantId);
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~TenantIsolationEndToEndTests"`
Expected: all assertions pass.

- [ ] **Step 4: Commit**

```bash
git add tests/PatchHound.Tests/Api/Tests/TenantIsolationEndToEndTests.cs
git commit -m "test(phase-4): extend tenant isolation e2e with remediation case assertions"
```

---

### Task 22: Remediation case stability test (spec §7)

**Files:**
- Create: `tests/PatchHound.Tests/Infrastructure/Services/RemediationCaseStabilityTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure.TestInfrastructure;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services;

public class RemediationCaseStabilityTests
{
    [Fact]
    public async Task Case_id_is_stable_across_multiple_get_or_create_calls()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var productId = Guid.NewGuid();
        ctx.SoftwareProducts.Add(TestData.Product(productId));
        await ctx.SaveChangesAsync();

        var sut = new RemediationCaseService(ctx);

        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var c = await sut.GetOrCreateAsync(tenantId, productId, CancellationToken.None);
            ids.Add(c.Id);
        }

        Assert.Single(ids.Distinct());
        Assert.Equal(1, ctx.RemediationCases.Count());
    }

    [Fact]
    public async Task Case_id_is_stable_across_device_churn()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var productId = Guid.NewGuid();
        ctx.SoftwareProducts.Add(TestData.Product(productId));
        await ctx.SaveChangesAsync();

        var sut = new RemediationCaseService(ctx);
        var initial = await sut.GetOrCreateAsync(tenantId, productId, CancellationToken.None);

        // Simulate Phase 1 snapshot churn: delete and recreate installed software for the product.
        // The case should not change.
        var device = TestData.Device(tenantId);
        ctx.Devices.Add(device);
        ctx.InstalledSoftware.Add(TestData.InstalledSoftware(tenantId, device.Id, productId));
        await ctx.SaveChangesAsync();

        ctx.InstalledSoftware.RemoveRange(ctx.InstalledSoftware);
        await ctx.SaveChangesAsync();

        var afterChurn = await sut.GetOrCreateAsync(tenantId, productId, CancellationToken.None);
        Assert.Equal(initial.Id, afterChurn.Id);
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~RemediationCaseStabilityTests"`
Expected: PASS (2 tests).

- [ ] **Step 3: Commit**

```bash
git add tests/PatchHound.Tests/Infrastructure/Services/RemediationCaseStabilityTests.cs
git commit -m "test(phase-4): assert remediation case (tenant,product) stability across churn"
```

---

### Task 23: Final grep sweep

- [ ] **Step 1: Legacy identifier sweep**

Run:
```bash
grep -rn "TenantSoftwareId\|SoftwareAssetId\|tenantSoftwareId\|softwareAssetId" src/ frontend/src/ 2>/dev/null | grep -v "\.Migrations/" | grep -v "Phase4" | head -30
```
Expected: empty (no matches).

- [ ] **Step 2: Legacy route sweep**

Run:
```bash
grep -rn "/api/software/.*remediation\|/api/remediation/tasks/software" src/ frontend/src/ 2>/dev/null | head -20
```
Expected: empty.

- [ ] **Step 3: `IgnoreQueryFilters` audit**

Run: `grep -rn "IgnoreQueryFilters" src/ | grep -v "\.bak"`
Expected: only the documented system-context allow-list from Phase 1. Record the exact lines in the PR description. No new entries from Phase 4.

- [ ] **Step 4: If sweeps are dirty, fix and commit; else skip**

---

### Task 24: Final build, test, PR

- [ ] **Step 1: Full build and test**

Run in parallel:
```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx
(cd frontend && npm run typecheck && npm test -- --run)
```
Expected: all green, 0 warnings, 0 errors.

- [ ] **Step 2: Push the branch**

```bash
git push -u origin data-model-canonical-cleanup-phase-4
```

- [ ] **Step 3: Open the PR**

Run:
```bash
gh pr create --title "Phase 4: RemediationCase case-first data model" --body "$(cat <<'EOF'
## Summary
- Introduce `RemediationCase` (tenant-scoped, keyed by `(TenantId, SoftwareProductId)`) as the remediation scope root.
- Re-anchor `RemediationWorkflow`, `RemediationDecision`, `PatchingTask`, `ApprovalTask`, `RiskAcceptance`, `AnalystRecommendation`, `AIReport`, `RemediationAiJob` on `RemediationCaseId`.
- Re-anchor `SoftwareDescriptionJob` on `SoftwareProductId` directly; description text lands in `TenantSoftwareProductInsight`.
- Collapse remediation API to `/api/remediation/cases/{caseId}/*`; delete `/api/software/{tenantSoftwareId}/remediation/*` and `/api/remediation/tasks/software/*`.
- Collapse remediation UI to `/remediation/cases/$caseId`; delete `_authed/software/$id_.remediation.tsx` and `_authed/assets/$id_.remediation.tsx`.
- Tenant isolation e2e extended: cross-tenant case access returns 404; `TenantId` on every decision is stamped from `TenantContext`.
- Remediation case stability test: `(TenantId, SoftwareProductId)` is stable across snapshot churn.

## Tenant scope audit
| Entity | Direct `TenantId` | EF global filter | Notes |
| --- | --- | --- | --- |
| `RemediationCase` | yes | yes | New — unique `(TenantId, SoftwareProductId)` |
| `RemediationWorkflow` | yes | yes | Re-anchored via `RemediationCaseId` (same-tenant FK) |
| `RemediationDecision` | yes | yes | Same |
| `PatchingTask` | yes | yes | Same |
| `ApprovalTask` | yes | yes | Same |
| `RiskAcceptance` | yes | yes | Same |
| `AnalystRecommendation` | yes | yes | Same |
| `AIReport` | yes | yes | Same |
| `RemediationAiJob` | yes | yes | Same |
| `SoftwareDescriptionJob` | yes | yes | Anchored on global `SoftwareProductId` (no FK navigation) |

## IgnoreQueryFilters audit
No new entries. Allow-list unchanged from Phase 1 (recorded in Phase 1 PR).

## Spec §4.10 rule compliance
- R1 (direct `TenantId`): every remediation entity keeps its own column.
- R2 (global filter): every new/modified entity has the filter.
- R4 (`TenantId` from context): tenant isolation test `Decisions_created_by_tenant_A_carry_tenant_A_id_even_if_payload_lies` proves it.
- R5 (no new `IgnoreQueryFilters`): grep clean.
- R8 (same-tenant FK): `RemediationCase.TenantId == workflow.TenantId == decision.TenantId == ...` — verified by isolation test seed path.

## Test plan
- [ ] `dotnet build` clean
- [ ] `dotnet test` green (new: `RemediationCaseTests`, `RemediationCaseServiceTests`, `RemediationCaseStabilityTests`, `RemediationWorkflowTests` extension, `TenantIsolationEndToEndTests` extension)
- [ ] `npm run typecheck` clean
- [ ] `npm test` green
- [ ] Manual smoke: open a software product in the UI → "Open remediation case" routes to `/remediation/cases/$caseId` → file decision → approve → create patching task

EOF
)"
```

- [ ] **Step 4: Confirm PR URL printed**

Expected: `https://github.com/frodehus/PatchHound/pull/<n>`

---

## Plan self-review

**Spec coverage (spec §5.4):**
- `RemediationCase` (tenant-scoped, non-null `SoftwareProductId`) — Task 1.
- `RemediationWorkflow`, `RemediationDecision`, `PatchingTask`, `ApprovalTask` re-anchored on `RemediationCaseId` — Tasks 4–7.
- `RiskAcceptance`, `AnalystRecommendation`, `AIReport`, `RemediationAiJob`, `RemediationWorkflowStageRecord` re-anchored — Tasks 7, 9.
- `SoftwareDescriptionJob` re-anchored on `SoftwareProductId` — Task 8.
- Drop `TenantSoftwareId`/`SoftwareAssetId` from every remediation table — Tasks 4–7.
- API collapses to `/api/remediation/cases/{caseId}/*` — Tasks 16–18.
- Frontend uses case IDs — Task 20.
- Tenant isolation test extended — Task 21.
- Case stability test (spec §7) — Task 22.

**Type consistency:** every task that creates a `RemediationCase`-anchored entity calls `Create(tenantId, remediationCaseId, ...)`. Service methods uniformly use `remediationCaseId`. Route params uniformly use `caseId` (URL segment) / `remediationCaseId` (service parameter).

**Placeholder scan:** no `TBD`/`TODO(phase-later)` except the one explicit `// TODO(phase-5)` note on owner-team resolution in Task 10 Step 2, which is legitimate — owner-team mapping is reworked in Phase 5 when dashboards are rewritten.

**Cross-phase dependency check:** Phase 4 assumes Phase 2 already replaced `TenantVulnerabilityId` with canonical `VulnerabilityId` on `RemediationDecisionVulnerabilityOverride`, `RiskAcceptance`, `AnalystRecommendation`, `AIReport`. The plan header prerequisites call this out. If Phase 2 left any of those fields in place, Phase 2 merged incomplete — treat that as a bug in Phase 2, not a Phase 4 fix.
