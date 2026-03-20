# Workflow Engine – Design Plan

**Date:** 2026-03-20
**Status:** Draft – awaiting feedback

---

## 1. Overview

Replace the current "assign asset → auto-create remediation task" flow with a
configurable **workflow engine** that routes vulnerabilities (and eventually
ingestion) through a series of steps.  Each step can be automated or require
human interaction before the flow continues.

Two workflow classes:

| Class | Scope | Editable by | Examples |
|---|---|---|---|
| **System** | Global | Global admin | Ingestion pipelines, enrichment |
| **Tenant** | Per-tenant | Tenant admin | Vulnerability triage, remediation chains |

---

## 2. Core Concepts

### 2.1 Workflow Definition (the template)

A **WorkflowDefinition** is a directed graph stored as a JSON document.
It contains:

- **Nodes** — typed steps (Start, AssignGroup, SendNotification, WaitForAction,
  Condition, Merge, SystemTask, End).
- **Edges** — connections between nodes with optional labels/conditions.
- **Metadata** — name, description, version, owner scope (system | tenant),
  trigger type.

```
Start ──▶ AssignGroup(X) ──▶ WaitForAction(Review) ──▶ Condition
                                                          ├── pass ──▶ AssignGroup(Y) ──▶ WaitForAction(QA) ──▶ End
                                                          └── fail ──▶ AssignGroup(X) ──▶ WaitForAction(Rework) ──▶ ...
```

Parallel branches are modeled by multiple outgoing edges from a single node
(fork) converging at a **Merge** (join/any) node.

### 2.2 Workflow Instance (a run)

A **WorkflowInstance** is created when a workflow is triggered.  It tracks:

- Current active node(s) (supports parallelism)
- Per-node execution status + timestamps
- Context bag (asset properties, vulnerability properties, user inputs)
- Overall status: Running | WaitingForAction | Completed | Failed | Cancelled

### 2.3 Node Types

| Node Type | Behaviour | Human-in-the-loop? |
|---|---|---|
| **Start** | Entry point. Populates context with trigger payload (asset props, vuln props). | No |
| **AssignGroup** | Creates an assignment targeting a Team. Configures what the group must do (review / form / QA). | No (fires and continues to WaitForAction) |
| **WaitForAction** | Pauses the branch until the assigned action is completed by a team member. | **Yes** |
| **SendNotification** | Fire-and-forget email / SignalR push to a team or user. Does **not** pause. | No |
| **Condition** | Evaluates a predicate on context properties (asset criticality, CVSS score, custom field, action result). Routes to one of N outgoing edges. | No |
| **SystemTask** | Executes a built-in operation: run ingestion step, enrich, recalculate score, apply rule, etc. | No |
| **Merge** | Synchronisation barrier for parallel branches. Configurable: wait-all or wait-any. | No |
| **End** | Terminal node. Optionally sets a final status on the vulnerability/asset (e.g. mark resolved). | No |

### 2.4 Triggers

| Trigger | Fires when |
|---|---|
| `VulnerabilityDetected` | A new VulnerabilityAsset record is created during merge |
| `VulnerabilityReopened` | A resolved VulnerabilityAsset is reopened |
| `AssetOnboarded` | A new Asset is created during ingestion |
| `ScheduledIngestion` | Cron-based (replaces current IngestionWorker schedule) |
| `ManualRun` | User clicks "Run workflow" |

---

## 3. Data Model (Core Entities)

### WorkflowDefinition

```
WorkflowDefinition
├── Id                  : Guid
├── TenantId            : Guid?          (null = system-level)
├── Name                : string
├── Description         : string?
├── Scope               : WorkflowScope  (System | Tenant)
├── TriggerType         : WorkflowTrigger
├── Version             : int            (monotonic, immutable once published)
├── Status              : DefinitionStatus (Draft | Published | Archived)
├── GraphJson           : string         (serialised node/edge graph — see §3.1)
├── CreatedAt           : DateTimeOffset
├── UpdatedAt           : DateTimeOffset
├── CreatedBy           : Guid
```

### 3.1 GraphJson Schema

```jsonc
{
  "nodes": [
    {
      "id": "start-1",
      "type": "Start",
      "position": { "x": 0, "y": 0 },       // designer layout
      "data": {}                               // type-specific config
    },
    {
      "id": "assign-1",
      "type": "AssignGroup",
      "position": { "x": 250, "y": 0 },
      "data": {
        "teamId": "uuid",
        "requiredAction": "Review",            // Review | FillForm | QA
        "instructions": "Verify the CVSS score and add a review comment.",
        "formTemplateId": null                  // optional, for FillForm
      }
    },
    {
      "id": "wait-1",
      "type": "WaitForAction",
      "position": { "x": 500, "y": 0 },
      "data": {
        "timeoutHours": 48                     // optional SLA
      }
    },
    {
      "id": "notify-1",
      "type": "SendNotification",
      "position": { "x": 250, "y": 200 },
      "data": {
        "teamId": "uuid",
        "channel": "Email",                    // Email | InApp
        "templateKey": "vulnerability-alert"
      }
    }
    // ...
  ],
  "edges": [
    { "id": "e1", "source": "start-1", "target": "assign-1" },
    { "id": "e2", "source": "start-1", "target": "notify-1" },  // parallel
    { "id": "e3", "source": "assign-1", "target": "wait-1" },
    {
      "id": "e4",
      "source": "wait-1",
      "target": "condition-1",
      "label": "completed"                     // optional
    }
  ]
}
```

