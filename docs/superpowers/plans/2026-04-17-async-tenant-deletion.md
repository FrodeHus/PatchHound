# Async Tenant Deletion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the synchronous tenant deletion sequence into a background job that returns 202 immediately, blocks access to the tenant while deletion is pending, and pushes an SSE notification to the requesting user on completion.

**Architecture:** A `TenantDeletionJob` DB record is created when deletion is requested; `IsPendingDeletion` on `Tenant` gates all access immediately. `TenantDeletionWorker` (BackgroundService in the API) polls for jobs, claims them atomically, runs the extracted `TenantDeletionService`, then pushes completion via `IEventPusher`. The frontend detects 410 responses globally and shows a non-dismissable tenant-switcher dialog.

**Tech Stack:** ASP.NET Core BackgroundService, EF Core ExecuteUpdateAsync, IEventPusher (HTTP→SSE), TanStack Query QueryCache subscription, React context

---

## File Map

**Create:**
- `src/PatchHound.Core/Enums/TenantDeletionJobStatus.cs`
- `src/PatchHound.Core/Entities/TenantDeletionJob.cs`
- `src/PatchHound.Infrastructure/Data/Configurations/TenantDeletionJobConfiguration.cs`
- `src/PatchHound.Infrastructure/Services/TenantDeletionService.cs`
- `src/PatchHound.Api/Workers/TenantDeletionWorker.cs`
- `frontend/src/components/layout/TenantUnavailableDialog.tsx`
- `frontend/src/lib/tenant-deletion.ts`

**Modify:**
- `src/PatchHound.Core/Entities/Tenant.cs` — add `IsPendingDeletion`
- `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` — add `DbSet<TenantDeletionJob>`
- `src/PatchHound.Api/Controllers/TenantsController.cs` — replace Delete body with enqueue
- `src/PatchHound.Api/Middleware/TenantContextMiddleware.cs` — add 410 gate
- `src/PatchHound.Api/Program.cs` — register worker + service
- `src/PatchHound.Infrastructure/DependencyInjection.cs` — register TenantDeletionService
- `frontend/src/server/api.ts` — add TenantPendingDeletionError + 410 handling
- `frontend/src/hooks/useSSE.ts` — add TenantDeleted / TenantDeletionFailed event types
- `frontend/src/components/layout/tenant-scope.ts` — add pending deletion state to context type
- `frontend/src/components/layout/TenantScopeProvider.tsx` — QueryCache + SSE wiring
- `frontend/src/components/layout/AppShell.tsx` — render TenantUnavailableDialog
- `frontend/src/components/features/admin/TenantAdministrationDetail.tsx` — update delete mutation success

---

## Task 1: Core data model — enum, Tenant.IsPendingDeletion, TenantDeletionJob entity

**Files:**
- Create: `src/PatchHound.Core/Enums/TenantDeletionJobStatus.cs`
- Modify: `src/PatchHound.Core/Entities/Tenant.cs`
- Create: `src/PatchHound.Core/Entities/TenantDeletionJob.cs`
- Test: `tests/PatchHound.Tests/Core/TenantDeletionJobTests.cs`

- [ ] **Step 1: Write failing tests for TenantDeletionJob entity**

```csharp
// tests/PatchHound.Tests/Core/TenantDeletionJobTests.cs
using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Tests.Core;

public class TenantDeletionJobTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_sets_Pending_status_and_timestamps()
    {
        var before = DateTimeOffset.UtcNow;
        var job = TenantDeletionJob.Create(TenantId, UserId);
        var after = DateTimeOffset.UtcNow;

        job.TenantId.Should().Be(TenantId);
        job.RequestedByUserId.Should().Be(UserId);
        job.Status.Should().Be(TenantDeletionJobStatus.Pending);
        job.StartedAt.Should().BeNull();
        job.CompletedAt.Should().BeNull();
        job.Error.Should().BeNull();
        job.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void MarkRunning_sets_Running_and_StartedAt()
    {
        var job = TenantDeletionJob.Create(TenantId, UserId);
        var before = DateTimeOffset.UtcNow;
        job.MarkRunning();
        var after = DateTimeOffset.UtcNow;

        job.Status.Should().Be(TenantDeletionJobStatus.Running);
        job.StartedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void MarkCompleted_sets_Completed_and_CompletedAt()
    {
        var job = TenantDeletionJob.Create(TenantId, UserId);
        job.MarkRunning();
        var before = DateTimeOffset.UtcNow;
        job.MarkCompleted();
        var after = DateTimeOffset.UtcNow;

        job.Status.Should().Be(TenantDeletionJobStatus.Completed);
        job.CompletedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        job.Error.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_sets_Failed_and_Error()
    {
        var job = TenantDeletionJob.Create(TenantId, UserId);
        job.MarkRunning();
        job.MarkFailed("Something went wrong");

        job.Status.Should().Be(TenantDeletionJobStatus.Failed);
        job.Error.Should().Be("Something went wrong");
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Reset_restores_Pending_and_clears_run_fields()
    {
        var job = TenantDeletionJob.Create(TenantId, UserId);
        job.MarkRunning();
        job.MarkFailed("error");
        var newUser = Guid.NewGuid();

        job.Reset(newUser);

        job.Status.Should().Be(TenantDeletionJobStatus.Pending);
        job.RequestedByUserId.Should().Be(newUser);
        job.StartedAt.Should().BeNull();
        job.CompletedAt.Should().BeNull();
        job.Error.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test PatchHound.slnx --filter "FullyQualifiedName~TenantDeletionJobTests" -v minimal
```
Expected: compilation error — `TenantDeletionJob` does not exist yet.

