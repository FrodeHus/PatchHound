# AI Patch Priority Assessment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the generic `RemediationAiJob`/`RemediationAiWorker` pipeline with a unified `VulnerabilityAssessmentJob` pipeline that auto-assesses newly observed critical CVEs and lets analysts manually trigger assessment for any severity, displaying the structured result in the remediation case UI.

**Architecture:** New global (per-CVE) entities `VulnerabilityAssessmentJob` and `VulnerabilityPatchAssessment` mirror the existing `RemediationAiJob` pattern. A new worker polls for pending jobs, runs the AI prompt, persists structured output, and sends emergency notifications by role. The ingestion pipeline triggers assessment after exposure derivation; a new API endpoint allows manual analyst requests.

**Tech Stack:** .NET 10 / C# 13, ASP.NET Core, EF Core + PostgreSQL, xunit + FluentAssertions + NSubstitute (tests), React 19 + TanStack Router + TanStack Query + Zod (frontend), Radix UI + Tailwind CSS v4 (UI)

**Spec:** `docs/superpowers/specs/2026-05-13-ai-patch-priority-design.md`

---

## File Map

| Action | Path |
|---|---|
| Create | `src/PatchHound.Core/Enums/VulnerabilityAssessmentJobStatus.cs` |
| Create | `src/PatchHound.Core/Entities/VulnerabilityAssessmentJob.cs` |
| Create | `src/PatchHound.Core/Entities/VulnerabilityPatchAssessment.cs` |
| Create | `src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityAssessmentJobConfiguration.cs` |
| Create | `src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityPatchAssessmentConfiguration.cs` |
| Create | `src/PatchHound.Infrastructure/Services/VulnerabilityAssessmentJobService.cs` |
| Create | `src/PatchHound.Worker/VulnerabilityAssessmentWorker.cs` |
| Create | `src/PatchHound.Api/Models/Decisions/PatchAssessmentDto.cs` |
| Create | `frontend/src/components/features/remediation/PatchAssessmentPanel.tsx` |
| Create | `tests/PatchHound.Tests/Infrastructure/VulnerabilityAssessmentJobServiceTests.cs` |
| Create | `tests/PatchHound.Tests/Api/VulnerabilityAssessmentControllerTests.cs` |
| Modify | `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` |
| Modify | `src/PatchHound.Infrastructure/DependencyInjection.cs` |
| Modify | `src/PatchHound.Infrastructure/Services/IngestionService.cs` |
| Modify | `src/PatchHound.Api/Controllers/RemediationDecisionsController.cs` |
| Modify | `src/PatchHound.Api/Controllers/VulnerabilitiesController.cs` |
| Modify | `src/PatchHound.Api/Services/RemediationDecisionQueryService.cs` |
| Modify | `src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs` |
| Modify | `src/PatchHound.Worker/Program.cs` |
| Modify | `frontend/src/api/remediation.schemas.ts` |
| Modify | `frontend/src/api/remediation.functions.ts` |
| Modify | `frontend/src/api/vulnerabilities.functions.ts` |
| Modify | `frontend/src/components/features/remediation/SoftwareRemediationView.tsx` |
| Delete | `src/PatchHound.Core/Entities/RemediationAiJob.cs` |
| Delete | `src/PatchHound.Core/Enums/RemediationAiJobStatus.cs` |
| Delete | `src/PatchHound.Infrastructure/Data/Configurations/RemediationAiJobConfiguration.cs` |
| Delete | `src/PatchHound.Infrastructure/Services/RemediationAiJobService.cs` |
| Delete | `src/PatchHound.Worker/RemediationAiWorker.cs` |
| Add migration | `src/PatchHound.Infrastructure/Migrations/<timestamp>_AddVulnerabilityAssessment.cs` |

---

## Task 1: Core entities — enums and job entity

**Files:**
- Create: `src/PatchHound.Core/Enums/VulnerabilityAssessmentJobStatus.cs`
- Create: `src/PatchHound.Core/Entities/VulnerabilityAssessmentJob.cs`

- [ ] **Step 1: Create the status enum**

```csharp
// src/PatchHound.Core/Enums/VulnerabilityAssessmentJobStatus.cs
namespace PatchHound.Core.Enums;

public enum VulnerabilityAssessmentJobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
}
```

- [ ] **Step 2: Create the job entity**

```csharp
// src/PatchHound.Core/Entities/VulnerabilityAssessmentJob.cs
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class VulnerabilityAssessmentJob
{
    public Guid Id { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public Guid TriggerTenantId { get; private set; }
    public VulnerabilityAssessmentJobStatus Status { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private VulnerabilityAssessmentJob() { }

    public static VulnerabilityAssessmentJob Create(
        Guid vulnerabilityId,
        Guid triggerTenantId,
        DateTimeOffset requestedAt)
    {
        return new VulnerabilityAssessmentJob
        {
            Id = Guid.NewGuid(),
            VulnerabilityId = vulnerabilityId,
            TriggerTenantId = triggerTenantId,
            Status = VulnerabilityAssessmentJobStatus.Pending,
            RequestedAt = requestedAt,
            UpdatedAt = requestedAt,
        };
    }

    public void Reset(Guid triggerTenantId, DateTimeOffset requestedAt)
    {
        TriggerTenantId = triggerTenantId;
        Status = VulnerabilityAssessmentJobStatus.Pending;
        Error = string.Empty;
        StartedAt = null;
        CompletedAt = null;
        RequestedAt = requestedAt;
        UpdatedAt = requestedAt;
    }

    public void Start(DateTimeOffset startedAt)
    {
        Status = VulnerabilityAssessmentJobStatus.Running;
        StartedAt = startedAt;
        CompletedAt = null;
        Error = string.Empty;
        UpdatedAt = startedAt;
    }

    public void CompleteSucceeded(DateTimeOffset completedAt)
    {
        Status = VulnerabilityAssessmentJobStatus.Succeeded;
        CompletedAt = completedAt;
        Error = string.Empty;
        UpdatedAt = completedAt;
    }

    public void CompleteFailed(DateTimeOffset completedAt, string error)
    {
        Status = VulnerabilityAssessmentJobStatus.Failed;
        CompletedAt = completedAt;
        Error = error.Trim();
        UpdatedAt = completedAt;
    }
}
```

- [ ] **Step 3: Build to confirm compilation**

```bash
dotnet build src/PatchHound.Core/PatchHound.Core.csproj -v minimal
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Core/Enums/VulnerabilityAssessmentJobStatus.cs \
        src/PatchHound.Core/Entities/VulnerabilityAssessmentJob.cs
git commit -m "feat: add VulnerabilityAssessmentJob entity and status enum"
```

---

## Task 2: Core entity — VulnerabilityPatchAssessment

**Files:**
- Create: `src/PatchHound.Core/Entities/VulnerabilityPatchAssessment.cs`

- [ ] **Step 1: Create the assessment entity**