This format is **directly compatible with ReactFlow** — the `nodes` and `edges`
arrays are the same shape ReactFlow expects, with `type` and `data` driving
custom node rendering.

### WorkflowInstance

```
WorkflowInstance
├── Id                   : Guid
├── WorkflowDefinitionId : Guid
├── DefinitionVersion    : int
├── TenantId             : Guid?
├── TriggerType          : WorkflowTrigger
├── ContextJson          : string         // runtime context bag
├── Status               : InstanceStatus (Running | WaitingForAction | Completed | Failed | Cancelled)
├── StartedAt            : DateTimeOffset
├── CompletedAt          : DateTimeOffset?
├── Error                : string?
├── CreatedBy            : Guid?          // null for system triggers
```

### WorkflowNodeExecution

```
WorkflowNodeExecution
├── Id                   : Guid
├── WorkflowInstanceId   : Guid
├── NodeId               : string          // matches node.id in graph
├── NodeType             : string
├── Status               : NodeExecutionStatus (Pending | Running | WaitingForAction | Completed | Failed | Skipped)
├── InputJson            : string?         // snapshot of context at entry
├── OutputJson           : string?         // result data
├── Error                : string?
├── StartedAt            : DateTimeOffset?
├── CompletedAt          : DateTimeOffset?
├── AssignedTeamId       : Guid?           // for AssignGroup/WaitForAction
├── CompletedByUserId    : Guid?           // who completed the action
```

### WorkflowAction (human-in-the-loop task)

```
WorkflowAction
├── Id                   : Guid
├── WorkflowInstanceId   : Guid
├── NodeExecutionId      : Guid
├── TenantId             : Guid
├── TeamId               : Guid
├── ActionType           : RequiredActionType (Review | FillForm | QA)
├── Instructions         : string?
├── Status               : ActionStatus (Pending | Completed | Rejected | TimedOut)
├── ResponseJson         : string?        // review comment, form data, QA result
├── DueAt                : DateTimeOffset?
├── CompletedAt          : DateTimeOffset?
├── CompletedByUserId    : Guid?
```

---

## 4. Engine Architecture

### 4.1 WorkflowEngine (backend service)

```
IWorkflowEngine
├── StartWorkflowAsync(definitionId, triggerContext)  → WorkflowInstance
├── ResumeWorkflowAsync(instanceId)                   → void
├── CompleteActionAsync(actionId, userId, response)    → void
├── CancelWorkflowAsync(instanceId)                   → void
├── GetInstanceStatusAsync(instanceId)                 → WorkflowInstanceStatus
```

**Execution model:**

1. `StartWorkflowAsync` — creates instance, seeds context, executes from Start
   node.
2. Engine walks outgoing edges, executing each target node:
   - **Automated nodes** (Condition, SendNotification, SystemTask): execute
     inline, then continue walking.
   - **Human nodes** (WaitForAction): create `WorkflowAction` row, set node +
     instance status to `WaitingForAction`, **stop walking this branch**.
   - **Fork** (node with 2+ outgoing edges): execute each branch. Can be
     parallel (multiple active nodes).
   - **Merge**: check if required branches are complete before continuing.
3. `CompleteActionAsync` — called when a team member completes a task. Marks
   node as Completed, then calls `ResumeWorkflowAsync` to continue from that
   node.
4. If all branches reach End nodes → instance status = Completed.

### 4.2 WorkflowWorker (new BackgroundService)

A lightweight poller that:

- Picks up instances with `Status = Running` that have actionable pending nodes
  (SystemTask nodes that are queued but not yet executed).
- Handles timeouts on WaitForAction nodes (checks DueAt, transitions to
  TimedOut).
- Replaces `IngestionWorker` for system ingestion workflows (the ingestion
  schedule becomes a workflow trigger).

The existing `IngestionWorker` / `EnrichmentWorker` remain for backward
compatibility during migration, then get retired.

### 4.3 Integration With Existing Code

| Current mechanism | Replaced by |
|---|---|
| `RemediationTaskProjectionService` auto-creating tasks | `AssignGroup` + `WaitForAction` nodes |
| `AssetService.AssignTeamOwner` as the end of the line | Middle-of-workflow step; workflow continues after completion |
| `IngestionWorker.RunIngestionCycleAsync` | System workflow with `SystemTask` nodes (FetchAssets → MergeAssets → FetchVulns → MergeVulns → …) |
| Direct email notifications in merge services | `SendNotification` node |

---

## 5. Frontend: Workflow Designer

### 5.1 Technology: ReactFlow

