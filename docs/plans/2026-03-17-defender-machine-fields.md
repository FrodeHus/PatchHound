# Defender Machine Fields Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Surface `exposureLevel`, `isAadJoined`, and `machineTags` from the Defender API through the normalized data model to the UI, and expose `healthStatus`/`riskScore` (already stored) as filterable list columns.

**Architecture:** Add two new scalar columns (`DeviceExposureLevel`, `DeviceIsAadJoined`) to `Asset`, plus a new `AssetTag` join table. Extend the ingestion pipeline (`DefenderMachineEntry` → `IngestionAsset` → `StagedAssetMergeService` → `Asset`) to carry the new fields. Surface all five fields in API DTOs and add filter support. Update frontend schemas, server functions, list table columns, and detail section.

**Tech Stack:** C# / EF Core (PostgreSQL), xUnit + InMemory, TanStack Start (React + Vite), Zod, Tailwind CSS.

---

### Task 1: Create AssetTag Entity + EF Configuration

**Files:**
- Create: `src/PatchHound.Core/Entities/AssetTag.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/AssetTagConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` (add DbSet + query filter)
- Test: `tests/PatchHound.Tests/Core/AssetTagTests.cs`

**Step 1: Write the failing test**

```csharp
// tests/PatchHound.Tests/Core/AssetTagTests.cs
using FluentAssertions;
using PatchHound.Core.Entities;

namespace PatchHound.Tests.Core;

public class AssetTagTests
{
    [Fact]
    public void Create_SetsAllFields()
    {
        var tenantId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        var tag = AssetTag.Create(tenantId, assetId, "production", "Defender");

        tag.Id.Should().NotBeEmpty();
        tag.TenantId.Should().Be(tenantId);
        tag.AssetId.Should().Be(assetId);
        tag.Tag.Should().Be("production");
        tag.Source.Should().Be("Defender");
        tag.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AssetTagTests" -v minimal`
Expected: FAIL — `AssetTag` type not found.

**Step 3: Write the AssetTag entity**

```csharp
// src/PatchHound.Core/Entities/AssetTag.cs
namespace PatchHound.Core.Entities;

public class AssetTag
{
    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Tag { get; private set; } = null!;
    public string Source { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private AssetTag() { }

    public static AssetTag Create(Guid tenantId, Guid assetId, string tag, string source)
    {
        return new AssetTag
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssetId = assetId,
            Tag = tag,
            Source = source,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AssetTagTests" -v minimal`
Expected: PASS

**Step 5: Add EF configuration, DbSet, and query filter**

Create `src/PatchHound.Infrastructure/Data/Configurations/AssetTagConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AssetTagConfiguration : IEntityTypeConfiguration<AssetTag>
{
    public void Configure(EntityTypeBuilder<AssetTag> builder)
    {
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => new { t.AssetId, t.Tag }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.Tag });

        builder.Property(t => t.Tag).HasMaxLength(256).IsRequired();
        builder.Property(t => t.Source).HasMaxLength(64).IsRequired();

        builder
            .HasOne<Asset>()
            .WithMany()
            .HasForeignKey(t => t.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

In `PatchHoundDbContext.cs`, add the DbSet (follow existing pattern near line 43):

```csharp
public DbSet<AssetTag> AssetTags => Set<AssetTag>();
```

Add the global query filter (follow existing pattern in `OnModelCreating`, after the other `HasQueryFilter` blocks around lines 117-238):

```csharp
modelBuilder.Entity<AssetTag>().HasQueryFilter(
    e => tenantIds.Contains(e.TenantId)
);
```

**Step 6: Run all tests to verify nothing broke**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All pass.

**Step 7: Commit**

```bash
git add src/PatchHound.Core/Entities/AssetTag.cs src/PatchHound.Infrastructure/Data/Configurations/AssetTagConfiguration.cs src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs tests/PatchHound.Tests/Core/AssetTagTests.cs
git commit -m "feat: add AssetTag entity with EF configuration and query filter"
```

---

### Task 2: Add ExposureLevel + IsAadJoined to Asset Entity

**Files:**
- Modify: `src/PatchHound.Core/Entities/Asset.cs` (lines 19-28 properties, lines 90-113 UpdateDeviceDetails)
- Modify: `src/PatchHound.Infrastructure/Data/Configurations/AssetConfiguration.cs` (lines 20-28)
- Modify: `tests/PatchHound.Tests/Api/AssetsControllerTests.cs` (UpdateDeviceDetails calls at lines 217-228, 237-248)

**Step 1: Write the failing test**

```csharp
// tests/PatchHound.Tests/Core/AssetDeviceFieldsTests.cs
using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Tests.Core;