```csharp
// src/PatchHound.Core/Entities/VulnerabilityPatchAssessment.cs
namespace PatchHound.Core.Entities;

public class VulnerabilityPatchAssessment
{
    public Guid Id { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public string Recommendation { get; private set; } = string.Empty;
    public string Confidence { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public string UrgencyTier { get; private set; } = string.Empty;
    public string UrgencyTargetSla { get; private set; } = string.Empty;
    public string UrgencyReason { get; private set; } = string.Empty;
    public string SimilarVulnerabilities { get; private set; } = "[]";
    public string CompensatingControlsUntilPatched { get; private set; } = "[]";
    public string References { get; private set; } = "[]";
    public string AiProfileName { get; private set; } = string.Empty;
    public string? RawOutput { get; private set; }
    public DateTimeOffset AssessedAt { get; private set; }

    private VulnerabilityPatchAssessment() { }

    public static VulnerabilityPatchAssessment Create(
        Guid vulnerabilityId,
        string recommendation,
        string confidence,
        string summary,
        string urgencyTier,
        string urgencyTargetSla,
        string urgencyReason,
        string similarVulnerabilities,
        string compensatingControlsUntilPatched,
        string references,
        string aiProfileName,
        string? rawOutput,
        DateTimeOffset assessedAt)
    {
        return new VulnerabilityPatchAssessment
        {
            Id = Guid.NewGuid(),
            VulnerabilityId = vulnerabilityId,
            Recommendation = recommendation.Trim(),
            Confidence = confidence.Trim(),
            Summary = summary.Trim(),
            UrgencyTier = urgencyTier.Trim().ToLowerInvariant(),
            UrgencyTargetSla = urgencyTargetSla.Trim(),
            UrgencyReason = urgencyReason.Trim(),
            SimilarVulnerabilities = similarVulnerabilities,
            CompensatingControlsUntilPatched = compensatingControlsUntilPatched,
            References = references,
            AiProfileName = aiProfileName.Trim(),
            RawOutput = rawOutput,
            AssessedAt = assessedAt,
        };
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/PatchHound.Core/PatchHound.Core.csproj -v minimal
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Core/Entities/VulnerabilityPatchAssessment.cs
git commit -m "feat: add VulnerabilityPatchAssessment entity"
```

---

## Task 3: EF configuration and DbContext

**Files:**
- Create: `src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityAssessmentJobConfiguration.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityPatchAssessmentConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`

- [ ] **Step 1: Create EF configuration for VulnerabilityAssessmentJob**

```csharp
// src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityAssessmentJobConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class VulnerabilityAssessmentJobConfiguration : IEntityTypeConfiguration<VulnerabilityAssessmentJob>
{
    public void Configure(EntityTypeBuilder<VulnerabilityAssessmentJob> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.VulnerabilityId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RequestedAt);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Error).HasMaxLength(2048).IsRequired();
    }
}
```

- [ ] **Step 2: Create EF configuration for VulnerabilityPatchAssessment**

```csharp
// src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityPatchAssessmentConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class VulnerabilityPatchAssessmentConfiguration : IEntityTypeConfiguration<VulnerabilityPatchAssessment>
{
    public void Configure(EntityTypeBuilder<VulnerabilityPatchAssessment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.VulnerabilityId).IsUnique();

        builder.Property(x => x.Recommendation).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.Confidence).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(4096).IsRequired();
        builder.Property(x => x.UrgencyTier).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UrgencyTargetSla).HasMaxLength(256).IsRequired();
        builder.Property(x => x.UrgencyReason).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.AiProfileName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.SimilarVulnerabilities).HasColumnType("text").IsRequired();
        builder.Property(x => x.CompensatingControlsUntilPatched).HasColumnType("text").IsRequired();
        builder.Property(x => x.References).HasColumnType("text").IsRequired();
        builder.Property(x => x.RawOutput).HasColumnType("text");
    }
}
```

- [ ] **Step 3: Add new DbSets to PatchHoundDbContext**

In `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`, find the line:

```csharp
    public DbSet<RemediationAiJob> RemediationAiJobs => Set<RemediationAiJob>();
```

Replace it with:

```csharp
    public DbSet<VulnerabilityAssessmentJob> VulnerabilityAssessmentJobs => Set<VulnerabilityAssessmentJob>();
    public DbSet<VulnerabilityPatchAssessment> VulnerabilityPatchAssessments => Set<VulnerabilityPatchAssessment>();
```

- [ ] **Step 4: Build Infrastructure**

```bash
dotnet build src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj -v minimal
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityAssessmentJobConfiguration.cs \
        src/PatchHound.Infrastructure/Data/Configurations/VulnerabilityPatchAssessmentConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs
git commit -m "feat: add EF config and DbSets for VulnerabilityAssessmentJob and VulnerabilityPatchAssessment"
```

---

## Task 4: VulnerabilityAssessmentJobService — tests then implementation

**Files:**
- Create: `tests/PatchHound.Tests/Infrastructure/VulnerabilityAssessmentJobServiceTests.cs`
- Create: `src/PatchHound.Infrastructure/Services/VulnerabilityAssessmentJobService.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PatchHound.Tests/Infrastructure/VulnerabilityAssessmentJobServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class VulnerabilityAssessmentJobServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly VulnerabilityAssessmentJobService _service;

    public VulnerabilityAssessmentJobServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(_tenantContext));
        _service = new VulnerabilityAssessmentJobService(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task EnqueueCriticalAsync_NoExistingJobOrAssessment_CreatesNewPendingJob()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0001", "Title", "Desc",
            Severity.Critical, 9.8m, null, null);
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        await _service.EnqueueCriticalAsync(_tenantId, vuln.Id, CancellationToken.None);

        var job = await _dbContext.VulnerabilityAssessmentJobs.SingleOrDefaultAsync();
        job.Should().NotBeNull();
        job!.Status.Should().Be(VulnerabilityAssessmentJobStatus.Pending);
        job.TriggerTenantId.Should().Be(_tenantId);
        job.VulnerabilityId.Should().Be(vuln.Id);
    }

    [Fact]
    public async Task EnqueueCriticalAsync_AssessmentAlreadyExists_DoesNotCreateJob()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0002", "Title", "Desc",
            Severity.Critical, 9.8m, null, null);
        _dbContext.Vulnerabilities.Add(vuln);
        var assessment = VulnerabilityPatchAssessment.Create(
            vuln.Id, "Patch now", "High", "Summary", "emergency",
            "24 hours", "Active exploit", "[]", "[]", "[]", "GPT-4o", null, DateTimeOffset.UtcNow);
        _dbContext.VulnerabilityPatchAssessments.Add(assessment);
        await _dbContext.SaveChangesAsync();

        await _service.EnqueueCriticalAsync(_tenantId, vuln.Id, CancellationToken.None);

        var jobCount = await _dbContext.VulnerabilityAssessmentJobs.CountAsync();
        jobCount.Should().Be(0);
    }

    [Fact]
    public async Task EnqueueCriticalAsync_RunningJobExists_DoesNotCreateNewJob()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0003", "Title", "Desc",
            Severity.Critical, 9.8m, null, null);
        _dbContext.Vulnerabilities.Add(vuln);
        var job = VulnerabilityAssessmentJob.Create(vuln.Id, _tenantId, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);
        _dbContext.VulnerabilityAssessmentJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        await _service.EnqueueCriticalAsync(_tenantId, vuln.Id, CancellationToken.None);

        var jobCount = await _dbContext.VulnerabilityAssessmentJobs.CountAsync();
        jobCount.Should().Be(1);
        (await _dbContext.VulnerabilityAssessmentJobs.SingleAsync()).Status
            .Should().Be(VulnerabilityAssessmentJobStatus.Running);
    }

    [Fact]
    public async Task EnqueueCriticalAsync_FailedJobExists_ResetsJobToPending()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0004", "Title", "Desc",
            Severity.Critical, 9.8m, null, null);
        _dbContext.Vulnerabilities.Add(vuln);
        var job = VulnerabilityAssessmentJob.Create(vuln.Id, _tenantId, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);
        job.CompleteFailed(DateTimeOffset.UtcNow, "timeout");
        _dbContext.VulnerabilityAssessmentJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        await _service.EnqueueCriticalAsync(_tenantId, vuln.Id, CancellationToken.None);

        var updated = await _dbContext.VulnerabilityAssessmentJobs.SingleAsync();
        updated.Status.Should().Be(VulnerabilityAssessmentJobStatus.Pending);
        updated.Error.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestManualAsync_RunningJobExists_ReturnsFalse()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0005", "Title", "Desc",
            Severity.High, 7.5m, null, null);
        _dbContext.Vulnerabilities.Add(vuln);
        var job = VulnerabilityAssessmentJob.Create(vuln.Id, _tenantId, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);
        _dbContext.VulnerabilityAssessmentJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var result = await _service.RequestManualAsync(_tenantId, vuln.Id, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RequestManualAsync_NoJobExists_CreatesJobAndReturnsTrue()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0006", "Title", "Desc",
            Severity.Medium, 5.0m, null, null);
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        var result = await _service.RequestManualAsync(_tenantId, vuln.Id, CancellationToken.None);

        result.Should().BeTrue();
        var job = await _dbContext.VulnerabilityAssessmentJobs.SingleOrDefaultAsync();
        job.Should().NotBeNull();
        job!.Status.Should().Be(VulnerabilityAssessmentJobStatus.Pending);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/PatchHound.Tests/PatchHound.Tests.csproj \
  --filter "FullyQualifiedName~VulnerabilityAssessmentJobServiceTests" -v minimal
```

