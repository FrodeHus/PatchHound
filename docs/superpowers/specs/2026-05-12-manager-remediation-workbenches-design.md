# Manager Remediation Workbenches Design

## Goal

Add dedicated Security Manager and Technical Manager workbenches for remediation approval tasks, and make the workflow handoff explicit after the asset owner records a remediation decision.

## Existing Context

PatchHound already models the post-decision approval step with `RemediationWorkflowApprovalMode`, `ApprovalTask`, `ApprovalTaskVisibleRole`, and role-aware authorization in `RemediationWorkflowAuthorizationService`.

The existing routing behavior is the product rule for this feature:

- `RiskAcceptance`, `AlternateMitigation`, and `PatchingDeferred` require Security Manager approval.
- `ApprovedForPatching` requires Technical Manager approval.
- `GlobalAdmin` can act on either approval path.

The existing `/api/approval-tasks` API, `ApprovalInbox`, and `ApprovalTaskDetail` components already list, display, and resolve approval tasks. The new workbenches should reuse that approval lifecycle instead of adding a parallel task model.

## User Experience

### Security Manager Workbench

Route: `/workbenches/security-manager`

The Security Manager workbench is a focused queue for exception-style decisions:

- risk acceptance approvals
- alternate mitigation approvals
- patching deferred approvals

It should default to pending work and prioritize expiry pressure. The screen should present Security Manager-specific copy, metrics for pending exception approvals and expiring tasks, and filters for software search, approval status, and approval type.

Each item links to a Security Manager task detail route:

Route: `/workbenches/security-manager/tasks/$id`

The detail view should reuse the existing approval task detail surface and resolution behavior. The page copy should frame the action as governance approval of a remediation posture, with justification required when the task requires it.

### Technical Manager Workbench

Route: `/workbenches/technical-manager`

The Technical Manager workbench is a focused queue for patch execution readiness:

- approved-for-patching decisions waiting for technical approval

It should default to pending `PatchingApproved` work. The screen should present Technical Manager-specific copy, metrics for pending patch approvals, missing maintenance windows, and overdue or expiring tasks, and filters for software search and approval status.

Each item links to a Technical Manager task detail route:

Route: `/workbenches/technical-manager/tasks/$id`

The detail view should reuse the existing approval task detail surface and resolution behavior. The page copy should frame the action as approval to move into execution. When approving an `ApprovedForPatching` decision, the existing maintenance window requirement remains in force.

## Workflow Design

The canonical workflow routing remains in `RemediationWorkflowService.DetermineApprovalMode`:

- Security approval for `RiskAcceptance`, `AlternateMitigation`, and `PatchingDeferred`.
- Technical approval for `ApprovedForPatching`.

When `RemediationDecisionService.CreateDecisionForCaseAsync` creates a decision, the workflow service attaches the decision to the active workflow, moves it into `Approval` when the selected outcome requires approval, and opens a stage record assigned to the expected role.

When the approval task is approved:

- `ApprovedForPatching` advances to `Execution` and patching tasks are created.
- `RiskAcceptance`, `AlternateMitigation`, and `PatchingDeferred` skip execution and move to `Closure`.

When denied:

- the workflow returns to `RemediationDecision` assigned to the software owner team.

## Architecture

Use one configurable approval workbench component rather than copying `ApprovalInbox`.

Create a role workbench configuration that controls:

- heading, explanatory copy, and metric labels
- default API filters
- allowed approval type filter options
- task detail link target

Add routes for the two manager workbench list pages and task detail pages. The routes should call the existing `fetchApprovalTasks`, `fetchApprovalTaskDetail`, `resolveApprovalTask`, and `markApprovalTaskRead` server functions.

The existing generic `/approvals` inbox remains available.

## Testing

Backend tests should prove the workflow routing and visible roles:

- risk acceptance routes to pending Security Manager approval
- alternate mitigation routes to pending Security Manager approval
- patching deferred routes to pending Security Manager approval
- approved-for-patching routes to pending Technical Manager approval and does not create execution tasks until approved

Frontend tests should cover the new workbench behavior:

- Security Manager workbench applies the expected default filters and links to `/workbenches/security-manager/tasks/$id`
- Technical Manager workbench applies the expected default filters and links to `/workbenches/technical-manager/tasks/$id`
- role-specific workbench copy and metrics render for the configured role

## Out of Scope

- New backend task entities.
- New approval resolution semantics.
- Changing dashboard data contracts unless route links need to point to the new workbenches.
- Role provisioning or tenant membership changes.

## Self-Review

- No placeholders remain.
- Routing explicitly matches the user's clarified rule: patching deferred stays with Security Manager.
- The design reuses existing approval task APIs and authorization instead of duplicating workflow state.
- Testing covers both backend workflow routing and frontend workbench wiring.
