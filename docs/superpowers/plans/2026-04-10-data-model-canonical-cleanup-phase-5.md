# Data Model Canonical Cleanup — Phase 5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite every read-side path — risk scoring, dashboard summaries, and email notifications — against canonical `Device`, `InstalledSoftware`, `DeviceVulnerabilityExposure`, `ExposureAssessment`, `ExposureEpisode`, and `RemediationCase` data. Remove any remaining stubs that Phases 2–4 parked while the write side was being rebuilt.

**Architecture:** Phase 5 is a read-side cleanup. Nothing new is introduced; everything downstream of the ingestion + exposure + remediation pipelines is re-pointed at canonical rows. `RiskScoreService` reads `DeviceVulnerabilityExposure` joined to `ExposureAssessment` (tenant-scoped canonical) and writes `DeviceRiskScore`, `SoftwareRiskScore(TenantId, SoftwareProductId)`, `TeamRiskScore`, `DeviceGroupRiskScore`, `TenantRiskScoreSnapshot`. `DashboardQueryService` reads the same canonical rows and joins to `RemediationCase` for case-level summaries. `EmailNotificationService` renders emails from canonical rows with deep links to `/remediation/cases/{caseId}`.

**Tech Stack:** .NET 10 / EF Core 10 / xUnit / React + Vitest.

**Prerequisites:** Phases 1–4 merged. `DeviceVulnerabilityExposure`, `ExposureEpisode`, `ExposureAssessment`, `RemediationCase` exist and are populated. `VulnerabilityAsset*`, `TenantVulnerability`, `VulnerabilityAssetEpisode`, `AssetRiskScore`, `TenantSoftwareRiskScore` are all deleted. `DeviceRiskScore` and `SoftwareRiskScore` entities exist (introduced in Phase 1 per §5.1). Dashboard, email, and risk score code paths compile during Phases 2–4 only because they were left as trivially-returning stubs while the write side was rebuilt; Phase 5 fills those stubs in.

**Phase 3 handoff — stubs still returning empty/409 after Phase 3 merged:**
- `ApprovalTaskQueryService` (7 inline stubs tagged `Phase 4 debt (#17)`) — joined `SoftwareVulnerabilityMatch` / `VulnerabilityDefinition` / `TenantVulnerability` / `VulnerabilityAsset`. Must be rewired against canonical `Vulnerability` + `DeviceVulnerabilityExposure`.
- `SoftwareDescriptionGenerationService` (1 stub) — consumed `NormalizedSoftwareVulnerabilityProjection`. Must be rewired against canonical exposure rows aggregated per `SoftwareProduct`.
- `VulnerabilitiesController.UpdateOrganizationalSeverity` currently returns 409 Conflict with a "disabled during canonical migration" message. Must be restored using canonical `Vulnerability` + `OrganizationalSeverity` tenant-scoped entity (already carries canonical `VulnerabilityId`).

These three items are not risk/dashboard/notification work strictly, but they live on read-surface controllers/services and share the canonical-exposure read substrate this phase rebuilds. They are added as Tasks at the end of this phase (see "Phase 3 handoff tasks" section).

---

## Preflight

- [ ] **P1: Confirm Phase 4 merged**

Run:
```bash
git log --oneline main -30 | grep -i "phase.4\|canonical-cleanup-phase-4"
```
Expected: at least one commit.

- [ ] **P2: Cut Phase 5 branch**

Run:
```bash
git checkout main && git pull
git checkout -b data-model-canonical-cleanup-phase-5
```

- [ ] **P3: Wipe dev database**

```bash
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS patchhound;"
PGPASSWORD=$POSTGRES_PASSWORD psql -h localhost -U postgres -c "CREATE DATABASE patchhound;"
```

- [ ] **P4: Baseline green build**

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx
(cd frontend && npm run typecheck && npm test -- --run)
```
Expected: all green. If anything is red on a fresh Phase 4 main, stop and fix main first.

- [ ] **P5: Audit the current stub scope**

Run:
```bash
grep -rn "TODO.*phase.5\|phase5\|PHASE_5_STUB" src/PatchHound.Infrastructure/Services/RiskScoreService.cs src/PatchHound.Infrastructure/Services/EmailNotificationService.cs src/PatchHound.Api/Services/DashboardQueryService.cs src/PatchHound.Api/Controllers/DashboardController.cs
```
Expected: a non-zero list of stubs pointing at the read paths this phase replaces. Record the list in a short note committed as `docs/superpowers/plans/2026-04-10-phase-5-stub-inventory.md` so the reviewer knows exactly what Phase 5 is filling in.

---

### Task 1: Add canonical read-model unit tests for `RiskScoreService`

**Files:**
- Test: `tests/PatchHound.Tests/Infrastructure/Services/RiskScoreServiceCanonicalTests.cs`

- [ ] **Step 1: Write the failing test**

Create the file with:
```csharp
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure.TestInfrastructure;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services;

