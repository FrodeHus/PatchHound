using FluentAssertions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;

namespace PatchHound.Tests.Core;

public class UserServiceTests
{
    private readonly IUserRepository _userRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserService _service;

    public UserServiceTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _service = new UserService(_userRepo, _unitOfWork);
    }

    [Fact]
    public async Task InviteUser_CreatesUser_Successfully()
    {
        _userRepo
            .GetByEmailAsync("test@example.com", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _service.InviteUserAsync(
            "test@example.com",
            "Test User",
            "entra-oid-123",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("test@example.com");
        result.Value.DisplayName.Should().Be("Test User");
        result.Value.EntraObjectId.Should().Be("entra-oid-123");
        await _userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteUser_DuplicateEmail_ReturnsFailure()
    {
        var existingUser = User.Create("test@example.com", "Existing", "entra-oid-existing");
        _userRepo
            .GetByEmailAsync("test@example.com", Arg.Any<CancellationToken>())
            .Returns(existingUser);

        var result = await _service.InviteUserAsync(
            "test@example.com",
            "Test User",
            "entra-oid-123",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
        await _userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignRole_AssignsNewRole_Successfully()
    {
        var user = User.Create("test@example.com", "Test User", "entra-oid-123");
        var tenantId = Guid.NewGuid();
        _userRepo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepo
            .GetRoleAsync(user.Id, tenantId, Arg.Any<CancellationToken>())
            .Returns((UserTenantRole?)null);

        var result = await _service.AssignRoleAsync(
            user.Id,
            tenantId,
            RoleName.SecurityAnalyst,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.Role.Should().Be(RoleName.SecurityAnalyst);
        await _userRepo
            .Received(1)
            .AddRoleAsync(Arg.Any<UserTenantRole>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignRole_ReplacesExistingRole_Successfully()
    {
        var user = User.Create("test@example.com", "Test User", "entra-oid-123");
        var tenantId = Guid.NewGuid();
        var existingRole = UserTenantRole.Create(user.Id, tenantId, RoleName.SecurityAnalyst);

        _userRepo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepo
            .GetRoleAsync(user.Id, tenantId, Arg.Any<CancellationToken>())
            .Returns(existingRole);

        var result = await _service.AssignRoleAsync(
            user.Id,
            tenantId,
            RoleName.SecurityManager,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be(RoleName.SecurityManager);
        _userRepo.Received(1).RemoveRole(existingRole);
        await _userRepo
            .Received(1)
            .AddRoleAsync(Arg.Any<UserTenantRole>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignRole_UserNotFound_ReturnsFailure()
    {
        _userRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _service.AssignRoleAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            RoleName.SecurityAnalyst,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RemoveRole_RemovesExistingRole_Successfully()
    {
        var user = User.Create("test@example.com", "Test User", "entra-oid-123");
        var tenantId = Guid.NewGuid();
        var existingRole = UserTenantRole.Create(user.Id, tenantId, RoleName.SecurityAnalyst);

        _userRepo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepo
            .GetRoleAsync(user.Id, tenantId, Arg.Any<CancellationToken>())
            .Returns(existingRole);

        var result = await _service.RemoveRoleAsync(user.Id, tenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _userRepo.Received(1).RemoveRole(existingRole);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveRole_UserNotFound_ReturnsFailure()
    {
        _userRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _service.RemoveRoleAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RemoveRole_RoleNotFound_ReturnsFailure()
    {
        var user = User.Create("test@example.com", "Test User", "entra-oid-123");
        var tenantId = Guid.NewGuid();

        _userRepo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepo
            .GetRoleAsync(user.Id, tenantId, Arg.Any<CancellationToken>())
            .Returns((UserTenantRole?)null);

        var result = await _service.RemoveRoleAsync(user.Id, tenantId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }
}
