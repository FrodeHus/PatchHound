> **ARCHIVED 2026-04-10.** This document is the historical working draft that
> preceded the canonical data model cleanup refactor. It is preserved verbatim
> for future archaeology. **Do not treat any statement in this file as current
> architectural guidance.** The authoritative reference is
> `docs/data-model-refactor.md` (≤200 lines) which describes the final model
> shipped at the end of the 6-phase canonical cleanup. The design rationale
> for the cleanup itself lives at
> `docs/superpowers/specs/2026-04-10-data-model-canonical-cleanup-design.md`.

---

# Data Model Refactor Plan

**Date:** 2026-04-06
**Status:** Draft
**Goal:** Replace the current mixed domain/projection/process-heavy model with a smaller, clearer canonical model that is easier to understand, maintain, and evolve.

## 1. Intent

This refactor treats PatchHound as effectively greenfield for persisted data design.

The main design goals are:

- use business names for authoritative entities
- separate domain data from ingestion state and read models
- remove processing-language names like `Normalized*` from canonical entities
- make identity rules explicit and source-safe
- reduce overlapping representations of software and vulnerability state
- preserve authenticated scans as a compatible subsystem while retargeting its ingestion model

## 2. Current Problems

The current model has several structural issues:

- `Asset` mixes device inventory, software inventory, ownership, security profile assignment, and device telemetry in one entity
- software concepts are spread across `NormalizedSoftware`, `TenantSoftware`, `NormalizedSoftwareInstallation`, `DeviceSoftwareInstallation`, `SoftwareVulnerabilityMatch`, and `NormalizedSoftwareVulnerabilityProjection`
- vulnerability concepts are spread across `VulnerabilityDefinition`, `TenantVulnerability`, `VulnerabilityAsset`, `VulnerabilityAssetEpisode`, `VulnerabilityAssetAssessment`, and `VulnerabilityEpisodeRiskAssessment`
- core operational tables use nullable `SnapshotId` columns to represent “current” state, which makes uniqueness and intent less clear
- some persisted entities are really derived projections but are modeled like primary domain objects
- naming reflects implementation steps rather than domain meaning

## 3. Naming Rules

These rules apply to the target model.

### 3.1 Authoritative entities

Authoritative entities use business names only.

Examples:

- `SoftwareProduct`
- `InstalledSoftware`
- `Vulnerability`
- `DeviceVulnerabilityExposure`

Do not use:

- `Normalized*`
- `Projected*`
- `Matched*`
- `Derived*`

### 3.2 Process and staging entities

Process entities may use operational language when they are not domain truths.

Examples:

- `ObservationRun`
- `RawAssetObservation`
- `ScanJob`
- `StagedDetectedSoftware`

### 3.3 Read-side models

Read-side tables must be named explicitly as summaries, scores, or snapshots.

Examples:

- `SoftwareVulnerabilitySummary`
- `DeviceRiskScore`
- `TenantRiskScoreSnapshot`

## 4. Target Model Categories

The target system divides persisted models into four categories.

### 4.1 Authoritative domain data

- `Tenant`
- `User`
- `Team`
- `TeamMember`
- `Device`
- `DeviceTag`
- `BusinessLabel`
- `DeviceBusinessLabel`
- `SecurityProfile`
- `SoftwareProduct`
- `SoftwareAlias`
- `InstalledSoftware`
- `InstalledSoftwareEpisode` if lifecycle history is needed
- `Vulnerability`
- `VulnerabilityReference`
- `VulnerabilityApplicability`
- `ThreatAssessment`
- `DeviceVulnerabilityExposure`
- `ExposureEpisode`
- `ExposureAssessment`
- `VulnerabilitySeverityOverride`
- `RiskAcceptance`
- `RemediationCase`
- `RemediationDecision`
- `ApprovalTask`
- `Comment`

### 4.2 Operational and configuration data

- `TenantSlaConfiguration`
- `TenantAiProfile`
- `ConnectionProfile`
- `ScanningTool`
- `ScanningToolVersion`
- `ScanProfile`
- `ScanProfileTool`
- `ScanRunner`
- `DeviceScanProfileAssignment`
- `AuthenticatedScanRun`
- `ScanJob`
- `ScanJobResult`
- `ScanJobValidationIssue`
- `WorkflowDefinition`
- `WorkflowInstance`
- `WorkflowNodeExecution`
- `WorkflowAction`
- `EnrichmentSourceConfiguration`
- `TenantSourceConfiguration`
- `EnrichmentJob`
- `EnrichmentRun`
- `AuditLogEntry`
- `Notification`

### 4.3 Ingestion and history

- `SourceSystem`
- `ObservationRun`
- `IngestionCheckpoint`
- `RawAssetObservation`
- `RawSoftwareObservation`
- `RawVulnerabilityObservation`
- `RawExposureObservation`
- `StagedDetectedSoftware` if authenticated scans needs source-isolated staging

### 4.4 Explicit read-side models

Only keep these when needed for performance or UX:

- `DeviceRiskScore`
- `SoftwareRiskScore`
- `TeamRiskScore`
- `DeviceGroupRiskScore`
- `TenantRiskScoreSnapshot`
- `SoftwareVulnerabilitySummary`
- `TenantSoftwareSummary`

These are not aggregate roots.

## 5. Canonical Domain Model

### 5.1 Device

`Device` is the authoritative model for an endpoint, server, or host inside a tenant.

It owns:

- source-aware identity
- ownership
- criticality
- security profile assignment
- device telemetry

It does not represent software.

Identity rule:

- unique on `(TenantId, SourceSystemId, ExternalId)`

### 5.2 SoftwareProduct

`SoftwareProduct` is the canonical software identity shared across tenants and sources.

It holds:

- canonical vendor
- canonical product name
- canonical product key
- optional primary CPE
- optional category
- optional enrichment data such as end-of-life and supply chain insight

It replaces `NormalizedSoftware`.

### 5.3 SoftwareAlias

`SoftwareAlias` captures alternate source identifiers or names that map to a `SoftwareProduct`.

It replaces `NormalizedSoftwareAlias`.

Example uses:

- source-specific software IDs
- vendor/product spellings
- product slugs

### 5.4 InstalledSoftware

`InstalledSoftware` is the core fact that a `Device` has a given `SoftwareProduct` installed, optionally at a detected version.

It holds:

- `DeviceId`
- `SoftwareProductId`
- `DetectedVersion`
- `SourceSystemId`
- `FirstSeenAt`
- `LastSeenAt`
- `RemovedAt`

It replaces the current overlap between:

- `TenantSoftware`
- `NormalizedSoftwareInstallation`
- `DeviceSoftwareInstallation`

Recommended uniqueness:

- current-state uniqueness on `(DeviceId, SoftwareProductId, DetectedVersion, SourceSystemId)`

If reinstall/removal episodes matter, keep `InstalledSoftwareEpisode` as explicit lifecycle history.

### 5.5 Vulnerability

`Vulnerability` is the global advisory or CVE record.

It holds:

- external ID
- source
- title
- description
- severity and CVSS data
- publication date

It replaces `VulnerabilityDefinition`.

### 5.6 VulnerabilityApplicability

`VulnerabilityApplicability` describes when a `Vulnerability` applies to a software identity or version range.

It holds:

- vulnerability FK
- affected criteria
- version range semantics
- vulnerable flag

It replaces `VulnerabilityDefinitionAffectedSoftware`.

### 5.7 ThreatAssessment

`ThreatAssessment` stores global threat facts for a vulnerability.

It holds:

- threat score
- exploit likelihood
- EPSS
- known exploited flags
- malware/ransomware associations

It replaces `VulnerabilityThreatAssessment`.

### 5.8 DeviceVulnerabilityExposure

`DeviceVulnerabilityExposure` is the current tenant/device-level exposure state for a vulnerability.

It holds:

- `DeviceId`
- `VulnerabilityId`
- `SourceSystemId`
- `DetectedAt`
- `ResolvedAt`
- `Status`
- detection evidence

It replaces `VulnerabilityAsset`.

Recommended uniqueness:

- source-owned current-state uniqueness on `(DeviceId, VulnerabilityId, SourceSystemId)`

### 5.9 ExposureEpisode

`ExposureEpisode` tracks reopen/resolve history for an exposure.

It holds:

- parent exposure
- episode number
- first seen
- last seen
- resolved at

It replaces `VulnerabilityAssetEpisode`.

### 5.10 ExposureAssessment

`ExposureAssessment` stores contextual scoring and severity for an exposure or active episode.

It holds:

- base severity
- effective severity
- reason and factors
- calculation version

It replaces `VulnerabilityAssetAssessment`.

`VulnerabilityEpisodeRiskAssessment` should be folded into this unless there is a strong reason to keep a separate risk-scoring table.

### 5.11 VulnerabilitySeverityOverride

`VulnerabilitySeverityOverride` replaces `OrganizationalSeverity`.

It is a human or policy-driven override of vulnerability severity within a tenant context.

### 5.12 RemediationCase

`RemediationCase` is the stable business object for remediation work.

It replaces `RemediationWorkflow` as the aggregate root.

Recommended scope:

- tenant-scoped
- anchored to `SoftwareProduct`
- optional version scope if needed

Workflow, approval, decisions, and notes attach to the case.

## 6. Keep / Rename / Remove Inventory

| Current name | Proposed name | Action | Reason |
|---|---|---|---|
| `Tenant` | `Tenant` | Keep | Clear domain root. |
| `User` | `User` | Keep | Clear domain concept. |
| `Team` | `Team` | Keep | Clear domain concept. |
| `TeamMember` | `TeamMember` | Keep | Valid relationship entity. |
| `TeamMembershipRule` | `TeamMembershipRule` | Keep | Valid routing rule. |
| `Asset` | `Device` | Replace | Remove mixed device/software abstraction. |
| `AssetTag` | `DeviceTag` | Rename | Align tags with device model. |
| `AssetBusinessLabel` | `DeviceBusinessLabel` | Rename | Align labels with device model. |
| `AssetSecurityProfile` | `SecurityProfile` | Rename | Cleaner business term. |
| `AssetRule` | `DeviceRule` | Rename | Align rule target with device model. |
| `BusinessLabel` | `BusinessLabel` | Keep | Good business term. |
| `NormalizedSoftware` | `SoftwareProduct` | Rename | Canonical software identity. |
| `NormalizedSoftwareAlias` | `SoftwareAlias` | Rename | Alias is domain language; normalized is not. |
| `TenantSoftware` | `TenantSoftwareSummary` | Demote or remove | Derived tenant-level rollup, not domain root. |
| `NormalizedSoftwareInstallation` | `InstalledSoftware` | Replace | Core fact should be software installed on device. |
| `DeviceSoftwareInstallation` | Remove or fold into `InstalledSoftware` | Remove | Overlaps with installation concept. |
| `DeviceSoftwareInstallationEpisode` | `InstalledSoftwareEpisode` | Keep only if needed | Explicit installation history. |
| `SoftwareCpeBinding` | `SoftwareProductCpeBinding` or fold into `SoftwareProduct` | Simplify | Keep only if 1:N binding is needed. |
| `SoftwareVulnerabilityMatch` | `SoftwareVulnerabilityMatchEvidence` or remove | Demote or remove | Correlation evidence, not primary domain. |
| `NormalizedSoftwareVulnerabilityProjection` | `SoftwareVulnerabilitySummary` | Demote | Explicit read model only. |
| `VulnerabilityDefinition` | `Vulnerability` | Rename | Simpler business term. |
| `VulnerabilityDefinitionReference` | `VulnerabilityReference` | Rename | Simpler business term. |
| `VulnerabilityDefinitionAffectedSoftware` | `VulnerabilityApplicability` | Rename | Better expresses meaning. |
| `VulnerabilityThreatAssessment` | `ThreatAssessment` | Rename | Shorter and clear. |
| `TenantVulnerability` | Remove or `TenantVulnerabilitySummary` | Remove as core entity | Exposure model should carry tenant scope. |
| `VulnerabilityAsset` | `DeviceVulnerabilityExposure` | Rename | Correct business meaning. |
| `VulnerabilityAssetEpisode` | `ExposureEpisode` | Rename | Better lifecycle term. |
| `VulnerabilityAssetAssessment` | `ExposureAssessment` | Rename | Better scope term. |
| `VulnerabilityEpisodeRiskAssessment` | Merge into `ExposureAssessment` or `ExposureRiskScore` | Simplify | Too overlapping today. |
| `OrganizationalSeverity` | `VulnerabilitySeverityOverride` | Rename | Explicit meaning. |
| `RiskAcceptance` | `RiskAcceptance` | Keep | Clear concept. |
| `RemediationWorkflow` | `RemediationCase` | Replace | Case is the stable aggregate. |
| `RemediationWorkflowStageRecord` | `RemediationStageRecord` | Rename if retained | Clearer process naming. |
| `RemediationDecision` | `RemediationDecision` | Keep | Good concept if anchored to case. |
| `RemediationDecisionVulnerabilityOverride` | `RemediationDecisionExposureOverride` | Rename or simplify | Must align with exposure model. |
| `AnalystRecommendation` | `AnalystRecommendation` | Keep | Clear concept. |
| `PatchingTask` | `PatchingTask` | Keep | Valid operational concept. |
| `ApprovalTask` | `ApprovalTask` | Keep | Valid operational concept. |
| `ApprovalTaskVisibleRole` | `ApprovalTaskVisibleRole` | Keep | Support table. |
| `Comment` | `Comment` | Keep | Fine if target strategy is clean. |
| `Notification` | `Notification` | Keep | Clear operational concept. |
| `AIReport` | `AiReport` | Keep | Operational/reporting concept. |
| `SoftwareDescriptionJob` | `SoftwareDescriptionJob` | Keep | Operational job. |
| `RemediationAiJob` | `RemediationAiJob` | Keep | Operational job. |
| `TenantRiskScoreSnapshot` | `TenantRiskScoreSnapshot` | Keep | Explicit snapshot/read model. |
| `AssetRiskScore` | `DeviceRiskScore` | Rename | Align with device model. |
| `TenantSoftwareRiskScore` | `SoftwareRiskScore` | Rename | Remove old software naming. |
| `TeamRiskScore` | `TeamRiskScore` | Keep | Clear read model. |
| `DeviceGroupRiskScore` | `DeviceGroupRiskScore` | Keep | Clear read model. |
| `StagedAsset` | `RawAssetObservation` | Rename | Make stage/process role explicit. |
| `StagedDeviceSoftwareInstallation` | `RawInstallationObservation` | Rename | Make stage/process role explicit. |
| `StagedVulnerability` | `RawVulnerabilityObservation` | Rename | Make stage/process role explicit. |
| `StagedVulnerabilityExposure` | `RawExposureObservation` | Rename | Make stage/process role explicit. |
| `IngestionRun` | `ObservationRun` | Rename | Easier to understand. |
| `IngestionSnapshot` | Remove or fold into history | Remove | Snapshot should not leak into core truth. |
| `IngestionCheckpoint` | `IngestionCheckpoint` | Keep | Useful operational state. |
| `EnrichmentJob` | `EnrichmentJob` | Keep | Operational state. |
| `EnrichmentRun` | `EnrichmentRun` | Keep | Operational state. |
| `EnrichmentSourceConfiguration` | `EnrichmentSourceConfiguration` | Keep | Operational config. |
| `TenantSourceConfiguration` | `TenantSourceConfiguration` | Keep | Operational config. |
| `TenantSlaConfiguration` | `TenantSlaConfiguration` | Keep | Tenant config. |
| `TenantAiProfile` | `TenantAiProfile` | Keep | Tenant config. |
| `ConnectionProfile` | `ConnectionProfile` | Keep | Authenticated-scan config. |
| `ScanningTool` | `ScanningTool` | Keep | Authenticated-scan config. |
| `ScanningToolVersion` | `ScanningToolVersion` | Keep | Authenticated-scan config history. |
| `ScanProfile` | `ScanProfile` | Keep | Authenticated-scan orchestration. |
| `ScanProfileTool` | `ScanProfileTool` | Keep | Join table. |
| `ScanRunner` | `ScanRunner` | Keep | Operational concept. |
| `AssetScanProfileAssignment` | `DeviceScanProfileAssignment` | Rename | Align with device model. |
| `AuthenticatedScanRun` | `AuthenticatedScanRun` | Keep | Operational run. |
| `ScanJob` | `ScanJob` | Keep | Operational unit of work. |
| `ScanJobResult` | `ScanJobResult` | Keep | Operational result. |
| `ScanJobValidationIssue` | `ScanJobValidationIssue` | Keep | Validation record. |
| `StagedAuthenticatedScanSoftware` | `StagedDetectedSoftware` | Rename | Process-stage table; remove normalized wording. |
| `WorkflowDefinition` | `WorkflowDefinition` | Keep | Generic workflow engine concept. |
| `WorkflowInstance` | `WorkflowInstance` | Keep | Generic workflow engine concept. |
| `WorkflowNodeExecution` | `WorkflowNodeExecution` | Keep | Generic workflow engine concept. |
| `WorkflowAction` | `WorkflowAction` | Keep | Generic workflow engine concept. |
| `AuditLogEntry` | `AuditLogEntry` | Keep | Clear operational concept. |
| `SentinelConnectorConfiguration` | `SentinelConnectorConfiguration` | Keep | Integration config. |
| `AdvancedTool` | `AdvancedTool` | Keep | Separate feature/config concept. |

