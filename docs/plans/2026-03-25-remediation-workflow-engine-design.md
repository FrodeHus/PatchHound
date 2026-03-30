# Remediation Workflow Engine Design

This document defines the next remediation model for PatchHound: a persisted workflow with explicit stage ownership, completion, and gating.

It replaces the current inferred workflow approach, where the UI derives stage state from a combination of:

- analyst recommendations
- remediation decisions
- approval tasks
- patching tasks
- unresolved exposure

The goal is to make remediation a true workflow with:

- one current stage
- explicit stage owner context
- explicit completion records
- role-gated progression
- read-only visibility for users who are not allowed to act on the current stage

## Intent

- Human:
  - security analyst
  - security manager
  - technical manager
  - software owner team member
  - device owner team member
- Job:
  - review exposure
  - recommend a course of action
  - decide how the software should be handled
  - approve when required
  - execute patching on affected devices
  - track closure
- Feel:
  - operational
  - explicit
  - role-aware
  - chronological

## Core Model

`Exposure` should not be a workflow stage.

It is shared context that stays visible throughout the remediation lifecycle. Every role should see the same evidence base.

The persisted workflow stages should be:

1. `SecurityAnalysis`
2. `RemediationDecision`
3. `Approval`
4. `Execution`
5. `Closure`

## Ownership Model

Two different ownership layers are required:

### Software Owner Team

- one owner team per tenant software
- fallback team: `Infrastructure`
- owns the `RemediationDecision` stage
- any user in that team may act
- first valid submission wins

### Device Owner Team

- one owner team per device
- fallback team: `Infrastructure`
- owns execution for patching tasks
- may be different from the software owner team

This split is important:

- software owner team decides posture for the software
- device owner teams execute patching on the affected devices

## Stage Rules

### 1. SecurityAnalysis

Allowed roles:

- `GlobalAdmin`
- `SecurityManager`
- `SecurityAnalyst`

Requires:

- software identity
- vulnerability exposure
- affected devices
- known exploit / alert signals

Outputs:

- recommendation:
  - `ApprovedForPatching`
  - `RiskAcceptance`
  - `AlternateMitigation`
  - `PatchingDeferred`
- priority:
  - `Emergency`
  - `Normal`
- rationale

Completion rule:

- stage is completed when a recommendation is submitted

### 2. RemediationDecision

Allowed actors:

- any member of the software owner team
- `GlobalAdmin`

Requires:

- exposure context
- latest analyst recommendation

Outputs:

- remediation decision:
  - `ApprovedForPatching`
  - `RiskAcceptance`
  - `AlternateMitigation`
  - `PatchingDeferred`
- justification
- optional expiry date
- optional re-evaluation date

Completion rule:

- stage is completed when the decision is submitted

### 3. Approval

This stage is conditional.

#### RiskAcceptance / AlternateMitigation

Allowed roles:

- `GlobalAdmin`
- `SecurityManager`

Outputs:

- `Approved`
- `Denied`

#### ApprovedForPatching

Allowed roles:

- `GlobalAdmin`
- `TechnicalManager`

Outputs:

- `Approved`
- patch window:
  - `NextAvailable`
  - or explicit date

#### PatchingDeferred

Rules:

- `Normal` priority: auto-approved
- `Emergency` priority: requires approval by:
  - `GlobalAdmin`
  - `TechnicalManager`

Outputs:

- `Approved`
- `Denied`

Completion rule:

- stage completes when the relevant approval result is recorded
- if the branch auto-approves, the system completes it automatically

### 4. Execution

Only applies to approved `ApprovedForPatching`.

Allowed actors:

- device owner team members through patching task updates
- `TechnicalManager` for oversight
- `GlobalAdmin`

Inputs:

- approved patch decision
- patch window
- affected devices grouped by device owner team

Outputs:

- patching tasks created per device owner team
- task progress
- task completion

Completion rule:

- stage completes when all relevant patching tasks are completed
- this alone is not enough for closure; unresolved vulnerability exposure must also be gone

### 5. Closure

This is system-driven.

For approved patching:

- complete when:
  - all workflow-linked patching tasks are completed
  - and the tenant software has zero unresolved vulnerabilities left

For approved `RiskAcceptance` / `AlternateMitigation`:

- these remain stable end states in `RemediationDecision`
- they do not transition into `Closure` yet

For approved `PatchingDeferred`:

- remains in `RemediationDecision`
- waits for re-evaluation rather than closing

## New Persisted Entities

### RemediationWorkflow

Purpose:

- one workflow per active remediation lifecycle for a tenant software

Suggested fields:

- `Id`
- `TenantId`
- `TenantSoftwareId`
- `SoftwareOwnerTeamId`
- `CurrentStage`
- `Status`
- `CurrentStageStartedAt`
- `CompletedAt`
- `CancelledAt`
- `CreatedAt`
- `UpdatedAt`

Suggested enums:

- `RemediationWorkflowStage`
  - `SecurityAnalysis`
  - `RemediationDecision`
  - `Approval`
  - `Execution`
  - `Closure`
- `RemediationWorkflowStatus`
  - `Active`
  - `Completed`
  - `Cancelled`

Rules:

- at most one active workflow per `TenantSoftwareId`

### RemediationWorkflowStageRecord

Purpose:

- immutable log of stage ownership and completion

Suggested fields:

- `Id`
- `RemediationWorkflowId`
- `Stage`
- `Status`
- `StartedAt`
- `CompletedAt`
- `CompletedByUserId`
- `AssignedRole`
- `AssignedTeamId`
- `SystemCompleted`
- `Summary`

Suggested stage record status:

- `Pending`
- `InProgress`
- `Completed`
- `Skipped`
- `AutoCompleted`

This gives the UI and timeline a single authoritative source.

### Recommendation Priority

The workflow needs persisted priority.

Options:

1. add `Priority` to `AnalystRecommendation`
2. copy the chosen recommendation priority onto `RemediationWorkflow`

Recommended:

- copy onto `RemediationWorkflow`
- keep the recommendation as historical input

That avoids stage progression depending on a mutable recommendation row.

### Approval Routing Snapshot

The workflow should persist the approval branch chosen at decision time.

Suggested fields on `RemediationWorkflow`:

- `ProposedOutcome`
- `Priority`
- `ApprovalMode`

Example approval modes:

- `None`
- `SecurityApproval`
- `TechnicalApproval`
- `TechnicalAutoApproved`

This prevents the UI from re-deriving approval logic later from partially changed data.

## Relationship to Existing Entities

The workflow should orchestrate the existing domain objects instead of replacing them immediately.

### Keep

- `AnalystRecommendation`
- `RemediationDecision`
- `ApprovalTask`
- `PatchingTask`

### Add linkage

Add `RemediationWorkflowId` to:

- `AnalystRecommendation`
- `RemediationDecision`
- `ApprovalTask`
- `PatchingTask`

This lets one workflow own the full chain.

### Why this is better than deriving stage from current state

- explicit current stage
- explicit owner
- explicit completion
- stable history
- simpler access control
- cleaner UI

## Access Control Model

The remediation page should expose:

- shared exposure information to all relevant viewers
- editability only for the current stage and only to the correct actors

### Shared Read Access

Users with any of these relationships should see the remediation in read-only mode:

- security roles
- software owner team members
- device owner team members for affected devices
- `GlobalAdmin`
- `TechnicalManager`

### Editable Current Stage Access

Only users who match the current stage actor model may complete that stage.

Examples:

- current stage `SecurityAnalysis`
  - editable for security roles only
- current stage `RemediationDecision`
  - editable for software owner team members and `GlobalAdmin`
- current stage `Approval`
  - editable for the stage’s routed approval role set only
- current stage `Execution`
  - editable through task actions for the relevant device owner teams

The UI should not infer this itself. The API should return:

- `CurrentStage`
- `CanActOnCurrentStage`
- `CurrentStageActorSummary`

## Progression Rules

The workflow engine should move the remediation forward explicitly.

### SecurityAnalysis -> RemediationDecision

- when a recommendation is created

### RemediationDecision -> Approval

- when a decision is submitted
- unless the branch auto-approves and has no approval wait state

### Approval -> Execution

- only for approved `ApprovedForPatching`

### Approval -> RemediationDecision

- when approval is denied
- this reopens the decision stage

### Approval -> RemediationDecision (steady state)

- for approved:
  - `RiskAcceptance`
  - `AlternateMitigation`
  - `PatchingDeferred`

