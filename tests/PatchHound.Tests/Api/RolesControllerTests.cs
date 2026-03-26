using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class RolesControllerTests : IDisposable
{
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly AuditLogWriter _auditLogWriter;
    private readonly RolesController _controller;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public RolesControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(_tenantContext));
        _auditLogWriter = new AuditLogWriter(_dbContext, _tenantContext);
        _controller = new RolesController(_tenantContext, _auditLogWriter, _dbContext);

        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
    }

    [Fact]
    public async Task Activate_ValidRoles_Returns200WithRoles()
    {
        _tenantContext.GetRolesForTenant(_tenantId)
            .Returns(new List<string> { "Stakeholder", "SecurityManager", "Auditor" });

        var result = await _controller.Activate(
            new RolesController.ActivateRequest { Roles = new[] { "SecurityManager" } },
            CancellationToken.None
        );

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<RolesController.ActivateResponse>().Subject;
        response.Roles.Should().Contain("SecurityManager");
    }

    [Fact]
    public async Task Activate_UnassignedRole_Returns403()
    {
        _tenantContext.GetRolesForTenant(_tenantId)
            .Returns(new List<string> { "Stakeholder" });

        var result = await _controller.Activate(
            new RolesController.ActivateRequest { Roles = new[] { "SecurityManager" } },
            CancellationToken.None
        );

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Activate_InvalidRoleName_Returns400()
    {
        _tenantContext.GetRolesForTenant(_tenantId)
            .Returns(new List<string> { "Stakeholder" });

        var result = await _controller.Activate(
            new RolesController.ActivateRequest { Roles = new[] { "NotARealRole" } },
            CancellationToken.None
        );

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value?.ToString().Should().Contain("NotARealRole");
    }

    [Fact]
    public async Task Activate_EmptyArray_DeactivatesAll()
    {
        _tenantContext.GetRolesForTenant(_tenantId)
            .Returns(new List<string> { "Stakeholder", "SecurityManager" });

        var result = await _controller.Activate(
            new RolesController.ActivateRequest { Roles = Array.Empty<string>() },
            CancellationToken.None
        );

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<RolesController.ActivateResponse>().Subject;
        response.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task Activate_WritesAuditLogForNewActivation()
    {
        _tenantContext.GetRolesForTenant(_tenantId)
            .Returns(new List<string> { "Stakeholder", "SecurityManager" });

        await _controller.Activate(
            new RolesController.ActivateRequest { Roles = new[] { "SecurityManager" } },
            CancellationToken.None
        );

        var auditEntries = await _dbContext.AuditLogEntries.ToListAsync();
        auditEntries.Should().ContainSingle();
        auditEntries[0].Action.Should().Be(AuditAction.Activated);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