### 6.1 Milestone 1 Mapping Note

Schema mapping:

- `Asset` -> `Device` for authenticated-scan targets and canonical inventory foundation.
- `NormalizedSoftware` -> `SoftwareProduct` for canonical software identity.
- `NormalizedSoftwareAlias` -> `SoftwareAlias` for source-specific observed software names and IDs.
- `NormalizedSoftwareInstallation` -> `InstalledSoftware` for device software presence.
- `AssetScanProfileAssignment` -> `DeviceScanProfileAssignment` for scan targeting.
- `StagedAuthenticatedScanSoftware` -> `StagedDetectedSoftware` for scan-stage detected software rows.

Service mapping:

- authenticated scan ingestion now writes `SoftwareProduct`, `SoftwareAlias`, and `InstalledSoftware`.
- authenticated scan dispatch now reads `DeviceScanProfileAssignment` and creates `ScanJob.DeviceId`.
- old remediation, risk, enrichment, and software API services still depend on `TenantSoftware` and `NormalizedSoftware*` until the next vertical refactor.

DTO/API mapping:

- scan runner job payload uses `deviceId`, not `assetId`.
- scan run details use `DeviceId` and `DeviceName`, not `AssetId` and `AssetName`.
- scan tool output model is `DetectedSoftware`, not `NormalizedSoftware`.

Docs mapping:

- authenticated scan docs now describe scanners as emitting `DetectedSoftware`.
- canonical storage docs describe persistence into `SoftwareProduct` and `InstalledSoftware`.

## 7. Authenticated Scans Impact

The authenticated scans work is mostly compatible with this refactor.

### 7.1 What stays the same

These entities and flows remain valid:

- `ConnectionProfile`
- `ScanningTool`
- `ScanningToolVersion`
- `ScanProfile`
- `ScanProfileTool`
- `ScanRunner`
- `AuthenticatedScanRun`
- `ScanJob`
- `ScanJobResult`
- `ScanJobValidationIssue`

These are operational/configuration entities and should not be blocked by the domain refactor.

### 7.2 What must change

The authenticated scans spec currently ties ingestion to the old inventory model.

The following changes are required:

- stop using `NormalizedSoftware` as the named output model
- stop targeting `NormalizedSoftwareInstallation`
- stop relying on `TenantSoftwareId`
- stop using `AssetId` for scan-profile assignment and scan jobs if `Asset` is replaced by `Device`

### 7.3 New authenticated-scan output contract

The output model name should become:

- `DetectedSoftware`

Each valid entry should map to:

- `SoftwareProduct`
- `InstalledSoftware`

Recommended conceptual JSON shape:

- canonical product identity fields
- optional detected version
- optional vendor
- optional category
- optional primary CPE

The runner still detects software.
The server persists canonical `SoftwareProduct` and `InstalledSoftware`.

### 7.4 Authenticated scan entity renames

- `AssetScanProfileAssignment` -> `DeviceScanProfileAssignment`
- `ScanJob.AssetId` -> `ScanJob.DeviceId`
- `StagedAuthenticatedScanSoftware` -> `StagedDetectedSoftware`

## 8. Identity Rules

Identity must be explicit and source-safe.

### 8.1 Source-derived entities

Whenever an upstream source provides IDs that are only source-local, uniqueness must include source.

Examples:

- `Device`: `(TenantId, SourceSystemId, ExternalId)`
- raw observations: `(ObservationRunId, SourceSystemId, ExternalId)` or equivalent

Do not make `SourceKey` or `SourceSystemId` descriptive-only metadata if it is part of identity.

### 8.2 Canonical entities

Canonical entities use business keys.

Example:

- `SoftwareProduct`: unique on `CanonicalProductKey`

### 8.3 Current-state entities

Current-state uniqueness must not rely on nullable fields like `SnapshotId = null`.

Use:

- explicit current-row keys
- partial indexes if necessary
- or separate history tables

Do not use nullable snapshot columns to stand in for “active now”.

## 9. Data Flow After Refactor

### 9.1 Inventory flow

1. source observes devices and software
2. raw observations land in ingestion/history tables
3. merge resolves canonical `SoftwareProduct`
4. merge upserts `Device`
5. merge upserts `InstalledSoftware`
6. optional read models refresh

### 9.2 Vulnerability flow

1. vulnerability sources upsert `Vulnerability`
2. affected software rules upsert `VulnerabilityApplicability`
3. threat intelligence updates `ThreatAssessment`
4. matching logic derives `DeviceVulnerabilityExposure`
5. lifecycle updates `ExposureEpisode`
6. risk logic updates `ExposureAssessment`
7. optional summaries refresh

### 9.3 Remediation flow

1. open exposures and software context contribute to a `RemediationCase`
2. decisions, approvals, and workflow state attach to the case
3. read models and dashboards summarize the case state

## 10. Migration Strategy

This plan assumes the old data model does not need to be preserved.

### 10.1 Phase 1: introduce new canonical schema

Add:

- `SourceSystem`
- `Device`
- `SoftwareProduct`
- `SoftwareAlias`
- `InstalledSoftware`
- `Vulnerability`
- `VulnerabilityReference`
- `VulnerabilityApplicability`
- `ThreatAssessment`
- `DeviceVulnerabilityExposure`
- `ExposureEpisode`
- `ExposureAssessment`
- `VulnerabilitySeverityOverride`
- `RemediationCase`

Keep current operational/authenticated-scan entities for now.

### 10.2 Phase 2: rewrite inventory ingestion

Rewrite ingestion to populate:

- `Device`
- `SoftwareProduct`
- `InstalledSoftware`

Stop writing to:

- `Asset` for software inventory
- `TenantSoftware`
- `NormalizedSoftwareInstallation`
- `DeviceSoftwareInstallation`

### 10.3 Phase 3: rewrite vulnerability matching

Rewrite correlation logic to produce:

- `DeviceVulnerabilityExposure`
- `ExposureEpisode`
- `ExposureAssessment`

Stop depending on:

- `TenantVulnerability`
- `VulnerabilityAsset`
- `VulnerabilityAssetEpisode`
- `VulnerabilityAssetAssessment`
- `VulnerabilityEpisodeRiskAssessment`

### 10.4 Phase 4: re-anchor remediation

Introduce `RemediationCase` as the stable remediation aggregate.

Move approvals, decisions, and workflow/process references to the case.

### 10.5 Phase 5: rebuild read models

Recreate only the read-side tables that are justified by performance or UX.

Any retained summary table must be:

- explicitly named as a summary/score/snapshot
- rebuildable from authoritative data

### 10.6 Phase 6: remove obsolete schema

Delete or retire:

- `Asset` if fully replaced
- `Normalized*`
- `TenantSoftware`
- `DeviceSoftwareInstallation`
- old vulnerability exposure/assessment chain
- any services that rebuild obsolete projections

## 11. Verification Gates

The refactor is not complete until these gates pass.

### 11.1 Model clarity

- every persisted entity fits one of the four model categories
- every authoritative entity can be explained in one sentence
- no authoritative entity uses `Normalized*` or similar process wording

### 11.2 Constraints

- source-derived identities include source in unique keys
- current-state uniqueness does not depend on nullable snapshot columns
- one installation fact model exists
- one current exposure model exists

### 11.3 Query behavior

Representative queries must be straightforward:

- list devices
- list software installed on a device
- list devices affected by a vulnerability
- list vulnerabilities affecting a software product
- list open remediation cases

### 11.4 Test coverage

Required coverage:

- ingestion idempotency
- source collision protection
- software canonicalization determinism
- vulnerability applicability version matching
- exposure open/reopen/resolve lifecycle
- authenticated-scan ingestion into `SoftwareProduct` and `InstalledSoftware`
- remediation case creation and stability

## 12. Recommended Execution Order

1. finalize target vocabulary and entity names
2. implement `SourceSystem`, `Device`, `SoftwareProduct`, `SoftwareAlias`, `InstalledSoftware`
3. retarget authenticated-scan ingestion to `DetectedSoftware` -> `SoftwareProduct` + `InstalledSoftware`
4. rewrite general inventory ingestion to same target model
5. implement `Vulnerability`, `VulnerabilityApplicability`, `ThreatAssessment`
6. rewrite exposure matching to `DeviceVulnerabilityExposure`, `ExposureEpisode`, `ExposureAssessment`
7. introduce `RemediationCase`
8. rebuild only necessary read models
9. remove obsolete tables and old services

## 13. Non-Goals

This refactor does not by itself redesign:

- runner transport protocol
- tenant authorization model
- AI provider strategy
- generic workflow engine internals

Those may evolve later, but they are not prerequisites for the canonical data model.

## 14. Implementation Backlog

This backlog is ordered to reduce churn and keep authenticated scans moving while the canonical model is introduced.

### 14.1 Track A: vocabulary and architecture baseline

**A1. Freeze target names**

Deliverables:

- approve the target authoritative entity names in this document
- approve the naming rule that `Normalized*` must not appear in canonical entities
- approve the naming rule that summaries and projections must say `Summary`, `Score`, or `Snapshot`

Completion criteria:

- this document is the single source of truth for target names
- new design docs and code use only target names

**A2. Create a replacement map**

Deliverables:

- old-to-new table mapping
- old-to-new service mapping
- old-to-new API/DTO mapping

Completion criteria:

- every existing persisted entity is accounted for
- every deleted entity has a replacement or explicit removal rationale

### 14.2 Track B: schema foundation

**B1. Introduce source-aware identity foundation**

Deliverables:

- `SourceSystem` entity
- source-aware FK usage in new schema
- unique constraints for source-derived identity

Completion criteria:

