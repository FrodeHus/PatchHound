# Materialized Views for Heavy Queries — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace three classes of expensive query patterns in PatchHound with PostgreSQL materialized views, eliminating per-row correlated subqueries and repeated full-table scans in risk scoring and the dashboard.

**Architecture:** Each view is created via a raw-SQL EF Core migration (`migrationBuilder.Sql`), mapped to a keyless EF entity (`HasNoKey().ToView(...)`), registered as a `DbSet<T>` in `PatchHoundDbContext`, and refreshed at the appropriate write boundary. A new `MaterializedViewRefreshService` wraps all refresh calls so callers don't embed raw SQL. Views 1 and 2 are refreshed before risk score recalculation; view 3 is refreshed at the end of ingestion.

**Tech Stack:** .NET 9, EF Core 9, PostgreSQL 16, `migrationBuilder.Sql`, `REFRESH MATERIALIZED VIEW CONCURRENTLY`.

---

## File Map

**Create:**
- `src/PatchHound.Infrastructure/Data/Views/ExposureLatestAssessment.cs` — keyless POCO for view 1
- `src/PatchHound.Infrastructure/Data/Views/AlternateMitigationVulnId.cs` — keyless POCO for view 2
- `src/PatchHound.Infrastructure/Data/Views/OpenExposureVulnSummary.cs` — keyless POCO for view 3
- `src/PatchHound.Infrastructure/Data/Configurations/ExposureLatestAssessmentConfiguration.cs` — EF mapping
- `src/PatchHound.Infrastructure/Data/Configurations/AlternateMitigationVulnIdConfiguration.cs` — EF mapping
- `src/PatchHound.Infrastructure/Data/Configurations/OpenExposureVulnSummaryConfiguration.cs` — EF mapping
- `src/PatchHound.Infrastructure/Services/MaterializedViewRefreshService.cs` — wraps REFRESH calls

**Migrations (run `dotnet ef migrations add <Name> --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api` for each):**
- `AddExposureLatestAssessmentView`
- `AddAlternateMitigationVulnIdView`
- `AddOpenExposureVulnSummaryView`

**Modify:**
- `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` — add 3 `DbSet<T>` properties
- `src/PatchHound.Infrastructure/Services/RiskRefreshService.cs` — inject `MaterializedViewRefreshService`, call refresh before risk scoring
- `src/PatchHound.Infrastructure/Services/RiskScoreService.cs` — replace correlated subqueries + NOT EXISTS with JOINs
- `src/PatchHound.Infrastructure/Services/IngestionService.cs` — inject `MaterializedViewRefreshService`, call refresh after exposure derivation
- `src/PatchHound.Infrastructure/DependencyInjection.cs` — register `MaterializedViewRefreshService`
- `src/PatchHound.Api/Controllers/DashboardController.cs` — replace 3 heavy queries with view reads

---

## Task 1: View 1 POCO and configuration

**Files:**
- Create: `src/PatchHound.Infrastructure/Data/Views/ExposureLatestAssessment.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/ExposureLatestAssessmentConfiguration.cs`

- [ ] **Step 1: Create the POCO**

```csharp
// src/PatchHound.Infrastructure/Data/Views/ExposureLatestAssessment.cs
namespace PatchHound.Infrastructure.Data.Views;

public class ExposureLatestAssessment
{
    public Guid TenantId { get; set; }
    public Guid DeviceVulnerabilityExposureId { get; set; }
    public decimal EnvironmentalCvss { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
// src/PatchHound.Infrastructure/Data/Configurations/ExposureLatestAssessmentConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Infrastructure.Data.Views;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ExposureLatestAssessmentConfiguration
    : IEntityTypeConfiguration<ExposureLatestAssessment>
{
    public void Configure(EntityTypeBuilder<ExposureLatestAssessment> builder)
    {
        builder.HasNoKey().ToView("mv_exposure_latest_assessment");
        builder.Property(x => x.EnvironmentalCvss).HasColumnType("numeric(4,2)");
    }
}
```

- [ ] **Step 3: Register in DbContext**

In `PatchHoundDbContext.cs`, add after the existing `ExposureAssessments` line (~line 82):

```csharp
public DbSet<ExposureLatestAssessment> ExposureLatestAssessments => Set<ExposureLatestAssessment>();
```

