using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.SecureScore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/secure-score")]
[Authorize(Policy = Policies.ViewVulnerabilities)]
public class SecureScoreController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly SecureScoreService _secureScoreService;
    private readonly ITenantContext _tenantContext;

    public SecureScoreController(
        PatchHoundDbContext dbContext,
        SecureScoreService secureScoreService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _secureScoreService = secureScoreService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Returns the tenant-level secure score summary with top-risk assets.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<SecureScoreSummaryDto>> GetSummary(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest("No tenant selected.");

        var target = await _dbContext.TenantSecureScoreTargets
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.TargetScore)
            .FirstOrDefaultAsync(ct);

        if (target == 0m)
            target = 40m;

        var tenantScore = await _secureScoreService.GetTenantScoreAsync(tenantId, ct);

        // Load asset names for the top-risk assets
        var topRiskScores = tenantScore.AssetScores
            .OrderByDescending(a => a.OverallScore)
            .Take(10)
            .ToList();

        var topAssetIds = topRiskScores.Select(a => a.AssetId).ToHashSet();
        var assetNames = await _dbContext.Assets
            .Where(a => topAssetIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var topRiskDtos = topRiskScores.Select(s => new AssetScoreSummaryDto(
            s.AssetId,
            assetNames.GetValueOrDefault(s.AssetId, "Unknown"),
            s.OverallScore,
            s.VulnerabilityScore,
            s.ConfigurationScore,
            s.DeviceValueWeight,
            s.ActiveVulnerabilityCount
        )).ToList();

        return Ok(new SecureScoreSummaryDto(
            tenantScore.OverallScore,
            target,
            tenantScore.AssetCount,
            tenantScore.AssetsAboveThreshold,
            topRiskDtos
        ));
    }

    /// <summary>
    /// Returns the detailed score breakdown for a specific asset.
    /// </summary>
    [HttpGet("assets/{assetId:guid}")]
    public async Task<ActionResult<AssetScoreDetailDto>> GetAssetScore(
        Guid assetId,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest("No tenant selected.");

        var score = await _dbContext.AssetSecureScores
            .Where(s => s.TenantId == tenantId && s.AssetId == assetId)
            .FirstOrDefaultAsync(ct);

        if (score is null)
            return NotFound();

        var assetName = await _dbContext.Assets
            .Where(a => a.Id == assetId)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(ct) ?? "Unknown";

        var factors = JsonSerializer.Deserialize<List<ScoreFactorDto>>(
            score.FactorsJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        ) ?? [];

        return Ok(new AssetScoreDetailDto(
            score.AssetId,
            assetName,
            score.OverallScore,
            score.VulnerabilityScore,
            score.ConfigurationScore,
            score.DeviceValueWeight,
            score.ActiveVulnerabilityCount,
            factors,
            score.CalculatedAt,
            score.CalculationVersion
        ));
    }

    /// <summary>
    /// Updates the tenant's target secure score.
    /// </summary>
    [HttpPut("target")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> UpdateTarget(
        [FromBody] UpdateTargetScoreRequest request,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest("No tenant selected.");

        var target = await _dbContext.TenantSecureScoreTargets
            .Where(t => t.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (target is null)
        {
            target = TenantSecureScoreTarget.CreateDefault(tenantId);
            _dbContext.TenantSecureScoreTargets.Add(target);
        }

        target.Update(request.TargetScore);
        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Returns the current target score for the tenant.
    /// </summary>
    [HttpGet("target")]
    public async Task<ActionResult<decimal>> GetTarget(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest("No tenant selected.");

        var target = await _dbContext.TenantSecureScoreTargets
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.TargetScore)
            .FirstOrDefaultAsync(ct);

        return Ok(target == 0m ? 40m : target);
    }
}