- new entities use `(TenantId, SourceSystemId, ExternalId)` where applicable
- no new table treats source as descriptive-only metadata

**B2. Introduce `Device`**

Deliverables:

- new `Device` entity and EF configuration
- source-aware identity constraints
- ownership, criticality, telemetry fields moved from `Asset`

Completion criteria:

- canonical device model exists
- no new code writes device data into `Asset`

**B3. Introduce `SoftwareProduct` and `SoftwareAlias`**

Deliverables:

- new `SoftwareProduct` entity
- new `SoftwareAlias` entity
- CPE strategy decision:
  either fold binding into `SoftwareProduct`
  or create `SoftwareProductCpeBinding`

Completion criteria:

- canonical software identity exists without `Normalized*`
- alias mapping strategy is explicit

**B4. Introduce `InstalledSoftware`**

Deliverables:

- new `InstalledSoftware` entity and EF configuration
- optional `InstalledSoftwareEpisode` if install lifecycle history is needed

Completion criteria:

- canonical installation fact exists
- uniqueness is based on device/product/version/source
- no nullable snapshot column is required for current state

### 14.3 Track C: authenticated scans retargeting

**C1. Revise authenticated scans spec**

Deliverables:

- update `docs/superpowers/specs/2026-04-05-authenticated-scans-design.md`
- replace `NormalizedSoftware` wording with `DetectedSoftware`
- replace `AssetId` references with `DeviceId` where the target is a device
- replace ingestion references to `NormalizedSoftwareInstallation` and `TenantSoftwareId`

Completion criteria:

- authenticated scans design doc aligns with this refactor plan
- no new spec text references canonical `Normalized*` entities

**C2. Update authenticated scan output contract**

Deliverables:

- output model name changed to `DetectedSoftware`
- validation contract updated accordingly
- UI wording updated in scan tool editor

Completion criteria:

- scan tool schema viewer shows `DetectedSoftware`
- runner/API contract no longer leaks old canonical naming

**C3. Retarget authenticated scan ingestion**

Deliverables:

- authenticated scan ingestion writes to `SoftwareProduct`
- authenticated scan ingestion writes to `InstalledSoftware`
- `StagedAuthenticatedScanSoftware` renamed to `StagedDetectedSoftware` or replaced by `RawSoftwareObservation`

Completion criteria:

- authenticated scans no longer depend on `TenantSoftwareId`
- authenticated scans no longer write `NormalizedSoftwareInstallation`

**C4. Rename scan assignment scope**

Deliverables:

- `AssetScanProfileAssignment` replaced by `DeviceScanProfileAssignment`
- `ScanJob.AssetId` replaced by `DeviceId`

Completion criteria:

- authenticated scan execution targets canonical devices

### 14.4 Track D: general inventory ingestion rewrite

**D1. Introduce raw observation schema**

Deliverables:

- `ObservationRun`
- `RawAssetObservation`
- `RawSoftwareObservation`
- retention strategy

Completion criteria:

- ingestion bookkeeping is separated from canonical domain state

**D2. Rewrite device merge**

Deliverables:

- merge service that upserts `Device`
- source precedence rules documented and tested

Completion criteria:

- device inventory no longer requires `Asset`

**D3. Rewrite software merge**

Deliverables:

- canonicalization flow into `SoftwareProduct`
- alias creation/update into `SoftwareAlias`
- installation upsert into `InstalledSoftware`

Completion criteria:

- current inventory is readable from `Device` + `InstalledSoftware`
- old software projection chain is not on the write path

### 14.5 Track E: vulnerability model rewrite

**E1. Introduce `Vulnerability`, `VulnerabilityReference`, `VulnerabilityApplicability`, `ThreatAssessment`**

Deliverables:

- new vulnerability entities and EF configurations
- migration/backfill or clean cutover strategy

Completion criteria:

- global vulnerability knowledge is independent of tenant exposure state

**E2. Implement exposure model**

Deliverables:

- `DeviceVulnerabilityExposure`
- `ExposureEpisode`
- `ExposureAssessment`
- optional `VulnerabilitySeverityOverride`

Completion criteria:

- one current exposure concept exists
- one history concept exists
- one contextual assessment concept exists

**E3. Rewrite matching logic**

Deliverables:

- vulnerability applicability matching from `InstalledSoftware`
- exposure creation and resolution logic
- episode open/reopen/resolve lifecycle logic

Completion criteria:

- exposure generation does not depend on old `TenantVulnerability` chain

### 14.6 Track F: remediation re-anchoring

**F1. Introduce `RemediationCase`**

Deliverables:

- new remediation aggregate root
- explicit scope definition

Recommended first scope:

- tenant + `SoftwareProduct`
- optional version scope if required

Completion criteria:

- remediation has one stable aggregate root

**F2. Re-anchor workflow and approvals**

Deliverables:

- `ApprovalTask` references `RemediationCase`
- `RemediationDecision` references `RemediationCase`
- workflow/process records attach to the case

Completion criteria:

- no remediation process depends on transient summary/projection rows

### 14.7 Track G: read model rebuild

**G1. Define required read models**

Deliverables:

- decision list of which summaries remain
- deletion list for summaries to remove

Completion criteria:

- every retained read model has a concrete consumer

**G2. Rebuild summary tables**

Deliverables:

- `DeviceRiskScore`
- `SoftwareRiskScore`
- `TeamRiskScore`
- `DeviceGroupRiskScore`
- `TenantRiskScoreSnapshot`
- `SoftwareVulnerabilitySummary`
- optional `TenantSoftwareSummary`

Completion criteria:

- all retained summary tables are rebuildable from canonical entities

### 14.8 Track H: API and DTO alignment

**H1. Introduce new internal query models**

Deliverables:

- query services over `Device`, `InstalledSoftware`, `DeviceVulnerabilityExposure`, `RemediationCase`

Completion criteria:

- core API queries no longer depend on obsolete entities

**H2. Rename API models and controller semantics**

Deliverables:

- replace software terminology that still says `Normalized`
- replace scan assignment terminology that still says `Asset` when it means device
- align vulnerability DTOs with exposure naming

Completion criteria:

- user-facing API language matches canonical model

### 14.9 Track I: frontend alignment

**I1. Rename user-facing vocabulary**

Deliverables:

- device pages use `Device`
- software pages use `SoftwareProduct` or user-facing equivalent like `Software`
- vulnerability pages use `Exposure` where device-level exposure is intended
- authenticated scan UI uses `DetectedSoftware`

Completion criteria:

- frontend language does not leak old implementation terms

**I2. Update data flows**

Deliverables:

- frontend queries against new API/DTO shapes
- detail pages and tables updated to new summary/read models

Completion criteria:

- key views work without old entities

### 14.10 Track J: cleanup and deletion

**J1. Remove obsolete schema and services**

Deliverables:

- delete old tables and configurations
- remove obsolete services and projections
- remove old migrations if a clean-baseline reset is chosen

Completion criteria:

- no active code path writes obsolete canonical entities
- no obsolete entities remain in `PatchHoundDbContext`

**J2. Documentation cleanup**

Deliverables:

- update all docs that reference `Normalized*`
- update diagrams and onboarding material

Completion criteria:

- docs match code and canonical terminology

## 15. Suggested Milestones

### Milestone 1: canonical inventory foundation

Scope:

- Track A
- Track B
- Track C
- Track D

Outcome:

- canonical `Device`, `SoftwareProduct`, and `InstalledSoftware` exist
- authenticated scans targets the new inventory model

### Milestone 2: canonical exposure model

Scope:

- Track E

Outcome:

- vulnerability and exposure state no longer depend on the old tenant-vulnerability chain

### Milestone 3: remediation re-anchor

Scope:

- Track F

Outcome:

- remediation process is attached to `RemediationCase`

### Milestone 4: query/read-side cutover

Scope:

- Track G
- Track H
- Track I

Outcome:

- main API and UI run on the new canonical model and explicit summaries

### Milestone 5: old model removal

Scope:

- Track J

Outcome:

- obsolete entities and services are deleted

## 16. Recommended First Implementation Slice

The first slice should be small but strategically correct.

Recommended first slice:

1. add `SourceSystem`
2. add `Device`
3. add `SoftwareProduct`
4. add `SoftwareAlias`
5. add `InstalledSoftware`
6. revise authenticated scans doc and output naming to `DetectedSoftware`
7. retarget authenticated scan ingestion to `SoftwareProduct` + `InstalledSoftware`

Why this first:

- it stops new work from deepening the old `Normalized*` model
- it gives authenticated scans a future-proof landing zone
- it creates the canonical inventory basis needed by every later phase

## 17. Milestone 1 Checklist

Milestone 1 goal:

- establish canonical inventory foundations
- retarget authenticated scans to the new inventory model
- stop adding new dependencies on `Normalized*` entities

### 17.1 Planning and naming

- [x] approve this document as the canonical refactor reference
- [x] approve target naming for `Device`, `SoftwareProduct`, `SoftwareAlias`, `InstalledSoftware`
- [x] approve that canonical entities must not use `Normalized*`
- [x] approve the authenticated-scans output model rename from `NormalizedSoftware` to `DetectedSoftware`
- [x] produce a short old-to-new mapping note for schema, services, DTOs, and docs

### 17.2 Schema: source and inventory foundation

- [x] add `SourceSystem` entity
- [x] add EF configuration for `SourceSystem`
- [x] decide whether `SourceSystem` is enum-backed, seeded rows, or fully data-driven
- [x] add `Device` entity
- [x] add EF configuration for `Device`
- [x] define `Device` unique constraint on `(TenantId, SourceSystemId, ExternalId)`
- [x] move device-specific fields from `Asset` design into `Device`
- [x] add `SoftwareProduct` entity
- [x] add EF configuration for `SoftwareProduct`
- [x] define `SoftwareProduct` unique constraint on `CanonicalProductKey`
- [x] add `SoftwareAlias` entity
- [x] add EF configuration for `SoftwareAlias`
- [x] define alias uniqueness strategy
- [x] decide whether CPE is stored directly on `SoftwareProduct` or in a separate binding table
- [x] add `InstalledSoftware` entity
- [x] add EF configuration for `InstalledSoftware`
- [x] define current-state uniqueness for `InstalledSoftware`
- [x] ensure `InstalledSoftware` uniqueness does not depend on nullable version values
- [x] decide whether `InstalledSoftwareEpisode` is needed in Milestone 1 or deferred

### 17.3 Authenticated scans spec and contract

- [x] update `docs/superpowers/specs/2026-04-05-authenticated-scans-design.md`
- [x] replace canonical output model wording with `DetectedSoftware`
- [x] replace references to `NormalizedSoftwareInstallation`
- [x] replace references to `TenantSoftwareId`
- [x] replace `AssetScanProfileAssignment` wording with `DeviceScanProfileAssignment`
- [x] replace `ScanJob.AssetId` wording with `DeviceId`
- [x] update sample JSON output contract to use `DetectedSoftware` wording
- [x] update scan tool editor copy and schema help text

### 17.4 Authenticated scans schema alignment

- [x] rename or replace `AssetScanProfileAssignment` with `DeviceScanProfileAssignment`
- [x] update EF configuration and foreign keys for the assignment entity
- [x] update `ScanJob` to target `DeviceId`
- [x] add EF foreign keys from authenticated-scan assignments and jobs to `Device`
- [x] update scan-runner payloads from `AssetId` to `DeviceId`
- [x] update scan-run summaries from `AssetName` to `DeviceName`
- [x] verify `AuthenticatedScanRun`, `ScanJobResult`, and `ScanJobValidationIssue` need no canonical-model changes
- [x] rename `StagedAuthenticatedScanSoftware` to `StagedDetectedSoftware` or replace it with a new raw software observation table

### 17.5 Authenticated scans ingestion rewrite

- [x] define the `DetectedSoftware` ingestion DTO
- [x] update validation rules to map into `SoftwareProduct` and `InstalledSoftware`
- [x] implement software-product resolution during authenticated scan ingestion
- [x] implement software-alias creation/update if required for authenticated scans
- [x] implement installed-software upsert per device
- [x] define source ownership/deactivation behavior for authenticated scan installations
- [x] remove authenticated-scan writes to `TenantSoftware`
- [x] remove authenticated-scan writes to `NormalizedSoftwareInstallation`
- [x] remove authenticated-scan dependence on `TenantSoftwareId`

### 17.6 General inventory service groundwork

- [x] identify which existing services must be split between device inventory and software inventory
- [x] introduce a dedicated canonical inventory write path for `Device`
- [x] introduce a dedicated canonical inventory write path for `SoftwareProduct` and `InstalledSoftware`
- [x] ensure no new code path adds writes to `NormalizedSoftwareInstallation`
- [x] ensure no new code path adds writes to `TenantSoftware` as a canonical dependency
- [x] identify old software inventory chain dependencies still blocking full removal

### 17.7 Query and API groundwork

- [x] identify APIs that will need `Device` rather than `Asset`
- [x] identify APIs that will need `InstalledSoftware` rather than `NormalizedSoftwareInstallation`
- [x] add internal query models for canonical inventory reads
- [x] decide whether any temporary compatibility DTOs are needed during cutover

### 17.8 Tests

- [x] add unit tests for `SoftwareProduct` identity rules
- [x] add unit tests for `SoftwareAlias` matching rules
- [x] add unit tests for `InstalledSoftware` current-state uniqueness semantics
- [x] add unit tests for authenticated-scan validation using `DetectedSoftware`
- [x] add integration test for authenticated scan ingestion into `SoftwareProduct` and `InstalledSoftware`
- [x] add regression coverage for duplicate detected software rows in one scan result
- [x] update authenticated-scan tests to seed canonical `Device` records instead of legacy `Asset` rows
- [x] add integration test proving source-local IDs do not collide across sources
- [x] add integration test for source-owned install deactivation behavior

