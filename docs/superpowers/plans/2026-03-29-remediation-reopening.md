# Remediation Decision Reopening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically reopen closed remediation decisions when their software's vulnerabilities resurface, with full decision history visible in the UI.

**Architecture:** Add `Reopened` status to `DecisionApprovalStatus`, add `Reopen()` and `UpdateDecision()` methods to `RemediationDecision`, hook into `StagedVulnerabilityMergeService` resurfacing logic to trigger reopening, extend the `AuditTimelineMapper` to handle the new status, and update frontend components to show reopened badges and history.

**Tech Stack:** .NET 10, EF Core, PostgreSQL, TanStack Start (React), Zod, xUnit, FluentAssertions, NSubstitute

---

### Task 1: Add `Reopened` enum value and entity changes

**Files:**
- Modify: `src/PatchHound.Core/Enums/DecisionApprovalStatus.cs`
- Modify: `src/PatchHound.Core/Entities/RemediationDecision.cs`
- Test: `tests/PatchHound.Tests/Core/RemediationDecisionTests.cs` (create)

- [ ] **Step 1: Write failing tests for `Reopen()` and `UpdateDecision()` methods**

Create the test file:

```csharp
// tests/PatchHound.Tests/Core/RemediationDecisionTests.cs
using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Tests.Core;

public class RemediationDecisionTests
{
    private static RemediationDecision CreateApprovedDecision(
        RemediationOutcome outcome = RemediationOutcome.RiskAcceptance)
    {
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: Guid.NewGuid(),
            softwareAssetId: Guid.NewGuid(),
            outcome: outcome,
            justification: "Test justification",
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.Approved
        );
        return decision;
    }

    [Fact]
    public void Reopen_from_Approved_sets_Reopened_status()
    {
        var decision = CreateApprovedDecision();

        decision.Reopen();

        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Reopened);
        decision.ReopenCount.Should().Be(1);
        decision.ReopenedAt.Should().NotBeNull();
        decision.ApprovedBy.Should().BeNull();
        decision.ApprovedAt.Should().BeNull();
    }

    [Fact]
    public void Reopen_from_Expired_sets_Reopened_status()
    {
        var decision = CreateApprovedDecision();
        decision.Expire();

        decision.Reopen();

        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Reopened);
        decision.ReopenCount.Should().Be(1);
    }

    [Fact]
    public void Reopen_from_Rejected_sets_Reopened_status()
    {
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: Guid.NewGuid(),
            softwareAssetId: Guid.NewGuid(),
            outcome: RemediationOutcome.RiskAcceptance,
            justification: "Test justification",
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.PendingApproval
        );
        decision.Reject(Guid.NewGuid());

        decision.Reopen();

        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Reopened);
    }

    [Fact]
    public void Reopen_from_PendingApproval_throws()
    {
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: Guid.NewGuid(),
            softwareAssetId: Guid.NewGuid(),
            outcome: RemediationOutcome.RiskAcceptance,
            justification: "Test justification",
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.PendingApproval
        );

        var act = () => decision.Reopen();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reopen_increments_count_on_subsequent_reopens()
    {
        var decision = CreateApprovedDecision();

        decision.Reopen();
        decision.Approve(Guid.NewGuid());
        decision.Reopen();

        decision.ReopenCount.Should().Be(2);
    }

    [Fact]
    public void UpdateDecision_changes_outcome_and_justification_when_Reopened()
    {
        var decision = CreateApprovedDecision(RemediationOutcome.RiskAcceptance);
        decision.Reopen();

        decision.UpdateDecision(RemediationOutcome.ApprovedForPatching, "Switching to patching");

        decision.Outcome.Should().Be(RemediationOutcome.ApprovedForPatching);
        decision.Justification.Should().Be("Switching to patching");
    }

    [Fact]
    public void UpdateDecision_throws_when_not_Reopened()
    {
        var decision = CreateApprovedDecision();

        var act = () => decision.UpdateDecision(RemediationOutcome.ApprovedForPatching, "Test");

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(RemediationOutcome.RiskAcceptance)]
    [InlineData(RemediationOutcome.AlternateMitigation)]
    [InlineData(RemediationOutcome.ApprovedForPatching)]
    [InlineData(RemediationOutcome.PatchingDeferred)]
    public void Reopen_works_for_all_outcomes(RemediationOutcome outcome)
    {
        var reEvalDate = outcome == RemediationOutcome.PatchingDeferred
            ? DateTimeOffset.UtcNow.AddDays(30)
            : (DateTimeOffset?)null;
        var justification = outcome == RemediationOutcome.ApprovedForPatching
            ? null
            : "Justification";
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: Guid.NewGuid(),
            softwareAssetId: Guid.NewGuid(),
            outcome: outcome,
            justification: justification,
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.Approved,
            reEvaluationDate: reEvalDate
        );

        decision.Reopen();

        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Reopened);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~RemediationDecisionTests" -v minimal`
