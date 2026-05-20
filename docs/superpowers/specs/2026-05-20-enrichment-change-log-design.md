# Enrichment Change Log Design

## Context

PatchHound runs background enrichment jobs that can update canonical entities with source-derived data. A vulnerability, for example, can receive a different CVSS score or vendor severity after an enrichment run. Today, the existing `AuditLogEntry` model records user-driven entity changes with old and new JSON values, but the audit interceptor intentionally skips worker/system context. That keeps compliance audit logs focused on human actions, but it means source-driven enrichment changes are not visible from an entity detail page.

The design should provide operational provenance: users should be able to answer "what enrichment source changed this value, from what, to what, and when?" without treating every worker update as a formal user audit event.

## Goals

- Record field-level enrichment changes with old value, new value, source, timestamp, and run/job provenance.
- Keep the model generic enough to reuse for vulnerabilities, software, devices, and future enriched entities.
- Preserve the semantic distinction between user audit logs and system enrichment provenance.
- Provide a reusable UI sheet that can be attached to entity detail pages.
- Allow entity timelines to show enrichment changes beside user audit events when useful.

## Non-Goals

- Do not replace `AuditLogEntry`.
- Do not log every EF system update automatically.
- Do not store full raw enrichment payload snapshots as the primary user-facing history.
- Do not build source-specific UI for vulnerabilities only.

## Recommended Approach

Add a dedicated `EnrichmentChangeLog` persistence model and expose it through a generic entity-scoped API. Enrichment runners or enrichment-domain services explicitly write change rows for meaningful source-derived field changes. The existing audit log remains the formal user audit trail; enrichment changes become operational provenance that can optionally be composed into entity activity timelines.

This avoids overloading the audit table with worker noise while still giving users a quick, trustworthy answer for changes like `CvssScore: 7.5 -> 8.8` from Defender or NVD.

## Data Model

Create a new canonical entity:

```csharp
public class EnrichmentChangeLog
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public string SourceKey { get; private set; } = null!;
    public Guid? EnrichmentRunId { get; private set; }
    public Guid? EnrichmentJobId { get; private set; }
    public string FieldPath { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? OldValueJson { get; private set; }
    public string? NewValueJson { get; private set; }
    public string ValueKind { get; private set; } = null!;
    public DateTimeOffset ChangedAt { get; private set; }
    public string? ChangeReason { get; private set; }
    public decimal? Confidence { get; private set; }
}
```

Indexes:

- `(TenantId, EntityType, EntityId, ChangedAt DESC)` for entity sheets and timelines.
- `(TenantId, SourceKey, ChangedAt DESC)` for source-level troubleshooting.
- `EnrichmentRunId` for drilling from run history into changed entities.
- `EnrichmentJobId` for exact job provenance.

Retention should initially match the operational data retention policy. If formal retention requirements emerge later, add a configured retention job rather than mixing these records into the user audit table.

## Value Encoding

Values are stored as JSON so scalar and structured values share the same table. The API should parse and return normalized display values, while preserving raw JSON for expandable details.

`ValueKind` should support:

- `String`
- `Number`
- `Boolean`
- `DateTime`
- `Enum`
- `Object`
- `Array`
- `Null`

The writer should skip unchanged values after normalizing semantically equivalent representations. Examples: `7.50` and `7.5` should not produce a change; unchanged nulls should not produce a change.

## Backend Flow

Add an `IEnrichmentChangeLogWriter` with scalar comparison helpers. Enrichment code captures before/after values around the mutation that applies source data.

For the first implementation slice, capture vulnerability fields:

- `Vulnerability.CvssScore`
- `Vulnerability.CvssVector`
- `Vulnerability.VendorSeverity`
- `Vulnerability.PublishedDate`

Example flow:

1. `DefenderVulnerabilityEnrichmentRunner` loads the target vulnerability.
2. It captures a small before snapshot for tracked fields.
3. `VulnerabilityResolver.ResolveAsync` applies the source data.
4. The runner or a resolver-level enrichment wrapper captures the after snapshot.
5. The writer persists one `EnrichmentChangeLog` row per changed field with `SourceKey`, `EnrichmentRunId`, `EnrichmentJobId`, and `ChangedAt`.

The preferred long-term boundary is a small reusable service around source-applied entity mutations, not scattered manual comparisons inside every runner. The initial slice can use explicit comparison in the vulnerability enrichment path if that keeps the change narrow.

## API Design

Add a generic endpoint:

