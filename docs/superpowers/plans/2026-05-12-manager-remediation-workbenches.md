# Manager Remediation Workbenches Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Security Manager and Technical Manager remediation approval workbenches wired to the existing workflow and approval task lifecycle.

**Architecture:** Reuse the existing approval task API, query functions, and detail resolver. Add a configurable frontend approval workbench component, role-specific routes, and focused tests; keep backend workflow routing in `RemediationWorkflowService` and prove it with infrastructure tests.

**Tech Stack:** ASP.NET Core, EF Core InMemory test provider, xunit, FluentAssertions, React 19, TanStack Router/Start, TanStack Query, Vitest, Testing Library.

---

## File Structure

- Modify `tests/PatchHound.Tests/Infrastructure/RemediationDecisionServiceTests.cs`: add backend tests for approval mode, visible roles, and patching task creation delay.
- Create `frontend/src/components/features/approvals/ApprovalWorkbench.tsx`: role-configurable approval workbench list component.
- Create `frontend/src/components/features/approvals/approval-workbench-config.ts`: Security Manager and Technical Manager copy, filters, type options, metrics, and link targets.
- Create `frontend/src/components/features/approvals/ApprovalWorkbench.test.tsx`: component tests for role copy, type options, metrics, and links.
- Modify `frontend/src/components/features/approvals/ApprovalInbox.tsx`: preserve generic inbox behavior by wrapping `ApprovalWorkbench` with generic configuration.
- Create `frontend/src/routes/_authed/workbenches/security-manager/index.tsx`: Security Manager workbench route using `fetchApprovalTasks`.
- Create `frontend/src/routes/_authed/workbenches/technical-manager/index.tsx`: Technical Manager workbench route using `fetchApprovalTasks`.
- Create `frontend/src/routes/_authed/workbenches/security-manager/tasks.$id.tsx`: Security Manager approval detail route using `fetchApprovalTaskDetail` and `ApprovalTaskDetail`.
- Create `frontend/src/routes/_authed/workbenches/technical-manager/tasks.$id.tsx`: Technical Manager approval detail route using `fetchApprovalTaskDetail` and `ApprovalTaskDetail`.
- Modify `frontend/src/components/features/dashboard/SecurityManagerOverview.tsx`: link pending approvals to `/workbenches/security-manager`.
- Modify `frontend/src/components/features/dashboard/TechnicalManagerOverview.tsx`: link pending approvals to `/workbenches/technical-manager`.

## Task 1: Backend Routing Tests

**Files:**
- Modify: `tests/PatchHound.Tests/Infrastructure/RemediationDecisionServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests after `CreateDecisionForCaseAsync_PatchingDeferredRoutesToSecurityApproval`:

```csharp
[Theory]
[InlineData(RemediationOutcome.RiskAcceptance)]
[InlineData(RemediationOutcome.AlternateMitigation)]
public async Task CreateDecisionForCaseAsync_ExceptionOutcomesRouteToSecurityManager(RemediationOutcome outcome)
{
    var remediationCase = await SeedCaseAsync();

    var result = await _service.CreateDecisionForCaseAsync(
        _tenantId,
        remediationCase.Id,
        outcome,
        "The asset owner supplied a documented exception.",
        _userId,
        expiryDate: outcome == RemediationOutcome.RiskAcceptance ? DateTimeOffset.UtcNow.AddDays(30) : null,
        reEvaluationDate: null,
        CancellationToken.None,
        deadlineMode: outcome == RemediationOutcome.RiskAcceptance ? RemediationDecisionDeadlineMode.Date : null
    );

    result.IsSuccess.Should().BeTrue();
    result.Value.ApprovalStatus.Should().Be(DecisionApprovalStatus.PendingApproval);

    var workflow = await _dbContext.RemediationWorkflows.SingleAsync();
    workflow.CurrentStage.Should().Be(RemediationWorkflowStage.Approval);
    workflow.ApprovalMode.Should().Be(RemediationWorkflowApprovalMode.SecurityApproval);

    var task = await _dbContext.ApprovalTasks.Include(item => item.VisibleRoles).SingleAsync();
    task.Status.Should().Be(ApprovalTaskStatus.Pending);
    task.VisibleToRoles.Should().BeEquivalentTo([RoleName.GlobalAdmin, RoleName.SecurityManager]);
}

