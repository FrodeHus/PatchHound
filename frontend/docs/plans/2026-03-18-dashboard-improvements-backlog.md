# Dashboard Improvements Backlog

Remaining suggestions from the vulnerability management dashboard review (2026-03-18).
Items 1-5 (SLA trend, sparklines, age distribution, MTTR, burndown) are implemented.

## Visualization Improvements

### 6. Risk Heatmap: Asset Group x Severity
2D heatmap with device groups on Y-axis and severity on X-axis, cells colored by count intensity. More information-dense than the current stacked bar chart. Data already available via `vulnerabilitiesByDeviceGroup`.

### 7. Owner/Team Accountability View
Leaderboard-style breakdown: outstanding criticals per owner, MTTR per team, SLA breach rate per team. Requires asset owner/team field on assets. Research shows this is the #1 driver of behavior change.

### 8. Remediation Pipeline Funnel
Show lifecycle stages: Detected → Triaged → Assigned → In Progress → Verified Fixed. Horizontal funnel or Sankey visualization revealing where vulnerabilities get stuck.

## Existing Component Enhancements

### 9. Enhance the Trend Chart
- Add a "net change" line (discovered minus resolved)
- Add annotations for significant events (new scanner, patch Tuesday)
- Consider stacked area chart for proportional severity view

### 10. Improve Remediation Velocity
- MTTR per severity as grouped bar (partially addressed by MttrCard)
- Velocity trend: is MTTR improving month over month?
- SLA target reference lines on the chart

### 11. Enrich the Exposure Score
Add a complementary posture metric (patch coverage %, scan coverage %, configuration compliance score) so dashboard shows both reactive (exposure) and proactive (posture) health.

### 12. Risk Change Card Enhancement
- 7-day rolling view option (24h can be noisy/empty on quiet days)
- Net risk delta as a number with directional arrow

## Layout & UX

### 13. Executive Summary Strip
3-4 large stat cards above filter bar: Exposure Score, MTTR (criticals), SLA Compliance %, Open Criticals. Visible without scrolling.

### 14. Drill-Down from Charts
Make chart elements clickable — clicking a severity band filters the vulnerability list, clicking a device group navigates to that group's assets.

## Data Model Gaps (Backend Work)

### 15. Exploitability Overlay
Add EPSS score or known-exploited flag per CVE. Surface "Critical+Exploitable open count" and average age metric. Separates theoretical risk from actionable risk.

### 16. Patch Coverage Metric
Assets patched / assets needing patch per CVE. Requires tracking patch deployment status.

### 17. Asset Owner/Team Field
Owner field on assets to enable accountability views (#7) and per-team MTTR/SLA metrics.
