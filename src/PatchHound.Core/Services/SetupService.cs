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
    private readonly IRepository<TenantSourceConfiguration> _tenantSourceRepository;
    private readonly IRepository<EnrichmentSourceConfiguration> _enrichmentSourceRepository;
    private readonly IRepository<TenantSlaConfiguration> _tenantSlaRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetupService(
        ITenantRepository tenantRepository,
        IUserRepository userRepository,
        IRepository<TenantSourceConfiguration> tenantSourceRepository,
        IRepository<EnrichmentSourceConfiguration> enrichmentSourceRepository,
        IRepository<TenantSlaConfiguration> tenantSlaRepository,
        IUnitOfWork unitOfWork
    )
    {
        _tenantRepository = tenantRepository;
        _userRepository = userRepository;
        _tenantSourceRepository = tenantSourceRepository;
        _enrichmentSourceRepository = enrichmentSourceRepository;
        _tenantSlaRepository = tenantSlaRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> IsInitializedAsync(CancellationToken ct)
    {
        return await _tenantRepository.AnyExistUnfilteredAsync(ct);
    }

    public async Task<Result<Tenant>> CompleteSetupAsync(SetupRequest request, CancellationToken ct)
    {
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

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);

        // Re-check inside the transaction to prevent TOCTOU race
        if (await IsInitializedAsync(ct))
        {
            return Result<Tenant>.Failure("Application is already initialized");
        }

        var existingUser = await _userRepository.GetByEntraObjectIdAsync(
            request.AdminEntraObjectId,
            ct
        );

        if (
            existingUser is null
            && await _userRepository.GetByEmailAsync(request.AdminEmail, ct) is not null
        )
        {
            return Result<Tenant>.Failure("Admin email already exists");
        }

        var tenant = Tenant.Create(request.TenantName.Trim(), request.EntraTenantId.Trim());

        var user =
            existingUser
            ?? User.Create(
                request.AdminEmail.Trim(),
                request.AdminDisplayName.Trim(),
                request.AdminEntraObjectId.Trim()
            );

        var role = UserTenantRole.Create(user.Id, tenant.Id, RoleName.GlobalAdmin);

        await _tenantRepository.AddAsync(tenant, ct);
        foreach (var source in TenantSourceDefaults.CreateDefaults(tenant.Id))
        {
            await _tenantSourceRepository.AddAsync(source, ct);
        }
        foreach (var source in EnrichmentSourceDefaults.CreateDefaults())
        {
            await _enrichmentSourceRepository.AddAsync(source, ct);
        }
        await _tenantSlaRepository.AddAsync(TenantSlaConfiguration.CreateDefault(tenant.Id), ct);

        if (existingUser is null)
        {
            await _userRepository.AddAsync(user, ct);
        }
        await _userRepository.AddRoleAsync(role, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Result<Tenant>.Success(tenant);
    }
}

internal static class TenantSourceDefaults
{
    public static IReadOnlyList<TenantSourceConfiguration> CreateDefaults(Guid tenantId)
    {
        return
        [
            TenantSourceConfiguration.Create(
                tenantId,
                "microsoft-defender",
                "Microsoft Defender",
                false,
                "0 */6 * * *",
                apiBaseUrl: "https://api.securitycenter.microsoft.com",
                tokenScope: "https://api.securitycenter.microsoft.com/.default"
            ),
        ];
    }
}

internal static class EnrichmentSourceDefaults
{
    public static IReadOnlyList<EnrichmentSourceConfiguration> CreateDefaults()
    {
        return
        [
            EnrichmentSourceConfiguration.Create(
                "nvd",
                "NVD API",
                false,
                apiBaseUrl: "https://services.nvd.nist.gov"
            ),
        ];
    }
}