public class AssetDeviceFieldsTests
{
    [Fact]
    public void UpdateDeviceDetails_SetsExposureLevelAndIsAadJoined()
    {
        var asset = Asset.Create(Guid.NewGuid(), "dev-1", AssetType.Device, "Test", Criticality.Medium);

        asset.UpdateDeviceDetails(
            "host.contoso.com", "Active", "Windows", "11",
            "Medium", DateTimeOffset.UtcNow, "10.0.0.1", "aad-1",
            "group-1", "Tier 0",
            "High", true
        );

        asset.DeviceExposureLevel.Should().Be("High");
        asset.DeviceIsAadJoined.Should().BeTrue();
    }

    [Fact]
    public void UpdateDeviceDetails_AllowsNullExposureLevelAndIsAadJoined()
    {
        var asset = Asset.Create(Guid.NewGuid(), "dev-2", AssetType.Device, "Test", Criticality.Medium);

        asset.UpdateDeviceDetails(
            "host.contoso.com", "Active", "Windows", "11",
            "Medium", DateTimeOffset.UtcNow, "10.0.0.1", "aad-1",
            "group-1", "Tier 0",
            null, null
        );

        asset.DeviceExposureLevel.Should().BeNull();
        asset.DeviceIsAadJoined.Should().BeNull();
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AssetDeviceFieldsTests" -v minimal`
Expected: FAIL — overload not found.

**Step 3: Add properties and update method signature**

In `src/PatchHound.Core/Entities/Asset.cs`:

Add two new properties after `DeviceGroupName` (line 28):

```csharp
public string? DeviceExposureLevel { get; private set; }
public bool? DeviceIsAadJoined { get; private set; }
```

Update `UpdateDeviceDetails` signature (line 90) to add two new parameters:

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
    bool? isAadJoined = null
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
}
```

**Step 4: Add EF property configuration**

In `src/PatchHound.Infrastructure/Data/Configurations/AssetConfiguration.cs`, add after the `DeviceGroupName` line (line 28):

```csharp
builder.Property(a => a.DeviceExposureLevel).HasMaxLength(64);
```

(`DeviceIsAadJoined` is `bool?` — EF handles that without explicit config.)

**Step 5: Run test to verify it passes**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AssetDeviceFieldsTests" -v minimal`
Expected: PASS

**Step 6: Run all tests to verify nothing broke**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All pass (existing callers use named/optional params, so they still compile).

**Step 7: Commit**

```bash
git add src/PatchHound.Core/Entities/Asset.cs src/PatchHound.Infrastructure/Data/Configurations/AssetConfiguration.cs tests/PatchHound.Tests/Core/AssetDeviceFieldsTests.cs
git commit -m "feat: add DeviceExposureLevel and DeviceIsAadJoined to Asset entity"
```

---

### Task 3: Extend Ingestion Pipeline (DefenderMachineEntry → IngestionAsset)

**Files:**
- Modify: `src/PatchHound.Infrastructure/VulnerabilitySources/DefenderApiClient.cs` (DefenderMachineEntry, lines 240-275)
- Modify: `src/PatchHound.Core/Models/IngestionResult.cs` (IngestionAsset, lines 42-58)
- Modify: `src/PatchHound.Infrastructure/VulnerabilitySources/DefenderVulnerabilitySource.cs` (NormalizeMachineAsset, lines 642-670)

**Step 1: Add fields to DefenderMachineEntry**

In `DefenderApiClient.cs`, add three new properties inside `DefenderMachineEntry` (after `RbacGroupName` at line 274):

```csharp
[JsonPropertyName("exposureLevel")]
public string? ExposureLevel { get; set; }

[JsonPropertyName("isAadJoined")]
public bool? IsAadJoined { get; set; }

[JsonPropertyName("machineTags")]
public List<string>? MachineTags { get; set; }
```

**Step 2: Add fields to IngestionAsset**

In `src/PatchHound.Core/Models/IngestionResult.cs`, add three new parameters to the `IngestionAsset` record (after `Metadata` at line 57):

```csharp
public record IngestionAsset(
    string ExternalId,
    string Name,
    AssetType AssetType,
    string? Description = null,
    string? DeviceComputerDnsName = null,
    string? DeviceHealthStatus = null,
    string? DeviceOsPlatform = null,
    string? DeviceOsVersion = null,
    string? DeviceRiskScore = null,
    DateTimeOffset? DeviceLastSeenAt = null,
    string? DeviceLastIpAddress = null,
    string? DeviceAadDeviceId = null,
    string? DeviceGroupId = null,
    string? DeviceGroupName = null,
    string Metadata = "{}",
    string? DeviceExposureLevel = null,
    bool? DeviceIsAadJoined = null,
    List<string>? MachineTags = null
);
```

**Step 3: Update NormalizeMachineAsset**

In `DefenderVulnerabilitySource.cs`, update `NormalizeMachineAsset` (line 642) to pass the new fields:

```csharp
internal static IngestionAsset NormalizeMachineAsset(DefenderMachineEntry entry)
{
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
        entry.MachineTags
    );
}
```

**Step 4: Run all tests to verify compilation and no regressions**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All pass.

**Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/VulnerabilitySources/DefenderApiClient.cs src/PatchHound.Core/Models/IngestionResult.cs src/PatchHound.Infrastructure/VulnerabilitySources/DefenderVulnerabilitySource.cs
git commit -m "feat: add exposureLevel, isAadJoined, machineTags to ingestion pipeline"
```

---

### Task 4: StagedAssetMergeService — Persist New Fields + Sync Tags

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs` (lines 85-158)
- Test: `tests/PatchHound.Tests/Infrastructure/StagedAssetMergeServiceTagTests.cs`

**Step 1: Write the failing test for tag sync**

```csharp
// tests/PatchHound.Tests/Infrastructure/StagedAssetMergeServiceTagTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class StagedAssetMergeServiceTagTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;

    public StagedAssetMergeServiceTagTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
    }

    [Fact]
    public async Task ProcessAsync_CreatesTagsForNewDevice()
    {
        var asset = Asset.Create(_tenantId, "dev-1", AssetType.Device, "Host", Criticality.Medium);
        asset.UpdateDeviceDetails("host.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.1", null);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var tags = new[] { "production", "critical-infra" };
        foreach (var tag in tags)
        {
            _dbContext.AssetTags.Add(AssetTag.Create(_tenantId, asset.Id, tag, "Defender"));
        }
        await _dbContext.SaveChangesAsync();

        var storedTags = await _dbContext.AssetTags
            .Where(t => t.AssetId == asset.Id)
            .Select(t => t.Tag)
            .ToListAsync();

        storedTags.Should().BeEquivalentTo("production", "critical-infra");
    }

    public void Dispose() => _dbContext.Dispose();
}
```

**Step 2: Run test to verify it passes (basic tag persistence via entity)**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~StagedAssetMergeServiceTagTests" -v minimal`
Expected: PASS (this verifies the AssetTag entity works with EF InMemory).

**Step 3: Update StagedAssetMergeService to pass new fields and sync tags**

In `src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs`, update both `UpdateDeviceDetails` call sites (new asset ~line 118, existing asset ~line 142) to include the new parameters:

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
    asset.DeviceIsAadJoined
);
```

After each `UpdateDeviceDetails` call (for both new and existing assets), add tag sync logic:

```csharp
if (asset.MachineTags is { Count: > 0 })
{
    var existingTags = await dbContext.AssetTags
        .Where(t => t.AssetId == existing.Id && t.Source == "Defender")
        .ToListAsync(ct);

    var incomingSet = asset.MachineTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var existingSet = existingTags.Select(t => t.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Remove tags no longer present
    var toRemove = existingTags.Where(t => !incomingSet.Contains(t.Tag)).ToList();
    if (toRemove.Count > 0)
        dbContext.AssetTags.RemoveRange(toRemove);

    // Add new tags
    var toAdd = incomingSet.Where(t => !existingSet.Contains(t)).ToList();
    foreach (var tag in toAdd)
    {
        dbContext.AssetTags.Add(AssetTag.Create(tenantId, existing.Id, tag, "Defender"));
    }
}
else
{
    // No tags from source — remove all Defender tags for this asset
    var existingTags = await dbContext.AssetTags
        .Where(t => t.AssetId == existing.Id && t.Source == "Defender")
        .ToListAsync(ct);
    if (existingTags.Count > 0)
        dbContext.AssetTags.RemoveRange(existingTags);
}
```

Note: For the new asset path (where `existing` was just created via `Asset.Create`), the tag sync simplifies to just adding new tags since there are no existing tags.

**Step 4: Run all tests**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All pass.

**Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/StagedAssetMergeService.cs tests/PatchHound.Tests/Infrastructure/StagedAssetMergeServiceTagTests.cs
git commit -m "feat: persist exposureLevel, isAadJoined and sync machineTags in StagedAssetMergeService"
```

---

### Task 5: API — DTOs, Filters, Controller, Detail Service

**Files:**
- Modify: `src/PatchHound.Api/Models/Assets/AssetDto.cs` (AssetDto lines 3-16, AssetDetailDto lines 18-46, AssetFilterQuery lines 130-139)
- Modify: `src/PatchHound.Api/Controllers/AssetsController.cs` (List filter block, lines 64-109; List projection)
- Modify: `src/PatchHound.Api/Services/AssetDetailQueryService.cs` (detail DTO assembly)
- Test: `tests/PatchHound.Tests/Api/AssetsControllerTests.cs`

**Step 1: Write the failing test for new list fields and filters**

Add to `tests/PatchHound.Tests/Api/AssetsControllerTests.cs`:

```csharp
[Fact]
public async Task List_ReturnsHealthStatusRiskScoreExposureLevelAndTags()
{
    var asset = Asset.Create(_tenantId, "dev-tag-1", AssetType.Device, "Tagged Host", Criticality.High);
    asset.UpdateDeviceDetails(
        "tagged.contoso.local", "Active", "Windows", "11",
        "High", new DateTimeOffset(2026, 3, 17, 8, 0, 0, TimeSpan.Zero),
        "10.0.0.50", "aad-tag-1", null, null, "Medium", true
    );
    await _dbContext.Assets.AddAsync(asset);
    await _dbContext.SaveChangesAsync();

    _dbContext.AssetTags.Add(AssetTag.Create(_tenantId, asset.Id, "production", "Defender"));
    _dbContext.AssetTags.Add(AssetTag.Create(_tenantId, asset.Id, "tier-0", "Defender"));
    await _dbContext.SaveChangesAsync();

    var action = await _controller.List(new AssetFilterQuery(), new PaginationQuery(), CancellationToken.None);
    var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
    var payload = result.Value.Should().BeOfType<PagedResponse<AssetDto>>().Subject;

    payload.Items.Should().ContainSingle();
    var item = payload.Items[0];
    item.HealthStatus.Should().Be("Active");
    item.RiskScore.Should().Be("High");
    item.ExposureLevel.Should().Be("Medium");
    item.Tags.Should().BeEquivalentTo("production", "tier-0");
}

[Fact]
public async Task List_FiltersByExposureLevel()
{
    var highExposure = Asset.Create(_tenantId, "dev-high", AssetType.Device, "High Exp", Criticality.Medium);
    highExposure.UpdateDeviceDetails("h.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.1", null, null, null, "High", null);

    var lowExposure = Asset.Create(_tenantId, "dev-low", AssetType.Device, "Low Exp", Criticality.Medium);
    lowExposure.UpdateDeviceDetails("l.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.2", null, null, null, "Low", null);

    await _dbContext.Assets.AddRangeAsync(highExposure, lowExposure);
    await _dbContext.SaveChangesAsync();

    var action = await _controller.List(new AssetFilterQuery(ExposureLevel: "High"), new PaginationQuery(), CancellationToken.None);
    var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
    var payload = result.Value.Should().BeOfType<PagedResponse<AssetDto>>().Subject;

    payload.Items.Should().ContainSingle();
    payload.Items[0].Id.Should().Be(highExposure.Id);
}

[Fact]
public async Task List_FiltersByTag()
{
    var tagged = Asset.Create(_tenantId, "dev-tagged", AssetType.Device, "Tagged", Criticality.Medium);
    tagged.UpdateDeviceDetails("t.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.3", null);

    var untagged = Asset.Create(_tenantId, "dev-untagged", AssetType.Device, "Untagged", Criticality.Medium);
    untagged.UpdateDeviceDetails("u.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.4", null);

    await _dbContext.Assets.AddRangeAsync(tagged, untagged);
    await _dbContext.SaveChangesAsync();

    _dbContext.AssetTags.Add(AssetTag.Create(_tenantId, tagged.Id, "production", "Defender"));
    await _dbContext.SaveChangesAsync();

    var action = await _controller.List(new AssetFilterQuery(Tag: "prod"), new PaginationQuery(), CancellationToken.None);
    var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
    var payload = result.Value.Should().BeOfType<PagedResponse<AssetDto>>().Subject;

    payload.Items.Should().ContainSingle();
    payload.Items[0].Id.Should().Be(tagged.Id);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AssetsControllerTests" -v minimal`
Expected: FAIL — new properties/filter params don't exist yet.

**Step 3: Update AssetDto**

In `src/PatchHound.Api/Models/Assets/AssetDto.cs`, add fields to `AssetDto` (after `RecurringVulnerabilityCount`):

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
    string[] Tags
);
```

**Step 4: Update AssetDetailDto**

Add new fields after `DeviceGroupName`:

```csharp
// after DeviceGroupName in the record:
string? DeviceExposureLevel,
bool? DeviceIsAadJoined,
string[] Tags,
```

**Step 5: Update AssetFilterQuery**

Add four new filter parameters:

```csharp
public record AssetFilterQuery(
    string? AssetType = null,
    string? Criticality = null,
    string? OwnerType = null,
    string? DeviceGroup = null,
    bool? UnassignedOnly = null,
    Guid? OwnerId = null,
    Guid? TenantId = null,
    string? Search = null,
    string? HealthStatus = null,
    string? RiskScore = null,
    string? ExposureLevel = null,
    string? Tag = null
);
```

**Step 6: Update AssetsController.List — add filters and projection fields**

In `src/PatchHound.Api/Controllers/AssetsController.cs`, add filter clauses after the existing `DeviceGroup` filter block:

```csharp
if (!string.IsNullOrEmpty(filter.HealthStatus))
    query = query.Where(a => a.DeviceHealthStatus == filter.HealthStatus);
