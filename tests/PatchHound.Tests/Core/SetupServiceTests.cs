using FluentAssertions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;

namespace PatchHound.Tests.Core;

public class SetupServiceTests
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SetupService _service;

    public SetupServiceTests()
    {
        _tenantRepository = Substitute.For<ITenantRepository>();
        _userRepository = Substitute.For<IUserRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _service = new SetupService(_tenantRepository, _userRepository, _unitOfWork);
    }

    [Fact]
    public async Task IsInitializedAsync_WhenNoTenants_ReturnsFalse()
    {
        _tenantRepository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Tenant>());

        var result = await _service.IsInitializedAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsInitializedAsync_WhenTenantsExist_ReturnsTrue()
    {
        _tenantRepository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Tenant.Create("Acme", "entra-tenant")]);

        var result = await _service.IsInitializedAsync(CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteSetupAsync_WhenNotInitialized_CreatesTenantAndAdminRole()
    {
        _tenantRepository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Tenant>());
        _userRepository.GetByEmailAsync("admin@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);

        var request = new SetupRequest(
            "Acme",
            "entra-tenant",
            "{}",
            "admin@example.com",
            "Admin",
            "entra-admin-id"
        );

        var result = await _service.CompleteSetupAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Acme");
        await _tenantRepository.Received(1).AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _userRepository.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _userRepository
            .Received(1)
            .AddRoleAsync(
                Arg.Is<UserTenantRole>(role => role.Role == RoleName.GlobalAdmin),
                Arg.Any<CancellationToken>()
            );
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteSetupAsync_WhenInitialized_ReturnsFailure()
    {
        _tenantRepository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Tenant.Create("Existing", "entra")]);

        var request = new SetupRequest(
            "Acme",
            "entra-tenant",
            "{}",
            "admin@example.com",
            "Admin",
            "entra-admin-id"
        );

        var result = await _service.CompleteSetupAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already initialized");
    }
}
