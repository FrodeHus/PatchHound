# ApprovalTaskDetail Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure ApprovalTaskDetail.tsx from single-column stacked layout to a two-column layout with context sidebar cards matching the design mockup.

**Architecture:** Single-file refactor of `ApprovalTaskDetail.tsx`. The component's JSX is reorganized into a CSS grid (`lg:grid-cols-[1fr_320px]`) with the main content on the left and three sidebar cards on the right. A recharts `RadialBarChart` is added for the risk gauge. No new files, no schema changes.

**Tech Stack:** React, Tailwind CSS, recharts (RadialBarChart/RadialBar/PolarAngleAxis), lucide-react icons

---

## File Map

- **Modify:** `frontend/src/components/features/approvals/ApprovalTaskDetail.tsx` — all layout changes happen here

---

### Task 1: Restructure to Two-Column Grid Layout

**Files:**
- Modify: `frontend/src/components/features/approvals/ApprovalTaskDetail.tsx`

This task replaces the outer `<section className="space-y-5">` wrapper with a two-column grid and moves existing content into the left column. The right column is left empty (populated in later tasks). No functionality changes.

- [ ] **Step 1: Update the outer wrapper and add column containers**

Replace the outermost element (line 122):

```tsx
// OLD:
<section className="space-y-5">
  <header className="rounded-[28px] ...">...</header>
  <section className="rounded-[28px] ...">...tabs...</section>
</section>

// NEW:
<div className="grid gap-5 lg:grid-cols-[1fr_320px]">
  {/* Left column */}
  <section className="min-w-0 space-y-5">
    <header className="rounded-[28px] ...">...</header>
    {/* Reviewer verdict card will go here (Task 3) */}
    <section className="rounded-[28px] ...">...tabs...</section>
  </section>

  {/* Right column — sidebar */}
  <aside className="space-y-5">
    {/* Sidebar cards will go here (Tasks 4, 5, 6) */}
  </aside>
</div>
```

The `<header>` and tabs `<section>` move inside the left column `<section>` unchanged.

- [ ] **Step 2: Verify the app renders correctly**

Run: `cd frontend && npm run dev`

Open the approval detail page. The layout should look identical to before (single column on small screens), with an empty right margin on large screens. All existing functionality (tabs, approve/deny, pagination) must still work.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/approvals/ApprovalTaskDetail.tsx
git commit -m "refactor: wrap ApprovalTaskDetail in two-column grid layout"
```

---

### Task 2: Simplify the Header

**Files:**
- Modify: `frontend/src/components/features/approvals/ApprovalTaskDetail.tsx`

Simplify the header: change title format, move badges/expiry inline, remove the metadata sidebar box and the "Requested decision" sub-section (that data moves to sidebar cards in later tasks).

- [ ] **Step 1: Update the header content**

Inside the `<header>` element, replace its children with this simplified structure:

```tsx
<header className="rounded-[28px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_8%,transparent),transparent_48%),var(--color-card)] p-5">
  <div className="space-y-3">
    <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
      Approval task
    </p>
    <div className="flex flex-wrap items-center gap-3">
      <h1 className="text-3xl font-semibold tracking-[-0.04em]">
        Remediation Approval: {startCase(data.softwareName)}
      </h1>
      {!isPending && !data.readAt ? (
        <Button
          variant="ghost"
          size="icon"
          onClick={onMarkRead}
          title="Mark as read"
          aria-label="Mark as read"
          className="size-8 rounded-full border border-border/70"
        >
          <Eye className="size-4" />
        </Button>
      ) : null}
    </div>
    <div className="flex flex-wrap items-center gap-2">
      <span
        className={`inline-flex rounded-full border px-2.5 py-1 text-[11px] font-medium ${toneBadge(severityTone(data.criticality))}`}
      >
        Severity: {data.criticality}
      </span>
      <ApprovalStatusBadge status={data.status} />
      {isPending ? (
        <ApprovalExpiryCountdown expiresAt={data.expiresAt} compact />
      ) : null}
    </div>
  </div>