Expected: Build failure or test failures (type not found).

- [ ] **Step 3: Implement VulnerabilityAssessmentJobService**

```csharp
// src/PatchHound.Infrastructure/Services/VulnerabilityAssessmentJobService.cs
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class VulnerabilityAssessmentJobService(PatchHoundDbContext dbContext)
{
    public async Task EnqueueCriticalAsync(
        Guid triggerTenantId,
        Guid vulnerabilityId,
        CancellationToken ct)
    {
        var assessmentExists = await dbContext.VulnerabilityPatchAssessments
            .AnyAsync(a => a.VulnerabilityId == vulnerabilityId, ct);
        if (assessmentExists)
            return;

        var existingJob = await dbContext.VulnerabilityAssessmentJobs
            .FirstOrDefaultAsync(j => j.VulnerabilityId == vulnerabilityId, ct);

        if (existingJob is null)
        {
            dbContext.VulnerabilityAssessmentJobs.Add(
                VulnerabilityAssessmentJob.Create(vulnerabilityId, triggerTenantId, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(ct);
            return;
        }

        if (existingJob.Status is VulnerabilityAssessmentJobStatus.Pending
            or VulnerabilityAssessmentJobStatus.Running)
            return;

        existingJob.Reset(triggerTenantId, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<bool> RequestManualAsync(
        Guid triggerTenantId,
        Guid vulnerabilityId,
        CancellationToken ct)
    {
        var existingJob = await dbContext.VulnerabilityAssessmentJobs
            .FirstOrDefaultAsync(j => j.VulnerabilityId == vulnerabilityId, ct);

        if (existingJob?.Status == VulnerabilityAssessmentJobStatus.Running)
            return false;

        if (existingJob is null)
        {
            dbContext.VulnerabilityAssessmentJobs.Add(
                VulnerabilityAssessmentJob.Create(vulnerabilityId, triggerTenantId, DateTimeOffset.UtcNow));
        }
        else
        {
            existingJob.Reset(triggerTenantId, DateTimeOffset.UtcNow);
            var existing = await dbContext.VulnerabilityPatchAssessments
                .FirstOrDefaultAsync(a => a.VulnerabilityId == vulnerabilityId, ct);
            if (existing is not null)
                dbContext.VulnerabilityPatchAssessments.Remove(existing);
        }

        await dbContext.SaveChangesAsync(ct);
        return true;
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test tests/PatchHound.Tests/PatchHound.Tests.csproj \
  --filter "FullyQualifiedName~VulnerabilityAssessmentJobServiceTests" -v minimal
```

Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/VulnerabilityAssessmentJobService.cs \
        tests/PatchHound.Tests/Infrastructure/VulnerabilityAssessmentJobServiceTests.cs
git commit -m "feat: implement VulnerabilityAssessmentJobService with dedup and upsert logic"
```

---

## Task 5: Register service in DI

**Files:**
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Register VulnerabilityAssessmentJobService**

In `src/PatchHound.Infrastructure/DependencyInjection.cs`, find the services block that registers scoped infrastructure services (near `RemediationAiJobService` if present, otherwise near other scoped services). Add:

```csharp
services.AddScoped<VulnerabilityAssessmentJobService>();
```

- [ ] **Step 2: Build**

```bash
dotnet build src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj -v minimal
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Infrastructure/DependencyInjection.cs
git commit -m "feat: register VulnerabilityAssessmentJobService in DI"
```

---

## Task 6: Wire trigger in IngestionService

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`

- [ ] **Step 1: Add VulnerabilityAssessmentJobService field and constructor parameter**

In `src/PatchHound.Infrastructure/Services/IngestionService.cs`, add the field after the existing fields (around line 40):

```csharp
    private readonly VulnerabilityAssessmentJobService? _vulnerabilityAssessmentJobService;
```

In the `[ActivatorUtilitiesConstructor]` constructor parameter list, add after `RemediationDecisionService? remediationDecisionService`:

```csharp
        VulnerabilityAssessmentJobService? vulnerabilityAssessmentJobService,
```

In the constructor body, add after `_remediationDecisionService = remediationDecisionService;`:

```csharp
        _vulnerabilityAssessmentJobService = vulnerabilityAssessmentJobService;
```

- [ ] **Step 2: Enqueue assessment jobs after exposure derivation**

In `RunExposureDerivationAsync` (around line 86), add after `await new RemediationCaseService(_dbContext).EnsureCasesForOpenExposuresAsync(tenantId, ct);`:

```csharp
        if (_vulnerabilityAssessmentJobService is not null)
        {
            await EnqueueAssessmentJobsForCriticalExposuresAsync(tenantId, ct);
        }
```

- [ ] **Step 3: Add the private helper method**

Add this private method to `IngestionService` before the closing brace of the class:

```csharp
    private async Task EnqueueAssessmentJobsForCriticalExposuresAsync(Guid tenantId, CancellationToken ct)
    {
        var criticalVulnIds = await _dbContext.DeviceVulnerabilityExposures
            .Where(e => e.TenantId == tenantId && e.Status == ExposureStatus.Open)
            .Join(_dbContext.Vulnerabilities,
                e => e.VulnerabilityId,
                v => v.Id,
                (e, v) => new { v.Id, v.VendorSeverity })
            .Where(x => x.VendorSeverity == Severity.Critical)
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(ct);

        foreach (var vulnId in criticalVulnIds)
        {
            await _vulnerabilityAssessmentJobService!.EnqueueCriticalAsync(tenantId, vulnId, ct);
        }
    }
```

- [ ] **Step 4: Build**

```bash
dotnet build src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj -v minimal
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionService.cs
git commit -m "feat: enqueue VulnerabilityAssessmentJobs for critical CVEs after exposure derivation"
```

---

## Task 7: VulnerabilityAssessmentWorker

**Files:**
- Create: `src/PatchHound.Worker/VulnerabilityAssessmentWorker.cs`
- Modify: `src/PatchHound.Worker/Program.cs`

- [ ] **Step 1: Create the worker**

`TenantAiTextGenerationService` handles provider selection, profile resolution, and error wrapping — use it directly rather than wiring providers manually. It returns `Result<AiTextGenerationResult>` where `Value.Content` is the raw model output and `Value.ProfileName` is the profile name for audit.

