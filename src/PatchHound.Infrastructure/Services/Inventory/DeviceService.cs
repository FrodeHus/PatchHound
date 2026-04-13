using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Inventory;

// Phase 1 canonical cleanup (Task 13): Device-keyed parallel of the legacy
// AssetService. Operates directly on the canonical Device entity through
// PatchHoundDbContext (no repository abstraction) following the newer service
// pattern used by DeviceRuleEvaluationService and StagedDeviceMergeService.
public class DeviceService
{
    private readonly PatchHoundDbContext _dbContext;

    public DeviceService(PatchHoundDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Device>> AssignOwnerAsync(
        Guid deviceId,
        Guid userId,
        CancellationToken ct
    )
    {
        var device = await _dbContext.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
            return Result<Device>.Failure("Device not found");

        device.AssignOwner(userId);
        await _dbContext.SaveChangesAsync(ct);
        return Result<Device>.Success(device);
    }

    public async Task<Result<Device>> AssignTeamOwnerAsync(
        Guid deviceId,
        Guid teamId,
        CancellationToken ct
    )
    {
        var device = await _dbContext.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
            return Result<Device>.Failure("Device not found");

        device.AssignTeamOwner(teamId);
        await _dbContext.SaveChangesAsync(ct);
        return Result<Device>.Success(device);
    }

    public async Task<Result<Device>> SetCriticalityAsync(
        Guid deviceId,
        Criticality criticality,
        CancellationToken ct
    )
    {
        var device = await _dbContext.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
            return Result<Device>.Failure("Device not found");

        device.SetCriticality(criticality);
        await _dbContext.SaveChangesAsync(ct);
        return Result<Device>.Success(device);
    }

    public async Task<Result<Device>> ClearManualCriticalityOverrideAsync(
        Guid deviceId,
        CancellationToken ct
    )
    {
        var device = await _dbContext.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
            return Result<Device>.Failure("Device not found");

        if (!string.Equals(device.CriticalitySource, "ManualOverride", StringComparison.Ordinal))
            return Result<Device>.Failure("Device does not have a manual criticality override");

        device.ClearManualCriticalityOverride();
        await _dbContext.SaveChangesAsync(ct);
        return Result<Device>.Success(device);
    }

    public async Task<Result<Device>> AssignSecurityProfileAsync(
        Guid deviceId,
        Guid? securityProfileId,
        CancellationToken ct
    )
    {
        var device = await _dbContext.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
            return Result<Device>.Failure("Device not found");

        device.AssignSecurityProfile(securityProfileId);
        await _dbContext.SaveChangesAsync(ct);
        return Result<Device>.Success(device);
    }

    public async Task<Result<int>> BulkAssignOwnerAsync(
        IReadOnlyList<Guid> deviceIds,
        Guid ownerId,
        OwnerType ownerType,
        CancellationToken ct
    )
    {
        if (deviceIds.Count == 0)
            return Result<int>.Success(0);

        var devices = await _dbContext.Devices
            .Where(d => deviceIds.Contains(d.Id))
            .ToListAsync(ct);

        foreach (var device in devices)
        {
            if (ownerType == OwnerType.Team)
                device.AssignTeamOwner(ownerId);
            else
                device.AssignOwner(ownerId);
        }

        await _dbContext.SaveChangesAsync(ct);
        return Result<int>.Success(devices.Count);
    }
}