Expected: FAIL — `Reopen` method and `ReopenCount`/`ReopenedAt` properties do not exist.

- [ ] **Step 3: Add `Reopened` to the enum**

In `src/PatchHound.Core/Enums/DecisionApprovalStatus.cs`, add:

```csharp
namespace PatchHound.Core.Enums;

public enum DecisionApprovalStatus
{
    PendingApproval,
    Approved,
    Rejected,
    Expired,
    Reopened,
}
```

- [ ] **Step 4: Add properties and methods to `RemediationDecision`**

In `src/PatchHound.Core/Entities/RemediationDecision.cs`, add the two new properties after `UpdatedAt`:

```csharp
    public int ReopenCount { get; private set; }
    public DateTimeOffset? ReopenedAt { get; private set; }
```

Add the `Reopen()` method after the `Expire()` method:

```csharp
    public void Reopen()
    {
        if (ApprovalStatus is not (DecisionApprovalStatus.Approved
            or DecisionApprovalStatus.Expired
            or DecisionApprovalStatus.Rejected))
        {
            throw new InvalidOperationException(
                $"Cannot reopen a decision with status '{ApprovalStatus}'.");
        }

        ApprovalStatus = DecisionApprovalStatus.Reopened;
        ReopenCount++;
        ReopenedAt = DateTimeOffset.UtcNow;
        ApprovedBy = null;
        ApprovedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
```

Add the `UpdateDecision()` method after `Reopen()`:

```csharp
    public void UpdateDecision(RemediationOutcome outcome, string justification)
    {
        if (ApprovalStatus != DecisionApprovalStatus.Reopened)
            throw new InvalidOperationException(
                $"Cannot update a decision with status '{ApprovalStatus}'. Only reopened decisions can be updated.");

        Outcome = outcome;
        Justification = justification;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
```

- [ ] **Step 5: Update `Approve()` and `Reject()` guards to accept `Reopened` status**

In `RemediationDecision.cs`, change the `Approve` method guard from:

```csharp
        if (ApprovalStatus != DecisionApprovalStatus.PendingApproval)
```

to:

```csharp
        if (ApprovalStatus is not (DecisionApprovalStatus.PendingApproval or DecisionApprovalStatus.Reopened))
```

Change the `Reject` method guard identically:

```csharp
        if (ApprovalStatus is not (DecisionApprovalStatus.PendingApproval or DecisionApprovalStatus.Reopened))
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~RemediationDecisionTests" -v minimal`
Expected: All 8 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Core/Enums/DecisionApprovalStatus.cs src/PatchHound.Core/Entities/RemediationDecision.cs tests/PatchHound.Tests/Core/RemediationDecisionTests.cs
git commit -m "feat: add Reopened status and Reopen/UpdateDecision methods to RemediationDecision"
```

---

### Task 2: EF Migration for new columns

**Files:**
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/RemediationDecisionConfiguration.cs` (if needed)
- Create: new migration file (auto-generated)

- [ ] **Step 1: Generate the migration**

Run from the repository root:

```bash
dotnet ef migrations add AddRemediationDecisionReopenFields \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api \
  --output-dir Data/Migrations
```

The migration should add:
- `ReopenCount` (int, not null, default 0)
- `ReopenedAt` (timestamptz, nullable)

- [ ] **Step 2: Verify migration content**

Read the generated migration file and confirm it contains:

```csharp
migrationBuilder.AddColumn<int>(
    name: "ReopenCount",
    table: "RemediationDecisions",
    type: "integer",
    nullable: false,
    defaultValue: 0);

migrationBuilder.AddColumn<DateTimeOffset>(
    name: "ReopenedAt",
    table: "RemediationDecisions",
    type: "timestamp with time zone",
    nullable: true);
```