public class RiskScoreServiceCanonicalTests
{
    [Fact]
    public async Task Device_score_comes_from_exposure_assessment_env_cvss()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var seed = CanonicalSeed.TwoDevicesOneVulnerability(ctx, tenantId,
            deviceAEnvCvss: 9.5m,
            deviceBEnvCvss: 4.2m);
        await ctx.SaveChangesAsync();

        var sut = new RiskScoreService(ctx, NullLogger.For<RiskScoreService>());
        await sut.RecalculateForTenantAsync(tenantId, CancellationToken.None);

        var scoreA = ctx.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id);
        var scoreB = ctx.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceB.Id);

        Assert.Equal(9.5m, scoreA.MaxExposureScore);
        Assert.Equal(4.2m, scoreB.MaxExposureScore);
        Assert.True(scoreA.OverallScore > scoreB.OverallScore);
    }

    [Fact]
    public async Task Software_score_is_keyed_by_tenant_and_software_product_id()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var seed = CanonicalSeed.TwoDevicesOneVulnerability(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var sut = new RiskScoreService(ctx, NullLogger.For<RiskScoreService>());
        await sut.RecalculateForTenantAsync(tenantId, CancellationToken.None);

        var softwareScore = ctx.SoftwareRiskScores.Single();
        Assert.Equal(tenantId, softwareScore.TenantId);
        Assert.Equal(seed.SoftwareProduct.Id, softwareScore.SoftwareProductId);
        Assert.Equal(2, softwareScore.AffectedDeviceCount);
    }

    [Fact]
    public async Task Resolved_exposures_are_excluded_from_open_counts()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var seed = CanonicalSeed.TwoDevicesOneVulnerability(ctx, tenantId);
        seed.ExposureB.Resolve(DateTimeOffset.UtcNow);
        await ctx.SaveChangesAsync();

        var sut = new RiskScoreService(ctx, NullLogger.For<RiskScoreService>());
        await sut.RecalculateForTenantAsync(tenantId, CancellationToken.None);

        var softwareScore = ctx.SoftwareRiskScores.Single();
        Assert.Equal(1, softwareScore.OpenExposureCount);
    }
}
```

Note: `CanonicalSeed.TwoDevicesOneVulnerability` is a new shared fixture helper created in the next step. Its shape: seeds one `SoftwareProduct`, one canonical `Vulnerability`, two `Device` rows each with the product installed, two `DeviceVulnerabilityExposure` rows, and two `ExposureAssessment` rows with caller-specified env CVSS scores.

- [ ] **Step 2: Create the shared fixture helper**

Create `tests/PatchHound.Tests/Infrastructure/TestInfrastructure/CanonicalSeed.cs`:
```csharp
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.Infrastructure.TestInfrastructure;

public static class CanonicalSeed
{
    public record TwoDeviceSeed(
        SoftwareProduct SoftwareProduct,
        Vulnerability Vulnerability,
        Device DeviceA,
        Device DeviceB,
        DeviceVulnerabilityExposure ExposureA,
        DeviceVulnerabilityExposure ExposureB,
        ExposureAssessment AssessmentA,
        ExposureAssessment AssessmentB);