### 17.9 Documentation and cleanup

- [x] update docs that introduce canonical software naming
- [x] update any current design docs that still describe `NormalizedSoftware` as canonical state
- [x] annotate any deferred old-model dependencies still present after Milestone 1
- [x] add a short “Milestone 1 complete” note to this document when done

Deferred old-model dependencies after the first implementation slice:

- `TenantSoftware`, `NormalizedSoftwareInstallation`, `SoftwareVulnerabilityMatch`, and `NormalizedSoftwareVulnerabilityProjection` still back remediation, risk scoring, enrichment, and several API query services.
- These should be removed in the next vertical refactor around `DeviceVulnerabilityExposure`, `ExposureAssessment`, `SoftwareVulnerabilitySummary`, and `RemediationCase`; deleting them directly in Milestone 1 would cut across too many workflows at once.

Milestone 1 service split notes:

- authenticated scans now has the first canonical inventory write path and writes `Device`-scoped `SoftwareProduct`, `SoftwareAlias`, and `InstalledSoftware` records.
- `IngestionService`, `StagedAssetMergeService`, `NormalizedSoftwareProjectionService`, `NormalizedSoftwareResolver`, and `SoftwareVulnerabilityMatchService` remain on the old Defender/general inventory chain.
- remediation and workflow services still use `TenantSoftware`, `NormalizedSoftwareInstallation`, and software `Asset` rows as their scope until `RemediationCase` exists.
- risk and enrichment services still use old software projections and should move only after the exposure model is introduced.

Milestone 1 API split notes:

- authenticated scan runner, scan run, and scanning tool APIs now use canonical scan language: `DeviceId`, `DeviceName`, and `DetectedSoftware`.
- software, assets, dashboard, risk, approval, remediation, tenant cleanup, and work-notes APIs still expose or query old `Asset`, `TenantSoftware`, and `NormalizedSoftware*` concepts.
- temporary compatibility DTOs will be needed for public software/remediation APIs during the next vertical cut, because the frontend and tests still depend on `TenantSoftwareId` and software asset identifiers.
- new APIs should not introduce additional `NormalizedSoftware*` or `TenantSoftwareId` dependencies.

### 17.10 Milestone 1 exit criteria

- [x] canonical entities `Device`, `SoftwareProduct`, `SoftwareAlias`, and `InstalledSoftware` exist in code
- [x] authenticated scans spec no longer references canonical `Normalized*` entities
- [x] authenticated scans ingestion writes to canonical inventory entities
- [x] no new feature work depends on `TenantSoftwareId` in the authenticated scans flow
- [x] no new feature work introduces additional writes to `NormalizedSoftwareInstallation`
- [x] tests cover the new authenticated-scan-to-canonical-inventory path

Milestone 1 status:

- Complete for the canonical inventory foundation and authenticated-scans retargeting slice.
- Deferred intentionally: replacing the Defender/general ingestion chain, vulnerability exposure model, remediation scope, risk scoring, and broader software APIs. Those belong in the next vertical cut because they are coupled through `TenantSoftware`, `NormalizedSoftwareInstallation`, `SoftwareVulnerabilityMatch`, and `NormalizedSoftwareVulnerabilityProjection`.

## 18. Milestone 2 Checklist

Milestone 2 goal:

- replace the old Defender/general software inventory write path
- introduce the canonical vulnerability applicability and exposure model
- keep remediation and risk operational through temporary compatibility reads until `RemediationCase` is introduced

### 18.1 Scope Guardrails

- [x] do not delete `TenantSoftware`, `NormalizedSoftwareInstallation`, `SoftwareVulnerabilityMatch`, or `NormalizedSoftwareVulnerabilityProjection` until replacement reads exist
- [x] keep public software/remediation API DTOs stable unless a paired frontend change is in the same slice
- [x] keep old remediation workflows functional until `RemediationCase` is available
- [x] regenerate the clean baseline migration only after schema/entity changes are complete for the slice

Note: baseline migration reset completed on 2026-04-07 after the Milestone 2 schema/entity bridge changes. The migration set now contains a single `Initial` migration and a regenerated `PatchHoundDbContextModelSnapshot`; full `dotnet test` passed with 508 tests after regeneration.

Note: the scope guardrails were followed for Milestone 2. Public software/remediation DTOs remain stable, old remediation workflows remain functional through compatibility IDs, and the old `TenantSoftware`, `NormalizedSoftwareInstallation`, `SoftwareVulnerabilityMatch`, and `NormalizedSoftwareVulnerabilityProjection` tables are intentionally retained until the API/frontend cutover and old-model removal milestones.

### 18.2 Canonical Inventory Ingestion

- [x] create a general inventory write service for `Device`
- [x] create a general inventory write service for `SoftwareProduct`, `SoftwareAlias`, and `InstalledSoftware`
- [x] dual-write Defender/general device merge into `Device`
- [x] dual-write Defender/general software merge into `SoftwareProduct` and `InstalledSoftware`
- [ ] remove Defender/general device merge dependence on `Asset`
- [ ] remove Defender/general software merge dependence on `Asset` and `NormalizedSoftware*`
- [x] define source precedence rules for device fields when multiple sources report the same device
- [x] define source ownership rules for installed software deactivation per source
- [x] add tests for same external device ID across two source systems
- [x] add tests for same software product observed from two source systems
- [x] add tests for install deactivation scoped to one source system

Note: Defender/general asset ingestion now dual-writes canonical `Device` and `InstalledSoftware` records through the inventory writer while retaining legacy `Asset` and `DeviceSoftwareInstallation` writes for compatibility. Canonical installed software is reconciled per source so missing rows are deactivated. The legacy path should remain until vulnerability, risk, and remediation reads have moved to canonical device/exposure models.

Note: staged device-software link processing now builds canonical `SoftwareProduct`/`InstalledSoftware` observations from the staged software payload instead of from the legacy software `Asset` row. The legacy `Asset`, `DeviceSoftwareInstallation`, and `DeviceSoftwareInstallationEpisode` writes still remain for compatibility, so the Defender/general software-merge removal checkbox stays open until those legacy writes and consumers are removed together.

Verification: full `dotnet test` passed on 2026-04-07 with 512 tests after decoupling the canonical installed-software upsert from the legacy software `Asset` row in staged link processing.

Source precedence rule: Milestone 2 does not merge fields across source systems. A canonical `Device` is source-owned by `(TenantId, SourceSystemId, ExternalId)`, so the source that owns the row owns its fields. Cross-source device correlation can be added later as a separate model instead of hiding precedence rules inside the device row.

### 18.3 Vulnerability Knowledge

- [x] introduce `Vulnerability` as the replacement for `VulnerabilityDefinition`
- [x] introduce `VulnerabilityReference`
- [x] introduce `VulnerabilityApplicability`
- [x] dual-write canonical vulnerability knowledge from staged vulnerability ingestion
- [x] introduce `ThreatAssessment`
- [x] dual-write canonical threat assessment from existing vulnerability threat assessment calculation
- [x] decide whether old `VulnerabilityDefinitionAffectedSoftware` can be mapped directly to `VulnerabilityApplicability`
- [x] add version-range matching tests against `InstalledSoftware`

Note: the old affected-software shape maps directly to canonical `VulnerabilityApplicability` for the CPE criteria and version-boundary fields. Matching against `InstalledSoftware` now exists for CPE-based product identity and version ranges. Any future non-CPE applicability format should be added as an explicit new matching strategy, not hidden inside the current CPE matcher.

### 18.4 Exposure Model

- [x] introduce `DeviceVulnerabilityExposure`
- [x] introduce `ExposureEpisode`
- [x] introduce `ExposureAssessment`
- [x] dual-write canonical exposure creation alongside direct software-vulnerability match persistence
- [ ] remove direct software-vulnerability match persistence after compatibility reads move
- [x] implement open/reopen/resolve lifecycle against `DeviceVulnerabilityExposure`
- [x] add tests for exposure creation from installed software applicability
- [x] add tests for exposure resolution when installed software is removed
- [x] add tests for exposure reopening when software returns
- [x] add canonical `Device` security-profile assignment behavior for exposure assessment
- [x] add tests for profile-aware `ExposureAssessment` calculation on canonical exposures

Note: staged vulnerability ingestion now dual-writes source-owned canonical `DeviceVulnerabilityExposure` and `ExposureEpisode` rows from observed affected assets. Missing canonical exposures are reconciled per source and resolve after the same two-miss rule as the legacy `VulnerabilityAssetEpisode` path. Reopening creates a new exposure episode.

Note: `CanonicalExposureSyncService` now derives canonical exposures from active `InstalledSoftware` plus CPE-based `VulnerabilityApplicability`, using the product primary CPE when available and an exact normalized vendor/product fallback when it is not. It uses a dedicated `patchhound-canonical-matcher` source system so software-derived exposure lifecycle does not accidentally resolve exposures that are still directly reported by a scanner. The old `SoftwareVulnerabilityMatch` and `NormalizedSoftwareVulnerabilityProjection` path remains as a compatibility read/write path until remediation, risk, and API reads move to canonical exposures.

Note: `CanonicalExposureAssessmentService` now calculates `ExposureAssessment` for open canonical exposures using the same environmental severity calculator as the legacy `VulnerabilityAssetAssessment` path. This keeps scoring behavior consistent while the read side migrates from the old asset/vulnerability chain to canonical exposures. Canonical `Device` now supports manual and rule-assigned `SecurityProfileId`, and profile-aware assessment is covered by tests so environmental CVSS modifiers flow into `ExposureAssessment.SecurityProfileId`, score, vector, and reason fields.

### 18.5 Compatibility Reads

- [x] define temporary read adapters for existing software list/detail pages
- [x] define temporary read adapters for approval task queries
- [x] define temporary read adapters for remediation decision queries
- [x] define temporary read adapters for risk score queries
- [x] avoid creating new durable projection tables unless a consumer requires them
- [ ] remove compatibility reads once frontend/API contracts are migrated

Note: `CanonicalExposureReadService` provides a stable open-exposure row shape over `DeviceVulnerabilityExposure`, open `ExposureEpisode`, and `ExposureAssessment`. It is the first compatibility read adapter and is intended for the risk/remediation/API migrations before those consumers query canonical tables directly. `RiskScoreService` now has a read-side canonical tenant/device risk calculation over this adapter without persisting a new durable `DeviceRiskScore` projection yet.

Note: `CanonicalRemediationCandidateReadService` provides a temporary remediation-oriented grouping over canonical exposures by observed product vendor/name/version. It also carries highest severity and earliest detected date so approval-task and SLA-style reads can be migrated without returning to the legacy `SoftwareVulnerabilityMatch` chain. This supports evaluating the recommended first `RemediationCase` scope without changing the existing `TenantSoftware`-anchored remediation workflow yet.

Note: `ApprovalTaskQueryService` now uses `RemediationCaseId` for approval-task search and display names when a task is case-linked, while retaining legacy `TenantSoftware` fallbacks for vulnerability counts, SLA status, device cohorts, risk, and existing public DTO compatibility.

Verification: full `dotnet test` passed on 2026-04-07 with 509 tests after adding the approval-task case-aware search/display bridge.

Note: `RemediationDecisionQueryService` now uses linked `RemediationCase` observed product names for decision list search/display and decision context display names when the active workflow is case-linked. The query still returns the existing `TenantSoftwareId`-based DTOs and keeps legacy `TenantSoftware`, software asset, vulnerability match, SLA, and risk fallbacks until the API/frontend contract moves to case-first shapes.

Verification: full `dotnet test` passed on 2026-04-07 with 511 tests after adding the remediation decision case-aware search/display bridge.

Note: `RemediationTaskQueryService` now prefers linked `RemediationCase` observed product names for patching-task list search and display when tasks are case-linked. It keeps the current task DTO IDs and legacy `TenantSoftware`/installation joins for device grouping, team filtering, summaries, and create-missing behavior until those flows move to case-first contracts.

Verification: full `dotnet test` passed on 2026-04-07 with 512 tests after adding the remediation task case-aware search/display bridge.

Note: dashboard owner, security-manager, and technical-manager summaries now prefer linked `RemediationCase` observed product names for owner patching actions, approved policy decisions, approved patching tasks, and approval attention cards. Dashboard severity/count stats still use the existing `TenantSoftware` rollups and public dashboard DTOs remain unchanged until the dashboard/frontend contract moves to case-first shapes.

Verification: full `dotnet test` passed on 2026-04-07 with 512 tests after adding the dashboard case-aware display bridge.

Note: `PatchingTaskService` notification text now prefers the linked `RemediationCase` observed product name when patching tasks are created from a case-linked decision, with the legacy `TenantSoftware`/`NormalizedSoftware` name as a fallback. This keeps user-facing task notifications aligned with the case-aware read surfaces while leaving task creation and grouping on the existing compatibility path.

Verification: full `dotnet test` passed on 2026-04-07 with 512 tests after adding the patching-task notification case-name bridge.

Note: `CanonicalSoftwareInventoryReadService` provides the temporary software list/detail read shape over canonical `SoftwareProduct` and `InstalledSoftware`, including version cohorts and active vulnerability counts from canonical exposures. Existing `SoftwareController` endpoints still use the legacy `TenantSoftware` contract until the API/frontend migration is done.

