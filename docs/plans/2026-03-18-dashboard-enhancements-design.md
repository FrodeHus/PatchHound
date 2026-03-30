# Dashboard Enhancements: Device Group Chart, Filters, Health Metric

## Problem

The dashboard lacks visibility into how vulnerabilities distribute across device groups, has no way to filter dashboard data by age/platform/group, and doesn't surface device health status.

## Decisions

- **Known exploits filter**: Deferred — no exploit flag exists on the data model yet.
- **Unhealthy definition**: Only `DeviceHealthStatus != "Active"` counts as unhealthy. Null/unknown devices are excluded.
- **Filter scope**: Dashboard-level — all cards/charts re-filter when toggled.
- **Age filter options**: All, >=30 days, >=90 days, >=180 days.

---

## 1. Dashboard Filter Bar

A filter bar at the top of the dashboard page with three controls:

| Filter | Type | Options | Default |
|--------|------|---------|---------|
| Age | Select | All, >=30 days, >=90 days, >=180 days | All |
| Platform | Select | "All platforms" + distinct `Asset.DeviceOsPlatform` values | All |
| Device Group | Select | "All groups" + distinct `Asset.DeviceGroupName` values | All |

Filter values stored in route search params (URL state). All filters passed as query parameters to API endpoints.

### API Changes

**`GET /dashboard/summary`** and **`GET /dashboard/trends`** gain optional query params:

- `minAgeDays` (int?) — filters to vulnerabilities where `PublishedDate <= UtcNow - N days`
- `platform` (string?) — filters to vulnerabilities affecting assets with matching `DeviceOsPlatform`
- `deviceGroup` (string?) — filters to vulnerabilities affecting assets with matching `DeviceGroupName`

**New endpoint: `GET /dashboard/filter-options`**

Returns distinct platform and device group values for the tenant:

```
DashboardFilterOptionsDto(
    string[] Platforms,
    string[] DeviceGroups
)
```

Query: distinct non-null `DeviceOsPlatform` and `DeviceGroupName` from `Assets` where `AssetType == Device`.

---

## 2. Vulnerabilities by Device Group Chart

New stacked bar chart (`DeviceGroupVulnerabilityChart.tsx`) using Recharts. Each bar = a device group, stacked by severity (Critical/High/Medium/Low). Same color scheme as existing trend chart.

### Data Model

New field on `DashboardSummaryDto`:

```
VulnerabilitiesByDeviceGroup: List<DeviceGroupVulnerabilityDto>
```

```
DeviceGroupVulnerabilityDto(
    string DeviceGroupName,
    int Critical,
    int High,
    int Medium,
    int Low
)
```

### Query Logic

Join `VulnerabilityAsset` → `Asset` (where `AssetType == Device`) → `TenantVulnerability` → `VulnerabilityDefinition`. Group by `Asset.DeviceGroupName`, count by `VendorSeverity`. Null group name → "Ungrouped". Sorted by total descending, top 10. Respects dashboard-level filters.

### Placement

Below the existing trend chart row on the dashboard.

---

## 3. Device Health Metric Card

New card (`DeviceHealthCard.tsx`) showing device health breakdown.

### Layout

Similar to `ExposureSlaCard`. Shows a breakdown by health status value:

- **Active: N** (healthy)
- **Inactive: N** (clickable → `/assets?assetType=Device&healthStatus=Inactive`)
- **ImpairedCommunication: N** (clickable → same pattern)
- etc.

Each non-Active row navigates to the assets page filtered on `assetType=Device&healthStatus=<value>` using the existing exact-match filter.

### Data Model

New field on `DashboardSummaryDto`:

```
DeviceHealthBreakdown: Dictionary<string, int>
```

Keys = health status values, values = counts. Only devices (`AssetType == Device`) with non-null `DeviceHealthStatus`.

**Not affected by vulnerability-focused dashboard filters** (age/platform/device group) — this is a device inventory metric.

### Placement

Alongside the ExposureSlaCard row.

---

## 4. Loading States

All dashboard cards/charts show skeleton placeholders during fetch and re-fetch.

- Each component receives an `isLoading` prop
- When true: render `animate-pulse` + `bg-muted` blocks matching component dimensions
- Uses TanStack Query `isFetching` (covers initial load and background re-fetches on filter change)
- No spinners — skeletons keep layout stable

---

## Summary of API Changes

| Endpoint | Change |
|----------|--------|
| `GET /dashboard/summary` | Add `minAgeDays`, `platform`, `deviceGroup` query params. Add `VulnerabilitiesByDeviceGroup` and `DeviceHealthBreakdown` to response. |
| `GET /dashboard/trends` | Add `minAgeDays`, `platform`, `deviceGroup` query params. |
| `GET /dashboard/filter-options` | New endpoint returning distinct platforms and device groups. |

## Summary of Frontend Changes

| Component | Change |
|-----------|--------|
| Dashboard route | Add filter bar with search params, pass `isLoading` to all components |
| `DashboardFilterBar.tsx` | New — age select, platform select, device group select |
| `DeviceGroupVulnerabilityChart.tsx` | New — stacked bar chart |
| `DeviceHealthCard.tsx` | New — health breakdown with clickable navigation |
| All existing dashboard components | Add `isLoading` prop with skeleton state |
| `dashboard.schemas.ts` | Add new fields + filter options schema |
| `dashboard.functions.ts` | Add filter params to existing functions + new `fetchDashboardFilterOptions` |
