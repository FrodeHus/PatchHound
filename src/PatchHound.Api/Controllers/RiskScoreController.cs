using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.RiskScore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/risk-score")]
[Authorize(Policy = Policies.ViewVulnerabilities)]
public class RiskScoreController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly RiskScoreService _riskScoreService;
    private readonly ITenantContext _tenantContext;

    public RiskScoreController(
        PatchHoundDbContext dbContext,
        RiskScoreService riskScoreService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _riskScoreService = riskScoreService;
        _tenantContext = tenantContext;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<RiskScoreSummaryDto>> GetSummary(
        [FromQuery] int? minAgeDays,
        [FromQuery] string? platform,
        [FromQuery] string? deviceGroup,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest("No tenant selected.");
        }

        var hasFilters =
            minAgeDays.HasValue
            || !string.IsNullOrWhiteSpace(platform)
            || !string.IsNullOrWhiteSpace(deviceGroup);
        var tenantRisk = hasFilters
            ? await _riskScoreService.GetFilteredTenantRiskAsync(tenantId, minAgeDays, platform, deviceGroup, ct)
            : await _riskScoreService.GetTenantRiskAsync(tenantId, ct);
        var topRiskAssets = tenantRisk.AssetScores
            .OrderByDescending(item => item.OverallScore)
            .Take(10)
            .ToList();

        var assetIds = topRiskAssets.Select(item => item.AssetId).ToHashSet();
        var assetNames = await _dbContext.Assets
            .Where(item => assetIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Name })
            .ToDictionaryAsync(item => item.Id, item => item.Name, ct);
        var episodeDrivers = await _dbContext.ExposureAssessments.AsNoTracking()
            .Where(item => item.TenantId == tenantId && assetIds.Contains(item.Exposure.DeviceId))
            .Select(item => new
            {
                DeviceId = item.Exposure.DeviceId,
                Driver = new AssetRiskEpisodeDriverDto(
                    item.Exposure.VulnerabilityId,
                    item.Exposure.Vulnerability.ExternalId,
                    item.Exposure.Vulnerability.Title,
                    item.EnvironmentalCvss >= 9.0m ? "Critical" : item.EnvironmentalCvss >= 7.0m ? "High" : item.EnvironmentalCvss >= 4.0m ? "Medium" : "Low",
                    item.EnvironmentalCvss,
                    _dbContext.ThreatAssessments
                        .Where(t => t.VulnerabilityId == item.Exposure.VulnerabilityId)
                        .Select(t => t.ThreatScore)
                        .FirstOrDefault(),
                    item.EnvironmentalCvss,
                    0m)
            })
            .ToListAsync(ct);

        var history = hasFilters
            ? []
            : await _riskScoreService.GetRiskHistoryAsync(tenantId, ct);
        var calculatedAt = hasFilters
            ? DateTimeOffset.UtcNow
            : await _dbContext.DeviceRiskScores.AsNoTracking()
                .Where(item => item.TenantId == tenantId)
                .Select(item => (DateTimeOffset?)item.CalculatedAt)
                .MaxAsync(ct);

        return Ok(new RiskScoreSummaryDto(
            tenantRisk.OverallScore,
            tenantRisk.AssetCount,
            tenantRisk.CriticalAssetCount,
            tenantRisk.HighAssetCount,
            topRiskAssets.Select(item => new AssetRiskScoreSummaryDto(
                item.AssetId,
                assetNames.GetValueOrDefault(item.AssetId, "Unknown"),
                item.OverallScore,
                item.MaxEpisodeRiskScore,
                item.CriticalCount,
                item.HighCount,
                item.MediumCount,
                item.LowCount,
                item.OpenEpisodeCount,
                episodeDrivers
                    .Where(driver => driver.DeviceId == item.AssetId)
                    .Select(driver => driver.Driver)
                    .Take(3)
                    .ToList()
            )).ToList(),
            history.Select(item => new RiskScoreSnapshotDto(
                item.Date,
                item.OverallScore,
                item.AssetCount,
                item.CriticalAssetCount,
                item.HighAssetCount
            )).ToList(),
            calculatedAt
        ));
    }

    [HttpPost("recalculate")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> Recalculate(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest("No tenant selected.");
        }

        await _riskScoreService.RecalculateForTenantAsync(tenantId, ct);
        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("device-groups/{deviceGroupName}")]
    public async Task<ActionResult<DeviceGroupRiskDetailDto>> GetDeviceGroupDetail(
        string deviceGroupName,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest("No tenant selected.");
        }

        var groupScore = await _dbContext.DeviceGroupRiskScores.AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.DeviceGroupName == deviceGroupName,
                ct
            );
        if (groupScore is null)
        {
            return NotFound();
        }

        var topRiskAssets = await _dbContext.DeviceRiskScores.AsNoTracking()
            .Where(score => score.TenantId == tenantId)
            .Join(
                _dbContext.Devices.AsNoTracking()
                    .Where(device => device.TenantId == tenantId && device.GroupName == deviceGroupName),
                score => score.DeviceId,
                device => device.Id,
                (score, device) => new
                {
                    AssetId = score.DeviceId,
                    AssetName = device.ComputerDnsName ?? device.Name,
                    score.OverallScore,
                    score.MaxEpisodeRiskScore,
                    score.CriticalCount,
                    score.HighCount,
                    score.MediumCount,
                    score.LowCount,
                    score.OpenEpisodeCount,
                }
            )
            .OrderByDescending(item => item.OverallScore)
            .Take(5)
            .ToListAsync(ct);

        var assetIds = topRiskAssets.Select(item => item.AssetId).ToHashSet();
        var episodeDrivers = await _dbContext.ExposureAssessments.AsNoTracking()
            .Where(item => item.TenantId == tenantId && assetIds.Contains(item.Exposure.DeviceId))
            .Select(item => new
            {
                DeviceId = item.Exposure.DeviceId,
                Driver = new AssetRiskEpisodeDriverDto(
                    item.Exposure.VulnerabilityId,
                    item.Exposure.Vulnerability.ExternalId,
                    item.Exposure.Vulnerability.Title,
                    item.EnvironmentalCvss >= 9.0m ? "Critical" : item.EnvironmentalCvss >= 7.0m ? "High" : item.EnvironmentalCvss >= 4.0m ? "Medium" : "Low",
                    item.EnvironmentalCvss,
                    _dbContext.ThreatAssessments.Where(t => t.VulnerabilityId == item.Exposure.VulnerabilityId).Select(t => t.ThreatScore).FirstOrDefault(),
                    item.EnvironmentalCvss,
                    0m)
            })
            .ToListAsync(ct);

        return Ok(new DeviceGroupRiskDetailDto(
            groupScore.DeviceGroupName,
            groupScore.OverallScore,
            groupScore.CalculatedAt,
            groupScore.AssetCount,
            groupScore.OpenEpisodeCount,
            groupScore.CriticalEpisodeCount,
            groupScore.HighEpisodeCount,
            groupScore.MediumEpisodeCount,
            groupScore.LowEpisodeCount,
            ToRollupRiskExplanationDto(
                groupScore.OverallScore,
                groupScore.MaxAssetRiskScore,
                groupScore.AssetCount,
                groupScore.OpenEpisodeCount,
                groupScore.CriticalEpisodeCount,
                groupScore.HighEpisodeCount,
                groupScore.MediumEpisodeCount,
                groupScore.LowEpisodeCount,
                groupScore.FactorsJson,
                groupScore.CalculationVersion,
                0.55m,
                0.25m
            ),
            topRiskAssets.Select(item => new AssetRiskScoreSummaryDto(
                item.AssetId,
                item.AssetName,
                item.OverallScore,
                item.MaxEpisodeRiskScore,
                item.CriticalCount,
                item.HighCount,
                item.MediumCount,
                item.LowCount,
                item.OpenEpisodeCount,
                episodeDrivers
                    .Where(driver => driver.DeviceId == item.AssetId)
                    .Select(driver => driver.Driver)
                    .Take(3)
                    .ToList()
            )).ToList()
        ));
    }

    [HttpGet("software/{softwareProductId:guid}")]
    public async Task<ActionResult<SoftwareRiskDetailDto>> GetSoftwareDetail(
        Guid softwareProductId,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest("No tenant selected.");
        }

        var softwareScore = await _dbContext.SoftwareRiskScores.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.SoftwareProductId == softwareProductId)
            .Select(item => new
            {
                item.SoftwareProductId,
                item.OverallScore,
                item.AffectedDeviceCount,
                item.OpenExposureCount,
                item.CriticalExposureCount,
                item.HighExposureCount,
                item.MediumExposureCount,
                item.LowExposureCount,
                item.CalculatedAt,
                SoftwareName = item.SoftwareProduct.Name,
                Vendor = (string?)item.SoftwareProduct.Vendor,
            })
            .FirstOrDefaultAsync(ct);
        if (softwareScore is null)
        {
            return NotFound();
        }

        var topRiskAssets = await _dbContext.InstalledSoftware.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.SoftwareProductId == softwareProductId)
            .Select(item => item.DeviceId)
            .Distinct()
            .Join(
                _dbContext.DeviceRiskScores.AsNoTracking().Where(item => item.TenantId == tenantId),
                deviceId => deviceId,
                score => score.DeviceId,
                (deviceId, score) => score
            )
            .Join(
                _dbContext.Devices.AsNoTracking(),
                score => score.DeviceId,
                device => device.Id,
                (score, device) => new
                {
                    AssetId = score.DeviceId,
                    AssetName = device.ComputerDnsName ?? device.Name,
                    score.OverallScore,
                    score.MaxEpisodeRiskScore,
                    score.CriticalCount,
                    score.HighCount,
                    score.MediumCount,
                    score.LowCount,
                    score.OpenEpisodeCount,
                }
            )
            .OrderByDescending(item => item.OverallScore)
            .Take(5)
            .ToListAsync(ct);

        var episodeDrivers = await _dbContext.ExposureAssessments.AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Join(
                _dbContext.DeviceVulnerabilityExposures.AsNoTracking()
                    .Where(e => e.TenantId == tenantId && e.SoftwareProductId == softwareProductId),
                assessment => assessment.DeviceVulnerabilityExposureId,
                exposure => exposure.Id,
                (assessment, exposure) => new
                {
                    exposure.SoftwareProductId,
                    DeviceId = assessment.Exposure.DeviceId,
                    Driver = new AssetRiskEpisodeDriverDto(
                        assessment.Exposure.VulnerabilityId,
                        exposure.Vulnerability.ExternalId,
                        exposure.Vulnerability.Title,
                        assessment.EnvironmentalCvss >= 9.0m ? "Critical" : assessment.EnvironmentalCvss >= 7.0m ? "High" : assessment.EnvironmentalCvss >= 4.0m ? "Medium" : "Low",
                        assessment.EnvironmentalCvss,
                        _dbContext.ThreatAssessments.Where(t => t.VulnerabilityId == assessment.Exposure.VulnerabilityId).Select(t => t.ThreatScore).FirstOrDefault(),
                        assessment.EnvironmentalCvss,
                        0m)
                })
            .ToListAsync(ct);
        return Ok(new SoftwareRiskDetailDto(
            softwareScore.SoftwareProductId,
            softwareScore.SoftwareName,
            softwareScore.Vendor,
            softwareScore.OverallScore,
            softwareScore.CalculatedAt,
            softwareScore.AffectedDeviceCount,
            softwareScore.OpenExposureCount,
            softwareScore.CriticalExposureCount,
            softwareScore.HighExposureCount,
            softwareScore.MediumExposureCount,
            softwareScore.LowExposureCount,
            topRiskAssets.Select(item => new AssetRiskScoreSummaryDto(
                item.AssetId,
                item.AssetName,
                item.OverallScore,
                item.MaxEpisodeRiskScore,
                item.CriticalCount,
                item.HighCount,
                item.MediumCount,
                item.LowCount,
                item.OpenEpisodeCount,
                episodeDrivers
                    .Where(driver => driver.DeviceId == item.AssetId)
                    .Select(driver => driver.Driver)
                    .Take(3)
                    .ToList()
            )).ToList()
        ));
    }

    private static RollupRiskExplanationDto ToRollupRiskExplanationDto(
        decimal overallScore,
        decimal maxAssetRiskScore,
        int assetCount,
        int openEpisodeCount,
        int criticalEpisodeCount,
        int highEpisodeCount,
        int mediumEpisodeCount,
        int lowEpisodeCount,
        string factorsJson,
        string calculationVersion,
        decimal maxWeight,
        decimal topThreeWeight
    )
    {
        var factors = ParseRiskFactors(factorsJson);
        var topThreeAverage = factors.FirstOrDefault(item => item.Name == "TopThreeAverage")?.Impact ?? 0m;
        var criticalContribution = factors.FirstOrDefault(item => item.Name == "CriticalEpisodes")?.Impact ?? 0m;
        var highContribution = factors.FirstOrDefault(item => item.Name == "HighEpisodes")?.Impact ?? 0m;
        var mediumContribution = factors.FirstOrDefault(item => item.Name == "MediumEpisodes")?.Impact ?? 0m;
        var lowContribution = factors.FirstOrDefault(item => item.Name == "LowEpisodes")?.Impact ?? 0m;

        return new RollupRiskExplanationDto(
            overallScore,
            calculationVersion,
            maxAssetRiskScore,
            topThreeAverage,
            Math.Round(maxWeight * maxAssetRiskScore, 2),
            Math.Round(topThreeWeight * topThreeAverage, 2),
            assetCount,
            openEpisodeCount,
            criticalEpisodeCount,
            highEpisodeCount,
            mediumEpisodeCount,
            lowEpisodeCount,
            criticalContribution,
            highContribution,
            mediumContribution,
            lowContribution,
            factors.Select(item => new RollupRiskExplanationFactorDto(
                item.Name,
                item.Description,
                item.Impact
            )).ToList()
        );
    }

    private static IReadOnlyList<ParsedRiskFactor> ParseRiskFactors(string factorsJson)
    {
        if (string.IsNullOrWhiteSpace(factorsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ParsedRiskFactor>>(factorsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed record ParsedRiskFactor(
        string Name,
        string Description,
        decimal Impact
    );
}
