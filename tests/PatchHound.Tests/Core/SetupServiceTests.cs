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
    private readonly IRepository<TenantSourceConfiguration> _tenantSourceRepository;
    private readonly IRepository<EnrichmentSourceConfiguration> _enrichmentSourceRepository;
    private readonly IRepository<TenantSlaConfiguration> _tenantSlaRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SetupService _service;

    public SetupServiceTests()
    {
        _tenantRepository = Substitute.For<ITenantRepository>();
        _userRepository = Substitute.For<IUserRepository>();
        _tenantSourceRepository = Substitute.For<IRepository<TenantSourceConfiguration>>();
        _enrichmentSourceRepository = Substitute.For<IRepository<EnrichmentSourceConfiguration>>();
        _tenantSlaRepository = Substitute.For<IRepository<TenantSlaConfiguration>>();
        _unitOfWork = Substitute.For<IUnitOfWork>();

        _unitOfWork
            .ExecuteResilientAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var operation = callInfo.Arg<Func<CancellationToken, Task>>();
                return operation(callInfo.Arg<CancellationToken>());
            });

        var transaction = Substitute.For<IUnitOfWorkTransaction>();
        _unitOfWork
            .BeginTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(transaction);

        _service = new SetupService(
            _tenantRepository,
            _userRepository,
            _tenantSourceRepository,
            _enrichmentSourceRepository,
            _tenantSlaRepository,
            _unitOfWork
        );
    }

    [Fact]
    public async Task IsInitializedAsync_WhenNoTenants_ReturnsFalse()
    {
        _tenantRepository.AnyExistUnfilteredAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await _service.IsInitializedAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsInitializedAsync_WhenTenantsExist_ReturnsTrue()
    {
        _tenantRepository.AnyExistUnfilteredAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.IsInitializedAsync(CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RequiresSetupForTenantAsync_WhenAppNotInitialized_ReturnsTrue()
    {
        _tenantRepository.AnyExistUnfilteredAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await _service.RequiresSetupForTenantAsync(
            "entra-tenant",
            CancellationToken.None
        );

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RequiresSetupForTenantAsync_WhenTenantMissing_ReturnsTrue()
    {
        _tenantRepository.AnyExistUnfilteredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _tenantRepository
            .ExistsByEntraTenantIdUnfilteredAsync("entra-tenant", Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _service.RequiresSetupForTenantAsync(
            "entra-tenant",
            CancellationToken.None
        );

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RequiresSetupForTenantAsync_WhenTenantExists_ReturnsFalse()
    {
        _tenantRepository.AnyExistUnfilteredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _tenantRepository
            .ExistsByEntraTenantIdUnfilteredAsync("entra-tenant", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _service.RequiresSetupForTenantAsync(
            "entra-tenant",
            CancellationToken.None
        );

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteSetupAsync_WhenNotInitialized_CreatesTenantAndAdminRole()
    {
        _tenantRepository.AnyExistUnfilteredAsync(Arg.Any<CancellationToken>()).Returns(false);
        _userRepository
            .GetByEntraObjectIdAsync("entra-admin-id", Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepository
            .GetByEmailAsync("admin@example.com", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var request = new SetupRequest(
            "Acme",
            "entra-tenant",
            "admin@example.com",
            "Admin",
            "entra-admin-id"
        );

        var result = await _service.CompleteSetupAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Acme");
        await _tenantRepository
            .Received(1)
            .AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _tenantSourceRepository
            .Received(1)
            .AddAsync(Arg.Any<TenantSourceConfiguration>(), Arg.Any<CancellationToken>());
        await _enrichmentSourceRepository
            .Received(1)
            .AddAsync(Arg.Any<EnrichmentSourceConfiguration>(), Arg.Any<CancellationToken>());
        await _tenantSlaRepository
            .Received(1)
            .AddAsync(Arg.Any<TenantSlaConfiguration>(), Arg.Any<CancellationToken>());
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
        _tenantRepository.AnyExistUnfilteredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _tenantRepository
            .ExistsByEntraTenantIdUnfilteredAsync("entra-tenant", Arg.Any<CancellationToken>())
            .Returns(true);

        var request = new SetupRequest(
            "Acme",
            "entra-tenant",
            "admin@example.com",
            "Admin",
            "entra-admin-id"
        );

        var result = await _service.CompleteSetupAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already initialized");
    }

    [Fact]
    public async Task CompleteSetupAsync_WhenOtherTenantsExistButCurrentTenantMissing_CreatesTenantWithoutGlobalEnrichmentDefaults()
    {
        _tenantRepository.AnyExistUnfilteredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _tenantRepository
            .ExistsByEntraTenantIdUnfilteredAsync("entra-tenant", Arg.Any<CancellationToken>())
            .Returns(false);
        _userRepository
            .GetByEntraObjectIdAsync("entra-admin-id", Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepository
            .GetByEmailAsync("admin@example.com", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var request = new SetupRequest(
            "Acme",
            "entra-tenant",
            "admin@example.com",
            "Admin",
            "entra-admin-id"
        );

        var result = await _service.CompleteSetupAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _enrichmentSourceRepository
            .DidNotReceive()
            .AddAsync(Arg.Any<EnrichmentSourceConfiguration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteSetupAsync_WhenGlobalEnrichmentDefaultAlreadyExists_DoesNotInsertDuplicate()
    {
        _tenantRepository.AnyExistUnfilteredAsync(Arg.Any<CancellationToken>()).Returns(false);
        _tenantRepository
            .ExistsByEntraTenantIdUnfilteredAsync("entra-tenant", Arg.Any<CancellationToken>())
            .Returns(false);
        _userRepository
            .GetByEntraObjectIdAsync("entra-admin-id", Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _userRepository
            .GetByEmailAsync("admin@example.com", Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _enrichmentSourceRepository
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(
                new List<EnrichmentSourceConfiguration>
                {
                    EnrichmentSourceConfiguration.Create(
                        "nvd",
                        "NVD API",
                        false,
                        apiBaseUrl: "https://services.nvd.nist.gov"
                    ),
                }
            );

        var request = new SetupRequest(
            "Acme",
            "entra-tenant",
            "admin@example.com",
            "Admin",
            "entra-admin-id"
        );

        var result = await _service.CompleteSetupAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _enrichmentSourceRepository
            .DidNotReceive()
            .AddAsync(
                Arg.Is<EnrichmentSourceConfiguration>(source => source.SourceKey == "nvd"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task CompleteSetupAsync_WhenUserAlreadyExistsByEntraObjectId_ReusesUser()
    {
        _tenantRepository.AnyExistUnfilteredAsync(Arg.Any<CancellationToken>()).Returns(false);

        var existingUser = User.Create("admin@example.com", "Admin", "entra-admin-id");
        _userRepository
            .GetByEntraObjectIdAsync("entra-admin-id", Arg.Any<CancellationToken>())
            .Returns(existingUser);

        var request = new SetupRequest(
            "Acme",
            "entra-tenant",
            "admin@example.com",
            "Admin",
            "entra-admin-id"
        );

        var result = await _service.CompleteSetupAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _userRepository
            .DidNotReceive()
            .AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _userRepository
            .Received(1)
            .AddRoleAsync(
                Arg.Is<UserTenantRole>(role =>
                    role.UserId == existingUser.Id && role.Role == RoleName.GlobalAdmin
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