```http
GET /api/enrichment-changes?entityType=Vulnerability&entityId={id}&page=1&pageSize=50
```

Supported filters:

- `sourceKey`
- `fieldPath`
- `fromDate`
- `toDate`

Response DTO:

```ts
type EnrichmentChangeDto = {
  id: string
  entityType: string
  entityId: string
  sourceKey: string
  sourceDisplayName: string | null
  fieldPath: string
  displayName: string
  oldValue: unknown
  newValue: unknown
  valueKind: string
  changedAt: string
  enrichmentRunId: string | null
  enrichmentJobId: string | null
  changeReason: string | null
  confidence: number | null
}
```

The endpoint should enforce tenant isolation by `TenantId`. For global canonical entities like `Vulnerability`, use the job tenant or requesting tenant context when writing and querying change rows. A CVE enriched for multiple tenants can therefore have tenant-local provenance, which matches source availability and UI access boundaries.

## UI Design

Create a reusable `EnrichmentChangesSheet` component. Attach it first to `VulnerabilityDetail` near existing entity actions such as work notes.

Sheet behavior:

- Right-side sheet, matching existing Radix sheet patterns.
- Header shows entity label and total recorded changes.
- Filters allow source and field narrowing.
- Rows show field display name, old value, new value, source, timestamp, and optional run/job link.
- Object or array values are collapsed by default with an expandable raw JSON detail.

Recommended row shape:

```text
CVSS score
7.5 -> 8.8
Defender · May 20, 2026 09:14 · Run 4f2...
```

Formatting rules:

- Scores and other numbers show compact numeric diff.
- Severities show existing severity tone badges.
- Booleans show yes/no badges.
- Dates use existing date/time formatting helpers.
- Unknown or null values are rendered as `None`.

The component should be generic. Entity-specific formatting can be supplied through a small field metadata map, for example:

```ts
{
  Vulnerability: {
    CvssScore: { label: 'CVSS score', kind: 'number' },
    VendorSeverity: { label: 'Vendor severity', kind: 'severity' }
  }
}
```

## Activity Timeline Integration

Keep storage separate from `AuditLogEntry`, but allow presentation composition.

For vulnerability detail, the current `Audit Timeline` tab can evolve into an `Activity` tab that combines:

- User audit events from `/api/audit-log`.
- System enrichment events from `/api/enrichment-changes`.

Each enrichment event should be labeled as source-driven system activity, for example:

```text
Defender changed CVSS score from 7.5 to 8.8.
System enrichment · May 20, 2026 09:14
```

This gives users one place to understand entity history without weakening the compliance meaning of the audit log.

## Error Handling

- If change logging fails after the entity update succeeds, log the failure and fail the enrichment job only if the system requires strict provenance. The recommended default is best-effort with structured error logging, because enrichment correctness should not be blocked by a secondary provenance write.
- If a change contains invalid or oversized structured values, store a truncated string representation and mark `ValueKind` as `String`.
- If source display name lookup fails, return the source key and leave `sourceDisplayName` null.

## Testing

Backend tests:

- Writer records one row per changed scalar field.
- Writer records no row for unchanged fields.
- Old and new values are serialized predictably.
- Tenant filtering prevents cross-tenant reads.
- Vulnerability enrichment records CVSS and severity changes with source/run/job provenance.

Frontend tests:

- Sheet renders empty state.
- Sheet renders old/new value pairs.
- Severity and null values are formatted correctly.
- Filters call the API with the expected query parameters.

## Implementation Slice

1. Add `EnrichmentChangeLog` entity, EF configuration, DbSet, and migration.
2. Add `IEnrichmentChangeLogWriter` and infrastructure implementation.
3. Capture vulnerability enrichment changes for CVSS score, CVSS vector, vendor severity, and published date.
4. Add generic API endpoint and DTOs.
5. Add frontend API schema/functions.
6. Add `EnrichmentChangesSheet`.
7. Attach the sheet to `VulnerabilityDetail`.
8. Optionally compose enrichment events into the vulnerability timeline after the sheet is working.

## Initial Decisions

- Enrichment provenance writes are best-effort initially. A logging failure should be recorded as a structured error but should not fail an otherwise successful enrichment job.
- Global canonical vulnerability changes are written as tenant-local rows using the enrichment job tenant. This keeps API access and source availability tenant scoped.
- The first UI ships as a separate reusable sheet. Renaming the audit tab to `Activity` and composing audit plus enrichment events can follow after the sheet is working.
