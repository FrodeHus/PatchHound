using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class ExposureEpisodeService(PatchHoundDbContext db)
{
    public async Task SyncEpisodesForTenantAsync(Guid tenantId, Guid runId, DateTimeOffset now, CancellationToken ct)
    {
        // Scope to exposures touched by the current run, OR exposures whose status just transitioned
        // to Resolved and whose ResolvedAt is recent. The 1-day window catches resolved-this-run
        // exposures whose LastSeenRunId still points at the prior run (DeviceVulnerabilityExposure.Resolve
        // deliberately does not update LastSeenRunId — see Task 1 design decision).
        var resolvedCutoff = now.AddDays(-1);
        var exposures = await db.DeviceVulnerabilityExposures
            .Where(e => e.TenantId == tenantId
                     && (e.LastSeenRunId == runId
                         || (e.Status == ExposureStatus.Resolved && e.ResolvedAt != null && e.ResolvedAt >= resolvedCutoff)))
            .ToListAsync(ct);

        if (exposures.Count == 0)
        {
            return;
        }

        var exposureIds = exposures.Select(e => e.Id).ToList();
        var episodesByExposure = await db.ExposureEpisodes
            .Where(e => exposureIds.Contains(e.DeviceVulnerabilityExposureId))
            .ToListAsync(ct);

        var lookup = episodesByExposure
            .GroupBy(e => e.DeviceVulnerabilityExposureId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.EpisodeNumber).ToList());

        var newEpisodes = new List<ExposureEpisode>();

        foreach (var exposure in exposures)
        {
            var episodes = lookup.TryGetValue(exposure.Id, out var list) ? list : [];
            var latest = episodes.FirstOrDefault();

            if (exposure.Status == ExposureStatus.Open)
            {
                if (latest is null || latest.ClosedAt is not null)
                {
                    var nextNumber = (latest?.EpisodeNumber ?? 0) + 1;
                    newEpisodes.Add(ExposureEpisode.Open(tenantId, exposure.Id, nextNumber, exposure.LastObservedAt));
                }
            }
            else
            {
                if (latest is not null && latest.ClosedAt is null)
                {
                    latest.Close(exposure.ResolvedAt ?? now);
                }
            }
        }

        if (newEpisodes.Count > 0)
        {
            db.ExposureEpisodes.AddRange(newEpisodes);
        }
    }
}
