using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Services;

public class DashboardQueryService(
    IRiskChangeBriefAiSummaryService riskChangeBriefAiSummaryService
)
{
    public record RecurrenceData(
        int RecurringVulnerabilityCount,
        decimal RecurrenceRatePercent,
        List<RecurringVulnerabilityDto> TopRecurringVulnerabilities,
        List<RecurringAssetDto> TopRecurringAssets
    );

    public Task<RecurrenceData> GetRecurrenceDataAsync(Guid tenantId, CancellationToken ct)
    {
        // Phase-2 stub: recurrence data sourced from VulnerabilityAssetEpisodes,
        // which is deleted. Restored by the canonical exposure merge in Phase 3.
        return Task.FromResult(new RecurrenceData(
            0,
            0m,
            new List<RecurringVulnerabilityDto>(),
            new List<RecurringAssetDto>()
        ));
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
        // Phase-2 stub: appeared/resolved derived from TenantVulnerabilities + episodes,
        // both deleted. Canonical exposure merge in Phase 3 restores this surface.
        var deterministicBrief = new DashboardRiskChangeBriefDto(
            0,
            0,
            new List<DashboardRiskChangeItemDto>(),
            new List<DashboardRiskChangeItemDto>(),
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
