# CPE Software Matching Plan

## Goal

Use NVD CPE applicability data to match vulnerabilities against PatchHound software inventory in a way that is:

- accurate enough to be operationally useful
- explainable in the UI
- safe enough to avoid noisy false-positive remediation tasks

This plan treats CPE as a normalized matching layer, not as a perfect source of truth.

## Current State

PatchHound already has:

- software inventory as `Asset` rows with `AssetType.Software`
- device-to-software relationships via `DeviceSoftwareInstallation`
- NVD enrichment for:
  - description
  - CVSS
  - references
  - affected software `cpeMatch` rows
- Defender direct device vulnerability correlation

Current gap:

- software assets are not yet mapped to CPE identities
- NVD `cpeMatch` data is shown in vulnerability detail, but not used to match installed software
- there is no confidence-scored link between a software asset and a vulnerable product definition

## Recommendation

Use a layered correlation model:

1. Defender direct correlation remains highest confidence.
2. CPE-backed matching becomes the standardized secondary correlation path.
3. Heuristic vendor/name/version matching is fallback only.

Do not use heuristic matching alone for automatic remediation task creation.

## Key Design

### 1. Add Software CPE Binding

Create a durable mapping table between software assets and normalized CPE identity.

Suggested entity: `SoftwareCpeBinding`

Fields:

- `Id`
- `TenantId`
- `SoftwareAssetId`
- `Cpe23Uri`
- `BindingMethod`
  - `Manual`
  - `NvdDictionaryLookup`
  - `Heuristic`
  - `DefenderDerived`
- `Confidence`
  - `High`
  - `Medium`
  - `Low`
- `MatchedVendor`
- `MatchedProduct`
- `MatchedVersion`
- `LastValidatedAt`
- `CreatedAt`
- `UpdatedAt`

Purpose:

- lets PatchHound reuse the same mapping across many CVEs
- makes matching explainable
- supports later manual approval/rejection

### 2. Keep NVD Affected Software as First-Class Rows

PatchHound should keep storing NVD `cpeMatch` rows on vulnerabilities.

Suggested entity shape already aligns with this:

- `VulnerabilityAffectedSoftware`
  - `VulnerabilityId`
  - `Vulnerable`
  - `Criteria`
  - `VersionStartIncluding`
  - `VersionStartExcluding`
  - `VersionEndIncluding`
  - `VersionEndExcluding`

Current implementation only includes:

- `Vulnerable`
- `Criteria`
- `VersionStartIncluding`
- `VersionEndExcluding`

Recommended next extension:

- add `VersionStartExcluding`
- add `VersionEndIncluding`

Reason:

- NVD range semantics use all four bounds depending on the record
- without all four, range evaluation will be incomplete

### 3. Add Derived Software Vulnerability Links

Add a derived link table for software exposure.

Suggested entity: `SoftwareVulnerabilityMatch`

Fields:

- `Id`
- `TenantId`
- `SoftwareAssetId`
- `VulnerabilityId`
- `MatchMethod`
  - `DefenderDirect`
  - `CpeExact`
  - `CpeRange`
  - `Heuristic`
- `Confidence`
- `EvidenceJson`
- `CreatedAt`
- `LastValidatedAt`
- `Status`

Purpose:

- show first-class software exposure in the product
- support filtering and remediation flows
- avoid recomputing every match at query time

## Matching Strategy

### Tier 1: Defender Direct

Use Defender-provided product/vendor/version-to-device correlation as the highest-confidence source.

If a Defender vulnerability record directly identifies a software product already present in inventory:

- create or update `SoftwareVulnerabilityMatch`
- set `MatchMethod = DefenderDirect`
- set `Confidence = High`

### Tier 2: Exact CPE Binding

If a software asset already has a known `Cpe23Uri`:

- compare it directly against vulnerability `Criteria`
- apply version-range rules
- if it matches, create `SoftwareVulnerabilityMatch`

This is the preferred NVD-based automated path.

### Tier 3: Dictionary-Assisted Binding

Use the NVD product/CPE APIs to find candidate CPE names for software assets based on:

- vendor
- product name
- version

Then:

- create or update `SoftwareCpeBinding`
- record confidence and method
- use the binding for vulnerability matching

