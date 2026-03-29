# Risk Score Calculation

This document describes how PatchHound currently calculates risk in the implemented system.

It reflects the running code, not just the original design intent.

Primary implementation sources:
- `src/PatchHound.Infrastructure/Services/VulnerabilityThreatAssessmentService.cs`
- `src/PatchHound.Infrastructure/Services/VulnerabilityEpisodeRiskAssessmentService.cs`
- `src/PatchHound.Infrastructure/Services/RiskScoreService.cs`

## Overview

PatchHound uses two different numeric scales:

- `ThreatScore` uses `0-100`
- all `RiskScore` values use `0-1000`

That means:
- seeing `65` usually means you are looking at a threat component score
- seeing `569` means you are looking at an episode, asset, software, team, device-group, or tenant risk score

### Which score am I looking at?

Use this shortcut:

```text
0-100   = ThreatScore
0-1000  = RiskScore
```

Common examples:
- vulnerability threat context panel -> `ThreatScore (0-100)`
- affected asset episode risk -> `EpisodeRiskScore (0-1000)`
- asset/software/team/device-group/tenant cards -> aggregate `RiskScore (0-1000)`

PatchHound calculates risk in three layers:

```text
Vulnerability Definition
  -> Threat Assessment
  -> ThreatScore (0-100)

Open Vulnerability Episode on an Asset
  + ThreatScore
  + Asset / environment context
  + Workflow / remediation state
  -> EpisodeRiskScore (0-1000)

Open Episodes rolled up by scope
  -> AssetRiskScore
  -> SoftwareRiskScore
  -> DeviceGroupRiskScore
  -> TeamRiskScore
  -> TenantRiskScore
```

The key rule is:

- only open episodes contribute to current risk
- resolved episodes are excluded from current calculations

## 1. Threat Assessment

Code:
- `src/PatchHound.Infrastructure/Services/VulnerabilityThreatAssessmentService.cs`

Current calculation version:
- `2`

Threat assessment is calculated once per `VulnerabilityDefinition`.

It uses:
- vendor severity
- CVSS score if present
- reference tags such as:
  - `KEV`
  - `known exploited`
  - `exploit verified`
  - `exploit in kit`
  - `active alert`
  - `ransomware`
  - `malware`
  - `EPSS:<value>`

### 1.1 Technical Score

Range:
- `0-100`

Logic:
- if CVSS exists: `TechnicalScore = clamp(CVSS * 10, 0, 100)`
- otherwise severity fallback:
  - `Critical = 95`
  - `High = 75`
  - `Medium = 50`
  - `Low/other = 25`

### 1.2 Exploit Likelihood Score

Range:
- `0-100`

Logic:

```text
score = 0
+ 60 if known exploited
+ 30 if public exploit
+ 10 if ransomware association
+ EPSS contribution
```

EPSS contribution:

```text
EPSS contribution = clamp(round(EPSS * 20, 1), 0, 20)
```

Examples:
- `EPSS 0.90` adds `18`
- `EPSS 0.30` adds `6`

### 1.3 Threat Activity Score

Range:
- `0-100`

Logic:

```text
score = 0
+ 40 if active alert
+ 30 if ransomware association
+ 20 if malware association
+ 10 if known exploited
```

### 1.4 Recency Score

Range:
- `20-80`

Logic:
- published within `30 days` -> `80`
- published within `90 days` -> `60`
- published within `180 days` -> `40`
- older or missing -> `20`

### 1.5 Final Threat Score

Range:
- `0-100`

Formula:

```text
ThreatScore =
  0.35 * TechnicalScore
+ 0.25 * ExploitLikelihoodScore
+ 0.25 * ThreatActivityScore
+ 0.15 * RecencyScore
```

The result is rounded to 2 decimals.

### Illustration

```text
ThreatScore
├─ 35% technical severity baseline
├─ 25% exploit likelihood
├─ 25% active threat signals
└─ 15% recency
```

## 2. Episode Risk Assessment

Code:
- `src/PatchHound.Infrastructure/Services/VulnerabilityEpisodeRiskAssessmentService.cs`

Current calculation version:
- `1`

An episode risk assessment is calculated for an open vulnerability episode on a specific asset.

