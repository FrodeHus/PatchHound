# Remediation Workflow UI Design

This document proposes a clearer remediation workflow interface based on the implemented flow in [REMEDIATION_FLOW.md](/Users/frode.hus/src/github.com/frodehus/PatchHound/REMEDIATION_FLOW.md).

The goal is to make remediation feel like a guided operational workflow instead of a long software detail page with sections.

## Intent

- Human:
  - analyst
  - security manager / approver
  - technical manager
  - owner team lead
- Job:
  - understand the software exposure
  - capture analyst guidance
  - make a remediation decision
  - get approval if required
  - execute patching if approved
  - confirm closure
- Feel:
  - operational
  - chronological
  - compact
  - high-signal

## Product Domain

Relevant domain concepts:

- software-wide exposure
- recommendation
- remediation decision
- approval gate
- owner-team execution
- closure
- audit trail
- SLA pressure

Color world:

- neutral console surfaces for evidence
- muted blue for analyst input / informational steps
- amber for pending review
- green for approved / on-track / completed
- red only for denied / overdue / blocked

Signature:

- a visible remediation stage rail that tracks the software from `Exposure` to `Closure`

Defaults to avoid:

- generic stacked cards with no stage progression
- burying approval and execution as status pills
- putting the primary workflow behind tabs

## Recommended Information Architecture

### 1. Header

Keep the current compact header direction, but make it explicitly workflow-oriented.

Contents:

- software name
- criticality
- risk band
- active decision badge if present
- approval status badge if present
- compact SLA pill
- one-line operational summary:
  - `1,867 open vulnerabilities · 9 known exploits · 3 owner teams affected · SLA on track`

This should remain concise and not repeat detail cards below.

### 2. Workflow Stage Rail

Directly under the header, add a horizontal stage rail:

1. `Exposure`
2. `Recommendation`
3. `Decision`
4. `Approval`
5. `Execution`
6. `Closure`

Each stage should have one of:

- `Complete`
- `Current`
- `Pending`
- `Skipped`
- `Closed`

This is the core interaction upgrade. Users should understand the software’s current remediation state without reading the whole page.

### 3. Active Stage Panel

Under the stage rail, show one primary workflow panel that changes emphasis based on the current stage.

Examples:

#### Exposure

- summary metrics
- top vulnerabilities
- affected devices / version cohorts
- analyst prompt:
  - `Review and recommend next action`

#### Recommendation

- analyst recommendations list
- recommendation form
- recommended outcome badges with rationale
- CTA:
  - `Create remediation decision`

#### Decision

- explicit outcome choices as cards:
  - `Patch this software`
  - `Accept the current risk`
  - `Use alternate mitigation`
  - `Defer patching`
- each choice explains:
  - requires approval?
  - creates patching tasks?
  - needs expiry?
  - needs re-evaluation?

#### Approval

- approval task summary
- approver audience
- expiry timer
- resolve actions if current user can act
- if auto-approved:
  - show a completed informational state instead of a generic task card

#### Execution

- generated patching tasks grouped by owner team
- due date
- affected devices count
- high/critical device pressure
- CTA:
  - `Open remediation workbench`

#### Closure

- completion state
- explicit system closure note
- completed patching tasks count
- closure timestamp

## Tabs for Supporting Material

Use tabs only for supporting evidence and history.

Recommended tabs:

- `Overview`
- `Vulnerabilities`
- `Devices`
- `History`

Rules:

- keep the workflow stage rail and active stage panel outside tabs
- keep `History` and raw evidence inside tabs

This preserves the main remediation flow while reducing page length.

## Timeline Design

Use one unified timeline component for the whole remediation narrative.

Timeline event types:

- analyst recommendation added
- decision created
- decision approved
- decision denied
- decision expired / cancelled
- patching tasks created
- system auto-closed remediation

The timeline should answer:

- how did we get here?
- what happened next?
- who acted?

## Screen-Level Layout

Recommended page structure:

```text
[Compact software header]
[Remediation stage rail]

[Active stage panel]

[Tabs]
- Overview
- Vulnerabilities
- Devices
- History
```

### Overview Tab

Purpose:
- the shortest path to understanding and acting

Contents:
- grouped summary cards:
  - Exposure
  - Threat activity
  - Owner-team scope
- analyst recommendations
- AI analysis
- current decision snapshot

### Vulnerabilities Tab

Purpose:
- evidence for why the remediation exists

Contents:
- top vulnerabilities table
- override visibility
- risk/threat indicators

### Devices Tab

Purpose:
- execution scope

Contents:
- affected owner teams
- affected device counts
- version cohorts
- device list

### History Tab

Purpose:
- timeline / audit

Contents:
- unified remediation history component

## Current Implementation vs Recommended UX

### What already aligns

The current implementation already has the right domain objects:

- software-scoped remediation
- analyst recommendations
- remediation decisions
- approval tasks
- patching tasks
- auto-close reconciliation
- unified history events for recommendations and decisions

The current remediation screen in [SoftwareRemediationView.tsx](/Users/frode.hus/src/github.com/frodehus/PatchHound/frontend/src/components/features/remediation/SoftwareRemediationView.tsx) also already improved several things:

- compact header
- stronger decision card
- tabs for supporting content
- cleaner timeline

### Main UX gaps

#### 1. The workflow is still implicit

Today:
- users infer stage from cards and statuses

Needed:
- explicit stage rail showing current step and what comes next

#### 2. Approval is still treated as metadata

Today:
- approval is visible as a status and action set

Needed:
- approval should feel like a dedicated workflow stage

#### 3. Execution handoff is weak

Today:
- patching tasks exist, but the remediation page does not strongly show the transition from approved decision to owner-team execution

Needed:
- visible execution stage with owner-team task summary

#### 4. Closure is backend-led, not UI-led

Today:
- auto-close happens, but it is not a strong completion state in the interface

Needed:
- explicit closure stage and celebratory completion language

#### 5. Supporting tabs are present, but the page still lacks a strong “current stage” panel

Today:
- the page is shorter, but still reads as a detail screen

Needed:
- one dominant workflow panel tied to the current stage

## Recommended Implementation Order

### Phase 1

- add remediation stage rail
- compute current stage from decision / approval / patching state
- add owner-team affected count to decision context

### Phase 2

- add active stage panel above tabs
- move current decision / approval / execution summary into that panel

### Phase 3

- add dedicated `Devices` tab with owner-team execution scope
- add closure state copy and success presentation

## Suggested Stage Mapping Logic

Use the following approximation:

- no decision yet:
  - if recommendations exist -> `Recommendation`
  - else -> `Exposure`
- decision exists and pending approval -> `Approval`
- decision exists and outcome is `ApprovedForPatching` and patching tasks open -> `Execution`
- decision exists and outcome is `PatchingDeferred` -> `Decision`
- decision exists and outcome is `RiskAcceptance` or `AlternateMitigation` and approved -> `Decision`
- no unresolved vulnerabilities left and patching remediation auto-closed -> `Closure`

## Summary

The current implementation has the correct workflow primitives, but the UI still presents them as sections of a page rather than steps in a process.

The most important UX improvement is:

- add a visible remediation stage rail
- add a single active stage panel
- keep evidence and history behind supporting tabs

That will make remediation feel intuitive from creation through recommendation, decision, approval, execution, and closure.
