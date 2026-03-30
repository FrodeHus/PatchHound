# Asset Rules Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement asset rules that automatically assign security profiles and teams to assets based on configurable filters, triggered after ingestion or manually.

**Architecture:** New `AssetRule` entity with JSON filter tree + operations. `AssetRuleFilterBuilder` converts filter tree to EF Core expressions. `AssetRuleEvaluationService` evaluates rules in priority order (first-match-wins). Frontend wizard in admin area for CRUD.

**Tech Stack:** C# / EF Core (backend), React / TanStack Router / Zod (frontend), JSON for filter/operation storage.

---

### Task 1: AssetRule Entity + Migration

**Files:**
- Create: `src/PatchHound.Core/Entities/AssetRule.cs`
- Create: `src/PatchHound.Core/Models/AssetRuleModels.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/AssetRuleConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`

**Step 1: Create filter/operation model types**

Create `src/PatchHound.Core/Models/AssetRuleModels.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatchHound.Core.Models;

[JsonConverter(typeof(FilterNodeConverter))]
public abstract record FilterNode(string Type);

public record FilterGroup(
    string Operator, // "AND" | "OR"
    List<FilterNode> Conditions
) : FilterNode("group");

public record FilterCondition(
    string Field,    // AssetType, Name, DeviceGroup, Vendor, Platform, Domain, Tag
    string Operator, // Equals, StartsWith, Contains, EndsWith
    string Value
) : FilterNode("condition");

public record AssetRuleOperation(
    string Type,       // AssignSecurityProfile, AssignTeam
    Dictionary<string, string> Parameters
);

public class FilterNodeConverter : JsonConverter<FilterNode>
{
    public override FilterNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();
        var raw = root.GetRawText();
        return type switch
        {
            "group" => JsonSerializer.Deserialize<FilterGroup>(raw, options),
            "condition" => JsonSerializer.Deserialize<FilterCondition>(raw, options),
            _ => throw new JsonException($"Unknown filter node type: {type}")
        };
    }

    public override void Write(Utf8JsonWriter writer, FilterNode value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
```

**Step 2: Create AssetRule entity**

Create `src/PatchHound.Core/Entities/AssetRule.cs`:

```csharp
using System.Text.Json;
using PatchHound.Core.Models;

namespace PatchHound.Core.Entities;

public class AssetRule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; private set; }
    public string FilterDefinition { get; private set; } = null!;
    public string Operations { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastExecutedAt { get; private set; }
    public int? LastMatchCount { get; private set; }

    public static AssetRule Create(
        Guid tenantId,
        string name,
        string? description,
        int priority,
        FilterNode filter,
        List<AssetRuleOperation> operations)
    {
        var now = DateTimeOffset.UtcNow;
        return new AssetRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = description,
            Priority = priority,
            Enabled = true,
            FilterDefinition = JsonSerializer.Serialize(filter, JsonOptions),
            Operations = JsonSerializer.Serialize(operations, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string? description, FilterNode filter, List<AssetRuleOperation> operations)
    {
        Name = name;
        Description = description;
        FilterDefinition = JsonSerializer.Serialize(filter, JsonOptions);
        Operations = JsonSerializer.Serialize(operations, JsonOptions);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetPriority(int priority)
    {
        Priority = priority;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordExecution(int matchCount)
    {
        LastExecutedAt = DateTimeOffset.UtcNow;
        LastMatchCount = matchCount;
    }

    public FilterNode ParseFilter() =>
        JsonSerializer.Deserialize<FilterNode>(FilterDefinition, JsonOptions)!;

    public List<AssetRuleOperation> ParseOperations() =>
        JsonSerializer.Deserialize<List<AssetRuleOperation>>(Operations, JsonOptions)!;
}
```

**Step 3: Create EF configuration**

