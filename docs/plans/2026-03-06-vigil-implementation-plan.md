# Vigil Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a self-hosted, multi-tenant vulnerability management platform that integrates with Microsoft Defender, supports role-based access, configurable SLAs, AI-generated reports, and full audit trails.

**Architecture:** .NET 10 monolith API + separate ingestion worker, both sharing a PostgreSQL database. Frontend is a Vite + TanStack Router/Query SPA with shadcn/ui. Docker Compose for self-hosting.

**Tech Stack:** .NET 10, EF Core, PostgreSQL, SignalR, Entra ID, Vite, React, TanStack Router/Query, Tailwind CSS, shadcn/ui, Vitest, Testcontainers

**Reference docs:**
- Design: `docs/plans/2026-03-06-vulnerability-management-design.md`
- Code standards: `docs/plans/2026-03-06-code-standards.md`

---

## Phase 1: Project Scaffolding

### Task 1: Initialize .NET solution and project structure

**Files:**
- Create: `src/Vigil.Api/Vigil.Api.csproj`
- Create: `src/Vigil.Worker/Vigil.Worker.csproj`
- Create: `src/Vigil.Core/Vigil.Core.csproj`
- Create: `src/Vigil.Infrastructure/Vigil.Infrastructure.csproj`
- Create: `tests/Vigil.Tests/Vigil.Tests.csproj`
- Create: `Vigil.sln`
- Create: `.gitignore`
- Create: `Directory.Build.props`

**Step 1: Create the solution and projects**

```bash
dotnet new sln -n Vigil
dotnet new webapi -n Vigil.Api -o src/Vigil.Api --no-openapi false
dotnet new worker -n Vigil.Worker -o src/Vigil.Worker
dotnet new classlib -n Vigil.Core -o src/Vigil.Core
dotnet new classlib -n Vigil.Infrastructure -o src/Vigil.Infrastructure
dotnet new xunit -n Vigil.Tests -o tests/Vigil.Tests
```

**Step 2: Add projects to solution and set up references**

```bash
dotnet sln add src/Vigil.Api src/Vigil.Worker src/Vigil.Core src/Vigil.Infrastructure tests/Vigil.Tests
dotnet add src/Vigil.Api reference src/Vigil.Core src/Vigil.Infrastructure
dotnet add src/Vigil.Worker reference src/Vigil.Core src/Vigil.Infrastructure
dotnet add src/Vigil.Infrastructure reference src/Vigil.Core
dotnet add tests/Vigil.Tests reference src/Vigil.Core src/Vigil.Infrastructure src/Vigil.Api
```

**Step 3: Create `Directory.Build.props` for shared settings**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

**Step 4: Create `.gitignore`**

Include: `bin/`, `obj/`, `.vs/`, `.idea/`, `*.user`, `*.env`, `node_modules/`, `dist/`, `*.DotSettings.user`

**Step 5: Verify build**

Run: `dotnet build Vigil.sln`
Expected: Build succeeded with 0 errors.

**Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold .NET solution with project structure"
```

---

### Task 2: Add core NuGet packages

**Files:**
- Modify: `src/Vigil.Api/Vigil.Api.csproj`
- Modify: `src/Vigil.Worker/Vigil.Worker.csproj`
- Modify: `src/Vigil.Core/Vigil.Core.csproj`
- Modify: `src/Vigil.Infrastructure/Vigil.Infrastructure.csproj`
- Modify: `tests/Vigil.Tests/Vigil.Tests.csproj`

**Step 1: Add packages to each project**

```bash
# Core - no external dependencies except FluentValidation abstractions
dotnet add src/Vigil.Core package FluentValidation

# Infrastructure
dotnet add src/Vigil.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Vigil.Infrastructure package Microsoft.EntityFrameworkCore.Design

# API
dotnet add src/Vigil.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Vigil.Api package Microsoft.Identity.Web
dotnet add src/Vigil.Api package Microsoft.AspNetCore.SignalR
dotnet add src/Vigil.Api package Swashbuckle.AspNetCore
dotnet add src/Vigil.Api package FluentValidation.AspNetCore

# Worker
dotnet add src/Vigil.Worker package Microsoft.Identity.Web

# Tests
dotnet add tests/Vigil.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Vigil.Tests package Testcontainers.PostgreSql
dotnet add tests/Vigil.Tests package FluentAssertions
dotnet add tests/Vigil.Tests package NSubstitute
dotnet add tests/Vigil.Tests package Microsoft.EntityFrameworkCore.InMemory
```

**Step 2: Verify build**

Run: `dotnet build Vigil.sln`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add -A
git commit -m "chore: add core NuGet packages"
```

---

## Phase 2: Domain Model (Vigil.Core)

### Task 3: Create enums and value objects

**Files:**
- Create: `src/Vigil.Core/Enums/Severity.cs`
- Create: `src/Vigil.Core/Enums/VulnerabilityStatus.cs`
- Create: `src/Vigil.Core/Enums/TaskStatus.cs`
- Create: `src/Vigil.Core/Enums/AssetType.cs`
- Create: `src/Vigil.Core/Enums/OwnerType.cs`
- Create: `src/Vigil.Core/Enums/Criticality.cs`
- Create: `src/Vigil.Core/Enums/RiskAcceptanceStatus.cs`
- Create: `src/Vigil.Core/Enums/CampaignStatus.cs`
- Create: `src/Vigil.Core/Enums/AuditAction.cs`
- Create: `src/Vigil.Core/Enums/NotificationType.cs`
- Create: `src/Vigil.Core/Enums/RoleName.cs`

**Step 1: Create all enum files**

Each enum should be in namespace `Vigil.Core.Enums`. Example:

```csharp
namespace Vigil.Core.Enums;

public enum Severity
{
    Low,
    Medium,
    High,
    Critical
}
```

Create all enums per the design doc:
- `Severity`: Low, Medium, High, Critical
- `VulnerabilityStatus`: Open, InRemediation, Resolved, RiskAccepted
- `RemediationTaskStatus`: Pending, InProgress, PatchScheduled, CannotPatch, Completed, RiskAccepted
- `AssetType`: Device, Software, CloudResource
- `OwnerType`: User, Team
- `Criticality`: Low, Medium, High, Critical
- `RiskAcceptanceStatus`: Pending, Approved, Rejected, Expired
- `CampaignStatus`: Active, Closed
- `AuditAction`: Created, Updated, Deleted
- `NotificationType`: TaskAssigned, SLAWarning, NewCriticalVuln, RiskAcceptanceRequired, RiskAcceptanceDecision, TaskStatusChanged
- `RoleName`: GlobalAdmin, SecurityManager, SecurityAnalyst, AssetOwner, Stakeholder, Auditor

**Step 2: Verify build**

Run: `dotnet build src/Vigil.Core`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add domain enums"
```

---

### Task 4: Create domain entities

**Files:**
- Create: `src/Vigil.Core/Entities/Tenant.cs`
- Create: `src/Vigil.Core/Entities/User.cs`
- Create: `src/Vigil.Core/Entities/UserTenantRole.cs`
- Create: `src/Vigil.Core/Entities/Team.cs`
- Create: `src/Vigil.Core/Entities/TeamMember.cs`
- Create: `src/Vigil.Core/Entities/Asset.cs`
- Create: `src/Vigil.Core/Entities/Vulnerability.cs`
- Create: `src/Vigil.Core/Entities/VulnerabilityAsset.cs`
- Create: `src/Vigil.Core/Entities/OrganizationalSeverity.cs`
- Create: `src/Vigil.Core/Entities/RemediationTask.cs`
- Create: `src/Vigil.Core/Entities/Campaign.cs`
- Create: `src/Vigil.Core/Entities/CampaignVulnerability.cs`
- Create: `src/Vigil.Core/Entities/Comment.cs`
- Create: `src/Vigil.Core/Entities/RiskAcceptance.cs`
- Create: `src/Vigil.Core/Entities/AuditLogEntry.cs`
- Create: `src/Vigil.Core/Entities/Notification.cs`
- Create: `src/Vigil.Core/Entities/AIReport.cs`

**Step 1: Create entity classes**

All entities follow the code standards: no public setters on domain entities, use methods to enforce invariants. Use `Guid` for all IDs. Each entity with a TenantId must include it for query filtering.

Example pattern for `Tenant.cs`:

```csharp
namespace Vigil.Core.Entities;