- [ ] **Step 3: Create the enum**

```csharp
// src/PatchHound.Core/Enums/TenantDeletionJobStatus.cs
namespace PatchHound.Core.Enums;

public enum TenantDeletionJobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}
```

- [ ] **Step 4: Add IsPendingDeletion to Tenant**

Edit `src/PatchHound.Core/Entities/Tenant.cs`:

```csharp
namespace PatchHound.Core.Entities;

public class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string EntraTenantId { get; private set; } = null!;
    public bool IsPrimary { get; private set; }
    public bool IsPendingDeletion { get; private set; }

    private Tenant() { }

    public static Tenant Create(string name, string entraTenantId, bool isPrimary = false)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            EntraTenantId = entraTenantId,
            IsPrimary = isPrimary,
        };
    }

    public void UpdateName(string name)
    {
        Name = name;
    }

    public void MarkPendingDeletion()
    {
        IsPendingDeletion = true;
    }
}
```

- [ ] **Step 5: Create the TenantDeletionJob entity**

```csharp
// src/PatchHound.Core/Entities/TenantDeletionJob.cs
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class TenantDeletionJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public TenantDeletionJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Error { get; private set; }

    private TenantDeletionJob() { }

    public static TenantDeletionJob Create(Guid tenantId, Guid requestedByUserId)
    {
        return new TenantDeletionJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestedByUserId = requestedByUserId,
            Status = TenantDeletionJobStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void MarkRunning()
    {
        Status = TenantDeletionJobStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCompleted()
    {
        Status = TenantDeletionJobStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = TenantDeletionJobStatus.Failed;
        Error = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Reset(Guid requestedByUserId)
    {
        RequestedByUserId = requestedByUserId;
        Status = TenantDeletionJobStatus.Pending;
        StartedAt = null;
        CompletedAt = null;
        Error = null;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test PatchHound.slnx --filter "FullyQualifiedName~TenantDeletionJobTests" -v minimal
```
Expected: all 5 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Core/Enums/TenantDeletionJobStatus.cs \
        src/PatchHound.Core/Entities/Tenant.cs \
        src/PatchHound.Core/Entities/TenantDeletionJob.cs \
        tests/PatchHound.Tests/Core/TenantDeletionJobTests.cs
git commit -m "feat(core): add TenantDeletionJob entity and IsPendingDeletion flag on Tenant"
```

---

## Task 2: EF configuration, DbContext, and migration

**Files:**
- Create: `src/PatchHound.Infrastructure/Data/Configurations/TenantDeletionJobConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Create: EF migration (generated via CLI)

- [ ] **Step 1: Create EF configuration**

```csharp
// src/PatchHound.Infrastructure/Data/Configurations/TenantDeletionJobConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantDeletionJobConfiguration : IEntityTypeConfiguration<TenantDeletionJob>
{
    public void Configure(EntityTypeBuilder<TenantDeletionJob> builder)
    {
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(j => j.Error).HasMaxLength(2048);
        builder.HasIndex(j => j.TenantId).IsUnique();
        builder.HasIndex(j => j.Status);
    }
}
```

The unique index on `TenantId` enforces one job per tenant at any time.

- [ ] **Step 2: Add DbSet to PatchHoundDbContext**

In `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`, add alongside the other `DbSet` properties (after the `Tenants` line):

```csharp
public DbSet<TenantDeletionJob> TenantDeletionJobs => Set<TenantDeletionJob>();
```

- [ ] **Step 3: Generate the migration**

```bash
dotnet ef migrations add AddTenantDeletionJob \
    --project src/PatchHound.Infrastructure \
    --startup-project src/PatchHound.Api
```
Expected: new migration files created under `src/PatchHound.Infrastructure/Migrations/`.

- [ ] **Step 4: Verify migration content**

Open the generated migration file. Confirm it contains:
- `AddColumn` for `IsPendingDeletion` on `Tenants` (bool, not null, default false)
- `CreateTable` for `TenantDeletionJobs` with all columns
- `CreateIndex` for the unique index on `TenantId` and the status index

- [ ] **Step 5: Build to verify no errors**

```bash
dotnet build PatchHound.slnx -v minimal 2>&1 | tail -5
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/Configurations/TenantDeletionJobConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        src/PatchHound.Infrastructure/Migrations/
git commit -m "feat(infrastructure): add TenantDeletionJob table and IsPendingDeletion column"
```

---

## Task 3: TenantDeletionService — extract deletion logic

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/TenantDeletionService.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create TenantDeletionService**