These remain stable on `RemediationDecision` rather than moving to `Closure`.

### Execution -> Closure

- when all linked patching tasks are complete
- and exposure is resolved

## API Shape

Add explicit workflow endpoints under tenant software remediation:

- `GET /api/software/{tenantSoftwareId}/remediation/workflow`
- `POST /api/software/{tenantSoftwareId}/remediation/workflow/security-analysis`
- `POST /api/software/{tenantSoftwareId}/remediation/workflow/decision`
- `POST /api/software/{tenantSoftwareId}/remediation/workflow/approval`
- `POST /api/software/{tenantSoftwareId}/remediation/workflow/execution/reassign`

The current remediation decision endpoints can remain temporarily, but the new UI should pivot to the workflow endpoints.

Response should include:

- workflow metadata
- current stage
- current stage status
- current stage actor summary
- `canActOnCurrentStage`
- linked decision / approval / execution summaries
- exposure summary

## UI Model

The current stage rail can remain informational, but it should now be driven by real workflow state.

### Shared layout

- top header
- stage rail
- shared exposure workspace
- current stage action panel
- history

### Shared Exposure Workspace

Visible in every stage:

- summary metrics
- vulnerabilities
- affected devices
- version cohorts
- device owner team scope

### Current Stage Action Panel

Changes by stage:

- `SecurityAnalysis`
  - recommendation form
- `RemediationDecision`
  - decision form
- `Approval`
  - approval action block
- `Execution`
  - grouped patching task execution state
- `Closure`
  - system-complete summary

If `CanActOnCurrentStage == false`:

- show the stage panel in read-only mode
- show a clear banner:
  - `Waiting for Security Manager approval`
  - `Waiting for software owner team decision`
  - `Waiting for device owner teams to complete patching`

## Migration Strategy

### Phase 1

- add `RemediationWorkflow`
- add `RemediationWorkflowStageRecord`
- add `RemediationWorkflowId` nullable foreign keys to existing remediation entities
- populate new workflows for active tenant-software remediation records

### Phase 2

- update services so:
  - recommendation creation writes stage completion
  - decision creation writes stage transition
  - approval writes stage transition
  - patching completion reconciliation writes execution / closure transition

### Phase 3

- add workflow-aware query DTOs and endpoints
- update remediation UI to consume persisted workflow state

### Phase 4

- retire the inferred-stage logic in `SoftwareRemediationView`
- move all role-gating decisions to backend-derived workflow permissions

## Legacy Cleanup Requirement

This change should actively remove legacy inferred workflow behavior as the new model lands.

Do not leave two competing remediation flow systems in place longer than the migration window.

### Remove or retire on the way

- inferred current-stage calculation in the frontend
- UI editability rules derived from:
  - current decision state
  - approval task presence
  - patching task counts
- duplicated workflow summary fields that only exist to help the UI infer stage progression
- legacy remediation endpoints that bypass workflow ownership or progression rules
- any approval / decision branching logic that is only encoded in the UI

### Keep only as compatibility shims during rollout

- existing decision and approval entities
- existing query endpoints needed by older views

These should either:

- be linked to `RemediationWorkflow`
- or be removed once the remediation UI is fully migrated

### Definition of done

The remediation feature is only complete when:

- the current stage comes from persisted workflow state
- the acting role comes from persisted workflow state
- the UI no longer computes stage progression itself
- old remediation paths cannot create state that bypasses workflow gates

## Suggested Initial Implementation Order

1. add workflow entities and migration
2. link new workflows to:
   - recommendations
   - decisions
   - approval tasks
   - patching tasks
3. update `RemediationDecisionService` to create and advance workflow records
4. update `ApprovalTaskService` to complete / reopen workflow stages
5. update patching-task completion reconciliation to advance `Execution` and `Closure`
6. expose workflow DTOs from the remediation query layer
7. update the remediation UI to:
   - show shared exposure context always
   - render current stage panel in edit or read-only mode based on backend workflow permissions

## Recommendation

Do this as an explicit workflow engine layered on top of the existing remediation entities.

Do not keep inferring stage progression in the UI.

The current inferred model was enough to prototype the remediation experience, but it is the wrong foundation for:

- gated stage progression
- role-based stage ownership
- read-only non-owner views
- future reassignment
- clean audit and stage history
