using FluentAssertions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;

namespace PatchHound.Tests.Core;

public class TeamServiceTests
{
    private readonly ITeamRepository _teamRepo;
    private readonly IUserRepository _userRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TeamService _service;
    private readonly Guid _tenantId = Guid.NewGuid();

    public TeamServiceTests()
    {
        _teamRepo = Substitute.For<ITeamRepository>();
        _userRepo = Substitute.For<IUserRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _service = new TeamService(_teamRepo, _userRepo, _unitOfWork);
    }

    [Fact]
    public async Task CreateTeam_CreatesTeam_Successfully()
    {
        var result = await _service.CreateTeamAsync(
            _tenantId,
            "Security Team",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Security Team");
        result.Value.TenantId.Should().Be(_tenantId);
        await _teamRepo.Received(1).AddAsync(Arg.Any<Team>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddMember_AddsUser_Successfully()
    {
        var team = Team.Create(_tenantId, "Security Team");
        var user = User.Create("test@example.com", "Test User", "entra-oid-123");

        _teamRepo.GetByIdWithMembersAsync(team.Id, Arg.Any<CancellationToken>()).Returns(team);
        _userRepo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _service.AddMemberAsync(team.Id, user.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Members.Should().HaveCount(1);
        result.Value.Members.First().UserId.Should().Be(user.Id);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddMember_TeamNotFound_ReturnsFailure()
    {
        _teamRepo
            .GetByIdWithMembersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Team?)null);

        var result = await _service.AddMemberAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Team not found");
    }

    [Fact]
    public async Task AddMember_UserNotFound_ReturnsFailure()
    {
        var team = Team.Create(_tenantId, "Security Team");
        _teamRepo.GetByIdWithMembersAsync(team.Id, Arg.Any<CancellationToken>()).Returns(team);
        _userRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _service.AddMemberAsync(team.Id, Guid.NewGuid(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("User not found");
    }

    [Fact]
    public async Task AddMember_DuplicateMember_Deduplicates()
    {
        var team = Team.Create(_tenantId, "Security Team");
        var user = User.Create("test@example.com", "Test User", "entra-oid-123");

        _teamRepo.GetByIdWithMembersAsync(team.Id, Arg.Any<CancellationToken>()).Returns(team);
        _userRepo.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        // Add member twice
        await _service.AddMemberAsync(team.Id, user.Id, CancellationToken.None);
        var result = await _service.AddMemberAsync(team.Id, user.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Members.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveMember_RemovesMember_Successfully()
    {
        var team = Team.Create(_tenantId, "Security Team");
        var user = User.Create("test@example.com", "Test User", "entra-oid-123");

        // Add a member first
        team.AddMember(user);
        team.Members.Should().HaveCount(1);

        _teamRepo.GetByIdWithMembersAsync(team.Id, Arg.Any<CancellationToken>()).Returns(team);

        var result = await _service.RemoveMemberAsync(team.Id, user.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Members.Should().BeEmpty();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveMember_TeamNotFound_ReturnsFailure()
    {
        _teamRepo
            .GetByIdWithMembersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Team?)null);

        var result = await _service.RemoveMemberAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Team not found");
    }
}