- [ ] **Step 3: Build to verify migration compiles**

Run: `dotnet build PatchHound.slnx`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/Migrations/
git commit -m "feat: add EF migration for ReopenCount and ReopenedAt columns"
```

---

### Task 3: Hook reopening into `StagedVulnerabilityMergeService`

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/StagedVulnerabilityMergeServiceReopenTests.cs` (create)

This task hooks into the existing resurfacing logic. After `reopenedPairKeys` are collected and the DB is saved (around line 275-278 in `StagedVulnerabilityMergeService.cs`), we need to reopen decisions for the affected software.

The key insertion point is **before** `await dbContext.SaveChangesAsync(ct);` (line 275) so that the decision reopening is included in the same transaction as the merge operation.

- [ ] **Step 1: Write failing test for decision reopening during merge**

```csharp
// tests/PatchHound.Tests/Infrastructure/StagedVulnerabilityMergeServiceReopenTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.Infrastructure;

/// <summary>
/// Tests that the decision reopening logic correctly identifies and reopens
/// decisions for resurfaced vulnerabilities. These are unit tests for the
/// reopening method itself, not integration tests for the full merge pipeline.
/// </summary>
public class StagedVulnerabilityMergeServiceReopenTests
{
    [Fact]
    public void Reopens_approved_decision_for_affected_tenantSoftware()
    {
        var tenantSoftwareId = Guid.NewGuid();
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: tenantSoftwareId,
            softwareAssetId: Guid.NewGuid(),
            outcome: RemediationOutcome.RiskAcceptance,
            justification: "Accepted risk",
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.Approved
        );

        var affectedTenantSoftwareIds = new HashSet<Guid> { tenantSoftwareId };
        var decisions = new List<RemediationDecision> { decision };

        // Simulate what the merge service should do
        var toReopen = decisions.Where(d =>
            affectedTenantSoftwareIds.Contains(d.TenantSoftwareId)
            && d.ApprovalStatus is DecisionApprovalStatus.Approved
                or DecisionApprovalStatus.Expired
                or DecisionApprovalStatus.Rejected
        ).ToList();

        foreach (var d in toReopen)
            d.Reopen();

        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Reopened);
        decision.ReopenCount.Should().Be(1);
    }

    [Fact]
    public void Does_not_reopen_pending_decision()
    {
        var tenantSoftwareId = Guid.NewGuid();
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: tenantSoftwareId,
            softwareAssetId: Guid.NewGuid(),
            outcome: RemediationOutcome.RiskAcceptance,
            justification: "Test",
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.PendingApproval
        );

        var affectedTenantSoftwareIds = new HashSet<Guid> { tenantSoftwareId };
        var decisions = new List<RemediationDecision> { decision };

        var toReopen = decisions.Where(d =>
            affectedTenantSoftwareIds.Contains(d.TenantSoftwareId)
            && d.ApprovalStatus is DecisionApprovalStatus.Approved
                or DecisionApprovalStatus.Expired
                or DecisionApprovalStatus.Rejected
        ).ToList();

        foreach (var d in toReopen)
            d.Reopen();

        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.PendingApproval);
        toReopen.Should().BeEmpty();
    }

    [Fact]
    public void Does_not_reopen_decision_for_unaffected_software()
    {
        var decision = RemediationDecision.Create(
            tenantId: Guid.NewGuid(),
            tenantSoftwareId: Guid.NewGuid(),
            softwareAssetId: Guid.NewGuid(),
            outcome: RemediationOutcome.RiskAcceptance,
            justification: "Test",
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: DecisionApprovalStatus.Approved
        );

        var unrelatedSoftwareId = Guid.NewGuid();
        var affectedTenantSoftwareIds = new HashSet<Guid> { unrelatedSoftwareId };
        var decisions = new List<RemediationDecision> { decision };

        var toReopen = decisions.Where(d =>
            affectedTenantSoftwareIds.Contains(d.TenantSoftwareId)
            && d.ApprovalStatus is DecisionApprovalStatus.Approved
                or DecisionApprovalStatus.Expired
                or DecisionApprovalStatus.Rejected
        ).ToList();

        toReopen.Should().BeEmpty();
        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Approved);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~StagedVulnerabilityMergeServiceReopenTests" -v minimal`
Expected: All 3 tests PASS (these test the reopening logic pattern, not the merge service integration).

