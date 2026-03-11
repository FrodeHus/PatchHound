# Environmental Severity Recalculation Plan

**Date:** 2026-03-07
**Status:** Proposed

## Goal

Allow PatchHound to recalculate vulnerability severity per device asset using reusable environment and exposure settings.

Examples:

- devices with no internet reachability should not be prioritized the same way as internet-facing systems,
- isolated lab or OT assets should score differently from standard user workstations,
- devices with strong compensating controls should show a lower effective severity than the vendor base score,
- high-confidentiality assets should be able to raise impact even when the vendor severity is moderate.

The result should be:

- standards-aligned,
- explainable,
- reusable across many devices,
- auditable,
- compatible with the current PatchHound domain model.

## Why This Should Be Done

Today PatchHound supports:

- vendor/base severity on `Vulnerability`,
- asset criticality on `Asset`,
- manual tenant-level override records via `OrganizationalSeverity`.

That is useful, but it is still too coarse.

Current gap:

- no reusable device environment model,
- no per-device environmental recalculation,
- no deterministic explanation for why a vulnerability is less severe on one device than another,
- no scalable alternative to manual severity adjustments.

## Standards Direction

Use CVSS Environmental scoring as the primary recalculation model.

Reasoning:

- FIRST CVSS explicitly supports organization-specific environment overrides,
- NVD treats Base metrics as generic severity, not environment-specific risk,
- the user examples map directly to modified exploitability and impact assumptions.

PatchHound should not replace CVSS with a custom severity engine for this layer.
Instead:

1. use CVSS Environmental metrics to compute an effective per-device severity,
2. optionally add a second prioritization layer later for operational decisioning.

## PatchHound Target Model

### 1. AssetSecurityProfile

Add a reusable profile entity, assigned to many device assets.

Suggested fields:

- `Id`
- `TenantId`
- `Name`
- `Description`
- `EnvironmentClass`
  - examples: `Workstation`, `Server`, `JumpHost`, `OT`, `Kiosk`, `Lab`
- `InternetReachability`
  - examples: `Internet`, `InternalNetwork`, `AdjacentOnly`, `LocalOnly`
- `OutboundInternetAccess`
  - `Allowed`, `Blocked`, `Restricted`
- `UserInteractionLikelihood`
  - `Common`, `Rare`, `None`
- `PrivilegeBoundary`
  - `StandardUser`, `PrivilegedRequired`
- `ConfidentialityRequirement`
  - `Low`, `Medium`, `High`
- `IntegrityRequirement`
  - `Low`, `Medium`, `High`
- `AvailabilityRequirement`
  - `Low`, `Medium`, `High`
- `CompensatingControlsJson`
  - structured list, not free text
- `Source`
  - `Manual`, `Imported`, `Policy`
- `CreatedAt`
- `UpdatedAt`

Purpose:

- reusable assignment to many assets,
- controlled settings instead of arbitrary strings,
- good defaulting and bulk management.

### 2. Asset-Level Overrides / Facts

Add a second entity for direct device facts and exceptions.

Suggested name:

- `AssetSecurityContext`

Suggested fields:

- `AssetId`
- `SecurityProfileId`
- `InternetFacing`
- `NetworkIsolationLevel`
- `HasInteractiveUsers`
- `RequiresPrivilegedAccessForCommonExploitPaths`
- `CompensatingControlsJson`
- `DeviceTagsJson`
- `FactSource`
  - `Manual`, `Defender`, `Intune`, `Derived`
- `LastEvaluatedAt`

Purpose:

- hold auto-discovered facts and explicit exceptions,
- let profile defaults be overridden per device,
- preserve explainability.

### 3. Per-Device Assessment Cache

Add a computed per-device severity projection rather than recalculating on every request.

Suggested name:

- `VulnerabilityAssetAssessment`

Suggested fields:

- `Id`
- `TenantId`
- `VulnerabilityId`
- `AssetId`
- `BaseSeverity`
- `BaseScore`
- `BaseVector`
- `EnvironmentalSeverity`
- `EnvironmentalScore`
- `EnvironmentalVector`
- `PriorityScore`
- `CalculationVersion`
- `CalculatedAt`
- `FactorsJson`
- `ReasonSummary`
- `UsedSecurityProfileId`