public class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string EntraTenantId { get; private set; } = string.Empty;
    public string Settings { get; private set; } = "{}"; // JSON

    private Tenant() { } // EF Core

    public static Tenant Create(string name, string entraTenantId)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            EntraTenantId = entraTenantId
        };
    }

    public void UpdateSettings(string settingsJson)
    {
        Settings = settingsJson;
    }
}
```

Follow this pattern for all entities per the design doc data model. Key relationships:
- `User` has `IReadOnlyCollection<UserTenantRole> TenantRoles`
- `Team` has `IReadOnlyCollection<TeamMember> Members`
- `Asset` has `OwnerType`, nullable `OwnerUserId`/`OwnerTeamId`
- `Vulnerability` has `IReadOnlyCollection<VulnerabilityAsset> AffectedAssets`
- `RemediationTask` references `VulnerabilityId` and `AssetId`
- `Campaign` has many-to-many with Vulnerability via `CampaignVulnerability`
- `Comment` uses polymorphic pattern: `EntityType` (string) + `EntityId` (Guid)

**Step 2: Verify build**

Run: `dotnet build src/Vigil.Core`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add domain entities"
```

---

### Task 5: Create core interfaces (repositories and services)

**Files:**
- Create: `src/Vigil.Core/Interfaces/IRepository.cs` (generic)
- Create: `src/Vigil.Core/Interfaces/IVulnerabilityRepository.cs`
- Create: `src/Vigil.Core/Interfaces/IAssetRepository.cs`
- Create: `src/Vigil.Core/Interfaces/IRemediationTaskRepository.cs`
- Create: `src/Vigil.Core/Interfaces/ICampaignRepository.cs`
- Create: `src/Vigil.Core/Interfaces/IRiskAcceptanceRepository.cs`
- Create: `src/Vigil.Core/Interfaces/IAuditLogRepository.cs`
- Create: `src/Vigil.Core/Interfaces/IVulnerabilitySource.cs`
- Create: `src/Vigil.Core/Interfaces/IAiReportProvider.cs`
- Create: `src/Vigil.Core/Interfaces/INotificationService.cs`
- Create: `src/Vigil.Core/Interfaces/ITenantContext.cs`
- Create: `src/Vigil.Core/Interfaces/IUnitOfWork.cs`

**Step 1: Create interfaces**

`IVulnerabilitySource` is the key vendor abstraction:

```csharp
namespace Vigil.Core.Interfaces;

public interface IVulnerabilitySource
{
    string SourceName { get; }
    Task<IReadOnlyList<IngestionResult>> FetchVulnerabilitiesAsync(
        Guid tenantId, CancellationToken cancellationToken);
}
```

`IAiReportProvider` is the pluggable AI abstraction:

```csharp
namespace Vigil.Core.Interfaces;

public interface IAiReportProvider
{
    string ProviderName { get; }
    Task<string> GenerateReportAsync(
        Vulnerability vulnerability,
        IReadOnlyList<Asset> affectedAssets,
        CancellationToken cancellationToken);
}
```

`ITenantContext` provides current user's tenant access:

```csharp
namespace Vigil.Core.Interfaces;

public interface ITenantContext
{
    Guid? CurrentTenantId { get; }
    IReadOnlyList<Guid> AccessibleTenantIds { get; }
    Guid CurrentUserId { get; }
}
```

**Step 2: Verify build**

Run: `dotnet build src/Vigil.Core`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add core interfaces"
```

---

### Task 6: Create the Result pattern

**Files:**
- Create: `src/Vigil.Core/Common/Result.cs`
- Create: `tests/Vigil.Tests/Core/ResultTests.cs`

**Step 1: Write tests for Result type**

```csharp
namespace Vigil.Tests.Core;