Important:
- this is the first layer that moves onto the `0-1000` scale

Inputs:
- `ThreatScore` from the vulnerability threat assessment
- asset criticality and Defender device context
- environmental severity assessment if available
- remediation task state
- approved risk acceptance
- ownership state

If no threat assessment exists, PatchHound falls back to:
- effective environmental score if available
- else CVSS
- else vendor severity fallback

## 2.1 Context Score

Range:
- `0-100`

Inputs:
- asset criticality
- Defender device value
- Defender exposure level
- Defender device risk state
- effective environmental severity

### 2.1.1 Criticality Score

- `Critical = 100`
- `High = 80`
- `Medium = 55`
- `Low = 30`

### 2.1.2 Device Value Score

- `HIGH = 100`
- `LOW = 30`
- default = `60`

### 2.1.3 Exposure Score

- `HIGH = 100`
- `MEDIUM = 70`
- `LOW = 40`
- default = `50`

### 2.1.4 Device Risk Score

- `HIGH = 95`
- `MEDIUM = 65`
- `LOW = 35`
- default = `50`

### 2.1.5 Effective Severity Score

- if effective environmental score exists: `clamp(EffectiveScore * 10, 0, 100)`
- otherwise environmental severity fallback:
  - `Critical = 95`
  - `High = 75`
  - `Medium = 50`
  - `Low = 25`
  - default = `50`

### 2.1.6 Final Context Score

Formula:

```text
ContextScore =
  0.35 * average(CriticalityScore, DeviceValueScore)
+ 0.25 * EffectiveSeverityScore
+ 0.20 * ExposureScore
+ 0.20 * DeviceRiskScore
```

## 2.2 Operational Score

Range:
- `0-100`

Base:
- starts at `40`

Adjustments:
- `+15` if the asset has no user owner, no team owner, and no fallback team
- `+10` if no open remediation task exists

If a remediation task exists:
- `Pending = +10`
- `InProgress = +5`
- `PatchScheduled = +0`
- `CannotPatch = +15`
- overdue task = `+20`

Risk acceptance:
- approved risk acceptance = `-20`

The result is clamped to `0-100`.

## 2.3 Final Episode Risk Score

Range:
- `0-1000`

Formula:

```text
EpisodeRiskScore =
  10 * (
    0.45 * ThreatScore
  + 0.40 * ContextScore
  + 0.15 * OperationalScore
  )
```

The result is rounded to 2 decimals and clamped to `0-1000`.

### Risk Bands

- `900-1000` -> `Critical`
- `750-899.99` -> `High`
- `500-749.99` -> `Medium`
- `0-499.99` -> `Low`

### Illustration

```text
EpisodeRiskScore
├─ 45% threat
├─ 40% context
└─ 15% operational urgency
```

## 3. Asset Risk Rollup

Code:
- `src/PatchHound.Infrastructure/Services/RiskScoreService.cs`

Assets are rolled up from unresolved episode risk assessments.

Inputs:
- all unresolved `EpisodeRiskScore` values for the asset
- counts by episode band

Formula:

```text
AssetRiskScore =
  0.70 * MaxEpisodeRisk
+ 0.20 * TopThreeEpisodeAverage
+ min(CriticalEpisodeCount * 35, 120)
+ min(HighEpisodeCount * 15, 60)
+ min(MediumEpisodeCount * 5, 20)
+ min(LowEpisodeCount * 1, 5)
```

Observations:
- the highest unresolved episode dominates
- the next two episodes still matter
- critical and high episode counts add pressure quickly
- medium and low counts matter, but are capped

## 4. Software Risk Rollup

Software risk is built from active software installations linked to unresolved software-vulnerability matches and unresolved episode risk on affected devices.

Formula:

```text
SoftwareRiskScore =
  0.65 * MaxEpisodeRisk
+ 0.20 * TopThreeEpisodeAverage
+ min(CriticalEpisodeCount * 30, 120)
+ min(HighEpisodeCount * 12, 48)
+ min(MediumEpisodeCount * 4, 16)
+ min(LowEpisodeCount * 1, 6)
```

Software is slightly less dominated by the maximum than asset risk, but still strongly driven by top unresolved episode pressure.

