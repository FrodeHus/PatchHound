using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;

namespace PatchHound.Core.Services;

public class AssetService
{
    private readonly IAssetRepository _assetRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AssetService(IAssetRepository assetRepository, IUnitOfWork unitOfWork)
    {
        _assetRepository = assetRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Asset>> AssignOwnerAsync(
        Guid assetId,
        Guid userId,
        CancellationToken ct
    )
    {
        var asset = await _assetRepository.GetByIdAsync(assetId, ct);
        if (asset is null)
            return Result<Asset>.Failure("Asset not found");

        asset.AssignOwner(userId);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Asset>.Success(asset);
    }

    public async Task<Result<Asset>> AssignTeamOwnerAsync(
        Guid assetId,
        Guid teamId,
        CancellationToken ct
    )
    {
        var asset = await _assetRepository.GetByIdAsync(assetId, ct);
        if (asset is null)
            return Result<Asset>.Failure("Asset not found");

        asset.AssignTeamOwner(teamId);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Asset>.Success(asset);
    }

    public async Task<Result<Asset>> SetCriticalityAsync(
        Guid assetId,
        Criticality criticality,
        CancellationToken ct
    )
    {
        var asset = await _assetRepository.GetByIdAsync(assetId, ct);
        if (asset is null)
            return Result<Asset>.Failure("Asset not found");

        asset.SetCriticality(criticality);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Asset>.Success(asset);
    }

    public async Task<Result<Asset>> ClearManualCriticalityOverrideAsync(
        Guid assetId,
        CancellationToken ct
    )
    {
        var asset = await _assetRepository.GetByIdAsync(assetId, ct);
        if (asset is null)
            return Result<Asset>.Failure("Asset not found");

        if (!string.Equals(asset.CriticalitySource, "ManualOverride", StringComparison.Ordinal))
            return Result<Asset>.Failure("Asset does not have a manual criticality override");

        asset.ClearManualCriticalityOverride();
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Asset>.Success(asset);
    }

    public async Task<Result<Asset>> AssignSecurityProfileAsync(
        Guid assetId,
        Guid? securityProfileId,
        CancellationToken ct
    )
    {
        var asset = await _assetRepository.GetByIdAsync(assetId, ct);
        if (asset is null)
            return Result<Asset>.Failure("Asset not found");

        asset.AssignSecurityProfile(securityProfileId);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<Asset>.Success(asset);
    }

    public async Task<Result<int>> BulkAssignOwnerAsync(
        IReadOnlyList<Guid> assetIds,
        Guid ownerId,
        OwnerType ownerType,
        CancellationToken ct
    )
    {
        var updatedCount = 0;

        foreach (var assetId in assetIds)
        {
            var asset = await _assetRepository.GetByIdAsync(assetId, ct);
            if (asset is null)
                continue;

            if (ownerType == OwnerType.Team)
                asset.AssignTeamOwner(ownerId);
            else
                asset.AssignOwner(ownerId);

            updatedCount++;
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<int>.Success(updatedCount);
    }
}
