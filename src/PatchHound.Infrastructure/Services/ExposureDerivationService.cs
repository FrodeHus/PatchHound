using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public sealed record ExposureDerivationResult(int Inserted, int Reobserved, int Resolved);

public class ExposureDerivationService(
    PatchHoundDbContext db,
    ILogger<ExposureDerivationService> logger)
{
    public async Task<ExposureDerivationResult> DeriveForTenantAsync(
        Guid tenantId,
        DateTimeOffset observedAt,
        CancellationToken ct)
    {
        var installs = await db.InstalledSoftware.AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .Select(i => new
            {
                i.Id,
                i.DeviceId,
                i.SoftwareProductId,
                MatchedVersion = i.Version,
                ProductCpe = db.SoftwareProducts
                    .Where(p => p.Id == i.SoftwareProductId)
                    .Select(p => p.PrimaryCpe23Uri)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var activePairs = new HashSet<(Guid DeviceId, Guid VulnerabilityId)>();
        var inserted = 0;
        var reobserved = 0;

        var existing = await db.DeviceVulnerabilityExposures
            .Where(e => e.TenantId == tenantId)
            .ToListAsync(ct);
        var existingByPair = existing.ToDictionary(e => (e.DeviceId, e.VulnerabilityId));

        if (installs.Count > 0)
        {
            var productIds = installs.Select(i => i.SoftwareProductId).Distinct().ToList();
            var cpes = installs.Where(i => !string.IsNullOrWhiteSpace(i.ProductCpe)).Select(i => i.ProductCpe!).Distinct().ToList();

            var apps = await db.VulnerabilityApplicabilities.AsNoTracking()
                .Where(a => a.Vulnerable && (
                    (a.SoftwareProductId != null && productIds.Contains(a.SoftwareProductId.Value)) ||
                    (a.SoftwareProductId == null && a.CpeCriteria != null && cpes.Contains(a.CpeCriteria))))
                .ToListAsync(ct);

            foreach (var install in installs)
            {
                var matchingApps = apps.Where(app =>
                    ((app.SoftwareProductId == install.SoftwareProductId)
                        || (app.SoftwareProductId == null
                            && !string.IsNullOrWhiteSpace(app.CpeCriteria)
                            && string.Equals(app.CpeCriteria, install.ProductCpe, StringComparison.OrdinalIgnoreCase)))
                    && VersionMatches(install.MatchedVersion, app));

                foreach (var app in matchingApps)
                {
                    var pair = (install.DeviceId, app.VulnerabilityId);
                    activePairs.Add(pair);

                    if (existingByPair.TryGetValue(pair, out var exposure))
                    {
                        // If a direct-report source (e.g. Defender) already resolved this
                        // exposure in the same ingestion cycle, respect that decision.
                        // Adding to activePairs prevents the bottom loop from re-resolving.
                        if (exposure.Status != ExposureStatus.Resolved)
                        {
                            exposure.Reobserve(observedAt);
                            reobserved++;
                        }
                        continue;
                    }

                    var matchSource = app.SoftwareProductId == install.SoftwareProductId
                        ? ExposureMatchSource.Product
                        : ExposureMatchSource.Cpe;

                    var fresh = DeviceVulnerabilityExposure.Observe(
                        tenantId,
                        install.DeviceId,
                        app.VulnerabilityId,
                        install.SoftwareProductId,
                        install.Id,
                        install.MatchedVersion,
                        matchSource,
                        observedAt);

                    db.DeviceVulnerabilityExposures.Add(fresh);
                    existingByPair[pair] = fresh;
                    inserted++;
                }
            }
        }

        var resolved = 0;
        foreach (var exposure in existing)
        {
            var pair = (exposure.DeviceId, exposure.VulnerabilityId);
            if (activePairs.Contains(pair) || exposure.Status == ExposureStatus.Resolved)
            {
                continue;
            }

            exposure.Resolve(observedAt);
            resolved++;
        }

        logger.LogInformation(
            "Derived exposures for tenant {TenantId}: inserted {Inserted}, reobserved {Reobserved}, resolved {Resolved}",
            tenantId,
            inserted,
            reobserved,
            resolved);

        return new ExposureDerivationResult(inserted, reobserved, resolved);
    }

    /// <summary>
    /// Returns true when the installed version satisfies every present predicate on
    /// the applicability. Unparseable versions (either side) fall back to a match
    /// so we don't silently drop a known-vulnerable product because of a non-numeric
    /// version string.
    /// </summary>
    internal static bool VersionMatches(string? installedVersion, VulnerabilityApplicability app)
    {
        var hasPredicate =
            !string.IsNullOrWhiteSpace(app.VersionStartIncluding)
            || !string.IsNullOrWhiteSpace(app.VersionStartExcluding)
            || !string.IsNullOrWhiteSpace(app.VersionEndIncluding)
            || !string.IsNullOrWhiteSpace(app.VersionEndExcluding);

        if (!hasPredicate)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(installedVersion)
            || !Version.TryParse(installedVersion, out var installed))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(app.VersionStartIncluding)
            && Version.TryParse(app.VersionStartIncluding, out var startInc)
            && installed < startInc)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(app.VersionStartExcluding)
            && Version.TryParse(app.VersionStartExcluding, out var startExc)
            && installed <= startExc)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(app.VersionEndIncluding)
            && Version.TryParse(app.VersionEndIncluding, out var endInc)
            && installed > endInc)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(app.VersionEndExcluding)
            && Version.TryParse(app.VersionEndExcluding, out var endExc)
            && installed >= endExc)
        {
            return false;
        }

        return true;
    }
}
