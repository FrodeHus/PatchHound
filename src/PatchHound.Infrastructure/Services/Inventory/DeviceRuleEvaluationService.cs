using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Inventory;

public class DeviceRuleEvaluationService : IDeviceRuleEvaluationService
{
    private const string DeviceAssetType = "Device";

    private readonly PatchHoundDbContext _dbContext;
    private readonly DeviceRuleFilterBuilder _filterBuilder;
    private readonly ILogger<DeviceRuleEvaluationService> _logger;

    public DeviceRuleEvaluationService(
        PatchHoundDbContext dbContext,
        DeviceRuleFilterBuilder filterBuilder,
        ILogger<DeviceRuleEvaluationService> logger)
    {
        _dbContext = dbContext;
        _filterBuilder = filterBuilder;
        _logger = logger;
    }

    public async Task EvaluateRulesAsync(Guid tenantId, CancellationToken ct)
    {
        var rules = await _dbContext.DeviceRules
            .Where(r => r.TenantId == tenantId
                && r.Enabled
                && r.AssetType == DeviceAssetType)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        var criticalityRuleMatchedDeviceIds = new HashSet<Guid>();
        var securityProfileRuleMatchedDeviceIds = new HashSet<Guid>();
        var fallbackTeamRuleMatchedDeviceIds = new HashSet<Guid>();
        var businessLabelRuleMatchedKeys = new HashSet<(Guid DeviceId, Guid BusinessLabelId, Guid RuleId)>();

        if (rules.Count == 0)
        {
            await ReconcileCriticalityAsync(tenantId, criticalityRuleMatchedDeviceIds, ct);
            await ReconcileSecurityProfilesAsync(tenantId, securityProfileRuleMatchedDeviceIds, ct);
            await ReconcileFallbackTeamsAsync(tenantId, fallbackTeamRuleMatchedDeviceIds, ct);
            await ReconcileBusinessLabelsAsync(tenantId, businessLabelRuleMatchedKeys, ct);
            _logger.LogDebug("No enabled device rules for tenant {TenantId}", tenantId);
            return;
        }

        var claimedDeviceIds = new HashSet<Guid>();

        foreach (var rule in rules)
        {
            try
            {
                var filter = rule.ParseFilter();
                var predicate = _filterBuilder.Build(filter);

                var matchingDeviceIds = await _dbContext.Devices
                    .AsNoTracking()
                    .Where(d => d.TenantId == tenantId)
                    .Where(predicate)
                    .Select(d => d.Id)
                    .ToListAsync(ct);

                var unclaimedIds = matchingDeviceIds
                    .Where(id => !claimedDeviceIds.Contains(id))
                    .ToList();

                if (unclaimedIds.Count > 0)
                {
                    var operations = rule.ParseOperations();
                    foreach (var op in operations)
                    {
                        await ApplyOperationAsync(
                            rule,
                            tenantId,
                            unclaimedIds,
                            op,
                            criticalityRuleMatchedDeviceIds,
                            securityProfileRuleMatchedDeviceIds,
                            fallbackTeamRuleMatchedDeviceIds,
                            businessLabelRuleMatchedKeys,
                            ct
                        );
                    }

                    foreach (var id in unclaimedIds)
                        claimedDeviceIds.Add(id);
                }

                rule.RecordExecution(unclaimedIds.Count);
                await _dbContext.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Device rule '{RuleName}' (priority {Priority}) matched {MatchCount} devices for tenant {TenantId}",
                    rule.Name, rule.Priority, unclaimedIds.Count, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error evaluating device rule '{RuleName}' ({RuleId}) for tenant {TenantId}",
                    rule.Name, rule.Id, tenantId);
            }
        }