Verification: full `dotnet test` passed on 2026-04-07 with 507 tests after adding the canonical software inventory read adapter and tests.

### 18.6 Remediation Bridge

- [x] define `RemediationCase` scope precisely before coding
- [x] decide whether first `RemediationCase` scope is tenant + `SoftwareProduct`, tenant + vulnerability, or tenant + exposure group
- [x] introduce `RemediationCase` aggregate shell
- [x] add EF configuration and tenant filter for `RemediationCase`
- [x] add tests for `RemediationCase` scope key and lifecycle behavior
- [x] map current `RemediationWorkflow`, `RemediationDecision`, `PatchingTask`, and `ApprovalTask` dependencies to `RemediationCase`
- [x] keep current remediation tables untouched until the case aggregate is introduced
- [x] re-anchor `RemediationWorkflow` to `RemediationCase`
- [x] re-anchor `RemediationDecision` to `RemediationCase`
- [x] re-anchor `PatchingTask` to `RemediationCase`
- [x] re-anchor `ApprovalTask` to `RemediationCase`

Note: `RemediationWorkflow` now has an optional `RemediationCaseId` bridge. Workflow creation gets or creates a case from the existing `TenantSoftware` scope, preferring a matching canonical `SoftwareProduct` by canonical product key and falling back to observed product identity. `TenantSoftwareId` remains on the workflow for current API/remediation compatibility while downstream consumers migrate to case-first reads.

Note: `RemediationDecision`, `PatchingTask`, and `ApprovalTask` now also carry optional `RemediationCaseId` bridges populated from the workflow/decision attach path. Their legacy `TenantSoftwareId`, `SoftwareAssetId`, and workflow links remain in place for current API, notification, SLA, and dashboard compatibility until those consumers are migrated to case-first DTOs and queries.

Verification: full `dotnet test` passed on 2026-04-07 with 508 tests after adding the remediation case bridge across workflows, decisions, patching tasks, and approval tasks.

Recommended first `RemediationCase` scope: tenant + observed product identity, ideally `SoftwareProductId` when the exposure is software-derived and otherwise the stable observed product key `{vendor}|{name}|{version}` from the exposure evidence. This keeps the remediation unit aligned with patching work while still allowing scanner-direct exposure groups to participate before all sources are fully canonicalized.

Remediation dependency map:

- `RemediationWorkflow.TenantSoftwareId` should become `RemediationWorkflow.RemediationCaseId`; workflow stage state remains process state attached to the case.
- `RemediationDecision.TenantSoftwareId` and `RemediationDecision.SoftwareAssetId` should become `RemediationDecision.RemediationCaseId`; vulnerability overrides should be renamed or replaced with exposure overrides when decisions need per-exposure exceptions.
- `PatchingTask.TenantSoftwareId`, `PatchingTask.SoftwareAssetId`, and optional `PatchingTask.RemediationWorkflowId` should collapse to `PatchingTask.RemediationCaseId`, with workflow linkage retained only if the workflow engine still needs process correlation.
- `ApprovalTask.RemediationDecisionId` remains useful as the approval target; `ApprovalTask.RemediationWorkflowId` should become optional process context, and the case can be reached through the decision after decisions are re-anchored.

### 18.7 Milestone 2 Exit Criteria

- [x] Defender/general ingestion writes canonical `Device`, `SoftwareProduct`, `SoftwareAlias`, and `InstalledSoftware`
- [x] vulnerability applicability is represented without `NormalizedSoftware*`
- [x] device-level exposure state exists and is lifecycle-tested
- [x] existing remediation and risk features still pass through compatibility reads
- [x] no canonical write path writes `TenantSoftware` or `NormalizedSoftwareInstallation`

Milestone 2 status note:

- Defender/general ingestion is currently in a compatibility dual-write state: canonical inventory and exposure rows are written, while legacy `Asset`, `TenantSoftware`, `NormalizedSoftwareInstallation`, `SoftwareVulnerabilityMatch`, and projection writes remain for existing remediation, risk, dashboard, and software API consumers.
- The next safe removal step is to migrate the remaining consumers to canonical read adapters, then delete the old write path. The old write path should not be removed while those consumers still query it.
- Milestone 2 is ready to hand off to Milestone 3 because the canonical inventory/exposure foundations exist, `RemediationCase` exists, remediation workflow/decision/task/approval records carry case bridges, the clean baseline migration has been regenerated, and full `dotnet test` passes. The unchecked removal items above are intentionally carried forward to the read-side cutover and old-model deletion milestones rather than being deleted under the compatibility DTO constraint.

## 19. Milestone 3 Checklist

Milestone 3 goal:

- make `RemediationCase` the stable remediation process anchor
- keep current public remediation routes/DTOs stable until the API/frontend cutover
- leave old read-model deletion to Milestone 4/5

### 19.1 Case Aggregate

- [x] introduce `RemediationCase` as the stable remediation aggregate root
- [x] define first case scope as tenant + observed product identity, preferring `SoftwareProductId` when available
- [x] add EF configuration and tenant filter for `RemediationCase`
- [x] add case lifecycle tests

### 19.2 Process Re-Anchor

- [x] bridge `RemediationWorkflow` to `RemediationCase`
- [x] bridge `RemediationDecision` to `RemediationCase`
- [x] bridge `PatchingTask` to `RemediationCase`
- [x] bridge `ApprovalTask` to `RemediationCase`
- [x] propagate case IDs through decision, approval-task, and patching-task attach paths
- [x] add workflow-scoped decision creation so `/api/remediation/{workflowId}/decision` uses the active workflow/case instead of re-resolving by tenant software
- [x] add workflow-scoped analyst recommendation creation so `/api/remediation/{workflowId}/analysis` updates the specified workflow/case process path
- [x] carry forward recurring decisions onto the verified active workflow/case instead of the previous decision's tenant-software scope

### 19.3 Compatibility Reads During Re-Anchor

- [x] make approval task query display/search case-aware
- [x] make remediation decision query display/search case-aware
- [x] make remediation task query display/search case-aware
- [x] make dashboard remediation summaries case-aware for display names
- [x] make patching task assignment notifications case-aware for display names
- [x] keep current public DTOs and routes stable

Note: Milestone 3 intentionally keeps `TenantSoftwareId`, `SoftwareAssetId`, and the old software/remediation routes in public DTOs while internals move to case-aware process anchoring. Public case-first routes and DTO names belong in Milestone 4 alongside frontend alignment.

Verification: full `dotnet test` passed on 2026-04-07 with 514 tests after adding workflow-scoped decision and analyst recommendation service paths and recurrence carry-forward case anchoring.

Milestone 3 status note:

- Complete for the backend remediation re-anchor bridge. `RemediationCase` exists, workflow/decision/patching/approval process records carry and propagate case IDs, workflow-scoped command paths no longer need to re-anchor through tenant-software lookup, and compatibility reads/notifications prefer case names where available.
- Remaining tenant-software and software-asset references are compatibility API/read-model concerns and should be handled in Milestone 4 read/API/frontend cutover, then deleted in Milestone 5 old-model removal.

## 20. Milestone 4 Checklist

Milestone 4 goal:

- introduce query/read API contracts over canonical inventory and exposure models
- move the main software catalog UI to canonical software inventory reads
- leave old mutation, AI/enrichment, remediation workbench, and deletion work to the compatibility/deletion milestone

### 20.1 Canonical Query/API Contracts

- [x] add software catalog list DTOs that use `SoftwareProductId` and do not expose `TenantSoftwareId`, `NormalizedSoftwareId`, or software `Asset` IDs
- [x] add software catalog detail DTOs that use canonical version cohorts from `InstalledSoftware`
- [x] expose additive canonical software list/detail routes under `api/software/canonical`
- [x] keep existing `api/software/{tenantSoftwareId}` compatibility routes stable for AI, supply-chain, installation, vulnerability, and remediation workbench paths

Note: `api/software/canonical` now reads through `CanonicalSoftwareInventoryReadService`, which groups active `InstalledSoftware` by `SoftwareProduct` and derives active vulnerability counts from canonical `DeviceVulnerabilityExposure` rows. This gives frontend catalog views a canonical API target without silently reinterpreting legacy `TenantSoftware*` DTOs.

### 20.2 Frontend Catalog Cutover

- [x] add frontend schemas/functions for canonical software catalog responses
- [x] switch the software list route to `api/software/canonical`
- [x] remove normalized-software wording from the software list view
- [x] remove legacy-only risk, maintenance-window, `TenantSoftwareId`, and software asset ID assumptions from the software list view
- [x] keep legacy software detail/remediation routes available for existing deep links and dashboard links until the case-first detail/workbench replacement exists

Note: the software list is now the first frontend view running on canonical software inventory. Detail, AI/enrichment, and remediation workbench flows still use compatibility endpoints because those workflows still depend on tenant-local enrichment state and case/approval semantics that should move as paired slices instead of as a silent DTO reinterpretation.

Verification: `dotnet test --filter SoftwareControllerTests`, full `dotnet test`, frontend `npm run typecheck`, frontend `npm test -- list-state`, and full frontend `npm test` passed on 2026-04-07 after adding the canonical software catalog API and switching the software list route.

Milestone 4 status note:

- Complete for the canonical software catalog read/API/frontend cutover. The main software catalog no longer needs `TenantSoftware`, `NormalizedSoftware*`, or software `Asset` IDs for its list data path.
- Remaining legacy reads are compatibility paths for software detail enrichment, AI reports, remediation workbench context, dashboard deep links, and old-model deletion. Those should be handled in Milestone 5 as explicit replacement/deletion slices rather than removed underneath stable frontend routes.

## 21. Milestone 5 Checklist

Milestone 5 goal:

- remove obsolete software compatibility schema and services
- stop writing old compatibility projections
- regenerate the clean baseline after old-model removal is complete

### 21.1 Deletion Readiness Inventory

- [x] identify remaining live dependencies on `TenantSoftware`
- [x] identify remaining live dependencies on `NormalizedSoftware*`
- [x] identify remaining live dependencies on `SoftwareVulnerabilityMatch`
- [x] identify remaining live dependencies on software `Asset` IDs in software/remediation flows
- [x] replace software detail, AI description, AI report, and supply-chain enrichment flows with `SoftwareProduct`-scoped or case-scoped equivalents
- [x] replace remediation workbench commands and audit trail reads with `RemediationCase`-scoped equivalents
- [x] replace approval-task detail metrics, device cohorts, SLA summaries, and vulnerability counts with canonical exposure/case reads
- [x] add canonical `SoftwareProductId` transition handles to asset/vulnerability context software DTOs
- [x] prefer canonical software detail links in asset/vulnerability context when `SoftwareProductId` is available
- [ ] replace remaining asset/vulnerability context software links that only have legacy tenant-software IDs
- [ ] replace ingestion snapshot re-keying and cleanup paths that still assume `TenantSoftware` snapshot rows

Note: Milestone 5 cannot safely delete the old software graph yet. The canonical catalog list is cut over, but `TenantSoftware`, `NormalizedSoftwareInstallation`, `SoftwareVulnerabilityMatch`, `NormalizedSoftwareVulnerabilityProjection`, `TenantSoftwareRiskScore`, and software `Asset` IDs are still active in remediation compatibility fallbacks, software detail/enrichment, dashboard deep links, asset/vulnerability context fallbacks, ingestion snapshot cleanup, and compatibility tests.

### 21.2 Replacement Before Deletion

- [x] add case-first remediation read/workflow routes that do not require `TenantSoftwareId` as the public route key
- [x] add case-first remediation command wrappers for decision override, AI generation/review, and audit trail sidecar actions
- [x] add case-first remediation command routing for owner decision, analyst recommendation, and approval submissions through workflow/case resolution
- [x] add case-first remediation task team-status reads for the remediation workbench
- [x] add case-first remediation task creation route for the remediation workbench
- [x] add canonical software detail/installations/vulnerabilities routes that do not require `TenantSoftwareId` or software `Asset` IDs
- [x] move frontend software detail route to canonical IDs for the software catalog path
- [x] move frontend remediation route to case-first IDs where `RemediationCaseId` is available
- [ ] move remaining dashboard, asset, and vulnerability deep links off `/software/{tenantSoftwareId}` routes
- [ ] stop direct ingestion writes to `SoftwareVulnerabilityMatch`
- [ ] stop direct ingestion writes to `NormalizedSoftware*` projections
- [ ] remove obsolete `NormalizedSoftwareProjectionService`, `NormalizedSoftwareResolver`, and legacy software match projection tests after replacement coverage exists

Note: `api/remediation/cases/{remediationCaseId}/decision-context` and `api/remediation/cases/{remediationCaseId}/workflow` now provide case-addressable remediation entrypoints. Case-addressable wrappers also exist for decision vulnerability overrides, AI summary generation/review, and audit trail. They intentionally bridge to the existing tenant-software internals until the remaining owner decision, analyst recommendation, approval, and frontend route contracts move fully to case-first shapes.

Note: `DecisionContextDto` now includes `RemediationCaseId` when the active workflow is case-linked, giving the frontend a transition handle for moving from tenant-software detail routes to case-first remediation routes.

Note: the frontend remediation API layer now prefers `RemediationCaseId` for decision, recommendation, approval, override, AI, and audit sidecar calls when the context includes one, with tenant-software fallback retained for compatibility routes.

Note: remediation task team-status reads now have a case-first route at `api/remediation/cases/{remediationCaseId}/tasks/team-statuses`, and the remediation workbench uses it when `DecisionContextDto.RemediationCaseId` is present.

