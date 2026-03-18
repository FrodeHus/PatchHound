# Dashboard Enhancements Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a vulnerabilities-by-device-group stacked bar chart, a device health metric card, dashboard-level filters (age/platform/device group), and loading skeletons to all dashboard components.

**Architecture:** Add a `DashboardFilterQuery` record for filter params, a new `GET /dashboard/filter-options` endpoint, extend the summary and trends endpoints to accept filters, add two new frontend components, and retrofit all existing components with `isLoading` skeleton states. Queries join through `VulnerabilityAsset` → `Asset` to apply platform/device group filters.

**Tech Stack:** C# / EF Core (PostgreSQL), xUnit + InMemory, TanStack Start (React + Vite), Zod, Recharts, Tailwind CSS.

---

### Task 1: Add DTOs and Filter Query Record

**Files:**
- Modify: `src/PatchHound.Api/Models/Dashboard/DashboardSummaryDto.cs`

**Step 1: Add new DTOs and filter query**

Add the following records to the end of the file:

```csharp
public record DashboardFilterQuery(
    int? MinAgeDays = null,
    string? Platform = null,
    string? DeviceGroup = null
);

public record DeviceGroupVulnerabilityDto(
    string DeviceGroupName,
    int Critical,
    int High,
    int Medium,
    int Low
);

public record DashboardFilterOptionsDto(
    string[] Platforms,
    string[] DeviceGroups
);
```

Extend `DashboardSummaryDto` to include two new fields at the end:

```csharp
public record DashboardSummaryDto(
    decimal ExposureScore,
    Dictionary<string, int> VulnerabilitiesBySeverity,
    Dictionary<string, int> VulnerabilitiesByStatus,
    decimal SlaCompliancePercent,
    int OverdueTaskCount,
    int TotalTaskCount,
    decimal AverageRemediationDays,
    List<TopVulnerabilityDto> TopCriticalVulnerabilities,
    DashboardRiskChangeBriefDto RiskChangeBrief,
    int RecurringVulnerabilityCount,
    decimal RecurrenceRatePercent,
    List<RecurringVulnerabilityDto> TopRecurringVulnerabilities,
    List<RecurringAssetDto> TopRecurringAssets,
    List<DeviceGroupVulnerabilityDto> VulnerabilitiesByDeviceGroup,
    Dictionary<string, int> DeviceHealthBreakdown
);
```

**Step 2: Build and verify compilation**

Run: `dotnet build PatchHound.slnx`
Expected: Compilation errors in DashboardController (constructor call mismatch) — this is expected and will be fixed in Task 2.

**Step 3: Commit**

```bash
git add src/PatchHound.Api/Models/Dashboard/DashboardSummaryDto.cs
git commit -m "feat: add dashboard filter query, device group and health DTOs"
```

---

### Task 2: Add Filter Options Endpoint + Wire Filters into Summary and Trends

**Files:**
- Modify: `src/PatchHound.Api/Controllers/DashboardController.cs`

**Step 1: Add `DashboardFilterQuery` parameter to `GetSummary`**

Change the signature from:

```csharp
public async Task<ActionResult<DashboardSummaryDto>> GetSummary(CancellationToken ct)
```

to:

```csharp
public async Task<ActionResult<DashboardSummaryDto>> GetSummary(
    [FromQuery] DashboardFilterQuery filter,
    CancellationToken ct
)
```

**Step 2: Build a filtered asset ID set**

After `var activeSnapshotId = ...` (line 45), add a helper that resolves the set of asset IDs matching the platform/device group filters:

```csharp
// Build filtered asset IDs if platform or device group filters are active
HashSet<Guid>? filteredAssetIds = null;
if (!string.IsNullOrEmpty(filter.Platform) || !string.IsNullOrEmpty(filter.DeviceGroup))
{
    var assetQuery = _dbContext.Assets.AsNoTracking()
        .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device);
    if (!string.IsNullOrEmpty(filter.Platform))
        assetQuery = assetQuery.Where(a => a.DeviceOsPlatform == filter.Platform);
    if (!string.IsNullOrEmpty(filter.DeviceGroup))
        assetQuery = assetQuery.Where(a => a.DeviceGroupName != null && a.DeviceGroupName.Contains(filter.DeviceGroup));
    filteredAssetIds = (await assetQuery.Select(a => a.Id).ToListAsync(ct)).ToHashSet();
}
```

Also compute the min published date cutoff:

```csharp
DateTimeOffset? minPublishedDate = filter.MinAgeDays.HasValue
    ? DateTimeOffset.UtcNow.AddDays(-filter.MinAgeDays.Value)
    : null;
```

**Step 3: Apply filters to existing queries**

For the `bySeverity` query (line 56), wrap the existing WHERE with additional filter conditions. After the `.Where(v => v.TenantId == tenantId && ...)` add:

- If `minPublishedDate` is set: `.Where(v => v.VulnerabilityDefinition.PublishedDate <= minPublishedDate)`
- If `filteredAssetIds` is set: filter to vulnerabilities that have at least one open episode on a matching asset:

```csharp
if (minPublishedDate.HasValue)
    query = query.Where(v => v.VulnerabilityDefinition.PublishedDate <= minPublishedDate);
if (filteredAssetIds is not null)
    query = query.Where(v =>
        _dbContext.VulnerabilityAssetEpisodes.Any(e =>
            e.TenantVulnerabilityId == v.Id
            && e.Status == VulnerabilityStatus.Open
            && filteredAssetIds.Contains(e.AssetId)
        )
    );
```

Apply the same pattern to: `openVulnerabilityCount` query, `topVulns` query, `vulnerabilityAssetPairs` query (for exposure score). For `vulnerabilityAssetPairs`, also filter the asset join by platform/device group directly.

For recurrence and risk change brief — pass the filter through (these are harder to filter efficiently; for the initial implementation, apply age filter only).

**Step 4: Compute VulnerabilitiesByDeviceGroup**

After the existing recurrence data block (line 178), add:

```csharp
// Vulnerabilities by device group
var deviceGroupQuery = _dbContext.VulnerabilityAssets.AsNoTracking()
    .Where(va => va.SnapshotId == activeSnapshotId && va.Status == VulnerabilityStatus.Open)
    .Join(
        _dbContext.TenantVulnerabilities.AsNoTracking().Where(v => v.TenantId == tenantId),
        va => va.TenantVulnerabilityId,
        v => v.Id,
        (va, v) => new { va.AssetId, v.VulnerabilityDefinition.VendorSeverity, v.VulnerabilityDefinition.PublishedDate }
    )
    .Join(
        _dbContext.Assets.AsNoTracking().Where(a => a.AssetType == AssetType.Device),
        x => x.AssetId,
        a => a.Id,
        (x, a) => new { a.DeviceGroupName, a.DeviceOsPlatform, x.VendorSeverity, x.PublishedDate }
    );

if (minPublishedDate.HasValue)
    deviceGroupQuery = deviceGroupQuery.Where(x => x.PublishedDate <= minPublishedDate);
if (!string.IsNullOrEmpty(filter.Platform))
    deviceGroupQuery = deviceGroupQuery.Where(x => x.DeviceOsPlatform == filter.Platform);
if (!string.IsNullOrEmpty(filter.DeviceGroup))
    deviceGroupQuery = deviceGroupQuery.Where(x => x.DeviceGroupName != null && x.DeviceGroupName.Contains(filter.DeviceGroup));

var deviceGroupRows = await deviceGroupQuery
    .GroupBy(x => new { GroupName = x.DeviceGroupName ?? "Ungrouped", x.VendorSeverity })
    .Select(g => new { g.Key.GroupName, g.Key.VendorSeverity, Count = g.Count() })
    .ToListAsync(ct);

var vulnsByDeviceGroup = deviceGroupRows
    .GroupBy(r => r.GroupName)
    .Select(g => new DeviceGroupVulnerabilityDto(
        g.Key,
        g.FirstOrDefault(r => r.VendorSeverity == Severity.Critical)?.Count ?? 0,
        g.FirstOrDefault(r => r.VendorSeverity == Severity.High)?.Count ?? 0,
        g.FirstOrDefault(r => r.VendorSeverity == Severity.Medium)?.Count ?? 0,
        g.FirstOrDefault(r => r.VendorSeverity == Severity.Low)?.Count ?? 0
    ))
    .OrderByDescending(d => d.Critical + d.High + d.Medium + d.Low)
    .Take(10)
    .ToList();
```