        await ReconcileCriticalityAsync(tenantId, criticalityRuleMatchedDeviceIds, ct);
        await ReconcileSecurityProfilesAsync(tenantId, securityProfileRuleMatchedDeviceIds, ct);
        await ReconcileFallbackTeamsAsync(tenantId, fallbackTeamRuleMatchedDeviceIds, ct);
        await ReconcileBusinessLabelsAsync(tenantId, businessLabelRuleMatchedKeys, ct);
    }

    public async Task EvaluateCriticalityForDeviceAsync(Guid tenantId, Guid deviceId, CancellationToken ct)
    {
        var device = await _dbContext.Devices
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == deviceId, ct);
        if (device is null)
        {
            return;
        }

        if (string.Equals(device.CriticalitySource, "ManualOverride", StringComparison.Ordinal))
        {
            return;
        }

        var rules = await _dbContext.DeviceRules
            .Where(r => r.TenantId == tenantId
                && r.Enabled
                && r.AssetType == DeviceAssetType)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        foreach (var rule in rules)
        {
            var criticalityOperation = rule
                .ParseOperations()
                .FirstOrDefault(operation => string.Equals(operation.Type, "SetCriticality", StringComparison.Ordinal));
            if (criticalityOperation is null)
            {
                continue;
            }

            if (!criticalityOperation.Parameters.TryGetValue("criticality", out var criticalityValue)
                || !Enum.TryParse<Criticality>(criticalityValue, true, out var criticality))
            {
                continue;
            }

            var predicate = _filterBuilder.Build(rule.ParseFilter());
            var matches = await _dbContext.Devices
                .AsNoTracking()
                .Where(current => current.TenantId == tenantId && current.Id == deviceId)
                .Where(predicate)
                .AnyAsync(ct);
            if (!matches)
            {
                continue;
            }

            device.SetCriticalityFromRule(
                criticality,
                rule.Id,
                criticalityOperation.Parameters.GetValueOrDefault("reason")
                    ?? $"Matched device rule '{rule.Name}'."
            );
            await _dbContext.SaveChangesAsync(ct);
            return;
        }

        device.ResetCriticalityToBaseline();
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<DeviceRulePreviewResult> PreviewFilterAsync(
        Guid tenantId, FilterNode filter, CancellationToken ct)
    {
        var predicate = _filterBuilder.Build(filter);

        var query = _dbContext.Devices
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .Where(predicate);

        var count = await query.CountAsync(ct);
        var samples = await query
            .OrderBy(d => d.Name)
            .Take(5)
            .Select(d => new DevicePreviewItem(d.Id, d.Name))
            .ToListAsync(ct);

        return new DeviceRulePreviewResult(count, samples);
    }

    private async Task ApplyOperationAsync(
        DeviceRule rule,
        Guid tenantId,
        List<Guid> deviceIds,
        AssetRuleOperation op,
        ISet<Guid> criticalityRuleMatchedDeviceIds,
        ISet<Guid> securityProfileRuleMatchedDeviceIds,
        ISet<Guid> fallbackTeamRuleMatchedDeviceIds,
        ISet<(Guid DeviceId, Guid BusinessLabelId, Guid RuleId)> businessLabelRuleMatchedKeys,
        CancellationToken ct)
    {
        switch (op.Type)
        {
            case "AssignSecurityProfile":
                if (op.Parameters.TryGetValue("securityProfileId", out var profileIdStr)
                    && Guid.TryParse(profileIdStr, out var profileId))
                {
                    var devices = await _dbContext.Devices
                        .Where(d => d.TenantId == tenantId && deviceIds.Contains(d.Id))
                        .ToListAsync(ct);

                    foreach (var device in devices)
                    {
                        device.AssignSecurityProfileFromRule(profileId, rule.Id);
                        securityProfileRuleMatchedDeviceIds.Add(device.Id);
                    }
                }
                break;

            case "AssignTeam":
                if (op.Parameters.TryGetValue("teamId", out var teamIdStr)
                    && Guid.TryParse(teamIdStr, out var teamId))
                {
                    var devices = await _dbContext.Devices
                        .Where(d => d.TenantId == tenantId && deviceIds.Contains(d.Id))
                        .ToListAsync(ct);

                    foreach (var device in devices)
                    {
                        device.SetFallbackTeamFromRule(teamId, rule.Id);
                        fallbackTeamRuleMatchedDeviceIds.Add(device.Id);
                    }
                }
                break;

            case "SetCriticality":
                if (op.Parameters.TryGetValue("criticality", out var criticalityStr)
                    && Enum.TryParse<Criticality>(criticalityStr, true, out var criticality))
                {
                    var devices = await _dbContext.Devices
                        .Where(d => d.TenantId == tenantId && deviceIds.Contains(d.Id))
                        .ToListAsync(ct);

                    foreach (var device in devices)
                    {
                        if (string.Equals(device.CriticalitySource, "ManualOverride", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        device.SetCriticalityFromRule(
                            criticality,
                            rule.Id,
                            op.Parameters.GetValueOrDefault("reason")
                                ?? $"Matched device rule '{rule.Name}'."
                        );
                        criticalityRuleMatchedDeviceIds.Add(device.Id);
                    }
                }
                break;

            case "AssignBusinessLabel":
                if (op.Parameters.TryGetValue("businessLabelId", out var businessLabelIdStr)
                    && Guid.TryParse(businessLabelIdStr, out var businessLabelId))
                {
                    var sourceKey = DeviceBusinessLabel.BuildRuleSourceKey(rule.Id);
                    var existingLinks = await _dbContext.DeviceBusinessLabels
                        .Where(link =>
                            link.TenantId == tenantId
                            && link.BusinessLabelId == businessLabelId
                            && link.SourceKey == sourceKey
                            && deviceIds.Contains(link.DeviceId))
                        .Select(link => link.DeviceId)
                        .ToListAsync(ct);

                    var existingSet = existingLinks.ToHashSet();
                    foreach (var deviceId in deviceIds)
                    {
                        businessLabelRuleMatchedKeys.Add((deviceId, businessLabelId, rule.Id));
                        if (existingSet.Contains(deviceId))
                        {
                            continue;
                        }

                        await _dbContext.DeviceBusinessLabels.AddAsync(
                            DeviceBusinessLabel.CreateRule(tenantId, deviceId, businessLabelId, rule.Id),
                            ct);
                    }
                }
                break;

            case "AssignScanProfile":
                if (op.Parameters.TryGetValue("scanProfileId", out var scanProfileIdStr)
                    && Guid.TryParse(scanProfileIdStr, out var scanProfileId))
                {
                    var existingAssignments = await _dbContext.DeviceScanProfileAssignments
                        .Where(a => a.ScanProfileId == scanProfileId && a.AssignedByRuleId == rule.Id)
                        .ToListAsync(ct);

                    var assignmentsToRemove = existingAssignments
                        .Where(a => !deviceIds.Contains(a.DeviceId))
                        .ToList();
                    if (assignmentsToRemove.Count > 0)
                    {
                        _dbContext.DeviceScanProfileAssignments.RemoveRange(assignmentsToRemove);
                    }

                    var existingDeviceIds = existingAssignments
                        .Select(a => a.DeviceId)
                        .ToHashSet();
                    var newAssignments = deviceIds
                        .Where(deviceId => !existingDeviceIds.Contains(deviceId))
                        .Select(deviceId => DeviceScanProfileAssignment.Create(tenantId, deviceId, scanProfileId, rule.Id))
                        .ToList();
                    if (newAssignments.Count > 0)
                    {
                        await _dbContext.DeviceScanProfileAssignments.AddRangeAsync(newAssignments, ct);
                    }
                }
                break;

            default:
                _logger.LogWarning("Unknown device rule operation type: {OperationType}", op.Type);
                break;
        }
    }

    private async Task ReconcileSecurityProfilesAsync(
        Guid tenantId,
        ISet<Guid> securityProfileRuleMatchedDeviceIds,
        CancellationToken ct)
    {
        var ruleDerivedDevices = await _dbContext.Devices
            .Where(d =>
                d.TenantId == tenantId
                && d.SecurityProfileRuleId != null
                && !securityProfileRuleMatchedDeviceIds.Contains(d.Id))
            .ToListAsync(ct);

        foreach (var device in ruleDerivedDevices)
        {
            if (device.SecurityProfileRuleId is Guid ruleId)
            {
                device.ClearRuleAssignedSecurityProfile(ruleId);
            }
        }

        if (ruleDerivedDevices.Count > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    private async Task ReconcileFallbackTeamsAsync(
        Guid tenantId,
        ISet<Guid> fallbackTeamRuleMatchedDeviceIds,
        CancellationToken ct)
    {
        var ruleDerivedDevices = await _dbContext.Devices
            .Where(d =>
                d.TenantId == tenantId
                && d.FallbackTeamRuleId != null
                && !fallbackTeamRuleMatchedDeviceIds.Contains(d.Id))
            .ToListAsync(ct);

        foreach (var device in ruleDerivedDevices)
        {
            if (device.FallbackTeamRuleId is Guid ruleId)
            {
                device.ClearRuleAssignedFallbackTeam(ruleId);
            }
        }

        if (ruleDerivedDevices.Count > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    private async Task ReconcileBusinessLabelsAsync(
        Guid tenantId,
        ISet<(Guid DeviceId, Guid BusinessLabelId, Guid RuleId)> businessLabelRuleMatchedKeys,
        CancellationToken ct)
    {
        var existingRuleLinks = await _dbContext.DeviceBusinessLabels
            .Where(link =>
                link.TenantId == tenantId
                && link.SourceType == DeviceBusinessLabel.RuleSourceType
                && link.AssignedByRuleId != null)
            .ToListAsync(ct);

        var staleLinks = existingRuleLinks
            .Where(link => !businessLabelRuleMatchedKeys.Contains(
                (link.DeviceId, link.BusinessLabelId, link.AssignedByRuleId!.Value)))
            .ToList();

        if (staleLinks.Count > 0)
        {
            _dbContext.DeviceBusinessLabels.RemoveRange(staleLinks);
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    private async Task ReconcileCriticalityAsync(
        Guid tenantId,
        ISet<Guid> criticalityRuleMatchedDeviceIds,
        CancellationToken ct)
    {
        var ruleDerivedDevices = await _dbContext.Devices
            .Where(d =>
                d.TenantId == tenantId
                && d.CriticalitySource == "Rule"
                && !criticalityRuleMatchedDeviceIds.Contains(d.Id)
            )
            .ToListAsync(ct);

        foreach (var device in ruleDerivedDevices)
        {
            device.ResetCriticalityToBaseline();
        }

        if (ruleDerivedDevices.Count > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
        }
    }
}
