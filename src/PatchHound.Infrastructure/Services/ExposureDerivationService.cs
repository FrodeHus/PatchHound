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
        BulkExposureUpsertResult bulkResult;
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            bulkResult = await DeriveAndUpsertInMemoryAsync(tenantId, observedAt, runId, ct);
        }
        else
        {
            bulkResult = await DeriveAndUpsertPostgresAsync(tenantId, observedAt, runId, ct);
        }

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
    /// Single server-side statement: derives installs × applicabilities, filters by
    /// version range via the <c>patchhound_version_matches</c> SQL function, dedupes
    /// per (device, vulnerability), and upserts into <c>DeviceVulnerabilityExposures</c>.
    /// Returns insert/update counts via <c>(xmax = 0)</c>. Memory cost is O(1) on the
    /// worker — only the count row crosses the wire.
    /// </summary>
    private async Task<BulkExposureUpsertResult> DeriveAndUpsertPostgresAsync(
        Guid tenantId, DateTimeOffset observedAt, Guid runId, CancellationToken ct)
    {
        // One round-trip: derive (installs × applicabilities, product-keyed first, CPE
        // fallback when applicability has no product), filter by version range via the
        // patchhound_version_matches() SQL function, dedupe per (device, vulnerability)
        // with a deterministic preference for Product matches over CPE-fallback matches,
        // then upsert into DeviceVulnerabilityExposures. Insert vs. update is detected
        // via xmax = 0 in RETURNING — same trick PostgresBulkExposureWriter uses.
        //
        // EF global query filter audit (raw SQL bypasses HasQueryFilter):
        //   - InstalledSoftware: filter is `IsSystemContext || AccessibleTenantIds.Contains(TenantId)`.
        //     Covered by the explicit `i."TenantId" = @tenantId` predicate below — this method
        //     is invoked per-tenant by the caller (which itself has authority to resolve tenantId).
        //   - SoftwareProducts / VulnerabilityApplicabilities: no global query filter (shared catalog).
        //
        // Derivation is intentionally NOT scoped to "LastSeenRunId" = @runId. Each ingestion
        // source acquires its own run id; a per-run filter would exclude installs from every
        // other source's prior run, and ResolveStaleAsync would then resolve every other
        // source's exposures. Staleness of InstalledSoftware rows is a separate concern.
        const string sql = """
            WITH active_installs AS (
                SELECT i."Id"                 AS installed_software_id,
                       i."DeviceId"           AS device_id,
                       i."SoftwareProductId"  AS software_product_id,
                       i."Version"            AS matched_version,
                       p."PrimaryCpe23Uri"    AS product_cpe
                FROM "InstalledSoftware" i
                LEFT JOIN "SoftwareProducts" p ON p."Id" = i."SoftwareProductId"
                WHERE i."TenantId" = @tenantId
            ),
            product_matches AS (
                SELECT ai.device_id,
                       a."VulnerabilityId" AS vulnerability_id,
                       ai.software_product_id,
                       ai.installed_software_id,
                       ai.matched_version,
                       'Product' AS match_source,
                       a."VersionStartIncluding" AS vsi,
                       a."VersionStartExcluding" AS vse,
                       a."VersionEndIncluding"   AS vei,
                       a."VersionEndExcluding"   AS vee
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
                       a."VersionStartIncluding" AS vsi,
                       a."VersionStartExcluding" AS vse,
                       a."VersionEndIncluding"   AS vei,
                       a."VersionEndExcluding"   AS vee
                FROM active_installs ai
                JOIN "VulnerabilityApplicabilities" a
                  ON a."SoftwareProductId" IS NULL
                 AND a."CpeCriteria" IS NOT NULL
                 AND ai.product_cpe IS NOT NULL
                 AND lower(a."CpeCriteria") = lower(ai.product_cpe)
                WHERE a."Vulnerable" = TRUE
            ),
            candidates AS (
                SELECT * FROM product_matches
                UNION ALL
                SELECT * FROM cpe_matches
            ),
            in_range AS (
                SELECT c.*
                FROM candidates c
                WHERE patchhound_version_matches(c.matched_version, c.vsi, c.vse, c.vei, c.vee)
            ),
            deduped AS (
                SELECT DISTINCT ON (device_id, vulnerability_id)
                       device_id, vulnerability_id, software_product_id,
                       installed_software_id, matched_version, match_source
                FROM in_range
                -- Prefer Product matches over CPE fallback when both apply to the same (device, vuln).
                ORDER BY device_id, vulnerability_id,
                         CASE match_source WHEN 'Product' THEN 0 ELSE 1 END
            ),
            upsert AS (
                INSERT INTO "DeviceVulnerabilityExposures"
                    ("Id", "TenantId", "DeviceId", "VulnerabilityId",
                     "SoftwareProductId", "InstalledSoftwareId",
                     "MatchedVersion", "MatchSource", "Status",
                     "FirstObservedAt", "LastObservedAt", "ResolvedAt", "LastSeenRunId")
                SELECT gen_random_uuid(), @tenantId, device_id, vulnerability_id,
                       software_product_id, installed_software_id,
                       COALESCE(matched_version, ''), match_source, 'Open',
                       @observedAt, @observedAt, NULL, @runId
                FROM deduped
                ON CONFLICT ("TenantId", "DeviceId", "VulnerabilityId")
                DO UPDATE SET
                    "LastObservedAt" = GREATEST(EXCLUDED."LastObservedAt", "DeviceVulnerabilityExposures"."LastObservedAt"),
                    "Status"         = 'Open',
                    "ResolvedAt"     = NULL,
                    "LastSeenRunId"  = EXCLUDED."LastSeenRunId"
                RETURNING (xmax = 0) AS inserted
            )
            SELECT
                COALESCE(SUM(CASE WHEN inserted THEN 1 ELSE 0 END), 0)::int AS inserted_count,
                COALESCE(SUM(CASE WHEN NOT inserted THEN 1 ELSE 0 END), 0)::int AS updated_count
            FROM upsert;
            """;

        // EF execution strategy wraps the (single) statement so Npgsql's retry-on-failure
        // policy can re-issue it on a transient error without partial visible state.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen) await connection.OpenAsync(ct);
            try
            {
                await using var cmd = new NpgsqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("tenantId", tenantId);
                cmd.Parameters.AddWithValue("observedAt", observedAt);
                cmd.Parameters.AddWithValue("runId", runId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    return new BulkExposureUpsertResult(0, 0);
                }
                var inserted = reader.GetInt32(0);
                var updated = reader.GetInt32(1);
                return new BulkExposureUpsertResult(inserted, updated);
            }
            finally
            {
                if (!wasOpen) await connection.CloseAsync();
            }
        });
    }

    /// <summary>
    /// InMemory provider path: keeps the LINQ derivation + buffered <see cref="IBulkExposureWriter.UpsertAsync"/>
    /// flow used by tests. Production traffic always goes through the Postgres path.
    /// </summary>
    private async Task<BulkExposureUpsertResult> DeriveAndUpsertInMemoryAsync(
        Guid tenantId, DateTimeOffset observedAt, Guid runId, CancellationToken ct)
    {
        var derived = await LoadDerivedExposuresInMemoryAsync(tenantId, runId, ct);

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
                d.SoftwareProductId,
                d.InstalledSoftwareId,
                d.MatchedVersion ?? string.Empty,
                d.MatchSource,
                observedAt,
                runId));
        }

        return await bulkWriter.UpsertAsync(rows, ct);
    }

    /// <summary>
    /// LINQ fallback for the EF Core InMemory provider. Mirrors the CTE join shape:
    /// match on SoftwareProductId first, then fall back to case-insensitive CPE
    /// equality when the applicability has no product key. Range predicates are
    /// applied later by the caller via <see cref="VersionMatches"/>.
    /// </summary>
    private async Task<List<DerivedExposureRow>> LoadDerivedExposuresInMemoryAsync(Guid tenantId, Guid runId, CancellationToken ct)
    {
        // See note in LoadDerivedExposuresPostgresAsync: derivation must NOT be
        // scoped by LastSeenRunId, because each ingestion source uses its own run id.
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
