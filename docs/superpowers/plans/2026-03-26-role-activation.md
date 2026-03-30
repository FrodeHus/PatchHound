# Role Activation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement session-based role activation so users start with Stakeholder (read-only) and explicitly elevate to their assigned roles, following least-privilege principles.

**Architecture:** Two-layer approach — frontend session stores `activeRoles`, sends them via `X-Active-Roles` header on every API request; backend `RoleRequirementHandler` validates header roles against assigned roles and authorizes only the active set. A new `POST /api/roles/activate` endpoint validates role assignment and writes audit logs.

**Tech Stack:** .NET 8 (ASP.NET Core, EF Core), TanStack Start (React, server functions), iron-session (PostgreSQL-backed), Zod schemas, Tailwind CSS, shadcn/ui components.

**Spec:** `docs/superpowers/specs/2026-03-26-role-activation-design.md`

---

## File Structure

### Backend (C# / .NET)

| File | Action | Responsibility |
|------|--------|---------------|
| `src/PatchHound.Api/Controllers/RolesController.cs` | Create | `POST /api/roles/activate` — validates role assignment, computes diff, writes audit logs |
| `src/PatchHound.Api/Auth/RoleRequirementHandler.cs` | Modify | Read `X-Active-Roles` header, validate against assigned roles, authorize active set only, always include Stakeholder |
| `src/PatchHound.Core/Enums/AuditAction.cs` | Modify | Add `Activated` and `Deactivated` enum values |

### Frontend (TypeScript / React)

| File | Action | Responsibility |
|------|--------|---------------|
| `frontend/src/server/session.ts` | Modify | Add `activeRoles` to `SessionData` interface and `AppSession` class + `save()` payload |
| `frontend/src/server/auth.functions.ts` | Modify | Return `activeRoles` from `getCurrentUser()` |
| `frontend/src/server/api.ts` | Modify | Add `activeRoles` to `ApiRequestContext`, send `X-Active-Roles` header in `buildHeaders()` |
| `frontend/src/server/middleware.ts` | Modify | Pass `activeRoles` from session into context |
| `frontend/src/api/roles.functions.ts` | Create | `activateRoles` server function — calls backend, updates session |
| `frontend/src/components/features/roles/RoleActivationDialog.tsx` | Create | Modal dialog with role toggles |
| `frontend/src/components/layout/TopNav.tsx` | Modify | Add "Activate Roles..." menu item, role indicator badge, use `activeRoles` for gating |
| `frontend/src/components/layout/Sidebar.tsx` | Modify | Change `canAccess` to check `activeRoles` instead of `roles` |

| `frontend/src/components/ui/switch.tsx` | Create | Switch toggle component built on base-ui Switch primitive |

### Tests

| File | Action | Responsibility |
|------|--------|---------------|
| `tests/PatchHound.Tests/Auth/RoleRequirementHandlerTests.cs` | Create | Tests for active-role filtering, Stakeholder always included, header spoofing prevention |
| `tests/PatchHound.Tests/Controllers/RolesControllerTests.cs` | Create | Tests for activation endpoint — valid/invalid roles, audit logging, idempotency |
| `frontend/src/components/features/roles/__tests__/RoleActivationDialog.test.tsx` | Create | Tests for dialog toggle behavior, error handling |

### Additional Frontend Files to Modify (role gating: `user.roles` → `user.activeRoles`)

| File | Lines | Change |
|------|-------|--------|
| `frontend/src/routes/_authed/dashboard.tsx` | 57-61, 270 | Change `user.roles.includes(...)` → `(user.activeRoles ?? []).includes(...)` |
| `frontend/src/routes/_authed/software/$id.tsx` | 97-98 | Same pattern |
| `frontend/src/routes/_authed/settings/index.tsx` | 97 | Same pattern |
| `frontend/src/routes/_authed/admin/security-profiles.tsx` | 128 | Same pattern |
| `frontend/src/routes/_authed/admin/tenants/$id.tsx` | 15 | Same pattern |
| `frontend/src/routes/_authed/admin/teams/index.tsx` | 35 | Same pattern |
| `frontend/src/routes/_authed/admin/sources.tsx` | 50 | Same pattern |
| `frontend/src/routes/_authed/admin/index.tsx` | 72 | Change `user.roles.some(...)` → `[...(user.activeRoles ?? []), 'Stakeholder'].some(...)` |

---

### Task 1: Add `Activated` and `Deactivated` to AuditAction Enum

**Files:**
- Modify: `src/PatchHound.Core/Enums/AuditAction.cs:1-8`

- [ ] **Step 1: Add new enum values**

```csharp
namespace PatchHound.Core.Enums;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
    Approved,
    Denied,
    AutoDenied,
    Activated,
    Deactivated,
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build PatchHound.slnx`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Core/Enums/AuditAction.cs
git commit -m "feat: add Activated and Deactivated audit actions for role activation"
```

---

### Task 2: Modify `RoleRequirementHandler` to Use Active Roles

**Files:**
- Modify: `src/PatchHound.Api/Auth/RoleRequirementHandler.cs:1-70`
- Test: `tests/PatchHound.Tests/Auth/RoleRequirementHandlerTests.cs`

The handler currently checks all assigned roles. Change it to:
1. Read `X-Active-Roles` header from the HTTP request
2. If present and non-empty, parse into a role list
3. Validate each header role is actually assigned to the user for the current tenant (prevents spoofing)
4. Authorize against only the validated active roles
5. Always include `Stakeholder` in the effective set

The handler needs `IHttpContextAccessor` injected to read request headers.

- [ ] **Step 1: Write failing tests**

Create `tests/PatchHound.Tests/Auth/RoleRequirementHandlerTests.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using PatchHound.Api.Auth;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using System.Security.Claims;

