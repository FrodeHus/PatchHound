# Ingestion Flow

This document visualizes the current PatchHound ingestion and enrichment pipeline as implemented in:

- `src/PatchHound.Infrastructure/Services/IngestionService.cs`
- `src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs`
- `src/PatchHound.Infrastructure/Services/StagedVulnerabilityMergeService.cs`
- `src/PatchHound.Infrastructure/Services/SoftwareVulnerabilityMatchService.cs`
- `src/PatchHound.Infrastructure/Services/NormalizedSoftwareProjectionService.cs`
- `src/PatchHound.Worker/EnrichmentWorker.cs`

## High-Level Flow

```mermaid
flowchart TD
    A[Ingestion trigger\nscheduled or manual] --> B[IngestionService.RunIngestionAsync]
    B --> C{Acquire tenant/source lease}
    C -- no --> Z1[Skip run]
    C -- yes --> D[Clear staged data for run\nupdate runtime state to Running]

    D --> E{Source supports asset inventory?}
    E -- yes --> F[FetchAssetsAsync]
    F --> G[Normalize asset snapshot]
    G --> H[Stage asset snapshot\nStagedAssets + StagedDeviceSoftwareInstallations]
    H --> I[ProcessStagedAssetsAsync]
    I --> J[StagedAssetMergeService.ProcessAsync]
    J --> J1[Upsert Assets]
    J --> J2[Upsert DeviceSoftwareInstallations]
    J --> J3[Open/seen/remove DeviceSoftwareInstallationEpisodes]
    J --> J4[NormalizedSoftwareProjectionService.SyncTenantAsync]

    E -- no --> K[Skip asset inventory branch]
    J4 --> L[FetchVulnerabilitiesAsync]
    K --> L

    L --> M[Normalize vulnerability results]
    M --> N[Stage vulnerabilities\nStagedVulnerabilities + StagedVulnerabilityExposures]
    N --> O[ProcessStagedResultsAsync]
    O --> P[StagedVulnerabilityMergeService.ProcessAsync]
    P --> P1[Upsert Vulnerabilities]
    P --> P2[Upsert VulnerabilityAssets]
    P --> P3[Open/resolve VulnerabilityAssetEpisodes]
    P --> P4[VulnerabilityAssessmentService]
    P --> P5[RemediationTaskProjectionService]

    P5 --> Q[Enqueue enrichment jobs]
    Q --> R[SoftwareVulnerabilityMatchService.SyncForTenantAsync]
    R --> R1[Defender direct matching]
    R --> R2[CPE rule matching]
    R --> R3[Auto-create/update SoftwareCpeBindings]
    R --> R4[Upsert/resolve SoftwareVulnerabilityMatches]
    R4 --> S[NormalizedSoftwareProjectionService.SyncTenantAsync]
    S --> S1[NormalizedSoftwareResolver.SyncTenantAsync]
    S1 --> S2[Upsert NormalizedSoftware + Aliases]
    S --> S3[Rebuild NormalizedSoftwareInstallations]
    S --> S4[Rebuild NormalizedSoftwareVulnerabilityProjections]

    S4 --> T[Mark run succeeded\nupdate runtime state]
    T --> U[Retention cleanup for old ingestion artifacts]
```

## Vulnerability Merge Detail

```mermaid
flowchart TD
    A[StagedVulnerabilityMergeService.ProcessAsync] --> B[Load staged vulnerability chunk]
    B --> C[Load matching staged exposures]
    C --> D[Load chunk state\nexisting vulnerabilities, assets, projections,\nopen episodes, latest episode numbers,\nsecurity profiles, open remediation tasks]
    D --> E[Execution strategy + transaction]
    E --> F[Upsert vulnerability catalog row]
    F --> G[Merge affected assets for this chunk]
    G --> H{Existing VulnerabilityAsset?}
    H -- no --> I[Create VulnerabilityAsset\ncreate episode #1]
    H -- yes --> J[Reopen or keep open projection\nupdate open episode]
    I --> K[Track opened projection]
    J --> K
    K --> L[Recalculate organizational assessment]
    L --> M[Ensure open remediation task if needed]
    M --> N[Save changes + commit]
    N --> O[Reconciliation transaction]
    O --> P[Resolve missing VulnerabilityAssetEpisodes\nfor pairs not present in current staged set]
    P --> Q[Close remediation tasks for resolved pairs]
    Q --> R[Update source-specific vulnerability statuses]
```

## Asset Inventory Merge Detail

```mermaid
flowchart TD
    A[StagedAssetMergeService.ProcessAsync] --> B[Load staged assets]
    B --> C[Load staged device/software links]
    C --> D[Chunk staged assets]
    D --> E[Deserialize IngestionAsset]
    E --> F{Asset exists by tenant + ExternalId?}
    F -- no --> G[Create Asset]
    F -- yes --> H[Update Asset details + metadata]
    G --> I[Save chunk + clear change tracker]
    H --> I
    I --> J[Deserialize device/software links]
    J --> K[Resolve device/software asset IDs]
    K --> L{Link resolved?}
    L -- no --> M[Skip unresolved link]
    L -- yes --> N[Upsert DeviceSoftwareInstallation]
    N --> O{Open installation episode exists?}
    O -- no --> P[Create new DeviceSoftwareInstallationEpisode]
    O -- yes --> Q[Mark episode seen]
    P --> R[Mark stale installations not seen in this sync]
    Q --> R
    R --> S[Remove installations after missing threshold\nclose episodes with RemovedAt]
    S --> T[Save changes + clear tracker]
    T --> U[NormalizedSoftwareProjectionService.SyncTenantAsync]
```

## Enrichment Worker Flow

```mermaid
flowchart TD
    A[BackgroundService loop\n30s interval] --> B[LoadEnabledSourcesAsync]
    B --> C{OpenBao available,\ninitialized, unsealed?}
    C -- no --> Z[Skip cycle]
    C -- yes --> D[Load enabled enrichment sources]
    D --> E{Configured credentials?}
    E -- no --> F[Skip source]
    E -- yes --> G[RunSourceCycleAsync]
    G --> H{Acquire source lease?}
    H -- no --> I[Skip source run]
    H -- yes --> J[Reload tracked source row\ninside same DbContext]
    J --> K[Reset expired running jobs]
    K --> L[Create EnrichmentRun]
    L --> M[Claim due jobs]
    M --> N{Any jobs?}
    N -- no --> O[Complete run as NoWork\nupdate runtime]
    N -- yes --> P[Execute runner per claimed job]
    P --> Q{Runner outcome}
    Q -- Succeeded --> R[Mark job Succeeded]
    Q -- NoData --> S[Mark job Skipped]
    Q -- Retry --> T[Schedule retry]
    Q -- Failed --> U[Mark job Failed]
    R --> V[Save]
    S --> V
    T --> V
    U --> V
    V --> W{More jobs?}
    W -- yes --> P
    W -- no --> X[Complete EnrichmentRun\nupdate runtime/lease]
```

## Notes

- Ingestion retries concurrency failures at the source-run level through `ExecuteWithConcurrencyRetryAsync(...)`.
- Asset inventory projection and normalized software projection are intentionally rebuilt deterministically per tenant in the current implementation.
- Software matching runs after vulnerability merge and enrichment enqueue, not before.
- Enrichment job execution is isolated from the ingestion transaction flow and runs through the worker.
- Normalized software detail/list APIs are served from the derived normalized tables, not by joining raw software assets on demand.
