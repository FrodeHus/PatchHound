using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

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
    /// Recalculates and persists secure scores for every asset in the tenant,
    /// including software exposure impact scores and device exposure scores.
    /// Designed to run after ingestion/assessment completes.
    /// </summary>
    public async Task RecalculateForTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var assets = await _dbContext.Assets
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(ct);

        if (assets.Count == 0)
            return;

        var assetIds = assets.Select(a => a.Id).ToHashSet();

        // ── Software exposure impact scores ──
        await CalculateSoftwareExposureImpactsAsync(tenantId, assets, ct);

        // ── Secure scores (device assets only) ──
        await CalculateSecureScoresAsync(tenantId, assets, assetIds, ct);

        await _dbContext.SaveChangesAsync(ct);
    }

    private async Task CalculateSoftwareExposureImpactsAsync(
        Guid tenantId,
        List<Asset> assets,
        CancellationToken ct)
    {
        // Resolve active software snapshot
        var activeSnapshotId = await _dbContext.TenantSourceConfigurations
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.SourceKey == TenantSourceCatalog.DefenderSourceKey)
            .Select(s => s.ActiveSnapshotId)
            .FirstOrDefaultAsync(ct);

        // Load all tenant software IDs for this snapshot
        var tenantSoftwareIds = await _dbContext.TenantSoftware
            .AsNoTracking()
            .Where(ts => ts.TenantId == tenantId && ts.SnapshotId == activeSnapshotId)
            .Select(ts => ts.Id)
            .ToListAsync(ct);

        if (tenantSoftwareIds.Count == 0)
            return;

        // Load vuln projections with their VulnerabilityDefinition severity + CVSS
        var vulnProjections = await _dbContext.NormalizedSoftwareVulnerabilityProjections
            .AsNoTracking()
            .Where(p =>
                p.TenantId == tenantId
                && p.SnapshotId == activeSnapshotId
                && p.ResolvedAt == null
                && tenantSoftwareIds.Contains(p.TenantSoftwareId))
            .Select(p => new
            {
                p.TenantSoftwareId,
                p.VulnerabilityDefinition.VendorSeverity,
                p.VulnerabilityDefinition.CvssScore,
            })
            .ToListAsync(ct);

        var vulnsByTenantSoftware = vulnProjections
            .GroupBy(v => v.TenantSoftwareId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Load active installations grouped by TenantSoftwareId → list of DeviceAssetIds
        var installations = await _dbContext.NormalizedSoftwareInstallations
            .AsNoTracking()
            .Where(i =>
                i.TenantId == tenantId
                && i.SnapshotId == activeSnapshotId
                && i.IsActive)
            .Select(i => new { i.TenantSoftwareId, i.DeviceAssetId })
            .ToListAsync(ct);

        var installsByTenantSoftware = installations
            .GroupBy(i => i.TenantSoftwareId)
            .ToDictionary(g => g.Key, g => g.Select(i => i.DeviceAssetId).Distinct().ToList());

        // Build device value lookup (DeviceAssetId → DeviceValue)
        var deviceValueMap = assets
            .Where(a => a.AssetType == AssetType.Device)
            .ToDictionary(a => a.Id, a => a.DeviceValue);

        // Calculate software impact scores
        var softwareImpacts = new Dictionary<Guid, decimal>();
        foreach (var tenantSoftwareId in tenantSoftwareIds)
        {
            var vulns = vulnsByTenantSoftware.GetValueOrDefault(tenantSoftwareId, []);
            var deviceIds = installsByTenantSoftware.GetValueOrDefault(tenantSoftwareId, []);

            var highValueCount = deviceIds.Count(d =>
                deviceValueMap.TryGetValue(d, out var dv)
                && string.Equals(dv, "High", StringComparison.OrdinalIgnoreCase));

            var input = new ExposureImpactCalculator.SoftwareImpactInput(
                tenantSoftwareId,
                deviceIds.Count,
                highValueCount,
                vulns.Select(v => new ExposureImpactCalculator.SoftwareVulnerabilityInput(
                    v.VendorSeverity, v.CvssScore)).ToList());

            var result = ExposureImpactCalculator.CalculateSoftwareImpact(input);
            softwareImpacts[tenantSoftwareId] = result.ImpactScore;
        }

        // Persist software impact scores on software assets
        // Map TenantSoftwareId → SoftwareAssetIds via installations
        var softwareAssetInstalls = await _dbContext.NormalizedSoftwareInstallations
            .AsNoTracking()
            .Where(i =>
                i.TenantId == tenantId
                && i.SnapshotId == activeSnapshotId
                && i.IsActive)
            .Select(i => new { i.TenantSoftwareId, i.SoftwareAssetId })
            .Distinct()
            .ToListAsync(ct);

        var softwareAssetToTenantSoftware = softwareAssetInstalls
            .GroupBy(i => i.SoftwareAssetId)
            .ToDictionary(g => g.Key, g => g.First().TenantSoftwareId);

        var softwareAssets = assets.Where(a => a.AssetType == AssetType.Software).ToList();
        foreach (var softwareAsset in softwareAssets)
        {
            if (softwareAssetToTenantSoftware.TryGetValue(softwareAsset.Id, out var tsId)
                && softwareImpacts.TryGetValue(tsId, out var impact))
            {
                softwareAsset.SetExposureImpactScore(impact);
            }
            else
            {
                softwareAsset.SetExposureImpactScore(null);
            }
        }

        // Calculate and set device exposure scores
        var deviceInstallsBySoftware = installations
            .GroupBy(i => i.DeviceAssetId)
            .ToDictionary(g => g.Key, g => g.Select(i => i.TenantSoftwareId).Distinct().ToList());

        var deviceAssets = assets.Where(a => a.AssetType == AssetType.Device).ToList();
        foreach (var device in deviceAssets)
        {
            var installedTenantSoftwareIds = deviceInstallsBySoftware.GetValueOrDefault(device.Id, []);
            var installedInputs = installedTenantSoftwareIds
                .Where(tsId => softwareImpacts.ContainsKey(tsId))
                .Select(tsId => new ExposureImpactCalculator.InstalledSoftwareInput(tsId, softwareImpacts[tsId]))
                .ToList();

            var deviceExposure = ExposureImpactCalculator.CalculateDeviceExposure(device.Id, installedInputs);
            device.SetExposureImpactScore(deviceExposure.ExposureScore);
        }

        _logger.LogInformation(
            "Exposure impact scores calculated for tenant {TenantId}. Software: {SoftwareCount}, Devices: {DeviceCount}",
            tenantId, softwareAssets.Count, deviceAssets.Count);
    }

    private async Task CalculateSecureScoresAsync(
        Guid tenantId,
        List<Asset> assets,
        HashSet<Guid> assetIds,
        CancellationToken ct)

    {
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
        var assetInputs = assets
            .Select(a =>
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