public class ResultTests
{
    [Fact]
    public void Success_ContainsValue()
    {
        var result = Result<int>.Success(42);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_ContainsError()
    {
        var result = Result<int>.Failure("not found");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("not found");
    }

    [Fact]
    public void AccessingValue_OnFailure_Throws()
    {
        var result = Result<int>.Failure("error");
        var act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>();
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Vigil.Tests --filter "ResultTests" -v minimal`
Expected: FAIL — `Result<T>` does not exist.

**Step 3: Implement Result type**

```csharp
namespace Vigil.Core.Common;

public class Result<T>
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    private readonly T? _value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed result.");

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
    }

    private Result(string error)
    {
        IsSuccess = false;
        Error = error;
    }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(string error) => new(error);
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Vigil.Tests --filter "ResultTests" -v minimal`
Expected: 3 tests passed.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add Result<T> pattern"
```

---

## Phase 3: Database & Infrastructure

### Task 7: Set up EF Core DbContext with tenant filtering

**Files:**
- Create: `src/Vigil.Infrastructure/Data/VigilDbContext.cs`
- Create: `src/Vigil.Infrastructure/Data/Configurations/TenantConfiguration.cs`
- Create: `src/Vigil.Infrastructure/Data/Configurations/UserConfiguration.cs`
- Create: `src/Vigil.Infrastructure/Data/Configurations/VulnerabilityConfiguration.cs`
- Create: `src/Vigil.Infrastructure/Data/Configurations/AssetConfiguration.cs`
- Create: `src/Vigil.Infrastructure/Data/Configurations/RemediationTaskConfiguration.cs`
- (one configuration file per entity)

**Step 1: Create VigilDbContext**

```csharp
namespace Vigil.Infrastructure.Data;

public class VigilDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public VigilDbContext(DbContextOptions<VigilDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    // ... all entity DbSets

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VigilDbContext).Assembly);

        // Global query filters for tenant isolation
        modelBuilder.Entity<Vulnerability>()
            .HasQueryFilter(v => _tenantContext.AccessibleTenantIds.Contains(v.TenantId));
        modelBuilder.Entity<Asset>()
            .HasQueryFilter(a => _tenantContext.AccessibleTenantIds.Contains(a.TenantId));
        // ... repeat for all tenant-scoped entities
    }
}
```

**Step 2: Create entity configurations using `IEntityTypeConfiguration<T>`**

Use Fluent API for all relationships, indexes, and constraints. Example:

```csharp
namespace Vigil.Infrastructure.Data.Configurations;

public class VulnerabilityConfiguration : IEntityTypeConfiguration<Vulnerability>
{
    public void Configure(EntityTypeBuilder<Vulnerability> builder)
    {
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => v.ExternalId);
        builder.HasIndex(v => v.TenantId);
        builder.Property(v => v.VendorSeverity).HasConversion<string>();
        builder.Property(v => v.Status).HasConversion<string>();
    }
}
```

**Step 3: Verify build**

Run: `dotnet build src/Vigil.Infrastructure`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add EF Core DbContext with tenant filtering"
```

---

### Task 8: Create audit log interceptor

**Files:**
- Create: `src/Vigil.Infrastructure/Data/AuditSaveChangesInterceptor.cs`
- Create: `tests/Vigil.Tests/Infrastructure/AuditInterceptorTests.cs`

**Step 1: Write test for audit interceptor**

```csharp
[Fact]
public async Task SavingChanges_CreatesAuditLogEntry_ForModifiedEntities()
{
    // Arrange: set up in-memory db with interceptor
    // Act: modify a Vulnerability entity and save
    // Assert: AuditLogEntry created with correct EntityType, Action, OldValues, NewValues
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/Vigil.Tests --filter "AuditInterceptorTests" -v minimal`
Expected: FAIL

**Step 3: Implement `AuditSaveChangesInterceptor`**

The interceptor extends `SaveChangesInterceptor` and overrides `SavingChangesAsync`. It inspects `ChangeTracker.Entries()` for Added/Modified/Deleted entities, serializes old/new values to JSON, and creates `AuditLogEntry` records.

```csharp
namespace Vigil.Infrastructure.Data;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;

    public AuditSaveChangesInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not AuditLogEntry) // Don't audit the audit log
            .ToList();

        foreach (var entry in entries)
        {
            var auditEntry = AuditLogEntry.Create(
                tenantId: GetTenantId(entry),
                entityType: entry.Entity.GetType().Name,
                entityId: GetEntityId(entry),
                action: entry.State switch
                {
                    EntityState.Added => AuditAction.Created,
                    EntityState.Modified => AuditAction.Updated,
                    EntityState.Deleted => AuditAction.Deleted,
                    _ => throw new InvalidOperationException()
                },
                oldValues: entry.State == EntityState.Added ? null : SerializeOriginalValues(entry),
                newValues: entry.State == EntityState.Deleted ? null : SerializeCurrentValues(entry),
                userId: _tenantContext.CurrentUserId
            );

            context.Set<AuditLogEntry>().Add(auditEntry);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
    // ... helper methods
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Vigil.Tests --filter "AuditInterceptorTests" -v minimal`
Expected: PASS

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add audit log EF Core interceptor"
```

---

### Task 9: Create initial EF Core migration

**Files:**
- Create: `src/Vigil.Infrastructure/Data/Migrations/` (generated)

**Step 1: Create migration**

```bash
dotnet ef migrations add InitialCreate \
  --project src/Vigil.Infrastructure \
  --startup-project src/Vigil.Api \
  -- --connection "Host=localhost;Database=vigil_dev"
```

**Step 2: Review the generated migration**

Read the migration file and verify all tables, indexes, and relationships are correct.

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add initial database migration"
```

---

### Task 10: Create repository implementations

**Files:**
- Create: `src/Vigil.Infrastructure/Repositories/VulnerabilityRepository.cs`
- Create: `src/Vigil.Infrastructure/Repositories/AssetRepository.cs`
- Create: `src/Vigil.Infrastructure/Repositories/RemediationTaskRepository.cs`
- Create: `src/Vigil.Infrastructure/Repositories/CampaignRepository.cs`
- Create: `src/Vigil.Infrastructure/Repositories/RiskAcceptanceRepository.cs`
- Create: `src/Vigil.Infrastructure/Repositories/AuditLogRepository.cs`
- Create: `src/Vigil.Infrastructure/Repositories/UnitOfWork.cs`
- Create: `tests/Vigil.Tests/Infrastructure/VulnerabilityRepositoryTests.cs`

**Step 1: Write integration tests using Testcontainers**

Create a test base class that spins up a PostgreSQL container:

```csharp
public class DatabaseTestBase : IAsyncLifetime
{
    protected VigilDbContext DbContext = null!;
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder().Build();
        await _postgres.StartAsync();
        var options = new DbContextOptionsBuilder<VigilDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        DbContext = new VigilDbContext(options, new TestTenantContext());
        await DbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
```

Write tests for CRUD operations on vulnerabilities.

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Vigil.Tests --filter "VulnerabilityRepositoryTests" -v minimal`
Expected: FAIL

**Step 3: Implement repositories**

Each repository wraps `VigilDbContext`, uses `AsNoTracking()` for reads, and delegates saves to `UnitOfWork`.

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Vigil.Tests --filter "VulnerabilityRepositoryTests" -v minimal`
Expected: PASS

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add repository implementations with integration tests"
```

---

## Phase 4: Authentication & Authorization

### Task 11: Configure Entra ID authentication

**Files:**
- Modify: `src/Vigil.Api/Program.cs`
- Create: `src/Vigil.Api/Auth/TenantContext.cs`
- Create: `src/Vigil.Api/appsettings.json` (update)
- Create: `src/Vigil.Infrastructure/Services/UserSyncService.cs`

**Step 1: Configure multi-tenant JWT auth in `Program.cs`**

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
```

`appsettings.json` section:
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "common",
    "ClientId": "<from-env>",
    "Audience": "<from-env>"
  }
}
```

**Step 2: Implement `TenantContext`**

Extract tenant info from the authenticated user's claims. Look up `UserTenantRole` records to determine accessible tenants and roles.

```csharp
namespace Vigil.Api.Auth;

public class TenantContext : ITenantContext
{
    // Populated from HttpContext claims + DB lookup of UserTenantRole
}
```

Register as scoped service.

**Step 3: Verify build**

Run: `dotnet build src/Vigil.Api`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: configure Entra ID multi-tenant authentication"
```

---

### Task 12: Implement policy-based authorization

**Files:**
- Create: `src/Vigil.Api/Auth/Policies.cs`
- Create: `src/Vigil.Api/Auth/RoleRequirement.cs`
- Create: `src/Vigil.Api/Auth/RoleRequirementHandler.cs`
- Create: `tests/Vigil.Tests/Auth/AuthorizationTests.cs`

**Step 1: Write tests for authorization policies**

```csharp
[Fact]
public async Task SecurityAnalyst_CanAdjustSeverity()
{
    // Arrange: user with SecurityAnalyst role for tenant
    // Act: evaluate "CanAdjustSeverity" policy
    // Assert: succeeds
}

[Fact]
public async Task AssetOwner_CannotAdjustSeverity()
{
    // Arrange: user with AssetOwner role
    // Act: evaluate "CanAdjustSeverity" policy
    // Assert: fails
}
```

**Step 2: Run tests to verify they fail**

**Step 3: Implement policies**

Define all policies from the permission matrix in the design doc:

```csharp
namespace Vigil.Api.Auth;

public static class Policies
{
    public const string ViewVulnerabilities = nameof(ViewVulnerabilities);
    public const string ModifyVulnerabilities = nameof(ModifyVulnerabilities);
    public const string AdjustSeverity = nameof(AdjustSeverity);
    public const string AssignTasks = nameof(AssignTasks);
    public const string UpdateTaskStatus = nameof(UpdateTaskStatus);
    public const string RequestRiskAcceptance = nameof(RequestRiskAcceptance);
    public const string ApproveRiskAcceptance = nameof(ApproveRiskAcceptance);
    public const string ManageCampaigns = nameof(ManageCampaigns);
    public const string ViewAuditLogs = nameof(ViewAuditLogs);
    public const string ManageUsers = nameof(ManageUsers);
    public const string ConfigureTenant = nameof(ConfigureTenant);
    public const string GenerateAiReports = nameof(GenerateAiReports);
    public const string AddComments = nameof(AddComments);
    public const string ManageTeams = nameof(ManageTeams);
}
```

Register policies in `Program.cs`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.ModifyVulnerabilities, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst)));
    // ... all other policies
});
```

**Step 4: Run tests to verify they pass**

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add policy-based authorization"
```

---

## Phase 5: API Endpoints

### Task 13: Set up API infrastructure (error handling, pagination, CORS)

**Files:**
- Create: `src/Vigil.Api/Middleware/ExceptionHandlerMiddleware.cs`
- Create: `src/Vigil.Api/Models/PagedResponse.cs`
- Create: `src/Vigil.Api/Models/PaginationQuery.cs`
- Modify: `src/Vigil.Api/Program.cs`

**Step 1: Create `PagedResponse<T>` record**

```csharp
namespace Vigil.Api.Models;

public record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount);
```

**Step 2: Create `PaginationQuery`**

```csharp
namespace Vigil.Api.Models;

public record PaginationQuery(int Page = 1, int PageSize = 25)
{
    public int Skip => (Page - 1) * PageSize;
}
```

**Step 3: Implement global exception handler middleware**

Returns RFC 9457 Problem Details. Logs errors. Never leaks stack traces in production.

**Step 4: Configure CORS, rate limiting in `Program.cs`**

```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(builder.Configuration["Frontend:Origin"]!)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()); // Required for SignalR
});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
});
```

**Step 5: Verify build**

Run: `dotnet build src/Vigil.Api`

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add API error handling, pagination, CORS, rate limiting"
```