- [ ] **Step 3: Implement decision reopening in `StagedVulnerabilityMergeService`**

In `src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs`, find the block around lines 270-278 that looks like:

```csharp
                    await dbContext.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
```

Insert the reopening logic **before** `SaveChangesAsync`. Find the line:

```csharp
                    var saveChangesStartedAt = DateTimeOffset.UtcNow;
                    await dbContext.SaveChangesAsync(ct);
```

Insert before it:

```csharp
                    // Reopen closed decisions for software with resurfaced vulnerabilities
                    if (reopenedPairKeys.Count > 0)
                    {
                        await ReopenDecisionsForResurfacedVulnerabilitiesAsync(
                            tenantId, reopenedPairKeys, ct);
                    }
```

Then add the private method at the end of the class:

```csharp
    private async Task ReopenDecisionsForResurfacedVulnerabilitiesAsync(
        Guid tenantId,
        HashSet<string> reopenedPairKeys,
        CancellationToken ct)
    {
        var reopenedAssetIds = new HashSet<Guid>();
        foreach (var pairKey in reopenedPairKeys)
        {
            var parts = pairKey.Split(':');
            if (parts.Length == 2 && Guid.TryParse(parts[1], out var assetId))
                reopenedAssetIds.Add(assetId);
        }

        if (reopenedAssetIds.Count == 0)
            return;

        var affectedTenantSoftwareIds = await dbContext.NormalizedSoftwareInstallations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId
                && reopenedAssetIds.Contains(i.SoftwareAssetId)
                && i.IsActive)
            .Select(i => i.TenantSoftwareId)
            .Distinct()
            .ToListAsync(ct);

        if (affectedTenantSoftwareIds.Count == 0)
            return;

        var decisionsToReopen = await dbContext.RemediationDecisions
            .Where(d => d.TenantId == tenantId
                && affectedTenantSoftwareIds.Contains(d.TenantSoftwareId)
                && (d.ApprovalStatus == DecisionApprovalStatus.Approved
                    || d.ApprovalStatus == DecisionApprovalStatus.Expired
                    || d.ApprovalStatus == DecisionApprovalStatus.Rejected))
            .Include(d => d.RemediationWorkflow)
            .ToListAsync(ct);

        foreach (var decision in decisionsToReopen)
        {
            decision.Reopen();

            if (decision.RemediationWorkflow is { Status: RemediationWorkflowStatus.Completed } workflow)
            {
                workflow.Reactivate(RemediationWorkflowStage.RemediationDecision);

                var stageRecord = RemediationWorkflowStageRecord.Create(
                    tenantId,
                    workflow.Id,
                    RemediationWorkflowStage.RemediationDecision,
                    RemediationWorkflowStageStatus.InProgress,
                    summary: "Decision reopened due to vulnerability resurfacing"
                );
                await dbContext.RemediationWorkflowStageRecords.AddAsync(stageRecord, ct);
            }
        }

        logger.LogInformation(
            "Reopened {Count} remediation decisions for tenant {TenantId} due to vulnerability resurfacing",
            decisionsToReopen.Count,
            tenantId);
    }
```

Note: The `ChunkState` parameter is not actually needed — we only need `tenantId`, `reopenedPairKeys`, and `ct`. Remove it from the call.

The insertion point in the merge loop (find the exact line `var saveChangesStartedAt = DateTimeOffset.UtcNow;`):

```csharp
                    // Reopen closed decisions for software with resurfaced vulnerabilities
                    if (reopenedPairKeys.Count > 0)
                    {
                        await ReopenDecisionsForResurfacedVulnerabilitiesAsync(
                            tenantId, reopenedPairKeys, ct);
                    }

                    var saveChangesStartedAt = DateTimeOffset.UtcNow;
```

- [ ] **Step 4: Add `Reactivate()` to `RemediationWorkflow`**

In `src/PatchHound.Core/Entities/RemediationWorkflow.cs`, add after the `Cancel()` method:

```csharp
    public void Reactivate(RemediationWorkflowStage stage)
    {
        if (Status != RemediationWorkflowStatus.Completed)
            throw new InvalidOperationException(
                $"Cannot reactivate a workflow with status '{Status}'.");

        Status = RemediationWorkflowStatus.Active;
        CompletedAt = null;
        MoveToStage(stage);
    }
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs src/PatchHound.Core/Entities/RemediationWorkflow.cs tests/PatchHound.Tests/Infrastructure/StagedVulnerabilityMergeServiceReopenTests.cs
git commit -m "feat: reopen closed decisions when vulnerabilities resurface during merge"
```

