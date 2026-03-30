# Asset Rules — Design Document

## Goal

Allow tenants to define rules that automatically apply operations (assign security profile, assign team) to assets matching a filter. Rules execute after ingestion completes or on manual trigger. First-match-wins by priority.

## Data Model

### AssetRule Entity

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| TenantId | Guid | Tenant isolation |
| Name | string(256) | Required |
| Description | string(2048) | Nullable |
| Priority | int | Lower = higher priority. Unique per tenant. |
| Enabled | bool | Default true |
| FilterDefinition | string (JSON) | Recursive filter tree |
| Operations | string (JSON) | Array of operations |
| CreatedAt | DateTimeOffset | |
| UpdatedAt | DateTimeOffset | |
| LastExecutedAt | DateTimeOffset? | Set after rule evaluation |
| LastMatchCount | int? | Assets matched on last run |

### Filter Tree (JSON)

Recursive structure supporting AND/OR grouping:

```json
{
  "type": "group",
  "operator": "AND",
  "conditions": [
    {
      "type": "condition",
      "field": "AssetType",
      "operator": "Equals",
      "value": "Device"
    },
    {
      "type": "group",
      "operator": "OR",
      "conditions": [
        { "type": "condition", "field": "Name", "operator": "StartsWith", "value": "SRV-" },
        { "type": "condition", "field": "Tag", "operator": "Equals", "value": "production" }
      ]
    }
  ]
}
```

**Supported fields:** AssetType, Name, DeviceGroup, Vendor, Platform, Domain, Tag

**Conditional fields:**
- DeviceGroup, Platform, Domain — only valid when AssetType is constrained to Device (in same or ancestor group)
- Vendor — only valid when AssetType is constrained to Software

**Supported operators:** Equals, StartsWith, Contains, EndsWith

### Operations (JSON)

```json
[
  { "type": "AssignSecurityProfile", "parameters": { "securityProfileId": "guid" } },
  { "type": "AssignTeam", "parameters": { "teamId": "guid" } }
]
```

## Backend Architecture

### Filter Evaluation — `AssetRuleFilterBuilder`

Converts the JSON filter tree into `Expression<Func<Asset, bool>>` for EF Core SQL translation.

- Group nodes combine children with `&&` (AND) or `||` (OR)
- Condition nodes map to property comparisons:
  - `AssetType` → `a.AssetType == Enum.Parse<AssetType>(value)`
  - `Name` → `a.Name.StartsWith(value)` / `.Contains()` / `.EndsWith()` / `== value`
  - `DeviceGroup` → `a.DeviceGroupName` (same string ops)
  - `Vendor` → query against asset metadata or software-specific fields
  - `Platform` → `a.DeviceOsPlatform`
  - `Domain` → `a.DeviceComputerDnsName` (contains domain portion)
  - `Tag` → subquery: `dbContext.AssetTags.Any(t => t.AssetId == a.Id && <string op on t.Tag>)`

### Rule Evaluation — `AssetRuleEvaluationService`

1. Load all enabled rules for tenant, ordered by `Priority ASC`
2. For each rule: build filter expression, query matching asset IDs
3. Subtract already-matched asset IDs (first-match-wins)
4. Apply operations to remaining matches via batch updates
5. Update `LastExecutedAt` and `LastMatchCount` on each rule

### Operations

- **AssignSecurityProfile**: `UPDATE Assets SET SecurityProfileId = @id WHERE Id IN (@matchedIds)`
- **AssignTeam**: `UPDATE Assets SET FallbackTeamId = @id WHERE Id IN (@matchedIds)`

Both use `ExecuteUpdateAsync` for efficient batch updates (no entity loading).

### Triggering

**After ingestion:** In `IngestionService`, after the enrichment job enqueueing on successful completion, call `AssetRuleEvaluationService.EvaluateRulesAsync(tenantId, ct)`.

**Manual trigger:** `POST /api/asset-rules/run` endpoint calls the same method.

### API Endpoints (AssetRulesController)

| Method | Route | Policy | Description |
|--------|-------|--------|-------------|
| GET | `/api/asset-rules` | ConfigureTenant | List rules (paged) |
| GET | `/api/asset-rules/{id}` | ConfigureTenant | Get rule by ID |
| POST | `/api/asset-rules` | ConfigureTenant | Create rule |
| PUT | `/api/asset-rules/{id}` | ConfigureTenant | Update rule |
| DELETE | `/api/asset-rules/{id}` | ConfigureTenant | Delete rule |
| POST | `/api/asset-rules/{id}/preview` | ConfigureTenant | Preview: count + 5 sample assets |
| POST | `/api/asset-rules/run` | ConfigureTenant | Manual trigger for all rules |
| PUT | `/api/asset-rules/reorder` | ConfigureTenant | Batch-update priorities |

## Frontend Architecture

### Routes

- `/admin/asset-rules` — List view with DataTableWorkbench
- `/admin/asset-rules/new` — Wizard for creating a rule (4 steps)
- `/admin/asset-rules/$id` — Wizard in edit mode

### Wizard Steps

1. **Basic Info** — Name, description
2. **Filters** — Filter builder with preview
3. **Operations** — Select and configure operations
4. **Summary** — Review and save

### Filter Builder Component

Recursive `FilterGroup` + `FilterCondition` components:
- Group: AND/OR toggle, list of children (conditions or nested groups), add condition/group buttons, remove group button
- Condition: field select → operator select → value input, remove button
- Field options change based on AssetType constraints in the tree
- Preview button calls `/api/asset-rules/{id}/preview` and shows count + sample list

### API Layer

- `asset-rules.schemas.ts` — Zod schemas for rule, filter tree, operations, preview response
- `asset-rules.functions.ts` — Server functions following existing patterns

### Admin Navigation

- Add "Asset Rules" card to `/admin` landing page
- Uses `ConfigureTenant` role access (same as existing admin features)
