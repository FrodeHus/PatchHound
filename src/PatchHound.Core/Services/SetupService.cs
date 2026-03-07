using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Core.Services;

public class SetupService : ISetupService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetupService(
        ITenantRepository tenantRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork
    )
    {
        _tenantRepository = tenantRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> IsInitializedAsync(CancellationToken ct)
    {
        var tenants = await _tenantRepository.GetAllAsync(ct);
        return tenants.Count > 0;
    }

    public async Task<Result<Tenant>> CompleteSetupAsync(SetupRequest request, CancellationToken ct)
    {
        if (await IsInitializedAsync(ct))
        {
            return Result<Tenant>.Failure("Application is already initialized");
        }

        if (string.IsNullOrWhiteSpace(request.TenantName))
        {
            return Result<Tenant>.Failure("Tenant name is required");
        }

        if (string.IsNullOrWhiteSpace(request.EntraTenantId))
        {
            return Result<Tenant>.Failure("Entra tenant ID is required");
        }

        if (string.IsNullOrWhiteSpace(request.AdminEmail))
        {
            return Result<Tenant>.Failure("Admin email is required");
        }

        if (string.IsNullOrWhiteSpace(request.AdminDisplayName))
        {
            return Result<Tenant>.Failure("Admin display name is required");
        }

        if (string.IsNullOrWhiteSpace(request.AdminEntraObjectId))
        {
            return Result<Tenant>.Failure("Admin Entra object ID is required");
        }

        var existingUser = await _userRepository.GetByEmailAsync(request.AdminEmail, ct);
        if (existingUser is not null)
        {
            return Result<Tenant>.Failure("Admin email already exists");
        }

        var tenant = Tenant.Create(
            request.TenantName.Trim(),
            request.EntraTenantId.Trim(),
            string.IsNullOrWhiteSpace(request.TenantSettings) ? "{}" : request.TenantSettings
        );

        var user = User.Create(
            request.AdminEmail.Trim(),
            request.AdminDisplayName.Trim(),
            request.AdminEntraObjectId.Trim()
        );

        var role = UserTenantRole.Create(user.Id, tenant.Id, RoleName.GlobalAdmin);

        await _tenantRepository.AddAsync(tenant, ct);
        await _userRepository.AddAsync(user, ct);
        await _userRepository.AddRoleAsync(role, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Tenant>.Success(tenant);
    }
}