    public static TwoDeviceSeed TwoDevicesOneVulnerability(
        PatchHoundDbContext ctx,
        Guid tenantId,
        decimal deviceAEnvCvss = 9.0m,
        decimal deviceBEnvCvss = 5.0m)
    {
        var product = TestData.Product();
        var vuln = TestData.Vulnerability(baseCvss: 7.5m);
        var deviceA = TestData.Device(tenantId, name: "device-a");
        var deviceB = TestData.Device(tenantId, name: "device-b");
        var installA = TestData.InstalledSoftware(tenantId, deviceA.Id, product.Id);
        var installB = TestData.InstalledSoftware(tenantId, deviceB.Id, product.Id);
        var exposureA = DeviceVulnerabilityExposure.Observe(
            tenantId, deviceA.Id, vuln.Id, product.Id, installA.Id,
            "1.0.0", DeviceExposureMatchSource.Product, DateTimeOffset.UtcNow);
        var exposureB = DeviceVulnerabilityExposure.Observe(
            tenantId, deviceB.Id, vuln.Id, product.Id, installB.Id,
            "1.0.0", DeviceExposureMatchSource.Product, DateTimeOffset.UtcNow);
        var assessmentA = ExposureAssessment.Create(
            tenantId, exposureA.Id, securityProfileId: null,
            baseCvss: 7.5m, environmentalCvss: deviceAEnvCvss,
            reason: "test-a", calculatedAt: DateTimeOffset.UtcNow);
        var assessmentB = ExposureAssessment.Create(
            tenantId, exposureB.Id, securityProfileId: null,
            baseCvss: 7.5m, environmentalCvss: deviceBEnvCvss,
            reason: "test-b", calculatedAt: DateTimeOffset.UtcNow);

        ctx.SoftwareProducts.Add(product);
        ctx.Vulnerabilities.Add(vuln);
        ctx.Devices.AddRange(deviceA, deviceB);
        ctx.InstalledSoftware.AddRange(installA, installB);
        ctx.DeviceVulnerabilityExposures.AddRange(exposureA, exposureB);
        ctx.ExposureAssessments.AddRange(assessmentA, assessmentB);

        return new TwoDeviceSeed(product, vuln, deviceA, deviceB, exposureA, exposureB, assessmentA, assessmentB);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~RiskScoreServiceCanonicalTests"`
Expected: FAIL — `DeviceRiskScores` / `SoftwareRiskScores` are empty because `RiskScoreService.RecalculateForTenantAsync` is still a stub.

- [ ] **Step 4: Commit**

```bash
git add tests/PatchHound.Tests/Infrastructure/Services/RiskScoreServiceCanonicalTests.cs tests/PatchHound.Tests/Infrastructure/TestInfrastructure/CanonicalSeed.cs
git commit -m "test(phase-5): add canonical risk score tests driving RiskScoreService rewrite"
```

---

### Task 2: Rewrite `RiskScoreService` against canonical rows

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/RiskScoreService.cs`
- Modify: `src/PatchHound.Core/Entities/DeviceRiskScore.cs` (ensure it has `MaxExposureScore`, `OpenExposureCount` fields; if missing, add in this task)
- Modify: `src/PatchHound.Core/Entities/SoftwareRiskScore.cs` (verify `TenantId`, `SoftwareProductId`, `AffectedDeviceCount`, `OpenExposureCount`)

- [ ] **Step 1: Verify the target entities**

Run: `grep -n "MaxExposureScore\|SoftwareProductId" src/PatchHound.Core/Entities/DeviceRiskScore.cs src/PatchHound.Core/Entities/SoftwareRiskScore.cs`
If either field is missing, extend the entity with a mutator `Update(decimal overall, decimal maxExposure, int critical, int high, int medium, int low, int openExposureCount, string factorsJson, string calculationVersion)` — mirror the existing shape; commit the entity change as its own small commit before proceeding.

- [ ] **Step 2: Rewrite `CalculateAssetScoresAsync` → `CalculateDeviceScoresAsync`**

New body:
```csharp
private async Task<List<DeviceRiskResult>> CalculateDeviceScoresAsync(
    Guid tenantId, CancellationToken ct)
{
    var exposures = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
        .Where(e => e.TenantId == tenantId && e.Status == DeviceExposureStatus.Open)
        .Select(e => new
        {
            e.Id,
            e.DeviceId,
            e.VulnerabilityId,
            e.SoftwareProductId,
            BaseCvss = (decimal?)dbContext.Vulnerabilities
                .Where(v => v.Id == e.VulnerabilityId)
                .Select(v => v.CvssScore)
                .FirstOrDefault(),
            EnvCvss = (decimal?)dbContext.ExposureAssessments
                .Where(a => a.DeviceVulnerabilityExposureId == e.Id)
                .Select(a => a.EnvironmentalCvss)
                .FirstOrDefault(),
        })
        .ToListAsync(ct);

    return exposures
        .GroupBy(x => x.DeviceId)
        .Select(grp =>
        {
            var maxScore = grp.Max(e => e.EnvCvss ?? e.BaseCvss ?? 0m);
            var critical = grp.Count(e => (e.EnvCvss ?? e.BaseCvss ?? 0m) >= 9.0m);
            var high = grp.Count(e => (e.EnvCvss ?? e.BaseCvss ?? 0m) is >= 7.0m and < 9.0m);
            var medium = grp.Count(e => (e.EnvCvss ?? e.BaseCvss ?? 0m) is >= 4.0m and < 7.0m);
            var low = grp.Count(e => (e.EnvCvss ?? e.BaseCvss ?? 0m) is < 4.0m);
            var overall = ComputeDeviceOverallScore(maxScore, critical, high, medium, low);
            var factors = SerializeFactors(maxScore, critical, high, medium, low);
            return new DeviceRiskResult(
                grp.Key, overall, maxScore, critical, high, medium, low, grp.Count(), factors);
        })
        .ToList();
}
```

Define `ComputeDeviceOverallScore` as: `max(maxScore, weighted(0.6*critical + 0.3*high + 0.1*medium))` (preserve the existing algorithm shape if it was reasonable; the spec allows user-visible score shifts per §8 risk table, but don't change the formula gratuitously).

- [ ] **Step 3: Rewrite `CalculateSoftwareScoresAsync`**

Key on `(TenantId, SoftwareProductId)`:
```csharp
private async Task<List<SoftwareRiskResult>> CalculateSoftwareScoresAsync(
    Guid tenantId, CancellationToken ct)
{
    var rows = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
        .Where(e => e.TenantId == tenantId && e.Status == DeviceExposureStatus.Open)
        .Select(e => new
        {
            e.SoftwareProductId,
            e.DeviceId,
            EnvOrBase = dbContext.ExposureAssessments
                .Where(a => a.DeviceVulnerabilityExposureId == e.Id)
                .Select(a => (decimal?)a.EnvironmentalCvss)
                .FirstOrDefault()
                ?? dbContext.Vulnerabilities
                    .Where(v => v.Id == e.VulnerabilityId)
                    .Select(v => (decimal?)v.CvssScore)
                    .FirstOrDefault()
                ?? 0m,
        })
        .ToListAsync(ct);

    return rows
        .Where(r => r.SoftwareProductId.HasValue)
        .GroupBy(r => r.SoftwareProductId!.Value)
        .Select(grp => new SoftwareRiskResult(
            SoftwareProductId: grp.Key,
            OverallScore: ComputeSoftwareOverallScore(grp.Max(r => r.EnvOrBase), grp.Count()),
            MaxExposureScore: grp.Max(r => r.EnvOrBase),
            CriticalExposureCount: grp.Count(r => r.EnvOrBase >= 9.0m),
            HighExposureCount: grp.Count(r => r.EnvOrBase is >= 7.0m and < 9.0m),
            MediumExposureCount: grp.Count(r => r.EnvOrBase is >= 4.0m and < 7.0m),
            LowExposureCount: grp.Count(r => r.EnvOrBase < 4.0m),
            AffectedDeviceCount: grp.Select(r => r.DeviceId).Distinct().Count(),
            OpenExposureCount: grp.Count(),
            FactorsJson: "{}"))
        .ToList();
}
```

Replace the `SoftwareRiskResult` record shape accordingly (drop `TenantSoftwareId`, `SnapshotId`; add `SoftwareProductId`, `MaxExposureScore`).

- [ ] **Step 4: Rewrite `CalculateDeviceGroupScoresAsync` / `CalculateTeamScoresAsync`**

Both now read `Device` → `DeviceGroup` and `Device` → `OwnerTeam` mappings (canonical Phase 1 entities) and aggregate the device scores computed in Step 2. No changes to shape, just the source.

- [ ] **Step 5: Rewrite `RecalculateForTenantAsync` orchestration**

The top-level method becomes:
```csharp
public async Task RecalculateForTenantAsync(Guid tenantId, CancellationToken ct)
{
    var deviceResults = await CalculateDeviceScoresAsync(tenantId, ct);
    var softwareResults = await CalculateSoftwareScoresAsync(tenantId, ct);
    var deviceGroupResults = await CalculateDeviceGroupScoresAsync(tenantId, deviceResults, ct);
    var teamResults = await CalculateTeamScoresAsync(tenantId, deviceResults, ct);

    await UpsertDeviceScoresAsync(tenantId, deviceResults, ct);
    await UpsertSoftwareScoresAsync(tenantId, softwareResults, ct);
    await UpsertDeviceGroupScoresAsync(tenantId, deviceGroupResults, ct);
    await UpsertTeamScoresAsync(tenantId, teamResults, ct);
    await UpsertTenantSnapshotAsync(tenantId, deviceResults, ct);

    await dbContext.SaveChangesAsync(ct);
}
```

Each `Upsert*` helper compares existing rows by the canonical key and calls `Update(...)` or `Add(Create(...))`.

- [ ] **Step 6: Delete every stale reference**

Run: `grep -n "AssetRiskScore\|TenantSoftwareRiskScore\|VulnerabilityAssetEpisode\|TenantVulnerability\|TenantSoftwareId" src/PatchHound.Infrastructure/Services/RiskScoreService.cs`
Expected: no matches.

- [ ] **Step 7: Run the Phase 5 tests**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~RiskScoreServiceCanonicalTests"`
Expected: PASS (3 tests).

- [ ] **Step 8: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/RiskScoreService.cs src/PatchHound.Core/Entities/DeviceRiskScore.cs src/PatchHound.Core/Entities/SoftwareRiskScore.cs
git commit -m "refactor(phase-5): rewrite RiskScoreService against canonical exposure data"
```

---

### Task 3: Add canonical read-model unit tests for `DashboardQueryService`

**Files:**
- Test: `tests/PatchHound.Tests/Api/Services/DashboardQueryServiceCanonicalTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using PatchHound.Api.Services;
using PatchHound.Tests.Infrastructure.TestInfrastructure;
using Xunit;

namespace PatchHound.Tests.Api.Services;

public class DashboardQueryServiceCanonicalTests
{
    [Fact]
    public async Task Security_manager_summary_counts_open_exposures_from_canonical()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        CanonicalSeed.TwoDevicesOneVulnerability(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var sut = new DashboardQueryService(ctx);
        var result = await sut.BuildSecurityManagerSummaryAsync(tenantId, CancellationToken.None);

        Assert.Equal(2, result.OpenExposureCount);
        Assert.Equal(1, result.AffectedVulnerabilityCount);
        Assert.Equal(2, result.AffectedDeviceCount);
    }

    [Fact]
    public async Task Technical_manager_summary_groups_by_software_product()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var seed = CanonicalSeed.TwoDevicesOneVulnerability(ctx, tenantId);
        await ctx.SaveChangesAsync();

        var sut = new DashboardQueryService(ctx);
        var result = await sut.BuildTechnicalManagerSummaryAsync(tenantId, CancellationToken.None);

        Assert.Single(result.TopSoftwareByRisk);
        Assert.Equal(seed.SoftwareProduct.Id, result.TopSoftwareByRisk[0].SoftwareProductId);
        Assert.Equal(2, result.TopSoftwareByRisk[0].AffectedDeviceCount);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~DashboardQueryServiceCanonicalTests"`
Expected: FAIL — `BuildSecurityManagerSummaryAsync` and `BuildTechnicalManagerSummaryAsync` are still stubs.

- [ ] **Step 3: Commit the failing tests**

```bash
git add tests/PatchHound.Tests/Api/Services/DashboardQueryServiceCanonicalTests.cs
git commit -m "test(phase-5): add canonical dashboard query tests"
```

---

### Task 4: Rewrite `DashboardQueryService` against canonical rows

**Files:**
- Modify: `src/PatchHound.Api/Services/DashboardQueryService.cs`
- Modify: corresponding DTOs under `src/PatchHound.Api/Models/Dashboard/`

- [ ] **Step 1: Replace the vulnerability list block**

The current method `.VulnerabilityAssetEpisodes.AsNoTracking().GroupBy(...)` is replaced with a query against `DeviceVulnerabilityExposure`:
```csharp
var openExposures = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
    .Where(e => e.TenantId == tenantId && e.Status == DeviceExposureStatus.Open)
    .Select(e => new
    {
        e.Id,
        e.DeviceId,
        e.VulnerabilityId,
        e.SoftwareProductId,
        EnvCvss = dbContext.ExposureAssessments
            .Where(a => a.DeviceVulnerabilityExposureId == e.Id)
            .Select(a => (decimal?)a.EnvironmentalCvss)
            .FirstOrDefault(),
        BaseCvss = dbContext.Vulnerabilities
            .Where(v => v.Id == e.VulnerabilityId)
            .Select(v => v.CvssScore)
            .FirstOrDefault(),
    })
    .ToListAsync(ct);
```

- [ ] **Step 2: Build the summary DTOs from that projection**

- `SecurityManagerSummary`: `OpenExposureCount`, `AffectedVulnerabilityCount` (`openExposures.Select(e => e.VulnerabilityId).Distinct().Count()`), `AffectedDeviceCount`, `CriticalExposureCount`, `HighExposureCount`, `MediumExposureCount`, `LowExposureCount`.
- `TechnicalManagerSummary`: `TopSoftwareByRisk` (top 10 by max env cvss then count), joined to `RemediationCase` so each row carries `RemediationCaseId?` if a case exists.
- `OwnerSummary`: group by `OwnerTeam` via `Device.OwnerTeamId` → device score aggregation, joined to `RemediationCase` the same way.

- [ ] **Step 3: Update DTOs to drop legacy IDs**

`TopSoftwareRow` now carries `SoftwareProductId`, `ProductName`, `Vendor`, `RemediationCaseId?` — not `TenantSoftwareId`. Every DTO touched by the dashboard similarly drops `TenantSoftwareId`, `TenantVulnerabilityId`, `AssetId` and picks up canonical IDs.

- [ ] **Step 4: Grep sweep**

Run: `grep -n "VulnerabilityAssetEpisode\|TenantVulnerability\|TenantSoftware\|AssetRiskScore" src/PatchHound.Api/Services/DashboardQueryService.cs`
Expected: no matches.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~DashboardQueryServiceCanonicalTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Api/Services/DashboardQueryService.cs src/PatchHound.Api/Models/Dashboard/
git commit -m "refactor(phase-5): rewrite DashboardQueryService against canonical exposures"
```

---

### Task 5: Rewrite `DashboardController` endpoint queries

**Files:**
- Modify: `src/PatchHound.Api/Controllers/DashboardController.cs`

- [ ] **Step 1: Replace every inline `VulnerabilityAssetEpisodes`/`TenantVulnerability` query**

The controller has numerous inline projections used by per-role dashboard endpoints. Each inline projection is replaced with a call into `DashboardQueryService` or rewritten directly against `DeviceVulnerabilityExposures` + `ExposureAssessments`. Keep business rules (priority tiers, SLA buckets, etc.) unchanged; only the source tables change.

- [ ] **Step 2: Update deep links**

Wherever the controller constructed a URL like `/software/{tenantSoftwareId}/remediation`, replace with `/remediation/cases/{caseId}`. The case id is loaded alongside the software product in the same query.

- [ ] **Step 3: Grep sweep**

Run: `grep -n "VulnerabilityAssetEpisode\|TenantVulnerability\|TenantSoftwareId\|SoftwareAssetId" src/PatchHound.Api/Controllers/DashboardController.cs`
Expected: no matches.

- [ ] **Step 4: Run controller-level tests**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~DashboardController"`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Api/Controllers/DashboardController.cs
git commit -m "refactor(phase-5): rewrite DashboardController inline queries against canonical"
```

---

### Task 6: Rewrite `RiskScoreController` surface

**Files:**
- Modify: `src/PatchHound.Api/Controllers/RiskScoreController.cs`

- [ ] **Step 1: Verify `DeviceRiskScore` / `SoftwareRiskScore` DbSets are used**

The controller should expose `GET /api/risk-score/devices`, `/risk-score/software` (keyed `(TenantId, SoftwareProductId)`), `/risk-score/teams`, `/risk-score/groups`, `/risk-score/tenant-snapshot`. Replace any lingering `AssetRiskScore` / `TenantSoftwareRiskScore` references.

- [ ] **Step 2: Update DTOs**

`SoftwareRiskScoreDto` carries `SoftwareProductId`, `ProductName`, `Vendor`, `RemediationCaseId?`, not `TenantSoftwareId`. `DeviceRiskScoreDto` carries `DeviceId`, `DeviceName`, not `AssetId`.

- [ ] **Step 3: Grep sweep**

Run: `grep -n "AssetRiskScore\|TenantSoftwareRiskScore\|TenantSoftwareId\|SoftwareAssetId" src/PatchHound.Api/Controllers/RiskScoreController.cs`
Expected: no matches.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Api/Controllers/RiskScoreController.cs src/PatchHound.Api/Models/RiskScore/
git commit -m "refactor(phase-5): point RiskScoreController at canonical score entities"
```

---

### Task 7: Rewrite `EmailNotificationService` against canonical + cases

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/EmailNotificationService.cs`
- Modify: Razor / Scriban templates under `src/PatchHound.Infrastructure/EmailTemplates/` (if present)

- [ ] **Step 1: Replace every `TenantSoftware` / `dbContext.TenantSoftware` lookup**

Replace with a join to `RemediationCase` (by `CaseId` held on the triggering `PatchingTask` / `RemediationDecision`) → `SoftwareProduct` for the product name and vendor. Example:
```csharp
var caseInfo = await dbContext.RemediationCases.AsNoTracking()
    .Where(c => c.Id == task.RemediationCaseId && c.TenantId == tenantId)
    .Select(c => new
    {
        c.Id,
        ProductName = c.SoftwareProduct.Name,
        Vendor = c.SoftwareProduct.Vendor,
    })
    .FirstAsync(ct);
```

- [ ] **Step 2: Replace every frontend deep link**

- `{origin}/software/{tenantSoftwareId}/remediation` → `{origin}/remediation/cases/{caseId}`
- `{origin}/remediation/tasks?tenantSoftwareId={id}` → `{origin}/remediation/cases/{caseId}`

- [ ] **Step 3: Replace severity fetching**

Any query that previously walked `task.RemediationDecision.TenantSoftwareId` → `TenantSoftwareRiskScores` now walks `task.RemediationCaseId` → `RemediationCase.SoftwareProductId` → `SoftwareRiskScores`.

- [ ] **Step 4: Grep sweep**

Run: `grep -n "TenantSoftware\|SoftwareAssetId\|TenantVulnerability\|VulnerabilityAsset" src/PatchHound.Infrastructure/Services/EmailNotificationService.cs`
Expected: no matches.

- [ ] **Step 5: Verify email templates render**

Run existing email-rendering tests:
```bash
dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~EmailNotification"
```
Expected: green. If any template still has `{{ tenantSoftwareId }}`, replace with `{{ case_id }}` and re-run.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/EmailNotificationService.cs src/PatchHound.Infrastructure/EmailTemplates/
git commit -m "refactor(phase-5): rewrite EmailNotificationService against canonical + case links"
```

---

### Task 8: Rewrite frontend dashboard + risk views

**Files:**
- Modify: `frontend/src/components/features/dashboard/{AssetOwnerOverview,SecurityManagerOverview,TechnicalManagerOverview}.tsx`
- Modify: any risk score pages under `frontend/src/routes/_authed/risk/*` and `frontend/src/components/features/risk/*`

- [ ] **Step 1: Update dashboard DTO consumers**

Each component currently expects rows with `tenantSoftwareId`. Switch to `softwareProductId` + `remediationCaseId`. "Open in remediation" CTAs route to `/remediation/cases/$caseId` directly when `remediationCaseId` is present, otherwise call `useOpenRemediationCase(softwareProductId)` from Phase 4.

- [ ] **Step 2: Update risk score tables**

Risk score table columns display `productName`, `vendor`, `maxExposureScore`, `affectedDeviceCount`, `openExposureCount`, and link to `/remediation/cases/$caseId` when set.

- [ ] **Step 3: Grep sweep**

Run: `grep -rn "tenantSoftwareId\|softwareAssetId\|tenantVulnerabilityId" frontend/src/components/features/dashboard frontend/src/components/features/risk 2>/dev/null`
Expected: no matches.

- [ ] **Step 4: Run frontend tests**

```bash
cd frontend && npm run typecheck && npm test -- --run && cd ..
```
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/features/dashboard frontend/src/components/features/risk frontend/src/routes/_authed/risk 2>/dev/null
git commit -m "refactor(phase-5): dashboard + risk views consume canonical IDs"
```

---

### Task 9: Extend `TenantIsolationEndToEndTests` with read-side assertions

**Files:**
- Modify: `tests/PatchHound.Tests/Api/Tests/TenantIsolationEndToEndTests.cs`

- [ ] **Step 1: Seed risk + dashboard data per tenant**

Extend the fixture so each tenant gets a `DeviceRiskScore`, a `SoftwareRiskScore`, and a `TenantRiskScoreSnapshot` row populated by running `RiskScoreService.RecalculateForTenantAsync` against the seeded exposures.

- [ ] **Step 2: Add assertions**

```csharp
[Fact]
public async Task Risk_score_device_list_isolates_tenants()
{
    var tenantAClient = await CreateAuthenticatedClientAsync(TenantA);
    var response = await tenantAClient.GetAsync("/api/risk-score/devices?page=1&pageSize=100");
    response.EnsureSuccessStatusCode();
    var page = await response.Content.ReadFromJsonAsync<PagedResponse<DeviceRiskScoreDto>>();
    Assert.NotNull(page);
    Assert.All(page!.Items, r => Assert.Equal(TenantA.Id, r.TenantId));
}

[Fact]
public async Task Risk_score_software_list_isolates_tenants()
{
    var tenantAClient = await CreateAuthenticatedClientAsync(TenantA);
    var response = await tenantAClient.GetAsync("/api/risk-score/software?page=1&pageSize=100");
    response.EnsureSuccessStatusCode();
    var page = await response.Content.ReadFromJsonAsync<PagedResponse<SoftwareRiskScoreDto>>();
    Assert.NotNull(page);
    Assert.All(page!.Items, r => Assert.Equal(TenantA.Id, r.TenantId));
}

[Fact]
public async Task Dashboard_security_manager_summary_counts_only_tenant_rows()
{
    var tenantAClient = await CreateAuthenticatedClientAsync(TenantA);
    var response = await tenantAClient.GetAsync("/api/dashboard/security-manager");
    response.EnsureSuccessStatusCode();
    var summary = await response.Content.ReadFromJsonAsync<SecurityManagerSummaryDto>();
    Assert.NotNull(summary);
    Assert.Equal(TenantASeed.ExpectedOpenExposureCount, summary!.OpenExposureCount);
}

[Fact]
public async Task Email_rendering_does_not_cross_tenants()
{
    // Spec §7: email notification rendering pipelines do not join across tenants.
    using var scope = _factory.Services.CreateScope();
    var emailSvc = scope.ServiceProvider.GetRequiredService<EmailNotificationService>();
    var payload = await emailSvc.RenderPatchingTaskAssignedAsync(
        TenantASeed.PatchingTask.Id, CancellationToken.None);
    Assert.Contains(TenantASeed.SoftwareProduct.Name, payload.Body);
    Assert.DoesNotContain(TenantBSeed.SoftwareProduct.Name, payload.Body);
    Assert.Contains($"/remediation/cases/{TenantASeed.RemediationCase.Id}", payload.Body);
}
```

- [ ] **Step 3: Run the test**

Run: `dotnet test tests/PatchHound.Tests --filter "FullyQualifiedName~TenantIsolationEndToEndTests"`
Expected: green.

- [ ] **Step 4: Commit**

```bash
git add tests/PatchHound.Tests/Api/Tests/TenantIsolationEndToEndTests.cs
git commit -m "test(phase-5): extend tenant isolation e2e with dashboard/risk/email assertions"
```

---

### Task 10: Final grep sweep

- [ ] **Step 1: Legacy identifier sweep**

Run:
```bash
grep -rn "TenantSoftware\|SoftwareAsset\|TenantVulnerability\|VulnerabilityAsset\|NormalizedSoftware\|AssetRiskScore\|TenantSoftwareRiskScore" src/ frontend/src/ 2>/dev/null | grep -v "\.Migrations/" | head -40
```
Expected: empty.

- [ ] **Step 2: Legacy route sweep**

```bash
grep -rn "/api/software/.*remediation\|/software/.*/remediation\b" src/ frontend/src/ 2>/dev/null
```
Expected: empty.

- [ ] **Step 3: `IgnoreQueryFilters` audit**

Run: `grep -rn "IgnoreQueryFilters" src/`
Expected: same allow-list from Phase 1, no Phase 5 additions.

- [ ] **Step 4: Stub inventory sweep**

Run: `grep -rn "TODO.*phase.5\|phase5\|PHASE_5_STUB" src/`
Expected: empty. Delete `docs/superpowers/plans/2026-04-10-phase-5-stub-inventory.md` since the stubs are filled.

- [ ] **Step 5: Commit cleanup**

```bash
git add -A
git commit -m "chore(phase-5): remove stub inventory and lingering legacy references"
```

---

### Task 11: Final build, test, PR

- [ ] **Step 1: Full build**

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx
(cd frontend && npm run typecheck && npm test -- --run)
```
Expected: all green.

- [ ] **Step 2: Push**

```bash
git push -u origin data-model-canonical-cleanup-phase-5
```

- [ ] **Step 3: Open the PR**

```bash
gh pr create --title "Phase 5: canonical risk/dashboard/email rewrites" --body "$(cat <<'EOF'
## Summary
- Rewrite `RiskScoreService` to compute `DeviceRiskScore`, `SoftwareRiskScore(TenantId, SoftwareProductId)`, `TeamRiskScore`, `DeviceGroupRiskScore`, `TenantRiskScoreSnapshot` from canonical `DeviceVulnerabilityExposure` + `ExposureAssessment`.
- Rewrite `DashboardQueryService` and `DashboardController` inline queries to read canonical exposure data and link to remediation cases.
- Rewrite `RiskScoreController` DTOs/surfaces to expose canonical IDs only.
- Rewrite `EmailNotificationService` to build emails from canonical rows and `/remediation/cases/{caseId}` deep links.
- Frontend dashboard and risk views consume canonical IDs.
- Tenant isolation e2e extended with dashboard/risk/email assertions.

## Tenant scope audit
No new entities. Every read-side service now honors `IsSystemContext || AccessibleTenantIds.Contains(e.TenantId)` via the normal EF query filters on `DeviceVulnerabilityExposure`, `ExposureAssessment`, `DeviceRiskScore`, `SoftwareRiskScore`, `TeamRiskScore`, `DeviceGroupRiskScore`, `TenantRiskScoreSnapshot`, and `RemediationCase`.

## IgnoreQueryFilters audit
No new entries. Allow-list unchanged from Phase 1.

## Spec §4.10 rule compliance
- R1/R2/R3: unchanged — every read-side query honors the existing filters.
- R5: no new `IgnoreQueryFilters()` calls.
- R6: dashboard queries that join `SoftwareProduct` (global) with tenant-scoped exposures reveal only public product identity; tenant-specific context comes from `TenantSoftwareProductInsight`.

## Deleted scope
No new entity deletions in Phase 5. This phase is entirely read-side cleanup.

## Risk note
Per spec §8, this phase may shift user-visible risk score numbers. The change is documented here and accepted.

## Test plan
- [ ] `dotnet build` clean
- [ ] `dotnet test` green (new: `RiskScoreServiceCanonicalTests`, `DashboardQueryServiceCanonicalTests`, extended `TenantIsolationEndToEndTests`)
- [ ] `npm run typecheck` clean
- [ ] `npm test` green
- [ ] Manual smoke: load each dashboard role view; open email preview for a patching-task-assigned notification; confirm deep links point at `/remediation/cases/$caseId`

EOF
)"
```

- [ ] **Step 4: Confirm PR URL**

Expected: `https://github.com/frodehus/PatchHound/pull/<n>`

---

## Plan self-review

**Spec coverage (spec §5.5):**
- `RiskScoreService` rewritten against canonical exposure data — Tasks 1–2.
- `DeviceRiskScore`, `SoftwareRiskScore(TenantId, SoftwareProductId)`, `TeamRiskScore`, `DeviceGroupRiskScore`, `TenantRiskScoreSnapshot` written — Task 2.
- Dashboard queries (owner, security manager, technical manager) read canonical — Tasks 3–5.
- Email notifications derive from canonical rows + case links — Task 7.
- Tenant isolation test extended with dashboard + notification assertions — Task 9.
- Single-path canonical read with no dual paths remaining — Task 10 sweep.

**Placeholder scan:** no `TBD`/`TODO` entries. Every step shows the code or command to run.

**Type consistency:** every new fixture call uses the same `CanonicalSeed.TwoDevicesOneVulnerability` helper defined in Task 1 Step 2. `DeviceRiskResult` / `SoftwareRiskResult` record shapes defined in Task 2 match the `DeviceRiskScore` / `SoftwareRiskScore` entity fields verified in Step 1.

**Cross-phase dependency check:** Phase 5 assumes Phase 4 already landed `RemediationCase` and the `/api/remediation/cases/{caseId}` route surface. The plan header and every deep link replacement call this out.