Add the using at the top of the file:
```csharp
using PatchHound.Infrastructure.Data.Views;
```

- [ ] **Step 4: Verify it compiles**

```bash
dotnet build src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj
```

Expected: Build succeeded.

- [ ] **Step 5: Generate migration**

```bash
dotnet ef migrations add AddExposureLatestAssessmentView \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

Expected: Migration file created. The generated migration body will be empty (EF doesn't know how to create the view — that's correct, we will fill it in next).

- [ ] **Step 6: Fill in the migration SQL**

Open the generated migration file and replace the empty `Up`/`Down` bodies:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(
        """
        CREATE MATERIALIZED VIEW mv_exposure_latest_assessment AS
        SELECT DISTINCT ON ("DeviceVulnerabilityExposureId")
            "TenantId",
            "DeviceVulnerabilityExposureId",
            "EnvironmentalCvss"
        FROM "ExposureAssessments"
        ORDER BY "DeviceVulnerabilityExposureId", "CalculatedAt" DESC;

        CREATE UNIQUE INDEX ix_mv_ela_exposure_id
            ON mv_exposure_latest_assessment ("DeviceVulnerabilityExposureId");

        CREATE INDEX ix_mv_ela_tenant_id
            ON mv_exposure_latest_assessment ("TenantId");
        """
    );
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS mv_exposure_latest_assessment;");
}
```

- [ ] **Step 7: Apply migration**

```bash
dotnet ef database update \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

Expected: Migration applied successfully.

- [ ] **Step 8: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/Views/ExposureLatestAssessment.cs \
        src/PatchHound.Infrastructure/Data/Configurations/ExposureLatestAssessmentConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        src/PatchHound.Infrastructure/Migrations/
git commit -m "feat: add mv_exposure_latest_assessment materialized view"
```

---

## Task 2: View 2 POCO and configuration

**Files:**
- Create: `src/PatchHound.Infrastructure/Data/Views/AlternateMitigationVulnId.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/AlternateMitigationVulnIdConfiguration.cs`

- [ ] **Step 1: Create the POCO**

```csharp
// src/PatchHound.Infrastructure/Data/Views/AlternateMitigationVulnId.cs
namespace PatchHound.Infrastructure.Data.Views;

public class AlternateMitigationVulnId
{
    public Guid TenantId { get; set; }
    public Guid VulnerabilityId { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
// src/PatchHound.Infrastructure/Data/Configurations/AlternateMitigationVulnIdConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Infrastructure.Data.Views;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AlternateMitigationVulnIdConfiguration
    : IEntityTypeConfiguration<AlternateMitigationVulnId>
{
    public void Configure(EntityTypeBuilder<AlternateMitigationVulnId> builder)
    {
        builder.HasNoKey().ToView("mv_alternate_mitigation_vuln_ids");
    }
}
```

- [ ] **Step 3: Register in DbContext**

In `PatchHoundDbContext.cs`, add after the `ExposureLatestAssessments` line:

```csharp
public DbSet<AlternateMitigationVulnId> AlternateMitigationVulnIds => Set<AlternateMitigationVulnId>();
```

- [ ] **Step 4: Verify it compiles**

```bash
dotnet build src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj
```

Expected: Build succeeded.

- [ ] **Step 5: Generate migration**

```bash
dotnet ef migrations add AddAlternateMitigationVulnIdView \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

- [ ] **Step 6: Fill in the migration SQL**

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(
        """
        CREATE MATERIALIZED VIEW mv_alternate_mitigation_vuln_ids AS
        SELECT DISTINCT "TenantId", "VulnerabilityId"
        FROM "ApprovedVulnerabilityRemediations"
        WHERE "Outcome" = 'AlternateMitigation';

        CREATE UNIQUE INDEX ix_mv_amvi_tenant_vuln
            ON mv_alternate_mitigation_vuln_ids ("TenantId", "VulnerabilityId");
        """
    );
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS mv_alternate_mitigation_vuln_ids;");
}
```

- [ ] **Step 7: Apply migration**

```bash
dotnet ef database update \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

- [ ] **Step 8: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/Views/AlternateMitigationVulnId.cs \
        src/PatchHound.Infrastructure/Data/Configurations/AlternateMitigationVulnIdConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        src/PatchHound.Infrastructure/Migrations/
git commit -m "feat: add mv_alternate_mitigation_vuln_ids materialized view"
```