[ReactFlow](https://reactflow.dev/) is the right choice here because:

- The graph structure maps 1:1 to the `GraphJson` schema (nodes + edges arrays).
- Custom node types for each workflow node (Start, AssignGroup, Condition, etc.)
  — each renders as a distinct card with type-specific configuration.
- Built-in drag-and-drop, connection handling, minimap, and layout.
- Well-maintained, typed, MIT-licensed.
- No compelling alternative — the other options (JointJS, Flume) are either
  heavier or less maintained.

### 5.2 Designer Components

```
frontend/src/components/features/workflows/
├── WorkflowDesigner.tsx          // ReactFlow canvas + toolbar
├── nodes/
│   ├── StartNode.tsx
│   ├── AssignGroupNode.tsx
│   ├── WaitForActionNode.tsx
│   ├── SendNotificationNode.tsx
│   ├── ConditionNode.tsx
│   ├── SystemTaskNode.tsx
│   ├── MergeNode.tsx
│   └── EndNode.tsx
├── panels/
│   ├── NodeConfigPanel.tsx        // right-side panel for selected node config
│   ├── WorkflowSettingsPanel.tsx  // name, description, trigger, scope
│   └── WorkflowRunViewer.tsx      // read-only view of a run with node statuses
├── WorkflowList.tsx               // list/manage definitions
└── WorkflowActionInbox.tsx        // team member's pending actions queue
```

### 5.3 Run Viewer (failure inspection)

When viewing a workflow run (`WorkflowInstance`), the same ReactFlow canvas
renders in **read-only mode** with each node color-coded by execution status:

| Status | Color |
|---|---|
| Completed | Green |
| Running | Blue pulse |
| WaitingForAction | Amber |
| Failed | Red |
| Skipped | Gray |
| Pending | Muted |

Clicking a node shows its `InputJson`, `OutputJson`, `Error`, timestamps, and
who completed it (for human nodes).

### 5.4 Action Inbox

Team members see a work queue of `WorkflowAction` items assigned to their
teams:

- Filter by action type (Review / Form / QA)
- Click to open the action with instructions + context
- Submit review, fill form, or approve/reject QA
- Submission calls `CompleteActionAsync` which resumes the workflow

---

## 6. API Surface

### Definitions

```
GET    /api/workflows                           — list definitions (filtered by scope + tenant)
GET    /api/workflows/{id}                      — get definition with graph
POST   /api/workflows                           — create draft definition
PUT    /api/workflows/{id}                      — update draft (graph, metadata)
POST   /api/workflows/{id}/publish              — promote draft → published (bumps version)
POST   /api/workflows/{id}/archive              — archive
DELETE /api/workflows/{id}                      — delete (draft only)
```

### Instances (runs)

```
GET    /api/workflows/{defId}/runs              — list runs for a definition
GET    /api/workflow-runs/{id}                   — get run detail with node executions
POST   /api/workflows/{defId}/run               — manual trigger
POST   /api/workflow-runs/{id}/cancel            — cancel running instance
```

### Actions (human-in-the-loop)

```
GET    /api/workflow-actions                     — list pending actions for current user's teams
GET    /api/workflow-actions/{id}                — get action detail + context
POST   /api/workflow-actions/{id}/complete        — submit response
POST   /api/workflow-actions/{id}/reject          — reject / return
```

---

## 7. Migration Strategy

### Phase 1 — Foundation (no breaking changes)

1. Add Core entities (`WorkflowDefinition`, `WorkflowInstance`,
   `WorkflowNodeExecution`, `WorkflowAction`).
2. Implement `IWorkflowEngine` with node executors for all node types.
3. Add `WorkflowWorker` BackgroundService.
4. Add API controllers.
5. Build the ReactFlow designer + run viewer (frontend).

### Phase 2 — Tenant vulnerability workflows

1. Add `VulnerabilityDetected` / `VulnerabilityReopened` trigger hooks in
   `StagedVulnerabilityMergeService`.
2. When a workflow is configured for the trigger, fire it instead of (or
   alongside) the current `RemediationTaskProjectionService`.
3. Build the Action Inbox UI.

### Phase 3 — System ingestion workflows

1. Model the current ingestion pipeline as a system workflow definition with
   `SystemTask` nodes.
2. Add system task executors that call existing services (fetch, merge, match, enrich).
3. Gradually migrate tenants from `IngestionWorker` to workflow-driven ingestion.
4. Retire `IngestionWorker` once all tenants are migrated.

---

## 8. Decisions (resolved)

1. **Condition expressions** — Simple field + operator + value rules
   (dropdown-driven UI). No expression language for now.
2. **Form templates** — Predefined schemas only. Full form builder deferred to
   a later iteration.
3. **Parallel branch policy** — Merge node uses **wait-all** (blocks until
   every incoming branch completes). Timed-out branches fail the merge.
4. **Versioning** — In-flight instances pin to `DefinitionVersion` at creation.
   Edits publish a new version; old runs continue on their pinned version.
5. **Assignment group fallback** — Current team ownership +
   `RemediationTaskProjectionService` remain as the default. Workflows are
   opt-in per trigger.
6. **Ingestion granularity** — One `SystemTask` node per checkpoint phase
   (asset-stage, asset-merge, vuln-stage, vuln-merge, software-match, enrich).
   Gives per-step visibility and retry.
