# PatchHound Critical Asset Management Design

## Goal

Introduce first-class critical asset management in PatchHound that:

- uses PatchHound-owned rules as the main classification mechanism
- keeps `Asset.Criticality` as the single canonical criticality used by risk scoring
- supports manual overrides and later external imports
- explains why an asset is classified as critical
- stays consistent across dashboards, tasking, and asset risk calculations

This design intentionally avoids creating a second competing criticality model.

## Core Rule

`Asset.Criticality` remains the canonical effective criticality.

Everything else is provenance and control around that field.

That means:

- asset risk scoring reads `Asset.Criticality`
- episode risk scoring reads `Asset.Criticality`
- dashboards and reporting read `Asset.Criticality`
- rules, overrides, and imports all resolve to `Asset.Criticality`

There must not be a separate “reporting criticality” or “risk criticality”.

## Why This Matters

PatchHound already uses `Asset.Criticality` in risk scoring.

Current code:

- `Asset.Criticality` is stored on `Asset` in `src/PatchHound.Core/Entities/Asset.cs`
- `VulnerabilityEpisodeRiskAssessmentService` uses `asset.Criticality` when calculating context score in `src/PatchHound.Infrastructure/Services/VulnerabilityEpisodeRiskAssessmentService.cs`

If critical asset management introduced a second field, the product would drift:

- admin sees one criticality
- risk score uses another
- dashboards become inconsistent

So the design must keep one effective value.

---

## 1. Data Model

### 1.1 Keep existing field as canonical

Keep:

- `Asset.Criticality`

Treat it as:

- the effective resolved criticality

### 1.2 Add provenance fields

Add to `Asset`:

- `CriticalitySource`
- `CriticalityReason`
- `CriticalityRuleId`
- `CriticalityUpdatedAt`
- `CriticalityOverride`
- `CriticalityOverrideReason`
- `CriticalityOverrideExpiresAt`
- `ImportedCriticality`
- `ImportedCriticalitySource`
- `ImportedCriticalityUpdatedAt`

Suggested source enum:

- `Default`
- `Rule`
- `ManualOverride`
- `ExternalImport`

Purpose:

- `Asset.Criticality` answers “what is the effective level?”
- provenance fields answer “why is it this level?”

### 1.3 Do not duplicate effective state

Do not add fields like:

- `EffectiveCriticality`
- `RiskCriticality`
- `DisplayedCriticality`

Those would duplicate the existing field and create alignment risk.

---

## 2. Precedence Model

Effective `Asset.Criticality` is resolved by precedence:

1. active manual override
2. imported external criticality, if import-as-authoritative is enabled
3. highest-priority matching PatchHound rule
4. default criticality

Recommended default:

- `Medium`

Reason:

- `None` or `Low` tends to under-prioritize by default
- `Medium` is safer until rules classify assets more precisely

### 2.1 Resolution contract

At the end of evaluation, write:

- `Asset.Criticality`
- `CriticalitySource`
- `CriticalityReason`
- `CriticalityRuleId`
- `CriticalityUpdatedAt`

This write is the canonical output used everywhere else.

---

## 3. Rule Model

### 3.1 Reuse asset rules pattern

Critical asset management should reuse the existing asset-rule framework.

Add a new rule action:

- `SetCriticality(level, reasonTemplate?)`

Example rules:

- if `Device Group = Tier 0 Servers` -> `Critical`
- if `Tag contains production` and `Owner Team = ERP` -> `High`
- if `Asset Type = Server` and `Internet Facing = true` -> `High`
- if `Name matches DC-*` -> `Critical`

### 3.2 Rule behavior

Rules should support:

- explicit priority order
- enable/disable
- preview matched assets
- stop on first match, or highest priority wins

Recommended initial behavior:

- evaluate in priority order
- first matching `SetCriticality` rule wins

This is simpler to explain and audit.

### 3.3 Rule output

When a rule wins, write:

- `Asset.Criticality = selected level`
- `CriticalitySource = Rule`
- `CriticalityRuleId = matched rule`
- `CriticalityReason = resolved explanation`

Example reason:

- `Matched criticality rule "Tier 0 infrastructure"`

---

## 4. Manual Overrides

### 4.1 Purpose

Manual overrides are for assets where the business importance is known but difficult to infer from metadata.

Examples:

- crown-jewel application server
- legal hold system
- merger and acquisition environment

### 4.2 Override behavior

An override sets:

- `CriticalityOverride`
- `CriticalityOverrideReason`
- optional `CriticalityOverrideExpiresAt`

