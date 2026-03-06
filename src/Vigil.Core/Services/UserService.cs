using Vigil.Core.Common;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;

namespace Vigil.Core.Services;

public class UserService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UserService(IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<User>> InviteUserAsync(
        string email,
        string displayName,
        string entraObjectId,
        CancellationToken ct
    )
    {
        var existing = await _userRepository.GetByEmailAsync(email, ct);
        if (existing is not null)
            return Result<User>.Failure("A user with this email already exists");

        var user = User.Create(email, displayName, entraObjectId);
        await _userRepository.AddAsync(user, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<User>.Success(user);
    }

    public async Task<Result<UserTenantRole>> AssignRoleAsync(
        Guid userId,
        Guid tenantId,
        RoleName role,
        CancellationToken ct
    )
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return Result<UserTenantRole>.Failure("User not found");

        var existingRole = await _userRepository.GetRoleAsync(userId, tenantId, ct);
        if (existingRole is not null)
        {
            _userRepository.RemoveRole(existingRole);
        }

        var newRole = UserTenantRole.Create(userId, tenantId, role);
        await _userRepository.AddRoleAsync(newRole, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<UserTenantRole>.Success(newRole);
    }

    public async Task<Result<bool>> RemoveRoleAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return Result<bool>.Failure("User not found");

        var existingRole = await _userRepository.GetRoleAsync(userId, tenantId, ct);
        if (existingRole is null)
            return Result<bool>.Failure("Role not found");

        _userRepository.RemoveRole(existingRole);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