Create `src/PatchHound.Infrastructure/Data/Configurations/AssetRuleConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AssetRuleConfiguration : IEntityTypeConfiguration<AssetRule>
{
    public void Configure(EntityTypeBuilder<AssetRule> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(256).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(2048);
        builder.Property(r => r.FilterDefinition).IsRequired();
        builder.Property(r => r.Operations).IsRequired();
        builder.HasIndex(r => new { r.TenantId, r.Priority }).IsUnique();
    }
}
```

**Step 4: Register DbSet and query filter**

Add to `PatchHoundDbContext.cs`:

```csharp
public DbSet<AssetRule> AssetRules => Set<AssetRule>();
```

And in `OnModelCreating`, add the tenant query filter:

```csharp
modelBuilder
    .Entity<AssetRule>()
    .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
```

**Step 5: Create migration**

Run: `dotnet ef migrations add AddAssetRules --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api`

**Step 6: Verify build**

Run: `dotnet build PatchHound.slnx`

**Step 7: Commit**

```bash
git add src/PatchHound.Core/Entities/AssetRule.cs src/PatchHound.Core/Models/AssetRuleModels.cs src/PatchHound.Infrastructure/Data/Configurations/AssetRuleConfiguration.cs src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs
git commit -m "feat: add AssetRule entity and migration"
```

---