Copy the entire deletion body from `TenantsController.Delete` (lines 400–538) and wrap it in a service. The controller's `_dbContext`, `_secretStore`, and `_tenantContext` become constructor parameters. Note: the service does NOT call `_dbContext.Tenants.Remove(tenant)` — the worker calls `MarkCompleted` instead; tenant record removal stays in the worker after the service succeeds (or can be included here — keep it in the service for atomicity):

```csharp
// src/PatchHound.Infrastructure/Services/TenantDeletionService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class TenantDeletionService(
    PatchHoundDbContext dbContext,
    ISecretStore secretStore,
    ILogger<TenantDeletionService> logger
)
{
    public async Task DeleteAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

        logger.LogInformation("Starting background deletion of tenant {TenantId}", tenantId);

        var tenantSourceSecretRefs = await dbContext.TenantSourceConfigurations
            .IgnoreQueryFilters()
            .Where(source => source.TenantId == tenantId && source.SecretRef != string.Empty)
            .Select(source => source.SecretRef)
            .ToListAsync(ct);
        var aiProfileSecretRefs = await dbContext.TenantAiProfiles
            .IgnoreQueryFilters()
            .Where(profile => profile.TenantId == tenantId && profile.SecretRef != string.Empty)
            .Select(profile => profile.SecretRef)
            .ToListAsync(ct);
        var secretRefs = tenantSourceSecretRefs
            .Concat(aiProfileSecretRefs)
            .Where(secretRef => !string.IsNullOrWhiteSpace(secretRef))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var secretRef in secretRefs)
        {
            await secretStore.DeleteSecretPathAsync(secretRef, ct);
        }

        var affectedUserIds = await dbContext.UserTenantRoles
            .IgnoreQueryFilters()
            .Where(role => role.TenantId == tenantId)
            .Select(role => role.UserId)
            .Distinct()
            .ToListAsync(ct);
        var userIdsToDelete = affectedUserIds.Count == 0
            ? []
            : await dbContext.Users
                .IgnoreQueryFilters()
                .Where(user => affectedUserIds.Contains(user.Id) && user.AccessScope == Core.Enums.UserAccessScope.Customer)
                .Where(user => !dbContext.UserTenantRoles.IgnoreQueryFilters()
                    .Any(role => role.UserId == user.Id && role.TenantId != tenantId))
                .Where(user => !dbContext.TeamMembers.IgnoreQueryFilters()
                    .Any(member => member.UserId == user.Id && member.Team.TenantId != tenantId))
                .Select(user => user.Id)
                .ToListAsync(ct);

        await DeleteEntitiesAsync(
            dbContext.WorkflowNodeExecutions
                .IgnoreQueryFilters()
                .Where(execution =>
                    dbContext.WorkflowInstances
                        .IgnoreQueryFilters()
                        .Where(instance => instance.TenantId == tenantId)
                        .Select(instance => instance.Id)
                        .Contains(execution.WorkflowInstanceId)),
            ct);
        await DeleteEntitiesAsync(
            dbContext.ApprovalTaskVisibleRoles
                .IgnoreQueryFilters()
                .Where(item =>
                    dbContext.ApprovalTasks
                        .IgnoreQueryFilters()
                        .Where(task => task.TenantId == tenantId)
                        .Select(task => task.Id)
                        .Contains(item.ApprovalTaskId)),
            ct);
        await DeleteEntitiesAsync(
            dbContext.RemediationDecisionVulnerabilityOverrides
                .IgnoreQueryFilters()
                .Where(item =>
                    dbContext.RemediationDecisions
                        .IgnoreQueryFilters()
                        .Where(decision => decision.TenantId == tenantId)
                        .Select(decision => decision.Id)
                        .Contains(item.RemediationDecisionId)),
            ct);
        await DeleteEntitiesAsync(
            dbContext.TeamMembers
                .IgnoreQueryFilters()
                .Where(member => member.Team.TenantId == tenantId),
            ct);

        await DeleteEntitiesAsync(dbContext.RemediationWorkflowStageRecords.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.WorkflowActions.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.WorkflowInstances.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.WorkflowDefinitions.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.ApprovalTasks.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.PatchingTasks.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.AnalystRecommendations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.RemediationDecisions.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.RemediationWorkflows.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.Comments.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.RiskAcceptances.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.Notifications.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.AIReports.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.SoftwareDescriptionJobs.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceTags.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceBusinessLabels.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.BusinessLabels.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceRules.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceGroupRiskScores.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TeamRiskScores.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TenantRiskScoreSnapshots.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.OrganizationalSeverities.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceSoftwareInstallationEpisodes.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceSoftwareInstallations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.SoftwareProductInstallations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.SoftwareTenantRecords.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.SecurityProfiles.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.ExposureAssessments.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.ExposureEpisodes.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceVulnerabilityExposures.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.DeviceRiskScores.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.Devices.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TeamMembershipRules.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.Teams.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.UserTenantRoles.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TenantAiProfiles.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TenantSlaConfigurations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.EnrichmentJobs.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.StagedDeviceSoftwareInstallations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.StagedDevices.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.StagedCloudApplications.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.CloudApplicationCredentialMetadata.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.CloudApplications.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.StagedVulnerabilityExposures.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.StagedVulnerabilities.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.IngestionCheckpoints.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.IngestionSnapshots.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.IngestionRuns.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);
        await DeleteEntitiesAsync(dbContext.TenantSourceConfigurations.IgnoreQueryFilters().Where(item => item.TenantId == tenantId), ct);

        if (userIdsToDelete.Count > 0)
        {
            await DeleteEntitiesAsync(
                dbContext.Users.IgnoreQueryFilters().Where(user => userIdsToDelete.Contains(user.Id)),
                ct);
        }

        dbContext.Tenants.Remove(tenant);
        await dbContext.TenantDeletionJobs.IgnoreQueryFilters()
            .Where(j => j.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Completed background deletion of tenant {TenantId}", tenantId);
    }

    private static async Task DeleteEntitiesAsync<T>(IQueryable<T> query, CancellationToken ct)
        where T : class
    {
        await query.ExecuteDeleteAsync(ct);
    }
}
```