**Step 5: Compute DeviceHealthBreakdown**

```csharp
// Device health breakdown (not affected by vulnerability filters)
var healthBreakdown = await _dbContext.Assets.AsNoTracking()
    .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device && a.DeviceHealthStatus != null)
    .GroupBy(a => a.DeviceHealthStatus!)
    .Select(g => new { Status = g.Key, Count = g.Count() })
    .ToDictionaryAsync(g => g.Status, g => g.Count, ct);
```

**Step 6: Update the DashboardSummaryDto construction**

Add the two new fields to the constructor call (after `recurrence.TopRecurringAssets`):

```csharp
vulnsByDeviceGroup,
healthBreakdown
```

**Step 7: Add filter param to GetTrends**

Change signature to accept `[FromQuery] DashboardFilterQuery filter`. Apply the same `minPublishedDate` and `filteredAssetIds` logic to the episode query. Add a WHERE on `PublishedDate` by joining to `TenantVulnerabilities` → `VulnerabilityDefinition`:

For the episode join query (line 221-243), add additional joins/filters for platform and device group by joining to Assets, and add a where clause for `PublishedDate` via the VulnerabilityDefinition.

**Step 8: Add filter-options endpoint**

```csharp
[HttpGet("filter-options")]
public async Task<ActionResult<DashboardFilterOptionsDto>> GetFilterOptions(CancellationToken ct)
{
    if (_tenantContext.CurrentTenantId is not Guid tenantId)
        return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

    var platforms = await _dbContext.Assets.AsNoTracking()
        .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device && a.DeviceOsPlatform != null)
        .Select(a => a.DeviceOsPlatform!)
        .Distinct()
        .OrderBy(p => p)
        .ToArrayAsync(ct);

    var deviceGroups = await _dbContext.Assets.AsNoTracking()
        .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device && a.DeviceGroupName != null)
        .Select(a => a.DeviceGroupName!)
        .Distinct()
        .OrderBy(g => g)
        .ToArrayAsync(ct);

    return Ok(new DashboardFilterOptionsDto(platforms, deviceGroups));
}
```

**Step 9: Build and run tests**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: Build succeeds. Tests pass (existing tests call `GetSummary` without filters, which is fine since `DashboardFilterQuery` has all-null defaults via model binding).

**Step 10: Commit**

```bash
git add src/PatchHound.Api/Controllers/DashboardController.cs
git commit -m "feat: add dashboard filters, device group breakdown, health breakdown, filter-options endpoint"
```

---

### Task 3: Backend Tests for New Dashboard Features

**Files:**
- Modify: `tests/PatchHound.Tests/Api/DashboardControllerTests.cs`

**Step 1: Add test for device group vulnerability breakdown**

