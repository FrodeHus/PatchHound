using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class SecureScoreService
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ILogger<SecureScoreService> _logger;

    public SecureScoreService(
        PatchHoundDbContext dbContext,
        ILogger<SecureScoreService> logger
    )
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Recalculates and persists secure scores for every asset in the tenant.
    /// Designed to run after ingestion/assessment completes.
    /// </summary>
    public async Task RecalculateForTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var assets = await _dbContext.Assets
            .Where(a => a.TenantId == tenantId)
            .Select(a => new
            {
                a.Id,
                a.DeviceValue,
                a.SecurityProfileId,
            })
            .ToListAsync(ct);

        if (assets.Count == 0)
            return;

        var assetIds = assets.Select(a => a.Id).ToHashSet();

        // Load all active assessments for this tenant's assets
        var assessments = await _dbContext.VulnerabilityAssetAssessments
            .Where(a => a.TenantId == tenantId && assetIds.Contains(a.AssetId))
            .Select(a => new
            {
                a.AssetId,
                a.EffectiveSeverity,
                a.EffectiveScore,
                a.TenantVulnerabilityId,
            })
            .ToListAsync(ct);

        // Load remediation task overdue status for each (vulnerability, asset)
        var now = DateTimeOffset.UtcNow;
        var terminalStatuses = new[]
        {
            RemediationTaskStatus.Completed,
            RemediationTaskStatus.RiskAccepted,
        };

        var overdueSet = await _dbContext.RemediationTasks
            .Where(t =>
                t.TenantId == tenantId
                && !terminalStatuses.Contains(t.Status)
                && t.DueDate < now)
            .Select(t => new { t.TenantVulnerabilityId, t.AssetId })
            .ToListAsync(ct);

        var overdueLookup = new HashSet<(Guid VulnId, Guid AssetId)>(
            overdueSet.Select(o => (o.TenantVulnerabilityId, o.AssetId)));

        // Group assessments by asset
        var assessmentsByAsset = assessments
            .GroupBy(a => a.AssetId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Build inputs
        var assetInputs = assets.Select(a =>
        {
            var vulns = assessmentsByAsset.GetValueOrDefault(a.Id, []);
            var vulnInputs = vulns.Select(v => new SecureScoreCalculator.VulnerabilityInput(
                v.EffectiveSeverity,
                v.EffectiveScore,
                overdueLookup.Contains((v.TenantVulnerabilityId, a.Id))
            )).ToList();

            return new SecureScoreCalculator.AssetInput(
                a.Id,
                a.DeviceValue,
                a.SecurityProfileId.HasValue,
                vulnInputs
            );
        }).ToList();

        // Calculate
        var results = assetInputs
            .Select(SecureScoreCalculator.CalculateAssetScore)
            .ToList();

        // Load existing scores for upsert
        var existingScores = await _dbContext.Set<AssetSecureScore>()
            .Where(s => s.TenantId == tenantId)
            .ToDictionaryAsync(s => s.AssetId, ct);

        foreach (var result in results)
        {
            var factorsJson = SecureScoreCalculator.SerializeFactors(result.Factors);

            if (existingScores.TryGetValue(result.AssetId, out var existing))
            {
                existing.Update(
                    result.OverallScore,
                    result.VulnerabilityScore,
                    result.ConfigurationScore,
                    result.DeviceValueWeight,
                    result.ActiveVulnerabilityCount,
                    factorsJson,
                    SecureScoreCalculator.CalculationVersion
                );
            }
            else
            {
                _dbContext.Set<AssetSecureScore>().Add(AssetSecureScore.Create(
                    tenantId,
                    result.AssetId,
                    result.OverallScore,
                    result.VulnerabilityScore,
                    result.ConfigurationScore,
                    result.DeviceValueWeight,
                    result.ActiveVulnerabilityCount,
                    factorsJson,
                    SecureScoreCalculator.CalculationVersion
                ));
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Secure scores recalculated for tenant {TenantId}. Assets scored: {AssetCount}",
            tenantId,
            results.Count
        );
    }

    /// <summary>
    /// Returns the tenant-level aggregate score and per-asset breakdown.
    /// </summary>
    public async Task<SecureScoreCalculator.TenantScoreResult> GetTenantScoreAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        var target = await _dbContext.Set<TenantSecureScoreTarget>()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.TargetScore)
            .FirstOrDefaultAsync(ct);

        if (target == 0m)
            target = 40m;

        var scores = await _dbContext.Set<AssetSecureScore>()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(ct);

        var assetResults = scores.Select(s => new SecureScoreCalculator.AssetScoreResult(
            s.AssetId,
            s.OverallScore,
            s.VulnerabilityScore,
            s.ConfigurationScore,
            s.DeviceValueWeight,
            s.ActiveVulnerabilityCount,
            [] // factors are in FactorsJson, loaded separately for detail view
        )).ToList();

        return SecureScoreCalculator.CalculateTenantScore(assetResults, target);
    }

}
