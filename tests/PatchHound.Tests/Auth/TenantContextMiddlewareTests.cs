using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Api.Auth;
using PatchHound.Api.Middleware;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.Auth;

public class TenantContextMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_SingleAccessibleTenantWithoutHeader_SelectsThatTenant()
    {
        var tenantContext = new TenantContext();
        var tenant = Tenant.Create("Tenant A", "entra-tenant-a");
        var user = User.Create("user@example.com", "User", Guid.NewGuid().ToString());

        await using var dbContext = CreateDbContext(tenantContext);
        await dbContext.Tenants.AddAsync(tenant);
        await dbContext.Users.AddAsync(user);
        await dbContext.UserTenantRoles.AddAsync(
            UserTenantRole.Create(user.Id, tenant.Id, RoleName.SecurityManager)
        );
        await dbContext.SaveChangesAsync();

        var httpContext = CreateHttpContext(
            dbContext,
            tenantContext,
            user.EntraObjectId,
            roles: []
        );

        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext);

        tenantContext.AccessibleTenantIds.Should().ContainSingle().Which.Should().Be(tenant.Id);
        tenantContext.CurrentTenantId.Should().Be(tenant.Id);
        tenantContext
            .GetRolesForTenant(tenant.Id)
            .Should()
            .Contain(RoleName.SecurityManager.ToString());
    }

    [Fact]
    public async Task InvokeAsync_MultiTenantUserWithAuthorizedHeader_SelectsRequestedTenant()
    {
        var tenantContext = new TenantContext();
        var tenantA = Tenant.Create("Tenant A", "entra-tenant-a");
        var tenantB = Tenant.Create("Tenant B", "entra-tenant-b");
        var user = User.Create("user@example.com", "User", Guid.NewGuid().ToString());

        await using var dbContext = CreateDbContext(tenantContext);
        await dbContext.Tenants.AddRangeAsync(tenantA, tenantB);
        await dbContext.Users.AddAsync(user);
        await dbContext.UserTenantRoles.AddRangeAsync(
            UserTenantRole.Create(user.Id, tenantA.Id, RoleName.SecurityAnalyst),
            UserTenantRole.Create(user.Id, tenantB.Id, RoleName.AssetOwner)
        );
        await dbContext.SaveChangesAsync();

        var httpContext = CreateHttpContext(
            dbContext,
            tenantContext,
            user.EntraObjectId,
            roles: [],
            tenantHeader: tenantB.Id.ToString()
        );

        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext);

        tenantContext.AccessibleTenantIds.Should().BeEquivalentTo([tenantA.Id, tenantB.Id]);
        tenantContext.CurrentTenantId.Should().Be(tenantB.Id);
        tenantContext
            .GetRolesForTenant(tenantB.Id)
            .Should()
            .Contain(RoleName.AssetOwner.ToString());
    }

    [Fact]
    public async Task InvokeAsync_NormalizedTokenRoleMapsToInternalTenantByTid()
    {
        var tenantContext = new TenantContext();
        var tenant = Tenant.Create("Tenant A", "entra-tenant-a");

        await using var dbContext = CreateDbContext(tenantContext);
        await dbContext.Tenants.AddAsync(tenant);
        await dbContext.SaveChangesAsync();

        var httpContext = CreateHttpContext(
            dbContext,
            tenantContext,
            Guid.NewGuid().ToString(),
            roles: ["Tenant.Admin"],
            tokenTenantId: tenant.EntraTenantId
        );

        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext);

        tenantContext.AccessibleTenantIds.Should().ContainSingle().Which.Should().Be(tenant.Id);
        tenantContext.CurrentTenantId.Should().Be(tenant.Id);
        tenantContext
            .GetRolesForTenant(tenant.Id)
            .Should()
            .Contain(RoleName.GlobalAdmin.ToString());
    }

    [Fact]
    public async Task InvokeAsync_UnauthorizedHeaderDoesNotSetCurrentTenant()
    {
        var tenantContext = new TenantContext();
        var tenantA = Tenant.Create("Tenant A", "entra-tenant-a");
        var tenantB = Tenant.Create("Tenant B", "entra-tenant-b");
        var user = User.Create("user@example.com", "User", Guid.NewGuid().ToString());

        await using var dbContext = CreateDbContext(tenantContext);
        await dbContext.Tenants.AddRangeAsync(tenantA, tenantB);
        await dbContext.Users.AddAsync(user);
        await dbContext.UserTenantRoles.AddRangeAsync(
            UserTenantRole.Create(user.Id, tenantA.Id, RoleName.SecurityAnalyst),
            UserTenantRole.Create(user.Id, tenantB.Id, RoleName.AssetOwner)
        );
        await dbContext.SaveChangesAsync();

        var httpContext = CreateHttpContext(
            dbContext,
            tenantContext,
            user.EntraObjectId,
            roles: [],
            tenantHeader: Guid.NewGuid().ToString()
        );

        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext);

        tenantContext.AccessibleTenantIds.Should().BeEquivalentTo([tenantA.Id, tenantB.Id]);
        tenantContext.CurrentTenantId.Should().BeNull();
    }

    private static PatchHoundDbContext CreateDbContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return new PatchHoundDbContext(options, services.BuildServiceProvider());
    }

    private static HttpContext CreateHttpContext(
        PatchHoundDbContext dbContext,
        TenantContext tenantContext,
        string objectId,
        IReadOnlyList<string> roles,
        string? tokenTenantId = null,
        string? tenantHeader = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(dbContext);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(
                new ClaimsIdentity(BuildClaims(objectId, roles, tokenTenantId), "test")
            ),
        };

        if (!string.IsNullOrWhiteSpace(tenantHeader))
        {
            httpContext.Request.Headers["X-Tenant-Id"] = tenantHeader;
        }

        return httpContext;
    }

    private static IEnumerable<Claim> BuildClaims(
        string objectId,
        IReadOnlyList<string> roles,
        string? tokenTenantId
    )
    {
        yield return new Claim("oid", objectId);

        if (!string.IsNullOrWhiteSpace(tokenTenantId))
        {
            yield return new Claim("tid", tokenTenantId);
        }

        foreach (var role in roles)
        {
            yield return new Claim("roles", role);
        }
    }
}