```csharp
[Fact]
public async Task GetSummary_ReturnsVulnerabilitiesByDeviceGroup()
{
    var tier0Asset = Asset.Create(_tenantId, "srv-1", AssetType.Device, "Server 1", Criticality.High);
    tier0Asset.UpdateDeviceDetails("srv-1.local", "Active", "Windows", "2022", "High", DateTimeOffset.UtcNow, "10.0.0.1", null, null, "Tier 0 Servers");
    var fieldAsset = Asset.Create(_tenantId, "ws-1", AssetType.Device, "Workstation 1", Criticality.Medium);
    fieldAsset.UpdateDeviceDetails("ws-1.local", "Active", "Windows", "11", "Medium", DateTimeOffset.UtcNow, "10.0.0.2", null, null, "Field Devices");

    var vulnDef = VulnerabilityDefinition.Create("CVE-2026-0001", "Test Vuln", "desc", Severity.High, 7.5m, null, null, null, null, DateTimeOffset.UtcNow.AddDays(-10), "Defender");
    var tenantVuln = TenantVulnerability.Create(_tenantId, vulnDef.Id);

    await _dbContext.AddRangeAsync(tier0Asset, fieldAsset, vulnDef, tenantVuln);
    await _dbContext.SaveChangesAsync();

    var episode1 = VulnerabilityAssetEpisode.Create(_tenantId, tenantVuln.Id, tier0Asset.Id, DateTimeOffset.UtcNow.AddDays(-5));
    var episode2 = VulnerabilityAssetEpisode.Create(_tenantId, tenantVuln.Id, fieldAsset.Id, DateTimeOffset.UtcNow.AddDays(-3));
    await _dbContext.AddRangeAsync(episode1, episode2);
    await _dbContext.SaveChangesAsync();

    var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);
    var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
    var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

    payload.VulnerabilitiesByDeviceGroup.Should().HaveCountGreaterOrEqualTo(1);
    payload.VulnerabilitiesByDeviceGroup.Should().Contain(d => d.DeviceGroupName == "Tier 0 Servers");
}
```

**Step 2: Add test for device health breakdown**

```csharp
[Fact]
public async Task GetSummary_ReturnsDeviceHealthBreakdown()
{
    var activeDevice = Asset.Create(_tenantId, "dev-active", AssetType.Device, "Active Device", Criticality.Medium);
    activeDevice.UpdateDeviceDetails("active.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.1", null);

    var inactiveDevice = Asset.Create(_tenantId, "dev-inactive", AssetType.Device, "Inactive Device", Criticality.Medium);
    inactiveDevice.UpdateDeviceDetails("inactive.local", "Inactive", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.2", null);

    await _dbContext.AddRangeAsync(activeDevice, inactiveDevice);
    await _dbContext.SaveChangesAsync();

    var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);
    var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
    var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

    payload.DeviceHealthBreakdown.Should().ContainKey("Active").WhoseValue.Should().Be(1);
    payload.DeviceHealthBreakdown.Should().ContainKey("Inactive").WhoseValue.Should().Be(1);
}
```

**Step 3: Add test for age filter**

```csharp
[Fact]
public async Task GetSummary_FiltersByMinAgeDays()
{
    var asset = Asset.Create(_tenantId, "dev-age", AssetType.Device, "Device", Criticality.Medium);
    asset.UpdateDeviceDetails("dev.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.1", null);

    var oldVulnDef = VulnerabilityDefinition.Create("CVE-2025-0001", "Old Vuln", "desc", Severity.High, 7.0m, null, null, null, null, DateTimeOffset.UtcNow.AddDays(-100), "Defender");
    var recentVulnDef = VulnerabilityDefinition.Create("CVE-2026-0002", "Recent Vuln", "desc", Severity.Medium, 5.0m, null, null, null, null, DateTimeOffset.UtcNow.AddDays(-10), "Defender");
    var oldTenantVuln = TenantVulnerability.Create(_tenantId, oldVulnDef.Id);
    var recentTenantVuln = TenantVulnerability.Create(_tenantId, recentVulnDef.Id);

    await _dbContext.AddRangeAsync(asset, oldVulnDef, recentVulnDef, oldTenantVuln, recentTenantVuln);
    await _dbContext.SaveChangesAsync();

    var oldEpisode = VulnerabilityAssetEpisode.Create(_tenantId, oldTenantVuln.Id, asset.Id, DateTimeOffset.UtcNow.AddDays(-100));
    var recentEpisode = VulnerabilityAssetEpisode.Create(_tenantId, recentTenantVuln.Id, asset.Id, DateTimeOffset.UtcNow.AddDays(-10));
    await _dbContext.AddRangeAsync(oldEpisode, recentEpisode);
    await _dbContext.SaveChangesAsync();

    // Filter to >= 90 days old: should only show the old vulnerability
    var action = await _controller.GetSummary(new DashboardFilterQuery(MinAgeDays: 90), CancellationToken.None);
    var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
    var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

    // Only the old vulnerability (High) should be counted
    payload.VulnerabilitiesBySeverity["High"].Should().Be(1);
    payload.VulnerabilitiesBySeverity["Medium"].Should().Be(0);
}
```