### Tier 4: Heuristic Fallback

If no CPE binding exists:

- normalize vendor
- normalize product name
- compare against parsed CPE vendor/product tokens
- evaluate version range if possible

This should be:

- visible in UI
- marked `Low` confidence
- excluded from automatic remediation by default

## Matching Rules

For the first implementation, use pragmatic CPE evaluation:

1. Parse `criteria` CPE 2.3 string.
2. Compare:
   - part
   - vendor
   - product
3. Evaluate version with:
   - `versionStartIncluding`
   - `versionStartExcluding`
   - `versionEndIncluding`
   - `versionEndExcluding`
4. Treat wildcard CPE versions as requiring range evaluation or software-version fallback.

Do not attempt full NIST WFN matching semantics in phase 1.

Reason:

- full CPE name matching is heavier to implement
- PatchHound will get most value from exact vendor/product plus version-range checks

## Confidence Model

Recommended scoring:

- `High`
  - Defender direct
  - manually approved CPE binding
  - exact CPE match with exact version fit
- `Medium`
  - NVD dictionary-assisted binding with strong vendor/product match
- `Low`
  - heuristic normalized string match

This confidence should be shown anywhere software-to-CVE correlation is surfaced.

## UI Plan

### Vulnerability Detail

Extend the `Affected Software` section to show:

- parsed product identity from CPE
- raw `criteria`
- version range
- matched installed software assets
- confidence
- match method

### Software Asset Detail

Add a `Matched Vulnerabilities` section showing:

- CVE
- severity
- match method
- confidence
- evidence

### Device Detail

For each installed software item, show:

- whether it is mapped to a CPE
- whether it is linked to any CVEs through that mapping

## Remediation Policy

Recommended default:

- auto-create remediation tasks only for:
  - Defender direct software matches
  - `High` confidence CPE matches

Do not auto-create tasks for:

- `Medium` or `Low` confidence heuristic-only matches

Instead:

- surface them for analyst review
- allow later approval/ignore workflows

## Data Model Changes

### Phase 1

Add:

- `SoftwareCpeBinding`
- `SoftwareVulnerabilityMatch`
- extend `VulnerabilityAffectedSoftware` with the missing range-bound fields

### Phase 2

Add optional approval state:

- `IsApproved`
- `ApprovedBy`
- `ApprovedAt`
- `RejectedAt`

This allows manual control for noisy software names.

## Ingestion and Processing Flow

1. Ingest software inventory.
2. Ingest vulnerabilities from Defender.
3. Enrich vulnerabilities from NVD.
4. Persist `VulnerabilityAffectedSoftware`.
5. Resolve or refresh `SoftwareCpeBinding` for software assets.
6. Evaluate `SoftwareVulnerabilityMatch`.
7. Create or update remediation items only for allowed confidence tiers.

This should run in the worker, not in request paths.

## API Additions

### Software Binding API

Needed later for admin/analyst workflows:

- list bindings
- view binding confidence/evidence
- approve/reject manual mapping
- manually set CPE for a software asset

### Detail Endpoints

Extend:

- software asset detail
- vulnerability detail

to include:

- matched software assets
- match confidence
- evidence payload

## Testing Plan

Add focused tests for:

- CPE parsing from NVD `criteria`
- exact version-in-range matching
- inclusive/exclusive range handling
- wildcard version handling
- software asset to CPE binding selection
- confidence assignment
- no auto-task creation for low-confidence matches

## Rollout Order

### Step 1

Extend `VulnerabilityAffectedSoftware` to store the full NVD range fields.

### Step 2

Add `SoftwareCpeBinding`.

### Step 3

Implement high-confidence CPE matching only.

### Step 4

Add `SoftwareVulnerabilityMatch` and UI display.

### Step 5

Add analyst controls for manual binding approval/correction.

### Step 6

Optionally add NVD `/cpes/2.0`-assisted binding enrichment and caching.

## Recommended First Implementation Slice

The best next concrete slice is:

1. extend `VulnerabilityAffectedSoftware` with all version-range fields
2. add `SoftwareCpeBinding`
3. implement exact/high-confidence software-to-CVE matching
4. show matched software plus evidence in vulnerability detail

That gives real operational value without jumping straight to fuzzy matching or over-automation.
