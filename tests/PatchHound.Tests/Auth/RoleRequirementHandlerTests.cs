using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using PatchHound.Api.Auth;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using System.Security.Claims;

namespace PatchHound.Tests.Auth;

public class RoleRequirementHandlerTests
{
    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly DefaultHttpContext _httpContext;
    private readonly RoleRequirementHandler _handler;

    public RoleRequirementHandlerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _httpContext = new DefaultHttpContext();
        _httpContextAccessor.HttpContext.Returns(_httpContext);
        _handler = new RoleRequirementHandler(_tenantContext, _httpContextAccessor);
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
        _tenantContext.CurrentTenantId.Returns(tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenantId });
        _tenantContext.GetRolesForTenant(tenantId)
            .Returns(new List<string> { "Stakeholder", "SecurityManager" });

        var requirement = new RoleRequirement(RoleName.Stakeholder);
        var context = CreateContext(requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task NoHeader_NonStakeholderRoleFails()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.CurrentTenantId.Returns(tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenantId });
        _tenantContext.GetRolesForTenant(tenantId)
            .Returns(new List<string> { "Stakeholder", "SecurityManager" });

        var requirement = new RoleRequirement(RoleName.SecurityManager);
        var context = CreateContext(requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task WithHeader_ActivatedRolePasses()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.CurrentTenantId.Returns(tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenantId });
        _tenantContext.GetRolesForTenant(tenantId)
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
        _tenantContext.CurrentTenantId.Returns(tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenantId });
        _tenantContext.GetRolesForTenant(tenantId)
            .Returns(new List<string> { "Stakeholder" });

        _httpContext.Request.Headers["X-Active-Roles"] = "SecurityManager";

        var requirement = new RoleRequirement(RoleName.SecurityManager);
        var context = CreateContext(requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task EntraClaimRoles_RequireActivation()
    {
        var claims = new[] { new Claim("roles", "GlobalAdmin") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        var tenantId = Guid.NewGuid();
        _tenantContext.CurrentTenantId.Returns(tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenantId });
        _tenantContext.GetRolesForTenant(tenantId)
            .Returns(new List<string> { "Stakeholder", "GlobalAdmin" });

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
        _tenantContext.CurrentTenantId.Returns(tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenantId });
        _tenantContext.GetRolesForTenant(tenantId)
            .Returns(new List<string> { "Stakeholder", "GlobalAdmin" });

        _httpContext.Request.Headers["X-Active-Roles"] = "GlobalAdmin";

        var requirement = new RoleRequirement(RoleName.GlobalAdmin);
        var context = CreateContext(requirement, user);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }
}