---

## Task 3: View 3 POCO and configuration

**Files:**
- Create: `src/PatchHound.Infrastructure/Data/Views/OpenExposureVulnSummary.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/OpenExposureVulnSummaryConfiguration.cs`

- [ ] **Step 1: Create the POCO**

The `VendorSeverity` column is stored as a string (convention: `HasConversion<string>()` throughout the project). Map it to the `Severity` enum.

```csharp
// src/PatchHound.Infrastructure/Data/Views/OpenExposureVulnSummary.cs
using PatchHound.Core.Enums;

namespace PatchHound.Infrastructure.Data.Views;

public class OpenExposureVulnSummary
{
    public Guid TenantId { get; set; }
    public Guid VulnerabilityId { get; set; }
    public Severity VendorSeverity { get; set; }
    public int AffectedDeviceCount { get; set; }
    public DateTimeOffset LatestSeenAt { get; set; }
    public decimal? MaxCvss { get; set; }
    public DateTimeOffset? PublishedDate { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
// src/PatchHound.Infrastructure/Data/Configurations/OpenExposureVulnSummaryConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Infrastructure.Data.Views;

namespace PatchHound.Infrastructure.Data.Configurations;

public class OpenExposureVulnSummaryConfiguration
    : IEntityTypeConfiguration<OpenExposureVulnSummary>
{
    public void Configure(EntityTypeBuilder<OpenExposureVulnSummary> builder)
    {
        builder.HasNoKey().ToView("mv_open_exposure_vuln_summary");
        builder.Property(x => x.VendorSeverity).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.MaxCvss).HasColumnType("numeric(5,2)");
    }
}
```

- [ ] **Step 3: Register in DbContext**

```csharp
public DbSet<OpenExposureVulnSummary> OpenExposureVulnSummaries => Set<OpenExposureVulnSummary>();
```

- [ ] **Step 4: Verify it compiles**

```bash
dotnet build src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj
```

- [ ] **Step 5: Generate migration**

```bash
dotnet ef migrations add AddOpenExposureVulnSummaryView \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

- [ ] **Step 6: Fill in the migration SQL**

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(
        """
        CREATE MATERIALIZED VIEW mv_open_exposure_vuln_summary AS
        SELECT
            dve."TenantId",
            dve."VulnerabilityId",
            v."VendorSeverity",
            COUNT(DISTINCT dve."DeviceId")::integer    AS "AffectedDeviceCount",
            MAX(dve."LastObservedAt")                  AS "LatestSeenAt",
            MAX(v."CvssScore")                         AS "MaxCvss",
            MIN(v."PublishedDate")                     AS "PublishedDate"
        FROM "DeviceVulnerabilityExposures" dve
        JOIN "Vulnerabilities" v ON v."Id" = dve."VulnerabilityId"
        WHERE dve."Status" = 'Open'
        GROUP BY dve."TenantId", dve."VulnerabilityId", v."VendorSeverity";

        CREATE UNIQUE INDEX ix_mv_oevs_tenant_vuln
            ON mv_open_exposure_vuln_summary ("TenantId", "VulnerabilityId");

        CREATE INDEX ix_mv_oevs_tenant_severity_count
            ON mv_open_exposure_vuln_summary ("TenantId", "VendorSeverity", "AffectedDeviceCount" DESC);
        """
    );
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS mv_open_exposure_vuln_summary;");
}
```

- [ ] **Step 7: Apply migration**

```bash
dotnet ef database update \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

- [ ] **Step 8: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/Views/OpenExposureVulnSummary.cs \
        src/PatchHound.Infrastructure/Data/Configurations/OpenExposureVulnSummaryConfiguration.cs \
        src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
        src/PatchHound.Infrastructure/Migrations/
git commit -m "feat: add mv_open_exposure_vuln_summary materialized view"
```

---

## Task 4: MaterializedViewRefreshService

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/MaterializedViewRefreshService.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create the service**