- [ ] **Step 2: Register in DependencyInjection.cs**

In `src/PatchHound.Infrastructure/DependencyInjection.cs`, add:

```csharp
services.AddScoped<TenantDeletionService>();
```

Place it near other service registrations (before or after `RemediationWorkflowService`).

- [ ] **Step 3: Build to verify**

```bash
dotnet build PatchHound.slnx -v minimal 2>&1 | tail -5
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/TenantDeletionService.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs
git commit -m "feat(infrastructure): extract TenantDeletionService from controller"
```

---

## Task 4: TenantContextMiddleware — 410 gate for pending-deletion tenants

**Files:**
- Modify: `src/PatchHound.Api/Middleware/TenantContextMiddleware.cs`

- [ ] **Step 1: Add the pending-deletion check**

In `TenantContextMiddleware.InvokeAsync`, after `tc.InitializeAsync(...)` completes and before `await _next(context)`, insert:

```csharp
// Check whether the requested tenant is pending deletion.
var requestedTenantIdHeader = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
if (Guid.TryParse(requestedTenantIdHeader, out var requestedTenantId))
{
    var isPendingDeletion = await dbContext.Tenants
        .IgnoreQueryFilters()
        .Where(t => t.Id == requestedTenantId && t.IsPendingDeletion)
        .AnyAsync(context.RequestAborted);

    if (isPendingDeletion)
    {
        context.Response.StatusCode = 410;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            """{"errorCode":"tenant_pending_deletion"}""",
            context.RequestAborted
        );
        return;
    }
}
```

The full updated `InvokeAsync` should look like:

```csharp
public async Task InvokeAsync(HttpContext context)
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var tenantContext = context.RequestServices.GetService<ITenantContext>();
        if (tenantContext is TenantContext tc)
        {
            var dbContext = context.RequestServices.GetRequiredService<PatchHoundDbContext>();
            var teamMembershipRuleService = context.RequestServices.GetRequiredService<PatchHound.Infrastructure.Services.TeamMembershipRuleService>();
            await tc.InitializeAsync(context, dbContext, teamMembershipRuleService);

            var requestedTenantIdHeader = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            if (Guid.TryParse(requestedTenantIdHeader, out var requestedTenantId))
            {
                var isPendingDeletion = await dbContext.Tenants
                    .IgnoreQueryFilters()
                    .Where(t => t.Id == requestedTenantId && t.IsPendingDeletion)
                    .AnyAsync(context.RequestAborted);

                if (isPendingDeletion)
                {
                    context.Response.StatusCode = 410;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        """{"errorCode":"tenant_pending_deletion"}""",
                        context.RequestAborted
                    );
                    return;
                }
            }
        }
    }

    await _next(context);

    if (context.Items.TryGetValue(TenantContext.BlockedTenantAccessItemsKey, out var existing)
        && existing is List<BlockedTenantAccessAttempt> attempts
        && attempts.Count > 0
        && context.User.Identity?.IsAuthenticated == true)
    {
        var logger = context.RequestServices.GetService<BlockedTenantAccessLogger>();
        var dbContext = context.RequestServices.GetService<PatchHoundDbContext>();
        if (logger is not null && dbContext is not null)
        {
            await logger.LogAsync(attempts, context.RequestAborted);
            await dbContext.SaveChangesAsync(context.RequestAborted);
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build PatchHound.slnx -v minimal 2>&1 | tail -5
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Api/Middleware/TenantContextMiddleware.cs
git commit -m "feat(api): return 410 for requests to pending-deletion tenants"
```

---

## Task 5: Update TenantsController.Delete to enqueue the job

**Files:**
- Modify: `src/PatchHound.Api/Controllers/TenantsController.cs`

The existing `Delete` action runs lines ~391–541. Replace the entire body (keep the method signature and `[HttpDelete]`/`[Authorize]` attributes) with the enqueue logic:

- [ ] **Step 1: Replace the Delete action body**

Find `public async Task<IActionResult> Delete(Guid id, CancellationToken ct)` and replace its body with:

```csharp
if (!_tenantContext.HasAccessToTenant(id))
    return Forbid();

var tenant = await _dbContext.Tenants
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(t => t.Id == id, ct);
if (tenant is null)
    return NotFound();

tenant.MarkPendingDeletion();

var existingJob = await _dbContext.TenantDeletionJobs
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(j => j.TenantId == id, ct);

if (existingJob is not null)
{
    existingJob.Reset(_tenantContext.CurrentUserId);
}
else
{
    var job = TenantDeletionJob.Create(id, _tenantContext.CurrentUserId);
    await _dbContext.TenantDeletionJobs.AddAsync(job, ct);
}

await _dbContext.SaveChangesAsync(ct);
return Accepted();
```

Add `using PatchHound.Core.Entities;` at the top of the file if not already present.

- [ ] **Step 2: Build to verify**

```bash
dotnet build PatchHound.slnx -v minimal 2>&1 | tail -5
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Api/Controllers/TenantsController.cs
git commit -m "feat(api): enqueue tenant deletion job instead of deleting synchronously"
```

---

## Task 6: TenantDeletionWorker — background service

**Files:**
- Create: `src/PatchHound.Api/Workers/TenantDeletionWorker.cs`
- Modify: `src/PatchHound.Api/Program.cs`

- [ ] **Step 1: Create the worker**

```csharp
// src/PatchHound.Api/Workers/TenantDeletionWorker.cs
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Workers;

public class TenantDeletionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TenantDeletionWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await RecoverStaleJobsAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            await ProcessNextJobAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
    }

    private async Task RecoverStaleJobsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        var recovered = await dbContext.TenantDeletionJobs
            .IgnoreQueryFilters()
            .Where(j => j.Status == TenantDeletionJobStatus.Running)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, TenantDeletionJobStatus.Pending)
                      .SetProperty(j => j.StartedAt, (DateTimeOffset?)null),
                ct);

        if (recovered > 0)
            logger.LogWarning("Recovered {Count} stale Running deletion job(s) to Pending on startup", recovered);
    }

    private async Task ProcessNextJobAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        var pendingJob = await dbContext.TenantDeletionJobs
            .IgnoreQueryFilters()
            .Where(j => j.Status == TenantDeletionJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (pendingJob is null)
            return;

        // Claim atomically — another instance may race us.
        var claimed = await dbContext.TenantDeletionJobs
            .IgnoreQueryFilters()
            .Where(j => j.Id == pendingJob.Id && j.Status == TenantDeletionJobStatus.Pending)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, TenantDeletionJobStatus.Running)
                      .SetProperty(j => j.StartedAt, DateTimeOffset.UtcNow),
                ct);

        if (claimed == 0)
            return;

        var tenantId = pendingJob.TenantId;
        var userId = pendingJob.RequestedByUserId.ToString();

        logger.LogInformation("Processing deletion job for tenant {TenantId}", tenantId);

        var eventPusher = scope.ServiceProvider.GetRequiredService<IEventPusher>();

        try
        {
            var deletionService = scope.ServiceProvider.GetRequiredService<TenantDeletionService>();
            await deletionService.DeleteAsync(tenantId, ct);

            logger.LogInformation("Tenant {TenantId} deleted successfully", tenantId);
            await eventPusher.PushAsync(
                "TenantDeleted",
                new { tenantId },
                userId: userId,
                ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete tenant {TenantId}", tenantId);

            await dbContext.TenantDeletionJobs
                .IgnoreQueryFilters()
                .Where(j => j.Id == pendingJob.Id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(j => j.Status, TenantDeletionJobStatus.Failed)
                          .SetProperty(j => j.Error, ex.Message)
                          .SetProperty(j => j.CompletedAt, DateTimeOffset.UtcNow),
                    ct);

            await eventPusher.PushAsync(
                "TenantDeletionFailed",
                new { tenantId },
                userId: userId,
                ct: ct);
        }
    }
}
```

- [ ] **Step 2: Register the worker in Program.cs**

In `src/PatchHound.Api/Program.cs`, find where services are added (before `var app = builder.Build()`) and add:

```csharp
builder.Services.AddHostedService<PatchHound.Api.Workers.TenantDeletionWorker>();
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build PatchHound.slnx -v minimal 2>&1 | tail -5
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Api/Workers/TenantDeletionWorker.cs \
        src/PatchHound.Api/Program.cs
git commit -m "feat(api): add TenantDeletionWorker background service"
```

---

## Task 7: Frontend — TenantPendingDeletionError and SSE event types

**Files:**
- Modify: `frontend/src/server/api.ts`
- Modify: `frontend/src/hooks/useSSE.ts`
- Create: `frontend/src/lib/tenant-deletion.ts`

- [ ] **Step 1: Add TenantPendingDeletionError to api.ts**

In `frontend/src/server/api.ts`, add after `ForbiddenApiError`:

```typescript
export class TenantPendingDeletionError extends ApiRequestError {
  constructor(statusText: string, bodyText: string | null) {
    super('TENANT_PENDING_DELETION', 410, statusText, bodyText)
    this.name = 'TenantPendingDeletionError'
  }
}
```

Then in `ensureOk`, add before the generic `throw` at the end:

```typescript
if (response.status === 410) {
  const body = (await response.text()).trim() || null
  let errorCode: string | null = null
  try { errorCode = body ? (JSON.parse(body) as { errorCode?: string }).errorCode ?? null : null } catch { /* ignore */ }
  if (errorCode === 'tenant_pending_deletion') {
    throw new TenantPendingDeletionError(response.statusText, body)
  }
}
```