Purpose:

- fast queries and UI rendering,
- auditability,
- versioned recalculation behavior,
- room for future prioritization logic.

### 4. Keep Existing Manual Override Model

Keep `OrganizationalSeverity`, but reposition it as a manual exception / governance record.

Recommended semantic split:

- `VulnerabilityAssetAssessment` = computed severity for one vulnerability on one asset,
- `OrganizationalSeverity` = manual tenant-level override and decision record.

This avoids replacing manual governance with an opaque calculation engine.

## How It Fits The Current Domain

### Asset

Current `Asset` already stores:

- criticality,
- device-specific normalized details,
- ownership,
- metadata.

Recommended additions:

- `SecurityProfileId`
- optional navigation to `AssetSecurityProfile`

No large expansion of `Asset` itself is recommended beyond the profile reference.
Most environment facts should live in a dedicated context/assessment model.

### VulnerabilityAsset

Current `VulnerabilityAsset` is the current projection for a vulnerability-device relationship.

Recommended additions:

- `EffectiveSeverity`
- `EffectiveScore`
- `AssessmentId`

Alternative:

- keep `VulnerabilityAsset` unchanged and fetch the latest `VulnerabilityAssetAssessment` alongside it.

Recommendation:

- add a nullable `AssessmentId` only if it makes query paths simpler,
- otherwise keep assessments separate and query by `(VulnerabilityId, AssetId)`.

## Recommended Calculation Model

### Layer 1: CVSS Environmental Recalculation

Given:

- vulnerability base score/vector,
- asset security profile,
- asset-specific security facts,

compute:

- modified exploitability assumptions,
- impact requirements,
- environmental score,
- derived severity band.

Mapping examples:

- `InternetReachability = LocalOnly`
  - can lower effective exploitability for network-dependent issues,
  - but should not blindly collapse every `AV:N` to `Local`; use a mapped downgrade model.
- `UserInteractionLikelihood = None`
  - mitigates vulnerabilities that require user interaction.
- `ConfidentialityRequirement = High`
  - raises impact importance for confidentiality-sensitive devices.
- `CompensatingControls` includes `ApplicationControl`, `NetworkIsolation`, `RestrictedFirewall`
  - lower prioritization or modified exploitability assumptions where justified.

### Layer 2: Operational Prioritization

Do not try to encode all business urgency into CVSS itself.

After computing environmental severity, calculate a separate priority score from:

- environmental score,
- asset criticality,
- internet-facing flag,
- active exploitation evidence,
- recurrence count,
- software reinstallation correlation,
- open remediation age.

This gives:

- standards-based severity,
- operations-based prioritization.

## Fact Sources

### Auto-Discovered Sources

1. Defender device inventory
   - internet-facing classification if available,
   - machine tags,
   - device group/context,
   - risk and exposure signals.

2. Intune
   - device categories,
   - compliance posture,
   - configuration profile grouping.

3. Internal tagging
   - tenant-defined tags mapped to PatchHound profiles.

### Manual Sources

1. Admin-managed security profile assignment
2. Device-level exception flags
3. Assignment-group or tenant defaults

## Product Behavior

### Profile Assignment

Allow these assignment paths:

1. manual assignment on the asset inspector,
2. bulk assignment in the asset list,
3. auto-assignment by Defender tag,
4. auto-assignment by asset type,
5. tenant default profile for new device assets.

### Recalculation Triggers

Recalculate assessments when:

1. a vulnerability is created or updated,
2. a vulnerability-device link is created or reopened,
3. an asset profile changes,
4. asset environment facts change,
5. a calculation version changes,
6. a manual “recompute severity” action is triggered.

### UI Surfaces

#### Vulnerability Detail

Show:

- vendor severity,
- environmental severity range across assets,
- top downgraded assets,
- top upgraded assets,
- explanation of common adjustment factors.

#### Asset Inspector

Show:

- assigned security profile,
- discovered exposure facts,
- linked vulnerabilities with:
  - base severity,
  - environmental severity,
  - adjustment reason.

#### Asset List

Show:

