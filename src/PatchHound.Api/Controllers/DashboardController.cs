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

        var recurrenceRows = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .GroupBy(episode => new { episode.VulnerabilityId, episode.AssetId })
            .Select(group => new
            {
                group.Key.VulnerabilityId,
                group.Key.AssetId,
                EpisodeCount = group.Count(),
            })
            .ToListAsync(ct);

        var recurringPairs = recurrenceRows.Where(row => row.EpisodeCount > 1).ToList();
        var recurringVulnerabilityIds = recurringPairs
            .Select(row => row.VulnerabilityId)
            .Distinct()
            .ToList();

        var recurrenceRatePercent =
            recurrenceRows.Count == 0
                ? 0
                : Math.Round((decimal)recurringPairs.Count / recurrenceRows.Count * 100m, 1);

        var topRecurringVulnerabilityCounts = recurringPairs
            .GroupBy(row => row.VulnerabilityId)
            .Select(group => new
            {
                VulnerabilityId = group.Key,
                EpisodeCount = group.Sum(row => row.EpisodeCount),
                ReappearanceCount = group.Sum(row => row.EpisodeCount - 1),
            })
            .OrderByDescending(row => row.ReappearanceCount)
            .ThenByDescending(row => row.EpisodeCount)
            .Take(5)
            .ToList();

        var topRecurringVulnerabilityIds = topRecurringVulnerabilityCounts
            .Select(row => row.VulnerabilityId)
            .ToList();

        var recurringVulnerabilities = await _dbContext
            .Vulnerabilities.AsNoTracking()
            .Where(v => topRecurringVulnerabilityIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        var topRecurringVulnerabilities = topRecurringVulnerabilityCounts
            .Select(row =>
            {
                var vulnerability = recurringVulnerabilities[row.VulnerabilityId];
                return new RecurringVulnerabilityDto(
                    vulnerability.Id,
                    vulnerability.ExternalId,
                    vulnerability.Title,
                    row.EpisodeCount,
                    row.ReappearanceCount
                );
            })
            .ToList();

        var topRecurringAssetCounts = recurringPairs
            .GroupBy(row => row.AssetId)
            .Select(group => new
            {
                AssetId = group.Key,
                RecurringVulnerabilityCount = group.Count(),
            })
            .OrderByDescending(row => row.RecurringVulnerabilityCount)
            .Take(5)
            .ToList();

        var recurringAssetIds = topRecurringAssetCounts.Select(row => row.AssetId).ToList();
        var recurringAssets = await _dbContext
            .Assets.AsNoTracking()
            .Where(asset => recurringAssetIds.Contains(asset.Id))
            .ToDictionaryAsync(asset => asset.Id, ct);

        var topRecurringAssets = topRecurringAssetCounts
            .Select(row =>
            {
                var asset = recurringAssets[row.AssetId];
                return new RecurringAssetDto(
                    asset.Id,
                    asset.AssetType == AssetType.Device
                        ? asset.DeviceComputerDnsName ?? asset.Name
                        : asset.Name,
                    asset.AssetType.ToString(),
                    row.RecurringVulnerabilityCount
                );
            })
            .ToList();

        return Ok(
            new DashboardSummaryDto(
                exposureScore,
                vulnsBySeverity,
                vulnsByStatus,
                slaPercent,
                overdueCount,
                tasks.Count,
                avgRemediationDays,
                topVulns,
                recurringVulnerabilityIds.Count,
                recurrenceRatePercent,
                topRecurringVulnerabilities,
                topRecurringAssets
            )
        );
    }

    [HttpGet("trends")]
    public async Task<ActionResult<TrendDataDto>> GetTrends(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-89);

        var episodeRows = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Join(
                _dbContext.Vulnerabilities.AsNoTracking(),
                episode => episode.VulnerabilityId,
                vulnerability => vulnerability.Id,
                (episode, vulnerability) =>
                    new
                    {
                        episode.VulnerabilityId,
                        vulnerability.VendorSeverity,
                        episode.FirstSeenAt,
                        episode.ResolvedAt,
                    }
            )
            .Where(row =>
                DateOnly.FromDateTime(row.FirstSeenAt.UtcDateTime) <= today
                && DateOnly.FromDateTime((row.ResolvedAt ?? DateTimeOffset.UtcNow).UtcDateTime)
                    >= startDate
            )
            .ToListAsync(ct);

        var counts = new Dictionary<(DateOnly Date, Severity Severity), HashSet<Guid>>();

        foreach (var row in episodeRows)
        {
            var firstSeenDate = DateOnly.FromDateTime(row.FirstSeenAt.UtcDateTime);
            var resolvedDate = DateOnly.FromDateTime(
                (row.ResolvedAt ?? DateTimeOffset.UtcNow).UtcDateTime
            );
            var effectiveStart = firstSeenDate < startDate ? startDate : firstSeenDate;
            var effectiveEnd = resolvedDate > today ? today : resolvedDate;

            for (var date = effectiveStart; date <= effectiveEnd; date = date.AddDays(1))
            {
                var key = (date, row.VendorSeverity);
                if (!counts.TryGetValue(key, out var vulnerabilityIds))
                {
                    vulnerabilityIds = [];
                    counts[key] = vulnerabilityIds;
                }

                vulnerabilityIds.Add(row.VulnerabilityId);
            }
        }

        var items = new List<TrendItem>();
        foreach (var date in EachDay(startDate, today))
        {
            foreach (var severity in Enum.GetValues<Severity>())
            {
                counts.TryGetValue((date, severity), out var vulnerabilityIds);
                items.Add(new TrendItem(date, severity.ToString(), vulnerabilityIds?.Count ?? 0));
            }
        }

        return Ok(new TrendDataDto(items));
    }

    private static IEnumerable<DateOnly> EachDay(DateOnly startDate, DateOnly endDate)
    {
        for (var current = startDate; current <= endDate; current = current.AddDays(1))
        {
            yield return current;
        }
    }
}