---

### Task 14: Vulnerability endpoints

**Files:**
- Create: `src/Vigil.Api/Controllers/VulnerabilitiesController.cs`
- Create: `src/Vigil.Core/Services/VulnerabilityService.cs`
- Create: `src/Vigil.Api/Models/Vulnerabilities/VulnerabilityDto.cs`
- Create: `src/Vigil.Api/Models/Vulnerabilities/UpdateOrgSeverityRequest.cs`
- Create: `tests/Vigil.Tests/Api/VulnerabilitiesControllerTests.cs`

**Step 1: Write integration tests**

Test all endpoints: GET list (with filters), GET detail, PUT organizational severity, GET/POST comments, GET audit log.

**Step 2: Run tests to verify they fail**

**Step 3: Create DTOs**

Record types for all request/response shapes.

**Step 4: Create VulnerabilityService in Core**

Business logic: validate severity adjustments, enforce permissions (delegated to controller), compute organizational severity.

**Step 5: Create VulnerabilitiesController**

```csharp
[ApiController]
[Route("api/vulnerabilities")]
[Authorize]
public class VulnerabilitiesController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<VulnerabilityDto>>> List(
        [FromQuery] VulnerabilityFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken cancellationToken) { ... }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<VulnerabilityDetailDto>> Get(
        Guid id, CancellationToken cancellationToken) { ... }

    [HttpPut("{id:guid}/organizational-severity")]
    [Authorize(Policy = Policies.AdjustSeverity)]
    public async Task<IActionResult> UpdateOrganizationalSeverity(
        Guid id,
        UpdateOrgSeverityRequest request,
        CancellationToken cancellationToken) { ... }
}
```

**Step 6: Run tests to verify they pass**

**Step 7: Commit**

```bash
git add -A
git commit -m "feat: add vulnerability API endpoints"
```

---

### Task 15: Remediation task endpoints

**Files:**
- Create: `src/Vigil.Api/Controllers/TasksController.cs`
- Create: `src/Vigil.Core/Services/RemediationTaskService.cs`
- Create: `src/Vigil.Api/Models/Tasks/RemediationTaskDto.cs`
- Create: `src/Vigil.Api/Models/Tasks/UpdateTaskStatusRequest.cs`
- Create: `tests/Vigil.Tests/Api/TasksControllerTests.cs`
- Create: `tests/Vigil.Tests/Core/RemediationTaskServiceTests.cs`

**Step 1: Write unit tests for task status transitions**

Test valid transitions (Pending→InProgress, InProgress→PatchScheduled, etc.) and invalid ones. Test that justification is required for CannotPatch and RiskAccepted.

**Step 2: Run tests to verify they fail**

**Step 3: Implement `RemediationTaskService`**

Enforce state machine transitions. Validate justification requirements. Calculate SLA dates.

**Step 4: Run tests to verify they pass**

**Step 5: Write integration tests for TasksController**

**Step 6: Implement TasksController**

Endpoints: GET list (auto-filtered for Asset Owners), GET detail, PUT status, POST risk-acceptance.

**Step 7: Run all tests**

**Step 8: Commit**

```bash
git add -A
git commit -m "feat: add remediation task endpoints with state machine"
```

---

### Task 16: Asset endpoints

**Files:**
- Create: `src/Vigil.Api/Controllers/AssetsController.cs`
- Create: `src/Vigil.Core/Services/AssetService.cs`
- Create: `src/Vigil.Api/Models/Assets/AssetDto.cs`
- Create: `src/Vigil.Api/Models/Assets/AssignOwnerRequest.cs`
- Create: `src/Vigil.Api/Models/Assets/BulkAssignRequest.cs`
- Create: `tests/Vigil.Tests/Api/AssetsControllerTests.cs`

Follow same pattern: tests first → implement service → implement controller → verify tests pass → commit.

Endpoints: GET list, GET detail, PUT owner, PUT criticality, POST bulk-assign.

**Commit message:** `feat: add asset management endpoints`

---

### Task 17: Campaign endpoints

**Files:**
- Create: `src/Vigil.Api/Controllers/CampaignsController.cs`
- Create: `src/Vigil.Core/Services/CampaignService.cs`
- Create: `src/Vigil.Api/Models/Campaigns/CampaignDto.cs`
- Create: `tests/Vigil.Tests/Api/CampaignsControllerTests.cs`

Endpoints: GET list, POST create, PUT update, POST link vulnerabilities, POST bulk-assign.

**Commit message:** `feat: add campaign endpoints`

---

### Task 18: Risk acceptance endpoints

**Files:**
- Create: `src/Vigil.Api/Controllers/RiskAcceptancesController.cs`
- Create: `src/Vigil.Core/Services/RiskAcceptanceService.cs`
- Create: `tests/Vigil.Tests/Core/RiskAcceptanceServiceTests.cs`

**Step 1: Write unit tests for risk acceptance workflow**

Test: request creates Pending record, approve transitions to Approved, reject transitions to Rejected, expired records reopen tasks.

**Step 2–6:** TDD cycle as before.

**Commit message:** `feat: add risk acceptance workflow endpoints`

---

### Task 19: Dashboard endpoints

**Files:**
- Create: `src/Vigil.Api/Controllers/DashboardController.cs`
- Create: `src/Vigil.Core/Services/DashboardService.cs`
- Create: `src/Vigil.Api/Models/Dashboard/DashboardSummaryDto.cs`
- Create: `src/Vigil.Api/Models/Dashboard/TrendDataDto.cs`
- Create: `tests/Vigil.Tests/Core/DashboardServiceTests.cs`

Summary endpoint returns: exposure score, vulnerability counts by severity/status, SLA compliance %, overdue count, top critical vulnerabilities.

Trends endpoint returns: time-series data for charts (vulnerability count by date, grouped by severity).

**Commit message:** `feat: add dashboard endpoints`

---

### Task 20: Admin endpoints (users, teams, tenants)

**Files:**
- Create: `src/Vigil.Api/Controllers/UsersController.cs`
- Create: `src/Vigil.Api/Controllers/TeamsController.cs`
- Create: `src/Vigil.Api/Controllers/TenantsController.cs`
- Create: `src/Vigil.Core/Services/UserService.cs`
- Create: `src/Vigil.Core/Services/TeamService.cs`
- Create: `tests/Vigil.Tests/Api/UsersControllerTests.cs`
- Create: `tests/Vigil.Tests/Api/TeamsControllerTests.cs`

Users: GET list, POST invite, PUT roles.
Teams: GET list, POST create, PUT members.
Tenants: GET list, PUT settings (Global Admin only).

**Commit message:** `feat: add admin endpoints for users, teams, tenants`

---

### Task 21: Audit log endpoint

