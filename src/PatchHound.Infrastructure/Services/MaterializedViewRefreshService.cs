using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class MaterializedViewRefreshService(PatchHoundDbContext dbContext)
{
    public Task RefreshExposureLatestAssessmentAsync(CancellationToken ct) =>
        dbContext.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_exposure_latest_assessment", ct);

    public Task RefreshAlternateMitigationVulnIdsAsync(CancellationToken ct) =>
        dbContext.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_alternate_mitigation_vuln_ids", ct);

    public Task RefreshOpenExposureVulnSummaryAsync(CancellationToken ct) =>
        dbContext.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY mv_open_exposure_vuln_summary", ct);
}
