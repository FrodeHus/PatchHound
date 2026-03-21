# PatchHound Risk Scoring Design

## Goal

Introduce a first-class risk scoring model for PatchHound that:

- uses open episodes as the canonical truth
- combines technical severity, threat intelligence, and asset context
- produces explainable per-episode risk scores
- supports risk-aware rollups for assets, software, device groups, teams, and tenants
- stays distinct from the existing `SecureScore` posture metric

This is intentionally TruRisk-like in structure, but PatchHound-specific in implementation.

## Why This Exists

PatchHound already has:

- environmental CVSS/context scoring in `VulnerabilityAssetAssessment`
- asset criticality and Defender device context
- tenant/asset secure score infrastructure

PatchHound does not yet have:

- a dedicated threat-intelligence scoring layer
- a first-class current risk score for open vulnerability episodes
- risk-aware rollups that prevent many low-risk items from diluting a few high-risk ones

That gap is the difference between severity-based prioritization and real risk-based prioritization.

## Design Rules

- Episodes are the truth.
- Only open episodes contribute to current risk.
- Resolved episodes contribute only to historical reporting.
- Threat score, episode risk score, and aggregate risk score are separate concepts.
- `SecureScore` remains a posture metric and must not be rebranded as risk.
- All scores must be explainable with persisted factor breakdowns.
- Every newly introduced risk should increase the score.
- Every resolved risk should decrease the score.

---

## 1. Scoring Model

### 1.1 Three score layers

The model has three layers:

1. `ThreatScore`
2. `EpisodeRiskScore`
3. `AggregateRiskScore`

### 1.2 ThreatScore

`ThreatScore` is vulnerability-centric and expresses how likely or dangerous exploitation is in the real world.

Range:

- `0–100`

Inputs:

- effective or base CVSS
- source severity
- exploit availability
- known exploitation
- active threat association
- malware/ransomware relevance
- recency of exploitation signal

Recommended initial composition:

- technical severity baseline: `35%`
- exploit likelihood: `25%`
- active threat evidence: `25%`
- weaponization/recency: `15%`

`ThreatScore` is global per `VulnerabilityDefinition` unless a provider only gives tenant-local or machine-local signals.

### 1.3 EpisodeRiskScore

`EpisodeRiskScore` is the main operational score for prioritization.

It is calculated per open vulnerability-on-asset episode.

Range:

- `0–1000`

Formula shape:

```text
EpisodeRiskScore =
  10 * (
    0.45 * ThreatScore +
    0.40 * ContextScore +
    0.15 * OperationalScore
  )
```

Clamp to `0–1000`.

This keeps the score intuitive:

- threat is slightly dominant
- asset/environment context materially changes priority
- workflow state influences action urgency but does not define inherent exposure

### 1.4 ContextScore

`ContextScore` measures how bad the vulnerability is on this specific asset in this specific environment.

Range:

- `0–100`

Recommended composition:

- asset criticality / device value: `35%`
- network reachability / attack surface: `25%`
- Defender device exposure / device risk state: `20%`
- environmental/business context: `20%`

Primary inputs:

- `Asset.Criticality`
- `Asset.DeviceValue`
- `Asset.DeviceExposureLevel`
- `Asset.DeviceRiskScore`
- `AssetSecurityProfile`
- effective CVSS from `VulnerabilityAssetAssessment`
- optional future asset tags / rules / device group weighting

### 1.5 OperationalScore

`OperationalScore` represents remediation urgency modifiers.

Range:

- `0–100`

Recommended inputs:

- overdue SLA
- asset ownership missing
- compensating controls
- approved risk acceptance

Important rule:

- risk acceptance lowers remediation priority
- risk acceptance does not erase the fact that the vulnerability is still present

### 1.6 AggregateRiskScore

`AggregateRiskScore` is the rollup for:

- asset
- software
- device group
- team
- tenant

It must be risk-aware, not average-based.

Recommended formula shape:

```text
AggregateRisk =
  MaxEpisodeRisk * g(MaxEpisodeRiskBand)
  + weighted counts of critical/high/medium/low episode risk items
```

Band weights:

- critical: `0.80`
- high: `0.15`
- medium: `0.03`
- low: `0.02`

Caps:

- cap medium and low-band counts to avoid inflating risk through volume
- optionally cap critical/high counts at very large scale

If there are no critical items:

- high inherits the critical band’s weight

This mirrors the useful part of the Qualys v2 idea: high-risk items remain visible even when they are surrounded by large volumes of low-risk items.

### 1.7 Risk bands

Episode and aggregate risk bands:

- `900–1000`: Critical
- `750–899`: High
- `500–749`: Medium
- `1–499`: Low
- `0`: None