[Fact]
public async Task CreateDecisionForCaseAsync_ApprovedForPatchingRoutesToTechnicalManagerAndWaitsForApprovalBeforeExecution()
{
    var remediationCase = await SeedCaseAsync();

    var result = await _service.CreateDecisionForCaseAsync(
        _tenantId,
        remediationCase.Id,
        RemediationOutcome.ApprovedForPatching,
        "Patch in the next maintenance window.",
        _userId,
        expiryDate: null,
        reEvaluationDate: null,
        CancellationToken.None
    );

    result.IsSuccess.Should().BeTrue();
    result.Value.ApprovalStatus.Should().Be(DecisionApprovalStatus.PendingApproval);

    var workflow = await _dbContext.RemediationWorkflows.SingleAsync();
    workflow.CurrentStage.Should().Be(RemediationWorkflowStage.Approval);
    workflow.ApprovalMode.Should().Be(RemediationWorkflowApprovalMode.TechnicalApproval);

    var task = await _dbContext.ApprovalTasks.Include(item => item.VisibleRoles).SingleAsync();
    task.Status.Should().Be(ApprovalTaskStatus.Pending);
    task.Type.Should().Be(ApprovalTaskType.PatchingApproved);
    task.VisibleToRoles.Should().BeEquivalentTo([RoleName.GlobalAdmin, RoleName.TechnicalManager]);
    _dbContext.PatchingTasks.Should().BeEmpty();
}
```

- [ ] **Step 2: Run tests to verify the target behavior**

Run: `dotnet test --filter "FullyQualifiedName~RemediationDecisionServiceTests"`

Expected: tests should pass if current backend routing already implements the clarified rule; if a test fails, update only the minimal workflow/task routing code needed to match the spec.

- [ ] **Step 3: If needed, implement minimal backend changes**

Expected backend routing code should remain:

```csharp
private static RemediationWorkflowApprovalMode DetermineApprovalMode(
    RemediationOutcome outcome,
    RemediationWorkflowPriority priority
) =>
    outcome switch
    {
        RemediationOutcome.RiskAcceptance
            or RemediationOutcome.AlternateMitigation
            or RemediationOutcome.PatchingDeferred =>
            RemediationWorkflowApprovalMode.SecurityApproval,
        RemediationOutcome.ApprovedForPatching =>
            RemediationWorkflowApprovalMode.TechnicalApproval,
        _ => RemediationWorkflowApprovalMode.None,
    };