- assigned security profile badge,
- optional environmental exposure badge:
  - `Internet-facing`,
  - `Internal only`,
  - `Isolated`,
  - `No user interaction`.

#### Admin / Settings

Add:

- profile management page,
- default profile configuration,
- tag-to-profile mapping screen.

## Rule Design Guidance

Avoid a free-form rule engine first.

Recommended approach:

1. define a small set of explicit exposure dimensions,
2. map those dimensions to environmental metric transformations,
3. record the exact factors used,
4. keep the logic deterministic and versioned.

Do not start with:

- arbitrary expression builders,
- unbounded text fields used in calculations,
- tenant-authored scripting.

That would be hard to validate and impossible to explain well.

## Suggested Delivery Phases

### Phase 1: Foundation

- add `AssetSecurityProfile`,
- add `Asset.SecurityProfileId`,
- add `VulnerabilityAssetAssessment`,
- build CVSS vector parser/recalculator service,
- support a small profile field set:
  - internet reachability,
  - user interaction likelihood,
  - CIA requirements.

Output:

- computed per-device environmental severity,
- visible in API and asset inspector.

### Phase 2: Auto Facts And Bulk Assignment

- add `AssetSecurityContext`,
- ingest Defender tags/internet-facing signals,
- add bulk profile assignment in assets UI,
- add tenant default profile behavior.

Output:

- less manual data entry,
- better consistency.

### Phase 3: Prioritization Layer

- add `PriorityScore`,
- incorporate asset criticality, internet-facing status, and exploit evidence,
- update dashboards and work queues.

Output:

- remediation ordering becomes more useful than vendor severity alone.

### Phase 4: Governance And Overrides

- allow manual approval/override flows,
- tie adjustments to assignment groups and approvers,
- add audit history for profile changes and recalculation events.

## Database Changes

Likely EF additions:

1. `AssetSecurityProfile`
2. `AssetSecurityContext`
3. `VulnerabilityAssetAssessment`
4. `Asset.SecurityProfileId`

Likely indexes:

- `AssetSecurityProfile(TenantId, Name)`
- `Asset(TenantId, SecurityProfileId)`
- `VulnerabilityAssetAssessment(TenantId, VulnerabilityId, AssetId)`
- `VulnerabilityAssetAssessment(CalculatedAt)`

## API Changes

### New Endpoints

- `GET /api/security-profiles`
- `POST /api/security-profiles`
- `PUT /api/security-profiles/{id}`
- `POST /api/assets/{id}/security-profile`
- `POST /api/assets/bulk-security-profile`
- `POST /api/vulnerabilities/{id}/recalculate`

### Existing Endpoint Extensions

- extend asset detail DTO with:
  - `securityProfile`
  - `securityContext`
  - `assessmentSummary`
- extend vulnerability detail DTO with:
  - environmental severity distribution,
  - per-asset environmental severity,
  - adjustment reasons.

## Testing Plan

Required tests:

1. profile assignment updates affected assets correctly,
2. CVSS recalculation is deterministic for a fixed input vector,
3. `No user interaction` lowers severity for `UI:R` vulnerabilities,
4. `High confidentiality requirement` raises effective severity where appropriate,
5. `Internal only` differs from `Internet-facing`,
6. asset profile change triggers recalculation,
7. vulnerability ingestion creates/updates assessment rows,
8. UI/API expose both vendor and environmental severity.

## Risks

1. Over-simplified reachability modeling can produce misleading results.
2. Free-form manual rules will become unmaintainable quickly.
3. If the system hides the original vendor severity, users may lose trust in the recalculation.
4. Recalculation must be explainable or operators will ignore it.

## Recommended First Slice For PatchHound

Implement this exact first slice:

1. `AssetSecurityProfile`
2. `Asset.SecurityProfileId`
3. `VulnerabilityAssetAssessment`
4. deterministic CVSS environmental recalculation service
5. three initial profile dimensions:
   - internet reachability,
   - user interaction likelihood,
   - confidentiality / integrity / availability requirements
6. asset inspector + vulnerability detail UI showing:
   - vendor severity,
   - environmental severity,
   - top adjustment reasons

That is the smallest version that delivers real value without overengineering.