**Step 4: Run tests**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All pass.

**Step 5: Commit**

```bash
git add tests/PatchHound.Tests/Api/DashboardControllerTests.cs
git commit -m "test: add dashboard device group, health breakdown, and age filter tests"
```

---

### Task 4: Frontend — Schemas, Server Functions, Filter Options

**Files:**
- Modify: `frontend/src/api/dashboard.schemas.ts`
- Modify: `frontend/src/api/dashboard.functions.ts`

**Step 1: Update Zod schemas**

Add to `dashboardSummarySchema` (after `topRecurringAssets`):

```typescript
vulnerabilitiesByDeviceGroup: z.array(z.object({
  deviceGroupName: z.string(),
  critical: z.number(),
  high: z.number(),
  medium: z.number(),
  low: z.number(),
})),
deviceHealthBreakdown: z.record(z.string(), z.number()),
```

Add new schemas:

```typescript
export const dashboardFilterOptionsSchema = z.object({
  platforms: z.array(z.string()),
  deviceGroups: z.array(z.string()),
})

export type DashboardFilterOptions = z.infer<typeof dashboardFilterOptionsSchema>
export type DeviceGroupVulnerability = z.infer<typeof dashboardSummarySchema>['vulnerabilitiesByDeviceGroup'][number]
```

**Step 2: Update server functions**

Add filter params to `fetchDashboardSummary` and `fetchDashboardTrends`:

```typescript
export const fetchDashboardSummary = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      minAgeDays: z.number().optional(),
      platform: z.string().optional(),
      deviceGroup: z.string().optional(),
    }),
  )
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    const data = await apiGet(`/dashboard/summary?${params.toString()}`, context)
    return dashboardSummarySchema.parse(data)
  })
```

Same pattern for `fetchDashboardTrends`. Add `import { buildFilterParams } from './utils'`.

Add new function:

```typescript
export const fetchDashboardFilterOptions = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/filter-options', context)
    return dashboardFilterOptionsSchema.parse(data)
  })
```

**Step 3: Run frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint`
Expected: Pass (pre-existing errors only).

**Step 4: Commit**

```bash
git add frontend/src/api/dashboard.schemas.ts frontend/src/api/dashboard.functions.ts
git commit -m "feat: update dashboard schemas and functions with filters and new fields"
```

---

### Task 5: Frontend — Dashboard Filter Bar Component

**Files:**
- Create: `frontend/src/components/features/dashboard/DashboardFilterBar.tsx`

**Step 1: Create the filter bar component**

```typescript
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { DashboardFilterOptions } from '@/api/dashboard.schemas'

type DashboardFilterBarProps = {
  minAgeDays: string
  platform: string
  deviceGroup: string
  filterOptions: DashboardFilterOptions | undefined
  onMinAgeDaysChange: (value: string) => void
  onPlatformChange: (value: string) => void
  onDeviceGroupChange: (value: string) => void
}