namespace PatchHound.Tests.Auth;

public class RoleRequirementHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor;
    private readonly DefaultHttpContext _httpContext;
    private readonly RoleRequirementHandler _handler;

    public RoleRequirementHandlerTests()
    {
        _tenantContext = new Mock<ITenantContext>();
        _httpContextAccessor = new Mock<IHttpContextAccessor>();
        _httpContext = new DefaultHttpContext();
        _httpContextAccessor.Setup(x => x.HttpContext).Returns(_httpContext);
        _handler = new RoleRequirementHandler(_tenantContext.Object, _httpContextAccessor.Object);
    }

    private AuthorizationHandlerContext CreateContext(RoleRequirement requirement, ClaimsPrincipal? user = null)
    {
        user ??= new ClaimsPrincipal(new ClaimsIdentity());
        return new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            null
        );
    }

    [Fact]
    public async Task StakeholderAlwaysIncluded_EvenWithNoHeader()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.Setup(x => x.CurrentTenantId).Returns(tenantId);
        _tenantContext.Setup(x => x.AccessibleTenantIds).Returns(new List<Guid> { tenantId });
        _tenantContext.Setup(x => x.GetRolesForTenant(tenantId))
            .Returns(new List<string> { "Stakeholder", "SecurityManager" });

        // No X-Active-Roles header → only Stakeholder
        var requirement = new RoleRequirement(RoleName.Stakeholder);
        var context = CreateContext(requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task NoHeader_NonStakeholderRoleFails()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.Setup(x => x.CurrentTenantId).Returns(tenantId);
        _tenantContext.Setup(x => x.AccessibleTenantIds).Returns(new List<Guid> { tenantId });
        _tenantContext.Setup(x => x.GetRolesForTenant(tenantId))
            .Returns(new List<string> { "Stakeholder", "SecurityManager" });

        // No header → SecurityManager not active even though assigned
        var requirement = new RoleRequirement(RoleName.SecurityManager);
        var context = CreateContext(requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task WithHeader_ActivatedRolePasses()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.Setup(x => x.CurrentTenantId).Returns(tenantId);
        _tenantContext.Setup(x => x.AccessibleTenantIds).Returns(new List<Guid> { tenantId });
        _tenantContext.Setup(x => x.GetRolesForTenant(tenantId))
            .Returns(new List<string> { "Stakeholder", "SecurityManager" });

        _httpContext.Request.Headers["X-Active-Roles"] = "SecurityManager";

        var requirement = new RoleRequirement(RoleName.SecurityManager);
        var context = CreateContext(requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task WithHeader_SpoofedRoleFails()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.Setup(x => x.CurrentTenantId).Returns(tenantId);
        _tenantContext.Setup(x => x.AccessibleTenantIds).Returns(new List<Guid> { tenantId });
        _tenantContext.Setup(x => x.GetRolesForTenant(tenantId))
            .Returns(new List<string> { "Stakeholder" }); // Only Stakeholder assigned

        // Try to spoof SecurityManager via header
        _httpContext.Request.Headers["X-Active-Roles"] = "SecurityManager";

        var requirement = new RoleRequirement(RoleName.SecurityManager);
        var context = CreateContext(requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded); // Spoofed role rejected
    }

    [Fact]
    public async Task EntraClaimRoles_RequireActivation()
    {
        // Entra claims (e.g., GlobalAdmin from Azure AD) also require activation per spec
        var claims = new[] { new Claim("roles", "GlobalAdmin") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        var tenantId = Guid.NewGuid();
        _tenantContext.Setup(x => x.CurrentTenantId).Returns(tenantId);
        _tenantContext.Setup(x => x.AccessibleTenantIds).Returns(new List<Guid> { tenantId });
        _tenantContext.Setup(x => x.GetRolesForTenant(tenantId))
            .Returns(new List<string> { "Stakeholder", "GlobalAdmin" });

        // No X-Active-Roles header → GlobalAdmin not active even though in Entra claims
        var requirement = new RoleRequirement(RoleName.GlobalAdmin);
        var context = CreateContext(requirement, user);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task EntraClaimRoles_PassWhenActivated()
    {
        var claims = new[] { new Claim("roles", "GlobalAdmin") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        var tenantId = Guid.NewGuid();
        _tenantContext.Setup(x => x.CurrentTenantId).Returns(tenantId);
        _tenantContext.Setup(x => x.AccessibleTenantIds).Returns(new List<Guid> { tenantId });
        _tenantContext.Setup(x => x.GetRolesForTenant(tenantId))
            .Returns(new List<string> { "Stakeholder", "GlobalAdmin" });

        _httpContext.Request.Headers["X-Active-Roles"] = "GlobalAdmin";

        var requirement = new RoleRequirement(RoleName.GlobalAdmin);
        var context = CreateContext(requirement, user);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~RoleRequirementHandlerTests" -v minimal`
Expected: Compilation error (constructor signature changed) or test failures

- [ ] **Step 3: Implement the handler changes**

Replace `src/PatchHound.Api/Auth/RoleRequirementHandler.cs` with:

```csharp
using Microsoft.AspNetCore.Authorization;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;

namespace PatchHound.Api.Auth;

public class RoleRequirementHandler : AuthorizationHandler<RoleRequirement>
{
    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RoleRequirementHandler(ITenantContext tenantContext, IHttpContextAccessor httpContextAccessor)
    {
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement
    )
    {
        // All roles — including Entra claim roles — must be explicitly activated.
        // Per spec: "GlobalAdmin follows the same activation pattern — no special treatment."

        if (_tenantContext.AccessibleTenantIds.Count == 0)
            return Task.CompletedTask;

        // Read active roles from header
        var activeRolesHeader = _httpContextAccessor.HttpContext?
            .Request.Headers["X-Active-Roles"].FirstOrDefault();

        var headerRoles = string.IsNullOrWhiteSpace(activeRolesHeader)
            ? Array.Empty<string>()
            : activeRolesHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Check against current tenant or all accessible tenants
        if (_tenantContext.CurrentTenantId is Guid currentTenantId)
        {
            var assignedRoles = _tenantContext.GetRolesForTenant(currentTenantId);
            var effectiveRoles = BuildEffectiveRoles(headerRoles, assignedRoles);

            if (effectiveRoles.Any(role => requirement.AllowedRoles.Contains(role)))
            {
                context.Succeed(requirement);
            }
        }
        else
        {
            foreach (var tenantId in _tenantContext.AccessibleTenantIds)
            {
                var assignedRoles = _tenantContext.GetRolesForTenant(tenantId);
                var effectiveRoles = BuildEffectiveRoles(headerRoles, assignedRoles);

                if (effectiveRoles.Any(role => requirement.AllowedRoles.Contains(role)))
                {
                    context.Succeed(requirement);
                    break;
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the effective role set from the active-roles header and assigned roles.
    /// - Stakeholder is always included (permanent, non-deactivatable).
    /// - Header roles are validated against assigned roles (prevents spoofing).
    /// - If no header is present, only Stakeholder is active.
    /// </summary>
    private static List<RoleName> BuildEffectiveRoles(
        string[] headerRoles,
        IReadOnlyList<string> assignedRoles)
    {
        var effective = new List<RoleName> { RoleName.Stakeholder };

        foreach (var headerRole in headerRoles)
        {
            if (Enum.TryParse<RoleName>(headerRole, out var roleName)
                && roleName != RoleName.Stakeholder
                && assignedRoles.Contains(headerRole, StringComparer.OrdinalIgnoreCase))
            {
                effective.Add(roleName);
            }
        }

        return effective;
    }
}
```

**Important:** Register `IHttpContextAccessor` in DI if not already registered. Check `Program.cs` — if `builder.Services.AddHttpContextAccessor()` is not present, add it.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~RoleRequirementHandlerTests" -v minimal`
Expected: All 5 tests pass

- [ ] **Step 5: Build full solution**

Run: `dotnet build PatchHound.slnx`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Api/Auth/RoleRequirementHandler.cs tests/PatchHound.Tests/Auth/RoleRequirementHandlerTests.cs
git commit -m "feat: modify RoleRequirementHandler to authorize against active roles only

Reads X-Active-Roles header, validates against assigned roles, always
includes Stakeholder. Prevents header spoofing by cross-checking
assigned roles for the current tenant."
```

---

### Task 3: Create `RolesController` Activation Endpoint

**Files:**
- Create: `src/PatchHound.Api/Controllers/RolesController.cs`
- Test: `tests/PatchHound.Tests/Controllers/RolesControllerTests.cs`

This endpoint validates that each requested role is assigned to the user, computes the diff between old and new active roles, and writes audit log entries.

- [ ] **Step 1: Write failing tests**

Create `tests/PatchHound.Tests/Controllers/RolesControllerTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PatchHound.Api.Controllers;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace PatchHound.Tests.Controllers;

public class RolesControllerTests
{
    private readonly Mock<ITenantContext> _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly AuditLogWriter _auditLogWriter;
    private readonly RolesController _controller;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public RolesControllerTests()
    {
        _tenantContext = new Mock<ITenantContext>();
        _tenantContext.Setup(x => x.CurrentTenantId).Returns(_tenantId);
        _tenantContext.Setup(x => x.CurrentUserId).Returns(_userId);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        _dbContext = new PatchHoundDbContext(options, serviceProvider);
        _auditLogWriter = new AuditLogWriter(_dbContext, _tenantContext.Object);
        _controller = new RolesController(_tenantContext.Object, _auditLogWriter, _dbContext);

        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
    }

    [Fact]
    public async Task Activate_ValidRoles_Returns200WithRoles()
    {
        _tenantContext.Setup(x => x.GetRolesForTenant(_tenantId))
            .Returns(new List<string> { "Stakeholder", "SecurityManager", "Auditor" });

        var result = await _controller.Activate(
            new RolesController.ActivateRequest { Roles = new[] { "SecurityManager" } },
            CancellationToken.None
        );

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<RolesController.ActivateResponse>(okResult.Value);
        Assert.Contains("SecurityManager", response.Roles);
    }

    [Fact]
    public async Task Activate_UnassignedRole_Returns403()
    {
        _tenantContext.Setup(x => x.GetRolesForTenant(_tenantId))
            .Returns(new List<string> { "Stakeholder" });

        var result = await _controller.Activate(
            new RolesController.ActivateRequest { Roles = new[] { "SecurityManager" } },
            CancellationToken.None
        );

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Activate_InvalidRoleName_Returns400()
    {
        _tenantContext.Setup(x => x.GetRolesForTenant(_tenantId))
            .Returns(new List<string> { "Stakeholder" });

        var result = await _controller.Activate(
            new RolesController.ActivateRequest { Roles = new[] { "NotARealRole" } },
            CancellationToken.None
        );

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("NotARealRole", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task Activate_EmptyArray_DeactivatesAll()
    {
        _tenantContext.Setup(x => x.GetRolesForTenant(_tenantId))
            .Returns(new List<string> { "Stakeholder", "SecurityManager" });

        var result = await _controller.Activate(
            new RolesController.ActivateRequest { Roles = Array.Empty<string>() },
            CancellationToken.None
        );

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<RolesController.ActivateResponse>(okResult.Value);
        Assert.Empty(response.Roles);
    }

    [Fact]
    public async Task Activate_WritesAuditLogForNewActivation()
    {
        _tenantContext.Setup(x => x.GetRolesForTenant(_tenantId))
            .Returns(new List<string> { "Stakeholder", "SecurityManager" });

        // No previous active roles header
        await _controller.Activate(
            new RolesController.ActivateRequest { Roles = new[] { "SecurityManager" } },
            CancellationToken.None
        );

        await _dbContext.SaveChangesAsync();
        var auditEntries = await _dbContext.AuditLogEntries.ToListAsync();
        Assert.Single(auditEntries);
        Assert.Equal(AuditAction.Activated, auditEntries[0].Action);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~RolesControllerTests" -v minimal`
Expected: Compilation error (RolesController doesn't exist yet)

- [ ] **Step 3: Implement the controller**

Create `src/PatchHound.Api/Controllers/RolesController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly AuditLogWriter _auditLogWriter;
    private readonly PatchHoundDbContext _dbContext;

    public RolesController(
        ITenantContext tenantContext,
        AuditLogWriter auditLogWriter,
        PatchHoundDbContext dbContext)
    {
        _tenantContext = tenantContext;
        _auditLogWriter = auditLogWriter;
        _dbContext = dbContext;
    }

    public class ActivateRequest
    {
        public string[] Roles { get; set; } = Array.Empty<string>();
    }

    public class ActivateResponse
    {
        public string[] Roles { get; set; } = Array.Empty<string>();
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate(
        [FromBody] ActivateRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest("No tenant selected.");
        }

        var assignedRoles = _tenantContext.GetRolesForTenant(tenantId);

        // Validate all requested role names are valid enum values
        var invalidRoles = request.Roles
            .Where(r => !Enum.TryParse<RoleName>(r, out _))
            .ToList();

        if (invalidRoles.Count > 0)
        {
            return BadRequest($"Invalid role name(s): {string.Join(", ", invalidRoles)}");
        }

        // Validate all requested roles are assigned to the user
        var unassignedRoles = request.Roles
            .Where(r => !assignedRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (unassignedRoles.Count > 0)
        {
            return Forbid();
        }

        // Compute diff for audit logging
        var previousHeader = Request.Headers["X-Active-Roles"].FirstOrDefault();
        var previousRoles = string.IsNullOrWhiteSpace(previousHeader)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                previousHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

        var newRoles = new HashSet<string>(request.Roles, StringComparer.OrdinalIgnoreCase);

        // Activated: in new but not in previous
        foreach (var role in newRoles.Except(previousRoles, StringComparer.OrdinalIgnoreCase))
        {
            await _auditLogWriter.WriteAsync(
                tenantId,
                "RoleActivation",
                _tenantContext.CurrentUserId,
                AuditAction.Activated,
                null,
                new { Role = role },
                ct);
        }

        // Deactivated: in previous but not in new
        foreach (var role in previousRoles.Except(newRoles, StringComparer.OrdinalIgnoreCase))
        {
            await _auditLogWriter.WriteAsync(
                tenantId,
                "RoleActivation",
                _tenantContext.CurrentUserId,
                AuditAction.Deactivated,
                new { Role = role },
                null,
                ct);
        }

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new ActivateResponse { Roles = request.Roles });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~RolesControllerTests" -v minimal`
Expected: All 5 tests pass

- [ ] **Step 5: Build full solution**

Run: `dotnet build PatchHound.slnx`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Api/Controllers/RolesController.cs tests/PatchHound.Tests/Controllers/RolesControllerTests.cs
git commit -m "feat: add POST /api/roles/activate endpoint with audit logging

Validates role assignment for the current tenant, computes diff between
previous and new active roles, writes per-role audit entries."
```

---

### Task 4: Frontend Session and API Layer Changes

**Files:**
- Modify: `frontend/src/server/session.ts:51-64` (SessionData) and `frontend/src/server/session.ts:143-155` (AppSession class) and `frontend/src/server/session.ts:169-182` (save payload)
- Modify: `frontend/src/server/auth.functions.ts:64-73` (getCurrentUser return)
- Modify: `frontend/src/server/api.ts:3-6` (ApiRequestContext) and `frontend/src/server/api.ts:51-67` (buildHeaders)
- Modify: `frontend/src/server/middleware.ts:53-61` (context)

- [ ] **Step 1: Add `activeRoles` to `SessionData` interface**

In `frontend/src/server/session.ts`, add to the `SessionData` interface (after line 63):

```typescript
export interface SessionData {
  accessToken?: string
  tokenExpiry?: number
  refreshToken?: string
  userId?: string
  email?: string
  displayName?: string
  tenantId?: string
  tenantName?: string
  entraRoles?: string[]
  roles?: string[]
  tenantIds?: string[]
  oauthState?: string
  activeRoles?: string[]
}
```

- [ ] **Step 2: Add `activeRoles` to `AppSession` class properties**

In `frontend/src/server/session.ts`, add `activeRoles` to the `AppSession` class (after line 155):

```typescript
class AppSession implements SessionData {
  accessToken?: string
  tokenExpiry?: number
  refreshToken?: string
  userId?: string
  email?: string
  displayName?: string
  tenantId?: string
  tenantName?: string
  entraRoles?: string[]
  roles?: string[]
  tenantIds?: string[]
  oauthState?: string
  activeRoles?: string[]
  // ... rest unchanged
```

- [ ] **Step 3: Add `activeRoles` to save payload**

In `frontend/src/server/session.ts`, add `activeRoles` to the payload object in `save()` (after line 181):

```typescript
const payload: SessionData = {
  accessToken: this.accessToken,
  tokenExpiry: this.tokenExpiry,
  refreshToken: this.refreshToken,
  userId: this.userId,
  email: this.email,
  displayName: this.displayName,
  tenantId: this.tenantId,
  tenantName: this.tenantName,
  entraRoles: this.entraRoles,
  roles: this.roles,
  tenantIds: this.tenantIds,
  oauthState: this.oauthState,
  activeRoles: this.activeRoles,
}
```

- [ ] **Step 4: Return `activeRoles` from `getCurrentUser()`**

In `frontend/src/server/auth.functions.ts`, add `activeRoles` to the return object (line 64-73):

```typescript
return {
  id: session.userId,
  email: session.email ?? '',
  displayName: session.displayName ?? '',
  roles: session.roles ?? [],
  activeRoles: session.activeRoles ?? [],
  tenantId: session.tenantId,
  tenantIds,
  requiresSetup: setupStatus?.requiresSetup ?? false,
  systemStatus,
}
```

- [ ] **Step 5: Add `activeRoles` to `ApiRequestContext` and `buildHeaders()`**

In `frontend/src/server/api.ts`, modify the `ApiRequestContext` type (line 3-6):

```typescript
export type ApiRequestContext = {
  token: string
  tenantId?: string
  activeRoles?: string[]
}
```

In the `buildHeaders()` function (line 51-67), add after the tenantId header:

```typescript
function buildHeaders(context: ApiRequestContext, includeJsonContentType = false) {
  const headers: Record<string, string> = {}

  if (context.token) {
    headers.Authorization = `Bearer ${context.token}`
  }

  if (context.tenantId) {
    headers['X-Tenant-Id'] = context.tenantId
  }

  if (context.activeRoles?.length) {
    headers['X-Active-Roles'] = context.activeRoles.join(',')
  }

  if (includeJsonContentType) {
    headers['Content-Type'] = 'application/json'
  }

  return headers
}
```

- [ ] **Step 6: Pass `activeRoles` from session into middleware context**

In `frontend/src/server/middleware.ts`, add `activeRoles` to the context object (line 53-61):

```typescript
return next({
  context: {
    token: session.accessToken,
    userId: session.userId,
    tenantId: activeTenantId,
    activeTenantId,
    accessibleTenantIds,
    roles: session.roles ?? [],
    activeRoles: session.activeRoles ?? [],
  },
})
```

- [ ] **Step 7: Verify frontend builds and typechecks**

Run: `cd frontend && npm run typecheck && npm run build`
Expected: No type errors, build succeeds

- [ ] **Step 8: Commit**

```bash
git add frontend/src/server/session.ts frontend/src/server/auth.functions.ts frontend/src/server/api.ts frontend/src/server/middleware.ts
git commit -m "feat: add activeRoles to frontend session, API context, and middleware

Stores activeRoles in session, returns them from getCurrentUser(),
sends X-Active-Roles header on every backend API request."
```

---

### Task 5: Create `activateRoles` Server Function

**Files:**
- Create: `frontend/src/api/roles.functions.ts`

- [ ] **Step 1: Create the server function**

Create `frontend/src/api/roles.functions.ts`:

```typescript
import { createServerFn } from '@tanstack/react-start'
import { getSession } from '@/server/session'
import { apiPost } from '@/server/api'

type ActivateRolesResponse = {
  roles: string[]
}

export const activateRoles = createServerFn({ method: 'POST' })
  .validator((data: { roles: string[] }) => data)
  .handler(async ({ data }) => {
    const session = await getSession()

    if (!session.accessToken || !session.userId) {
      throw new Error('Not authenticated')
    }

    const response = await apiPost<ActivateRolesResponse>(
      '/roles/activate',
      {
        token: session.accessToken,
        tenantId: session.tenantId,
        activeRoles: session.activeRoles ?? [],
      },
      { roles: data.roles },
    )

    session.activeRoles = response.roles
    await session.save()

    // Return only the activeRoles — the caller (mutation onSuccess) calls
    // router.invalidate() which re-runs getCurrentUser() for the full user object.
    return { activeRoles: response.roles }
  })
```

- [ ] **Step 2: Verify frontend typechecks**

Run: `cd frontend && npm run typecheck`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/roles.functions.ts
git commit -m "feat: add activateRoles server function

Calls POST /api/roles/activate, updates session activeRoles on success,
returns updated CurrentUser."
```

---

### Task 6: Create Switch UI Component

**Files:**
- Create: `frontend/src/components/ui/switch.tsx`

The project uses base-ui primitives (like Checkbox from `@base-ui/react/checkbox`). base-ui has a Switch primitive at `@base-ui/react/switch`. Create a styled Switch component following the same pattern.

- [ ] **Step 1: Create the Switch component**

Create `frontend/src/components/ui/switch.tsx`:

```tsx
import { Switch as SwitchPrimitive } from "@base-ui/react/switch"

import { cn } from "@/lib/utils"

function Switch({
  className,
  ...props
}: SwitchPrimitive.Root.Props) {
  return (
    <SwitchPrimitive.Root
      data-slot="switch"
      className={cn(
        "peer inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full border-2 border-transparent transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 data-[checked]:bg-primary data-[unchecked]:bg-input",
        className
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb
        data-slot="switch-thumb"
        className="pointer-events-none block size-4 rounded-full bg-background shadow-lg ring-0 transition-transform data-[checked]:translate-x-4 data-[unchecked]:translate-x-0"
      />
    </SwitchPrimitive.Root>
  )
}

export { Switch }
```

- [ ] **Step 2: Verify frontend typechecks**

Run: `cd frontend && npm run typecheck`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/ui/switch.tsx
git commit -m "feat: add Switch component using base-ui Switch primitive"
```

---

### Task 7: Create `RoleActivationDialog` Component

**Files:**
- Create: `frontend/src/components/features/roles/RoleActivationDialog.tsx`

- [ ] **Step 1: Create the dialog component**

Create `frontend/src/components/features/roles/RoleActivationDialog.tsx`:

```tsx
import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useRouter } from '@tanstack/react-router'
import { toast } from 'sonner'
import { activateRoles } from '@/api/roles.functions'
import type { CurrentUser } from '@/server/auth.functions'

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Switch } from '@/components/ui/switch'

const ROLE_DISPLAY_NAMES: Record<string, string> = {
  SecurityManager: 'Security Manager',
  SecurityAnalyst: 'Security Analyst',
  AssetOwner: 'Asset Owner',
  TechnicalManager: 'Technical Manager',
  GlobalAdmin: 'Global Admin',
  Auditor: 'Auditor',
  Stakeholder: 'Stakeholder',
}

type RoleActivationDialogProps = {
  open: boolean
  onOpenChange: (open: boolean) => void
  user: CurrentUser
}

export function RoleActivationDialog({
  open,
  onOpenChange,
  user,
}: RoleActivationDialogProps) {
  const router = useRouter()
  const queryClient = useQueryClient()
  const [pendingRole, setPendingRole] = useState<string | null>(null)

  const elevatedRoles = user.roles.filter((role) => role !== 'Stakeholder')
  const activeRoles = new Set(user.activeRoles ?? [])

  const mutation = useMutation({
    mutationFn: (roles: string[]) => activateRoles({ data: { roles } }),
    onSuccess: async (updatedUser) => {
      await router.invalidate()
      await queryClient.invalidateQueries()
      setPendingRole(null)
    },
    onError: (error) => {
      toast.error(
        error instanceof Error ? error.message : 'Failed to update roles',
      )
      setPendingRole(null)
    },
  })

  function handleToggle(role: string, checked: boolean) {
    setPendingRole(role)
    const newRoles = checked
      ? [...activeRoles, role]
      : [...activeRoles].filter((r) => r !== role)

    const displayName = ROLE_DISPLAY_NAMES[role] ?? role

    mutation.mutate(newRoles, {
      onSuccess: () => {
        toast.success(
          checked
            ? `${displayName} activated`
            : `${displayName} deactivated`,
        )
      },
    })
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Activate Roles</DialogTitle>
          <DialogDescription>
            Elevated roles grant additional permissions. Active roles reset when
            you log out.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-2">
          {/* Stakeholder — always active */}
          <div className="flex items-center justify-between rounded-lg bg-muted/40 px-4 py-3">
            <div>
              <p className="text-sm font-medium">Stakeholder</p>
              <p className="text-xs text-muted-foreground">Always active</p>
            </div>
            <Switch checked disabled aria-label="Stakeholder role (always active)" />
          </div>

          {/* Assigned elevated roles */}
          {elevatedRoles.length === 0 ? (
            <p className="px-4 py-3 text-sm text-muted-foreground">
              No additional roles are assigned to your account. Contact your
              administrator to request role access.
            </p>
          ) : (
            elevatedRoles.map((role) => {
              const isActive = activeRoles.has(role)
              const isPending = pendingRole === role && mutation.isPending
              const displayName = ROLE_DISPLAY_NAMES[role] ?? role

              return (
                <div
                  key={role}
                  className="flex items-center justify-between rounded-lg border border-border/50 px-4 py-3"
                >
                  <p className="text-sm font-medium">{displayName}</p>
                  <Switch
                    checked={isActive}
                    disabled={isPending}
                    onCheckedChange={(checked) => handleToggle(role, checked)}
                    aria-label={`${displayName} role`}
                  />
                </div>
              )
            })
          )}
        </div>

        <p className="text-center text-xs text-muted-foreground">
          Active roles reset when you log out
        </p>
      </DialogContent>
    </Dialog>
  )
}
```

- [ ] **Step 2: Verify frontend typechecks**

Run: `cd frontend && npm run typecheck`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/roles/RoleActivationDialog.tsx
git commit -m "feat: add RoleActivationDialog component

Modal with toggles for each assigned role. Stakeholder shown as always
active. Each toggle calls activateRoles server function with toast feedback."
```

---

### Task 8: Add RoleActivationDialog Tests

**Files:**
- Create: `frontend/src/components/features/roles/__tests__/RoleActivationDialog.test.tsx`

- [ ] **Step 1: Create the test file**

Create `frontend/src/components/features/roles/__tests__/RoleActivationDialog.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RoleActivationDialog } from '../RoleActivationDialog'
import type { CurrentUser } from '@/server/auth.functions'

// Mock dependencies
vi.mock('@/api/roles.functions', () => ({
  activateRoles: vi.fn(),
}))

vi.mock('@tanstack/react-query', () => ({
  useMutation: vi.fn(({ mutationFn, onSuccess, onError }) => ({
    mutate: vi.fn((roles, opts) => {
      mutationFn(roles).then(() => {
        onSuccess?.()
        opts?.onSuccess?.()
      }).catch((err: Error) => {
        onError?.(err)
      })
    }),
    isPending: false,
  })),
  useQueryClient: vi.fn(() => ({
    invalidateQueries: vi.fn(),
  })),
}))

vi.mock('@tanstack/react-router', () => ({
  useRouter: vi.fn(() => ({
    invalidate: vi.fn(),
  })),
}))

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}))

function makeUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: 'user-1',
    email: 'test@example.com',
    displayName: 'Test User',
    roles: ['Stakeholder', 'SecurityManager', 'Auditor'],
    activeRoles: [],
    tenantId: 'tenant-1',
    tenantIds: ['tenant-1'],
    requiresSetup: false,
    systemStatus: null,
    ...overrides,
  }
}

describe('RoleActivationDialog', () => {
  it('renders Stakeholder as always active and disabled', () => {
    render(
      <RoleActivationDialog
        open={true}
        onOpenChange={vi.fn()}
        user={makeUser()}
      />,
    )

    expect(screen.getByText('Stakeholder')).toBeInTheDocument()
    expect(screen.getByText('Always active')).toBeInTheDocument()
    expect(screen.getByLabelText('Stakeholder role (always active)')).toBeDisabled()
  })

  it('renders assigned elevated roles with toggles', () => {
    render(
      <RoleActivationDialog
        open={true}
        onOpenChange={vi.fn()}
        user={makeUser()}
      />,
    )

    expect(screen.getByLabelText('Security Manager role')).toBeInTheDocument()
    expect(screen.getByLabelText('Auditor role')).toBeInTheDocument()
  })

  it('shows empty message when no elevated roles assigned', () => {
    render(
      <RoleActivationDialog
        open={true}
        onOpenChange={vi.fn()}
        user={makeUser({ roles: ['Stakeholder'] })}
      />,
    )

    expect(
      screen.getByText(/No additional roles are assigned/),
    ).toBeInTheDocument()
  })

  it('reflects active roles as checked', () => {
    render(
      <RoleActivationDialog
        open={true}
        onOpenChange={vi.fn()}
        user={makeUser({ activeRoles: ['SecurityManager'] })}
      />,
    )

    const smSwitch = screen.getByLabelText('Security Manager role')
    expect(smSwitch).toBeChecked()

    const auditorSwitch = screen.getByLabelText('Auditor role')
    expect(auditorSwitch).not.toBeChecked()
  })
})
```

- [ ] **Step 2: Run the tests**

Run: `cd frontend && npm test -- --run src/components/features/roles/__tests__/RoleActivationDialog.test.tsx`
Expected: All 4 tests pass

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/roles/__tests__/RoleActivationDialog.test.tsx
git commit -m "test: add RoleActivationDialog component tests

Covers Stakeholder always-active state, elevated role rendering,
empty state message, and active role toggle state."
```

---

### Task 9: Add "Activate Roles..." to TopNav User Menu + Role Indicator

**Files:**
- Modify: `frontend/src/components/layout/TopNav.tsx`

This task adds:
1. An "Activate Roles..." menu item in the user dropdown (between tenant scope and Logout)
2. A subtle dot indicator on the avatar when elevated roles are active
3. Changes `user.roles` checks to `user.activeRoles` for gating

- [ ] **Step 1: Add imports and state for dialog**

At the top of TopNav.tsx, add the import for the dialog component:

```typescript
import { RoleActivationDialog } from '@/components/features/roles/RoleActivationDialog'
```

Inside the `TopNav` function, add state:

```typescript
const [isRoleDialogOpen, setIsRoleDialogOpen] = useState(false);
```

- [ ] **Step 2: Change role-gating to use `activeRoles`**

Change lines 71-72 from:

```typescript
const canUnsealOpenBao = user.roles.includes("GlobalAdmin");
const canSwitchPortalView = user.roles.includes("GlobalAdmin");
```

To:

```typescript
const canUnsealOpenBao = (user.activeRoles ?? []).includes("GlobalAdmin");
const canSwitchPortalView = (user.activeRoles ?? []).includes("GlobalAdmin");
```

- [ ] **Step 3: Add role indicator to avatar area**

On the avatar (around line 243), wrap it in a relative div to show a dot when roles are elevated:

```tsx
<div className="relative">
  <Avatar className="size-7">
    <AvatarFallback className="bg-primary/15 text-[11px] font-semibold text-primary">
      {getInitials(user.displayName, user.email)}
    </AvatarFallback>
  </Avatar>
  {(user.activeRoles ?? []).length > 0 && (
    <span className="absolute -right-0.5 -top-0.5 size-2.5 rounded-full bg-lime-500 ring-2 ring-background" />
  )}
</div>
```

- [ ] **Step 4: Update role display under name**

Change line 252-253 from showing `user.roles[0]` to showing active role count:

```tsx
<p className="mt-0.5 text-[11px] text-muted-foreground">
  {(user.activeRoles ?? []).length > 0
    ? `${(user.activeRoles ?? []).length} role${(user.activeRoles ?? []).length === 1 ? '' : 's'} active`
    : 'Stakeholder'}
</p>
```

- [ ] **Step 5: Add "Activate Roles..." menu item**

After the tenant scope `DropdownMenuItem` (line 325-328) and before the Logout item (line 329), add:

```tsx
<DropdownMenuItem
  className="rounded-lg px-3 py-2"
  onClick={() => setIsRoleDialogOpen(true)}
>
  <Shield className="size-4" />
  Activate Roles{(user.activeRoles ?? []).length > 0
    ? ` (${(user.activeRoles ?? []).length})`
    : '...'}
</DropdownMenuItem>
<DropdownMenuSeparator />
```

Add `Shield` to the `lucide-react` import at the top of TopNav.tsx (it is NOT currently imported there).

- [ ] **Step 6: Render the dialog**

After the `OpenBaoUnsealDialog` component (around line 343), add:

```tsx
<RoleActivationDialog
  open={isRoleDialogOpen}
  onOpenChange={setIsRoleDialogOpen}
  user={user}
/>
```

- [ ] **Step 7: Verify frontend typechecks and lint**

Run: `cd frontend && npm run typecheck && npm run lint`
Expected: No errors

- [ ] **Step 8: Commit**

```bash
git add frontend/src/components/layout/TopNav.tsx
git commit -m "feat: add Activate Roles menu item and role indicator to TopNav

Shows role count badge, lime dot on avatar when elevated, and opens
RoleActivationDialog from user dropdown menu."
```

---

### Task 10: Update Sidebar Role Gating to Use `activeRoles`

**Files:**
- Modify: `frontend/src/components/layout/Sidebar.tsx:103-109`

- [ ] **Step 1: Change `canAccess` to use `activeRoles`**

In `frontend/src/components/layout/Sidebar.tsx`, change the `canAccess` function (lines 103-109) from:

```typescript
function canAccess(item: NavItem, user: CurrentUser): boolean {
  if (!item.roles?.length) {
    return true
  }

  return user.roles.some((role) => item.roles?.includes(role as RoleName))
}
```

To:

```typescript
function canAccess(item: NavItem, user: CurrentUser): boolean {
  if (!item.roles?.length) {
    return true
  }

  const effective: string[] = ['Stakeholder', ...(user.activeRoles ?? [])]
  return effective.some((role) => item.roles?.includes(role as RoleName))
}
```

- [ ] **Step 2: Verify frontend typechecks**

Run: `cd frontend && npm run typecheck`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/layout/Sidebar.tsx
git commit -m "feat: change sidebar canAccess to check activeRoles instead of roles

Navigation items now only visible when their required role is activated.
Stakeholder is always included in the effective set."
```

---

### Task 11: Update All Remaining `user.roles` Gating to Use `activeRoles`

**Files:**
- Modify: `frontend/src/routes/_authed/dashboard.tsx:57-61,270`
- Modify: `frontend/src/routes/_authed/software/$id.tsx:97-98`
- Modify: `frontend/src/routes/_authed/settings/index.tsx:97`
- Modify: `frontend/src/routes/_authed/admin/security-profiles.tsx:128`
- Modify: `frontend/src/routes/_authed/admin/tenants/$id.tsx:15`
- Modify: `frontend/src/routes/_authed/admin/teams/index.tsx:35`
- Modify: `frontend/src/routes/_authed/admin/sources.tsx:50`
- Modify: `frontend/src/routes/_authed/admin/index.tsx:72`

All `user.roles.includes(...)` and `user.roles.some(...)` calls must change to use `user.activeRoles` (with Stakeholder always included in the effective set).

- [ ] **Step 1: Apply the pattern replacement across all files**

The pattern is:

```typescript
// BEFORE:
user.roles.includes('SecurityManager')

// AFTER:
(user.activeRoles ?? []).includes('SecurityManager')
```

For `.some()` checks like in `admin/index.tsx`:

```typescript
// BEFORE:
user.roles.some((role) => area.roles.includes(role as ...))

// AFTER:
[...(user.activeRoles ?? []), 'Stakeholder'].some((role) => area.roles.includes(role as ...))
```

Apply this to every file listed above.

- [ ] **Step 2: Verify frontend typechecks and lint**

Run: `cd frontend && npm run typecheck && npm run lint`
Expected: No errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/routes/
git commit -m "feat: update all role gating to use activeRoles

All user.roles.includes() checks changed to user.activeRoles to
enforce role activation across dashboard, admin, and settings views."
```

---

### Task 12: Handle Tenant Switching — Clear Active Roles

**Files:**
- Modify: `frontend/src/api/roles.functions.ts` (add `clearActiveRoles` server function)
- Modify: `frontend/src/components/layout/TopNav.tsx:223-231` (call on tenant switch)

Tenant switching is currently client-side only (sets a cookie via `TenantScopeProvider`). Since `activeRoles` is stored in the server session, we need a server function to clear it.

- [ ] **Step 1: Add `clearActiveRoles` server function**

In `frontend/src/api/roles.functions.ts`, add:

```typescript
export const clearActiveRoles = createServerFn({ method: 'POST' })
  .handler(async () => {
    const session = await getSession()
    session.activeRoles = []
    await session.save()
    return { activeRoles: [] }
  })
```

- [ ] **Step 2: Call on tenant switch in TopNav**

In `frontend/src/components/layout/TopNav.tsx`, modify the `onSelectTenant` handler (lines 223-231):

```tsx
import { clearActiveRoles } from '@/api/roles.functions'

// In the onSelectTenant callback:
onSelectTenant={(tenantId) => {
  if (tenantId === selectedTenantId) {
    return;
  }

  setSelectedTenantId(tenantId);
  void clearActiveRoles();
  void queryClient.invalidateQueries();
  void router.invalidate();
}}
```

- [ ] **Step 3: Verify frontend typechecks**

Run: `cd frontend && npm run typecheck`
Expected: No type errors

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/roles.functions.ts frontend/src/components/layout/TopNav.tsx
git commit -m "feat: clear activeRoles on tenant switch

Different tenants may have different role assignments. Active roles
reset when switching to prevent stale elevated privileges."
```

---

### Task 13: Integration Verification

- [ ] **Step 1: Run all backend tests**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: All tests pass

- [ ] **Step 2: Run all frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm test`
Expected: All pass

- [ ] **Step 3: Run full build**

Run: `dotnet build PatchHound.slnx && cd frontend && npm run build`
Expected: Both build successfully

- [ ] **Step 4: Manual smoke test checklist**

Verify the following in a running environment:

1. Log in → only Stakeholder-level access (limited sidebar, read-only views)
2. Open user menu → "Activate Roles..." item visible
3. Click "Activate Roles..." → dialog opens showing assigned roles
4. Toggle SecurityManager on → toast confirms, sidebar shows Remediation
5. Toggle SecurityManager off → toast confirms, Remediation disappears
6. Refresh page → active roles persist within session
7. Log out and back in → all elevated roles reset
8. Switch tenants → elevated roles clear
9. Attempt header spoofing (curl with X-Active-Roles for unassigned role) → 403

- [ ] **Step 5: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix: integration fixes for role activation"
```
