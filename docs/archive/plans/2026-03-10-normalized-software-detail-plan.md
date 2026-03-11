# Normalized Software Detail Plan

**Date:** 2026-03-10
**Status:** Proposed

## Goal

Add a tenant-scoped software detail workspace centered on a stable internal normalized software identity rather than a source-specific software asset or Defender identifier.

The view should let operators answer:

- what this software product is,
- how prevalent it is in the tenant,
- which versions are present,
- where it is installed,
- which vulnerabilities are known to affect it,
- how strong the normalization and correlation evidence is.

This plan assumes:

- tenant-scoped only,
- one normalized product with related versions grouped beneath it,
- version cohorts are the primary navigation unit,
- cohort installation lists must be paged.

## Why this is needed

Current state:

- PatchHound has software assets as `Asset` rows with `AssetType.Software`.
- Device-to-software presence is modeled via `DeviceSoftwareInstallation` and episode history.
- Defender provides tenant-local software identifiers today.
- CPE binding and software-vulnerability matching already exist or are planned around software assets.
- Current software detail views are asset-centric and not suitable as a durable software workspace.

Current gaps:

- there is no stable normalized software identifier independent of Defender,
- there is no product-level view across related versions,
- prevalence is visible only indirectly through devices and software assets,
- software-vulnerability correlation is visible at the asset level, not the normalized product level,
- future inventory providers would force route and UI churn if Defender identifiers became the page key.

## Recommendation

Introduce a first-class normalized software aggregate and build the new UI on top of that aggregate.

Key decisions:

1. Use an internal `NormalizedSoftware.Id` as the page key.
2. Treat Defender software identifiers as source aliases, not the canonical identity.
3. Derive normalized software automatically using:
   - explicit CPE binding first,
   - vendor/name heuristics second,
   - manual override later only for exceptions.
4. Model versions as cohorts under the normalized identity, not as separate top-level pages.
5. Keep installation rows paged per selected version cohort.

## Intent

Who:

- security analysts,
- endpoint/platform operators,
- remediation owners.

What they must do:

- assess software blast radius,
- compare version spread,
- identify which installed versions are risky,
- navigate from product-level prevalence to affected devices and vulnerabilities,
- judge whether the identity/correlation is trustworthy.

How it should feel:

- operational,
- dense but legible,
- evidence-first,
- closer to a fleet inventory console than a generic asset profile.

## Domain exploration

### Domain

- software inventory
- install footprint
- version drift
- prevalence
- rollout waves
- identity normalization
- correlation confidence
- vulnerable install surface
- source coverage
- recurrence and retirement

### Color world

- graphite and slate for inventory structure
- muted steel blue for normalized identity and correlation state
- amber for vulnerable or drifting version cohorts
- red oxide for exposed installations and urgent versions
- pale green for healthy or standardized cohorts
- fog gray for historical install/remove signals

### Signature

Use a **Version Pressure Rail** as the page signature:

- one band per related version,
- width and emphasis reflect prevalence,
- each band shows install count, device count, vulnerability pressure, and recency,
- selecting a band filters the page into that cohort.

This is more specific to software operations than a generic summary card row.

### Defaults to avoid

1. Generic overview tabs.
   Replace with a software workspace where prevalence and version spread remain visible while drilling into cohorts.

2. Flat device table as the primary content.
   Replace with version cohorts first, each with a paged installation list.

3. Software asset detail copy/paste.
   Replace with a normalized-product model that can absorb multiple inventory sources later.

## Product scope

This is a new dedicated detail experience, not an extension of the right-side asset sheet.

Recommended route:

- `/software/$id`

Where:

- `$id` is `NormalizedSoftware.Id`
- all queries are tenant-scoped through the active tenant context

## Target domain model

### 1. NormalizedSoftware

New aggregate representing one tenant-scoped normalized software product.

Suggested fields:

