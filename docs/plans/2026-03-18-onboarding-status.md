# Onboarding Status Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Persist `onboardingStatus` from the Defender API, skip `InsufficientInfo` devices during ingestion, surface the field in the asset UI, and add a dashboard onboarding status metric card.

**Architecture:** Add the field through the full ingestion pipeline (DefenderMachineEntry → IngestionAsset → Asset entity), filter out `InsufficientInfo` at the normalization step, extend API DTOs and frontend schemas, and add a dashboard card following the DeviceHealthCard pattern.

**Tech Stack:** C# / EF Core (PostgreSQL), xUnit + InMemory, TanStack Start (React + Vite), Zod, Tailwind CSS.

---

### Task 1: Add OnboardingStatus to Data Model and Ingestion Pipeline

**Files:**
- Modify: `src/PatchHound.Infrastructure/VulnerabilitySources/DefenderApiClient.cs:240-284`
- Modify: `src/PatchHound.Core/Models/IngestionResult.cs:42-61`
- Modify: `src/PatchHound.Core/Entities/Asset.cs:92-119`
- Modify: `src/PatchHound.Infrastructure/VulnerabilitySources/DefenderVulnerabilitySource.cs:642-674`
- Modify: `src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs:120-132,146-158`

**Step 1: Add OnboardingStatus to DefenderMachineEntry**

In `DefenderApiClient.cs`, add after the `MachineTags` property (line 283):

```csharp
[JsonPropertyName("onboardingStatus")]
public string? OnboardingStatus { get; set; }
```

**Step 2: Add DeviceOnboardingStatus to IngestionAsset**

In `IngestionResult.cs`, add after `MachineTags` (line 60):

```csharp
string? DeviceOnboardingStatus = null
```

**Step 3: Add DeviceOnboardingStatus to Asset entity**

In `Asset.cs`, add a property alongside the other device fields (after `DeviceIsAadJoined`):

```csharp
public string? DeviceOnboardingStatus { get; private set; }
```

Add `onboardingStatus` parameter to `UpdateDeviceDetails` (after `isAadJoined`):

```csharp
public void UpdateDeviceDetails(
    string? computerDnsName,
    string? healthStatus,
    string? osPlatform,
    string? osVersion,
    string? riskScore,
    DateTimeOffset? lastSeenAt,
    string? lastIpAddress,
    string? aadDeviceId,
    string? groupId = null,
    string? groupName = null,
    string? exposureLevel = null,
    bool? isAadJoined = null,
    string? onboardingStatus = null
)
{
    DeviceComputerDnsName = computerDnsName;
    DeviceHealthStatus = healthStatus;
    DeviceOsPlatform = osPlatform;
    DeviceOsVersion = osVersion;
    DeviceRiskScore = riskScore;
    DeviceLastSeenAt = lastSeenAt;
    DeviceLastIpAddress = lastIpAddress;
    DeviceAadDeviceId = aadDeviceId;
    DeviceGroupId = groupId;
    DeviceGroupName = groupName;
    DeviceExposureLevel = exposureLevel;
    DeviceIsAadJoined = isAadJoined;
    DeviceOnboardingStatus = onboardingStatus;
}
```

**Step 4: Skip InsufficientInfo and pass field through NormalizeMachineAsset**

In `DefenderVulnerabilitySource.cs`, change `NormalizeMachineAsset` to return `IngestionAsset?` and skip `InsufficientInfo`:

```csharp
internal static IngestionAsset? NormalizeMachineAsset(DefenderMachineEntry entry)
{
    if (string.Equals(entry.OnboardingStatus, "InsufficientInfo", StringComparison.OrdinalIgnoreCase))
        return null;

    var name = string.IsNullOrWhiteSpace(entry.ComputerDnsName)
        ? entry.Id
        : entry.ComputerDnsName;
    var description = string.Join(
        " ",
        new[] { entry.OsPlatform, entry.OsVersion }.Where(value =>
            !string.IsNullOrWhiteSpace(value)
        )
    );

    return new IngestionAsset(
        entry.Id,
        name,
        AssetType.Device,
        string.IsNullOrWhiteSpace(description) ? null : description,
        entry.ComputerDnsName,
        entry.HealthStatus,
        entry.OsPlatform,
        entry.OsVersion,
        entry.RiskScore,
        entry.LastSeen,
        entry.LastIpAddress,
        entry.AadDeviceId,
        entry.RbacGroupId,
        entry.RbacGroupName,
        "{}",
        entry.ExposureLevel,
        entry.IsAadJoined,
        entry.MachineTags,
        entry.OnboardingStatus
    );
}
```

