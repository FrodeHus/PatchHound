using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;

namespace PatchHound.Api.Services;

public sealed class DeviceRuleCleanupService
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly DeviceRuleFilterBuilder _filterBuilder;
    private readonly SoftwareRuleFilterBuilder _softwareFilterBuilder;
    private readonly CloudApplicationRuleFilterBuilder _cloudApplicationFilterBuilder;

    public DeviceRuleCleanupService(
        PatchHoundDbContext dbContext,
        DeviceRuleFilterBuilder filterBuilder,
        SoftwareRuleFilterBuilder softwareFilterBuilder,
        CloudApplicationRuleFilterBuilder cloudApplicationFilterBuilder)
    {
        _dbContext = dbContext;
        _filterBuilder = filterBuilder;
        _softwareFilterBuilder = softwareFilterBuilder;
        _cloudApplicationFilterBuilder = cloudApplicationFilterBuilder;
    }

    public async Task ResetDeletedRuleEffectsAsync(
        Guid tenantId,
        DeviceRule deletedRule,
        IReadOnlyCollection<AssetRuleOperation> operations,
        CancellationToken ct)
    {
        if (operations.Count == 0)
            return;

        if (DeviceRuleAssetTypes.IsApplication(deletedRule.AssetType))
        {
            await ResetApplicationRuleEffectsAsync(tenantId, deletedRule, operations, ct);
            return;
        }

        if (DeviceRuleAssetTypes.IsSoftware(deletedRule.AssetType))
        {
            await ResetSoftwareRuleEffectsAsync(tenantId, deletedRule, operations, ct);
            return;
        }

        await ResetDeviceRuleEffectsAsync(tenantId, deletedRule, operations, ct);
    }

    private async Task ResetApplicationRuleEffectsAsync(
        Guid tenantId,
        DeviceRule deletedRule,
        IReadOnlyCollection<AssetRuleOperation> operations,
        CancellationToken ct)
    {
        var applicationIds = await GetMatchingSoftwareIdsAsync(deletedRule, tenantId, ct);
        foreach (var operation in operations)
        {
            if (operation.Type != DeviceRuleOperationTypes.AssignOwnerTeam || applicationIds.Count == 0)
                continue;

            var applications = await _dbContext.CloudApplications
                .IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId
                    && applicationIds.Contains(item.Id)
                    && item.OwnerTeamRuleId == deletedRule.Id)
                .ToListAsync(ct);

            foreach (var application in applications)
            {
                application.ClearRuleAssignedOwnerTeam(deletedRule.Id);
            }

            if (applications.Count > 0)
                await _dbContext.SaveChangesAsync(ct);
        }
    }

    private async Task ResetSoftwareRuleEffectsAsync(
        Guid tenantId,
        DeviceRule deletedRule,
        IReadOnlyCollection<AssetRuleOperation> operations,
        CancellationToken ct)
    {
        var softwareIds = await GetMatchingSoftwareIdsAsync(deletedRule, tenantId, ct);
        foreach (var operation in operations)
        {
            if (operation.Type != DeviceRuleOperationTypes.AssignOwnerTeam || softwareIds.Count == 0)
                continue;

            var records = await _dbContext.SoftwareTenantRecords
                .Where(item =>
                    item.TenantId == tenantId
                    && softwareIds.Contains(item.Id)
                    && item.OwnerTeamRuleId == deletedRule.Id)
                .ToListAsync(ct);

            foreach (var record in records)
            {
                record.ClearRuleAssignedOwnerTeam(deletedRule.Id);
            }

            if (records.Count > 0)
                await _dbContext.SaveChangesAsync(ct);
        }
    }

    private async Task ResetDeviceRuleEffectsAsync(
        Guid tenantId,
        DeviceRule deletedRule,
        IReadOnlyCollection<AssetRuleOperation> operations,
        CancellationToken ct)
    {
        var deviceIds = await GetMatchingDeviceIdsAsync(deletedRule, tenantId, ct);

        foreach (var operation in operations)
        {
            switch (operation.Type)
            {
                case DeviceRuleOperationTypes.AssignSecurityProfile:
                    await ClearRuleAssignedSecurityProfileAsync(tenantId, deletedRule, deviceIds, ct);
                    break;

                case DeviceRuleOperationTypes.AssignTeam:
                    await ClearRuleAssignedFallbackTeamAsync(tenantId, deletedRule, deviceIds, ct);
                    break;

                case DeviceRuleOperationTypes.AssignOwnerTeam:
                    await ClearRuleAssignedOwnerTeamAsync(tenantId, deletedRule, deviceIds, ct);
                    break;

                case DeviceRuleOperationTypes.SetCriticality:
                    await ResetRuleAssignedCriticalityAsync(tenantId, deletedRule, deviceIds, ct);
                    break;

                case DeviceRuleOperationTypes.AssignBusinessLabel:
                    await RemoveRuleAssignedBusinessLabelsAsync(tenantId, deletedRule, ct);
                    break;
            }
        }
    }

    private async Task<List<Guid>> GetMatchingDeviceIdsAsync(
        DeviceRule rule,
        Guid tenantId,
        CancellationToken ct)
    {
        try
        {
            var predicate = _filterBuilder.Build(rule.ParseFilter());
            return await _dbContext.Devices
                .AsNoTracking()
                .Where(device => device.TenantId == tenantId)
                .Where(predicate)
                .Select(device => device.Id)
                .ToListAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException(
                $"Unable to evaluate deleted device rule filter for cleanup: {ex.Message}",
                ex);
        }
    }

    private async Task<List<Guid>> GetMatchingSoftwareIdsAsync(
        DeviceRule rule,
        Guid tenantId,
        CancellationToken ct)
    {
        try
        {
            if (DeviceRuleAssetTypes.IsApplication(rule.AssetType))
            {
                var applicationPredicate = _cloudApplicationFilterBuilder.Build(rule.ParseFilter());
                return await _dbContext.CloudApplications
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(item => item.TenantId == tenantId && item.ActiveInTenant)
                    .Where(applicationPredicate)
                    .Select(item => item.Id)
                    .ToListAsync(ct);
            }

            var predicate = _softwareFilterBuilder.Build(rule.ParseFilter());
            return await _dbContext.SoftwareTenantRecords
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId)
                .Where(predicate)
                .Select(item => item.Id)
                .ToListAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException(
                $"Unable to evaluate deleted {rule.AssetType.ToLowerInvariant()} rule filter for cleanup: {ex.Message}",
                ex);
        }
    }

    private async Task ClearRuleAssignedSecurityProfileAsync(
        Guid tenantId,
        DeviceRule deletedRule,
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken ct)
    {
        if (deviceIds.Count == 0)
            return;

        var devices = await _dbContext.Devices
            .Where(device =>
                device.TenantId == tenantId
                && deviceIds.Contains(device.Id)
                && device.SecurityProfileRuleId == deletedRule.Id)
            .ToListAsync(ct);

        foreach (var device in devices)
            device.ClearRuleAssignedSecurityProfile(deletedRule.Id);

        if (devices.Count > 0)
            await _dbContext.SaveChangesAsync(ct);
    }

    private async Task ClearRuleAssignedFallbackTeamAsync(
        Guid tenantId,
        DeviceRule deletedRule,
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken ct)
    {
        if (deviceIds.Count == 0)
            return;

        var devices = await _dbContext.Devices
            .Where(device =>
                device.TenantId == tenantId
                && deviceIds.Contains(device.Id)
                && device.FallbackTeamRuleId == deletedRule.Id)
            .ToListAsync(ct);

        foreach (var device in devices)
            device.ClearRuleAssignedFallbackTeam(deletedRule.Id);

        if (devices.Count > 0)
            await _dbContext.SaveChangesAsync(ct);
    }

    private async Task ClearRuleAssignedOwnerTeamAsync(
        Guid tenantId,
        DeviceRule deletedRule,
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken ct)
    {
        if (deviceIds.Count == 0)
            return;

        var devices = await _dbContext.Devices
            .Where(device =>
                device.TenantId == tenantId
                && deviceIds.Contains(device.Id)
                && device.OwnerTeamRuleId == deletedRule.Id)
            .ToListAsync(ct);

        foreach (var device in devices)
            device.ClearRuleAssignedOwnerTeam(deletedRule.Id);

        if (devices.Count > 0)
            await _dbContext.SaveChangesAsync(ct);
    }

    private async Task ResetRuleAssignedCriticalityAsync(
        Guid tenantId,
        DeviceRule deletedRule,
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken ct)
    {
        if (deviceIds.Count == 0)
            return;

        var devices = await _dbContext.Devices
            .Where(device =>
                device.TenantId == tenantId
                && deviceIds.Contains(device.Id)
                && device.CriticalitySource == "Rule"
                && device.CriticalityRuleId == deletedRule.Id)
            .ToListAsync(ct);

        foreach (var device in devices)
            device.ResetCriticalityToBaseline();

        if (devices.Count > 0)
            await _dbContext.SaveChangesAsync(ct);
    }

    private async Task RemoveRuleAssignedBusinessLabelsAsync(
        Guid tenantId,
        DeviceRule deletedRule,
        CancellationToken ct)
    {
        var existingAssignments = await _dbContext.DeviceBusinessLabels
            .Where(link =>
                link.TenantId == tenantId
                && link.AssignedByRuleId == deletedRule.Id
                && link.SourceType == DeviceBusinessLabel.RuleSourceType)
            .ToListAsync(ct);

        if (existingAssignments.Count > 0)
        {
            _dbContext.DeviceBusinessLabels.RemoveRange(existingAssignments);
            await _dbContext.SaveChangesAsync(ct);
        }
    }
}