```

- [ ] **Step 4: Re-run backend tests**

Run: `dotnet test --filter "FullyQualifiedName~RemediationDecisionServiceTests"`

Expected: all `RemediationDecisionServiceTests` pass.

## Task 2: Configurable Approval Workbench Component

**Files:**
- Create: `frontend/src/components/features/approvals/approval-workbench-config.ts`
- Create: `frontend/src/components/features/approvals/ApprovalWorkbench.tsx`
- Modify: `frontend/src/components/features/approvals/ApprovalInbox.tsx`
- Test: `frontend/src/components/features/approvals/ApprovalWorkbench.test.tsx`

- [ ] **Step 1: Write failing component tests**

Create `ApprovalWorkbench.test.tsx` with tests that render Security Manager and Technical Manager configurations, assert role copy, assert metric counts, and assert detail links use the configured route.

- [ ] **Step 2: Run frontend test to verify it fails**

Run: `cd frontend && npm test -- ApprovalWorkbench.test.tsx`

Expected: FAIL because `ApprovalWorkbench` and config files do not exist.

- [ ] **Step 3: Implement workbench config**

Create configs for generic approvals, Security Manager, and Technical Manager. Security Manager type options are `RiskAcceptanceApproval` and `PatchingDeferred`; Technical Manager type option is `PatchingApproved`.

- [ ] **Step 4: Implement `ApprovalWorkbench`**

Move the table/filter implementation from `ApprovalInbox` into `ApprovalWorkbench`, parameterized by config. Preserve the existing mark-read behavior, pagination, filters, and `ApprovalExpiryCountdown`.

- [ ] **Step 5: Keep `ApprovalInbox` as generic wrapper**

Update `ApprovalInbox` to call `ApprovalWorkbench` with generic approval inbox config and the same props.

- [ ] **Step 6: Run component tests**

Run: `cd frontend && npm test -- ApprovalWorkbench.test.tsx`

Expected: PASS.

## Task 3: Manager Workbench Routes

**Files:**
- Create: `frontend/src/routes/_authed/workbenches/security-manager/index.tsx`
- Create: `frontend/src/routes/_authed/workbenches/technical-manager/index.tsx`
- Create: `frontend/src/routes/_authed/workbenches/security-manager/tasks.$id.tsx`
- Create: `frontend/src/routes/_authed/workbenches/technical-manager/tasks.$id.tsx`

- [ ] **Step 1: Add list routes**

Security Manager route should default missing search state to:

```ts
{
  page: 1,
  pageSize: 25,
  status: 'Pending',
  type: '',
  search: '',
  showRead: false,
}
```

When calling `fetchApprovalTasks`, if `type` is empty use the backend list with no type filter so both security-manager task types can appear. The visible UI type filter should only offer `RiskAcceptanceApproval` and `PatchingDeferred`.

Technical Manager route should default missing search state to:

```ts
{
  page: 1,
  pageSize: 25,
  status: 'Pending',
  type: 'PatchingApproved',
  search: '',
  showRead: false,
}
```

- [ ] **Step 2: Add detail routes**

Both detail routes should mirror `/approvals/$id`, but navigate back to their role workbench after resolve/read.

- [ ] **Step 3: Run typecheck**

Run: `cd frontend && npm run typecheck`

Expected: PASS.

## Task 4: Dashboard Links

**Files:**
- Modify: `frontend/src/components/features/dashboard/SecurityManagerOverview.tsx`
- Modify: `frontend/src/components/features/dashboard/TechnicalManagerOverview.tsx`

- [ ] **Step 1: Update links**

Change pending approval links:

```tsx
<Link to="/workbenches/security-manager" search={{ page: 1, pageSize: 25, status: 'Pending', type: '', search: '', showRead: false }} />
```

and:

```tsx
<Link to="/workbenches/technical-manager" search={{ page: 1, pageSize: 25, status: 'Pending', type: 'PatchingApproved', search: '', showRead: false }} />
```

- [ ] **Step 2: Run dashboard tests if they exist**

Run: `cd frontend && npm test -- SecurityManagerOverview.test.tsx TechnicalManagerOverview.test.tsx`

Expected: PASS.

## Task 5: Final Verification

**Files:**
- All changed files.

- [ ] **Step 1: Run targeted backend tests**

Run: `dotnet test --filter "FullyQualifiedName~RemediationDecisionServiceTests"`

Expected: PASS.

- [ ] **Step 2: Run targeted frontend tests**

Run: `cd frontend && npm test -- ApprovalWorkbench.test.tsx SecurityManagerOverview.test.tsx TechnicalManagerOverview.test.tsx`

Expected: PASS.

- [ ] **Step 3: Run frontend typecheck**

Run: `cd frontend && npm run typecheck`

Expected: PASS.

- [ ] **Step 4: Run GitNexus change detection**

Run: `gitnexus_detect_changes({scope: "all"})`

Expected: changed symbols and affected flows match manager workbench routing and approval workbench UI.

## Self-Review

- Every requirement in the design spec maps to a task.
- No new task entities or approval APIs are introduced.
- The plan preserves the clarified Security Manager routing for patching deferred decisions.
- Backend tests cover workflow routing; frontend tests cover workbench configuration and links.