**IMPORTANT:** Update both call sites of `NormalizeMachineAsset` (lines 178 and 390) to filter out nulls. Change:

```csharp
var assets = response.Value.Select(NormalizeMachineAsset).ToList();
```

to:

```csharp
var assets = response.Value.Select(NormalizeMachineAsset).Where(a => a is not null).ToList()!;
```

**Step 5: Pass onboardingStatus through StagedAssetMergeService**

In `StagedAssetMergeService.cs`, update both `UpdateDeviceDetails` call sites (lines 120-132 and 146-158) to add the new parameter:

```csharp
existing.UpdateDeviceDetails(
    asset.DeviceComputerDnsName,
    asset.DeviceHealthStatus,
    asset.DeviceOsPlatform,
    asset.DeviceOsVersion,
    asset.DeviceRiskScore,
    asset.DeviceLastSeenAt,
    asset.DeviceLastIpAddress,
    asset.DeviceAadDeviceId,
    asset.DeviceGroupId,
    asset.DeviceGroupName,
    asset.DeviceExposureLevel,
    asset.DeviceIsAadJoined,
    asset.DeviceOnboardingStatus
);
```

**Step 6: Add EF migration**

Configure max length in `AssetConfiguration.cs` (if it has one for DeviceExposureLevel, follow same pattern).

Run:

```bash
dotnet ef migrations add AddDeviceOnboardingStatus -p src/PatchHound.Infrastructure -s src/PatchHound.Api
```

**Step 7: Build and run tests**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass.

**Step 8: Commit**

```bash
git add src/ tests/
git commit -m "feat: add DeviceOnboardingStatus to ingestion pipeline, skip InsufficientInfo devices"
```

---

### Task 2: Add Backend API Changes (DTOs, Filters, Dashboard Breakdown)

**Files:**
- Modify: `src/PatchHound.Api/Models/Assets/AssetDto.cs`
- Modify: `src/PatchHound.Api/Controllers/AssetsController.cs`
- Modify: `src/PatchHound.Api/Services/AssetDetailQueryService.cs`
- Modify: `src/PatchHound.Api/Models/Dashboard/DashboardSummaryDto.cs`
- Modify: `src/PatchHound.Api/Controllers/DashboardController.cs`

**Step 1: Extend AssetDto**

Add `OnboardingStatus` field after `Tags` (line 19):

```csharp
public record AssetDto(
    Guid Id,
    string ExternalId,
    string Name,
    string AssetType,
    string? DeviceGroupName,
    string Criticality,
    string OwnerType,
    Guid? OwnerUserId,
    Guid? OwnerTeamId,
    string? SecurityProfileName,
    int VulnerabilityCount,
    int RecurringVulnerabilityCount,
    string? HealthStatus,
    string? RiskScore,
    string? ExposureLevel,
    string[] Tags,
    string? OnboardingStatus
);
```

**Step 2: Extend AssetDetailDto**

Add `DeviceOnboardingStatus` after `DeviceIsAadJoined` (line 46):

```csharp
string? DeviceOnboardingStatus,
```

**Step 3: Extend AssetFilterQuery**

Add after `Tag` (line 149):

```csharp
string? OnboardingStatus = null
```

**Step 4: Add filter clause in AssetsController**

After the `Tag` filter clause (around line 117), add:

```csharp
if (!string.IsNullOrEmpty(filter.OnboardingStatus))
    query = query.Where(a => a.DeviceOnboardingStatus == filter.OnboardingStatus);
```

Update the AssetDto construction in the Select to include `a.DeviceOnboardingStatus` as the last field.

**Step 5: Update AssetDetailQueryService**

In the `new AssetDetailDto(...)` construction (line 339), add `asset.DeviceOnboardingStatus` after `asset.DeviceIsAadJoined` (line 366).

**Step 6: Extend DashboardSummaryDto**

Add a new field at the end:

```csharp
Dictionary<string, int> DeviceOnboardingBreakdown
```

**Step 7: Add onboarding breakdown query to DashboardController.GetSummary**

After the device health breakdown block (line 288), add:

```csharp
// Device onboarding status breakdown — NOT affected by vulnerability filters
var deviceOnboardingRows = await _dbContext
    .Assets.AsNoTracking()
    .Where(a => a.TenantId == tenantId && a.AssetType == AssetType.Device && a.DeviceOnboardingStatus != null)
    .GroupBy(a => a.DeviceOnboardingStatus!)
    .Select(g => new { Status = g.Key, Count = g.Count() })
    .ToListAsync(ct);

var deviceOnboardingBreakdown = deviceOnboardingRows.ToDictionary(r => r.Status, r => r.Count);
```

Add `deviceOnboardingBreakdown` to the DashboardSummaryDto construction (after `deviceHealthBreakdown`).

**Step 8: Build and run tests**

Run: `dotnet build PatchHound.slnx && dotnet test PatchHound.slnx -v minimal`
Expected: All pass (existing tests may need the new positional DTO argument).

**Step 9: Commit**

```bash
git add src/
git commit -m "feat: add onboarding status to API DTOs, filters, and dashboard breakdown"
```

---

### Task 3: Backend Tests

**Files:**
- Modify: `tests/PatchHound.Tests/Api/DashboardControllerTests.cs`

**Step 1: Add test for onboarding status breakdown**

```csharp
[Fact]
public async Task GetSummary_ReturnsDeviceOnboardingBreakdown()
{
    var onboarded = Asset.Create(_tenantId, "dev-onboarded", AssetType.Device, "Onboarded Device", Criticality.Medium);
    onboarded.UpdateDeviceDetails("onb.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.1", null, onboardingStatus: "Onboarded");

    var canBeOnboarded = Asset.Create(_tenantId, "dev-can-onboard", AssetType.Device, "Can Be Onboarded", Criticality.Medium);
    canBeOnboarded.UpdateDeviceDetails("can.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.2", null, onboardingStatus: "CanBeOnboarded");

    await _dbContext.AddRangeAsync(onboarded, canBeOnboarded);
    await _dbContext.SaveChangesAsync();

    var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);
    var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
    var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

    payload.DeviceOnboardingBreakdown.Should().ContainKey("Onboarded").WhoseValue.Should().Be(1);
    payload.DeviceOnboardingBreakdown.Should().ContainKey("CanBeOnboarded").WhoseValue.Should().Be(1);
}
```

**Step 2: Add test for InsufficientInfo skip**

Add a unit test for `DefenderVulnerabilitySource.NormalizeMachineAsset`:

```csharp
[Fact]
public void NormalizeMachineAsset_SkipsInsufficientInfo()
{
    var entry = new DefenderMachineEntry
    {
        Id = "machine-1",
        ComputerDnsName = "test.local",
        OnboardingStatus = "InsufficientInfo"
    };

    var result = DefenderVulnerabilitySource.NormalizeMachineAsset(entry);
    result.Should().BeNull();
}

[Fact]
public void NormalizeMachineAsset_IncludesOnboardedDevice()
{
    var entry = new DefenderMachineEntry
    {
        Id = "machine-2",
        ComputerDnsName = "test2.local",
        OnboardingStatus = "Onboarded"
    };

    var result = DefenderVulnerabilitySource.NormalizeMachineAsset(entry);
    result.Should().NotBeNull();
    result!.DeviceOnboardingStatus.Should().Be("Onboarded");
}
```

Note: `NormalizeMachineAsset` is `internal static` — the test project may need `InternalsVisibleTo` or the method may already be accessible. Check and add if needed.

**Step 3: Run tests**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All pass.

**Step 4: Commit**

```bash
git add tests/
git commit -m "test: add onboarding status breakdown and InsufficientInfo skip tests"
```

---

### Task 4: Frontend — Schemas and Asset List/Detail Updates

**Files:**
- Modify: `frontend/src/api/assets.schemas.ts`
- Modify: `frontend/src/api/assets.functions.ts`
- Modify: `frontend/src/api/dashboard.schemas.ts`
- Modify: `frontend/src/features/assets/list-state.ts`
- Modify: `frontend/src/routes/_authed/assets/index.tsx`
- Modify: `frontend/src/components/features/assets/AssetManagementTable.tsx`
- Modify: `frontend/src/components/features/assets/AssetDetailSections.tsx`

**Step 1: Update asset schemas**

In `assets.schemas.ts`:
- `assetSchema`: add `onboardingStatus: z.string().nullable()` after `tags`
- `assetDetailSchema`: add `deviceOnboardingStatus: z.string().nullable()` after `deviceIsAadJoined`

**Step 2: Update dashboard schema**