</header>
```

Key changes from the current header:
- Title now prefixed with "Remediation Approval: "
- The subtitle paragraph ("Review the owner decision...") is removed
- `ApprovalTypeBadge` removed (not in the mockup)
- Severity badge text changed to "Severity: {value}" format
- Expiry countdown rendered inline as `compact` variant
- The metadata sidebar box (`min-w-[220px] rounded-2xl...`) is removed entirely
- The "Requested decision" sub-section (`border-primary/15`) is removed entirely
- The entire approve/deny `isPending` section is removed from the header (moves to Task 3)

- [ ] **Step 2: Verify the header renders correctly**

Run: `cd frontend && npm run dev`

The header should show: label, title with "Remediation Approval:" prefix, badges row with severity + status + expiry inline. The approve/deny controls are temporarily missing (added back in Task 3).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/approvals/ApprovalTaskDetail.tsx
git commit -m "refactor: simplify approval header with inline badges and expiry"
```

---

### Task 3: Extract Reviewer Verdict as Standalone Card

**Files:**
- Modify: `frontend/src/components/features/approvals/ApprovalTaskDetail.tsx`

Add the Reviewer Verdict card between the header and tabs in the left column. This contains the approval justification editor and approve/deny buttons, only shown when `isPending`.

- [ ] **Step 1: Add new icon imports**

Update the lucide-react import at the top of the file:

```tsx
import { CheckCircle, XCircle, Eye, AlertTriangle, MessageSquare, ShieldCheck } from 'lucide-react'
```

- [ ] **Step 2: Add the Reviewer Verdict card JSX**

In the left column, between `</header>` and the tabs `<section>`, add:

```tsx
{isPending ? (
  <section className="rounded-2xl border border-border/70 bg-card p-5">
    <div className="flex items-start justify-between">
      <div className="flex items-center gap-2">
        <ShieldCheck className="size-5 text-muted-foreground" />
        <h2 className="text-lg font-semibold tracking-[-0.02em]">
          Reviewer Verdict
        </h2>
      </div>
      <CheckCircle className="size-10 text-muted-foreground/30" />
    </div>

    <div className="mt-4 space-y-3">
      <div className="flex items-center justify-between">
        <label className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
          Approval justification
        </label>
        {justificationRequired ? (
          <span className="inline-flex rounded-full border border-border/70 bg-background/70 px-2.5 py-1 text-[11px] text-muted-foreground">
            Required
          </span>
        ) : null}
      </div>

      {maintenanceWindowRequired ? (
        <div className="space-y-2">
          <label className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
            Maintenance window date
          </label>
          <Input
            type="date"
            value={maintenanceWindowDate}
            onChange={(e) => setMaintenanceWindowDate(e.target.value)}
          />
          <p className="text-xs text-muted-foreground">
            The technical manager sets when the approved patch is expected to be in place.
          </p>
        </div>
      ) : null}

      <Textarea
        placeholder="Provide technical rationale for this decision..."
        value={justification}
        onChange={(e) => setJustification(e.target.value)}
        rows={4}
      />

      {resolveAction && justificationRequired && !justification.trim() ? (
        <p className="flex items-center gap-1.5 text-sm text-tone-danger-foreground">
          <AlertTriangle className="size-3.5" />
          Justification is required to {resolveAction} this task.
        </p>
      ) : null}
      {resolveAction === 'approve' && maintenanceWindowRequired && !maintenanceWindowDate ? (
        <p className="flex items-center gap-1.5 text-sm text-tone-danger-foreground">
          <AlertTriangle className="size-3.5" />
          Maintenance window date is required to approve this patching request.
        </p>
      ) : null}

      <div className="flex flex-wrap gap-3 pt-1">
        <Button onClick={() => handleResolve('approve')} className="min-w-[180px]">
          <CheckCircle className="mr-1.5 size-4" />
          Approve Remediation
        </Button>
        <Button variant="destructive" onClick={() => handleResolve('deny')} className="min-w-[180px]">
          <XCircle className="mr-1.5 size-4" />
          Deny Request
        </Button>
      </div>
    </div>
  </section>
) : null}
```

- [ ] **Step 3: Verify approve/deny functionality**