```csharp
// src/PatchHound.Worker/VulnerabilityAssessmentWorker.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Worker;

public class VulnerabilityAssessmentWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<VulnerabilityAssessmentWorker> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private const string PromptTemplate =
        "As a security analyst responsible for prioritization of patching of vulnerabilities, " +
        "{0} with regards to how quickly it should be patched (emergency patch, as soon as possible, " +
        "normal patch window or low priority). Correlate with likelihood of exploitation based on previous " +
        "similar vulnerabilities for the system/service related to the vulnerability and history of exploits. " +
        "Output as a structured JSON with clear recommendations, justifications and references. The following " +
        "properties must always be present: Recommendation, Confidence, Summary, Urgency (with sub-fields " +
        "tier, target SLA, reason), SimilarVulnerabilities, CompensatingControlsUntilPatched, References";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("VulnerabilityAssessmentWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during vulnerability assessment polling cycle");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessPendingJobAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var aiService = scope.ServiceProvider.GetRequiredService<TenantAiTextGenerationService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var job = await dbContext.VulnerabilityAssessmentJobs
            .Where(j => j.Status == VulnerabilityAssessmentJobStatus.Pending)
            .OrderBy(j => j.RequestedAt)
            .FirstOrDefaultAsync(ct);

        if (job is null)
            return;

        job.Start(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(ct);

        try
        {
            var vulnerability = await dbContext.Vulnerabilities
                .FirstOrDefaultAsync(v => v.Id == job.VulnerabilityId, ct);
            if (vulnerability is null)
            {
                job.CompleteFailed(DateTimeOffset.UtcNow, "Vulnerability not found.");
                await dbContext.SaveChangesAsync(ct);
                return;
            }

            var prompt = string.Format(PromptTemplate, vulnerability.ExternalId);
            var request = new AiTextGenerationRequest(
                SystemPrompt: string.Empty,
                UserPrompt: prompt);

            var generated = await aiService.GenerateAsync(
                job.TriggerTenantId, null, request, ct);

            if (!generated.IsSuccess)
            {
                job.CompleteFailed(DateTimeOffset.UtcNow,
                    generated.Error ?? "AI generation failed.");
                await dbContext.SaveChangesAsync(ct);
                return;
            }

            if (!TryParseAssessment(generated.Value.Content, out var parsed, out var parseError))
            {
                job.CompleteFailed(DateTimeOffset.UtcNow, $"Malformed AI response: {parseError}");
                await dbContext.SaveChangesAsync(ct);
                logger.LogWarning(
                    "VulnerabilityAssessmentWorker: malformed AI response for job {JobId}: {Error}",
                    job.Id, parseError);
                return;
            }

            var assessment = VulnerabilityPatchAssessment.Create(
                vulnerability.Id,
                parsed.Recommendation,
                parsed.Confidence,
                parsed.Summary,
                parsed.UrgencyTier,
                parsed.UrgencyTargetSla,
                parsed.UrgencyReason,
                parsed.SimilarVulnerabilitiesJson,
                parsed.CompensatingControlsJson,
                parsed.ReferencesJson,
                generated.Value.ProfileName,
                generated.Value.Content,
                DateTimeOffset.UtcNow);

            dbContext.VulnerabilityPatchAssessments.Add(assessment);
            job.CompleteSucceeded(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(ct);

            logger.LogInformation(
                "VulnerabilityAssessmentWorker: completed job {JobId} for {ExternalId}, tier={Tier}",
                job.Id, vulnerability.ExternalId, assessment.UrgencyTier);

            if (assessment.UrgencyTier == "emergency")
            {
                await DispatchEmergencyNotificationsAsync(
                    dbContext, notificationService, vulnerability, assessment, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var trackedJob = await dbContext.VulnerabilityAssessmentJobs
                .FirstAsync(j => j.Id == job.Id, ct);
            trackedJob.CompleteFailed(DateTimeOffset.UtcNow, ex.Message);
            await dbContext.SaveChangesAsync(ct);
            logger.LogError(ex, "VulnerabilityAssessmentWorker: job {JobId} failed", job.Id);
        }
    }

    private async Task DispatchEmergencyNotificationsAsync(
        PatchHoundDbContext dbContext,
        INotificationService notificationService,
        Vulnerability vulnerability,
        VulnerabilityPatchAssessment assessment,
        CancellationToken ct)
    {
        var affectedTenantIds = await dbContext.DeviceVulnerabilityExposures
            .Where(e => e.VulnerabilityId == vulnerability.Id && e.Status == ExposureStatus.Open)
            .Select(e => e.TenantId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tenantId in affectedTenantIds)
        {
            var userIds = await dbContext.UserTenantRoles
                .Where(utr => utr.TenantId == tenantId
                    && (utr.Role == RoleName.SecurityManager || utr.Role == RoleName.TechnicalManager))
                .Select(utr => utr.UserId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var userId in userIds)
            {
                var alreadyNotified = await dbContext.Notifications
                    .AnyAsync(n => n.UserId == userId
                        && n.Type == NotificationType.NewCriticalVuln
                        && n.RelatedEntityId == vulnerability.Id, ct);

                if (alreadyNotified)
                    continue;

                var remediationCaseId = await dbContext.RemediationCases
                    .Where(c => c.TenantId == tenantId)
                    .Join(dbContext.DeviceVulnerabilityExposures
                        .Where(e => e.TenantId == tenantId
                            && e.VulnerabilityId == vulnerability.Id
                            && e.Status == ExposureStatus.Open),
                        c => c.SoftwareProductId,
                        e => e.SoftwareProductId,
                        (c, _) => (Guid?)c.Id)
                    .FirstOrDefaultAsync(ct);

                await notificationService.SendAsync(
                    userId,
                    tenantId,
                    NotificationType.NewCriticalVuln,
                    $"Emergency patch required: {vulnerability.ExternalId}",
                    $"{vulnerability.ExternalId} requires emergency patching. " +
                    $"Confidence: {assessment.Confidence}. " +
                    $"Target SLA: {assessment.UrgencyTargetSla}. " +
                    $"Reason: {assessment.UrgencyReason}",
                    "Vulnerability",
                    vulnerability.Id,
                    ct);
            }
        }
    }

    private static bool TryParseAssessment(
        string raw,
        out ParsedAssessment result,
        out string error)
    {
        result = default;
        error = string.Empty;

        try
        {
            var json = ExtractJson(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var recommendation = GetString(root, "Recommendation");
            var confidence = GetString(root, "Confidence");
            var summary = GetString(root, "Summary");

            if (!root.TryGetProperty("Urgency", out var urgency))
            {
                error = "Missing Urgency object";
                return false;
            }

            var tier = GetString(urgency, "tier");
            var targetSla = GetString(urgency, "target SLA");
            var reason = GetString(urgency, "reason");

            if (string.IsNullOrWhiteSpace(recommendation) || string.IsNullOrWhiteSpace(confidence)
                || string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(tier))
            {
                error = "One or more required fields are empty (Recommendation, Confidence, Summary, Urgency.tier)";
                return false;
            }

            var similarJson = root.TryGetProperty("SimilarVulnerabilities", out var sv)
                ? sv.GetRawText() : "[]";
            var controlsJson = root.TryGetProperty("CompensatingControlsUntilPatched", out var cc)
                ? cc.GetRawText() : "[]";
            var refsJson = root.TryGetProperty("References", out var refs)
                ? refs.GetRawText() : "[]";

            result = new ParsedAssessment(
                recommendation!, confidence!, summary!,
                NormalizeTier(tier!), targetSla ?? string.Empty, reason ?? string.Empty,
                similarJson, controlsJson, refsJson);

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end < start)
            throw new InvalidOperationException("No JSON object found in AI response.");
        return raw[start..(end + 1)];
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var val) ? val.GetString() : null;

    private static string NormalizeTier(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "emergency" or "emergency patch" => "emergency",
        "as soon as possible" or "asap" => "as_soon_as_possible",
        "normal patch window" or "normal" => "normal_patch_window",
        "low priority" or "low" => "low_priority",
        _ => raw.Trim().ToLowerInvariant(),
    };

    private readonly record struct ParsedAssessment(
        string Recommendation,
        string Confidence,
        string Summary,
        string UrgencyTier,
        string UrgencyTargetSla,
        string UrgencyReason,
        string SimilarVulnerabilitiesJson,
        string CompensatingControlsJson,
        string ReferencesJson);
}
```