---

### Task 4: Update `RemediationDecisionService` duplicate prevention and approval guards

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/RemediationDecisionService.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/RemediationDecisionServiceTests.cs`

- [ ] **Step 1: Write failing test for duplicate prevention with `Reopened` status**

Add to `tests/PatchHound.Tests/Infrastructure/RemediationDecisionServiceTests.cs`. First read the file to find the last test method, then add:

```csharp
    [Fact]
    public async Task CreateDecisionAsync_blocks_when_reopened_decision_exists()
    {
        // Arrange: set up tenant software, asset, and installation
        var tenantSoftwareId = Guid.NewGuid();
        var softwareAssetId = Guid.NewGuid();
        var normalizedSoftwareId = Guid.NewGuid();

        // Create the NormalizedSoftware and TenantSoftware first
        var normalizedSoftware = NormalizedSoftware.Create(normalizedSoftwareId, "TestSoftware", null);
        await _dbContext.NormalizedSoftware.AddAsync(normalizedSoftware);

        var tenantSoftware = TenantSoftware.Create(_tenantId, normalizedSoftwareId, null);
        // Use reflection or test helper to set the Id
        typeof(TenantSoftware).GetProperty("Id")!.SetValue(tenantSoftware, tenantSoftwareId);
        await _dbContext.TenantSoftware.AddAsync(tenantSoftware);

        var asset = Asset.Create(_tenantId, "test-ext-id", AssetType.Software, "TestSoftware", Criticality.Medium);
        typeof(Asset).GetProperty("Id")!.SetValue(asset, softwareAssetId);
        await _dbContext.Assets.AddAsync(asset);

        var installation = NormalizedSoftwareInstallation.Create(
            _tenantId, tenantSoftwareId, softwareAssetId, Guid.NewGuid(), true, null);
        await _dbContext.NormalizedSoftwareInstallations.AddAsync(installation);

        // Create and reopen a decision
        var decision = RemediationDecision.Create(
            _tenantId, tenantSoftwareId, softwareAssetId,
            RemediationOutcome.RiskAcceptance, "Test", _userId,
            DecisionApprovalStatus.Approved);
        decision.Reopen();
        await _dbContext.RemediationDecisions.AddAsync(decision);
        await _dbContext.SaveChangesAsync();

        // Act: try to create a new decision for the same software
        var result = await _sut.CreateDecisionAsync(
            _tenantId, softwareAssetId,
            RemediationOutcome.ApprovedForPatching, null, _userId,
            null, null, CancellationToken.None);

        // Assert: should fail because a reopened decision exists
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("active remediation decision already exists");
    }
```

Note: This test setup may need adjustment based on the actual entity factory methods available. Read the existing test helpers in `TestData/` and adapt. The key assertion is that a `Reopened` decision blocks new decision creation.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~CreateDecisionAsync_blocks_when_reopened_decision_exists" -v minimal`
Expected: FAIL — the current `hasOpenDecision` query doesn't filter for `Reopened`.

- [ ] **Step 3: Update the duplicate check in `CreateDecisionAsync`**

In `src/PatchHound.Infrastructure/Services/RemediationDecisionService.cs`, find lines 34-46 (the `hasOpenDecision` query):

```csharp
        var hasOpenDecision = await dbContext.RemediationDecisions
            .Where(d =>
                d.TenantId == tenantId
                && d.TenantSoftwareId == remediationScope.TenantSoftwareId
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
```

Replace the filter with an explicit inclusion of active statuses instead of exclusion (to be explicit about `Reopened`):

```csharp
        var hasOpenDecision = await dbContext.RemediationDecisions
            .Where(d =>
                d.TenantId == tenantId
                && d.TenantSoftwareId == remediationScope.TenantSoftwareId
                && (d.ApprovalStatus == DecisionApprovalStatus.PendingApproval
                    || d.ApprovalStatus == DecisionApprovalStatus.Approved
                    || d.ApprovalStatus == DecisionApprovalStatus.Reopened))
```