- `Id`
- `TenantId`
- `CanonicalName`
- `CanonicalVendor`
- `CanonicalProductKey`
- `PrimaryCpe23Uri`
- `NormalizationMethod`
  - `Manual`
  - `ExplicitCpe`
  - `AliasConsensus`
  - `Heuristic`
- `Confidence`
  - `High`
  - `Medium`
  - `Low`
- `LastEvaluatedAt`
- `CreatedAt`
- `UpdatedAt`

Purpose:

- stable route key,
- stable aggregation point across source systems,
- software-level home for normalization state.

### 2. NormalizedSoftwareAlias

Maps source-system identities into one normalized software.

Suggested fields:

- `Id`
- `TenantId`
- `NormalizedSoftwareId`
- `SourceSystem`
  - `Defender`
  - future additional inventory providers
- `ExternalSoftwareId`
- `RawName`
- `RawVendor`
- `RawVersion`
- `AliasConfidence`
- `MatchReason`
- `CreatedAt`
- `UpdatedAt`

Recommended unique index:

- `(TenantId, SourceSystem, ExternalSoftwareId)`

Purpose:

- keep source provenance,
- support future providers without route churn,
- explain why multiple source identities rolled into one normalized product.

### 3. NormalizedSoftwareInstallation

Derived projection joining normalized identity to installed presence.

Suggested fields:

- `Id`
- `TenantId`
- `NormalizedSoftwareId`
- `SoftwareAssetId`
- `DeviceAssetId`
- `SourceSystem`
- `DetectedVersion`
- `FirstSeenAt`
- `LastSeenAt`
- `RemovedAt`
- `IsActive`
- `CurrentEpisodeNumber`

Purpose:

- fast cohort and prevalence queries,
- stable base for paging install lists,
- avoid rebuilding product-level views from raw joins for every request.

### 4. NormalizedSoftwareVulnerabilityProjection

Derived software-centric vulnerability summary.

Suggested fields:

- `Id`
- `TenantId`
- `NormalizedSoftwareId`
- `VulnerabilityId`
- `BestMatchMethod`
- `BestConfidence`
- `AffectedInstallCount`
- `AffectedDeviceCount`
- `AffectedVersionCount`
- `FirstSeenAt`
- `LastSeenAt`
- `ResolvedAt`
- `EvidenceJson`

Purpose:

- fast software-level vulnerability queries,
- support aggregation by normalized software rather than per software asset.

## Normalization strategy

Use automatic normalization with manual override only for exceptions.

### Matching order

1. Explicit CPE-backed identity

- if a software asset or alias has a trusted CPE binding, prefer that as the strongest grouping signal.

2. Source alias continuity

- if an existing alias already maps a source identity to a normalized software, reuse it.

3. Vendor/name heuristic normalization

- normalize vendor and product strings,
- strip punctuation and common suffix noise,
- lower-case and collapse whitespace,
- use version as a cohort attribute, not as the normalized identity key.

4. Fallback new normalized software

- if confidence is insufficient, create a new normalized software row rather than over-merging.

### Confidence guidance

- `High`
  - explicit trusted CPE match
  - repeated alias consensus across observations
- `Medium`
  - strong vendor/product heuristic match
- `Low`
  - weak heuristic match retained for review or later refinement

### Important rule

Do not include version in the normalized identity key.

Version must remain a cohorting dimension under one normalized product.

## Backend query and API design

## Query model

The software detail page should not query raw software assets directly.

Serve it from a normalized-software read model optimized for:

- summary metrics,
- version cohorts,
- paged installation lists,
- vulnerability aggregation,
- source evidence.

### Suggested endpoints

#### `GET /software/{id}`

Returns the normalized software detail summary.

Suggested payload:

- identity
  - `id`
  - `canonicalName`
  - `canonicalVendor`
  - `primaryCpe23Uri`
  - `normalizationMethod`
  - `confidence`
  - `firstSeenAt`
  - `lastSeenAt`
