using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class MaterializedViewRefreshService(PatchHoundDbContext dbContext)
{
    public Task RefreshExposureLatestAssessmentAsync(CancellationToken ct) =>
        RefreshAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY mv_exposure_latest_assessment", ct);

    public Task RefreshAlternateMitigationVulnIdsAsync(CancellationToken ct) =>
        RefreshAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY mv_alternate_mitigation_vuln_ids", ct);

    public Task RefreshOpenExposureVulnSummaryAsync(CancellationToken ct) =>
        RefreshAsync("REFRESH MATERIALIZED VIEW CONCURRENTLY mv_open_exposure_vuln_summary", ct);

    private Task RefreshAsync(string sql, CancellationToken ct)
    {
        if (dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return Task.CompletedTask;
        }

        return dbContext.Database.ExecuteSqlRawAsync(sql, ct);
    }
}