In `dashboard.schemas.ts`, add after `deviceHealthBreakdown`:

```typescript
deviceOnboardingBreakdown: z.record(z.string(), z.number()),
```

**Step 3: Update list-state**

In `list-state.ts`:
- Add `onboardingStatus: string` to `AssetsListSearch`
- Add `...(search.onboardingStatus ? { onboardingStatus: search.onboardingStatus } : {})` to `buildAssetsListRequest`
- Add `search.onboardingStatus` to `assetQueryKeys.list`

**Step 4: Update assets.functions.ts**

Add `onboardingStatus` to the `fetchAssets` input validator.

**Step 5: Update asset route search schema**

In `routes/_authed/assets/index.tsx`, add `onboardingStatus: ''` to the search schema defaults. Add filter prop/handler for onboarding status.

**Step 6: Update AssetManagementTable**

Add an "Onboarding" column and filter control, following the same pattern as Health Status.

**Step 7: Update AssetDetailSections**

In the `DeviceSection`, add an "Onboarding Status" field after the existing device info fields.

**Step 8: Run checks**

Run: `cd frontend && npm run typecheck && npm run lint`
Expected: Pass (pre-existing errors only).

**Step 9: Commit**

```bash
git add frontend/
git commit -m "feat: add onboarding status to asset schemas, list, detail, and filters"
```

---

### Task 5: Frontend — Onboarding Status Dashboard Card

**Files:**
- Create: `frontend/src/components/features/dashboard/OnboardingStatusCard.tsx`
- Modify: `frontend/src/routes/_authed/index.tsx`

**Step 1: Create OnboardingStatusCard**

Follow the exact same pattern as `DeviceHealthCard.tsx`. Key differences:
- Props: `onboardingBreakdown: Record<string, number>`
- "Onboarded" is the "healthy" equivalent (sorted first)
- Non-Onboarded statuses are clickable, linking to `/assets?onboardingStatus=<value>`
- Icon: Use `MonitorCheck` from lucide-react (or similar device-onboarding icon)
- Label: "Onboarding status"
- Display: `{onboarded}/{total}` with "onboarded devices" subtitle

```typescript
import { Link } from '@tanstack/react-router'
import { MonitorCheck } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'

type OnboardingStatusCardProps = {
  onboardingBreakdown: Record<string, number>
  isLoading?: boolean
}

export function OnboardingStatusCard({ onboardingBreakdown, isLoading }: OnboardingStatusCardProps) {
  const entries = Object.entries(onboardingBreakdown).sort(([a], [b]) => {
    if (a === 'Onboarded') return -1
    if (b === 'Onboarded') return 1
    return a.localeCompare(b)
  })
  const total = entries.reduce((sum, [, count]) => sum + count, 0)
  const onboarded = onboardingBreakdown['Onboarded'] ?? 0

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
                <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Onboarding status</p>
                <p className="mt-3 text-4xl font-semibold tracking-[-0.04em]">
                  {onboarded}<span className="text-lg text-muted-foreground">/{total}</span>
                </p>
                <p className="mt-1 text-xs text-muted-foreground">onboarded devices</p>
              </div>
              <span className="flex size-12 items-center justify-center rounded-2xl border border-chart-1/20 bg-chart-1/10 text-chart-1">
                <MonitorCheck className="size-5" />
              </span>
            </div>

            {total - onboarded > 0 && (
              <div className="mt-4 space-y-1.5">
                {entries
                  .filter(([status]) => status !== 'Onboarded')
                  .map(([status, count]) => (
                    <Link
                      key={status}
                      to="/assets"
                      search={{ /* all required search params with onboardingStatus: status */ }}
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

IMPORTANT: Read the assets route search schema to fill in ALL required search params for the Link.

**Step 2: Wire into dashboard route**

In `routes/_authed/index.tsx`:
- Import `OnboardingStatusCard`
- Add it alongside `DeviceHealthCard` in the metrics row
- Pass `onboardingBreakdown={summary.deviceOnboardingBreakdown}` and `isLoading={summaryQuery.isFetching}`

**Step 3: Run checks**

Run: `cd frontend && npm run typecheck && npm run lint`
Expected: Pass.

**Step 4: Commit**

```bash
git add frontend/
git commit -m "feat: add OnboardingStatusCard to dashboard"
```

---

### Task 6: Final Verification

**Step 1: Run full backend test suite**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All pass.

**Step 2: Run frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm test`
Expected: All pass.
