using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Bulk;

public sealed class PostgresBulkExposureWriter(PatchHoundDbContext db) : IBulkExposureWriter
{
    public async Task<BulkExposureUpsertResult> UpsertAsync(
        IReadOnlyCollection<ExposureUpsertRow> rows,
        CancellationToken ct)
    {
        ValidateLengths(rows);

        if (rows.Count == 0) return new BulkExposureUpsertResult(0, 0);

        // Wrap in EF execution strategy because Npgsql is configured with EnableRetryOnFailure;
        // opening an explicit ADO.NET transaction otherwise throws under the retrying strategy.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen)
                await connection.OpenAsync(ct);

            try
            {
                await using var tx = await connection.BeginTransactionAsync(ct);

                // ON COMMIT DROP guarantees the temp table is gone at the start of each new
                // transaction; IF NOT EXISTS is defensive against in-transaction retries where
                // the same session re-enters this block before commit/rollback.
                await using (var create = new NpgsqlCommand("""
                    CREATE TEMP TABLE IF NOT EXISTS _exposure_upsert (
                        id uuid, tenant_id uuid, device_id uuid, vulnerability_id uuid,
                        software_product_id uuid, installed_software_id uuid,
                        matched_version text, match_source text, observed_at timestamptz,
                        run_id uuid
                    ) ON COMMIT DROP;
                    TRUNCATE _exposure_upsert;
                    """, connection, tx))
                {
                    await create.ExecuteNonQueryAsync(ct);
                }

                await using (var copy = await connection.BeginBinaryImportAsync(
                    "COPY _exposure_upsert (id, tenant_id, device_id, vulnerability_id, software_product_id, installed_software_id, matched_version, match_source, observed_at, run_id) FROM STDIN (FORMAT BINARY)", ct))
                {
                    foreach (var r in rows)
                    {
                        await copy.StartRowAsync(ct);
                        await copy.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.TenantId, NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.DeviceId, NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.VulnerabilityId, NpgsqlDbType.Uuid, ct);
                        if (r.SoftwareProductId is { } sp) await copy.WriteAsync(sp, NpgsqlDbType.Uuid, ct); else await copy.WriteNullAsync(ct);
                        if (r.InstalledSoftwareId is { } isw) await copy.WriteAsync(isw, NpgsqlDbType.Uuid, ct); else await copy.WriteNullAsync(ct);
                        await copy.WriteAsync(r.MatchedVersion, NpgsqlDbType.Text, ct);
                        await copy.WriteAsync(r.MatchSource, NpgsqlDbType.Text, ct);
                        await copy.WriteAsync(r.ObservedAt, NpgsqlDbType.TimestampTz, ct);
                        await copy.WriteAsync(r.RunId, NpgsqlDbType.Uuid, ct);
                    }
                    await copy.CompleteAsync(ct);
                }

                await using var merge = new NpgsqlCommand(transaction: tx, connection: connection, cmdText: """
                    WITH upsert AS (
                        INSERT INTO "DeviceVulnerabilityExposures"
                            ("Id", "TenantId", "DeviceId", "VulnerabilityId",
                             "SoftwareProductId", "InstalledSoftwareId",
                             "MatchedVersion", "MatchSource", "Status",
                             "FirstObservedAt", "LastObservedAt", "ResolvedAt", "LastSeenRunId")
                        SELECT id, tenant_id, device_id, vulnerability_id,
                               software_product_id, installed_software_id,
                               matched_version, match_source, 'Open',
                               observed_at, observed_at, NULL, run_id
                        FROM _exposure_upsert
                        ON CONFLICT ("TenantId", "DeviceId", "VulnerabilityId")
                        DO UPDATE SET
                            "LastObservedAt" = GREATEST(EXCLUDED."LastObservedAt", "DeviceVulnerabilityExposures"."LastObservedAt"),
                            "Status"         = CASE WHEN "DeviceVulnerabilityExposures"."Status" = 'Resolved'
                                                    THEN 'Resolved'
                                                    ELSE 'Open' END,
                            "ResolvedAt"     = CASE WHEN "DeviceVulnerabilityExposures"."Status" = 'Resolved'
                                                    THEN "DeviceVulnerabilityExposures"."ResolvedAt"
                                                    ELSE NULL END,
                            "LastSeenRunId"  = EXCLUDED."LastSeenRunId"
                        RETURNING (xmax = 0) AS inserted
                    )
                    SELECT
                        COALESCE(SUM(CASE WHEN inserted THEN 1 ELSE 0 END), 0) AS inserted_count,
                        COALESCE(SUM(CASE WHEN NOT inserted THEN 1 ELSE 0 END), 0) AS updated_count
                    FROM upsert;
                    """);

                await using var reader = await merge.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);
                var inserted = Convert.ToInt32(reader["inserted_count"]);
                var updated = Convert.ToInt32(reader["updated_count"]);
                await reader.CloseAsync();
                await tx.CommitAsync(ct);
                return new BulkExposureUpsertResult(inserted, updated);
            }
            finally
            {
                if (!wasOpen)
                    await connection.CloseAsync();
            }
        });
    }

    public async Task<int> ResolveStaleAsync(Guid tenantId, Guid runId, DateTimeOffset resolvedAt, CancellationToken ct)
    {
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "DeviceVulnerabilityExposures"
            SET "Status" = 'Resolved', "ResolvedAt" = {resolvedAt}
            WHERE "TenantId" = {tenantId}
              AND "Status" = 'Open'
              AND ("LastSeenRunId" IS DISTINCT FROM {runId})
            """, ct);
    }

    private static void ValidateLengths(IReadOnlyCollection<ExposureUpsertRow> rows)
    {
        foreach (var r in rows)
        {
            if (r.MatchedVersion?.Length > 128)
                throw new ArgumentException(
                    $"MatchedVersion exceeds 128 chars ({r.MatchedVersion.Length}) for device {r.DeviceId} vuln {r.VulnerabilityId}",
                    nameof(rows));
            if (r.MatchSource?.Length > 16)
                throw new ArgumentException(
                    $"MatchSource exceeds 16 chars ({r.MatchSource.Length}) for device {r.DeviceId} vuln {r.VulnerabilityId}",
                    nameof(rows));
        }
    }
}