Note: remediation task creation now has a case-first route at `api/remediation/cases/{remediationCaseId}/tasks`, with the old tenant-software route retained as a compatibility wrapper.

Note: canonical software detail reads now include additive `api/software/canonical/{softwareProductId}/installations` and `api/software/canonical/{softwareProductId}/vulnerabilities` routes. These read from `InstalledSoftware`, `Device`, `SoftwareProduct`, and canonical `DeviceVulnerabilityExposure` rows, and intentionally avoid returning `TenantSoftwareId`, `NormalizedSoftwareId`, or software `Asset` IDs. The frontend API layer has matching schemas/functions, but the existing software detail page still needs a route/UI cutover before the legacy tenant-software detail endpoints can be removed.

Note: the frontend software catalog now links to `/software/{softwareProductId}?source=canonical`, and the shared `/software/$id` route has a canonical mode backed by the new canonical detail/installations/vulnerability APIs. The legacy route mode remains the default so dashboard, asset, vulnerability, and remediation links that still pass `TenantSoftwareId` do not break during the rest of the Milestone 5 cutover.

Note: dashboard action/approval DTOs now include nullable `RemediationCaseId` alongside `TenantSoftwareId` for owner actions, approval attention tasks, approved policy decisions, and approved patching tasks. The UI links still use compatibility tenant-software routes, but the case transition handle is now present in the API/frontend schema for the dashboard deep-link cutover.

Note: asset and vulnerability detail software context DTOs now carry nullable `SoftwareProductId` alongside the legacy `TenantSoftwareId` and software `Asset` IDs. The value is bridged from the existing tenant-software alias resolver through matching canonical product keys, and the asset/vulnerability context UI now prefers `/software/{softwareProductId}?source=canonical` when a canonical product exists. Legacy tenant-software links remain as fallback for rows without a canonical bridge.

Note: remediation decision list and remediation task list DTOs now include nullable `RemediationCaseId`, matching the existing case-linked backend rows. The existing `/software/$id/remediation` frontend route now has a case mode (`?source=case`) that loads decision context through `api/remediation/cases/{remediationCaseId}/decision-context`, and the remediation workbench, remediation task workbench/detail, and security/technical dashboard links prefer that case mode when `RemediationCaseId` is available. Tenant-software routing remains as fallback.

Note: approval-task detail pages now prefer canonical case scope for product-backed cases, and also bridge observed-product cases to a matching `SoftwareProduct` by canonical product key when one exists. In canonical scope, highest severity, SLA summary, vulnerability list, version cohorts, and device rows are derived from `InstalledSoftware`, `Device`, `SoftwareProduct`, canonical `Vulnerability`, canonical `ThreatAssessment`, and `DeviceVulnerabilityExposure`. Legacy approval detail reads remain as fallback for cases without a canonical product bridge or canonical exposures.

Note: remediation notification emails now prefer linked `RemediationCase` product names and case-first remediation URLs (`/software/{remediationCaseId}/remediation?source=case`) when an approval or patching task is case-linked. Patching-task email primary actions now open the specific remediation task detail route instead of a tenant-software-filtered task list. Tenant-software remediation URLs remain as fallback when no case link exists.

Note: `SoftwareProduct` now has product-level lifecycle fields and deprecated compatibility fallback fields for description/supply-chain enrichment. Tenant-context description and supply-chain evidence now lives on `TenantSoftwareProductInsight`; canonical software detail DTOs/API/frontend schemas prefer that tenant insight data and retain product fields only as fallback during the transition. This is a schema/model change; defer clean baseline regeneration until the remaining Milestone 5 deletion work is finished.

Note: canonical software detail now has an additive AI report route at `api/software/canonical/{softwareProductId}/ai-report`, plus matching frontend API/schema support and canonical detail UI wiring. It generates from `SoftwareProduct`, canonical installation cohorts, and canonical exposure vulnerability rows without requiring `TenantSoftwareId`. The legacy tenant-software AI report route remains as compatibility fallback until the old software detail route can be removed.

Note: remediation decision context, remediation decision list, and remediation task list DTOs now include nullable `SoftwareProductId` from linked `RemediationCase` rows. The remediation workbench software-detail link and remediation detail back-link prefer `/software/{softwareProductId}?source=canonical` when available, with tenant-software detail retained as fallback for rows that do not yet have a canonical product bridge.

Note: canonical software now has direct product-scoped description generation and CycloneDX supply-chain import routes at `api/software/canonical/{softwareProductId}/description` and `api/software/canonical/{softwareProductId}/supply-chain/cyclonedx`, with matching frontend API/schema support. They update tenant/product insight without requiring `TenantSoftwareId` or adding more legacy job coupling. The existing tenant-software background description and supply-chain import routes remain as compatibility fallbacks for the legacy detail page.

Note: software enrichment jobs now target `SoftwareProduct` for end-of-life and supply-chain enrichment. The ingestion enqueue path and manual tenant enrichment trigger collect product IDs from canonical `InstalledSoftware` rows, and the supply-chain enrichment runner applies CycloneDX evidence directly to `SoftwareProduct`. The old normalized-software enqueue methods remain only as compatibility wrappers that resolve a matching product key when called.

Note: remediation task list/summary reads now prefer canonical `RemediationCase` -> `SoftwareProduct` -> `InstalledSoftware` rows for product-backed cases, using the legacy normalized-installation path only for tasks without a canonical product bridge. Regression coverage removes normalized installation rows after task creation to prove product-backed case task list rows can still render from canonical installations.

Note: unused normalized-software enrichment entrypoints were removed after the product-target enrichment bridge became the only caller path. `EnrichmentJobEnqueuer` no longer exposes normalized-software EOL/supply-chain enqueue methods, and `CycloneDxSupplyChainImportService` no longer exposes the normalized-software direct import method.

Note: software asset detail now carries nullable `RemediationCaseId` from the active remediation workflow. The asset detail UI and `/assets/{id}/remediation` redirect prefer the case-first remediation route when that ID is available, falling back to tenant-software routing only for unbridged software assets.

Note: asset detail CPE binding reads now prefer `SoftwareProduct.PrimaryCpe23Uri` for product-bridged software rows and fall back to the legacy `SoftwareCpeBinding` table only when no canonical product CPE is available. This lets asset detail render canonical product identity without requiring `NormalizedSoftware` binding rows for bridged software.

Review follow-up note: asset CPE DTOs now carry explicit `source`, `softwareCpeBindingId`, and `softwareProductId` fields so product-derived CPE identity no longer silently masquerades as a legacy binding row. Product-backed remediation task and approval detail canonical reads now honor `RemediationCase.ObservedProductVersion` when a case is version-scoped. Canonical software description generation now counts open exposures using the same vendor/name product-key matching used by the canonical software inventory reads instead of exact `ProductName == CanonicalName`.

Review follow-up note: canonical AI description and CycloneDX supply-chain writes now use `TenantSoftwareProductInsight` keyed by tenant and `SoftwareProduct`, so tenant-context evidence no longer mutates global `SoftwareProduct` fields. Canonical software detail reads prefer tenant insight description/supply-chain data and retain product-level fields only as a fallback. End-of-life enrichment remains product-level because it represents global product lifecycle metadata.

Note: case-mode remediation pages now use canonical software detail and canonical installation reads when `DecisionContextDto.SoftwareProductId` is available, instead of always loading the legacy tenant-software detail/installations path. The canonical installations DTO/read model now carries owner user/team IDs and names so the remediation devices tab can keep owner counts and owner-team task status matching without software `Asset` IDs. Legacy tenant-software detail/installations remain fallback for unbridged remediation contexts.

Note: ingestion no longer calls `SoftwareVulnerabilityMatchService` and `NormalizedSoftwareProjectionService` directly from the run/merge flow. Those legacy writes are now isolated behind `LegacySoftwareCompatibilitySyncService`, which keeps compatibility projections alive while making the final stop/delete step a single explicit boundary. The checkbox for stopping legacy match/projection writes remains open because the compatibility service still writes the old graph for active fallback consumers.

Note: patching-task creation now resolves highest severity from canonical `RemediationCase`/`SoftwareProduct` exposure rows first, using `SoftwareVulnerabilityMatch` only as fallback for unbridged decisions. This keeps task due dates and notification severity aligned with canonical exposure state for product-backed remediation cases.

Note: software asset detail known-vulnerability reads now prefer canonical `SoftwareProduct` exposure rows for product-bridged software assets, using `SoftwareVulnerabilityMatch` only as fallback when no canonical product exposure rows exist. This removes another active read dependency on the legacy match table for canonical software assets without changing the current asset detail DTO.

Note: remediation email summaries now resolve severity from canonical `RemediationCase`/`SoftwareProduct` exposure rows first, using `NormalizedSoftwareVulnerabilityProjection` only as fallback for unbridged contexts. This moves approval and patching-task email severity off the legacy projection graph for product-backed cases.

Note: dashboard policy-decision, approved-patching, approval-attention severity/count/device stats, and missed maintenance-window counts now prefer canonical `RemediationCase`/`SoftwareProduct` exposure rows for product-backed cases, using `NormalizedSoftwareVulnerabilityProjection` only as fallback for unbridged contexts. This removes another active dashboard read dependency on legacy projection rows for case-linked remediation cards without changing the dashboard DTOs.

Note: vulnerability detail matched-software reads now include canonical `DeviceVulnerabilityExposure` product matches when no legacy `SoftwareVulnerabilityMatch` row exists, and legacy rows are suppressed when the same `SoftwareProductId` is already represented canonically. `MatchedSoftwareDto.SoftwareAssetId` is now nullable so product-derived vulnerability context does not masquerade as a software asset row.

Note: owner dashboard patching actions now prefer canonical `RemediationCase`/`SoftwareProduct` exposure rows when selecting the top vulnerability for case-linked tasks, using `SoftwareVulnerabilityMatch` only as fallback for unbridged software-asset tasks. Canonical exposure rows are mapped back to tenant vulnerabilities by external ID so the existing owner action DTO remains linkable.

Note: remediation decision list rows now prefer canonical `RemediationCase`/`SoftwareProduct` exposure stats for product-backed decisions when computing total vulnerabilities, critical/high counts, affected devices, and SLA timing. Legacy `SoftwareVulnerabilityMatch` rollups remain fallback for unbridged tenant-software rows.

Note: remediation decision context now prefers canonical `RemediationCase`/`SoftwareProduct` exposure rows for product-backed workflows when building top vulnerabilities, summary counts, SLA timing, affected-device scope, and AI-context device metadata. Legacy normalized-installation, match, and risk paths remain fallback for unbridged contexts and for compatibility data the canonical exposure model does not yet provide.

Note: remediation decision context now also has a canonical fallback for bridged tenant-software rows without a linked remediation case. When normalized installations, software matches, or `TenantSoftwareRiskScore` rows are missing, the existing `TenantSoftwareId`-based context DTO can still derive top vulnerabilities, summary counts, affected-device scope, highest criticality, SLA timing, and risk from canonical exposure and episode-risk data.

Note: approval-task detail now has the same canonical tenant-software fallback shape that approval-task list rows already had. For bridged tenant-software approvals without a linked remediation case, `ApprovalTaskQueryService.GetDetailAsync` can now resolve canonical scope from `TenantSoftware -> SoftwareProduct` and derive severity, criticality, vulnerability list, version cohorts, device rows, SLA, and risk from canonical installations/exposures before falling back to normalized-install/software-match rows.

Note: remediation task list rows now also have a canonical tenant-software branch, not just the case-linked canonical branch. `RemediationTaskQueryService` can now load bridged tenant-software tasks through `TenantSoftware -> SoftwareProduct -> InstalledSoftware -> Device/Asset` when there is no remediation case, and the legacy normalized-installation task-row query now skips rows that already have a canonical product bridge to avoid duplicate task hydration.

Note: the legacy tenant-software detail route now also prefers canonical inventory for bridged rows. `GET /api/software/{tenantSoftwareId}` keeps the same DTO shape, but when a canonical `SoftwareProduct` bridge exists it now derives first/last seen, active installs, unique devices, vulnerable installs, active vulnerability count, version cohorts, and exposure impact from canonical software inventory/exposure reads before falling back to `NormalizedSoftwareInstallation` and `NormalizedSoftwareVulnerabilityProjection`.

Note: approval-task shared severity/count/SLA helpers now also prefer canonical tenant-software summaries before dropping to normalized-install/software-match joins. That narrows several remaining old-graph reads in one place instead of only at individual approval-task entrypoints.

Note: approval-task legacy vulnerability detail now also has an alias-backed scope fallback. When a tenant-software approval detail path still needs the legacy vulnerability list and `NormalizedSoftwareInstallation` rows are gone, `ApprovalTaskQueryService` now recovers scoped software assets through `NormalizedSoftwareAlias -> Assets` before reading `SoftwareVulnerabilityMatch`, keeping the compatibility vulnerability list alive for alias-bridged software without reintroducing a normalized-install dependency.

Note: approval-task legacy device scope now has the same alias-backed fallback. When a tenant-software approval detail path still needs device cohorts or device rows and `NormalizedSoftwareInstallation` rows are gone, `ApprovalTaskQueryService` now falls back to `DeviceSoftwareInstallation` rows keyed by alias-backed software assets. That preserves the compatibility device list/cohort view for alias-bridged software even when version-specific install rows have already been removed; in that fallback the version cohort is intentionally unknown rather than silently empty.