Run: `cd frontend && npm run dev`

Navigate to a pending approval task. The Reviewer Verdict card should appear between header and tabs, with the shield icon, editor, and buttons. Test: try to approve without justification when required — validation error should appear. Try approving with justification — should call `onResolve`.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/features/approvals/ApprovalTaskDetail.tsx
git commit -m "feat: add standalone Reviewer Verdict card for approval actions"
```

---

### Task 4: Add Requested Action Sidebar Card

**Files:**
- Modify: `frontend/src/components/features/approvals/ApprovalTaskDetail.tsx`

Add the first sidebar card showing the requested outcome and vulnerable software versions.

- [ ] **Step 1: Add Package icon import**

Update the lucide-react import:

```tsx
import { CheckCircle, XCircle, Eye, AlertTriangle, MessageSquare, ShieldCheck, Package, Info } from 'lucide-react'
```

- [ ] **Step 2: Compute vulnerable cohorts**

Add this computed value after the existing `affectedDeviceCount` computation (around line 98):

```tsx
const vulnerableCohorts = data.deviceVersionCohorts.filter(
  (c) => c.activeVulnerabilityCount > 0
)
```

- [ ] **Step 3: Add the Requested Action card in the sidebar `<aside>`**

Inside the `<aside className="space-y-5">` element, add:

```tsx
<section className="rounded-2xl border border-border/70 bg-background/60 p-5 space-y-4">
  <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
    Requested action
  </p>
  <p className="text-lg font-semibold tracking-[-0.02em]">
    {outcomeLabel(data.outcome)} for this software scope
  </p>
  <div className="rounded-xl border border-border/60 bg-background/45 p-3">
    <div className="flex items-center gap-3">
      <div className="flex size-10 items-center justify-center rounded-lg border border-border/60 bg-muted/30">
        <Package className="size-5 text-muted-foreground" />
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium truncate">{startCase(data.softwareName)}</p>
        <p className="text-xs text-muted-foreground">Software Library</p>
      </div>
      <Info className="size-4 shrink-0 text-muted-foreground" />
    </div>
  </div>
  {vulnerableCohorts.length > 0 ? (
    <div className="space-y-1.5">
      <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
        Vulnerable versions
      </p>
      <ul className="space-y-1 text-sm">
        {vulnerableCohorts.map((cohort) => (
          <li key={normalizeVersion(cohort.version) || '__unknown__'} className="flex items-center justify-between text-foreground/90">
            <span className="font-mono text-xs">{formatVersion(cohort.version)}</span>
            <span className="text-xs text-muted-foreground">
              {cohort.activeVulnerabilityCount} vuln{cohort.activeVulnerabilityCount === 1 ? '' : 's'}
            </span>
          </li>
        ))}
      </ul>
    </div>
  ) : (
    <p className="text-xs text-muted-foreground">No vulnerable versions</p>
  )}
</section>
```

- [ ] **Step 4: Verify the sidebar card renders**

Run: `cd frontend && npm run dev`

The right column should now show the Requested Action card with the outcome label, software name with package icon, and a list of vulnerable version cohorts.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/features/approvals/ApprovalTaskDetail.tsx
git commit -m "feat: add Requested Action sidebar card with vulnerable versions"
```

---

### Task 5: Add Risk Context Sidebar Card with Gauge

**Files:**
- Modify: `frontend/src/components/features/approvals/ApprovalTaskDetail.tsx`

Add the Risk Context card with a large score number, recharts semicircular gauge, and stat boxes for vulnerability and device counts.

- [ ] **Step 1: Add recharts imports**

Add at the top of the file:

```tsx
import { RadialBarChart, RadialBar, PolarAngleAxis } from 'recharts'
```

- [ ] **Step 2: Add a tone-to-CSS-color helper**

Add this function after the existing `riskBandTone` function. Recharts needs raw CSS color strings, not Tailwind classes. We use `getComputedStyle` to resolve the CSS custom property at render time:

```tsx
const toneColorVar: Record<string, string> = {
  danger: '--color-tone-danger-foreground',
  warning: '--color-tone-warning-foreground',
  success: '--color-tone-success-foreground',
  info: '--color-tone-info-foreground',
  neutral: '--color-foreground',
}

function useToneCssColor(tone: string): string {
  // Resolve CSS custom property to a usable color string for recharts
  if (typeof window === 'undefined') return 'currentColor'
  const varName = toneColorVar[tone] ?? toneColorVar.neutral
  return getComputedStyle(document.documentElement).getPropertyValue(varName).trim() || 'currentColor'
}
```

- [ ] **Step 3: Add the Risk Context card in the sidebar**

Inside the `<aside>`, after the Requested Action card, add:

```tsx
<section className="rounded-2xl border border-border/70 bg-background/60 p-5 space-y-4">
  <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
    Risk context
  </p>
  {data.riskScore != null && data.riskBand ? (
    <>
      <div className="flex items-center justify-between">
        <div>
          <p className="text-4xl font-bold tabular-nums tracking-tight">
            {data.riskScore.toFixed(1)}
          </p>
          <p className={`text-xs font-semibold uppercase tracking-[0.1em] ${toneText(riskBandTone(data.riskBand))}`}>
            {data.riskBand} risk score
          </p>
        </div>
        <RiskGauge score={data.riskScore} tone={riskBandTone(data.riskBand)} />
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="rounded-xl border border-border/60 bg-background/45 p-3 text-center">
          <p className="text-2xl font-bold tabular-nums">{vulnerabilityCount}</p>
          <p className="text-[10px] uppercase tracking-[0.1em] text-muted-foreground">
            Open<br />vulnerability
          </p>
        </div>
        <div className="rounded-xl border border-border/60 bg-background/45 p-3 text-center">
          <p className="text-2xl font-bold tabular-nums">{affectedDeviceCount}</p>
          <p className="text-[10px] uppercase tracking-[0.1em] text-muted-foreground">
            Affected<br />device
          </p>
        </div>
      </div>
    </>
  ) : (
    <p className="text-sm text-muted-foreground">No risk data available</p>
  )}
</section>
```

- [ ] **Step 4: Add the RiskGauge helper component**

Add this function component above the main `ApprovalTaskDetail` export (after `useToneCssColor`):

```tsx
function RiskGauge({ score, tone }: { score: number; tone: Tone }) {
  const color = useToneCssColor(tone)
  const gaugeData = [{ value: score, fill: color }]

  return (
    <div className="size-[100px]">
      <RadialBarChart
        width={100}
        height={60}
        cx={50}
        cy={55}
        innerRadius={35}
        outerRadius={50}
        startAngle={180}
        endAngle={0}
        data={gaugeData}
        barSize={10}
      >
        <PolarAngleAxis
          type="number"
          domain={[0, 10]}
          angleAxisId={0}
          tick={false}
        />
        <RadialBar
          dataKey="value"
          cornerRadius={5}
          background={{ fill: 'var(--color-muted)' }}
          angleAxisId={0}
        />
      </RadialBarChart>
    </div>
  )
}
```

- [ ] **Step 5: Add `toneText` import**

Update the import from `@/lib/tone-classes`:

```tsx
import { toneBadge, toneText } from '@/lib/tone-classes'
```

Also add the `Tone` type import:

```tsx
import { toneBadge, toneText, type Tone } from '@/lib/tone-classes'
```

- [ ] **Step 6: Import `riskBandTone` from remediation-utils instead of local**

Update the import from remediation-utils to also include `riskBandTone`:

```tsx
import {
  outcomeLabel,
  outcomeTone,
  riskBandTone,
} from '@/components/features/remediation/remediation-utils'
```

Then remove the local `riskBandTone` function (lines 45-58 in the original file) since it duplicates the utility.

- [ ] **Step 7: Verify the Risk Context card renders**

Run: `cd frontend && npm run dev`