**Files:**
- Create: `src/Vigil.Api/Controllers/AuditLogController.cs`
- Create: `src/Vigil.Api/Models/Audit/AuditLogDto.cs`
- Create: `tests/Vigil.Tests/Api/AuditLogControllerTests.cs`

GET endpoint with filters: entity type, action, user, date range, tenant. Read-only. Policy: `ViewAuditLogs`.

**Commit message:** `feat: add audit log endpoint`

---

### Task 22: Comments endpoints

**Files:**
- Create: `src/Vigil.Api/Controllers/CommentsController.cs`
- Modify: `src/Vigil.Api/Controllers/VulnerabilitiesController.cs` (if nested route)
- Create: `src/Vigil.Core/Services/CommentService.cs`
- Create: `tests/Vigil.Tests/Api/CommentsTests.cs`

Comments are nested under vulnerabilities, tasks, and campaigns:
- `GET /api/vulnerabilities/{id}/comments`
- `POST /api/vulnerabilities/{id}/comments`
- Same pattern for tasks and campaigns.

**Commit message:** `feat: add comments endpoints`

---

## Phase 6: SignalR Real-Time

### Task 23: Set up SignalR hub

**Files:**
- Create: `src/Vigil.Api/Hubs/NotificationHub.cs`
- Create: `src/Vigil.Core/Interfaces/IRealTimeNotifier.cs`
- Create: `src/Vigil.Infrastructure/Services/SignalRNotifier.cs`
- Modify: `src/Vigil.Api/Program.cs`

**Step 1: Create `IRealTimeNotifier` interface in Core**

```csharp
namespace Vigil.Core.Interfaces;

public interface IRealTimeNotifier
{
    Task NotifyNewVulnerabilityAsync(Guid tenantId, VulnerabilityDto vulnerability, CancellationToken ct);
    Task NotifyTaskAssignedAsync(Guid userId, RemediationTaskDto task, CancellationToken ct);
    Task NotifyTaskStatusChangedAsync(Guid tenantId, RemediationTaskDto task, CancellationToken ct);
    Task NotifySlaWarningAsync(Guid userId, RemediationTaskDto task, CancellationToken ct);
}
```

**Step 2: Create `NotificationHub`**

```csharp
[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Add user to groups based on their accessible tenant IDs
        var tenantIds = Context.User?.FindAll("tenant_ids");
        // Add to tenant groups for broadcast
    }
}
```

**Step 3: Implement `SignalRNotifier`**

Uses `IHubContext<NotificationHub>` to push events to tenant groups or specific users.

**Step 4: Register in `Program.cs`**

```csharp
builder.Services.AddSignalR();
app.MapHub<NotificationHub>("/hubs/notifications");
```

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add SignalR notification hub"
```

---

## Phase 7: Email Notifications

### Task 24: Implement email notification service

**Files:**
- Create: `src/Vigil.Infrastructure/Services/EmailNotificationService.cs`
- Create: `src/Vigil.Core/Interfaces/IEmailSender.cs`
- Create: `src/Vigil.Infrastructure/Services/SmtpEmailSender.cs`
- Create: `src/Vigil.Infrastructure/Options/SmtpOptions.cs`
- Create: `tests/Vigil.Tests/Infrastructure/EmailNotificationServiceTests.cs`

**Step 1: Write tests**

Test that notifications are created in the database and emails are dispatched. Mock `IEmailSender` for unit tests.

Test that team-owned asset notifications are sent to all team members.

**Step 2: Implement**

`EmailNotificationService` implements `INotificationService`. It creates `Notification` records in the DB and sends emails via `IEmailSender`.

`SmtpEmailSender` uses `System.Net.Mail.SmtpClient` configured via `IOptions<SmtpOptions>`.

**Step 3: Run tests, verify pass**

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add email notification service"
```

---

## Phase 8: AI Report Generation

### Task 25: Implement pluggable AI report provider

**Files:**
- Create: `src/Vigil.Core/Services/AiReportService.cs`
- Create: `src/Vigil.Infrastructure/AiProviders/AzureOpenAiProvider.cs`
- Create: `src/Vigil.Infrastructure/AiProviders/AnthropicProvider.cs`
- Create: `src/Vigil.Infrastructure/Options/AiProviderOptions.cs`
- Create: `tests/Vigil.Tests/Core/AiReportServiceTests.cs`

**Step 1: Write tests for AiReportService**

Test provider selection based on tenant config. Test report storage. Mock the provider interface.

**Step 2: Implement `AiReportService`**

```csharp
namespace Vigil.Core.Services;

public class AiReportService
{
    private readonly IEnumerable<IAiReportProvider> _providers;
    private readonly IRepository<AIReport> _reportRepository;

    public async Task<Result<AIReport>> GenerateReportAsync(
        Guid vulnerabilityId,
        string providerName,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderName == providerName);
        if (provider is null)
            return Result<AIReport>.Failure($"Unknown AI provider: {providerName}");

        // Fetch vulnerability + affected assets
        // Call provider.GenerateReportAsync(...)
        // Save AIReport to DB
        // Return result
    }
}
```

**Step 3: Implement provider stubs**

Azure OpenAI and Anthropic providers implement `IAiReportProvider`. Each calls its respective API to generate a markdown report.

**Step 4: Run tests, verify pass**

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add pluggable AI report generation"
```

---

## Phase 9: Ingestion Worker

### Task 26: Create ingestion pipeline

**Files:**
- Create: `src/Vigil.Worker/IngestionWorker.cs`
- Create: `src/Vigil.Core/Services/IngestionService.cs`
- Create: `src/Vigil.Core/Models/IngestionResult.cs`
- Create: `tests/Vigil.Tests/Core/IngestionServiceTests.cs`

**Step 1: Write tests for IngestionService**

```csharp
[Fact]
public async Task NewVulnerability_CreatesTasksForAffectedAssets()
{
    // Arrange: mock IVulnerabilitySource returns a new vulnerability affecting 3 assets
    // Act: run ingestion
    // Assert: 3 RemediationTasks created, assigned to correct owners
}

[Fact]
public async Task ResolvedVulnerability_ClosesRelatedTasks()
{
    // Arrange: existing vulnerability with open tasks, source reports it resolved
    // Act: run ingestion
    // Assert: tasks auto-closed, VulnerabilityAsset status updated
}

[Fact]
public async Task NoOwner_UseFallbackChain()
{
    // Arrange: asset with no owner, fallback team configured
    // Act: run ingestion with new vulnerability
    // Assert: task assigned to fallback team lead/member
}
```

**Step 2: Run tests to verify they fail**

**Step 3: Implement `IngestionService`**

Orchestrates: fetch → normalize → upsert → create tasks → notify. Uses `IUnitOfWork` for atomic operations.

**Step 4: Implement `IngestionWorker` (background service)**

```csharp
public class IngestionWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // For each tenant, run ingestion
            await _ingestionService.RunAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
```

**Step 5: Run tests to verify they pass**

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add ingestion worker and pipeline"
```

---

### Task 27: Implement Microsoft Defender vulnerability source

**Files:**
- Create: `src/Vigil.Infrastructure/VulnerabilitySources/DefenderVulnerabilitySource.cs`
- Create: `src/Vigil.Infrastructure/VulnerabilitySources/DefenderApiClient.cs`
- Create: `src/Vigil.Infrastructure/Options/DefenderOptions.cs`
- Create: `tests/Vigil.Tests/Infrastructure/DefenderVulnerabilitySourceTests.cs`

**Step 1: Write tests**

Mock the Defender API responses. Test normalization from Defender's data format to internal `IngestionResult` model.

**Step 2: Implement**

`DefenderVulnerabilitySource` implements `IVulnerabilitySource`. Uses Microsoft Graph API / Defender for Endpoint API to fetch vulnerabilities and affected machines.