Note: approval-task shared tenant-software list helpers now also have alias-backed fallback for unresolved legacy rows. When canonical tenant-software summaries do not apply and active `NormalizedSoftwareInstallation` rows are gone, `ApprovalTaskQueryService` now derives highest severity, vulnerability count, SLA, and highest device criticality from alias-backed software assets and `DeviceSoftwareInstallation` links instead of returning empty legacy summary state. This keeps the approval list alive for alias-bridged tenant-software rows without reintroducing a hard dependency on normalized-install snapshots.

Note: remediation decision query helpers now consistently use the shared alias-backed scope bridge. `RemediationDecisionQueryService` list rows already had alias-backed helper coverage, but tenant-software decision context still reimplemented `NormalizedSoftwareInstallation`-first scope resolution inline. That branch now uses the shared alias-backed software/device scope helpers as well, so decision context and decision list no longer drift on whether normalized-install rows still exist for the same bridged tenant-software row.

Note: remediation workflow owner-team resolution now also has an alias-backed fallback for unbridged legacy rows. When a remediation case is not product-backed and canonical device scope does not apply, `RemediationWorkflowService` now recovers candidate software assets through `TenantSoftware -> NormalizedSoftwareAlias -> Assets` before choosing the owner team. That keeps workflow creation alive after normalized-install rows are removed, as long as the bridged software assets still carry ownership.

Note: remediation decision list now only hits legacy normalized-install/software-match/risk rows for tenant-software IDs that are still unresolved after canonical tenant-software summary/risk helpers run. Bridged rows can now skip the broad old-graph sweep for representative assets, severity counts, criticality, device counts, and risk.

Note: tenant-software remediation decision context now also short-circuits on canonical scope before loading legacy normalized-install and software-match data. `BuildByTenantSoftwareAsync` still returns the same `DecisionContextDto`, but when canonical tenant-software context data is available it now derives device scope, owner-team counts, business-label context, top vulnerabilities, summary, SLA, and open-episode trend without first sweeping `NormalizedSoftwareInstallation`, `SoftwareVulnerabilityMatch`, `VulnerabilityAssetAssessment`, or `TenantSoftwareRiskScore`. Legacy rows remain fallback only when the tenant-software row is still unresolved after the canonical bridge.

Note: dashboard software-scope helper reads now also run canonical-first. `BuildSoftwareScopeStatsAsync` now fills bridged tenant-software rows from canonical exposure scope before reading `NormalizedSoftwareVulnerabilityProjection`, and only queries the projection table for IDs still unresolved after the canonical pass.

Note: the legacy software list route now only computes legacy risk/install/vulnerability/version/last-seen aggregates for tenant-software IDs that remain unresolved after canonical catalog/risk hydration. Bridged rows can now skip another broad old-model sweep even though the route shape remains the same.

Note: the existing `GET /api/risk-score/software/{tenantSoftwareId}` route now has a canonical product fallback. When a matching `SoftwareProduct` exists, it derives software risk detail from canonical `InstalledSoftware`, canonical `DeviceVulnerabilityExposure`, existing asset risk scores, and episode risk assessments instead of requiring `TenantSoftwareRiskScore` and `NormalizedSoftwareInstallation` rows. The route still returns the same tenant-software DTO shape and falls back to the stored tenant-software risk row when no canonical bridge exists.

Note: remediation workflow owner-team resolution and patching-task fanout now prefer canonical `InstalledSoftware` plus matched device assets for product-backed remediation cases. When a workflow is case-linked to a `SoftwareProduct`, `RemediationWorkflowService` no longer requires `NormalizedSoftwareInstallation` rows to choose the software owner team, and `PatchingTaskService` no longer requires them to derive affected-team device counts for task creation. Legacy normalized-installation reads remain fallback only for unbridged or observed-product contexts.

Note: remediation closure/reconciliation now prefers canonical `RemediationCase`/`SoftwareProduct` exposure state for product-backed active workflows. `ReconcileResolvedSoftwareRemediationsAsync` checks open canonical `DeviceVulnerabilityExposure` rows for case-linked product scopes before falling back to legacy `NormalizedSoftwareVulnerabilityProjection` rows for observed-product or unbridged workflows. This removes another active dependency on the legacy projection graph for canonical remediation cases.

Note: the legacy `GET /api/software` compatibility list route now hydrates install/device/vulnerability/version counts and `LastSeenAt` from canonical software inventory when the tenant-software row bridges to a `SoftwareProduct` and the legacy normalized-install/projection rows are absent. The route still returns the existing tenant-software DTO shape and continues to use stored tenant-software risk and maintenance-window state where present.

Note: the legacy `/software/{tenantSoftwareId}` frontend detail route now uses the canonical installs, vulnerabilities, AI report, and direct description-generation APIs whenever the tenant-software detail response includes a bridged `SoftwareProductId`. The URL and remediation route shape remain tenant-software based for compatibility, but the overview/AI tabs stop calling the old tenant-software installs/vulnerabilities/AI endpoints for product-bridged rows.

Note: the backend legacy software installations and vulnerabilities routes now also prefer canonical product reads when the tenant-software row bridges to a `SoftwareProduct` and the legacy normalized-install or software-match rows are absent. `GET /api/software/{tenantSoftwareId}/installations` maps canonical installs back into the existing installation DTO through device assets by `ExternalId` and software assets by alias `ExternalSoftwareId`, and `GET /api/software/{tenantSoftwareId}/vulnerabilities` maps canonical product vulnerabilities back to existing tenant-vulnerability rows by `ExternalId`.

Note: remediation decision creation no longer requires a live `NormalizedSoftwareInstallation` row to resolve tenant-software scope. When the caller still submits a legacy software asset ID, `RemediationDecisionService` can now recover the tenant-software scope from the software asset `ExternalId` via `NormalizedSoftwareAlias`, keeping the compatibility command path alive without depending on normalized-install snapshot rows.

Note: the legacy tenant-software detail/API wrapper now prefers canonical product metadata when a bridge exists. `GET /api/software/{tenantSoftwareId}` resolves `SoftwareProduct` and `TenantSoftwareProductInsight` by canonical product key and prefers tenant/product description, supply-chain insight, and product lifecycle metadata over `NormalizedSoftware`. The legacy AI report route now delegates to the canonical product AI report generator when normalized installations are gone, and the legacy CycloneDX import route now writes tenant/product insight when the tenant-software row bridges to a canonical product.

Note: snapshot publish re-keying is now narrowed to legacy-only remediation records. `PublishSnapshotAsync` no longer reassigns `RemediationDecision`, `RemediationWorkflow`, or `PatchingTask` `TenantSoftwareId` values when those rows are already linked to a `RemediationCase`; case-linked records now stay anchored on case scope during snapshot rotation, and only unbridged legacy rows still participate in `TenantSoftware` snapshot re-keying.

Note: asset-side software compatibility upkeep is now funneled through `LegacySoftwareCompatibilitySyncService` as well. Manual software CPE binding changes in `AssetsController` no longer call `NormalizedSoftwareProjectionService` directly; they trigger the same legacy compatibility boundary used by ingestion so the final stop/delete step is centralized in one place.

Note: dashboard summary name resolution now avoids eager joins through `TenantSoftware -> NormalizedSoftware` for case-linked and summary-card rows. Security-manager approved decisions, technical-manager approved patching tasks, and approval-attention cards now query remediation/task rows first, resolve case names through `RemediationCase`, and only fall back to `TenantSoftware` name lookup for unbridged legacy rows. This removes another cluster of old-graph joins from hot dashboard queries without changing DTO shapes.

Note: `IngestionService` runtime activation is now explicitly anchored on `LegacySoftwareCompatibilitySyncService` rather than the legacy match/projection services directly. The old `SoftwareVulnerabilityMatchService` and `NormalizedSoftwareProjectionService` remain behind the compatibility boundary, but the ingestion runtime no longer exposes them as first-class constructor dependencies in DI.

Note: remediation work notes are now case-aware. `WorkNotesController` resolves `remediations/{entityId}` against `RemediationCase` first and, when the ID is a case, lists both new case-scoped `TenantSoftwareRemediation` notes and legacy notes still attached to the bridged `TenantSoftwareId` values from related workflows. The remediation workbench now opens the work-notes sheet with `RemediationCaseId` when available, so new notes for case-linked workflows no longer need the old software identity.

Note: software work notes now have a canonical bridge as well. `WorkNotesController` resolves `software/{entityId}` against `SoftwareProduct` when the ID is not a `TenantSoftwareId`, aggregates existing note threads from bridged tenant-software rows that share the canonical product key, and stores new notes against the newest bridged tenant-software row. The canonical software detail page now exposes the same work-notes sheet using `SoftwareProductId`.

Note: approval-task list rows are now canonical-first for product-backed cases as well. `ApprovalTaskQueryService.ListAsync` resolves page-scoped `RemediationCaseId` values into canonical case/product scope and prefers canonical exposures/installations for highest severity, highest criticality, vulnerability count, and SLA. Legacy `TenantSoftware`/normalized-install/match rollups remain fallback only for unbridged rows.

Note: ingestion snapshot re-keying and legacy snapshot cleanup are now also owned by `LegacySoftwareCompatibilitySyncService`. `IngestionService` no longer contains its own `TenantSoftware` re-key loop or direct deletion sequence for the legacy software graph; publish/discard now delegate the old-graph re-key and cleanup steps through the same compatibility boundary already used for legacy match/projection sync.

Note: remediation email summaries now also prefer canonical installed-software scope for case-linked device counts. `EmailNotificationService` already preferred case/product severity; it now counts affected devices from `InstalledSoftware` and `Device` ownership for product-backed cases before falling back to `NormalizedSoftwareInstallations`, and the regression now proves the approval email path still renders the correct count after normalized-install rows are removed.

Note: remediation email summaries now also have a canonical tenant-software fallback for bridged non-case rows. `EmailNotificationService.BuildRemediationSummaryAsync` now resolves canonical product scope from `TenantSoftware`, derives highest severity from canonical exposures, and derives affected-device counts from canonical `InstalledSoftware` mapped back to owned device assets before falling back to normalized installations or vulnerability projections. This removes another active notification dependency on the legacy software graph for bridged patching-task emails.

Note: `PatchingTaskService` now also has a canonical tenant-software fallback for bridged non-case decisions. When a decision is not case-linked but its `TenantSoftware` row bridges to a canonical `SoftwareProduct`, patching-task fanout now resolves affected devices and owner teams from canonical `InstalledSoftware` mapped back to device assets, and highest severity now comes from canonical exposure rows before falling back to normalized installations or software matches. This removes another active non-case execution dependency on the old software graph without changing task shape or notification text.

Note: legacy tenant-software description generation now also prefers canonical product evidence when a bridge exists. `SoftwareDescriptionGenerationService.GenerateAsync` now uses canonical `InstalledSoftware` versions and canonical `DeviceVulnerabilityExposure` counts for the prompt when the tenant-software row bridges to a `SoftwareProduct` by canonical product key, falling back to normalized-install/projection reads only when no canonical product bridge exists. A direct service regression now proves the prompt still carries observed versions and vulnerability count after normalized rows are removed.

Note: software risk recalculation is now canonical-first for bridged rows. `RiskScoreService.CalculateSoftwareScoresAsync` still writes compatibility `TenantSoftwareRiskScore` rows, but it now prefers canonical `InstalledSoftware` + `DeviceVulnerabilityExposure` + `VulnerabilityEpisodeRiskAssessment` joins keyed through canonical product identity and mapped device assets when the active tenant-software row bridges to a canonical product key. The old normalized-install + software-match path remains only as fallback for unbridged rows.

Note: remediation decision list/context risk display now also has a canonical case fallback. `RemediationDecisionQueryService` still prefers stored `TenantSoftwareRiskScore` rows when present, but for case-linked decisions it can now reconstruct the decision risk card from canonical product/device exposures plus episode-risk assessments mapped through device assets, so the remediation workbench no longer requires the legacy software risk row to exist for bridged cases.

Note: approval-task detail now has the same canonical risk fallback. `ApprovalTaskQueryService.GetDetailAsync` still prefers a stored `TenantSoftwareRiskScore` when present, but for product-backed approval tasks it can now reconstruct the risk card from canonical installs/exposures plus episode-risk assessments mapped through device assets, so approval detail no longer requires the legacy software risk row for bridged cases.

Note: case-based remediation task creation is now canonical-first as well. `RemediationTaskQueryService.CreateMissingTasksForCaseAsync` no longer just bounces through the tenant-software task path; it resolves the active workflow for the case, counts eligible scope from canonical `InstalledSoftware` when the case is product-backed, reuses an existing approved case-linked decision when present, and otherwise seeds a workflow-scoped patching decision. That keeps the case task command path working after normalized-install rows are removed, while preserving the existing compatibility behavior for unbridged legacy cases.

Note: tenant-software remediation task creation is now canonical-first as well. `RemediationTaskQueryService.CreateMissingTasksForSoftwareAsync` no longer enumerates `NormalizedSoftwareInstallation` software assets to seed or reuse patching decisions. It now counts eligible scope from a bridged `SoftwareProduct` when available, reuses an existing approved decision by `TenantSoftwareId`, and otherwise creates the decision through the tenant-software-scoped decision service entrypoint. That keeps the compatibility software workbench command working after normalized-install rows are removed, while preserving the existing tenant-software route and decision shape.

Note: the legacy software compatibility list route now also has a canonical installed-device risk fallback. `GET /api/software` still prefers stored `TenantSoftwareRiskScore` rows when they exist, but when a tenant-software row bridges to a canonical product and the legacy software risk row is gone, the route now derives `CurrentRiskScore` from the active canonical installed-device `AssetRiskScore` rollup for that product. This removes another live read dependency on `TenantSoftwareRiskScore` from the list view without changing the existing DTO or sort behavior.