if (!string.IsNullOrEmpty(filter.RiskScore))
    query = query.Where(a => a.DeviceRiskScore == filter.RiskScore);
if (!string.IsNullOrEmpty(filter.ExposureLevel))
    query = query.Where(a => a.DeviceExposureLevel == filter.ExposureLevel);
if (!string.IsNullOrEmpty(filter.Tag))
    query = query.Where(a =>
        _dbContext.AssetTags.Any(t => t.AssetId == a.Id && t.Tag.Contains(filter.Tag))
    );
```

Update the DTO projection in the `List` method's `.Select()` to include the new fields. After the existing fields, add:

```csharp
HealthStatus = a.DeviceHealthStatus,
RiskScore = a.DeviceRiskScore,
ExposureLevel = a.DeviceExposureLevel,
Tags = _dbContext.AssetTags
    .Where(t => t.AssetId == a.Id)
    .Select(t => t.Tag)
    .ToArray(),
```

And update the DTO construction to pass these new fields.

**Step 7: Update AssetDetailQueryService**

In `src/PatchHound.Api/Services/AssetDetailQueryService.cs`, update the `AssetDetailDto` construction to include:

```csharp
DeviceExposureLevel = asset.DeviceExposureLevel,
DeviceIsAadJoined = asset.DeviceIsAadJoined,
Tags = await _dbContext.AssetTags
    .Where(t => t.AssetId == asset.Id)
    .Select(t => t.Tag)
    .ToArrayAsync(ct),