**Step 3: Run tests, verify pass**

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add Microsoft Defender vulnerability source"
```

---

### Task 28: SLA deadline calculation and warning service

**Files:**
- Create: `src/Vigil.Core/Services/SlaService.cs`
- Create: `src/Vigil.Worker/SlaCheckWorker.cs`
- Create: `tests/Vigil.Tests/Core/SlaServiceTests.cs`

**Step 1: Write tests**

```csharp
[Fact]
public void CalculateDueDate_CriticalSeverity_Uses7DaySla()
{
    // Arrange: tenant with SLA config Critical=7 days
    // Act: calculate due date
    // Assert: due date is 7 days from now
}

[Fact]
public async Task SlaCheck_OverdueTask_SendsWarning()
{
    // Arrange: task past due date
    // Act: run SLA check
    // Assert: SLAWarning notification sent
}
```

**Step 2: Implement `SlaService`**

Reads tenant SLA settings. Calculates due dates. Checks for tasks at warning thresholds (75%, 100%, overdue).

**Step 3: Implement `SlaCheckWorker`**

Runs periodically (e.g., every hour). Calls `SlaService` to find tasks needing warnings.

**Step 4: Run tests, verify pass**

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add SLA calculation and warning service"
```

---

## Phase 10: OpenAPI & DI Registration

### Task 29: Wire up dependency injection and OpenAPI

**Files:**
- Modify: `src/Vigil.Api/Program.cs`
- Create: `src/Vigil.Infrastructure/DependencyInjection.cs`
- Create: `src/Vigil.Api/Program.cs` (finalize)

**Step 1: Create `DependencyInjection.cs` extension method in Infrastructure**

```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddVigilInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<VigilDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Vigil")));

        services.AddScoped<IVulnerabilityRepository, VulnerabilityRepository>();
        // ... all repository registrations
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<INotificationService, EmailNotificationService>();
        services.AddScoped<IRealTimeNotifier, SignalRNotifier>();

        // AI providers
        services.AddScoped<IAiReportProvider, AzureOpenAiProvider>();
        services.AddScoped<IAiReportProvider, AnthropicProvider>();

        // Vulnerability sources
        services.AddScoped<IVulnerabilitySource, DefenderVulnerabilitySource>();

        return services;
    }
}
```

**Step 2: Configure OpenAPI/Swagger**

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Vigil API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { ... });
});
```

**Step 3: Verify full API starts**

Run: `dotnet run --project src/Vigil.Api`
Expected: API starts, Swagger UI available at `/swagger`.

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: wire up dependency injection and OpenAPI"
```

---

## Phase 11: Frontend

### Task 30: Scaffold frontend project

**Files:**
- Create: `frontend/package.json`
- Create: `frontend/vite.config.ts`
- Create: `frontend/tsconfig.json`
- Create: `frontend/tailwind.config.ts`
- Create: `frontend/postcss.config.js`
- Create: `frontend/index.html`
- Create: `frontend/src/main.tsx`
- Create: `frontend/src/App.tsx`

**Step 1: Create Vite + React project**

```bash
cd frontend
npm create vite@latest . -- --template react-ts
```

**Step 2: Install dependencies**

```bash
npm install @tanstack/react-router @tanstack/react-query
npm install tailwindcss @tailwindcss/vite
npm install @microsoft/signalr
npm install zod
npm install -D @tanstack/router-plugin
npm install -D vitest @testing-library/react @testing-library/jest-dom jsdom msw
```

**Step 3: Initialize shadcn/ui**

```bash
npx shadcn@latest init
```

**Step 4: Configure Tailwind, TanStack Router plugin in `vite.config.ts`**

**Step 5: Verify dev server starts**

Run: `npm run dev`
Expected: Vite dev server starts at `http://localhost:5173`.

**Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold frontend with Vite, TanStack, Tailwind, shadcn"
```

---

### Task 31: Set up API client and auth

**Files:**
- Create: `frontend/src/lib/api-client.ts`
- Create: `frontend/src/lib/auth.ts`
- Create: `frontend/src/api/queryClient.ts`
- Create: `frontend/src/types/api.ts`

**Step 1: Create API client**

```typescript
const API_BASE = import.meta.env.VITE_API_URL ?? '/api';

async function fetchWithAuth<T>(path: string, options?: RequestInit): Promise<T> {
  const token = await getAccessToken();
  const response = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      ...options?.headers,
    },
  });

  if (!response.ok) {
    throw new ApiError(response.status, await response.json());
  }

  return response.json();
}
```

**Step 2: Create QueryClient with global error handler**

```typescript
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: 1, staleTime: 30_000 },
    mutations: {
      onError: (error) => {
        if (error instanceof ApiError && error.status === 401) {
          // Redirect to login
        }
      },
    },
  },
});
```

**Step 3: Set up MSAL auth (Entra ID)**

```bash
npm install @azure/msal-browser @azure/msal-react
```

Configure MSAL provider for multi-tenant Entra ID auth.

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add API client with auth and query client"
```

---

### Task 32: Create shared layout (shell, sidebar, nav)

**Files:**
- Create: `frontend/src/components/layout/AppShell.tsx`
- Create: `frontend/src/components/layout/Sidebar.tsx`
- Create: `frontend/src/components/layout/TopNav.tsx`
- Create: `frontend/src/components/layout/TenantSelector.tsx`
- Create: `frontend/src/components/layout/NotificationBell.tsx`
- Create: `frontend/src/hooks/useCurrentUser.ts`

**Step 1: Add shadcn components needed**

```bash
npx shadcn@latest add button dropdown-menu avatar badge sheet select
```

**Step 2: Implement AppShell**

Layout with TopNav (tenant selector, user menu, notification bell) and Sidebar (role-adaptive nav links).

**Step 3: Implement role-adaptive sidebar**

Navigation items shown/hidden based on user's roles. Use the permission model from the design.

**Step 4: Implement TenantSelector**

Dropdown for users with cross-tenant access.

**Step 5: Implement NotificationBell**

Shows unread count. Connects to SignalR hub for real-time updates.

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add app shell with role-adaptive sidebar and tenant selector"
```

---

### Task 33: Set up TanStack Router with routes

**Files:**
- Create: `frontend/src/routes/__root.tsx`
- Create: `frontend/src/routes/index.tsx` (dashboard)
- Create: `frontend/src/routes/vulnerabilities/index.tsx`
- Create: `frontend/src/routes/vulnerabilities/$id.tsx`
- Create: `frontend/src/routes/tasks/index.tsx`
- Create: `frontend/src/routes/assets/index.tsx`
- Create: `frontend/src/routes/campaigns/index.tsx`
- Create: `frontend/src/routes/campaigns/$id.tsx`
- Create: `frontend/src/routes/audit-log/index.tsx`
- Create: `frontend/src/routes/admin/users.tsx`
- Create: `frontend/src/routes/admin/teams.tsx`
- Create: `frontend/src/routes/settings/index.tsx`
- Create: `frontend/src/routeTree.gen.ts` (auto-generated)

**Step 1: Create root route with AppShell layout**

```typescript
// frontend/src/routes/__root.tsx
import { createRootRoute, Outlet } from '@tanstack/react-router'
import { AppShell } from '../components/layout/AppShell'

export const Route = createRootRoute({
  component: () => (
    <AppShell>
      <Outlet />
    </AppShell>
  ),
})
```

**Step 2: Create placeholder route components**

Each route file exports a `Route` with a basic placeholder component. These will be fleshed out in subsequent tasks.

**Step 3: Verify routing works**

Run dev server, navigate between routes.

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: set up TanStack Router with route structure"
```

---

### Task 34: Dashboard view

