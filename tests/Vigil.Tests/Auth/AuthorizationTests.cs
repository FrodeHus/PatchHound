using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Vigil.Api.Auth;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Data;

namespace Vigil.Tests.Auth;

public class AuthorizationTests : IDisposable
{
    private readonly VigilDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public AuthorizationTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });

        var options = new DbContextOptionsBuilder<VigilDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new VigilDbContext(options, _tenantContext);
    }

    [Fact]
    public async Task SecurityAnalyst_CanAdjustSeverity()
    {
        await SeedUserRole(RoleName.SecurityAnalyst);
        var handler = new RoleRequirementHandler(_dbContext, _tenantContext);
        var requirement = new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst);
        var context = new AuthorizationHandlerContext(new[] { requirement }, CreateClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task AssetOwner_CannotAdjustSeverity()
    {
        await SeedUserRole(RoleName.AssetOwner);
        var handler = new RoleRequirementHandler(_dbContext, _tenantContext);
        var requirement = new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst);
        var context = new AuthorizationHandlerContext(new[] { requirement }, CreateClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Stakeholder_CanViewVulnerabilities()
    {
        await SeedUserRole(RoleName.Stakeholder);
        var handler = new RoleRequirementHandler(_dbContext, _tenantContext);
        var requirement = new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst,
            RoleName.AssetOwner, RoleName.Stakeholder, RoleName.Auditor);
        var context = new AuthorizationHandlerContext(new[] { requirement }, CreateClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Stakeholder_CannotAddComments()
    {
        await SeedUserRole(RoleName.Stakeholder);
        var handler = new RoleRequirementHandler(_dbContext, _tenantContext);
        var requirement = new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst, RoleName.AssetOwner);
        var context = new AuthorizationHandlerContext(new[] { requirement }, CreateClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task GlobalAdmin_CanDoEverything()
    {
        await SeedUserRole(RoleName.GlobalAdmin);
        var handler = new RoleRequirementHandler(_dbContext, _tenantContext);

        // Test with ManageUsers (most restrictive - only GlobalAdmin)
        var requirement = new RoleRequirement(RoleName.GlobalAdmin);
        var context = new AuthorizationHandlerContext(new[] { requirement }, CreateClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownUser_IsRejected()
    {
        _tenantContext.CurrentUserId.Returns(Guid.Empty);
        var handler = new RoleRequirementHandler(_dbContext, _tenantContext);
        var requirement = new RoleRequirement(RoleName.GlobalAdmin);
        var context = new AuthorizationHandlerContext(new[] { requirement }, CreateClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Auditor_CanViewAuditLogs()
    {
        await SeedUserRole(RoleName.Auditor);
        var handler = new RoleRequirementHandler(_dbContext, _tenantContext);
        var requirement = new RoleRequirement(RoleName.GlobalAdmin, RoleName.Auditor);
        var context = new AuthorizationHandlerContext(new[] { requirement }, CreateClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Auditor_CannotManageUsers()
    {
        await SeedUserRole(RoleName.Auditor);
        var handler = new RoleRequirementHandler(_dbContext, _tenantContext);
        var requirement = new RoleRequirement(RoleName.GlobalAdmin);
        var context = new AuthorizationHandlerContext(new[] { requirement }, CreateClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    private async Task SeedUserRole(RoleName role)
    {
        var userTenantRole = UserTenantRole.Create(_userId, _tenantId, role);
        _dbContext.UserTenantRoles.Add(userTenantRole);
        await _dbContext.SaveChangesAsync();
    }

    private ClaimsPrincipal CreateClaimsPrincipal()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("oid", _userId.ToString()));
        return new ClaimsPrincipal(identity);
    }

    public void Dispose() => _dbContext.Dispose();
}