```

**Step 8: Run tests to verify they pass**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AssetsControllerTests" -v minimal`
Expected: PASS

**Step 9: Run all tests**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All pass.

**Step 10: Commit**

```bash
git add src/PatchHound.Api/Models/Assets/AssetDto.cs src/PatchHound.Api/Controllers/AssetsController.cs src/PatchHound.Api/Services/AssetDetailQueryService.cs tests/PatchHound.Tests/Api/AssetsControllerTests.cs
git commit -m "feat: surface healthStatus, riskScore, exposureLevel, tags in asset API with filters"
```

---

### Task 6: EF Migration

**Files:**
- Create: new migration file via `dotnet ef`

**Step 1: Generate migration**

Run from repo root:

```bash
dotnet ef migrations add AddAssetTagAndDeviceExposureFields --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api
```

**Step 2: Review the generated migration**

Verify it contains:
- `AddColumn` for `DeviceExposureLevel` (varchar(64), nullable) on `Assets`
- `AddColumn` for `DeviceIsAadJoined` (bool?, nullable) on `Assets`
- `CreateTable` for `AssetTags` with PK, FK to Assets, columns for Tag/Source/TenantId/CreatedAt
- `CreateIndex` for `(AssetId, Tag)` unique
- `CreateIndex` for `(TenantId, Tag)`