```csharp
// src/PatchHound.Infrastructure/Services/MaterializedViewRefreshService.cs
using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class MaterializedViewRefreshService(PatchHoundDbContext dbContext)
{
    public Task RefreshExposureLatestAssessmentAsync(CancellationToken ct) =>
        dbContext.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_exposure_latest_assessment", ct);

    public Task RefreshAlternateMitigationVulnIdsAsync(CancellationToken ct) =>
        dbContext.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_alternate_mitigation_vuln_ids", ct);

    public Task RefreshOpenExposureVulnSummaryAsync(CancellationToken ct) =>
        dbContext.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_open_exposure_vuln_summary", ct);
}
```

- [ ] **Step 2: Register in DI**

In `DependencyInjection.cs`, find where `RiskRefreshService` is registered and add next to it:

```csharp
services.AddScoped<MaterializedViewRefreshService>();
```

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj
```

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/MaterializedViewRefreshService.cs \
        src/PatchHound.Infrastructure/DependencyInjection.cs
git commit -m "feat: add MaterializedViewRefreshService"
```

---

## Task 5: Wire refresh into RiskRefreshService

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/RiskRefreshService.cs`

`RiskRefreshService` is the single chokepoint where `exposureAssessmentService.AssessForTenantAsync` is called before risk scoring. Refresh both views 1 and 2 here so `RiskScoreService` always reads fresh data.

- [ ] **Step 1: Inject MaterializedViewRefreshService**

Replace the primary constructor parameter list:

```csharp
public class RiskRefreshService(
    PatchHoundDbContext dbContext,
    ExposureAssessmentService exposureAssessmentService,
    RiskScoreService riskScoreService,
    MaterializedViewRefreshService materializedViewRefreshService
)
```

- [ ] **Step 2: Add refresh calls in all paths**

Every code path in `RiskRefreshService` that calls `riskScoreService.RecalculateForTenantAsync` must first refresh both views. There are 4 such paths (`RefreshForAssetsAsync`, `RefreshForPairAsync`, `RefreshForVulnerabilityAsync`, `RefreshForTenantAsync`). Add the same two lines before each `RecalculateForTenantAsync` call:

```csharp
await materializedViewRefreshService.RefreshExposureLatestAssessmentAsync(ct);
await materializedViewRefreshService.RefreshAlternateMitigationVulnIdsAsync(ct);
await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
```

Full updated file (showing all 4 methods after edit):

```csharp
public async Task RefreshForAssetsAsync(
    Guid tenantId,
    IReadOnlyCollection<Guid> assetIds,
    bool recalculateAssessments,
    CancellationToken ct
)
{
    var distinctAssetIds = assetIds.Distinct().ToList();
    if (distinctAssetIds.Count == 0)
        return;

    if (recalculateAssessments)
        await exposureAssessmentService.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, ct);

    await materializedViewRefreshService.RefreshExposureLatestAssessmentAsync(ct);
    await materializedViewRefreshService.RefreshAlternateMitigationVulnIdsAsync(ct);
    await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
    await dbContext.SaveChangesAsync(ct);
}

public async Task RefreshForPairAsync(
    Guid tenantId,
    Guid tenantVulnerabilityId,
    Guid assetId,
    bool recalculateAssessments,
    CancellationToken ct
)
{
    if (recalculateAssessments)
        await exposureAssessmentService.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, ct);

    await materializedViewRefreshService.RefreshExposureLatestAssessmentAsync(ct);
    await materializedViewRefreshService.RefreshAlternateMitigationVulnIdsAsync(ct);
    await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
    await dbContext.SaveChangesAsync(ct);
}

public async Task RefreshForVulnerabilityAsync(
    Guid tenantId,
    Guid tenantVulnerabilityId,
    bool recalculateAssessments,
    CancellationToken ct
)
{
    if (recalculateAssessments)
        await exposureAssessmentService.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, ct);

    await materializedViewRefreshService.RefreshExposureLatestAssessmentAsync(ct);
    await materializedViewRefreshService.RefreshAlternateMitigationVulnIdsAsync(ct);
    await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
    await dbContext.SaveChangesAsync(ct);
}

