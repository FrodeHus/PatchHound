using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class ExposureEpisodeService(PatchHoundDbContext db)
{
    public async Task SyncEpisodesForTenantAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var exposures = await db.DeviceVulnerabilityExposures
            .Where(e => e.TenantId == tenantId)
            .ToListAsync(ct);

        var exposureIds = exposures.Select(e => e.Id).ToList();
        var episodesByExposure = await db.ExposureEpisodes
            .Where(e => exposureIds.Contains(e.DeviceVulnerabilityExposureId))
            .ToListAsync(ct);

        var lookup = episodesByExposure
            .GroupBy(e => e.DeviceVulnerabilityExposureId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.EpisodeNumber).ToList());

        foreach (var exposure in exposures)
        {
            var episodes = lookup.TryGetValue(exposure.Id, out var list) ? list : [];
            var latest = episodes.FirstOrDefault();

            if (exposure.Status == ExposureStatus.Open)
            {
                if (latest is null || latest.ClosedAt is not null)
                {
                    var nextNumber = (latest?.EpisodeNumber ?? 0) + 1;
                    db.ExposureEpisodes.Add(ExposureEpisode.Open(tenantId, exposure.Id, nextNumber, exposure.LastObservedAt));
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
    }
}