- [ ] **Step 2: Update Worker/Program.cs** — replace `RemediationAiWorker` registration with `VulnerabilityAssessmentWorker` and remove the `RemediationDecisionQueryService` scoped registration (no longer needed by worker):

Replace in `src/PatchHound.Worker/Program.cs`:

```csharp
builder.Services.AddScoped<RemediationDecisionQueryService>();
```
```csharp
// builder.Services.AddScoped<RemediationDecisionQueryService>();  // removed — no longer used by worker
```

Replace:
```csharp
builder.Services.AddHostedService<RemediationAiWorker>();
```
with:
```csharp
builder.Services.AddHostedService<VulnerabilityAssessmentWorker>();
```

- [ ] **Step 3: Build**

```bash
dotnet build src/PatchHound.Worker/PatchHound.Worker.csproj -v minimal
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Worker/VulnerabilityAssessmentWorker.cs \
        src/PatchHound.Worker/Program.cs
git commit -m "feat: add VulnerabilityAssessmentWorker, replace RemediationAiWorker"
```

---

## Task 8: EF migration

**Files:**
- Add migration (generated file)

- [ ] **Step 1: Add the EF migration**

```bash
dotnet ef migrations add AddVulnerabilityAssessment \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

Expected: New migration file created in `src/PatchHound.Infrastructure/Migrations/`.

- [ ] **Step 2: Open the generated migration and verify**

The `Up()` method should:
- `DropTable("RemediationAiJobs")` — will be here because DbSet was removed
- `CreateTable("VulnerabilityAssessmentJobs", ...)`
- `CreateTable("VulnerabilityPatchAssessments", ...)`

If `DropTable("RemediationAiJobs")` is not present, the old DbSet was not removed in Task 3. Go back and confirm that step.

- [ ] **Step 3: Commit the migration**

```bash
git add src/PatchHound.Infrastructure/Migrations/
git commit -m "feat: EF migration AddVulnerabilityAssessment (replaces RemediationAiJobs)"
```

---

## Task 9: Manual trigger API endpoint — tests then implementation

**Files:**
- Create: `tests/PatchHound.Tests/Api/VulnerabilityAssessmentControllerTests.cs`
- Modify: `src/PatchHound.Api/Controllers/VulnerabilitiesController.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PatchHound.Tests/Api/VulnerabilityAssessmentControllerTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class VulnerabilityAssessmentControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly VulnerabilitiesController _controller;

    public VulnerabilityAssessmentControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(_tenantContext));

        var assessmentJobService = new VulnerabilityAssessmentJobService(_dbContext);
        var detailQueryService = new PatchHound.Api.Services.VulnerabilityDetailQueryService(_dbContext);
        _controller = new VulnerabilitiesController(_dbContext, _tenantContext, detailQueryService, assessmentJobService);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task RequestAssessment_VulnerabilityExists_Returns202()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0010", "Title", "Desc",
            Severity.High, 7.5m, null, null);
        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.RequestAssessment(vuln.Id, CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task RequestAssessment_VulnerabilityNotFound_Returns404()
    {
        var result = await _controller.RequestAssessment(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task RequestAssessment_RunningJobExists_Returns409()
    {
        var vuln = Vulnerability.Create("nvd", "CVE-2026-0011", "Title", "Desc",
            Severity.High, 7.5m, null, null);
        _dbContext.Vulnerabilities.Add(vuln);
        var job = VulnerabilityAssessmentJob.Create(vuln.Id, _tenantId, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);
        _dbContext.VulnerabilityAssessmentJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.RequestAssessment(vuln.Id, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }
}
```

- [ ] **Step 2: Run tests to confirm failure**

```bash
dotnet test tests/PatchHound.Tests/PatchHound.Tests.csproj \
  --filter "FullyQualifiedName~VulnerabilityAssessmentControllerTests" -v minimal
```

Expected: compilation error (controller constructor mismatch / missing method).

- [ ] **Step 3: Add VulnerabilityAssessmentJobService to VulnerabilitiesController and add endpoint**

In `src/PatchHound.Api/Controllers/VulnerabilitiesController.cs`, update the constructor to inject `VulnerabilityAssessmentJobService`:

```csharp
// Add field
private readonly VulnerabilityAssessmentJobService _assessmentJobService;

// Update constructor
public VulnerabilitiesController(
    PatchHoundDbContext dbContext,
    ITenantContext tenantContext,
    VulnerabilityDetailQueryService detailQueryService,
    VulnerabilityAssessmentJobService assessmentJobService)
{
    _dbContext = dbContext;
    _tenantContext = tenantContext;
    _detailQueryService = detailQueryService;
    _assessmentJobService = assessmentJobService;
}
```

Add the new endpoint (at the end of the controller, before the closing brace):

```csharp
[HttpPost("{id:guid}/assessment")]
[Authorize(Policy = Policies.ManageRemediation)]
public async Task<IActionResult> RequestAssessment(Guid id, CancellationToken ct)
{
    if (_tenantContext.CurrentTenantId is not Guid tenantId)
        return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

    var exists = await _dbContext.Vulnerabilities
        .AnyAsync(v => v.Id == id, ct);
    if (!exists)
        return NotFound();

    var accepted = await _assessmentJobService.RequestManualAsync(tenantId, id, ct);
    if (!accepted)
        return Conflict(new ProblemDetails { Title = "Assessment already in progress." });

    return Accepted();
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test tests/PatchHound.Tests/PatchHound.Tests.csproj \
  --filter "FullyQualifiedName~VulnerabilityAssessmentControllerTests" -v minimal
```

Expected: All 3 tests pass.

- [ ] **Step 5: Build full solution**

```bash
dotnet build PatchHound.slnx -v minimal
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Api/Controllers/VulnerabilitiesController.cs \
        tests/PatchHound.Tests/Api/VulnerabilityAssessmentControllerTests.cs
git commit -m "feat: add POST /api/vulnerabilities/{id}/assessment endpoint"
```

---

## Task 10: Update remediation case DTO and query service

**Files:**
- Create: `src/PatchHound.Api/Models/Decisions/PatchAssessmentDto.cs`
- Modify: `src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs`
- Modify: `src/PatchHound.Api/Services/RemediationDecisionQueryService.cs`

- [ ] **Step 1: Create the new DTO**

```csharp
// src/PatchHound.Api/Models/Decisions/PatchAssessmentDto.cs
namespace PatchHound.Api.Models.Decisions;

public record PatchAssessmentDto(
    string? Recommendation,
    string? Confidence,
    string? Summary,
    string? UrgencyTier,
    string? UrgencyTargetSla,
    string? UrgencyReason,
    string? SimilarVulnerabilities,
    string? CompensatingControlsUntilPatched,
    string? References,
    string? AiProfileName,
    DateTimeOffset? AssessedAt,
    string JobStatus
);
```

- [ ] **Step 2: Replace AiSummary with PatchAssessment in DecisionContextDto**

In `src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs`, replace:

```csharp
    DecisionAiSummaryDto AiSummary,
    ThreatIntelDto ThreatIntel
```

with:

```csharp
    PatchAssessmentDto PatchAssessment,
    ThreatIntelDto ThreatIntel
```

Also delete the entire `DecisionAiSummaryDto` record (lines 46–68 in the original file) and the `ReviewDecisionAiSummaryRequest` record. Keep `ThreatIntelDto`.

- [ ] **Step 3: Update BuildByCaseIdAsync in RemediationDecisionQueryService**

The method at line ~449 builds the `DecisionContextDto`. Find where `aiSummary` is assigned and the `AiSummary` field is set in the returned record.

Replace the `ResolveAiSummaryAsync` call and `aiSummary` construction with a `patchAssessment` lookup:

```csharp
var patchAssessment = await ResolvePatchAssessmentAsync(tenantId, caseId, ct);
```

Replace the `aiSummary` and `AiSummary` fields in the returned `DecisionContextDto` constructor call with:

```csharp
patchAssessment,
```

- [ ] **Step 4: Add ResolvePatchAssessmentAsync helper method**

Add this private method to `RemediationDecisionQueryService` (replace the old `ResolveAiSummaryAsync`, `BuildAiSummaryDto`, `ResolveAiSummaryStatus`, and `GenerateAiNarrativeAsync` methods):

```csharp
private async Task<PatchAssessmentDto> ResolvePatchAssessmentAsync(
    Guid tenantId,
    Guid caseId,
    CancellationToken ct)
{
    var vulnerabilityId = await dbContext.RemediationCases.AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.Id == caseId)
        .Join(dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Status == ExposureStatus.Open),
            c => c.SoftwareProductId,
            e => e.SoftwareProductId,
            (_, e) => e.VulnerabilityId)
        .Join(dbContext.Vulnerabilities.AsNoTracking(),
            id => id,
            v => v.Id,
            (_, v) => new { v.Id, v.VendorSeverity })
        .OrderByDescending(x => x.VendorSeverity)
        .Select(x => (Guid?)x.Id)
        .FirstOrDefaultAsync(ct);

    if (vulnerabilityId is null)
        return new PatchAssessmentDto(null, null, null, null, null, null, null, null, null, null, null, "None");

    var assessment = await dbContext.VulnerabilityPatchAssessments.AsNoTracking()
        .FirstOrDefaultAsync(a => a.VulnerabilityId == vulnerabilityId, ct);

    var job = await dbContext.VulnerabilityAssessmentJobs.AsNoTracking()
        .FirstOrDefaultAsync(j => j.VulnerabilityId == vulnerabilityId, ct);

    var jobStatus = job?.Status.ToString() ?? (assessment is not null ? "Succeeded" : "None");

    if (assessment is null)
        return new PatchAssessmentDto(null, null, null, null, null, null, null, null, null, null, null, jobStatus);

    return new PatchAssessmentDto(
        assessment.Recommendation,
        assessment.Confidence,
        assessment.Summary,
        assessment.UrgencyTier,
        assessment.UrgencyTargetSla,
        assessment.UrgencyReason,
        assessment.SimilarVulnerabilities,
        assessment.CompensatingControlsUntilPatched,
        assessment.References,
        assessment.AiProfileName,
        assessment.AssessedAt,
        jobStatus);
}
```

- [ ] **Step 5: Remove the old AI draft methods from RemediationDecisionQueryService**

Delete the following methods from `RemediationDecisionQueryService.cs`:
- `GenerateAndStoreAiDraftsAsync` (line ~1024)
- `RefreshAiSummaryAsync` (line ~1015)
- `ResolveAiSummaryAsync` (private, line ~1444)
- `BuildAiSummaryDto` (private, line ~1472)
- `ResolveAiSummaryStatus` (private, line ~1519)
- `GenerateAiNarrativeAsync` (private, line ~1552)
- `TryParseAiDraftPayload` (private, line ~1651)
- Remove the private record types `RemediationAiDraftPayload` and `RemediationAiContext` that are only used by those methods.

Also remove the constructor parameter `TenantAiTextGenerationService aiTextGenerationService` if it's only used by the removed methods. Check by searching for other uses of `aiTextGenerationService` — if none remain, remove the parameter and its backing field.

- [ ] **Step 6: Build**

```bash
dotnet build PatchHound.slnx -v minimal
```

Expected: `Build succeeded.` Fix any remaining references to `AiSummary` or `DecisionAiSummaryDto` that the compiler flags.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Api/Models/Decisions/PatchAssessmentDto.cs \
        src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs \
        src/PatchHound.Api/Services/RemediationDecisionQueryService.cs
git commit -m "feat: replace DecisionAiSummaryDto with PatchAssessmentDto in remediation case response"
```

---

## Task 11: Remove old remediation AI code

**Files:**
- Delete: `src/PatchHound.Core/Entities/RemediationAiJob.cs`
- Delete: `src/PatchHound.Core/Enums/RemediationAiJobStatus.cs`
- Delete: `src/PatchHound.Infrastructure/Data/Configurations/RemediationAiJobConfiguration.cs`
- Delete: `src/PatchHound.Infrastructure/Services/RemediationAiJobService.cs`
- Delete: `src/PatchHound.Worker/RemediationAiWorker.cs`
- Modify: `src/PatchHound.Api/Controllers/RemediationDecisionsController.cs`

- [ ] **Step 1: Delete the old files**

```bash
rm src/PatchHound.Core/Entities/RemediationAiJob.cs
rm src/PatchHound.Core/Enums/RemediationAiJobStatus.cs
rm src/PatchHound.Infrastructure/Data/Configurations/RemediationAiJobConfiguration.cs
rm src/PatchHound.Infrastructure/Services/RemediationAiJobService.cs
rm src/PatchHound.Worker/RemediationAiWorker.cs
```

- [ ] **Step 2: Remove RemediationAiJobService from RemediationDecisionsController**

In `src/PatchHound.Api/Controllers/RemediationDecisionsController.cs`:

Remove the constructor parameter `RemediationAiJobService remediationAiJobService` (line 27) and its backing usages (the controller calls `remediationAiJobService` — search and remove those endpoints). Also remove any `generate-ai-summary` or `review-ai-summary` endpoints that depend on it.

- [ ] **Step 3: Build**

```bash
dotnet build PatchHound.slnx -v minimal
```

Expected: `Build succeeded.` Fix any remaining references flagged by the compiler.

- [ ] **Step 4: Run all tests**

```bash
dotnet test PatchHound.slnx -v minimal
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: remove RemediationAiJob, RemediationAiJobService, and RemediationAiWorker"
```

---

## Task 12: Frontend — API layer

**Files:**
- Modify: `frontend/src/api/remediation.schemas.ts`
- Modify: `frontend/src/api/remediation.functions.ts`
- Modify: `frontend/src/api/vulnerabilities.functions.ts`

- [ ] **Step 1: Add patchAssessmentSchema and update decisionContextSchema**

In `frontend/src/api/remediation.schemas.ts`:

After `export const threatIntelSchema = z.object({...})` (around line 163), add:

```ts
export const patchAssessmentSchema = z.object({
  recommendation: z.string().nullable(),
  confidence: z.string().nullable(),
  summary: z.string().nullable(),
  urgencyTier: z.string().nullable(),
  urgencyTargetSla: z.string().nullable(),
  urgencyReason: z.string().nullable(),
  similarVulnerabilities: z.string().nullable(),
  compensatingControlsUntilPatched: z.string().nullable(),
  references: z.string().nullable(),
  aiProfileName: z.string().nullable(),
  assessedAt: z.string().nullable(),
  jobStatus: z.string(),
})
```

In `decisionContextSchema`, replace:

```ts
  aiSummary: decisionAiSummarySchema,
```

with:

```ts
  patchAssessment: patchAssessmentSchema,
```

At the bottom of the file, replace:

```ts
export type DecisionAiSummary = z.infer<typeof decisionAiSummarySchema>
```

with:

```ts
export type PatchAssessment = z.infer<typeof patchAssessmentSchema>
```

Delete `decisionAiSummarySchema` (the entire `z.object({...})` block for it).

- [ ] **Step 2: Remove old AI functions from remediation.functions.ts**

In `frontend/src/api/remediation.functions.ts`, remove:
- The import of `decisionAiSummarySchema`
- `export const generateRemediationAiSummary = ...`
- `export const reviewRemediationAiSummary = ...`

- [ ] **Step 3: Add requestVulnerabilityAssessment to vulnerabilities.functions.ts**

In `frontend/src/api/vulnerabilities.functions.ts`, add at the end of the file:

```ts
export const requestVulnerabilityAssessment = createServerFn({ method: 'POST' })
  .validator((data: { tenantId: string; vulnerabilityId: string }) => data)
  .handler(async ({ data }) => {
    const res = await apiFetch(
      `/api/vulnerabilities/${data.vulnerabilityId}/assessment`,
      { method: 'POST', tenantId: data.tenantId }
    )
    if (!res.ok && res.status !== 202) {
      throw new Error(await res.text())
    }
    return res.status
  })
```

(Use the same `apiFetch` / auth pattern as existing functions in that file.)

- [ ] **Step 4: Run frontend typecheck**

```bash
cd frontend && npm run typecheck
```

Expected: No errors. Fix any type references to `aiSummary` or `DecisionAiSummary` that appear.

- [ ] **Step 5: Commit**

```bash
cd .. && git add frontend/src/api/remediation.schemas.ts \
              frontend/src/api/remediation.functions.ts \
              frontend/src/api/vulnerabilities.functions.ts
git commit -m "feat: update frontend API layer for patch priority assessment"
```

---

## Task 13: Frontend — PatchAssessmentPanel component

**Files:**
- Create: `frontend/src/components/features/remediation/PatchAssessmentPanel.tsx`

- [ ] **Step 1: Create the component**

```tsx
// frontend/src/components/features/remediation/PatchAssessmentPanel.tsx
import type { PatchAssessment } from '@/api/remediation.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { LoaderCircle, ShieldAlert } from 'lucide-react'

type Props = {
  assessment: PatchAssessment
  canRequest: boolean
  onRequest: () => void
  requesting: boolean
}

function urgencyTone(tier: string | null): string {
  switch (tier) {
    case 'emergency': return 'destructive'
    case 'as_soon_as_possible': return 'warning'
    case 'normal_patch_window': return 'secondary'
    case 'low_priority': return 'outline'
    default: return 'secondary'
  }
}

function urgencyLabel(tier: string | null): string {
  switch (tier) {
    case 'emergency': return 'Emergency'
    case 'as_soon_as_possible': return 'As Soon As Possible'
    case 'normal_patch_window': return 'Normal Patch Window'
    case 'low_priority': return 'Low Priority'
    default: return tier ?? 'Unknown'
  }
}

export function PatchAssessmentPanel({ assessment, canRequest, onRequest, requesting }: Props) {
  const isLoading = assessment.jobStatus === 'Pending' || assessment.jobStatus === 'Running'
  const hasAssessment = assessment.recommendation !== null

  return (
    <div className="space-y-2">
      {assessment.urgencyTier === 'emergency' && (
        <div className="flex items-center gap-2 rounded-md bg-destructive/15 border border-destructive px-4 py-3 text-destructive font-semibold">
          <ShieldAlert className="h-5 w-5 shrink-0" />
          <span>Emergency patch required — Target SLA: {assessment.urgencyTargetSla}</span>
        </div>
      )}

      <Card>
        <CardHeader className="flex flex-row items-center justify-between pb-2">
          <CardTitle className="text-sm font-medium">Patch Priority Assessment</CardTitle>
          {canRequest && !isLoading && (
            <Button
              variant="outline"
              size="sm"
              onClick={onRequest}
              disabled={requesting}
            >
              {requesting ? (
                <LoaderCircle className="h-4 w-4 animate-spin mr-1" />
              ) : null}
              {hasAssessment ? 'Re-assess' : 'Request assessment'}
            </Button>
          )}
        </CardHeader>

        <CardContent>
          {isLoading && (
            <div className="flex items-center gap-2 text-muted-foreground text-sm py-4">
              <LoaderCircle className="h-4 w-4 animate-spin" />
              Assessment in progress…
            </div>
          )}

          {!isLoading && !hasAssessment && assessment.jobStatus === 'Failed' && (
            <p className="text-sm text-destructive">Assessment failed. Request a new one above.</p>
          )}

          {!isLoading && !hasAssessment && assessment.jobStatus === 'None' && (
            <p className="text-sm text-muted-foreground">No assessment yet.</p>
          )}

          {hasAssessment && !isLoading && (
            <div className="space-y-4 text-sm">
              <div className="flex flex-wrap gap-2">
                <Badge variant={urgencyTone(assessment.urgencyTier) as any}>
                  {urgencyLabel(assessment.urgencyTier)}
                </Badge>
                {assessment.urgencyTargetSla && (
                  <Badge variant="outline">SLA: {assessment.urgencyTargetSla}</Badge>
                )}
                {assessment.confidence && (
                  <Badge variant="secondary">Confidence: {assessment.confidence}</Badge>
                )}
              </div>

              {assessment.recommendation && (
                <div>
                  <div className="font-medium mb-1">Recommendation</div>
                  <p className="text-muted-foreground">{assessment.recommendation}</p>
                </div>
              )}

              {assessment.urgencyReason && (
                <div>
                  <div className="font-medium mb-1">Urgency Reason</div>
                  <p className="text-muted-foreground">{assessment.urgencyReason}</p>
                </div>
              )}

              {assessment.summary && (
                <div>
                  <div className="font-medium mb-1">Summary</div>
                  <p className="text-muted-foreground">{assessment.summary}</p>
                </div>
              )}

              {assessment.compensatingControlsUntilPatched &&
                assessment.compensatingControlsUntilPatched !== '[]' && (
                <div>
                  <div className="font-medium mb-1">Compensating Controls</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {tryParseArray(assessment.compensatingControlsUntilPatched).map((c, i) => (
                      <li key={i}>{typeof c === 'string' ? c : JSON.stringify(c)}</li>
                    ))}
                  </ul>
                </div>
              )}

              {assessment.references && assessment.references !== '[]' && (
                <div>
                  <div className="font-medium mb-1">References</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {tryParseArray(assessment.references).map((r, i) => (
                      <li key={i}>
                        {typeof r === 'string'
                          ? <a href={r} className="underline break-all" target="_blank" rel="noreferrer">{r}</a>
                          : JSON.stringify(r)}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {assessment.assessedAt && (
                <p className="text-xs text-muted-foreground">
                  Assessed {new Date(assessment.assessedAt).toLocaleString()}
                  {assessment.aiProfileName ? ` · ${assessment.aiProfileName}` : ''}
                </p>
              )}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

function tryParseArray(json: string | null): unknown[] {
  try {
    const parsed = JSON.parse(json ?? '[]')
    return Array.isArray(parsed) ? parsed : [parsed]
  } catch {
    return []
  }
}
```

- [ ] **Step 2: Run typecheck**

```bash
cd frontend && npm run typecheck
```

Expected: No errors.

- [ ] **Step 3: Commit**

```bash
cd .. && git add frontend/src/components/features/remediation/PatchAssessmentPanel.tsx
git commit -m "feat: add PatchAssessmentPanel component with emergency ribbon"
```

---

## Task 14: Frontend — SoftwareRemediationView integration

**Files:**
- Modify: `frontend/src/components/features/remediation/SoftwareRemediationView.tsx`

- [ ] **Step 1: Replace AI draft imports and state with assessment panel**

In `SoftwareRemediationView.tsx`:

Remove these imports:
```ts
import {
  generateRemediationAiSummary,
  reviewRemediationAiSummary,
} from '@/api/remediation.functions'
```

Add:
```ts
import { requestVulnerabilityAssessment } from '@/api/vulnerabilities.functions'
import { PatchAssessmentPanel } from './PatchAssessmentPanel'
```

Remove the state:
```ts
const [generatingAiSummary, setGeneratingAiSummary] = useState(false)
```

Add:
```ts
const [requestingAssessment, setRequestingAssessment] = useState(false)
```

Remove the `handleGenerateAiSummary` function entirely.

Add a new handler:
```ts
async function handleRequestAssessment() {
  if (!data?.topVulnerabilities[0]?.vulnerabilityId) return
  setRequestingAssessment(true)
  try {
    await requestVulnerabilityAssessment({
      data: { tenantId: tenant.id, vulnerabilityId: data.topVulnerabilities[0].vulnerabilityId }
    })
    await queryClient.invalidateQueries({ queryKey: ['decision-context', caseId] })
  } catch (err) {
    console.error('Failed to request assessment', err)
  } finally {
    setRequestingAssessment(false)
  }
}
```

- [ ] **Step 2: Replace the RecommendationPanel / AI draft UI block**

Find the JSX block that renders `data.aiSummary` (around line 346 in `SoftwareRemediationView.tsx`). It typically looks like:

```tsx
{data.aiSummary.status === 'Queued' || data.aiSummary.status === 'Generating' ? (
  ...
) : (
  <RecommendationPanel ... />
)}
```

Replace the entire block with:

```tsx
<PatchAssessmentPanel
  assessment={data.patchAssessment}
  canRequest={true}
  onRequest={handleRequestAssessment}
  requesting={requestingAssessment}
/>
```

Also remove the `RecommendationPanel` import if it's no longer used anywhere in the file.

- [ ] **Step 3: Run typecheck and lint**

```bash
cd frontend && npm run typecheck && npm run lint
```

Expected: No errors. Fix any remaining references to `aiSummary`, `generateRemediationAiSummary`, `reviewRemediationAiSummary`, or `generatingAiSummary`.

- [ ] **Step 4: Run all tests**

```bash
cd frontend && npm test
cd .. && dotnet test PatchHound.slnx -v minimal
```

Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/features/remediation/SoftwareRemediationView.tsx
git commit -m "feat: integrate PatchAssessmentPanel in remediation case view, remove AI draft UI"
```

---

## Task 15: Late-discovery notifications (ingestion inline path)

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`

The auto-trigger (Task 6) handles creating jobs for unassessed critical CVEs. This task adds the inline notification path for when an assessment already exists as `emergency` at the time a tenant first discovers the CVE.

- [ ] **Step 1: Inject INotificationService into IngestionService**

Add field:
```csharp
private readonly INotificationService? _notificationService;
```

Add constructor parameter (after `VulnerabilityAssessmentJobService?`):
```csharp
INotificationService? notificationService,
```

Assignment in constructor body:
```csharp
_notificationService = notificationService;
```

- [ ] **Step 2: Add notification dispatch in EnqueueAssessmentJobsForCriticalExposuresAsync**

Update the existing helper method added in Task 6 to also handle the late-discovery path:

```csharp
private async Task EnqueueAssessmentJobsForCriticalExposuresAsync(Guid tenantId, CancellationToken ct)
{
    var criticalVulns = await _dbContext.DeviceVulnerabilityExposures
        .Where(e => e.TenantId == tenantId && e.Status == ExposureStatus.Open)
        .Join(_dbContext.Vulnerabilities,
            e => e.VulnerabilityId,
            v => v.Id,
            (e, v) => new { v.Id, v.ExternalId, v.VendorSeverity })
        .Where(x => x.VendorSeverity == Severity.Critical)
        .Select(x => new { x.Id, x.ExternalId })
        .Distinct()
        .ToListAsync(ct);

    foreach (var vuln in criticalVulns)
    {
        await _vulnerabilityAssessmentJobService!.EnqueueCriticalAsync(tenantId, vuln.Id, ct);

        if (_notificationService is null)
            continue;

        var emergencyAssessment = await _dbContext.VulnerabilityPatchAssessments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.VulnerabilityId == vuln.Id
                && a.UrgencyTier == "emergency", ct);

        if (emergencyAssessment is null)
            continue;

        var userIds = await _dbContext.UserTenantRoles
            .Where(utr => utr.TenantId == tenantId
                && (utr.Role == RoleName.SecurityManager || utr.Role == RoleName.TechnicalManager))
            .Select(utr => utr.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            var alreadyNotified = await _dbContext.Notifications
                .AnyAsync(n => n.UserId == userId
                    && n.Type == NotificationType.NewCriticalVuln
                    && n.RelatedEntityId == vuln.Id, ct);

            if (alreadyNotified)
                continue;

            await _notificationService.SendAsync(
                userId,
                tenantId,
                NotificationType.NewCriticalVuln,
                $"Emergency patch required: {vuln.ExternalId}",
                $"{vuln.ExternalId} requires emergency patching. " +
                $"Confidence: {emergencyAssessment.Confidence}. " +
                $"Target SLA: {emergencyAssessment.UrgencyTargetSla}.",
                "Vulnerability",
                vuln.Id,
                ct);
        }
    }
}
```

- [ ] **Step 3: Build and run tests**

```bash
dotnet build PatchHound.slnx -v minimal
dotnet test PatchHound.slnx -v minimal
```

Expected: Build succeeded, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionService.cs
git commit -m "feat: send emergency notifications inline when existing assessment is found during ingestion"
```

---

## Task 16: Final check

- [ ] **Step 1: Build entire solution**

```bash
dotnet build PatchHound.slnx -v minimal
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 2: Run all backend tests**

```bash
dotnet test PatchHound.slnx -v minimal
```

Expected: All tests pass.

- [ ] **Step 3: Run frontend checks**

```bash
cd frontend && npm run typecheck && npm run lint && npm test
```

Expected: No errors, all tests pass.

- [ ] **Step 4: Verify no references to old types remain**

```bash
grep -r "RemediationAiJob\|DecisionAiSummaryDto\|RemediationAiWorker\|generateRemediationAiSummary\|reviewRemediationAiSummary" \
  src/ frontend/src/ tests/ --include="*.cs" --include="*.ts" --include="*.tsx" \
  | grep -v "\.Designer\." | grep -v "Migrations/"
```

Expected: No output (no remaining references outside designer/migration files).

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "feat: AI patch priority assessment complete (issue #72)"
```