public async Task RefreshForTenantAsync(
    Guid tenantId,
    bool recalculateAssessments,
    CancellationToken ct
)
{
    if (recalculateAssessments)
        await exposureAssessmentService.AssessForTenantAsync(tenantId, DateTimeOffset.UtcNow, ct);

    await materializedViewRefreshService.RefreshExposureLatestAssessmentAsync(ct);
    await materializedViewRefreshService.RefreshAlternateMitigationVulnIdsAsync(ct);
    await riskScoreService.RecalculateForTenantAsync(tenantId, ct);
    await dbContext.SaveChangesAsync(ct);
}
```

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj
```

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/RiskRefreshService.cs
git commit -m "feat: refresh exposure and mitigation views before risk scoring"
```

---

## Task 6: Rewrite RiskScoreService — correlated subquery → LEFT JOIN

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/RiskScoreService.cs`

Both `CalculateAssetScoresAsync` (lines ~458-477) and `CalculateSoftwareScoresAsync` (lines ~557-579) have identical patterns to fix: a correlated subquery for `AssessmentScore` and a NOT EXISTS filter for alternate mitigation. Replace both with explicit LEFT JOINs to the two views.

- [ ] **Step 1: Rewrite `CalculateAssetScoresAsync` exposure query**

Replace the block from `var exposureRows = await dbContext.DeviceVulnerabilityExposures...` through `.ToListAsync(ct)` (lines 458-477) with:

```csharp
var exposureRows = await (
    from item in dbContext.DeviceVulnerabilityExposures.AsNoTracking()
    join assessment in dbContext.ExposureLatestAssessments
        on item.Id equals assessment.DeviceVulnerabilityExposureId into assessmentJoin
    from assessment in assessmentJoin.DefaultIfEmpty()
    join mitigated in dbContext.AlternateMitigationVulnIds
            .Where(m => m.TenantId == tenantId)
        on item.VulnerabilityId equals mitigated.VulnerabilityId into mitigatedJoin
    from mitigated in mitigatedJoin.DefaultIfEmpty()
    where item.TenantId == tenantId
        && item.Status == ExposureStatus.Open
        && mitigated == null
    select new
    {
        item.DeviceId,
        item.VulnerabilityId,
        item.SoftwareProductId,
        DeviceCriticality = item.Device.Criticality,
        VendorSeverity = item.Vulnerability.VendorSeverity,
        VulnerabilityCvss = item.Vulnerability.CvssScore,
        AssessmentScore = (decimal?)assessment.EnvironmentalCvss,
    }
).ToListAsync(ct);
```

- [ ] **Step 2: Rewrite `CalculateSoftwareScoresAsync` exposure query**

Replace the block from `var exposures = await dbContext.DeviceVulnerabilityExposures...` through `.ToListAsync(ct)` (lines 557-579) with:

```csharp
var exposures = await (
    from item in dbContext.DeviceVulnerabilityExposures.AsNoTracking()
    join assessment in dbContext.ExposureLatestAssessments
        on item.Id equals assessment.DeviceVulnerabilityExposureId into assessmentJoin
    from assessment in assessmentJoin.DefaultIfEmpty()
    join mitigated in dbContext.AlternateMitigationVulnIds
            .Where(m => m.TenantId == tenantId)
        on item.VulnerabilityId equals mitigated.VulnerabilityId into mitigatedJoin
    from mitigated in mitigatedJoin.DefaultIfEmpty()
    where item.TenantId == tenantId
        && item.SoftwareProductId != null
        && item.Status == ExposureStatus.Open
        && mitigated == null
    select new
    {
        item.Id,
        item.DeviceId,
        SoftwareProductId = item.SoftwareProductId!.Value,
        item.VulnerabilityId,
        DeviceCriticality = item.Device.Criticality,
        VendorSeverity = item.Vulnerability.VendorSeverity,
        VulnerabilityCvss = item.Vulnerability.CvssScore,
        AssessmentScore = (decimal?)assessment.EnvironmentalCvss,
    }
).ToListAsync(ct);
```

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj
```

- [ ] **Step 4: Run tests**

```bash
dotnet test PatchHound.slnx -v minimal --filter "FullyQualifiedName~RiskScore"
```

Expected: All existing risk score tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/RiskScoreService.cs
git commit -m "perf: replace correlated subqueries in risk scoring with materialized view JOINs"
```

---

## Task 7: Wire refresh into IngestionService for view 3

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`

View 3 (`mv_open_exposure_vuln_summary`) must be refreshed after `RunExposureDerivationAsync` completes, which is the step that writes `DeviceVulnerabilityExposures`. The view is then stale only during ingestion itself and becomes consistent before the next dashboard or risk score read.

- [ ] **Step 1: Inject MaterializedViewRefreshService**

In `IngestionService`'s primary constructor, add `MaterializedViewRefreshService materializedViewRefreshService` as a parameter and store it:

```csharp
private readonly MaterializedViewRefreshService _materializedViewRefreshService;