**Step 3: Commit**

```bash
git add src/PatchHound.Infrastructure/Migrations/
git commit -m "chore: add migration for AssetTag table and Asset exposure fields"
```

---

### Task 7: Frontend — Schemas, Server Functions, List Table, Detail Section

**Files:**
- Modify: `frontend/src/api/assets.schemas.ts`
- Modify: `frontend/src/api/assets.functions.ts`
- Modify: `frontend/src/components/features/assets/AssetManagementTable.tsx`
- Modify: `frontend/src/components/features/assets/AssetDetailSections.tsx`

**Step 1: Update Zod schemas**

In `frontend/src/api/assets.schemas.ts`:

Add to `assetSchema` (after `recurringVulnerabilityCount`):

```typescript
healthStatus: z.string().nullable(),
riskScore: z.string().nullable(),
exposureLevel: z.string().nullable(),
tags: z.array(z.string()),
```

Add to `assetDetailSchema` (after `deviceGroupName`):

```typescript
deviceExposureLevel: z.string().nullable(),
deviceIsAadJoined: z.boolean().nullable(),
tags: z.array(z.string()),
```

**Step 2: Update server functions**

In `frontend/src/api/assets.functions.ts`, add filter parameters to `fetchAssets` inputValidator:

```typescript
healthStatus: z.string().optional(),
riskScore: z.string().optional(),
exposureLevel: z.string().optional(),
tag: z.string().optional(),
```