### Task 2: AssetRuleFilterBuilder — Expression Tree Builder

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/AssetRuleFilterBuilder.cs`

**Step 1: Implement the filter builder**

This service converts a `FilterNode` tree into `Expression<Func<Asset, bool>>` that EF Core can translate to SQL.

```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class AssetRuleFilterBuilder
{
    private readonly PatchHoundDbContext _dbContext;

    public AssetRuleFilterBuilder(PatchHoundDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Expression<Func<Asset, bool>> Build(FilterNode root)
    {
        var param = Expression.Parameter(typeof(Asset), "a");
        var body = BuildNode(root, param);
        return Expression.Lambda<Func<Asset, bool>>(body, param);
    }

    private Expression BuildNode(FilterNode node, ParameterExpression param)
    {
        return node switch
        {
            FilterGroup group => BuildGroup(group, param),
            FilterCondition condition => BuildCondition(condition, param),
            _ => throw new InvalidOperationException($"Unknown filter node type: {node.Type}")
        };
    }

    private Expression BuildGroup(FilterGroup group, ParameterExpression param)
    {
        if (group.Conditions.Count == 0)
            return Expression.Constant(true);

        var expressions = group.Conditions.Select(c => BuildNode(c, param)).ToList();
        var combined = expressions[0];
        for (var i = 1; i < expressions.Count; i++)
        {
            combined = group.Operator.ToUpperInvariant() == "OR"
                ? Expression.OrElse(combined, expressions[i])
                : Expression.AndAlso(combined, expressions[i]);
        }
        return combined;
    }

    private Expression BuildCondition(FilterCondition condition, ParameterExpression param)
    {
        return condition.Field switch
        {
            "AssetType" => BuildEnumComparison<AssetType>(param, nameof(Asset.AssetType), condition),
            "Name" => BuildStringComparison(param, nameof(Asset.Name), condition),
            "DeviceGroup" => BuildStringComparison(param, nameof(Asset.DeviceGroupName), condition),
            "Platform" => BuildStringComparison(param, nameof(Asset.DeviceOsPlatform), condition),
            "Domain" => BuildStringComparison(param, nameof(Asset.DeviceComputerDnsName), condition),
            "Tag" => BuildTagCondition(param, condition),
            _ => throw new InvalidOperationException($"Unknown filter field: {condition.Field}")
        };
    }

    private static Expression BuildStringComparison(
        ParameterExpression param, string propertyName, FilterCondition condition)
    {
        var property = Expression.Property(param, propertyName);
        var value = Expression.Constant(condition.Value);

        // Handle nullable strings: coalesce to empty string
        var safeProperty = property.Type == typeof(string)
            ? (Expression)property
            : Expression.Coalesce(property, Expression.Constant(string.Empty));

        // For nullable string properties (string?), we need the coalesce
        if (Nullable.GetUnderlyingType(property.Type) != null || !property.Type.IsValueType)
        {
            // Check if property type allows null
            var nullCheck = Expression.NotEqual(property, Expression.Constant(null, property.Type));
            var comparison = condition.Operator switch
            {
                "Equals" => Expression.Call(property, nameof(string.Equals), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
                "StartsWith" => Expression.Call(property, nameof(string.StartsWith), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
                "Contains" => Expression.Call(property, nameof(string.Contains), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
                "EndsWith" => Expression.Call(property, nameof(string.EndsWith), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
                _ => throw new InvalidOperationException($"Unknown operator: {condition.Operator}")
            };
            // For non-nullable string, just return comparison; for nullable, add null guard
            return property.Type == typeof(string) && propertyName == nameof(Asset.Name)
                ? (Expression)comparison
                : Expression.AndAlso(nullCheck, comparison);
        }

        return condition.Operator switch
        {
            "Equals" => Expression.Call(safeProperty, nameof(string.Equals), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
            "StartsWith" => Expression.Call(safeProperty, nameof(string.StartsWith), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
            "Contains" => Expression.Call(safeProperty, nameof(string.Contains), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
            "EndsWith" => Expression.Call(safeProperty, nameof(string.EndsWith), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
            _ => throw new InvalidOperationException($"Unknown operator: {condition.Operator}")
        };
    }

    private static Expression BuildEnumComparison<TEnum>(
        ParameterExpression param, string propertyName, FilterCondition condition)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(condition.Value, ignoreCase: true, out var enumValue))
            throw new InvalidOperationException($"Invalid {typeof(TEnum).Name} value: {condition.Value}");

        var property = Expression.Property(param, propertyName);
        return Expression.Equal(property, Expression.Constant(enumValue));
    }

    private Expression BuildTagCondition(ParameterExpression param, FilterCondition condition)
    {
        // Build: _dbContext.AssetTags.Any(t => t.AssetId == a.Id && <string op on t.Tag>)
        var assetId = Expression.Property(param, nameof(Asset.Id));
        var tagParam = Expression.Parameter(typeof(AssetTag), "t");
        var tagAssetId = Expression.Property(tagParam, nameof(AssetTag.AssetId));
        var tagValue = Expression.Property(tagParam, nameof(AssetTag.Tag));
        var value = Expression.Constant(condition.Value);

        var assetIdMatch = Expression.Equal(tagAssetId, assetId);
        var stringMatch = condition.Operator switch
        {
            "Equals" => Expression.Call(tagValue, nameof(string.Equals), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
            "StartsWith" => Expression.Call(tagValue, nameof(string.StartsWith), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
            "Contains" => Expression.Call(tagValue, nameof(string.Contains), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
            "EndsWith" => Expression.Call(tagValue, nameof(string.EndsWith), null, value, Expression.Constant(StringComparison.OrdinalIgnoreCase)),
            _ => throw new InvalidOperationException($"Unknown operator: {condition.Operator}")
        };
        var predicate = Expression.AndAlso(assetIdMatch, stringMatch);
        var lambda = Expression.Lambda<Func<AssetTag, bool>>(predicate, tagParam);

        // Call _dbContext.AssetTags.Any(lambda)
        var assetTags = Expression.Property(Expression.Constant(_dbContext), nameof(PatchHoundDbContext.AssetTags));
        var anyMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == "Any" && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(AssetTag));
        return Expression.Call(anyMethod, assetTags, lambda);
    }
}
```

Note: The Tag condition uses `Enumerable.Any` on the DbSet which EF Core translates to a SQL EXISTS subquery. The `StringComparison.OrdinalIgnoreCase` may need adjustment depending on database collation — EF Core for SQL Server/PostgreSQL typically handles case-insensitivity at the database level via `LIKE` or `ILIKE`.

**Step 2: Register in DI**

Add `AssetRuleFilterBuilder` as scoped service in `Program.cs` or wherever services are registered.

**Step 3: Verify build**

Run: `dotnet build PatchHound.slnx`

**Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/AssetRuleFilterBuilder.cs
git commit -m "feat: add AssetRuleFilterBuilder for dynamic filter expression building"
```

---

### Task 3: AssetRuleEvaluationService

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/AssetRuleEvaluationService.cs`

**Step 1: Implement the evaluation service**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class AssetRuleEvaluationService
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly AssetRuleFilterBuilder _filterBuilder;
    private readonly ILogger<AssetRuleEvaluationService> _logger;

    public AssetRuleEvaluationService(
        PatchHoundDbContext dbContext,
        AssetRuleFilterBuilder filterBuilder,
        ILogger<AssetRuleEvaluationService> logger)
    {
        _dbContext = dbContext;
        _filterBuilder = filterBuilder;
        _logger = logger;
    }

    public async Task EvaluateRulesAsync(Guid tenantId, CancellationToken ct)
    {
        var rules = await _dbContext.AssetRules
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Enabled)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            _logger.LogDebug("No enabled asset rules for tenant {TenantId}", tenantId);
            return;
        }

        var claimedAssetIds = new HashSet<Guid>();

        foreach (var rule in rules)
        {
            try
            {
                var filter = rule.ParseFilter();
                var predicate = _filterBuilder.Build(filter);

                var matchingAssetIds = await _dbContext.Assets
                    .AsNoTracking()
                    .Where(a => a.TenantId == tenantId)
                    .Where(predicate)
                    .Select(a => a.Id)
                    .ToListAsync(ct);

                // First-match-wins: exclude already-claimed assets
                var unclaimedIds = matchingAssetIds
                    .Where(id => !claimedAssetIds.Contains(id))
                    .ToList();

                if (unclaimedIds.Count > 0)
                {
                    var operations = rule.ParseOperations();
                    foreach (var op in operations)
                    {
                        await ApplyOperationAsync(tenantId, unclaimedIds, op, ct);
                    }

                    foreach (var id in unclaimedIds)
                        claimedAssetIds.Add(id);
                }

                // Track rule and update execution metadata
                var tracked = _dbContext.AssetRules.Attach(rule);
                tracked.Entity.RecordExecution(unclaimedIds.Count);
                await _dbContext.SaveChangesAsync(ct);
                _dbContext.ChangeTracker.Clear();

                _logger.LogInformation(
                    "Asset rule '{RuleName}' (priority {Priority}) matched {MatchCount} assets for tenant {TenantId}",
                    rule.Name, rule.Priority, unclaimedIds.Count, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error evaluating asset rule '{RuleName}' ({RuleId}) for tenant {TenantId}",
                    rule.Name, rule.Id, tenantId);
            }
        }
    }

    public async Task<(int count, List<AssetPreviewItem> samples)> PreviewFilterAsync(
        Guid tenantId, FilterNode filter, int sampleSize, CancellationToken ct)
    {
        var predicate = _filterBuilder.Build(filter);

        var query = _dbContext.Assets
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .Where(predicate);

        var count = await query.CountAsync(ct);
        var samples = await query
            .OrderBy(a => a.Name)
            .Take(sampleSize)
            .Select(a => new AssetPreviewItem(a.Id, a.Name, a.AssetType.ToString()))
            .ToListAsync(ct);

        return (count, samples);
    }

    private async Task ApplyOperationAsync(
        Guid tenantId, List<Guid> assetIds, AssetRuleOperation op, CancellationToken ct)
    {
        switch (op.Type)
        {
            case "AssignSecurityProfile":
                if (op.Parameters.TryGetValue("securityProfileId", out var profileIdStr)
                    && Guid.TryParse(profileIdStr, out var profileId))
                {
                    await _dbContext.Assets
                        .Where(a => a.TenantId == tenantId && assetIds.Contains(a.Id))
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.SecurityProfileId, profileId), ct);
                }
                break;

            case "AssignTeam":
                if (op.Parameters.TryGetValue("teamId", out var teamIdStr)
                    && Guid.TryParse(teamIdStr, out var teamId))
                {
                    await _dbContext.Assets
                        .Where(a => a.TenantId == tenantId && assetIds.Contains(a.Id))
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.FallbackTeamId, teamId), ct);
                }
                break;

            default:
                _logger.LogWarning("Unknown asset rule operation type: {OperationType}", op.Type);
                break;
        }
    }
}

public record AssetPreviewItem(Guid Id, string Name, string AssetType);
public record AssetRuleOperation(string Type, Dictionary<string, string> Parameters);
```

Note: The `AssetRuleOperation` record here duplicates the one in `AssetRuleModels.cs` — use the one from `PatchHound.Core.Models` and remove this duplicate during implementation.

**Step 2: Register in DI**

**Step 3: Verify build**

Run: `dotnet build PatchHound.slnx`

**Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/AssetRuleEvaluationService.cs
git commit -m "feat: add AssetRuleEvaluationService for rule evaluation and preview"
```

---

### Task 4: AssetRulesController — API Endpoints

**Files:**
- Create: `src/PatchHound.Api/Controllers/AssetRulesController.cs`
- Create: `src/PatchHound.Api/Models/AssetRules/AssetRuleDto.cs`
- Create: `src/PatchHound.Api/Models/AssetRules/CreateAssetRuleRequest.cs`
- Create: `src/PatchHound.Api/Models/AssetRules/UpdateAssetRuleRequest.cs`
- Create: `src/PatchHound.Api/Models/AssetRules/PreviewFilterRequest.cs`
- Create: `src/PatchHound.Api/Models/AssetRules/ReorderRulesRequest.cs`

**Step 1: Create DTOs**

`AssetRuleDto.cs`:
```csharp
namespace PatchHound.Api.Models.AssetRules;

public record AssetRuleDto(
    Guid Id,
    string Name,
    string? Description,
    int Priority,
    bool Enabled,
    object FilterDefinition,
    object[] Operations,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastExecutedAt,
    int? LastMatchCount
);
```

`CreateAssetRuleRequest.cs`:
```csharp
using System.Text.Json;

namespace PatchHound.Api.Models.AssetRules;

public record CreateAssetRuleRequest(
    string Name,
    string? Description,
    JsonElement FilterDefinition,
    JsonElement Operations
);
```

`UpdateAssetRuleRequest.cs`:
```csharp
using System.Text.Json;

namespace PatchHound.Api.Models.AssetRules;

public record UpdateAssetRuleRequest(
    string Name,
    string? Description,
    bool Enabled,
    JsonElement FilterDefinition,
    JsonElement Operations
);
```

`PreviewFilterRequest.cs`:
```csharp
using System.Text.Json;

namespace PatchHound.Api.Models.AssetRules;

public record PreviewFilterRequest(JsonElement FilterDefinition);
```

`ReorderRulesRequest.cs`:
```csharp
namespace PatchHound.Api.Models.AssetRules;

public record ReorderRulesRequest(List<Guid> RuleIds);
```

**Step 2: Create controller**

Create `src/PatchHound.Api/Controllers/AssetRulesController.cs` with endpoints:

- `GET /api/asset-rules` — paged list
- `GET /api/asset-rules/{id}` — single rule
- `POST /api/asset-rules` — create (auto-assign priority = max + 1)
- `PUT /api/asset-rules/{id}` — update
- `DELETE /api/asset-rules/{id}` — delete (reorder remaining)
- `POST /api/asset-rules/preview` — preview filter (count + 5 samples)
- `POST /api/asset-rules/run` — manual trigger
- `PUT /api/asset-rules/reorder` — reorder priorities

All endpoints use `[Authorize(Policy = Policies.ConfigureTenant)]`.

Use `JsonSerializer.Deserialize<FilterNode>` and `JsonSerializer.Deserialize<List<AssetRuleOperation>>` to parse request JSON into model types, and serialize back for DTOs.

**Step 3: Verify build**

Run: `dotnet build PatchHound.slnx`

**Step 4: Commit**

```bash
git add src/PatchHound.Api/Controllers/AssetRulesController.cs src/PatchHound.Api/Models/AssetRules/
git commit -m "feat: add AssetRulesController with CRUD, preview, and manual trigger"
```

---

### Task 5: Hook into Ingestion Pipeline

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/IngestionService.cs`

**Step 1: Add AssetRuleEvaluationService dependency**

Add `AssetRuleEvaluationService` to the constructor.

**Step 2: Call after successful ingestion**

After the enrichment job enqueueing (line ~522), add:

```csharp
await _assetRuleEvaluationService.EvaluateRulesAsync(tenantId, ct);
```

This runs synchronously within the ingestion pipeline after merge + enrichment enqueueing.

**Step 3: Verify build**

Run: `dotnet build PatchHound.slnx`

**Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/IngestionService.cs
git commit -m "feat: evaluate asset rules after successful ingestion"
```

---

### Task 6: Register Services in DI

**Files:**
- Modify: `src/PatchHound.Api/Program.cs` (or wherever services are registered)

**Step 1: Register new services**

```csharp
builder.Services.AddScoped<AssetRuleFilterBuilder>();
builder.Services.AddScoped<AssetRuleEvaluationService>();
```

**Step 2: Verify build + run**

Run: `dotnet build PatchHound.slnx`

**Step 3: Commit**

```bash
git add src/PatchHound.Api/Program.cs
git commit -m "chore: register asset rule services in DI"
```

---

### Task 7: Frontend — API Schemas and Server Functions

**Files:**
- Create: `frontend/src/api/asset-rules.schemas.ts`
- Create: `frontend/src/api/asset-rules.functions.ts`

**Step 1: Create schemas**

```typescript
import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

// Filter tree types
const filterConditionSchema: z.ZodType = z.object({
  type: z.literal('condition'),
  field: z.string(),
  operator: z.string(),
  value: z.string(),
})

const filterGroupSchema: z.ZodType = z.lazy(() =>
  z.object({
    type: z.literal('group'),
    operator: z.enum(['AND', 'OR']),
    conditions: z.array(z.union([filterConditionSchema, filterGroupSchema])),
  })
)

export const filterNodeSchema = z.union([filterConditionSchema, filterGroupSchema])

export const assetRuleOperationSchema = z.object({
  type: z.string(),
  parameters: z.record(z.string()),
})

export const assetRuleSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  description: z.string().nullable(),
  priority: z.number(),
  enabled: z.boolean(),
  filterDefinition: filterNodeSchema,
  operations: z.array(assetRuleOperationSchema),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
  lastExecutedAt: isoDateTimeSchema.nullable(),
  lastMatchCount: z.number().nullable(),
})

export const pagedAssetRulesSchema = pagedResponseMetaSchema.extend({
  items: z.array(assetRuleSchema),
})

export const filterPreviewSchema = z.object({
  count: z.number(),
  samples: z.array(z.object({
    id: z.string().uuid(),
    name: z.string(),
    assetType: z.string(),
  })),
})

export type FilterNode = z.infer<typeof filterNodeSchema>
export type FilterCondition = z.infer<typeof filterConditionSchema>
export type FilterGroup = z.infer<typeof filterGroupSchema>
export type AssetRuleOperation = z.infer<typeof assetRuleOperationSchema>
export type AssetRule = z.infer<typeof assetRuleSchema>
export type FilterPreview = z.infer<typeof filterPreviewSchema>
```

**Step 2: Create server functions**

Following the existing `security-profiles.functions.ts` pattern:

- `fetchAssetRules` — GET paged list
- `fetchAssetRule` — GET by id
- `createAssetRule` — POST create
- `updateAssetRule` — PUT update
- `deleteAssetRule` — DELETE
- `previewAssetRuleFilter` — POST preview
- `runAssetRules` — POST manual trigger
- `reorderAssetRules` — PUT reorder

**Step 3: Verify typecheck**

Run: `cd frontend && npm run typecheck`

**Step 4: Commit**

```bash
git add frontend/src/api/asset-rules.schemas.ts frontend/src/api/asset-rules.functions.ts
git commit -m "feat: add asset rules API schemas and server functions"
```

---

### Task 8: Frontend — Asset Rules List Page

**Files:**
- Create: `frontend/src/routes/_authed/admin/asset-rules/index.tsx`
- Modify: `frontend/src/routes/_authed/admin/index.tsx` (add card)

**Step 1: Add Asset Rules card to admin landing**

Add to the `adminAreas` array in `/admin/index.tsx`:

```typescript
{
  title: 'Asset Rules',
  description: 'Automate security profile and team assignment for assets based on filters.',
  to: '/admin/asset-rules',
  roles: ['GlobalAdmin', 'SecurityManager'],
  icon: GitBranchPlus, // or similar Lucide icon
},
```

**Step 2: Create list route**

Create `frontend/src/routes/_authed/admin/asset-rules/index.tsx`:

- Route validates search: `page`, `pageSize`
- Loader fetches `fetchAssetRules`
- Table columns: Priority (#), Name, Description, Enabled (toggle), Last Run, Matched Assets, Actions (edit/delete)
- Enable/disable toggle calls `updateAssetRule` mutation inline
- "Run all rules" button calls `runAssetRules`
- "Create rule" button navigates to `/admin/asset-rules/new`
- Edit button navigates to `/admin/asset-rules/$id`
- Delete button with confirmation dialog

**Step 3: Verify typecheck + lint**

Run: `npm run typecheck && npm run lint`

**Step 4: Commit**

```bash
git add frontend/src/routes/_authed/admin/asset-rules/ frontend/src/routes/_authed/admin/index.tsx
git commit -m "feat: add asset rules list page in admin area"
```

---

### Task 9: Frontend — Filter Builder Component

**Files:**
- Create: `frontend/src/components/features/admin/asset-rules/FilterBuilder.tsx`

**Step 1: Implement recursive filter builder**

Key components:

`FilterBuilder` — top-level component, manages the root `FilterGroup` state:
- Props: `value: FilterNode`, `onChange: (value: FilterNode) => void`
- Renders the root `FilterGroupEditor`

`FilterGroupEditor` — renders a group (AND/OR) with its children:
- AND/OR toggle button
- List of children (each either `FilterGroupEditor` or `FilterConditionEditor`)
- "Add condition" and "Add group" buttons
- "Remove group" button (not for root)
- Visual: bordered container with slight indent, connector lines

`FilterConditionEditor` — renders a single condition row:
- Field select: AssetType, Name, DeviceGroup, Vendor, Platform, Domain, Tag
- Operator select: Equals, StartsWith, Contains, EndsWith
  - For AssetType: only Equals
- Value input:
  - AssetType: select dropdown (Device, Software, CloudResource)
  - Tag, Name, DeviceGroup, etc.: text input
- Remove button
- Conditional field visibility: DeviceGroup/Platform/Domain only shown when an AssetType=Device constraint exists in an ancestor group. Vendor only when AssetType=Software.

Helper: `getAvailableFields(ancestors: FilterGroup[])` — checks if AssetType is constrained in the ancestor chain and returns appropriate field options.

**Step 2: Verify typecheck**

Run: `npm run typecheck`

**Step 3: Commit**

```bash
git add frontend/src/components/features/admin/asset-rules/FilterBuilder.tsx
git commit -m "feat: add recursive filter builder component for asset rules"
```

---

### Task 10: Frontend — Asset Rule Wizard (Create/Edit)

**Files:**
- Create: `frontend/src/routes/_authed/admin/asset-rules/new.tsx`
- Create: `frontend/src/routes/_authed/admin/asset-rules/$id.tsx`
- Create: `frontend/src/components/features/admin/asset-rules/AssetRuleWizard.tsx`

**Step 1: Create shared wizard component**

`AssetRuleWizard` — 4-step wizard following SetupWizard pattern:

Props:
- `mode: 'create' | 'edit'`
- `initialData?: AssetRule` (for edit mode)
- `onSave: (data) => Promise<void>`

State:
- `step: 0 | 1 | 2 | 3`
- `name: string`, `description: string`
- `filter: FilterNode` (default: empty AND group)
- `operations: AssetRuleOperation[]`

**Step 0 — Basic Info:**
- Name input (required)
- Description textarea (optional)
- Continue button (disabled if name empty)

**Step 1 — Filters:**
- `FilterBuilder` component with current filter state
- "Preview" button → calls `previewAssetRuleFilter` → shows count + 5 sample assets in a small table below
- Continue button

**Step 2 — Operations:**
- Checkboxes/cards for each operation type
- "Assign Security Profile" → select dropdown of security profiles (fetched via `fetchSecurityProfiles`)
- "Assign Team" → select dropdown of teams (fetched via `fetchTeams`)
- At least one operation required to continue
- Continue button

**Step 3 — Summary:**
- Read-only display of all settings:
  - Name + description
  - Filter tree rendered as readable text (e.g., "AssetType = Device AND (Name starts with 'SRV-' OR Tag = 'production')")
  - Operations listed with resolved names (profile name, team name)
- Save button

**Step 2: Create route for new**

`/admin/asset-rules/new.tsx`:
- Loader fetches security profiles + teams for the operation dropdowns
- Renders `AssetRuleWizard` in create mode
- On save: calls `createAssetRule`, then navigates to `/admin/asset-rules`

**Step 3: Create route for edit**

`/admin/asset-rules/$id.tsx`:
- Loader fetches the rule + security profiles + teams
- Renders `AssetRuleWizard` in edit mode with initial data
- On save: calls `updateAssetRule`, then navigates to `/admin/asset-rules`

**Step 4: Verify typecheck + lint**

Run: `npm run typecheck && npm run lint`

**Step 5: Commit**

```bash
git add frontend/src/routes/_authed/admin/asset-rules/ frontend/src/components/features/admin/asset-rules/
git commit -m "feat: add asset rule wizard for create and edit flows"
```

---

### Task 11: Backend Tests

**Files:**
- Create: `tests/PatchHound.Tests/Infrastructure/AssetRuleFilterBuilderTests.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/AssetRuleEvaluationServiceTests.cs`

**Step 1: Test filter builder**

Test cases:
- Simple equals condition on AssetType
- String operators (StartsWith, Contains, EndsWith) on Name
- Nested AND/OR groups
- Tag condition (with AssetTag subquery)
- Empty group returns all assets

**Step 2: Test evaluation service**

Test cases:
- First-match-wins: two rules, asset matches both, only first rule's operations apply
- Disabled rules are skipped
- Priority ordering is respected
- Preview returns correct count and samples

**Step 3: Run tests**

Run: `dotnet test PatchHound.slnx -v minimal`

**Step 4: Commit**

```bash
git add tests/PatchHound.Tests/Infrastructure/
git commit -m "test: add asset rule filter builder and evaluation service tests"
```

---

### Task 12: Frontend Tests

**Files:**
- Create: `frontend/src/components/features/admin/asset-rules/FilterBuilder.test.tsx`

**Step 1: Test filter builder component**

Test cases:
- Renders empty group with add condition button
- Adding a condition shows field/operator/value selects
- Changing AND/OR toggle updates group operator
- Adding nested group renders recursively
- Removing condition updates parent group

**Step 2: Run tests**

Run: `cd frontend && npm test`

**Step 3: Commit**

```bash
git add frontend/src/components/features/admin/asset-rules/FilterBuilder.test.tsx
git commit -m "test: add filter builder component tests"
```