---

## 2. Current PatchHound Inputs

### Reuse directly

- `VulnerabilityAssetAssessment` in `src/PatchHound.Core/Entities/VulnerabilityAssetAssessment.cs`
- `EnvironmentalSeverityCalculator` in `src/PatchHound.Infrastructure/Services/EnvironmentalSeverityCalculator.cs`
- `VulnerabilityAssessmentService` in `src/PatchHound.Infrastructure/Services/VulnerabilityAssessmentService.cs`
- `Asset.Criticality`
- `Asset.DeviceValue`
- `Asset.DeviceRiskScore`
- `Asset.DeviceExposureLevel`
- `Asset.DeviceGroupId`
- `Asset.DeviceGroupName`
- `AssetSecurityProfile` and its environmental overrides
- workflow entities such as remediation tasks and risk acceptances

### Reuse partially

- `SecureScoreService`
- `SecureScoreCalculator`
- `TenantSecureScoreSnapshot`

These can provide useful infrastructure patterns, but should not be the source of truth for risk.

### Missing today

- threat-enrichment persistence model
- episode-level risk assessment entity
- aggregate risk snapshot entity
- threat-aware software risk model
- risk-aware tenant rollup

---

## 3. New Entities

### 3.1 VulnerabilityThreatAssessment

Purpose:

- persist calculated threat signals for a vulnerability definition

Shape:

```csharp
public class VulnerabilityThreatAssessment
{
    public Guid Id { get; private set; }
    public Guid VulnerabilityDefinitionId { get; private set; }
    public decimal ThreatScore { get; private set; }
    public decimal TechnicalScore { get; private set; }
    public decimal ExploitLikelihoodScore { get; private set; }
    public decimal ThreatActivityScore { get; private set; }
    public bool KnownExploited { get; private set; }
    public bool PublicExploit { get; private set; }
    public bool ActiveAlert { get; private set; }
    public bool HasRansomwareAssociation { get; private set; }
    public bool HasMalwareAssociation { get; private set; }
    public string FactorsJson { get; private set; } = "[]";
    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }
}
```

Notes:

- one row per vulnerability definition
- recalculated when enrichment changes

### 3.2 VulnerabilityEpisodeRiskAssessment

Purpose:

- persist current risk for an open episode

Shape:

```csharp
public class VulnerabilityEpisodeRiskAssessment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid VulnerabilityAssetEpisodeId { get; private set; }
    public Guid TenantVulnerabilityId { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid? SnapshotId { get; private set; }
    public decimal ThreatScore { get; private set; }
    public decimal ContextScore { get; private set; }
    public decimal OperationalScore { get; private set; }
    public decimal EpisodeRiskScore { get; private set; }
    public string RiskBand { get; private set; } = null!;
    public string FactorsJson { get; private set; } = "[]";
    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }
}
```

Notes:

- only for open episodes
- delete or archive on resolution
- can be regenerated

### 3.3 AggregateRiskSnapshot

Purpose:

- store rollup scores for trends and dashboards

Shape:

```csharp
public class AggregateRiskSnapshot
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string ScopeType { get; private set; } = null!; // Tenant, Asset, Software, DeviceGroup, Team
    public string ScopeKey { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public decimal AggregateRiskScore { get; private set; }
    public decimal MaxEpisodeRiskScore { get; private set; }
    public int CriticalCount { get; private set; }
    public int HighCount { get; private set; }
    public int MediumCount { get; private set; }
    public int LowCount { get; private set; }
    public int OpenEpisodeCount { get; private set; }
}
```

---

## 4. New Services

### 4.1 VulnerabilityThreatAssessmentService

Responsibilities:

- compute and persist `VulnerabilityThreatAssessment`
- normalize multi-source threat inputs
- version and explain factor breakdowns

Inputs:

- NVD enrichment
- Defender recommendation enrichment
- optional EPSS / KEV / other intelligence later

### 4.2 EpisodeRiskAssessmentService

Responsibilities:

- compute `VulnerabilityEpisodeRiskAssessment` for open episodes
- join:
  - `VulnerabilityThreatAssessment`
  - `VulnerabilityAssetAssessment`
  - asset state
  - workflow state
- persist factor breakdown

### 4.3 AggregateRiskService

Responsibilities:

- compute risk-aware rollups
- calculate:
  - asset risk
  - software risk
  - device group risk
  - team risk
  - tenant risk
- write daily snapshots

### 4.4 RiskScoreExplanationService

Responsibilities:

- convert factor JSON into UI-ready explanations
- provide:
  - “why this is high”
  - top contributing factors
  - score delta explanation

---

## 5. Defender API Expansion