const ageOptions = [
  { label: 'All ages', value: '' },
  { label: '≥ 30 days', value: '30' },
  { label: '≥ 90 days', value: '90' },
  { label: '≥ 180 days', value: '180' },
]

export function DashboardFilterBar({
  minAgeDays,
  platform,
  deviceGroup,
  filterOptions,
  onMinAgeDaysChange,
  onPlatformChange,
  onDeviceGroupChange,
}: DashboardFilterBarProps) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">Filters</p>

      <Select value={minAgeDays || 'all'} onValueChange={(v) => onMinAgeDaysChange(v === 'all' ? '' : v)}>
        <SelectTrigger className="h-9 min-w-[140px] rounded-xl border-border/70 bg-background/80 px-3">
          <SelectValue placeholder="All ages" />
        </SelectTrigger>
        <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
          {ageOptions.map((opt) => (
            <SelectItem key={opt.value || 'all'} value={opt.value || 'all'}>{opt.label}</SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select value={platform || 'all'} onValueChange={(v) => onPlatformChange(v === 'all' ? '' : v)}>
        <SelectTrigger className="h-9 min-w-[150px] rounded-xl border-border/70 bg-background/80 px-3">
          <SelectValue placeholder="All platforms" />
        </SelectTrigger>
        <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
          <SelectItem value="all">All platforms</SelectItem>
          {filterOptions?.platforms.map((p) => (
            <SelectItem key={p} value={p}>{p}</SelectItem>
          ))}
        </SelectContent>
      </Select>

      <Select value={deviceGroup || 'all'} onValueChange={(v) => onDeviceGroupChange(v === 'all' ? '' : v)}>
        <SelectTrigger className="h-9 min-w-[160px] rounded-xl border-border/70 bg-background/80 px-3">
          <SelectValue placeholder="All groups" />
        </SelectTrigger>
        <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
          <SelectItem value="all">All groups</SelectItem>
          {filterOptions?.deviceGroups.map((g) => (
            <SelectItem key={g} value={g}>{g}</SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}
```

**Step 2: Commit**

```bash
git add frontend/src/components/features/dashboard/DashboardFilterBar.tsx
git commit -m "feat: add DashboardFilterBar component"
```

---

### Task 6: Frontend — Device Group Vulnerability Chart

**Files:**
- Create: `frontend/src/components/features/dashboard/DeviceGroupVulnerabilityChart.tsx`

**Step 1: Create the stacked bar chart component**

```typescript
import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import type { DashboardSummary } from '@/api/dashboard.schemas'

type DeviceGroupVulnerabilityChartProps = {
  data: DashboardSummary['vulnerabilitiesByDeviceGroup']
  isLoading?: boolean
}

export function DeviceGroupVulnerabilityChart({ data, isLoading }: DeviceGroupVulnerabilityChartProps) {
  const legend = [
    { label: 'Low', className: 'bg-chart-2' },
    { label: 'Medium', className: 'bg-chart-4' },
    { label: 'High', className: 'bg-chart-1' },
    { label: 'Critical', className: 'bg-destructive' },
  ]

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="p-5 pb-4">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Device groups</p>
            <CardTitle className="mt-2 text-xl font-semibold tracking-tight">Vulnerabilities by device group</CardTitle>
          </div>
          <div className="flex flex-wrap gap-2">
            {legend.map((item) => (
              <Badge key={item.label} variant="outline" className="rounded-full border-border/70 bg-background/30 px-2.5 py-1 text-xs text-foreground">
                <span className={`mr-2 inline-block size-2 rounded-full ${item.className}`} />
                {item.label}
              </Badge>
            ))}
          </div>
        </div>
      </CardHeader>
      <CardContent className="pt-0">
        {isLoading ? (
          <div className="h-[300px] w-full animate-pulse rounded-2xl bg-muted/60" />
        ) : data.length === 0 ? (
          <p className="py-12 text-center text-sm text-muted-foreground">No device group data available.</p>
        ) : (
          <div className="h-[300px] w-full">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={data}>
                <CartesianGrid vertical={false} stroke="color-mix(in oklab, var(--border) 85%, transparent)" />
                <XAxis dataKey="deviceGroupName" axisLine={false} tickLine={false} tick={{ fill: 'var(--color-muted-foreground)', fontSize: 12 }} />
                <YAxis allowDecimals={false} axisLine={false} tickLine={false} tick={{ fill: 'var(--color-muted-foreground)', fontSize: 12 }} />
                <Tooltip
                  cursor={{ fill: 'color-mix(in oklab, var(--accent) 32%, transparent)' }}
                  contentStyle={{
                    background: 'var(--color-popover)',
                    border: '1px solid color-mix(in oklab, var(--border) 90%, transparent)',
                    borderRadius: '16px',
                    color: 'var(--color-popover-foreground)',
                  }}
                />
                <Bar dataKey="low" stackId="severity" fill="var(--color-chart-2)" radius={[0, 0, 4, 4]} name="Low" />
                <Bar dataKey="medium" stackId="severity" fill="var(--color-chart-4)" />
                <Bar dataKey="high" stackId="severity" fill="var(--color-chart-1)" />
                <Bar dataKey="critical" stackId="severity" fill="var(--color-destructive)" radius={[10, 10, 0, 0]} name="Critical" />
              </BarChart>
            </ResponsiveContainer>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
```

**Step 2: Commit**

```bash
git add frontend/src/components/features/dashboard/DeviceGroupVulnerabilityChart.tsx
git commit -m "feat: add DeviceGroupVulnerabilityChart component"
```

---

### Task 7: Frontend — Device Health Card

**Files:**
- Create: `frontend/src/components/features/dashboard/DeviceHealthCard.tsx`

**Step 1: Create the health breakdown card**

```typescript
import { Link } from '@tanstack/react-router'
import { HeartPulse } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'

type DeviceHealthCardProps = {
  healthBreakdown: Record<string, number>
  isLoading?: boolean
}

export function DeviceHealthCard({ healthBreakdown, isLoading }: DeviceHealthCardProps) {
  const entries = Object.entries(healthBreakdown).sort(([a], [b]) => {
    if (a === 'Active') return -1
    if (b === 'Active') return 1
    return a.localeCompare(b)
  })
  const total = entries.reduce((sum, [, count]) => sum + count, 0)
  const healthy = healthBreakdown['Active'] ?? 0
  const unhealthy = total - healthy

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardContent className="p-5">
        {isLoading ? (
          <div className="space-y-3">
            <div className="h-4 w-32 animate-pulse rounded bg-muted/60" />
            <div className="h-10 w-20 animate-pulse rounded bg-muted/60" />
            <div className="h-4 w-48 animate-pulse rounded bg-muted/60" />
          </div>
        ) : (
          <>
            <div className="flex items-start justify-between gap-4">
              <div>
                <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Device health</p>
                <p className="mt-3 text-4xl font-semibold tracking-[-0.04em]">
                  {healthy}<span className="text-lg text-muted-foreground">/{total}</span>
                </p>
                <p className="mt-1 text-xs text-muted-foreground">healthy devices</p>
              </div>
              <span className="flex size-12 items-center justify-center rounded-2xl border border-chart-3/20 bg-chart-3/10 text-chart-3">
                <HeartPulse className="size-5" />
              </span>
            </div>

            {unhealthy > 0 && (
              <div className="mt-4 space-y-1.5">
                {entries
                  .filter(([status]) => status !== 'Active')
                  .map(([status, count]) => (
                    <Link
                      key={status}
                      to="/assets"
                      search={{ assetType: 'Device', healthStatus: status, search: '', criticality: '', ownerType: '', deviceGroup: '', unassignedOnly: false, page: 1, pageSize: 25, riskScore: '', exposureLevel: '', tag: '' }}
                      className="flex items-center justify-between rounded-xl border border-border/70 bg-background/35 px-3 py-2 text-sm transition hover:bg-background/60"
                    >
                      <span>{status}</span>
                      <Badge variant="outline" className="rounded-full border-border/70 text-xs">{count}</Badge>
                    </Link>
                  ))}
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}
```

**Step 2: Commit**

```bash
git add frontend/src/components/features/dashboard/DeviceHealthCard.tsx
git commit -m "feat: add DeviceHealthCard component with asset navigation"
```

---

### Task 8: Frontend — Add Loading Skeletons to Existing Components

**Files:**
- Modify: `frontend/src/components/features/dashboard/TrendChart.tsx`
- Modify: `frontend/src/components/features/dashboard/ExposureSlaCard.tsx`
- Modify: `frontend/src/components/features/dashboard/RemediationVelocity.tsx`
- Modify: `frontend/src/components/features/dashboard/RiskChangeBriefCard.tsx`
- Modify: `frontend/src/components/features/dashboard/CriticalVulnerabilities.tsx`

**Step 1: Add `isLoading` prop to each component**

For each component, add `isLoading?: boolean` to the props type and render a skeleton when true.

**TrendChart** — add `isLoading?: boolean` to `TrendChartProps`. Wrap the chart JSX:

```typescript
{isLoading ? (
  <div className={embedded ? 'mt-5 h-[320px] w-full animate-pulse rounded-2xl bg-muted/60' : 'h-[320px] w-full animate-pulse rounded-2xl bg-muted/60'} />
) : chart}
```

**ExposureSlaCard** — add `isLoading?: boolean` to props. When true, render placeholder divs inside each panel.

**RemediationVelocity** — add `isLoading?: boolean` to props. When true, replace chart with `<div className="h-[250px] w-full animate-pulse rounded-2xl bg-muted/60" />`.

**RiskChangeBriefCard** — add `isLoading?: boolean` to props. When true, render `<div className="h-32 animate-pulse rounded-2xl bg-muted/60" />`.

**CriticalVulnerabilities** — add `isLoading?: boolean` to props. When true, render `<div className="h-64 animate-pulse rounded-2xl bg-muted/60" />`.

**Step 2: Run checks**

Run: `cd frontend && npm run typecheck && npm run lint`
Expected: Pass.

**Step 3: Commit**

```bash
git add frontend/src/components/features/dashboard/
git commit -m "feat: add loading skeletons to all dashboard components"
```

---

### Task 9: Frontend — Wire Everything in Dashboard Route

**Files:**
- Modify: `frontend/src/routes/_authed/index.tsx`

**Step 1: Add search schema, filter state, and query wiring**

Import the new components and functions. Add search params for filters. Wire filter options query. Pass `isFetching` to all components. Add the filter bar and new components to the layout.

The route should:
1. Add `minAgeDays`, `platform`, `deviceGroup` to the route's search schema (using `searchStringSchema` for all three — convert minAgeDays to number when passing to API)
2. Load filter options via `fetchDashboardFilterOptions`
3. Pass filter values to `fetchDashboardSummary` and `fetchDashboardTrends`
4. Include query keys that incorporate filter values
5. Add `DashboardFilterBar` above the stat cards
6. Add `DeviceGroupVulnerabilityChart` below the trend chart row
7. Add `DeviceHealthCard` alongside the ExposureSlaCard
8. Pass `isLoading={summaryQuery.isFetching}` to summary-derived components
9. Pass `isLoading={trendsQuery.isFetching}` to TrendChart

**Step 2: Run all frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm test`
Expected: Pass.

**Step 3: Commit**

```bash
git add frontend/src/routes/_authed/index.tsx
git commit -m "feat: wire dashboard filters, device group chart, and health card into route"
```

---

### Task 10: Final Verification

**Step 1: Run full backend test suite**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All pass.

**Step 2: Run frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm test`
Expected: All pass.