## 5. Device Group Risk Rollup

Device group risk aggregates asset risk scores for devices in the same Defender device group.

Formula:

```text
DeviceGroupRiskScore =
  0.55 * MaxAssetRisk
+ 0.25 * TopThreeAssetAverage
+ min(CriticalEpisodeCount * 8, 120)
+ min(HighEpisodeCount * 3, 60)
+ min(MediumEpisodeCount * 1, 20)
+ min(LowEpisodeCount * 0.25, 8)
```

This is intentionally more conservative than raw asset scoring:
- it still highlights the worst asset in the group
- but count-based pressure scales more slowly

## 6. Team Risk Rollup

Team risk aggregates asset risk scores for assets assigned to the team, directly or via fallback ownership.

Formula:

```text
TeamRiskScore =
  0.60 * MaxAssetRisk
+ 0.25 * TopThreeAssetAverage
+ min(CriticalEpisodeCount * 10, 150)
+ min(HighEpisodeCount * 4, 72)
+ min(MediumEpisodeCount * 1, 20)
+ min(LowEpisodeCount * 0.25, 8)
```

Compared to device groups, teams get slightly more pressure from severe counts because this rollup is meant to support accountability and workload prioritization.

## 7. Tenant Risk Rollup

Tenant risk aggregates asset risk scores across the tenant.

Formula:

```text
TenantRiskScore =
  0.55 * MaxAssetRisk
+ 0.30 * TopFiveAssetAverage
+ min(CriticalAssetCount * 18, 90)
+ min(HighAssetCount * 8, 40)
+ min(MediumAssetCount * 2, 10)
+ min(LowAssetCount * 0.5, 5)
```

This means:
- the single worst asset matters a lot
- the next worst assets matter too
- a large number of medium or low assets can increase pressure, but only within caps

### Why the tenant score can be high when critical/high counts are zero

This is expected.

Example:

```text
No critical assets
No high assets
Several medium assets
One very high medium asset score
--------------------------------
Tenant score can still be elevated
```

That is because the tenant score is not only:
- `criticalAssetCount`
- `highAssetCount`

It also uses:
- `maxAssetRisk`
- `topFiveAssetAverage`
- capped medium/low contributions

## 8. Filtered Tenant Risk

Filtered risk views do not use persisted tenant snapshots directly.

Instead they recalculate live from unresolved episode risk rows, filtered by:
- minimum published age
- platform
- device group

Current supported filtered dimensions:
- `minAgeDays`
- `platform`
- `deviceGroup`

Historical risk trend remains tenant-wide only.

## 9. What Changes Risk Outside Ingestion

Risk is recalculated not only during ingestion, but also when certain business actions change risk-relevant state.

Examples:
- remediation task changes
- risk acceptance approve/reject
- asset ownership changes
- security profile changes
- asset rule application that changes ownership/profile/criticality

This keeps current risk aligned with operational state, not just source imports.

## 10. Current Calculation Versions

At the time of writing:
- `Threat assessment`: `2`
- `Episode risk assessment`: `1`
- `Aggregate risk rollups`: `1`

## 11. Mental Model

If you want to reason about PatchHound risk quickly, use this:

```text
How dangerous is the vulnerability itself?
  -> ThreatScore

How dangerous is it on this asset, in this environment, with this workflow state?
  -> EpisodeRiskScore

How much unresolved risk pressure is concentrated in this scope?
  -> Asset / Software / Group / Team / Tenant RiskScore
```

## 12. Important Caveats

- current risk is episode-backed; resolved episodes should not drive current score
- software risk depends on active software installations plus unresolved matches and unresolved episode risk
- tenant history is snapshot-based; filtered history is live current-state only
- EPSS is now a first-class persisted field on threat assessment, but it is still sourced from available reference tags and enrichment inputs

## 13. Visual Summary

```text
Threat Layer (0-100)
  severity + CVSS
  exploit evidence
  threat activity
  recency
        |
        v
Episode Layer (0-1000)
  45% threat
  40% context
  15% operational
        |
        v
Aggregate Layer (0-1000)
  max pressure
  top-N average
  capped count contributions
```