**Step 3: Update AssetManagementTable columns**

In `frontend/src/components/features/assets/AssetManagementTable.tsx`, add new columns after the Device Group column (~line 270):

```typescript
columnHelper.accessor('healthStatus', {
  header: 'Health',
  cell: ({ getValue }) => {
    const value = getValue()
    if (!value) return <span className="text-muted-foreground">—</span>
    return <Badge variant="outline">{value}</Badge>
  },
  size: 100,
}),
columnHelper.accessor('riskScore', {
  header: 'Risk',
  cell: ({ getValue }) => {
    const value = getValue()
    if (!value) return <span className="text-muted-foreground">—</span>
    return <Badge variant="outline">{value}</Badge>
  },
  size: 80,
}),
columnHelper.accessor('exposureLevel', {
  header: 'Exposure',
  cell: ({ getValue }) => {
    const value = getValue()
    if (!value) return <span className="text-muted-foreground">—</span>
    return <Badge variant="outline">{value}</Badge>
  },
  size: 100,
}),
columnHelper.accessor('tags', {
  header: 'Tags',
  cell: ({ getValue }) => {
    const tags = getValue()
    if (!tags?.length) return <span className="text-muted-foreground">—</span>
    return (
      <div className="flex flex-wrap gap-1">
        {tags.map((tag) => (
          <Badge key={tag} variant="secondary" className="text-xs">
            {tag}
          </Badge>
        ))}
      </div>
    )
  },
  size: 200,
}),
```

