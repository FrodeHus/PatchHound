using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class AssetRuleEvaluationService : IAssetRuleEvaluationService
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly AssetRuleFilterBuilder _filterBuilder;
    private readonly ILogger<AssetRuleEvaluationService> _logger;

    public AssetRuleEvaluationService(
        PatchHoundDbContext dbContext,
        AssetRuleFilterBuilder filterBuilder,
        ILogger<AssetRuleEvaluationService> logger)
    {
        _dbContext = dbContext;
        _filterBuilder = filterBuilder;
        _logger = logger;
    }

    public async Task EvaluateRulesAsync(Guid tenantId, CancellationToken ct)
    {
        var rules = await _dbContext.AssetRules
            .Where(r => r.TenantId == tenantId && r.Enabled)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            _logger.LogDebug("No enabled asset rules for tenant {TenantId}", tenantId);
            return;
        }

        var claimedAssetIds = new HashSet<Guid>();

        foreach (var rule in rules)
        {
            try
            {
                var filter = rule.ParseFilter();
                var predicate = _filterBuilder.Build(filter);

                var matchingAssetIds = await _dbContext.Assets
                    .AsNoTracking()
                    .Where(a => a.TenantId == tenantId)
                    .Where(predicate)
                    .Select(a => a.Id)
                    .ToListAsync(ct);

                var unclaimedIds = matchingAssetIds
                    .Where(id => !claimedAssetIds.Contains(id))
                    .ToList();

                if (unclaimedIds.Count > 0)
                {
                    var operations = rule.ParseOperations();
                    foreach (var op in operations)
                    {
                        await ApplyOperationAsync(tenantId, unclaimedIds, op, ct);
                    }

                    foreach (var id in unclaimedIds)
                        claimedAssetIds.Add(id);
                }

                rule.RecordExecution(unclaimedIds.Count);
                await _dbContext.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Asset rule '{RuleName}' (priority {Priority}) matched {MatchCount} assets for tenant {TenantId}",
                    rule.Name, rule.Priority, unclaimedIds.Count, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error evaluating asset rule '{RuleName}' ({RuleId}) for tenant {TenantId}",
                    rule.Name, rule.Id, tenantId);
            }
        }
    }

    public async Task<AssetRulePreviewResult> PreviewFilterAsync(
        Guid tenantId, FilterNode filter, CancellationToken ct)
    {
        var predicate = _filterBuilder.Build(filter);

        var query = _dbContext.Assets
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .Where(predicate);

        var count = await query.CountAsync(ct);
        var samples = await query
            .OrderBy(a => a.Name)
            .Take(5)
            .Select(a => new AssetPreviewItem(a.Id, a.Name, a.AssetType.ToString()))
            .ToListAsync(ct);

        return new AssetRulePreviewResult(count, samples);
    }

    private async Task ApplyOperationAsync(
        Guid tenantId, List<Guid> assetIds, AssetRuleOperation op, CancellationToken ct)
    {
        switch (op.Type)
        {
            case "AssignSecurityProfile":
                if (op.Parameters.TryGetValue("securityProfileId", out var profileIdStr)
                    && Guid.TryParse(profileIdStr, out var profileId))
                {
                    await _dbContext.Assets
                        .Where(a => a.TenantId == tenantId && assetIds.Contains(a.Id))
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.SecurityProfileId, profileId), ct);
                }
                break;

            case "AssignTeam":
                if (op.Parameters.TryGetValue("teamId", out var teamIdStr)
                    && Guid.TryParse(teamIdStr, out var teamId))
                {
                    await _dbContext.Assets
                        .Where(a => a.TenantId == tenantId && assetIds.Contains(a.Id))
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.FallbackTeamId, teamId), ct);
                }
                break;

            default:
                _logger.LogWarning("Unknown asset rule operation type: {OperationType}", op.Type);
                break;
        }
    }
}