- prevalence
  - `totalInstallCount`
  - `activeInstallCount`
  - `uniqueDeviceCount`
  - `versionCount`
  - `activeVulnerabilityCount`
  - `vulnerableInstallCount`
- versions
  - `version`
  - `installCount`
  - `activeInstallCount`
  - `uniqueDeviceCount`
  - `activeVulnerabilityCount`
  - `firstSeenAt`
  - `lastSeenAt`
- source coverage
  - `sourceSystem`
  - `aliasCount`
  - `installCount`
- top distributions
  - assignment groups
  - OS platforms
  - criticality bands
- recent timeline summary

#### `GET /software/{id}/installations`

Returns paged installation rows for one selected cohort.

Required query params:

- `version`
- `page`
- `pageSize`

Suggested payload:

- `items`
  - `deviceAssetId`
  - `deviceName`
  - `deviceExternalId`
  - `criticality`
  - `ownerType`
  - `ownerName`
  - `assignmentGroupName`
  - `firstSeenAt`
  - `lastSeenAt`
  - `removedAt`
  - `activeVulnerabilityCount`
  - `sourceSystems`
- pagination metadata

#### `GET /software/{id}/vulnerabilities`

Returns normalized-software vulnerability summaries.

Suggested payload:

- `vulnerabilityId`
- `externalId`
- `title`
- `vendorSeverity`
- `cvssScore`
- `effectiveSeverity`
- `bestMatchMethod`
- `bestConfidence`
- `affectedInstallCount`
- `affectedDeviceCount`
- `affectedVersions`
- `firstSeenAt`
- `lastSeenAt`
- `resolvedAt`

#### Optional later: `GET /software/{id}/timeline`

Use if timeline query cost becomes large.

## Frontend design

### View structure

Create a dedicated page component rather than extending:

- `AssetDetailPane`
- `AssetDetailPageView`

Suggested feature folder:

- `frontend/src/components/features/software/`

Suggested route:

- `frontend/src/routes/_authed/software/$id.tsx`

### Layout

#### Header band

Shows:

- canonical software name
- canonical vendor
- normalization confidence
- CPE binding state
- quick stats:
  - active installs
  - devices
  - versions
  - active vulnerabilities

Actions:

- edit/view normalized identity
- open vulnerability list
- export installation list

#### Version Pressure Rail

Primary page signature.

Behavior:

- versions displayed as weighted bands,
- active cohort highlighted,
- show install count and vulnerability pressure,
- clicking a version changes the cohort filter,
- default cohort:
  - highest active install count,
  - tie-break on highest vulnerability pressure.

#### Main column

1. Cohort workspace

- selected version summary
- prevalence and recency stats
- paged installation list
- filters:
  - vulnerable only
  - active installs only
  - critical devices only
  - owner / assignment group

2. Known vulnerabilities

- software-centric rows
- grouped by severity or sorted by affected installs
- each row shows:
  - CVE
  - severity
  - match method
  - confidence
  - affected versions
  - affected install count

Selecting a vulnerability should highlight its affected versions in the rail if implemented later.

#### Right rail

1. Identity and correlation

- primary CPE
- normalization method
- confidence
- source alias count
- manual override flag later

2. Source evidence

- Defender aliases today
- future additional inventories later
- show raw names and vendors where useful

3. Organizational spread

- top assignment groups
- top operating systems
- top criticality bands

4. Timeline summary

- first seen
- most recent install seen
- newest version introduced
- oldest still-active version

## Information hierarchy

The top of the page must answer, in order:

1. What software product is this?
2. How common is it?
3. Which versions dominate?
4. How much vulnerability pressure does it carry?

The middle of the page must answer:

1. Which devices are running the selected version?
2. Is the problem concentrated in one cohort or spread broadly?

The right rail must answer:

1. Can I trust the normalization?
2. Which sources support this identity?
3. What follow-up action makes sense?

## Pagination behavior

Version cohorts are primary, but device/install rows must be paged.

