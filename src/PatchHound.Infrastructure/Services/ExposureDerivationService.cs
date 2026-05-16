using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public sealed record ExposureDerivationResult(int Inserted, int Reobserved, int Resolved);

public class ExposureDerivationService(
    PatchHoundDbContext db,
    ILogger<ExposureDerivationService> logger,
    IBulkExposureWriter bulkWriter)
{
    /// <summary>
    /// Row shape emitted by the server-side CTE (Postgres) or its LINQ fallback (InMemory).
    /// Carries enough information to (a) re-apply the client-side <see cref="VersionMatches"/>
    /// predicate that can't be expressed in pure SQL, and (b) build an <see cref="ExposureUpsertRow"/>.
    /// </summary>
    private sealed record DerivedExposureRow(
        Guid DeviceId,
        Guid VulnerabilityId,
        Guid SoftwareProductId,
        Guid InstalledSoftwareId,
        string? MatchedVersion,
        string MatchSource,
        string? VersionStartIncluding,
        string? VersionStartExcluding,
        string? VersionEndIncluding,
        string? VersionEndExcluding);

    public async Task<ExposureDerivationResult> DeriveForTenantAsync(
        Guid tenantId,
        DateTimeOffset observedAt,
        Guid runId,
        CancellationToken ct)
    {
        var derived = await LoadDerivedExposuresAsync(tenantId, runId, ct);

        var rows = new List<ExposureUpsertRow>(derived.Count);
        foreach (var d in derived)
        {
            if (!VersionMatches(
                    d.MatchedVersion,
                    d.VersionStartIncluding,
                    d.VersionStartExcluding,
                    d.VersionEndIncluding,
                    d.VersionEndExcluding))
            {
                continue;
            }

            rows.Add(new ExposureUpsertRow(
                tenantId,
                d.DeviceId,
                d.VulnerabilityId,
                // MatchSource encodes provenance: "Product" means we joined on SoftwareProductId,
                // so SoftwareProductId on the exposure should match the install's product. For
                // "Cpe" matches the applicability has no SoftwareProductId — we still record the
                // install's product since that's what was scanned on the device.
                d.SoftwareProductId,
                d.InstalledSoftwareId,
                d.MatchedVersion ?? string.Empty,
                d.MatchSource,
                observedAt,
                runId));
        }

        var bulkResult = await bulkWriter.UpsertAsync(rows, ct);
        var resolved = await bulkWriter.ResolveStaleAsync(tenantId, runId, observedAt, ct);

        logger.LogInformation(
            "Derived exposures for tenant {TenantId}: inserted {Inserted}, reobserved {Reobserved}, resolved {Resolved}",
            tenantId,
            bulkResult.Inserted,
            bulkResult.Reobserved,
            resolved);

        return new ExposureDerivationResult(bulkResult.Inserted, bulkResult.Reobserved, resolved);
    }

    /// <summary>
    /// Runs the install × applicability cross-join server-side as a single CTE on
    /// PostgreSQL. Falls back to a LINQ-shaped equivalent for the EF Core InMemory
    /// provider (used by legacy InMemory tests). Both paths emit the same row shape.
    /// </summary>
    private async Task<List<DerivedExposureRow>> LoadDerivedExposuresAsync(Guid tenantId, Guid runId, CancellationToken ct)
    {
        var provider = db.Database.ProviderName;
        if (provider == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return await LoadDerivedExposuresInMemoryAsync(tenantId, runId, ct);
        }

        return await LoadDerivedExposuresPostgresAsync(tenantId, runId, ct);
    }

    private async Task<List<DerivedExposureRow>> LoadDerivedExposuresPostgresAsync(Guid tenantId, Guid runId, CancellationToken ct)
    {
        // Single CTE — joins active installs to applicabilities by SoftwareProductId
        // first, with a CPE-equality fallback when the applicability has no product
        // key. Range predicates and Version.TryParse can't be expressed in pure SQL
        // (we'd need a semver parser) so VersionMatches stays client-side and runs
        // over this already-narrowed output.
        //
        // EF global query filter audit (raw SQL bypasses HasQueryFilter):
        //   - InstalledSoftware: filter is `IsSystemContext || AccessibleTenantIds.Contains(TenantId)`.
        //     Covered by the explicit `i."TenantId" = @tenantId` predicate below — this method
        //     is invoked per-tenant by the caller (which itself has authority to resolve tenantId).
        //   - SoftwareProducts: no global query filter (canonical/shared catalog entity).
        //   - VulnerabilityApplicabilities: no global query filter (canonical/shared catalog entity).
        // No additional predicates are needed.
        const string sql = """
            WITH active_installs AS (
                SELECT i."Id" AS installed_software_id,
                       i."DeviceId" AS device_id,
                       i."SoftwareProductId" AS software_product_id,
                       i."Version" AS matched_version,
                       p."PrimaryCpe23Uri" AS product_cpe
                FROM "InstalledSoftware" i
                LEFT JOIN "SoftwareProducts" p ON p."Id" = i."SoftwareProductId"
                WHERE i."TenantId" = @tenantId
                  AND i."LastSeenRunId" = @runId
            ),
            product_matches AS (
                SELECT ai.device_id,
                       a."VulnerabilityId" AS vulnerability_id,
                       ai.software_product_id,
                       ai.installed_software_id,
                       ai.matched_version,
                       'Product' AS match_source,
                       a."VersionStartIncluding" AS version_start_including,
                       a."VersionStartExcluding" AS version_start_excluding,
                       a."VersionEndIncluding" AS version_end_including,
                       a."VersionEndExcluding" AS version_end_excluding
                FROM active_installs ai
                JOIN "VulnerabilityApplicabilities" a
                  ON a."SoftwareProductId" = ai.software_product_id
                WHERE a."Vulnerable" = TRUE
            ),
            cpe_matches AS (
                SELECT ai.device_id,
                       a."VulnerabilityId" AS vulnerability_id,
                       ai.software_product_id,
                       ai.installed_software_id,
                       ai.matched_version,
                       'Cpe' AS match_source,
                       a."VersionStartIncluding" AS version_start_including,
                       a."VersionStartExcluding" AS version_start_excluding,
                       a."VersionEndIncluding" AS version_end_including,
                       a."VersionEndExcluding" AS version_end_excluding
                FROM active_installs ai
                JOIN "VulnerabilityApplicabilities" a
                  ON a."SoftwareProductId" IS NULL
                 AND a."CpeCriteria" IS NOT NULL
                 AND ai.product_cpe IS NOT NULL
                 AND lower(a."CpeCriteria") = lower(ai.product_cpe)
                WHERE a."Vulnerable" = TRUE
            )
            SELECT ai.device_id,
                   ai.vulnerability_id,
                   ai.software_product_id,
                   ai.installed_software_id,
                   ai.matched_version,
                   ai.match_source,
                   ai.version_start_including,
                   ai.version_start_excluding,
                   ai.version_end_including,
                   ai.version_end_excluding
            FROM product_matches ai
            UNION ALL
            SELECT ai.device_id,
                   ai.vulnerability_id,
                   ai.software_product_id,
                   ai.installed_software_id,
                   ai.matched_version,
                   ai.match_source,
                   ai.version_start_including,
                   ai.version_start_excluding,
                   ai.version_end_including,
                   ai.version_end_excluding
            FROM cpe_matches ai;
            """;

        var rows = new List<DerivedExposureRow>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(ct);
        try
        {
            // Intentional: no explicit transaction. This is a single SELECT statement;
            // PostgreSQL's default READ COMMITTED isolation provides a consistent
            // snapshot within the statement. Contrast with PostgresBulkExposureWriter,
            // which wraps multiple statements (temp table + insert/update) in an
            // explicit transaction because temp tables and atomicity require it.
            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("runId", runId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new DerivedExposureRow(
                    DeviceId: reader.GetGuid(0),
                    VulnerabilityId: reader.GetGuid(1),
                    SoftwareProductId: reader.GetGuid(2),
                    InstalledSoftwareId: reader.GetGuid(3),
                    MatchedVersion: reader.IsDBNull(4) ? null : reader.GetString(4),
                    MatchSource: reader.GetString(5),
                    VersionStartIncluding: reader.IsDBNull(6) ? null : reader.GetString(6),
                    VersionStartExcluding: reader.IsDBNull(7) ? null : reader.GetString(7),
                    VersionEndIncluding: reader.IsDBNull(8) ? null : reader.GetString(8),
                    VersionEndExcluding: reader.IsDBNull(9) ? null : reader.GetString(9)));
            }
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
        return rows;
    }

    /// <summary>
    /// LINQ fallback for the EF Core InMemory provider. Mirrors the CTE join shape:
    /// match on SoftwareProductId first, then fall back to case-insensitive CPE
    /// equality when the applicability has no product key. Range predicates are
    /// applied later by the caller via <see cref="VersionMatches"/>.
    /// </summary>
    private async Task<List<DerivedExposureRow>> LoadDerivedExposuresInMemoryAsync(Guid tenantId, Guid runId, CancellationToken ct)
    {
        var installs = await db.InstalledSoftware.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.LastSeenRunId == runId)
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

        if (installs.Count == 0) return new List<DerivedExposureRow>();

        var productIds = installs.Select(i => i.SoftwareProductId).Distinct().ToList();
        var cpes = installs.Where(i => !string.IsNullOrWhiteSpace(i.ProductCpe))
            .Select(i => i.ProductCpe!).Distinct().ToList();

        var apps = await db.VulnerabilityApplicabilities.AsNoTracking()
            .Where(a => a.Vulnerable && (
                (a.SoftwareProductId != null && productIds.Contains(a.SoftwareProductId.Value)) ||
                (a.SoftwareProductId == null && a.CpeCriteria != null && cpes.Contains(a.CpeCriteria))))
            .ToListAsync(ct);

        var rows = new List<DerivedExposureRow>();
        foreach (var install in installs)
        {
            foreach (var app in apps)
            {
                var productMatch = app.SoftwareProductId == install.SoftwareProductId;
                var cpeMatch = app.SoftwareProductId == null
                    && !string.IsNullOrWhiteSpace(app.CpeCriteria)
                    && string.Equals(app.CpeCriteria, install.ProductCpe, StringComparison.OrdinalIgnoreCase);

                if (!productMatch && !cpeMatch) continue;

                rows.Add(new DerivedExposureRow(
                    DeviceId: install.DeviceId,
                    VulnerabilityId: app.VulnerabilityId,
                    SoftwareProductId: install.SoftwareProductId,
                    InstalledSoftwareId: install.Id,
                    MatchedVersion: install.MatchedVersion,
                    MatchSource: productMatch ? nameof(ExposureMatchSource.Product) : nameof(ExposureMatchSource.Cpe),
                    VersionStartIncluding: app.VersionStartIncluding,
                    VersionStartExcluding: app.VersionStartExcluding,
                    VersionEndIncluding: app.VersionEndIncluding,
                    VersionEndExcluding: app.VersionEndExcluding));
            }
        }
        return rows;
    }

    /// <summary>
    /// Returns true when the installed version satisfies every present predicate on
    /// the applicability. Unparseable versions (either side) fall back to a match
    /// so we don't silently drop a known-vulnerable product because of a non-numeric
    /// version string.
    /// </summary>
    internal static bool VersionMatches(string? installedVersion, VulnerabilityApplicability app)
        => VersionMatches(
            installedVersion,
            app.VersionStartIncluding,
            app.VersionStartExcluding,
            app.VersionEndIncluding,
            app.VersionEndExcluding);

    internal static bool VersionMatches(
        string? installedVersion,
        string? versionStartIncluding,
        string? versionStartExcluding,
        string? versionEndIncluding,
        string? versionEndExcluding)
    {
        var hasPredicate =
            !string.IsNullOrWhiteSpace(versionStartIncluding)
            || !string.IsNullOrWhiteSpace(versionStartExcluding)
            || !string.IsNullOrWhiteSpace(versionEndIncluding)
            || !string.IsNullOrWhiteSpace(versionEndExcluding);

        if (!hasPredicate)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(installedVersion)
            || !Version.TryParse(installedVersion, out var installed))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(versionStartIncluding)
            && Version.TryParse(versionStartIncluding, out var startInc)
            && installed < startInc)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(versionStartExcluding)
            && Version.TryParse(versionStartExcluding, out var startExc)
            && installed <= startExc)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(versionEndIncluding)
            && Version.TryParse(versionEndIncluding, out var endInc)
            && installed > endInc)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(versionEndExcluding)
            && Version.TryParse(versionEndExcluding, out var endExc)
            && installed >= endExc)
        {
            return false;
        }

        return true;
    }
}