Note: dashboard software-scope summaries now also have a canonical fallback for non-case rows. `BuildSoftwareScopeStatsAsync` still prefers legacy `NormalizedSoftwareVulnerabilityProjection` rows when present, but when a tenant-software row is bridged and those projections are gone it now derives severity, vulnerability count, and affected-device count from open canonical `DeviceVulnerabilityExposure` rows keyed by the tenant-software canonical vendor/name. The missed-maintenance-window count now uses that same helper instead of checking the legacy projection table directly, so the dashboard summary path no longer requires projections for bridged tenant-software rows.

Note: approval-task list rows now also have a canonical fallback for bridged tenant-software rows, not just case-linked rows. `ApprovalTaskQueryService.ListAsync` now builds canonical summaries keyed by `TenantSoftwareId` and, when the tenant-software row bridges to a canonical product, fills highest severity, highest device criticality, vulnerability count, and SLA from canonical installs/exposures before falling back to normalized-install/software-match rollups. That removes another active read dependency on the legacy software graph for non-case approval rows without changing the list DTO.

Note: remediation decision list rows now also have a canonical fallback for bridged tenant-software rows, not just case-linked rows. `RemediationDecisionQueryService.ListAsync` now builds canonical vulnerability/device summaries keyed by `TenantSoftwareId` and, when the tenant-software row bridges to a canonical product, fills total/critical/high vulnerability counts, affected-device count, and highest device criticality from canonical installs/exposures before falling back to normalized-install/software-match rollups. That removes another active read dependency on the legacy software graph for non-case decision rows without changing the list DTO.

Note: owner dashboard actions now also have a canonical tenant-software fallback for bridged non-case patching tasks. `GetOwnerSummary` now builds top vulnerability context by `TenantSoftwareId` from canonical `DeviceVulnerabilityExposure` and canonical `Vulnerability` rows, mapped back to `TenantVulnerability` by external ID, before falling back to `SoftwareVulnerabilityMatch` keyed by software asset. This removes another active owner-action dependency on the legacy match graph for bridged patching-task rows.

Note: dashboard latest-unhandled suppression is now canonical-aware for bridged tenant-software rows as well. `GetSummary` no longer decides "already covered by an active remediation decision" only through `SoftwareVulnerabilityMatch`; it now also suppresses vulnerabilities whose external IDs are covered by active case-linked or bridged tenant-software decisions through canonical `DeviceVulnerabilityExposure` scope. That removes another hot dashboard dependency on the legacy match graph without changing the existing summary DTO.

Note: the legacy tenant-software installations and vulnerabilities compatibility routes are now canonical-first for bridged rows, not just canonical-backed when legacy rows are missing. When `TenantSoftware` resolves to a canonical `SoftwareProduct` and canonical installations or vulnerabilities exist, `GET /api/software/{tenantSoftwareId}/installations` and `GET /api/software/{tenantSoftwareId}/vulnerabilities` now return canonical-mapped results before considering `NormalizedSoftwareInstallation` or `SoftwareVulnerabilityMatch` rows. The route shapes stay the same, but product-bridged callers stop drifting based on whether stale legacy snapshot rows still exist.

Note: remediation decision creation no longer needs a second normalized-install lookup after scope has already been resolved. `CreateDecisionAsync` now keeps the caller's software asset when `ResolveScopeAsync` finds tenant-software scope through an active installation, and `CreateDecisionForTenantSoftwareAsync` now prefers alias-backed software asset recovery before falling back to `NormalizedSoftwareInstallation` when it needs a representative software asset for a bridged tenant-software command. This narrows another command-side dependency on normalized-install snapshot rows without changing decision/workflow DTO shape.

Note: `PatchingTaskService` now narrows two more non-case legacy fallbacks. When normalized-install rows are absent, scoped software asset IDs can now be recovered through tenant-software alias bindings instead of only through `NormalizedSoftwareInstallation`, and notification software names now prefer the bridged canonical product name before falling back to the tenant-software normalized name. This keeps more of the non-case execution path alive without depending on snapshot installs.

Note: tenant-software remediation decision context now also has an alias-backed legacy fallback when canonical scope is unavailable and normalized-install rows are gone. `RemediationDecisionQueryService.BuildByTenantSoftwareAsync` can now recover the representative software asset and scoped software asset IDs through `NormalizedSoftwareAlias` -> software asset bindings, so the compatibility decision context still builds top vulnerabilities from legacy `SoftwareVulnerabilityMatch` rows without requiring active `NormalizedSoftwareInstallation` rows.

Verification: `dotnet test --filter SoftwareControllerTests`, `dotnet test --filter DashboardControllerTests`, `dotnet test --filter AssetsControllerTests`, `dotnet test --filter VulnerabilitiesControllerTests`, `dotnet test --filter RiskScoreControllerTests`, `dotnet test --filter RiskScoreServiceTests`, `dotnet test --filter RemediationTasksControllerTests`, `dotnet test --filter RemediationDecisionsControllerTests`, `dotnet test --filter RemediationDecisionServiceTests`, `dotnet test --filter ApprovalTaskQueryServiceTests`, `dotnet test --filter EmailNotificationServiceTests`, `dotnet test --filter WorkNotesControllerTests`, `dotnet test --filter SoftwareDescriptionGenerationServiceTests`, `dotnet test --filter "SoftwareProductTests|SoftwareControllerTests"`, `dotnet test --filter EnrichmentJobEnqueuerTests`, `dotnet test --filter "EnrichmentJobEnqueuerTests|SoftwareControllerTests"`, `dotnet test --filter "EnrichmentJobEnqueuerTests|RemediationTasksControllerTests"`, `dotnet test --filter "ApprovalTaskQueryServiceTests|RemediationTasksControllerTests"`, `dotnet test --filter "RiskScoreControllerTests|RemediationDecisionsControllerTests"`, `dotnet test --filter IngestionServiceTests`, `dotnet build`, `dotnet test`, `npm run typecheck`, and `npm test` pass after the canonical software detail path, dashboard transition-field slice, asset/vulnerability canonical link cutover, case-first remediation link mode, approval-task canonical detail metrics bridge, approval-task canonical detail risk bridge, approval-task canonical list metrics bridge, approval-task tenant-software canonical summary bridge, approval-task tenant-software canonical detail bridge, approval-task canonical helper bridge, remediation decision tenant-software canonical summary bridge, remediation decision tenant-software canonical list helper bridge, remediation decision tenant-software canonical context/risk bridge, remediation decision alias-backed legacy context bridge, remediation task tenant-software canonical list bridge, tenant-software detail canonical inventory bridge, dashboard canonical helper bridge, case-first notification link bridge, product enrichment schema/read/dual-write bridge, canonical AI report route/UI bridge, remediation `SoftwareProductId` transition handles, direct canonical description/supply-chain import route bridges, product-target enrichment enqueue/runner bridge, product-backed remediation task list read bridge, case-based remediation task command bridge, tenant-software remediation task command bridge, canonical-backed legacy software list risk bridge, canonical dashboard software-scope fallback bridge, removal of unused normalized-software enrichment entrypoints, asset-detail remediation case link bridge, canonical product CPE binding read bridge, tenant-scoped product insight bridge, case-mode canonical remediation devices bridge, legacy software compatibility sync boundary, canonical-first patching-task severity bridge, canonical-first software asset known-vulnerability bridge, canonical-first remediation email severity bridge, canonical-first remediation email device-count bridge, canonical-first tenant-software remediation email summary bridge, canonical-first non-case patching-task fanout bridge, alias-backed non-case patching-task asset/name fallback bridge, canonical-first dashboard remediation stats bridge, canonical vulnerability matched-software bridge, canonical-first owner dashboard action vulnerability bridge, canonical-first non-case owner dashboard action bridge, canonical-aware dashboard latest-unhandled suppression bridge, canonical-first remediation decision list stats bridge, canonical-first remediation decision context scope bridge, canonical-first remediation decision risk bridge, canonical-first software risk detail bridge, canonical-first software risk recalculation bridge, canonical-installation workflow owner-team bridge, canonical-installation patching-task fanout bridge, canonical-case remediation reconcile bridge, canonical-backed legacy software list bridge, tenant-software detail-page canonical caller cutover, canonical-first legacy software installations/vulnerabilities bridge, canonical-first remediation decision scope bridge, alias-backed remediation decision scope resolution, canonical-backed legacy software detail/AI report/supply-chain wrappers, legacy tenant-software description generation on canonical product evidence, snapshot publish re-key narrowing for case-linked remediation records, asset-side compatibility sync centralization behind `LegacySoftwareCompatibilitySyncService`, dashboard summary name-resolution decoupling from the old software graph, ingestion runtime constructor centralization on the compatibility boundary, ingestion legacy snapshot re-key/cleanup centralization on the compatibility boundary, case-aware remediation work-note resolution, canonical software work-note bridging, and approval-task alias-backed legacy list/device-scope bridges. Latest focused vulnerability controller verification passed with 6 tests, latest focused risk-score/remediation-decision verification passed with 20 tests, latest focused risk-score service verification passed with 5 tests, latest focused dashboard verification passed with 20 tests, latest focused software controller verification passed with 29 tests, latest focused asset controller verification passed with 9 tests, latest focused remediation decisions verification passed with 18 tests, latest focused remediation decision service verification passed with 19 tests, latest focused remediation-tasks verification passed with 9 tests, latest focused email notification verification passed with 8 tests, latest focused approval/remediation-task verification passed with 8 tests, latest focused approval-task verification passed with 8 tests, latest focused work-notes verification passed with 9 tests, latest focused software-description generation verification passed with 1 test, latest focused remediation/enrichment verification passed with 12 tests, latest focused ingestion verification passed with 46 tests, latest frontend typecheck and frontend tests passed with 20 tests, latest build passed with 0 warnings and 0 errors, and latest full backend verification passed with 577 tests. One parallel focused `dotnet` attempt hit an MVC/static-web-assets or MVC manifest cache file lock and passed when rerun by itself.

### 21.3 Schema Removal

- [ ] remove old compatibility entities from `PatchHoundDbContext`
- [ ] remove old EF configurations
- [ ] remove old DTOs and frontend schemas after routes are no longer used
- [ ] regenerate the clean baseline migration
- [ ] run full backend and frontend verification

Milestone 5 status note:

- Started, but not complete. Case-addressable remediation context/workflow/command entrypoints exist, canonical software detail/installations/vulnerability/read AI report routes exist, the software catalog has a canonical detail/enrichment path, the legacy software list/installations/vulnerabilities/detail/AI report/supply-chain routes now have canonical-backed compatibility bridges for product-bridged rows, the tenant-software detail page now calls canonical installs/vulnerabilities/AI generation for product-bridged rows, remediation decision creation can recover scope from software asset aliases without normalized-install rows, dashboard/asset/vulnerability/remediation DTOs now expose transition IDs, approval-task details have a canonical metrics bridge, remediation task list reads have a product-backed canonical branch, remediation decision list stats and decision context scope are canonical-first for product cases, patching task severity/fanout, workflow owner-team resolution, remediation reconcile/closure, remediation email, dashboard remediation-card severity, owner dashboard action vulnerability context, vulnerability matched-software context, and software risk detail are canonical-first for product cases, product enrichment dual-writes/product-target jobs are in place, software asset detail known-vulnerability reads are canonical-first for product-bridged assets, ingestion legacy match/projection writes are isolated behind a compatibility boundary, and snapshot publish no longer re-keys case-linked remediation records through `TenantSoftware`. The safe next slice is still replacing the remaining old-model write/cleanup assumptions so the compatibility boundary can be disabled and deleted. Deleting old entities before that would still break ingestion cleanup, snapshot discard/publish behavior for unbridged legacy rows, and the remaining fallback flows that still depend on the compatibility boundary.
- Started, but not complete. Case-addressable remediation context/workflow/command entrypoints exist, canonical software detail/installations/vulnerability/read AI report routes exist, the software catalog has a canonical detail/enrichment path, the legacy software list/installations/vulnerabilities/detail/AI report/supply-chain routes now have canonical-backed compatibility bridges for product-bridged rows, the tenant-software detail page now calls canonical installs/vulnerabilities/AI generation for product-bridged rows, remediation decision creation can recover scope from software asset aliases without normalized-install rows, tenant-software remediation task creation no longer requires normalized-install rows for bridged products, dashboard/asset/vulnerability/remediation DTOs now expose transition IDs, approval-task details and list rows have canonical metrics bridges, approval-task legacy vulnerability detail can now also recover alias-backed software scope without normalized-install rows, approval-task legacy device scope and shared tenant-software list helpers now also have alias-backed fallback, remediation task list reads have a product-backed canonical branch, remediation decision list stats and decision context scope are canonical-first for product cases, patching task severity/fanout, workflow owner-team resolution, remediation reconcile/closure, remediation email, dashboard remediation-card severity, owner dashboard action vulnerability context, vulnerability matched-software context, and software risk detail are canonical-first for product cases, product enrichment dual-writes/product-target jobs are in place, software asset detail known-vulnerability reads are canonical-first for product-bridged assets, ingestion legacy match/projection writes are isolated behind a compatibility boundary, and legacy snapshot re-key/cleanup logic is now centralized behind that same compatibility boundary. The safe next slice is still disabling the boundary by removing the remaining fallback readers and then deleting the old entities/configuration. Deleting old entities before that would still break the remaining compatibility consumers and the unbridged legacy snapshot paths that still rely on the boundary.
