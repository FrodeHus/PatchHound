using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Policies.ViewVulnerabilities)]
public class DashboardController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;

    public DashboardController(PatchHoundDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(CancellationToken ct)
    {
        // Vulnerability counts by severity
        var bySeverity = await _dbContext
            .Vulnerabilities.AsNoTracking()
            .GroupBy(v => v.VendorSeverity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var vulnsBySeverity = Enum.GetValues<Severity>()
            .ToDictionary(
                s => s.ToString(),
                s => bySeverity.FirstOrDefault(x => x.Severity == s)?.Count ?? 0
            );

        // Vulnerability counts by status
        var byStatus = await _dbContext
            .Vulnerabilities.AsNoTracking()
            .GroupBy(v => v.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var vulnsByStatus = Enum.GetValues<VulnerabilityStatus>()
            .ToDictionary(
                s => s.ToString(),
                s => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0
            );

        // SLA compliance
        var tasks = await _dbContext
            .RemediationTasks.AsNoTracking()
            .Select(t => new
            {
                t.Status,
                t.DueDate,
                t.CreatedAt,
                t.UpdatedAt,
            })
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var taskTuples = tasks.Select(t => (t.Status, t.DueDate)).ToList();
        var (slaPercent, overdueCount) = DashboardService.CalculateSlaCompliance(taskTuples, now);

        // Average remediation days
        var completedTasks = tasks
            .Where(t => t.Status == RemediationTaskStatus.Completed)
            .Select(t => (t.CreatedAt, t.UpdatedAt))
            .ToList();
        var avgRemediationDays = DashboardService.CalculateAverageRemediationDays(completedTasks);

        // Exposure score: vulnerability severity × asset criticality for open vulnerability-asset pairs
        var vulnerabilityAssetPairs = await _dbContext
            .VulnerabilityAssets.AsNoTracking()
            .Where(va => va.Status != VulnerabilityStatus.Resolved)
            .Join(
                _dbContext.Vulnerabilities,
                va => va.VulnerabilityId,
                v => v.Id,
                (va, v) => new { v.VendorSeverity, va.AssetId }
            )
            .Join(
                _dbContext.Assets,
                x => x.AssetId,
                a => a.Id,
                (x, a) => new { x.VendorSeverity, a.Criticality }
            )
            .ToListAsync(ct);

        var exposurePairs = vulnerabilityAssetPairs
            .Select(p => (p.VendorSeverity, p.Criticality))
            .ToList();
        var exposureScore = DashboardService.CalculateExposureScore(exposurePairs);

        // Top 10 most urgent vulnerabilities (highest severity × oldest)
        var topVulns = await _dbContext
            .Vulnerabilities.AsNoTracking()
            .Where(v =>
                v.Status == VulnerabilityStatus.Open
                || v.Status == VulnerabilityStatus.InRemediation
            )
            .OrderByDescending(v => v.VendorSeverity)
            .ThenBy(v => v.PublishedDate)
            .Take(10)
            .Select(v => new TopVulnerabilityDto(
                v.Id,
                v.ExternalId,
                v.Title,
                v.VendorSeverity.ToString(),
                v.CvssScore,
                v.AffectedAssets.Count,
                v.PublishedDate.HasValue ? (int)(now - v.PublishedDate.Value).TotalDays : 0
            ))
            .ToListAsync(ct);

        return Ok(
            new DashboardSummaryDto(
                exposureScore,
                vulnsBySeverity,
                vulnsByStatus,
                slaPercent,
                overdueCount,
                tasks.Count,
                avgRemediationDays,
                topVulns
            )
        );
    }

    [HttpGet("trends")]
    public async Task<ActionResult<TrendDataDto>> GetTrends(CancellationToken ct)
    {
        var twelveMonthsAgo = DateTimeOffset.UtcNow.AddMonths(-12);

        var trends = await _dbContext
            .Vulnerabilities.AsNoTracking()
            .Where(v => v.PublishedDate.HasValue && v.PublishedDate.Value >= twelveMonthsAgo)
            .GroupBy(v => new
            {
                v.PublishedDate!.Value.Year,
                v.PublishedDate!.Value.Month,
                v.VendorSeverity,
            })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Severity = g.Key.VendorSeverity,
                Count = g.Count(),
            })
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ThenBy(x => x.Severity)
            .ToListAsync(ct);

        var items = trends
            .Select(t => new TrendItem(
                new DateOnly(t.Year, t.Month, 1),
                t.Severity.ToString(),
                t.Count
            ))
            .ToList();

        return Ok(new TrendDataDto(items));
    }
}