If active, the override becomes the source of truth for `Asset.Criticality`.

### 4.3 Expiry

Optional override expiry is useful for:

- temporary business events
- migration windows
- project cutovers

When the override expires:

- reevaluate the asset
- fall back to import/rule/default precedence

---

## 5. External Imports

### 5.1 Not the foundation

PatchHound should not depend on Microsoft Security Exposure Management to have a criticality model.

### 5.2 Future import option

Later, PatchHound can ingest external criticality such as:

- Microsoft Security Exposure Management critical asset classification

If imported, store it separately:

- `ImportedCriticality`
- `ImportedCriticalitySource`
- `ImportedCriticalityUpdatedAt`

Then choose one of two modes:

- advisory only
- authoritative import

Recommended initial mode:

- advisory only

Later, allow:

- `Use imported criticality as authoritative`

### 5.3 Why separate imported state

This preserves:

- vendor independence
- auditability
- safe fallback if imports fail

---

## 6. Risk Alignment

### 6.1 Single source of truth

`Asset.Criticality` must remain the field consumed by risk scoring.

That means:

- `VulnerabilityEpisodeRiskAssessmentService` continues to read `asset.Criticality`
- `RiskScoreService` continues to roll up from episode scores that already use `asset.Criticality`

### 6.2 Required contract

Criticality evaluation must run before any recalculation that depends on asset context.

When criticality changes:

1. recompute `Asset.Criticality`
2. recompute vulnerability asset assessments if needed
3. recompute episode risk for affected open episodes
4. recompute aggregate asset/software/team/device-group/tenant risk

This should be routed through the existing refresh orchestration path, not ad hoc updates.

### 6.3 Rule for implementation

If a developer changes criticality evaluation, they must not separately change risk criticality logic.

There is only one effective criticality:

- `Asset.Criticality`

---

## 7. Operational Triggers

Reevaluate criticality when:

- asset rules change
- asset tags change
- owner team / fallback team changes
- device group changes
- source metadata changes
- manual override changes
- manual override expires
- external import updates

After reevaluation, trigger risk refresh for affected assets.

Recommended implementation:

- reuse `AssetRuleEvaluationService`
- route follow-up recompute through `RiskRefreshService`

---

## 8. Admin UX

### 8.1 Dedicated section

Add `Critical Asset Management` in admin.

Sections:

1. `Overview`
2. `Classification Rules`
3. `Overrides`
4. `Imports`

### 8.2 Overview

Show:

- count of assets by criticality
- recent criticality changes
- assets with manual overrides
- unclassified/default assets
- top rules by matched asset count

### 8.3 Classification rules

Reuse the asset-rules workbench pattern.

Each rule should show:

- condition summary
- resulting criticality
- priority
- preview matched assets

### 8.4 Overrides

Allow:

- set criticality
- set reason
- optional expiry
- view audit trail

### 8.5 Asset detail

Each asset page should show:

- current criticality
- source
- reason
- rule or override reference
- updated timestamp

Example:

```text
Criticality: Critical
Source: Rule
Reason: Matched rule "Tier 0 infrastructure"
Updated: 2026-03-23 09:41 UTC
```

---

## 9. Auditability

Every effective criticality change should write an audit event containing:

- asset id
- old criticality
- new criticality
- old source
- new source
- rule id or override details
- actor, if manual
- timestamp

This is necessary because criticality directly influences:

- risk score
- prioritization
- board reporting

---

## 10. Implementation Plan

### Phase 1

- treat `Asset.Criticality` as the canonical effective field
- add provenance fields to `Asset`
- add `SetCriticality` asset-rule action
- materialize rule output onto assets
- add asset detail explanation
- trigger risk refresh when criticality changes

### Phase 2

- add manual overrides
- add expiry support
- add admin overview and overrides UI
- add audit reporting for criticality changes

### Phase 3

- add optional Microsoft criticality import
- support advisory vs authoritative import mode
- expose import provenance in asset detail

---

## 11. Explicit Non-Goals

Not part of this design:

- creating a second effective criticality field
- calculating a separate “risk-only criticality”
- making Microsoft import mandatory
- using `DeviceValue` as a substitute for business criticality

---

## Recommendation

Implement critical asset management on top of the existing `Asset.Criticality` field.

That is the cleanest design because:

- risk scoring already depends on it
- it avoids model drift
- it keeps admin intent and risk behavior aligned
- it supports rules, overrides, and imports without introducing a second criticality system