The full updated `ensureOk`:

```typescript
async function ensureOk(response: Response): Promise<void> {
  if (response.ok) {
    return
  }

  if (response.status === 410) {
    const body = (await response.text()).trim() || null
    let errorCode: string | null = null
    try { errorCode = body ? (JSON.parse(body) as { errorCode?: string }).errorCode ?? null : null } catch { /* ignore */ }
    if (errorCode === 'tenant_pending_deletion') {
      throw new TenantPendingDeletionError(response.statusText, body)
    }
  }

  const bodyText = (await response.text()).trim() || null

  if (response.status === 401) {
    throw new UnauthenticatedApiError(response.statusText, bodyText)
  }

  if (response.status === 403) {
    throw new ForbiddenApiError(response.statusText, bodyText)
  }

  if (response.status === 400 || response.status === 422) {
    throw new ValidationApiError(response.status, response.statusText, bodyText)
  }

  throw new ApiRequestError(
    buildFriendlyErrorMessage(
      bodyText,
      `API request failed with ${response.status} ${response.statusText}.`,
    ),
    response.status,
    response.statusText,
    bodyText,
  )
}
```

- [ ] **Step 2: Add SSE event types to useSSE.ts**

In `frontend/src/hooks/useSSE.ts`, update the `SSEEvent` union:

```typescript
type SSEEvent =
  | 'NotificationCountUpdated'
  | 'CriticalVulnerabilityDetected'
  | 'TaskStatusChanged'
  | 'IngestionRunProgress'
  | 'TenantDeleted'
  | 'TenantDeletionFailed'
```

- [ ] **Step 3: Create tenant-deletion detection utility**

```typescript
// frontend/src/lib/tenant-deletion.ts
export function isTenantPendingDeletion(error: unknown): boolean {
  if (!error || typeof error !== 'object') return false
  const err = error as Record<string, unknown>
  // name is preserved through TanStack Start's error serialization
  return err['name'] === 'TenantPendingDeletionError' || err['message'] === 'TENANT_PENDING_DELETION'
}
```

- [ ] **Step 4: TypeScript check**

```bash
cd frontend && npm run typecheck 2>&1 | tail -10
```
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/server/api.ts \
        frontend/src/hooks/useSSE.ts \
        frontend/src/lib/tenant-deletion.ts
git commit -m "feat(frontend): add TenantPendingDeletionError and SSE event types for tenant deletion"
```

---

## Task 8: Frontend — TenantUnavailableDialog component

**Files:**
- Create: `frontend/src/components/layout/TenantUnavailableDialog.tsx`

- [ ] **Step 1: Create the dialog**

```tsx
// frontend/src/components/layout/TenantUnavailableDialog.tsx
import { Building2 } from 'lucide-react'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/ui/dialog'
import { TenantSelector } from '@/components/layout/TenantSelector'

type TenantUnavailableDialogProps = {
  open: boolean
  tenants: Array<{ id: string; name: string }>
  onSelectTenant: (tenantId: string) => void
}

export function TenantUnavailableDialog({ open, tenants, onSelectTenant }: TenantUnavailableDialogProps) {
  return (
    <Dialog open={open} onOpenChange={() => { /* non-dismissable */ }}>
      <DialogContent
        className="max-w-sm"
        onEscapeKeyDown={(e) => e.preventDefault()}
        onPointerDownOutside={(e) => e.preventDefault()}
        onInteractOutside={(e) => e.preventDefault()}
        hideCloseButton
      >
        <DialogHeader className="items-center text-center">
          <span className="flex size-12 items-center justify-center rounded-2xl border border-border/60 bg-muted/40 mb-2">
            <Building2 className="size-5 text-muted-foreground" />
          </span>
          <DialogTitle>Tenant no longer available</DialogTitle>
          <DialogDescription>
            This tenant is being deleted and can no longer be accessed. Please select another tenant to continue.
          </DialogDescription>
        </DialogHeader>
        <div className="mt-2 flex justify-center">
          <TenantSelector
            tenants={tenants}
            selectedTenantId={null}
            onSelectTenant={onSelectTenant}
          />
        </div>
      </DialogContent>
    </Dialog>
  )
}
```

> **Note:** If `hideCloseButton` is not a prop on `DialogContent` in this project's `dialog.tsx`, check the component and either add that prop or remove the close button via the `[&>button]:hidden` className instead.

- [ ] **Step 2: TypeScript check**

```bash
cd frontend && npm run typecheck 2>&1 | tail -10
```
Expected: no errors. If `hideCloseButton` causes an error, replace with `className="[&>button:last-child]:hidden"` on `DialogContent`.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/layout/TenantUnavailableDialog.tsx
git commit -m "feat(frontend): add TenantUnavailableDialog component"
```

---

## Task 9: Frontend — wire TenantScopeProvider and AppShell

**Files:**
- Modify: `frontend/src/components/layout/tenant-scope.ts`
- Modify: `frontend/src/components/layout/TenantScopeProvider.tsx`
- Modify: `frontend/src/components/layout/AppShell.tsx`