// In the constructor body, add:
_materializedViewRefreshService = materializedViewRefreshService;
```

- [ ] **Step 2: Call refresh after each `RunExposureDerivationAsync` call**

There are two calls to `RunExposureDerivationAsync` in `IngestionService` (lines ~649 and ~690). Add the refresh call immediately after each:

```csharp
await RunExposureDerivationAsync(tenantId, ct);
await _materializedViewRefreshService.RefreshOpenExposureVulnSummaryAsync(ct);
```

Apply this to both call sites.

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj
```

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionService.cs
git commit -m "feat: refresh open exposure vuln summary view post-ingestion"
```

---

## Task 8: Rewrite DashboardController — severity counts and top vulns

**Files:**
- Modify: `src/PatchHound.Api/Controllers/DashboardController.cs`

The dashboard currently fires five separate heavy queries against `DeviceVulnerabilityExposures`. Three of them (severity counts, top critical/high vulns, latest unhandled) can be served from `mv_open_exposure_vuln_summary`. The MTTR and age bucket queries still need to hit base tables since they require episode-level or published-date data that the view doesn't contain.

The `ExcludeAcceptedRiskVulnerabilities` helper at line 80 applies an additional filter. Check its implementation — if it also does a NOT EXISTS against `ApprovedVulnerabilityRemediations`, the view (which doesn't pre-apply this filter) will need the filter applied on top of the view query. Apply an anti-join to `AlternateMitigationVulnIds` when querying the summary view.

- [ ] **Step 1: Read `ExcludeAcceptedRiskVulnerabilities` implementation**

```bash
grep -n "ExcludeAcceptedRiskVulnerabilities" src/PatchHound.Api/Controllers/DashboardController.cs | head -5
```

Then read the method body. Confirm what filters it adds.

- [ ] **Step 2: Replace severity counts query (lines 82-91)**

Replace:
```csharp
var exposureSeverityCounts = await presentationExposureQuery
    .GroupBy(e => new { e.Status, e.Vulnerability.VendorSeverity })
    .Select(g => new { g.Key.Status, g.Key.VendorSeverity, Count = g.Select(e => e.VulnerabilityId).Distinct().Count() })
    .ToListAsync(ct);
```

With (add the mitigation anti-join if `ExcludeAcceptedRiskVulnerabilities` filters alternate mitigations):
```csharp
var summaryQuery = _dbContext.OpenExposureVulnSummaries.AsNoTracking()
    .Where(s => s.TenantId == tenantId);

// If ExcludeAcceptedRiskVulnerabilities also removes AlternateMitigation vulns,
// apply the same anti-join here:
summaryQuery = summaryQuery
    .Where(s => !_dbContext.AlternateMitigationVulnIds
        .Any(m => m.TenantId == tenantId && m.VulnerabilityId == s.VulnerabilityId));

// Apply any additional caller-supplied filters (minPublishedDate, filteredAssetIds)
// Note: filteredAssetIds cannot be pushed into the pre-aggregated view.
// If filteredAssetIds is set, fall back to the original base-table query for this block.
// (See note below.)

var vulnSummaryRows = await summaryQuery.ToListAsync(ct);

var vulnsBySeverity = Enum.GetValues<Severity>().ToDictionary(
    s => s.ToString(),
    s => vulnSummaryRows.Where(r => r.VendorSeverity == s).Sum(r => r.AffectedDeviceCount > 0 ? 1 : 0));