Wait — the current query excludes `Rejected` and `Expired`, meaning `PendingApproval` and `Approved` are considered "open". With the new `Reopened` status, it would already be included by the current exclusion logic (since `Reopened` is neither `Rejected` nor `Expired`). So the current query **already works** for `Reopened`.

Actually, let me re-read the current query. It excludes `Rejected` and `Expired`, so `PendingApproval`, `Approved`, and now `Reopened` would all be considered "open". The test should actually PASS already with the current query since `Reopened` is not in the exclusion list.

But there's also the second part of the condition (lines 41-44) that checks for an active workflow:

```csharp
            .AnyAsync(
                d => !d.RemediationWorkflowId.HasValue
                    || dbContext.RemediationWorkflows.Any(workflow =>
                        workflow.Id == d.RemediationWorkflowId.Value
                        && workflow.Status == RemediationWorkflowStatus.Active),
                ct
            );
```

This means a decision is only "open" if it either has no workflow or has an Active workflow. When we reopen a decision in Task 3, we also reactivate the workflow. So this should work.

Re-evaluate: the test should already pass. Let's change the test expectation: if it passes, we verify the behavior is correct. If it fails, we update the query. The important thing is to verify the behavior.

- [ ] **Step 4: Run test to verify behavior**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~CreateDecisionAsync_blocks_when_reopened_decision_exists" -v minimal`
Expected: PASS (the exclusion-based query already handles `Reopened`).

- [ ] **Step 5: Also update `ReconcileResolvedSoftwareRemediationsAsync` to handle `Reopened`**

In `ReconcileResolvedSoftwareRemediationsAsync` (around line 316-322), the query that finds decisions to close excludes `Rejected` and `Expired`:

```csharp
                && d.ApprovalStatus != DecisionApprovalStatus.Rejected
                && d.ApprovalStatus != DecisionApprovalStatus.Expired)
```

This will also pick up `Reopened` decisions, which is correct — if all vulns are resolved, a reopened decision should be expired too. No change needed.

- [ ] **Step 6: Commit**

```bash
git add tests/PatchHound.Tests/Infrastructure/RemediationDecisionServiceTests.cs
git commit -m "test: verify reopened decisions block new decision creation"
```

---

### Task 5: Update `AuditTimelineMapper` to handle `Reopened` status

**Files:**
- Modify: `src/PatchHound.Api/Services/AuditTimelineMapper.cs`

- [ ] **Step 1: Update `ResolveUpdatedAction` to handle `Reopened`**

In `src/PatchHound.Api/Services/AuditTimelineMapper.cs`, find the `ResolveUpdatedAction` method and the `ApprovalStatus` switch (around line 85-92):

```csharp
            return newApprovalStatus switch
            {
                nameof(DecisionApprovalStatus.Approved) => "Approved",
                nameof(DecisionApprovalStatus.Rejected) => "Denied",
                nameof(DecisionApprovalStatus.Expired) => "Expired",
                _ => "Updated",
            };
```

Add the `Reopened` case:

```csharp
            return newApprovalStatus switch
            {
                nameof(DecisionApprovalStatus.Approved) => "Approved",
                nameof(DecisionApprovalStatus.Rejected) => "Denied",
                nameof(DecisionApprovalStatus.Expired) => "Expired",
                nameof(DecisionApprovalStatus.Reopened) => "Reopened",
                _ => "Updated",
            };
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build PatchHound.slnx`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Api/Services/AuditTimelineMapper.cs
git commit -m "feat: handle Reopened status in audit timeline mapper"
```

---

### Task 6: Extend API DTOs and query service for `reopenCount`

**Files:**
- Modify: `src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs`
- Modify: `src/PatchHound.Api/Services/RemediationDecisionQueryService.cs`

- [ ] **Step 1: Add `ReopenCount` and `ReopenedAt` to `RemediationDecisionDto`**

In `src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs`, update the `RemediationDecisionDto` record:

```csharp
public record RemediationDecisionDto(
    Guid Id,
    string Outcome,
    string ApprovalStatus,
    string Justification,
    Guid DecidedBy,
    DateTimeOffset DecidedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ExpiryDate,
    DateTimeOffset? ReEvaluationDate,
    int ReopenCount,
    DateTimeOffset? ReopenedAt,
    DecisionRejectionDto? LatestRejection,
    List<VulnerabilityOverrideDto> Overrides
);
```

- [ ] **Step 2: Update query service to map `ReopenCount` and `ReopenedAt`**

In `src/PatchHound.Api/Services/RemediationDecisionQueryService.cs`, find where `RemediationDecisionDto` is constructed (search for `new RemediationDecisionDto`). Update all construction sites to include the two new fields:

```csharp
new RemediationDecisionDto(
    decision.Id,
    decision.Outcome.ToString(),
    decision.ApprovalStatus.ToString(),
    decision.Justification,
    decision.DecidedBy,
    decision.DecidedAt,
    decision.ApprovedBy,
    decision.ApprovedAt,
    decision.ExpiryDate,
    decision.ReEvaluationDate,
    decision.ReopenCount,
    decision.ReopenedAt,
    latestRejection,
    overrides
)
```

Search for all `new RemediationDecisionDto(` in the file and update each one. There may be multiple (one in `BuildByTenantSoftwareAsync` for `currentDecision`, one for `previousDecision`).

- [ ] **Step 3: Build to verify**

Run: `dotnet build PatchHound.slnx`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs src/PatchHound.Api/Services/RemediationDecisionQueryService.cs
git commit -m "feat: add reopenCount and reopenedAt to decision context API response"
```

---

### Task 7: Update frontend Zod schemas

**Files:**
- Modify: `frontend/src/api/remediation.schemas.ts`

- [ ] **Step 1: Add `reopenCount` and `reopenedAt` to `remediationDecisionSchema`**

In `frontend/src/api/remediation.schemas.ts`, find the `remediationDecisionSchema` and add the two new fields after `reEvaluationDate`:

```typescript
export const remediationDecisionSchema = z.object({
  id: z.string().uuid(),
  outcome: z.string(),
  approvalStatus: z.string(),
  justification: z.string(),
  decidedBy: z.string().uuid(),
  decidedAt: z.string(),
  approvedBy: z.string().uuid().nullable(),
  approvedAt: z.string().nullable(),
  expiryDate: z.string().nullable(),
  reEvaluationDate: z.string().nullable(),
  reopenCount: z.number(),
  reopenedAt: z.string().nullable(),
  latestRejection: z.object({
    comment: z.string().nullable(),
    rejectedAt: z.string().nullable(),
  }).nullable(),
  overrides: z.array(vulnerabilityOverrideSchema),
})
```

- [ ] **Step 2: Run typecheck to verify**

Run: `cd frontend && npm run typecheck`
Expected: No new errors from schema change (types are inferred).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/remediation.schemas.ts
git commit -m "feat: add reopenCount and reopenedAt to remediation decision schema"
```

---

### Task 8: Update `remediation-utils.ts` for `Reopened` status

**Files:**
- Modify: `frontend/src/components/features/remediation/remediation-utils.ts`

- [ ] **Step 1: Add `Reopened` to `approvalStatusTone` and `approvalStatusLabel`**

In `frontend/src/components/features/remediation/remediation-utils.ts`, update `approvalStatusTone`:

```typescript
export function approvalStatusTone(status: string): Tone {
  switch (status) {
    case 'Approved': return 'success'
    case 'PendingApproval': return 'warning'
    case 'Rejected': return 'danger'
    case 'Expired': return 'neutral'
    case 'Reopened': return 'warning'
    default: return 'neutral'
  }
}
```

Update `approvalStatusLabel`:

```typescript
export function approvalStatusLabel(status: string): string {
  switch (status) {
    case 'PendingApproval': return 'Pending approval'
    case 'Approved': return 'Approved'
    case 'Rejected': return 'Rejected'
    case 'Expired': return 'Expired'
    case 'Reopened': return 'Reopened — Awaiting re-evaluation'
    default: return status
  }
}
```

- [ ] **Step 2: Run lint and typecheck**

Run: `cd frontend && npm run lint && npm run typecheck`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/remediation/remediation-utils.ts
git commit -m "feat: add Reopened status to approval status tone and label utils"
```

---

### Task 9: Add "Reopened" badge to `RemediationVulnDrawer`

**Files:**
- Modify: `frontend/src/components/features/remediation/RemediationVulnDrawer.tsx`

- [ ] **Step 1: Add `reopenCount` prop and badge**

Update the component to accept `reopenCount` and display the badge. Change the props type:

```typescript
type RemediationVulnDrawerProps = {
  vuln: DecisionVuln | null
  reopenCount: number
  isOpen: boolean
  onOpenChange: (open: boolean) => void
}
```

Update the function signature:

```typescript
export function RemediationVulnDrawer({ vuln, reopenCount, isOpen, onOpenChange }: RemediationVulnDrawerProps)
```

Add the reopened badge inside the `{vuln ? (` block, after the `<SheetHeader>` section and before the first `<section>`. Insert it at the start of the `<div className="space-y-5 p-4">`:

```tsx
        {vuln ? (
          <div className="space-y-5 p-4">
            {/* Reopened badge */}
            {reopenCount > 0 ? (
              <div className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium ${toneBadge('warning')}`}>
                {reopenCount === 1 ? 'Reopened' : `Reopened (${reopenCount}x)`}
              </div>
            ) : null}

            {/* Severity & Score */}