The sidebar should now show two cards. The Risk Context card displays the score, a semicircular gauge arc colored by risk band, and two stat boxes. For tasks with null `riskScore`, it shows the fallback text.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/components/features/approvals/ApprovalTaskDetail.tsx
git commit -m "feat: add Risk Context sidebar card with recharts gauge"
```

---

### Task 6: Add Threat Intelligence Placeholder Card

**Files:**
- Modify: `frontend/src/components/features/approvals/ApprovalTaskDetail.tsx`

Add the third sidebar card as a placeholder for future threat intelligence data.

- [ ] **Step 1: Add the Threat Intelligence card in the sidebar**

Inside the `<aside>`, after the Risk Context card, add:

```tsx
<section className="rounded-2xl border border-border/70 bg-background/40 p-5 space-y-3">
  <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
    Threat intelligence
  </p>
  <p className="text-sm leading-relaxed text-muted-foreground italic">
    Threat intelligence summary will appear here when available.
  </p>
</section>
```

- [ ] **Step 2: Verify it renders**

Run: `cd frontend && npm run dev`

Three sidebar cards should now be visible in the right column.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/approvals/ApprovalTaskDetail.tsx
git commit -m "feat: add Threat Intelligence placeholder sidebar card"
```

---

### Task 7: Update Justification Tab with Requester Footer

**Files:**
- Modify: `frontend/src/components/features/approvals/ApprovalTaskDetail.tsx`

Add a requester info footer to the Justification tab content, showing who created the approval task and when.

- [ ] **Step 1: Compute the requester name**

Add this computed value alongside the other derived values (near `auditEvents`):

```tsx
const requesterName =
  data.auditTrail.find((e) => e.action === 'Created')?.userDisplayName ??
  data.decidedByName
```

- [ ] **Step 2: Add the footer card to the Justification tab**

Inside `<TabsContent value="justification">`, after the last `</section>` (the analyst recommendations section or the decision justification section if no recommendations), add:

```tsx
<section className="rounded-2xl border border-border/70 bg-background/60 p-5">
  <div className="flex flex-wrap items-center gap-6">
    <div>
      <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
        Requested by
      </p>
      <p className="mt-1 text-sm font-medium">{requesterName}</p>
    </div>
    <div>
      <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
        Request date
      </p>
      <p className="mt-1 text-sm font-medium">{formatDateTime(data.createdAt)}</p>
    </div>
  </div>
</section>
```

- [ ] **Step 3: Add `formatDateTime` import**

Update the formatting import:

```tsx
import { formatDate, formatDateTime, startCase } from '@/lib/formatting'
```

- [ ] **Step 4: Verify the footer renders in the Justification tab**

Run: `cd frontend && npm run dev`

Navigate to the Justification tab. A footer card with "Requested by" and "Request date" should appear at the bottom.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/features/approvals/ApprovalTaskDetail.tsx
git commit -m "feat: add requester info footer to Justification tab"
```

---

### Task 8: Clean Up Unused Code

**Files:**
- Modify: `frontend/src/components/features/approvals/ApprovalTaskDetail.tsx`

Remove the local `severityTone` and `riskBandTone` functions that are now unused or replaced by imports from `remediation-utils`. The local `severityTone` is still used in the vulnerabilities and devices tables — import it from `remediation-utils` instead (it's already exported there as `severityTone`).

- [ ] **Step 1: Update remediation-utils import to include severityTone**

```tsx
import {
  outcomeLabel,
  outcomeTone,
  riskBandTone,
  severityTone,
} from '@/components/features/remediation/remediation-utils'
```

- [ ] **Step 2: Remove local `severityTone` and `riskBandTone` functions**

Delete the local `severityTone` function (lines 30-43) and `riskBandTone` function (lines 45-58). Both are now imported.

Note: The local `severityTone` maps 'Medium' to `'neutral'` while the remediation-utils version maps it to `'info'`. This is a minor inconsistency — the utils version (`'info'`) is correct per the design system. Verify that Medium severity badges look acceptable with the info tone.

- [ ] **Step 3: Verify everything still renders correctly**

Run: `cd frontend && npm run dev`

Check all tabs, all badge colors, approve/deny flow. Medium severity badges will now use the info tone (blue-ish) instead of neutral (gray). All other functionality must be unchanged.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/features/approvals/ApprovalTaskDetail.tsx
git commit -m "refactor: use shared severity/risk tone functions from remediation-utils"
```
