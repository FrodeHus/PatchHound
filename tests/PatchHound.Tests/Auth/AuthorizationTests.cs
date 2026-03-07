using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using PatchHound.Api.Auth;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;

namespace PatchHound.Tests.Auth;

public class AuthorizationTests
{
    private readonly ITenantContext _tenantContext;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public AuthorizationTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentTenantId.Returns(_tenantId);
    }

    [Fact]
    public async Task SecurityAnalyst_CanAdjustSeverity()
    {
        SetupRolesForTenant(RoleName.SecurityAnalyst);
        var handler = new RoleRequirementHandler(_tenantContext);
        var requirement = new RoleRequirement(
            RoleName.GlobalAdmin,
            RoleName.SecurityManager,
            RoleName.SecurityAnalyst
        );
        var context = CreateAuthContext(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task AssetOwner_CannotAdjustSeverity()
    {
        SetupRolesForTenant(RoleName.AssetOwner);
        var handler = new RoleRequirementHandler(_tenantContext);
        var requirement = new RoleRequirement(
            RoleName.GlobalAdmin,
            RoleName.SecurityManager,
            RoleName.SecurityAnalyst
        );
        var context = CreateAuthContext(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Stakeholder_CanViewVulnerabilities()
    {
        SetupRolesForTenant(RoleName.Stakeholder);
        var handler = new RoleRequirementHandler(_tenantContext);
        var requirement = new RoleRequirement(
            RoleName.GlobalAdmin,
            RoleName.SecurityManager,
            RoleName.SecurityAnalyst,
            RoleName.AssetOwner,
            RoleName.Stakeholder,
            RoleName.Auditor
        );
        var context = CreateAuthContext(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Stakeholder_CannotAddComments()
    {
        SetupRolesForTenant(RoleName.Stakeholder);
        var handler = new RoleRequirementHandler(_tenantContext);
        var requirement = new RoleRequirement(
            RoleName.GlobalAdmin,
            RoleName.SecurityManager,
            RoleName.SecurityAnalyst,
            RoleName.AssetOwner
        );
        var context = CreateAuthContext(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task GlobalAdmin_CanDoEverything()
    {
        SetupRolesForTenant(RoleName.GlobalAdmin);
        var handler = new RoleRequirementHandler(_tenantContext);
        var requirement = new RoleRequirement(RoleName.GlobalAdmin);
        var context = CreateAuthContext(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownUser_IsRejected()
    {
        _tenantContext.CurrentUserId.Returns(Guid.Empty);
        var handler = new RoleRequirementHandler(_tenantContext);
        var requirement = new RoleRequirement(RoleName.GlobalAdmin);
        var context = CreateAuthContext(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task NormalizedEntraTenantAdminRole_SatisfiesGlobalAdminRequirement()
    {
        _tenantContext.CurrentUserId.Returns(Guid.Empty);
        _tenantContext.AccessibleTenantIds.Returns(Array.Empty<Guid>());

        var handler = new RoleRequirementHandler(_tenantContext);
        var requirement = new RoleRequirement(RoleName.GlobalAdmin);
        var context = CreateAuthContext(requirement, "Tenant.Admin");

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Auditor_CanViewAuditLogs()
    {
        SetupRolesForTenant(RoleName.Auditor);
        var handler = new RoleRequirementHandler(_tenantContext);
        var requirement = new RoleRequirement(RoleName.GlobalAdmin, RoleName.Auditor);
        var context = CreateAuthContext(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Auditor_CannotManageUsers()
    {
        SetupRolesForTenant(RoleName.Auditor);
        var handler = new RoleRequirementHandler(_tenantContext);
        var requirement = new RoleRequirement(RoleName.GlobalAdmin);
        var context = CreateAuthContext(requirement);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task MultiTenant_RolesCheckedForCurrentTenantOnly()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenantA, tenantB });
        _tenantContext.CurrentTenantId.Returns(tenantB);

        // User is SecurityAnalyst in tenantA, Stakeholder in tenantB
        _tenantContext.GetRolesForTenant(tenantA)
            .Returns(new List<string> { RoleName.SecurityAnalyst.ToString() });
        _tenantContext.GetRolesForTenant(tenantB)
            .Returns(new List<string> { RoleName.Stakeholder.ToString() });

        var handler = new RoleRequirementHandler(_tenantContext);
        var requirement = new RoleRequirement(
            RoleName.GlobalAdmin,
            RoleName.SecurityManager,
            RoleName.SecurityAnalyst
        );
        var context = CreateAuthContext(requirement);

        await handler.HandleAsync(context);

        // Should fail because current tenant is B and user only has Stakeholder there
        context.HasSucceeded.Should().BeFalse();
    }

    private void SetupRolesForTenant(RoleName role)
    {
        _tenantContext.GetRolesForTenant(_tenantId)
            .Returns(new List<string> { role.ToString() });
    }

    private AuthorizationHandlerContext CreateAuthContext(RoleRequirement requirement, params string[] roles)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("oid", _userId.ToString()));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim("roles", role));
        }
        var principal = new ClaimsPrincipal(identity);

        return new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            null
        );
    }
}