PatchHound already uses `/api/machines`.

For risk scoring, add:

### 5.1 Machine recommendations

Endpoint:

- `GET /api/machines/{machineId}/recommendations`

Use:

- `publicExploit`
- `activeAlert`
- `associatedThreats`
- `severityScore`
- `exposureImpact`
- `configScoreImpact`

Reference:

- [Microsoft Learn: Get security recommendations](https://learn.microsoft.com/en-us/defender-endpoint/api/get-security-recommendations)

### 5.2 Recommendation detail

Endpoint:

- `GET /api/recommendations/{id}`

Use:

- deeper per-recommendation metadata if machine-level recommendation rows are insufficient

Reference:

- [Microsoft Learn: Get recommendation by ID](https://learn.microsoft.com/en-us/defender-endpoint/api/get-recommendation-by-id)

### 5.3 Recommendation software

Endpoint:

- `GET /api/recommendations/{id}/software`

Use:

- software-centric risk signals
- better software rollups

Reference:

- [Microsoft Learn: List software by recommendation](https://learn.microsoft.com/en-us/defender-endpoint/api/list-recommendation-software)

### 5.4 Machine vulnerabilities

Endpoint:

- `GET /api/machines/{machineId}/vulnerabilities`

Use:

- machine-local vulnerability context if recommendation linkage is incomplete

Reference:

- [Microsoft Learn: Get discovered vulnerabilities](https://learn.microsoft.com/en-us/defender-endpoint/api/get-discovered-vulnerabilities)

### 5.5 Keep but do not use as episode risk input

Endpoint:

- device secure score / configuration score APIs

Use:

- benchmarking or posture views only

Do not use:

- as the main score for vulnerability risk prioritization

Reason:

- too coarse
- blends posture with threat exposure

### Defender mapping

Recommended field mapping:

| Defender field | PatchHound role |
|---|---|
| `riskScore` | context multiplier |
| `exposureLevel` | context multiplier |
| `deviceValue` | business criticality multiplier |
| `publicExploit` | threat multiplier |
| `activeAlert` | threat multiplier |
| `associatedThreats` | threat multiplier |
| `exposureImpact` | context/threat signal |
| `configScoreImpact` | operational/configuration signal |
| `rbacGroupId` / `rbacGroupName` | aggregation boundary and filtering |

---

## 6. Scoring Inputs in Detail

### 6.1 Threat score inputs

Phase 1:

- effective/base CVSS
- source severity
- Defender `publicExploit`
- Defender `activeAlert`
- Defender `associatedThreats`

Phase 2:

- CISA KEV
- EPSS
- NVD exploitability metadata if available

### 6.2 Context score inputs

- `Asset.Criticality`
- `Asset.DeviceValue`
- `Asset.DeviceExposureLevel`
- `Asset.DeviceRiskScore`
- `AssetSecurityProfile.InternetReachability`
- effective CVSS environmental score
- optional future business asset rules

### 6.3 Operational score inputs

- remediation overdue
- remediation unassigned
- risk accepted
- compensating controls
- optionally: asset offline / stale last-seen state

---

## 7. Read Model Rules

### Canonical current-state rule

Every current vulnerability view must be based on open episodes or data directly derived from open episodes.

Specifically:

- vulnerability detail
- asset detail
- software detail
- dashboard risk brief
- device group views
- risk trends

must all derive from:

- `VulnerabilityAssetEpisode.Status == Open`

and not from stale projections alone.

### Software risk rule

Software risk is not “all historical matched vulnerabilities”.

Software current risk is:

- the set of open episodes whose open assets currently map to the software in the active snapshot

This is important because software views are especially susceptible to drift if they rely on match tables without episode status.

---

## 8. API Changes

### New DTOs

- `RiskScoreDto`
- `RiskFactorDto`
- `AggregateRiskSummaryDto`
- `RiskTrendPointDto`

### Vulnerability detail

Add:

- current highest episode risk
- affected asset risk distribution
- threat factor explanation

### Asset detail

Add:

- current aggregate asset risk
- top contributing open episodes
- risk trend

### Software detail

Add:

- current software aggregate risk
- top contributing vulnerabilities
- affected high-risk devices

### Dashboard

Add:

- tenant risk score
- risk trend
- top device groups by risk
- top software by risk
- top open episodes by risk

### Tasks

Change primary sort option to:

- `EpisodeRiskScore desc`

instead of severity-only priority.

---

## 9. UI Changes

### 9.1 Common UI pattern

Show risk in three layers:

- score
- band
- explanation

Every risk display should have:

- numeric score
- colored band
- “why” panel or tooltip

### 9.2 Vulnerability page

Add:

- `Risk Score`
- factor stack:
  - threat
  - environment
  - workflow
- highest-risk affected assets

### 9.3 Asset page

Add:

- current asset risk score
- current risk trend
- top unresolved episodes driving risk

### 9.4 Software page

Add:

- aggregate software risk score
- vulnerabilities sorted by risk contribution
- top exposed device groups for that software

### 9.5 Dashboard

Add:

- tenant risk score card
- trend chart
- device-group risk table
- software risk table
- “risk moved because” summary

---

## 10. Migration Strategy

### Phase 1 — Threat scoring foundation

- add `VulnerabilityThreatAssessment`
- extend enrichment jobs and source runners
- add Defender recommendation enrichment
- persist threat factors

### Phase 2 — Episode risk scoring

- add `VulnerabilityEpisodeRiskAssessment`
- compute for open episodes only
- recalc on:
  - ingestion merge
  - enrichment update
  - asset context/profile change
  - workflow state change

### Phase 3 — Aggregate rollups

- add `AggregateRiskService`
- add daily snapshots
- compute tenant, asset, software, device group, and team rollups

### Phase 4 — Read model migration

- move vulnerability, asset, software, dashboard, and task prioritization to risk-backed queries
- leave existing secure score surfaces in place as posture views

### Phase 5 — Optional external intelligence

- add KEV
- add EPSS
- add richer software-specific threat evidence

---

## 11. Recalculation Triggers

Recalculate `ThreatScore` when:

- vulnerability enrichment changes
- Defender recommendation threat fields change

Recalculate `EpisodeRiskScore` when:

- an episode opens
- an episode resolves
- `VulnerabilityThreatAssessment` changes
- `VulnerabilityAssetAssessment` changes
- asset criticality changes
- device value / device risk / device exposure changes
- security profile changes
- remediation task SLA state changes
- risk acceptance changes

Recalculate aggregate risk when:

- any episode risk changes
- any episode opens or resolves
- membership in asset/software/device-group/team scopes changes

---

## 12. Testing Strategy

### Unit tests

- threat score calculation
- context score calculation
- operational modifiers
- aggregate risk rollup with dilution-resistant behavior

### Integration tests

- episode open -> score created
- episode resolved -> score removed from current risk
- Defender recommendation changes -> threat score changes
- asset criticality change -> episode and aggregate risk change
- software risk reflects only open episode-backed exposure

### Regression tests

- many low-risk assets do not drown one critical/high-risk cluster
- accepted risk lowers priority but does not mark issue as gone
- dashboard/detail/software all agree on current risk state

---

## 13. Keep / Avoid

### Keep

- `SecureScore` for posture and hardening
- environmental CVSS assessment logic
- episode model as canonical lifecycle truth
- explainable factors persisted as JSON

### Avoid

- severity-only prioritization
- average-based tenant risk rollups
- using stale projections as current risk truth
- collapsing posture score and threat risk into one number
- treating risk acceptance as disappearance of exposure

---

## 14. Recommendation

Recommended implementation order:

1. Build `VulnerabilityThreatAssessment`
2. Add Defender recommendation enrichment
3. Build `VulnerabilityEpisodeRiskAssessment`
4. Move current risk reads to episode-backed scoring
5. Add risk-aware aggregate rollups
6. Keep `SecureScore` as a separate posture metric

This is the minimum path that gives PatchHound a defensible, TruRisk-like model without confusing posture and risk or reintroducing read-model drift.

## Sources

- [Qualys: What is TruRisk](https://docs.qualys.com/en/vmdr/latest/trurisk/what_is_trurisk.htm)
- [Qualys: Understanding Your TruRisk Score](https://docs.qualys.com/en/vm/latest/mergedProjects/risk_score/trurisk/understanding_your_trurisk_score.htm)
- [Qualys: Platform Level TruRisk v2 Formula](https://docs.qualys.com/en/etm/latest/appendix/platform_level_trurisk_score.htm)
- [Microsoft Learn: List machines API](https://learn.microsoft.com/en-us/defender-endpoint/api/get-machines)
- [Microsoft Learn: Get security recommendations](https://learn.microsoft.com/en-us/defender-endpoint/api/get-security-recommendations)
- [Microsoft Learn: Get recommendation by ID](https://learn.microsoft.com/en-us/defender-endpoint/api/get-recommendation-by-id)
- [Microsoft Learn: List software by recommendation](https://learn.microsoft.com/en-us/defender-endpoint/api/list-recommendation-software)
- [Microsoft Learn: Get discovered vulnerabilities](https://learn.microsoft.com/en-us/defender-endpoint/api/get-discovered-vulnerabilities)
