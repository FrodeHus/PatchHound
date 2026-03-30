# Remediation Decision Reopening on Vulnerability Resurfacing — Design Spec

## Overview

When vulnerabilities resurface for software that has a closed remediation decision, automatically reopen that decision so the team can re-evaluate. Maintain a full history of all prior decisions and approvals, surfaced both on the remediation detail page and as a badge on CVE cards.

## Current Behavior

When vulnerabilities resurface (detected in `StagedVulnerabilityMergeService`):
- `VulnerabilityAsset.Reopen()` is called, episode tracking updates
- `reopenedPairKeys` tracks which (vuln, asset) pairs resurfaced
- No action is taken on existing `RemediationDecision` entities — they stay closed
- `WorkflowTrigger.VulnerabilityReopened` exists but has no implemented workflow

## Design

### Data Model Changes

#### DecisionApprovalStatus Enum

Add `Reopened = 4`:

```csharp
public enum DecisionApprovalStatus
{
    PendingApproval,  // Awaiting approval
    Approved,         // Decision approved
    Rejected,         // Decision rejected
    Expired,          // Decision expired
    Reopened,         // Decision reopened due to vulnerability resurfacing
}
```

#### RemediationDecision Entity

Add two properties:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| ReopenCount | int | 0 | Number of times this decision has been reopened |
| ReopenedAt | DateTimeOffset? | null | Timestamp of last reopen |

Add `Reopen()` method:

```csharp
public void Reopen()
{
    if (ApprovalStatus is not (DecisionApprovalStatus.Approved
        or DecisionApprovalStatus.Expired
        or DecisionApprovalStatus.Rejected))
    {
        throw new InvalidOperationException(
            $"Cannot reopen a decision with status '{ApprovalStatus}'");
    }

    ApprovalStatus = DecisionApprovalStatus.Reopened;
    ReopenCount++;
    ReopenedAt = DateTimeOffset.UtcNow;
    ApprovedBy = null;
    ApprovedAt = null;
}
```

Clearing `ApprovedBy`/`ApprovedAt` signals that re-approval is needed. The audit trail preserves who previously approved.

#### EF Migration

One migration adding `ReopenCount` (int, default 0) and `ReopenedAt` (DateTimeOffset?, nullable) columns to the `RemediationDecisions` table.

### Backend Logic

#### StagedVulnerabilityMergeService — Resurfacing Hook

After the existing resurfacing logic processes `reopenedPairKeys`, add a new step:

1. Collect distinct `TenantSoftwareId`s from the reopened vulnerability-asset pairs
2. Query `RemediationDecision` rows where:
   - `TenantSoftwareId` is in the collected set
   - `ApprovalStatus` is `Approved`, `Expired`, or `Rejected`
3. For each matching decision:
   - Call `decision.Reopen()`
   - If the decision has a linked `RemediationWorkflow` that is `Completed`:
     - Set workflow status back to `Active`
     - Move workflow to `RemediationDecision` stage
     - Create a new `RemediationWorkflowStageRecord` for the reopened stage

This runs within the same DB transaction as the merge operation.

#### RemediationDecisionService — Duplicate Prevention

Update `CreateDecisionAsync` to treat `Reopened` as an active status. The existing check for "open decisions" should include `Reopened` alongside `PendingApproval`:

```csharp
// Existing: prevents new decision when one is pending
// Updated: also prevents new decision when one is reopened
var hasActiveDecision = await _dbContext.RemediationDecisions
    .AnyAsync(d => d.TenantSoftwareId == tenantSoftwareId
        && (d.ApprovalStatus == DecisionApprovalStatus.PendingApproval
            || d.ApprovalStatus == DecisionApprovalStatus.Reopened), ct);
```

#### RemediationDecisionService — Re-evaluation Flow

When a reopened decision is re-evaluated, the existing `Approve`/`Reject` methods handle the transitions:
- `Approve()` — moves from `Reopened` to `Approved` (update guard to allow `Reopened` status)
- `Reject()` — moves from `Reopened` to `Rejected` (update guard to allow `Reopened` status)

The team can also change the outcome before re-approving (e.g., change from RiskAcceptance to ApprovedForPatching). Add an `UpdateDecision(RemediationOutcome outcome, string justification)` method on `RemediationDecision` that is only valid when status is `Reopened`. This updates `Outcome`, `Justification`, and resets approval requirements based on the new outcome. The existing approval flow then handles re-approval.