**Files:**
- Create: `frontend/src/api/useDashboard.ts`
- Create: `frontend/src/components/features/dashboard/ExposureScore.tsx`
- Create: `frontend/src/components/features/dashboard/TrendChart.tsx`
- Create: `frontend/src/components/features/dashboard/SlaComplianceCard.tsx`
- Create: `frontend/src/components/features/dashboard/CriticalVulnerabilities.tsx`
- Create: `frontend/src/components/features/dashboard/RemediationVelocity.tsx`
- Modify: `frontend/src/routes/index.tsx`
- Create: `frontend/src/__tests__/dashboard.test.tsx`

**Step 1: Add shadcn components**

```bash
npx shadcn@latest add card table tabs
```

**Step 2: Create TanStack Query hooks**

```typescript
// frontend/src/api/useDashboard.ts
import { useQuery } from '@tanstack/react-query'

const dashboardKeys = {
  summary: (tenantId?: string) => ['dashboard', 'summary', tenantId] as const,
  trends: (tenantId?: string) => ['dashboard', 'trends', tenantId] as const,
}

export function useDashboardSummary(tenantId?: string) {
  return useQuery({
    queryKey: dashboardKeys.summary(tenantId),
    queryFn: () => fetchWithAuth<DashboardSummary>(`/dashboard/summary?tenantId=${tenantId ?? ''}`),
  })
}
```

**Step 3: Build dashboard components**

- ExposureScore: large number with trend indicator
- TrendChart: line chart (use recharts or similar lightweight charting lib)
- SlaComplianceCard: percentage with progress bar
- CriticalVulnerabilities: table of top-N
- RemediationVelocity: bar chart by severity

**Step 4: Write component tests with MSW**

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add dashboard view with charts and metrics"
```

---

### Task 35: Vulnerability list and detail views

**Files:**
- Create: `frontend/src/api/useVulnerabilities.ts`
- Create: `frontend/src/components/features/vulnerabilities/VulnerabilityTable.tsx`
- Create: `frontend/src/components/features/vulnerabilities/VulnerabilityDetail.tsx`
- Create: `frontend/src/components/features/vulnerabilities/OrgSeverityPanel.tsx`
- Create: `frontend/src/components/features/vulnerabilities/AiReportTab.tsx`
- Create: `frontend/src/components/features/vulnerabilities/AffectedAssetsTab.tsx`
- Create: `frontend/src/components/features/vulnerabilities/TimelineTab.tsx`
- Create: `frontend/src/components/features/vulnerabilities/CommentsTab.tsx`
- Modify: `frontend/src/routes/vulnerabilities/index.tsx`
- Modify: `frontend/src/routes/vulnerabilities/$id.tsx`

**Step 1: Add shadcn components**

```bash
npx shadcn@latest add dialog input textarea select separator data-table
```

**Step 2: Create query key factory and hooks**

Follow the pattern from code standards spec.

**Step 3: Build VulnerabilityTable**

Filterable/sortable data table with quick action buttons.

**Step 4: Build VulnerabilityDetail**

Tabbed view: AI Report, Affected Assets, Timeline, Comments. Org severity adjustment panel.

**Step 5: Write tests**

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add vulnerability list and detail views"
```

---

### Task 36: My Tasks view (Asset Owner)

**Files:**
- Create: `frontend/src/api/useTasks.ts`
- Create: `frontend/src/components/features/tasks/TaskList.tsx`
- Create: `frontend/src/components/features/tasks/TaskStatusUpdate.tsx`
- Modify: `frontend/src/routes/tasks/index.tsx`

**Step 1: Create query hooks for tasks**

**Step 2: Build TaskList component**

Grouped by SLA status: overdue (red), due soon (amber), on track (green). Each task has quick status update dropdown.

**Step 3: Build TaskStatusUpdate dialog**

Status selector + justification textarea (required for CannotPatch/RiskAccepted).

**Step 4: Write tests**

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add My Tasks view for asset owners"
```

---

### Task 37: Asset management view

**Files:**
- Create: `frontend/src/api/useAssets.ts`
- Create: `frontend/src/components/features/assets/AssetTable.tsx`
- Create: `frontend/src/components/features/assets/AssignOwnerDialog.tsx`
- Create: `frontend/src/components/features/assets/BulkAssignDialog.tsx`
- Modify: `frontend/src/routes/assets/index.tsx`

Filterable table, owner assignment, criticality setting, bulk operations.

**Commit message:** `feat: add asset management view`

---

### Task 38: Campaign views

**Files:**
- Create: `frontend/src/api/useCampaigns.ts`
- Create: `frontend/src/components/features/campaigns/CampaignList.tsx`
- Create: `frontend/src/components/features/campaigns/CampaignDetail.tsx`
- Create: `frontend/src/components/features/campaigns/CreateCampaignDialog.tsx`
- Modify: `frontend/src/routes/campaigns/index.tsx`
- Modify: `frontend/src/routes/campaigns/$id.tsx`

Campaign list with progress bars, detail view with linked vulnerabilities, bulk assign.

**Commit message:** `feat: add campaign views`

---

### Task 39: Audit log view

**Files:**
- Create: `frontend/src/api/useAuditLog.ts`
- Create: `frontend/src/components/features/audit/AuditLogTable.tsx`
- Create: `frontend/src/components/features/audit/AuditDetailDialog.tsx`
- Modify: `frontend/src/routes/audit-log/index.tsx`

Searchable/filterable log. Detail dialog shows before/after JSON diff.

**Commit message:** `feat: add audit log view`

---

### Task 40: User & team management views

**Files:**
- Create: `frontend/src/api/useUsers.ts`
- Create: `frontend/src/api/useTeams.ts`
- Create: `frontend/src/components/features/admin/UserTable.tsx`
- Create: `frontend/src/components/features/admin/TeamTable.tsx`
- Create: `frontend/src/components/features/admin/InviteUserDialog.tsx`
- Create: `frontend/src/components/features/admin/ManageRolesDialog.tsx`
- Create: `frontend/src/components/features/admin/CreateTeamDialog.tsx`
- Modify: `frontend/src/routes/admin/users.tsx`
- Modify: `frontend/src/routes/admin/teams.tsx`

**Commit message:** `feat: add user and team management views`

---

### Task 41: Settings view

**Files:**
- Create: `frontend/src/api/useSettings.ts`
- Create: `frontend/src/components/features/settings/SlaConfigForm.tsx`
- Create: `frontend/src/components/features/settings/FallbackChainForm.tsx`
- Create: `frontend/src/components/features/settings/AiProviderForm.tsx`
- Create: `frontend/src/components/features/settings/TenantManagement.tsx`
- Modify: `frontend/src/routes/settings/index.tsx`

Forms for SLA config, fallback chain, AI provider selection, tenant management.

**Commit message:** `feat: add settings view`

---

### Task 42: SignalR client integration

**Files:**
- Create: `frontend/src/lib/signalr.ts`
- Create: `frontend/src/hooks/useSignalR.ts`
- Modify: `frontend/src/components/layout/NotificationBell.tsx`

**Step 1: Create SignalR connection manager**

```typescript
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'

