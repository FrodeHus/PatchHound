using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;

namespace PatchHound.Core.Services;

public class TeamService
{
    private readonly ITeamRepository _teamRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public TeamService(
        ITeamRepository teamRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork
    )
    {
        _teamRepository = teamRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Team>> CreateTeamAsync(
        Guid tenantId,
        string name,
        CancellationToken ct
    )
    {
        var team = Team.Create(tenantId, name);
        await _teamRepository.AddAsync(team, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Team>.Success(team);
    }

    public async Task<Result<Team>> AddMemberAsync(Guid teamId, Guid userId, CancellationToken ct)
    {
        var team = await _teamRepository.GetByIdWithMembersAsync(teamId, ct);
        if (team is null)
            return Result<Team>.Failure("Team not found");

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return Result<Team>.Failure("User not found");

        team.AddMember(user);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Team>.Success(team);
    }

    public async Task<Result<Team>> RemoveMemberAsync(
        Guid teamId,
        Guid userId,
        CancellationToken ct
    )
    {
        var team = await _teamRepository.GetByIdWithMembersAsync(teamId, ct);
        if (team is null)
            return Result<Team>.Failure("Team not found");

        team.RemoveMember(userId);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Team>.Success(team);
    }
}