- [ ] **Step 1: Extend the context type in tenant-scope.ts**

In `frontend/src/components/layout/tenant-scope.ts`, add `tenantPendingDeletion` to `TenantScopeContextValue`:

```typescript
export type TenantScopeContextValue = {
  selectedTenantId: string | null
  tenants: Array<{ id: string; name: string }>
  isLoadingTenants: boolean
  setSelectedTenantId: (tenantId: string) => void
  tenantPendingDeletion: boolean
  clearTenantPendingDeletion: () => void
}
```

- [ ] **Step 2: Wire state and listeners in TenantScopeProvider.tsx**

Replace the full content of `TenantScopeProvider.tsx`:

```tsx
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { fetchTenants } from '@/api/settings.functions'
import type { TenantListItem } from '@/api/settings.schemas'
import type { CurrentUser } from '@/server/auth.functions'
import { isTenantPendingDeletion } from '@/lib/tenant-deletion'
import { useSSE } from '@/hooks/useSSE'
import {
  persistSelectedTenant,
  selectedTenantCookieKey,
  selectedTenantStorageKey,
  TenantScopeContext,
  type TenantScopeContextValue,
} from '@/components/layout/tenant-scope'

function getInitialTenantId(): string | null {
  if (typeof window === 'undefined') return null

  const storedTenantId = window.localStorage.getItem(selectedTenantStorageKey)
  if (storedTenantId) {
    return storedTenantId
  }

  const cookieValue = document.cookie
    .split('; ')
    .find((entry) => entry.startsWith(`${selectedTenantCookieKey}=`))
    ?.slice(selectedTenantCookieKey.length + 1)

  return cookieValue ? decodeURIComponent(cookieValue) : null
}

function buildTenantOptions(user: CurrentUser, tenantItems: TenantListItem[] | undefined) {
  if (tenantItems && tenantItems.length > 0) {
    return tenantItems.map((tenant) => ({
      id: tenant.id,
      name: tenant.name,
    }))
  }

  const allowedIds = user.tenantIds.length ? user.tenantIds : []

  return allowedIds.map((tenantId, index) => ({
    id: tenantId,
    name: `Tenant ${index + 1}`,
  }))
}

type TenantScopeProviderProps = {
  user: CurrentUser
  children: ReactNode
}

export function TenantScopeProvider({ user, children }: TenantScopeProviderProps) {
  const [storedTenantId, setStoredTenantId] = useState<string | null>(getInitialTenantId)
  const [tenantPendingDeletion, setTenantPendingDeletion] = useState(false)
  const queryClient = useQueryClient()

  const tenantQuery = useQuery({
    queryKey: ['tenant-scope', 'tenants'],
    queryFn: () => fetchTenants({ data: { page: 1, pageSize: 100 } }),
  })

  const tenants = useMemo(
    () => buildTenantOptions(user, tenantQuery.data?.items),
    [tenantQuery.data?.items, user],
  )

  const effectiveSelectedTenantId = useMemo(() => {
    if (storedTenantId && tenants.some((tenant) => tenant.id === storedTenantId)) {
      return storedTenantId
    }
    return tenants[0]?.id ?? null
  }, [storedTenantId, tenants])

  useEffect(() => {
    persistSelectedTenant(effectiveSelectedTenantId)
  }, [effectiveSelectedTenantId])

  // Detect 410 errors from any query — triggers the unavailable dialog.
  useEffect(() => {
    return queryClient.getQueryCache().subscribe((event) => {
      if (event.type === 'updated' && event.query.state.status === 'error') {
        if (isTenantPendingDeletion(event.query.state.error)) {
          setTenantPendingDeletion(true)
        }
      }
    })
  }, [queryClient])

  // SSE: another user's deletion completed while we're looking at that tenant.
  useSSE('TenantDeleted', (data) => {
    const payload = data as { tenantId?: string }
    queryClient.invalidateQueries({ queryKey: ['tenant-scope', 'tenants'] })
    if (payload?.tenantId === effectiveSelectedTenantId) {
      setTenantPendingDeletion(true)
    } else {
      toast.success('Tenant deleted successfully.')
    }
  })

  useSSE('TenantDeletionFailed', () => {
    queryClient.invalidateQueries({ queryKey: ['tenant-scope', 'tenants'] })
    toast.error('Tenant deletion failed. Please contact an administrator.')
  })

  const value = useMemo<TenantScopeContextValue>(() => ({
    selectedTenantId: effectiveSelectedTenantId,
    tenants,
    isLoadingTenants: tenantQuery.isPending,
    setSelectedTenantId: (tenantId: string) => {
      setStoredTenantId(tenantId)
      persistSelectedTenant(tenantId)
    },
    tenantPendingDeletion,
    clearTenantPendingDeletion: () => setTenantPendingDeletion(false),
  }), [effectiveSelectedTenantId, tenantQuery.isPending, tenants, tenantPendingDeletion])

  return (
    <TenantScopeContext.Provider value={value}>
      {children}
    </TenantScopeContext.Provider>
  )
}
```

- [ ] **Step 3: Render TenantUnavailableDialog in AppShell.tsx**

Replace the full content of `frontend/src/components/layout/AppShell.tsx`:

```tsx
import { useState, type ReactNode } from 'react'
import { Sidebar } from '@/components/layout/Sidebar'
import { TenantScopeProvider } from '@/components/layout/TenantScopeProvider'
import { TenantUnavailableDialog } from '@/components/layout/TenantUnavailableDialog'
import { TopNav } from '@/components/layout/TopNav'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Sheet, SheetContent } from '@/components/ui/sheet'
import type { CurrentUser } from '@/server/auth.functions'

const sidebarStorageKey = "patchhound:sidebar-collapsed"

function getInitialSidebarCollapsed(): boolean {
  if (typeof window === "undefined") return false
  return window.sessionStorage.getItem(sidebarStorageKey) === "true"
}

function TenantGuard({ children }: { children: ReactNode }) {
  const { tenantPendingDeletion, clearTenantPendingDeletion, tenants, selectedTenantId, setSelectedTenantId } = useTenantScope()
  const availableTenants = tenants.filter(t => t.id !== selectedTenantId)
  return (
    <>
      <TenantUnavailableDialog
        open={tenantPendingDeletion}
        tenants={availableTenants}
        onSelectTenant={(id) => {
          setSelectedTenantId(id)
          clearTenantPendingDeletion()
        }}
      />
      {children}
    </>
  )
}

type AppShellProps = {
  user: CurrentUser
  children: ReactNode
}

export function AppShell({ user, children }: AppShellProps) {
  const [isSidebarOpen, setIsSidebarOpen] = useState(false)
  const [isDesktopCollapsed, setIsDesktopCollapsed] = useState(getInitialSidebarCollapsed)

  const toggleDesktopSidebar = () => {
    setIsDesktopCollapsed((prev) => {
      const next = !prev
      try { window.sessionStorage.setItem(sidebarStorageKey, String(next)) } catch { /* ignore */ }
      return next
    })
  }

  return (
    <TenantScopeProvider user={user}>
      <TenantGuard>
        <div className="min-h-screen bg-background text-foreground">
          <div className="flex min-h-screen">
            <div className="sticky top-0 hidden h-screen md:block">
              <Sidebar user={user} collapsed={isDesktopCollapsed} />
            </div>

            <Sheet open={isSidebarOpen} onOpenChange={setIsSidebarOpen}>
              <SheetContent
                side="left"
                className="w-[22rem] border-r border-sidebar-border/80 bg-sidebar/94 p-0 text-sidebar-foreground sm:max-w-[22rem]"
              >
                <Sidebar
                  user={user}
                  compact
                  onNavigate={() => { setIsSidebarOpen(false) }}
                />
              </SheetContent>
            </Sheet>

            <div className="flex min-h-screen min-w-0 flex-1 flex-col">
              <TopNav
                user={user}
                onToggleSidebar={() => { setIsSidebarOpen((v) => !v) }}
                onToggleDesktopSidebar={toggleDesktopSidebar}
                isDesktopSidebarCollapsed={isDesktopCollapsed}
                onLogout={() => { window.location.href = "/auth/logout" }}
              />
              <main className="flex-1 px-4 pb-6 sm:px-6">
                <div className="mx-auto w-full max-w-[1600px]">{children}</div>
              </main>
            </div>
          </div>
        </div>
      </TenantGuard>
    </TenantScopeProvider>
  )
}
```

- [ ] **Step 4: TypeScript check**

```bash
cd frontend && npm run typecheck 2>&1 | tail -10
```
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/layout/tenant-scope.ts \
        frontend/src/components/layout/TenantScopeProvider.tsx \
        frontend/src/components/layout/AppShell.tsx
git commit -m "feat(frontend): wire tenant pending deletion detection and unavailable dialog"
```

---

## Task 10: Frontend — update delete mutation in TenantAdministrationDetail

**Files:**
- Modify: `frontend/src/components/features/admin/TenantAdministrationDetail.tsx`

The current `deleteMutation.onSuccess` navigates to `/admin/tenants` and shows "Tenant deleted". Since deletion is now async, update it to show "queued" and navigate:

- [ ] **Step 1: Update the delete mutation handler**

Find the `deleteMutation` in `TenantAdministrationDetail.tsx` (around line 255) and replace:

```typescript
const deleteMutation = useMutation({
  mutationFn: async () => {
    await deleteTenant({ data: { tenantId: tenant.id } })
  },
  onSuccess: async () => {
    toast.success('Tenant deletion queued. You will be notified when complete.')
    setDeleteDialogOpen(false)
    setDeleteConfirmation('')
    await router.invalidate()
    await router.navigate({ to: '/admin/tenants', search: { page: 1, pageSize: 25 } })
  },
  onError: (error) => {
    toast.error(getApiErrorMessage(error, 'Failed to delete tenant'))
  },
})
```

- [ ] **Step 2: TypeScript check**

```bash
cd frontend && npm run typecheck 2>&1 | tail -10
```
Expected: no errors.

- [ ] **Step 3: Full build check**

```bash
dotnet build PatchHound.slnx -v minimal 2>&1 | tail -5
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/features/admin/TenantAdministrationDetail.tsx
git commit -m "feat(frontend): update delete mutation to handle async tenant deletion"
```