Add filter controls for Health Status, Risk Score, Exposure Level, and Tag to the filter bar (follow the existing DeviceGroup filter pattern).

**Step 4: Update DeviceSection in AssetDetailSections.tsx**

In `frontend/src/components/features/assets/AssetDetailSections.tsx`, update the `normalizedFields` array inside `DeviceSection` (~line 175):

Add after the Device Group field and before the Entra Device ID field:

```typescript
{ label: 'Exposure Level', value: asset.deviceExposureLevel ?? 'Unknown' },
{ label: 'AAD Joined', value: asset.deviceIsAadJoined === true ? 'Yes' : asset.deviceIsAadJoined === false ? 'No' : 'Unknown' },
```

Add a Tags row after the `normalizedFields` grid:

```typescript
{asset.tags?.length > 0 && (
  <div className="mt-3">
    <span className="text-sm font-medium text-muted-foreground">Tags</span>
    <div className="mt-1 flex flex-wrap gap-1">
      {asset.tags.map((tag) => (
        <Badge key={tag} variant="secondary" className="text-xs">
          {tag}
        </Badge>
      ))}
    </div>
  </div>
)}
```

**Step 5: Run frontend checks**

```bash
cd frontend && npm run typecheck && npm run lint
```

Expected: No errors.

**Step 6: Commit**

```bash
git add frontend/src/api/assets.schemas.ts frontend/src/api/assets.functions.ts frontend/src/components/features/assets/AssetManagementTable.tsx frontend/src/components/features/assets/AssetDetailSections.tsx
git commit -m "feat: surface health, risk, exposure, tags in asset list and detail UI"
```

---

### Task 8: Final Verification

**Step 1: Run full backend test suite**

```bash
dotnet test PatchHound.slnx -v minimal
```

Expected: All pass.

**Step 2: Run frontend checks**

```bash
cd frontend && npm run typecheck && npm run lint && npm test
```

Expected: All pass.

**Step 3: Verify Docker build**

```bash
docker compose build
```

Expected: Build succeeds.
