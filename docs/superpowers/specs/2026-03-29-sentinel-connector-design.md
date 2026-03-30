# Microsoft Sentinel CCF Push Connector — Design Spec

## Overview

Add a global Microsoft Sentinel integration that forwards audit log events in real-time via the Codeless Connector Framework (CCF) push model. Events are queued in-memory and pushed asynchronously — the database save path is never blocked or affected by Sentinel availability.

A new **Integrations** admin section houses the connector configuration UI.

## Data Flow

```
DB SaveChanges
  -> AuditSaveChangesInterceptor creates AuditLogEntry
  -> SentinelAuditQueue.TryWrite(event)  [non-blocking, sync]
  -> Save completes normally regardless of Sentinel state

        | (Channel<SentinelAuditEvent>)

SentinelConnectorWorker (BackgroundService)
  -> Reads from channel
  -> Batches up to 100 items or 500ms window
  -> Authenticates via MSAL ConfidentialClientApplication (token cached)
  -> POST to DCE Logs Ingestion API
  -> On failure: log warning, drop batch, continue
```

## Data Model

### Entity: SentinelConnectorConfiguration

Single global row. Stored in `PatchHoundDbContext`.

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | PK |
| Enabled | bool | Master toggle |
| DceEndpoint | string | Data Collection Endpoint URI |
| DcrImmutableId | string | Data Collection Rule Immutable ID |
| StreamName | string | e.g. `Custom-PatchHoundAuditLog` |
| TenantId | string | Entra tenant ID for OAuth |
| ClientId | string | Entra app client ID |
| SecretRef | string | Vault reference for client secret |
| UpdatedAt | DateTimeOffset | Last config change |

Client secret stored in OpenBao at path `system/sentinel-connector`, key `clientSecret`.

### Record: SentinelAuditEvent

Lightweight value type mapped from `AuditLogEntry` fields. No EF entity references.

| Field | Type |
|-------|------|
| AuditEntryId | Guid |
| TenantId | Guid |
| EntityType | string |
| EntityId | Guid |
| Action | string |
| OldValues | string? |
| NewValues | string? |
| UserId | Guid |
| Timestamp | DateTimeOffset |

### Sentinel Custom Table Schema

What gets POSTed to the Logs Ingestion API:

```json
{
  "TimeGenerated": "2026-03-29T12:00:00Z",
  "TenantId": "guid",
  "EntityType": "Vulnerability",
  "EntityId": "guid",
  "Action": "Updated",
  "OldValues": "{ ... }",
  "NewValues": "{ ... }",
  "UserId": "guid",
  "AuditEntryId": "guid"
}
```

## Backend Services

### SentinelAuditQueue (Singleton)

- Wraps `Channel<SentinelAuditEvent>` — bounded at 10,000 capacity, `BoundedChannelFullMode.DropOldest`
- `TryWrite(SentinelAuditEvent)` — non-blocking, returns bool
- `ReadAllAsync(CancellationToken)` — exposes the channel reader for the worker

### SentinelConnectorWorker (BackgroundService, Hosted)

- Reads from `SentinelAuditQueue` in a loop
- Micro-batches: collects events for up to 500ms or 100 items, whichever comes first
- Authenticates to Entra via MSAL `ConfidentialClientApplication` (scope: `https://monitor.azure.com/.default`), token cached automatically by MSAL
- POSTs batch as JSON array to `{DceEndpoint}/dataCollectionRules/{DcrImmutableId}/streams/{StreamName}?api-version=2023-01-01`
- On failure: logs warning, discards batch, continues (best-effort forwarding)
- When connector is disabled or not configured: drains the channel silently (discard events) and sleeps, checking config periodically

### SentinelConnectorConfigurationService (Scoped)

- Reads the single config row from DB
- On GET: returns config with secret masked
- On PUT: updates DB row, writes secret to vault (only when non-empty), signals config reload
- Config reload: the worker re-reads the config row from DB at the start of each batch cycle (single-row query, at most every 500ms) — no signaling mechanism or restart needed

### Integration Point: AuditSaveChangesInterceptor

The existing interceptor gets `SentinelAuditQueue` injected (singleton). After creating each `AuditLogEntry`, it calls `TryWrite` with a mapped `SentinelAuditEvent`. One extra line. No async. No await. The save path is completely unaffected.

## API Endpoints

New controller: `IntegrationsController` (route: `/api/integrations`)

| Method | Path | Policy | Description |
|--------|------|--------|-------------|
| GET | `/sentinel-connector` | ManageVault (GlobalAdmin) | Returns current config, secret masked |
| PUT | `/sentinel-connector` | ManageVault (GlobalAdmin) | Creates/updates config + vault secret |

Follows the enrichment source pattern:
- Collect pending secret writes
- Save config to DB
- Write secret to vault after DB commit
- Secret field only written when non-empty in request

## Frontend

### New Admin Area: Integrations

Added to `adminAreas` array in `admin/index.tsx`:
- Title: "Integrations"
- Description: "External service connectors"
- Route: `/admin/integrations`
- Role: GlobalAdmin
- Icon: `Plug` (lucide-react)

### Route: /admin/integrations

Landing page with a card for "Microsoft Sentinel Data Connector". Clicking opens the connector configuration form inline.

### Sentinel Connector Form

| Field | Input Type | Notes |
|-------|-----------|-------|
| Enabled | Toggle switch | Master on/off |
| DCE Endpoint | Text | `https://<name>.ingest.monitor.azure.com` |
| DCR Immutable ID | Text | `dcr-xxxxxxxx...` |
| Stream Name | Text | `Custom-PatchHoundAuditLog` |
| Entra Tenant ID | Text | Azure AD tenant GUID |
| Client ID | Text | App registration client ID |
| Client Secret | Password | Only sent on change, shown as masked when set |

Empty state: "Not configured" with setup prompt. When configured and enabled: shows status indicator.

### New Frontend Files

- `frontend/src/routes/_authed/admin/integrations/index.tsx` — landing page
- `frontend/src/components/features/admin/SentinelConnectorCard.tsx` — form component
- `frontend/src/api/integrations.functions.ts` — server functions (GET/PUT)
- `frontend/src/api/integrations.schemas.ts` — Zod schemas

## DI Registration

In `DependencyInjection.cs` / `Program.cs`:
- `SentinelAuditQueue` — singleton
- `SentinelConnectorWorker` — hosted service (`AddHostedService`)
- `SentinelConnectorConfigurationService` — scoped

`IntegrationsController` discovered via existing `AddControllers()`.

## EF Migration

One new migration adding `SentinelConnectorConfigurations` table with the fields listed above.

## Decisions

- **Global, not per-tenant.** Single connector for the whole system. TenantId is included in the event payload for Sentinel-side filtering.
- **Fire-and-forget.** The save path never waits for or fails on Sentinel. Channel is bounded with drop-oldest overflow.
- **No retry on push failure.** Audit events are best-effort forwarding. The authoritative audit trail remains in the PatchHound database.
- **No DELETE endpoint.** Disabling via the Enabled toggle is sufficient.
- **MSAL for auth.** Token caching and refresh handled by the library.