var openCount = vulnSummaryRows.Count;
```

> **Note on `filteredAssetIds`:** The summary view pre-aggregates all devices. If the dashboard filter narrows to specific assets, the view's `AffectedDeviceCount` is wrong. In that case, fall back to the original base-table query for the severity counts block only. Add a branch:
>
> ```csharp
> if (filteredAssetIds != null)
> {
>     // original base-table query path (unchanged)
>     var exposureSeverityCounts = await presentationExposureQuery
>         .GroupBy(e => new { e.Status, e.Vulnerability.VendorSeverity })
>         .Select(g => new { g.Key.Status, g.Key.VendorSeverity, Count = g.Select(e => e.VulnerabilityId).Distinct().Count() })
>         .ToListAsync(ct);
>     // ... rest of original logic
> }
> else
> {
>     // view-backed fast path
>     // ... new code
> }
> ```

- [ ] **Step 3: Replace top critical/high vulns query (lines 99-123)**

Replace the `topVulnRows` query with a read from the summary view:

```csharp
var topVulnRows = await summaryQuery
    .Where(s => s.VendorSeverity == Severity.Critical || s.VendorSeverity == Severity.High)
    .OrderByDescending(s => s.VendorSeverity)
    .ThenByDescending(s => s.AffectedDeviceCount)
    .Take(10)
    .Select(s => new
    {
        VulnerabilityId = s.VulnerabilityId,
        VendorSeverity = s.VendorSeverity,
        AffectedDeviceCount = s.AffectedDeviceCount,
        CvssScore = s.MaxCvss,
        PublishedDate = s.PublishedDate,
        // ExternalId and Title must still come from the Vulnerabilities table
    })
    .Join(_dbContext.Vulnerabilities.AsNoTracking(),
        s => s.VulnerabilityId,
        v => v.Id,
        (s, v) => new
        {
            s.VulnerabilityId,
            v.ExternalId,
            v.Title,
            s.VendorSeverity,
            s.CvssScore,
            s.PublishedDate,
            s.AffectedDeviceCount,
        })
    .ToListAsync(ct);
```

- [ ] **Step 4: Replace latest unhandled query (lines 126-145)**

Replace the LEFT JOIN query with a view-backed version:

```csharp
var latestUnhandledRows = await (
    from s in summaryQuery
    join rc in _dbContext.RemediationCases.AsNoTracking()
            .Where(rc => rc.TenantId == tenantId)
        on s.VulnerabilityId equals rc.SoftwareProductId into rcJoin
    from rc in rcJoin.DefaultIfEmpty()
    where rc == null
    join v in _dbContext.Vulnerabilities.AsNoTracking()
        on s.VulnerabilityId equals v.Id
    orderby s.LatestSeenAt descending
    select new
    {
        s.VulnerabilityId,
        v.ExternalId,
        v.Title,
        s.VendorSeverity,
        s.MaxCvss,
        s.PublishedDate,
        s.AffectedDeviceCount,
        s.LatestSeenAt,
    }
).Take(10).ToListAsync(ct);
```

- [ ] **Step 5: Verify it compiles**

```bash
dotnet build src/PatchHound.Api/PatchHound.Api.csproj
```

- [ ] **Step 6: Run full test suite**

```bash
dotnet test PatchHound.slnx -v minimal
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Api/Controllers/DashboardController.cs
git commit -m "perf: serve dashboard severity counts and top vulns from materialized view"
```

---

## Self-Review Checklist

- [x] **View 1** (`mv_exposure_latest_assessment`): POCO ✓, configuration ✓, DbContext ✓, migration SQL ✓, rewrite in `CalculateAssetScoresAsync` and `CalculateSoftwareScoresAsync` ✓, refresh in `RiskRefreshService` ✓
- [x] **View 2** (`mv_alternate_mitigation_vuln_ids`): POCO ✓, configuration ✓, DbContext ✓, migration SQL ✓, NOT EXISTS replaced with LEFT JOIN anti-join ✓, refresh in `RiskRefreshService` ✓
- [x] **View 3** (`mv_open_exposure_vuln_summary`): POCO ✓, configuration ✓, DbContext ✓, migration SQL ✓, dashboard queries replaced ✓, refresh after `RunExposureDerivationAsync` ✓
- [x] **Refresh service**: Created ✓, registered in DI ✓, injected into `RiskRefreshService` and `IngestionService` ✓
- [x] **filteredAssetIds fallback**: Dashboard fast path falls back to original query when per-asset filters are active ✓
- [x] **Enum storage**: `VendorSeverity` mapped with `HasConversion<string>()` matching the project convention ✓
- [x] **CONCURRENT refresh**: All `REFRESH MATERIALIZED VIEW CONCURRENTLY` calls require the unique index created in each migration — indexes are present in all three migration SQL blocks ✓
- [x] **Down migrations**: All three `Down` methods drop the materialized view ✓