Recommendation:

- one selected cohort at a time,
- paged installation list for the selected cohort,
- page state resets when the selected version changes,
- page size default `25`

Avoid:

- rendering all version cohorts fully expanded at once,
- nested unbounded tables,
- one giant cross-version install table as the default.

## Navigation and integration

Entry points should be added from:

- software asset detail views,
- software inventory rows on device detail,
- matched software references in vulnerability detail,
- future software-focused search.

Transitional behavior:

- software asset detail can show a link:
  - `Open normalized software workspace`

This keeps current asset-specific editing intact while shifting product-level analysis into the new page.

## Suggested frontend modules

- `software-detail-page.tsx`
- `software-header-band.tsx`
- `version-pressure-rail.tsx`
- `software-cohort-panel.tsx`
- `software-installations-table.tsx`
- `software-vulnerability-panel.tsx`
- `software-identity-rail.tsx`
- `software-source-evidence-panel.tsx`

## Suggested API/schema files

- `frontend/src/api/software.schemas.ts`
- `frontend/src/api/software.functions.ts`

## Implementation phases

### Phase 1: normalized software backend model

- add `NormalizedSoftware`
- add `NormalizedSoftwareAlias`
- map existing Defender software identities into aliases
- establish automatic normalization pipeline

Outcome:

- stable internal normalized software ID exists.

### Phase 2: derived projections

- add normalized software installation projection
- add normalized software vulnerability projection
- backfill current tenant software inventory into the projection

Outcome:

- software detail queries can be served without heavy raw joins.

### Phase 3: API

- add software detail summary endpoint
- add paged cohort installation endpoint
- add vulnerability summary endpoint

Outcome:

- frontend can load a dedicated normalized software page.

### Phase 4: UI shell

- add route and feature components
- implement header band
- implement Version Pressure Rail
- implement selected-cohort pagination flow

Outcome:

- operators can inspect one normalized product end-to-end.

### Phase 5: integration and navigation

- link from software asset detail
- link from vulnerability detail matched software
- link from device software inventory rows

Outcome:

- normalized software becomes a first-class operational surface.

### Phase 6: correction tools later

- manual merge
- manual split
- alias reassignment
- override audit history

Outcome:

- automatic normalization remains default, with admin repair paths for bad merges.

## Risks

### 1. Over-merging software identities

If heuristics are too aggressive, unrelated products may collapse into one normalized software.

Mitigation:

- favor creating a new normalized product over low-confidence merges,
- persist confidence,
- add correction workflows later.

### 2. Under-merging versions or aliases

If heuristics are too conservative, the same product may fragment.

Mitigation:

- keep alias history,
- allow later regrouping,
- use CPE binding to strengthen normalization.

### 3. Expensive aggregation queries

Raw joins across assets, installations, episodes, and vulnerabilities may be expensive.

Mitigation:

- use derived projections for installations and vulnerabilities,
- index by `TenantId`, `NormalizedSoftwareId`, and `DetectedVersion`.

### 4. Defender assumptions leaking into canonical identity

Even with a normalized ID, names and vendor fields may still reflect Defender quirks.

Mitigation:

- keep raw source aliases explicit,
- maintain canonical name/vendor separately from raw alias fields.

## Open items

These are intentionally deferred, not blockers for the first plan:

- whether normalized software should support package ecosystem metadata such as PURL,
- whether cohort vulnerability counts should be precomputed or query-time,
- whether the timeline should be fully paged or summary-only in phase 1,
- whether cross-software comparison views should follow later.

## Recommendation summary

Build the new software detail experience on a first-class `NormalizedSoftware` aggregate with a stable internal ID.

The page should be:

- tenant-scoped,
- normalized-product first,
- version-cohort first,
- paged at the cohort installation level,
- explicit about correlation evidence and source coverage.

This keeps the UI aligned with current PatchHound software correlation work while avoiding future lock-in to Defender-specific identifiers.
