using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Services;

public class DashboardQueryService(
    PatchHoundDbContext dbContext,
    IRiskChangeBriefAiSummaryService riskChangeBriefAiSummaryService
)
{
    public record RecurrenceData(
        int RecurringVulnerabilityCount,
        decimal RecurrenceRatePercent,
        List<RecurringVulnerabilityDto> TopRecurringVulnerabilities,
        List<RecurringAssetDto> TopRecurringAssets
    );

    public async Task<RecurrenceData> GetRecurrenceDataAsync(Guid tenantId, CancellationToken ct)
    {
        var recurringEpisodes = await dbContext.ExposureEpisodes.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.EpisodeNumber > 1)
            .Select(e => new
            {
                e.DeviceVulnerabilityExposureId,
                e.Exposure.DeviceId,
                e.Exposure.VulnerabilityId,
                e.Exposure.Vulnerability.ExternalId,
                e.Exposure.Vulnerability.Title,
                DeviceName = e.Exposure.Device.ComputerDnsName ?? e.Exposure.Device.Name,
            })
            .ToListAsync(ct);

        var recurringVulnerabilities = recurringEpisodes
            .GroupBy(e => new { e.VulnerabilityId, e.ExternalId, e.Title })
            .Select(group => new RecurringVulnerabilityDto(
                group.Key.VulnerabilityId,
                group.Key.ExternalId,
                group.Key.Title,
                group.Count(),
                group.Select(item => item.DeviceVulnerabilityExposureId).Distinct().Count()
            ))
            .OrderByDescending(item => item.EpisodeCount)
            .ThenBy(item => item.ExternalId)
            .Take(5)
            .ToList();

        var recurringAssets = recurringEpisodes
            .GroupBy(e => new { e.DeviceId, e.DeviceName })
            .Select(group => new RecurringAssetDto(
                group.Key.DeviceId,
                group.Key.DeviceName,
                "Device",
                group.Select(item => item.VulnerabilityId).Distinct().Count()
            ))
            .OrderByDescending(item => item.RecurringVulnerabilityCount)
            .ThenBy(item => item.Name)
            .Take(5)
            .ToList();

        var recurringExposureCount = recurringEpisodes
            .Select(e => e.DeviceVulnerabilityExposureId)
            .Distinct()
            .Count();
        var totalExposureCount = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => e.Id)
            .Distinct()
            .CountAsync(ct);

        return new RecurrenceData(
            recurringExposureCount,
            totalExposureCount == 0 ? 0m : Math.Round((decimal)recurringExposureCount / totalExposureCount * 100m, 2),
            recurringVulnerabilities,
            recurringAssets
        );
    }

    public async Task<DashboardRiskChangeBriefDto> BuildRiskChangeBriefAsync(
        Guid tenantId,
        Guid currentTenantId,
        int? limit,
        bool highCriticalOnly,
        CancellationToken ct,
        int cutoffHours = 24
    )
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-Math.Abs(cutoffHours));

        var appeared = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.FirstObservedAt >= cutoff)
            .Select(e => new
            {
                e.VulnerabilityId,
                e.Vulnerability.ExternalId,
                e.Vulnerability.Title,
                Severity = e.Vulnerability.VendorSeverity.ToString(),
                ChangedAt = e.FirstObservedAt,
                e.DeviceId,
            })
            .ToListAsync(ct);

        var resolved = await dbContext.ExposureEpisodes.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ResolvedAt != null && e.ResolvedAt >= cutoff)
            .Select(e => new
            {
                e.Exposure.VulnerabilityId,
                e.Exposure.Vulnerability.ExternalId,
                e.Exposure.Vulnerability.Title,
                Severity = e.Exposure.Vulnerability.VendorSeverity.ToString(),
                ChangedAt = e.ResolvedAt!.Value,
                e.Exposure.DeviceId,
            })
            .ToListAsync(ct);

        var deterministicBrief = new DashboardRiskChangeBriefDto(
            appeared.Select(item => item.VulnerabilityId).Distinct().Count(),
            resolved.Select(item => item.VulnerabilityId).Distinct().Count(),
            appeared.GroupBy(item => new { item.VulnerabilityId, item.ExternalId, item.Title, item.Severity })
                .Select(group => new DashboardRiskChangeItemDto(
                    group.Key.VulnerabilityId,
                    group.Key.ExternalId,
                    group.Key.Title,
                    group.Key.Severity,
                    group.Select(item => item.DeviceId).Distinct().Count(),
                    group.Max(item => item.ChangedAt)
                ))
                .OrderByDescending(item => item.ChangedAt)
                .Take(limit ?? 3)
                .ToList(),
            resolved.GroupBy(item => new { item.VulnerabilityId, item.ExternalId, item.Title, item.Severity })
                .Select(group => new DashboardRiskChangeItemDto(
                    group.Key.VulnerabilityId,
                    group.Key.ExternalId,
                    group.Key.Title,
                    group.Key.Severity,
                    group.Select(item => item.DeviceId).Distinct().Count(),
                    group.Max(item => item.ChangedAt)
                ))
                .OrderByDescending(item => item.ChangedAt)
                .Take(limit ?? 3)
                .ToList(),
            null
        );

        if (limit != 3)
        {
            return deterministicBrief;
        }

        try
        {
            var aiSummary = await riskChangeBriefAiSummaryService.GenerateAsync(
                currentTenantId,
                new RiskChangeBriefSummaryInput(
                    deterministicBrief.AppearedCount,
                    deterministicBrief.ResolvedCount,
                    deterministicBrief.Appeared
                        .Select(item => new RiskChangeBriefSummaryItemInput(
                            item.ExternalId,
                            item.Title,
                            item.Severity,
                            item.AffectedAssetCount,
                            item.ChangedAt
                        ))
                        .ToList(),
                    deterministicBrief.Resolved
                        .Select(item => new RiskChangeBriefSummaryItemInput(
                            item.ExternalId,
                            item.Title,
                            item.Severity,
                            item.AffectedAssetCount,
                            item.ChangedAt
                        ))
                        .ToList()
                ),
                ct
            );

            return deterministicBrief with { AiSummary = aiSummary };
        }
        catch
        {
            return deterministicBrief;
        }
    }
}