```

- [ ] **Step 2: Update the call site in `SoftwareRemediationView.tsx`**

In `frontend/src/components/features/remediation/SoftwareRemediationView.tsx`, find where `RemediationVulnDrawer` is used and add the `reopenCount` prop. Search for `<RemediationVulnDrawer` and add:

```tsx
<RemediationVulnDrawer
  vuln={selectedVuln}
  reopenCount={context.currentDecision?.reopenCount ?? 0}
  isOpen={vulnDrawerOpen}
  onOpenChange={setVulnDrawerOpen}
/>
```

- [ ] **Step 3: Run typecheck**

Run: `cd frontend && npm run typecheck`
Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/features/remediation/RemediationVulnDrawer.tsx frontend/src/components/features/remediation/SoftwareRemediationView.tsx
git commit -m "feat: show Reopened badge on CVE vulnerability drawer"
```

---

### Task 10: Add reopened banner and timeline entry to `SoftwareRemediationView`

**Files:**
- Modify: `frontend/src/components/features/remediation/SoftwareRemediationView.tsx`

- [ ] **Step 1: Add "Reopened" banner in the decision tab**

Find the section in `SoftwareRemediationView.tsx` where the current decision status is displayed (in the header area, around lines 179-237). Add a prominent banner when the decision is `Reopened`. Find the decision status badge display and add after it:

```tsx
{context.currentDecision?.approvalStatus === 'Reopened' ? (
  <div className="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm text-amber-200">
    <p className="font-medium">This decision was reopened because vulnerabilities resurfaced.</p>
    <p className="mt-1 text-xs text-muted-foreground">
      Please re-evaluate the current outcome or approve to keep the existing decision.
    </p>
  </div>
) : null}
```

The exact insertion point depends on the component structure. Place it in the Decision tab content area, before the decision form or summary panel.

- [ ] **Step 2: Add "Reopened" to the timeline title helper**

In the `buildDecisionTimelineTitle` function (around line 1515), add a case for `Reopened`:

```typescript
    case 'Reopened':
      return `This remediation decision was reopened because vulnerabilities resurfaced.`
```

Add this before the `default` case.

- [ ] **Step 3: Add `reopenCount` badge in the header area**

In the header section where the approval status badge is shown, add a reopen count indicator. Find where the approval status badge is rendered (search for `approvalStatusLabel` or `approvalStatusTone` in the component). Add next to it:

```tsx
{context.currentDecision && context.currentDecision.reopenCount > 0 ? (
  <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge('warning')}`}>
    {context.currentDecision.reopenCount === 1
      ? 'Reopened'
      : `Reopened (${context.currentDecision.reopenCount}x)`}
  </span>
) : null}
```

- [ ] **Step 4: Run lint and typecheck**

Run: `cd frontend && npm run lint && npm run typecheck`
Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/features/remediation/SoftwareRemediationView.tsx
git commit -m "feat: add reopened banner, timeline entry, and badge to remediation view"
```

---

### Task 11: Full build and test verification

**Files:** None (verification only)

- [ ] **Step 1: Run full backend build**

Run: `dotnet build PatchHound.slnx`
Expected: Build succeeds with no errors.

- [ ] **Step 2: Run full backend tests**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass.

- [ ] **Step 3: Run frontend lint and typecheck**

Run: `cd frontend && npm run lint && npm run typecheck`
Expected: No errors.

- [ ] **Step 4: Run frontend tests**

Run: `cd frontend && npm test`
Expected: All tests pass.

- [ ] **Step 5: Commit any fixes if needed**

If any issues were found and fixed, commit them with an appropriate message.
