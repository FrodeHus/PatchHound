# ApprovalTaskDetail Redesign

Restructure `ApprovalTaskDetail.tsx` from a single-column stacked layout to a two-column layout with a context sidebar, matching the provided design mockup.

## Layout

Two-column CSS grid: `lg:grid-cols-[1fr_320px]`, gap-5. Stacks vertically on mobile (sidebar below main content).

### Left Column

#### 1. Header

Keep the existing gradient card and breadcrumb. Changes:

- Title format: "Remediation Approval: {softwareName}" (prefix added)
- Status badges, severity badge, and expiry countdown all render inline in a single `flex-wrap` row beneath the title
- Remove the current metadata sidebar box from the header (its data moves to the right-column cards)
- Remove the "Requested decision" sub-section from the header (moves to Requested Action sidebar card)
- Keep the mark-as-read button for non-pending resolved tasks

#### 2. Reviewer Verdict Card (pending only)

Standalone card below the header (not nested inside it). Structure:

- Card header row: shield icon (`ShieldCheck` from lucide) + "Reviewer Verdict" title, large checkmark icon on the right
- "APPROVAL JUSTIFICATION" label (uppercase tracking)
- Rich text editor (existing `Textarea` component with toolbar)
- Placeholder: "Provide technical rationale for this decision..."
- Validation errors inline below the editor (existing logic)
- Maintenance window date input above the editor when `maintenanceWindowRequired` (existing logic)
- Two action buttons side by side:
  - "Approve Remediation" — primary/success style with `CheckCircle` icon
  - "Deny Request" — destructive/outline style with `XCircle` icon

#### 3. Tabs Section

Same four tabs (Justification, Vulnerabilities, Affected Devices, Timeline). All existing tab content stays.

**Justification tab addition**: Add a footer card at the bottom with:
- "REQUESTED BY" — derived from the audit trail "Created" event's `userDisplayName`, fallback to `decidedByName`
- "REQUEST DATE" — `createdAt` formatted as date + time

### Right Column (Sidebar)

#### 1. Requested Action Card

- Section label: "REQUESTED ACTION" (uppercase tracking)
- Outcome text: "{outcomeLabel} for this software scope" (e.g., "Patch this software for this software scope")
- Vulnerable versions list: compact list of all `deviceVersionCohorts` where `activeVulnerabilityCount > 0`, each showing version string + vuln count (e.g., "1.10.0 — 3 vulns"). If none, show "No vulnerable versions".
- Software name displayed with a package-style icon (`Package` from lucide) + "Software Library" subtitle

#### 2. Risk Context Card

- Section label: "RISK CONTEXT" (uppercase tracking)
- Large risk score number (text-4xl font-bold)
- Risk band label below the number (e.g., "HIGH RISK SCORE"), colored by `riskBandTone`
- Semicircular gauge using recharts `RadialBarChart`:
  - `startAngle={180}` `endAngle={0}` for a 180-degree arc
  - Single `RadialBar` filled proportionally (`riskScore / 10`)
  - Bar color derived from `riskBandTone`
  - Centered to the right of the score number
- Two stat boxes below in a 2-column grid:
  - Open vulnerabilities count (`vulnerabilities.totalCount`)
  - Affected devices count (sum of `deviceVersionCohorts[].deviceCount`)
- If `riskScore` is null, show "No risk data" placeholder instead of gauge

#### 3. Threat Intelligence Card

- Section label: "THREAT INTELLIGENCE" (uppercase tracking)
- Dark-toned card (`bg-background/40`)
- Placeholder text: "Threat intelligence summary will appear here when available."
- Styled to match the dark card aesthetic from the mockup

## Styling

All cards use existing design system patterns:
- `rounded-2xl border border-border/70 bg-background/60 p-5`
- Section labels: `text-[10px] uppercase tracking-[0.14em] text-muted-foreground`
- Stat boxes: bordered sub-cards with large number + small label

The header retains its existing `rounded-[28px]` gradient style.

## Data

No schema changes. All data derived from existing `ApprovalTaskDetail` type:
- Requester name: `auditTrail.find(e => e.action === 'Created')?.userDisplayName ?? decidedByName`
- Software version list: `deviceVersionCohorts.filter(c => c.activeVulnerabilityCount > 0)`
- Risk gauge: `riskScore` (0-10 scale), `riskBand`
- Vuln/device counts: existing computed values

## Component Changes

Only `ApprovalTaskDetail.tsx` is modified. No new files, no new components, no schema changes. The recharts `RadialBarChart` and `RadialBar` are imported directly into the component.

## Responsive Behavior

- `lg:` breakpoint: two-column grid
- Below `lg`: single column, sidebar cards stack below the tabs section