### Applies to All Outcomes

Reopening applies equally to all `RemediationOutcome` values:
- **RiskAcceptance** — risk acceptance needs re-evaluation
- **AlternateMitigation** — mitigation may have failed
- **ApprovedForPatching** — patch didn't hold, needs investigation
- **PatchingDeferred** — was deferred, now resurfaced

### API Changes

#### DecisionContext Response Extension

Extend `DecisionContextDto` (returned by `GetDecisionContext`):

| Field | Type | Description |
|-------|------|-------------|
| reopenCount | int | Number of times the current decision was reopened |
| decisionHistory | DecisionHistoryEntry[] | Audit trail of all decision state transitions |

`DecisionHistoryEntry`:

| Field | Type | Description |
|-------|------|-------------|
| action | string | AuditAction (Created, Approved, Reopened, etc.) |
| userId | string | User who performed the action |
| userName | string | Display name of the user |
| timestamp | string | ISO 8601 timestamp |
| outcome | string? | RemediationOutcome at the time |
| approvalStatus | string? | DecisionApprovalStatus after the action |
| justification | string? | Justification text (from NewValues) |

Built from existing `AuditLogEntry` records where `EntityType == "RemediationDecision"` and `EntityId` matches the decision. The `AuditSaveChangesInterceptor` already captures old/new values on every state change, so no additional audit logging is needed.

No new endpoints. The existing `GetDecisionContext` endpoint is extended.

### Frontend Changes

#### Zod Schema Updates (`remediation.schemas.ts`)

Extend `remediationDecisionSchema`:
```typescript
reopenCount: z.number(),
reopenedAt: z.string().nullable(),
```

Add `decisionHistoryEntrySchema`:
```typescript
const decisionHistoryEntrySchema = z.object({
  action: z.string(),
  userId: z.string().uuid(),
  userName: z.string(),
  timestamp: z.string(),
  outcome: z.string().nullable(),
  approvalStatus: z.string().nullable(),
  justification: z.string().nullable(),
});
```

Extend `decisionContextSchema`:
```typescript
decisionHistory: z.array(decisionHistoryEntrySchema),
```

#### SoftwareRemediationView.tsx — Decision History Section

Add a collapsible "Decision History" section in the Decision tab:

- Timeline layout showing each state transition chronologically
- Each entry: action icon, action label, user name, timestamp, outcome badge (if changed), justification snippet
- Entries derived from `decisionHistory` in the context response
- Collapsed by default, expanded when user clicks — keeps the page clean for simple cases

When the decision is in `Reopened` status, show a prominent banner:
> "This decision was reopened because vulnerabilities resurfaced. Please re-evaluate the current outcome."

With action buttons to approve (keep current outcome) or modify the decision.

#### RemediationVulnDrawer.tsx — Reopened Badge on CVE Cards

When the parent decision has `reopenCount > 0`, show a badge next to the CVE severity:

- Badge text: "Reopened" (if count is 1) or "Reopened (Nx)" (if count > 1)
- Badge color: amber/warning tone
- Tooltip: "This vulnerability resurfaced after a previous remediation decision was closed"

#### Outcome Badge Update

Update the outcome badge component to handle `Reopened` approval status:
- Color: amber/warning
- Label: "Reopened — Awaiting Re-evaluation"

## Decisions

- **Reopen in-place, not snapshot chain.** A single `RemediationDecision` entity accumulates state changes. History comes from `AuditLogEntry` records, which the interceptor already captures. No new tables.
- **All outcomes reopen equally.** No special handling per `RemediationOutcome` — every closed decision reopens when its software's vulnerabilities resurface.
- **Workflow moves back to RemediationDecision stage.** Skips Verification and SecurityAnalysis — the team already knows this software and just needs to re-evaluate the decision.
- **Approve/Reject guards updated.** Both methods accept `Reopened` status as a valid source state, enabling the existing approval flow to handle re-evaluation.
- **No new endpoints.** The existing `GetDecisionContext` endpoint is extended with history data.
- **Badge on CVE cards and history on detail page.** Both surfaces requested by user for full visibility.