export function createSignalRConnection(token: string) {
  return new HubConnectionBuilder()
    .withUrl('/hubs/notifications', {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()
}
```

**Step 2: Create `useSignalR` hook**

Manages connection lifecycle. Exposes event handlers. Invalidates relevant TanStack Query caches on events.

**Step 3: Integrate into NotificationBell**

Update unread count in real-time. Toast notifications for critical events.

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add SignalR client integration for real-time updates"
```

---

## Phase 12: Docker Compose

### Task 43: Create Dockerfiles and Docker Compose

**Files:**
- Create: `src/Vigil.Api/Dockerfile`
- Create: `src/Vigil.Worker/Dockerfile`
- Create: `frontend/Dockerfile`
- Create: `docker-compose.yml`
- Create: `.env.example`

**Step 1: Create API Dockerfile**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Vigil.Api/Vigil.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Vigil.Api.dll"]
```

**Step 2: Create Worker Dockerfile** (similar pattern)

**Step 3: Create Frontend Dockerfile**

Multi-stage: Vite build → nginx to serve static files.

**Step 4: Create `docker-compose.yml`**

```yaml
services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: vigil
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
    ports:
      - "5432:5432"

  api:
    build:
      context: .
      dockerfile: src/Vigil.Api/Dockerfile
    environment:
      ConnectionStrings__Vigil: "Host=postgres;Database=vigil;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      AzureAd__ClientId: ${AZURE_AD_CLIENT_ID}
      AzureAd__TenantId: ${AZURE_AD_TENANT_ID}
      Frontend__Origin: http://localhost:3000
      Smtp__Host: ${SMTP_HOST}
      Smtp__Port: ${SMTP_PORT}
    ports:
      - "8080:8080"
    depends_on:
      - postgres

  worker:
    build:
      context: .
      dockerfile: src/Vigil.Worker/Dockerfile
    environment:
      ConnectionStrings__Vigil: "Host=postgres;Database=vigil;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      Defender__ClientId: ${DEFENDER_CLIENT_ID}
      Defender__ClientSecret: ${DEFENDER_CLIENT_SECRET}
    depends_on:
      - postgres

  frontend:
    build:
      context: frontend
      dockerfile: Dockerfile
      args:
        VITE_API_URL: http://localhost:8080/api
    ports:
      - "3000:80"
    depends_on:
      - api

volumes:
  pgdata:
```

**Step 5: Create `.env.example`**

List all required environment variables with placeholder values.

**Step 6: Test docker-compose up**

Run: `docker compose build`
Expected: All images build successfully.

**Step 7: Commit**

```bash
git add -A
git commit -m "feat: add Docker Compose for self-hosting"
```

---

## Phase 13: Final Integration & Polish

### Task 44: Run full integration test suite

**Step 1:** `dotnet test Vigil.sln -v minimal`
Expected: All tests pass.

**Step 2:** `cd frontend && npm test`
Expected: All tests pass.

**Step 3:** Fix any failures found.

**Step 4: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve integration test issues"
```

---

### Task 45: Guided onboarding UI

**Files:**
- Create: `src/Vigil.Api/Controllers/SetupController.cs`
- Create: `src/Vigil.Core/Services/SetupService.cs`
- Create: `src/Vigil.Core/Interfaces/ISetupService.cs`
- Create: `frontend/src/routes/setup/index.tsx`
- Create: `frontend/src/components/features/setup/SetupWizard.tsx`
- Create: `frontend/src/components/features/setup/steps/WelcomeStep.tsx`
- Create: `frontend/src/components/features/setup/steps/TenantConfigStep.tsx`
- Create: `frontend/src/components/features/setup/steps/EntraIdStep.tsx`
- Create: `frontend/src/components/features/setup/steps/DefenderConnectionStep.tsx`
- Create: `frontend/src/components/features/setup/steps/SlaConfigStep.tsx`
- Create: `frontend/src/components/features/setup/steps/AdminUserStep.tsx`
- Create: `frontend/src/components/features/setup/steps/ReviewStep.tsx`
- Create: `frontend/src/api/useSetup.ts`
- Create: `tests/Vigil.Tests/Core/SetupServiceTests.cs`

**Step 1: Create `ISetupService` and `SetupService`**

The setup service checks whether the application has been initialized (any tenants exist). If not, it exposes endpoints to walk the admin through first-time configuration.

```csharp
namespace Vigil.Core.Interfaces;

public interface ISetupService
{
    Task<bool> IsInitializedAsync(CancellationToken ct);
    Task<Result<Tenant>> CompleteSetupAsync(SetupRequest request, CancellationToken ct);
}
```

`SetupRequest` contains: tenant name, Entra tenant ID, Entra app registration details, Defender connection settings, initial SLA configuration, and admin user info.

**Step 2: Create `SetupController`**

```csharp
[ApiController]
[Route("api/setup")]
public class SetupController : ControllerBase
{
    [HttpGet("status")]
    [AllowAnonymous] // Must be accessible before auth is configured
    public async Task<ActionResult<SetupStatusDto>> GetStatus(CancellationToken ct)
    {
        var isInitialized = await _setupService.IsInitializedAsync(ct);
        return Ok(new SetupStatusDto(isInitialized));
    }

    [HttpPost("complete")]
    [AllowAnonymous] // Only works when not yet initialized
    public async Task<IActionResult> CompleteSetup(
        SetupRequest request, CancellationToken ct)
    {
        if (await _setupService.IsInitializedAsync(ct))
            return Conflict("Application is already initialized.");

        var result = await _setupService.CompleteSetupAsync(request, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return Ok();
    }
}
```

**Step 3: Create the frontend setup wizard**

Multi-step wizard using shadcn/ui `Stepper` or custom step navigation:

1. **Welcome** — Introduction to Vigil, what will be configured
2. **Tenant Configuration** — Organization name, primary Entra tenant ID
3. **Entra ID Connection** — App registration client ID/secret, validate connection
4. **Defender Connection** — Defender API credentials, test connectivity
5. **SLA Configuration** — Set remediation deadlines per severity (with sensible defaults: Critical=7d, High=30d, Medium=90d, Low=180d)
6. **Admin User** — Confirm the current authenticated user as Global Admin, optionally invite additional admins
7. **Review & Complete** — Summary of all settings, confirm and initialize

```bash
npx shadcn@latest add stepper progress alert
```

**Step 4: Add setup guard to router**

The app checks `/api/setup/status` on load. If not initialized, redirect all routes to `/setup`. After setup completes, redirect to the dashboard.

```typescript
// In root route loader or a guard
const setupStatus = await fetchSetupStatus()
if (!setupStatus.isInitialized && !location.pathname.startsWith('/setup')) {
  throw redirect({ to: '/setup' })
}
```

**Step 5: Write tests**

- `SetupService` unit tests: verify setup creates tenant, SLA config, admin user, and marks as initialized
- `SetupController` integration tests: verify setup endpoint works when uninitialized and returns 409 when already initialized
- Frontend: test wizard step navigation and validation

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add guided onboarding wizard for first-time setup"
```

---

### Task 46: Create README

**Files:**
- Create: `README.md`

Include:
- Project overview
- Architecture diagram (text-based)
- Prerequisites (Docker, .NET 10 SDK, Node.js)
- Quick start with Docker Compose
- Development setup instructions
- Environment variables reference
- Link to design doc

**Commit message:** `docs: add README with setup instructions`

---

## Summary

| Phase | Tasks | Focus |
|-------|-------|-------|
| 1. Scaffolding | 1-2 | Solution structure, packages |
| 2. Domain Model | 3-6 | Enums, entities, interfaces, Result pattern |
| 3. Database | 7-10 | DbContext, audit interceptor, migrations, repositories |
| 4. Auth | 11-12 | Entra ID, policy-based authorization |
| 5. API | 13-22 | All REST endpoints |
| 6. SignalR | 23 | Real-time notifications hub |
| 7. Email | 24 | Email notification service |
| 8. AI Reports | 25 | Pluggable AI report generation |
| 9. Ingestion | 26-28 | Worker, Defender source, SLA service |
| 10. DI & OpenAPI | 29 | Wire everything together |
| 11. Frontend | 30-42 | Full SPA with all views |
| 12. Docker | 43 | Docker Compose self-hosting |
| 13. Polish | 44-46 | Integration tests, onboarding wizard, README |

**Total: 46 tasks across 13 phases.**
