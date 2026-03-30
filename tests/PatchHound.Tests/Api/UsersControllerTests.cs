using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Admin;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class UsersControllerTests : IDisposable
{
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly UsersController _controller;
    private readonly Guid _tenantId = Guid.NewGuid();

    public UsersControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.IsInternalUser.Returns(false);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(_tenantContext));
        var userRepository = Substitute.For<IUserRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var userService = new UserService(userRepository, unitOfWork);
        var teamMembershipRuleService = new TeamMembershipRuleService(
            _dbContext,
            new TeamMembershipRuleFilterBuilder()
        );

        _controller = new UsersController(
            _dbContext,
            userService,
            teamMembershipRuleService,
            _tenantContext
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    [Fact]
    public async Task Update_CustomerAdminCannotAssignInternalRoles()
    {
        var tenant = Tenant.Create("Tenant A", "entra-tenant-a");
        _tenantContext.CurrentTenantId.Returns(tenant.Id);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        var customerUser = CreateCustomerUser("customer@example.com", "Customer User");

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.Users.AddAsync(customerUser);
        await _dbContext.UserTenantRoles.AddAsync(
            UserTenantRole.Create(customerUser.Id, tenant.Id, RoleName.CustomerViewer)
        );
        await _dbContext.SaveChangesAsync();

        var result = await _controller.Update(
            customerUser.Id,
            new UpdateUserRequest(
                customerUser.DisplayName,
                customerUser.Email,
                customerUser.Company,
                customerUser.IsEnabled,
                UserAccessScope.Customer.ToString(),
                [RoleName.GlobalAdmin.ToString()],
                [],
                []
            ),
            CancellationToken.None
        );

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<ProblemDetails>()
            .Which.Title.Should().Contain("only assign customer roles");
    }

    [Fact]
    public async Task Update_CustomerAdminCannotPromoteUserToInternalScope()
    {
        var tenant = Tenant.Create("Tenant A", "entra-tenant-a");
        _tenantContext.CurrentTenantId.Returns(tenant.Id);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        var customerUser = CreateCustomerUser("customer@example.com", "Customer User");

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.Users.AddAsync(customerUser);
        await _dbContext.UserTenantRoles.AddAsync(
            UserTenantRole.Create(customerUser.Id, tenant.Id, RoleName.CustomerViewer)
        );
        await _dbContext.SaveChangesAsync();

        var result = await _controller.Update(
            customerUser.Id,
            new UpdateUserRequest(
                customerUser.DisplayName,
                customerUser.Email,
                customerUser.Company,
                customerUser.IsEnabled,
                UserAccessScope.Internal.ToString(),
                [RoleName.CustomerViewer.ToString()],
                [],
                []
            ),
            CancellationToken.None
        );

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task List_CustomerAdminDoesNotSeeInternalUsers()
    {
        var tenant = Tenant.Create("Tenant A", "entra-tenant-a");
        _tenantContext.CurrentTenantId.Returns(tenant.Id);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        var internalUser = User.Create("internal@example.com", "Internal User", Guid.NewGuid().ToString());
        var customerUser = CreateCustomerUser("customer@example.com", "Customer User");

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.Users.AddRangeAsync(internalUser, customerUser);
        await _dbContext.UserTenantRoles.AddRangeAsync(
            UserTenantRole.Create(internalUser.Id, tenant.Id, RoleName.SecurityManager),
            UserTenantRole.Create(customerUser.Id, tenant.Id, RoleName.CustomerViewer)
        );
        await _dbContext.SaveChangesAsync();

        var actionResult = await _controller.List(
            null,
            null,
            null,
            null,
            new PaginationQuery(1, 25),
            CancellationToken.None
        );

        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PagedResponse<UserListItemDto>>().Subject;

        response.Items.Should().ContainSingle();
        response.Items[0].Id.Should().Be(customerUser.Id);
    }

    private static User CreateCustomerUser(string email, string displayName)
    {
        var user = User.Create(email, displayName, Guid.NewGuid().ToString());
        user.SetAccessScope(UserAccessScope.Customer);
        return user;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
