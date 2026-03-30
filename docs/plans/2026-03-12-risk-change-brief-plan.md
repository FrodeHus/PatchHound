# Risk Change Brief Plan

## Intent

- Who: a tenant-scoped security analyst or vulnerability owner starting their day in PatchHound
- What they need to do: understand which high or critical risks entered or exited the organization in the last 24 hours, without reading a long table
- How it should feel: operational, time-sensitive, compact, and trustworthy

## Domain

- arrival
- clearance
- churn
- recurrence
- blast radius
- newly exposed
- disappeared from scope
- operational delta

## Color World

- amber and red for newly appeared risk
- green and blue-green for resolved or disappeared risk
- slate and graphite for stable dashboard structure
- pale neutral separators so the module reads as a briefing block, not a dense report

## Signature

`Risk Change Brief`

A compact dashboard card split into two balanced lanes:

- `New in last 24h`
- `Resolved in last 24h`

Each lane shows only the top few high-signal items and a count badge. The card acts like a morning change brief rather than another dashboard table.

## Defaults To Avoid

- a full-width dense table on the dashboard
- separate cards for every vulnerability event
- a large AI-generated prose block
- mixing this with the existing trend graph as another metric tile

## Recommendation

Implement a compact dashboard component with a linked detail view.

Dashboard:

- one card, two lanes
- up to 3 items per lane
- counts for:
  - new high or critical vulnerabilities
  - resolved high or critical vulnerabilities
- one quiet footer link:
  - `Open full change log`

Drill-down:

- dedicated route for full details
- full list of:
  - appeared vulnerabilities
  - resolved vulnerabilities
- optional future filters for time window, recurrence, and asset count

## Placement

Place the component on the overview dashboard below the hero graph row and above the larger supporting widgets.

This gives it:

- high visibility
- clear relationship to trend movement
- enough prominence without overwhelming the landing screen

Suggested placement in:

- [frontend/src/routes/_authed/index.tsx](/Users/frode.hus/src/github.com/frodehus/PatchHound/frontend/src/routes/_authed/index.tsx)

## Interaction Model

### Dashboard Card

Header:

- `Risk Change Brief`
- time window badge: `Last 24 hours`

Left lane:

- `New`
- count badge
- top 3 items

Right lane:

- `Resolved`
- count badge
- top 3 items

Item row:

- CVE ID
- severity
- asset count
- compact secondary line:
  - `Seen on 3 assets`
  - `Resolved from 5 assets`

Footer:

- `Open full change log`

### Empty States

Per lane:

- `No new high or critical vulnerabilities in the last 24 hours.`
- `No high or critical vulnerabilities resolved in the last 24 hours.`

The component should still render when only one side has changes.

## Data Semantics

Use tenant-scoped vulnerability exposure history, not global definition timestamps.

### Appeared

A high or critical tenant vulnerability that became present in the tenant within the last 24 hours.

### Resolved

A high or critical tenant vulnerability that stopped being present in the tenant within the last 24 hours.

### Severity Source

Use the effective tenant-facing severity for dashboard display:

- adjusted environmental severity if present
- otherwise vendor severity

This matches how operators triage in PatchHound.

## Backend Shape

Add a compact change summary to the dashboard API.

Suggested DTO addition to the dashboard summary response:

```csharp
public sealed record DashboardRiskChangeBriefDto(
    int AppearedCount,
    int ResolvedCount,
    IReadOnlyList<DashboardRiskChangeItemDto> Appeared,
    IReadOnlyList<DashboardRiskChangeItemDto> Resolved,
    string? AiSummary);

public sealed record DashboardRiskChangeItemDto(
    Guid TenantVulnerabilityId,
    string ExternalId,
    string Title,
    string Severity,
    int AffectedAssetCount,
    DateTime ChangedAt);
```

Suggested source:

- [src/PatchHound.Api/Controllers/DashboardController.cs](/Users/frode.hus/src/github.com/frodehus/PatchHound/src/PatchHound.Api/Controllers/DashboardController.cs)

Suggested underlying model inputs:

- `TenantVulnerability`
- `VulnerabilityAsset`
- `VulnerabilityAssetEpisode`
- `OrganizationalSeverity`
- `VulnerabilityDefinition`

The query should be bounded and summary-oriented:

- only `High` and `Critical`
- only current tenant
- only changes in last 24 hours
- top 3 items per lane for dashboard

## Frontend Shape

Suggested new components:

- `RiskChangeBriefCard.tsx`
- `RiskChangeLane.tsx`
- `RiskChangeRow.tsx`

Suggested location:

- `frontend/src/components/features/dashboard`

The dashboard card should use existing shared primitives:

- `Card`
- `InsetPanel`
- `Badge`
- `Button` or text link

Do not introduce a new dashboard-specific chrome system.

## AI Augmentation

AI should be optional and supplemental, not required for the component to be useful.

Recommended AI usage:

- one short summary sentence only
- generated from the same deterministic change brief payload
- shown only if the tenant has a valid default AI profile

Good examples:

- `3 critical issues appeared, concentrated on internet-facing server profiles.`
- `2 high-severity issues cleared, reducing exposure on workstation devices.`

Do not:

- generate a long markdown block on the dashboard
- make AI part of first paint
- call AI synchronously during normal dashboard loading

Recommended implementation:

- dashboard always renders deterministic data
- optional background-generated AI summary can be fetched separately or cached
- if unavailable, the component still looks complete

## Detail View

Add a dedicated route later for full inspection:

- `/vulnerabilities/changes`

Suggested layout:

- header with time filter
- two stacked sections:
  - `New`
  - `Resolved`
- same row model as dashboard, expanded with:
  - first seen / resolved timestamp
  - current asset count
  - recurrence marker

This route should be introduced after the dashboard card, not before.

## Visual Direction

The component should feel like a briefing card, not another metric grid.

Recommended styling:

- one major card
- two inset lanes
- strong whitespace
- restrained typography
- count badges with severity-aware color
- rows separated by quiet borders, not full boxed cards

Hierarchy:

1. counts
2. lane titles
3. CVE rows
4. footer link

## Suggested Implementation Phases

### Phase 1

- add backend deterministic `RiskChangeBrief` summary
- render dashboard card with counts and top 3 items per lane
- add footer link placeholder

### Phase 2

- add full change-log route
- wire footer link

### Phase 3

- add optional AI one-line summary
- cache/store it per tenant and time window if needed

## Acceptance Criteria

- dashboard shows high or critical appeared and resolved items for the selected tenant only
- tenant switch refreshes the card correctly
- empty states are clear and not visually broken
- the dashboard card remains compact and readable
- AI is optional and never blocks the deterministic view

## Sources

- FIRST CVSS v3.1 Calculator: https://www.first.org/cvss/calculator/3.1
- FIRST CVSS v3.1 User Guide: https://www.first.org/cvss/v3.1/user-guide
- FIRST CVSS v3.1 Specification: https://www.first.org/cvss/v3.1/specification-document
