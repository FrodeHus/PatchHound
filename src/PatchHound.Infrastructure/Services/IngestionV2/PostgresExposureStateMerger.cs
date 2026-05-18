using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.IngestionV2;

public sealed class PostgresExposureStateMerger(PatchHoundDbContext db) : IExposureStateMerger
{
    public async Task<DirectExposureMergeResult> MergeDirectExposuresAsync(
        Guid tenantId,
        Guid runId,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen)
            {
                await connection.OpenAsync(ct);
            }

            try
            {
                await using var tx = await connection.BeginTransactionAsync(ct);
                var exposures = await UpsertDirectExposuresAsync(connection, tx, tenantId, runId, ct);
                var resolved = await ResolveStaleDirectExposuresAsync(connection, tx, tenantId, runId, ct);
                await tx.CommitAsync(ct);
                return new DirectExposureMergeResult(exposures, resolved);
            }
            finally
            {
                if (!wasOpen)
                {
                    await connection.CloseAsync();
                }
            }
        });
    }

    private static Task<int> UpsertDirectExposuresAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid runId,
        CancellationToken ct)
    {
        return ExecuteScalarAsync(connection, tx, """
            WITH source_rows AS (
                SELECT DISTINCT ON (d."Id", v."Id")
                       d."Id" AS device_id,
                       v."Id" AS vulnerability_id,
                       ssi."SoftwareProductId" AS software_product_id,
                       i."Id" AS installed_software_id,
                       COALESCE(r."SoftwareVersion", '') AS matched_version,
                       r."ObservedAt"
                FROM "RawExposureObservations" r
                JOIN "Devices" d
                  ON d."TenantId" = r."TenantId"
                 AND d."SourceSystemId" = r."SourceSystemId"
                 AND d."ExternalId" = r."DeviceExternalId"
                JOIN "Vulnerabilities" v
                  ON v."ExternalId" = r."VulnerabilityExternalId"
                LEFT JOIN "SoftwareSourceIdentities" ssi
                  ON r."SoftwareExternalId" <> ''
                 AND ssi."SourceSystemId" = r."SourceSystemId"
                 AND ssi."ExternalId" = r."SoftwareExternalId"
                LEFT JOIN "InstalledSoftware" i
                  ON i."TenantId" = r."TenantId"
                 AND i."DeviceId" = d."Id"
                 AND i."SoftwareProductId" = ssi."SoftwareProductId"
                 AND i."SourceSystemId" = r."SourceSystemId"
                 AND i."Version" = COALESCE(r."SoftwareVersion", '')
                WHERE r."TenantId" = @tenantId
                  AND r."IngestionRunId" = @runId
                ORDER BY d."Id", v."Id", r."ObservedAt" DESC
            ),
            upserted AS (
                INSERT INTO "DeviceVulnerabilityExposures" (
                    "Id",
                    "TenantId",
                    "DeviceId",
                    "VulnerabilityId",
                    "SoftwareProductId",
                    "InstalledSoftwareId",
                    "MatchedVersion",
                    "MatchSource",
                    "Status",
                    "FirstObservedAt",
                    "LastObservedAt",
                    "ResolvedAt",
                    "LastSeenRunId")
                SELECT gen_random_uuid(),
                       @tenantId,
                       device_id,
                       vulnerability_id,
                       software_product_id,
                       installed_software_id,
                       matched_version,
                       'Product',
                       'Open',
                       "ObservedAt",
                       "ObservedAt",
                       NULL,
                       @runId
                FROM source_rows
                ON CONFLICT ("TenantId", "DeviceId", "VulnerabilityId") DO UPDATE SET
                    "SoftwareProductId" = EXCLUDED."SoftwareProductId",
                    "InstalledSoftwareId" = EXCLUDED."InstalledSoftwareId",
                    "MatchedVersion" = EXCLUDED."MatchedVersion",
                    "MatchSource" = EXCLUDED."MatchSource",
                    "Status" = 'Open',
                    "LastObservedAt" = GREATEST("DeviceVulnerabilityExposures"."LastObservedAt", EXCLUDED."LastObservedAt"),
                    "ResolvedAt" = NULL,
                    "LastSeenRunId" = EXCLUDED."LastSeenRunId"
                RETURNING 1
            )
            SELECT COUNT(*) FROM upserted;
            """, tenantId, runId, ct);
    }

    private static Task<int> ResolveStaleDirectExposuresAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid runId,
        CancellationToken ct)
    {
        return ExecuteScalarAsync(connection, tx, """
            WITH touched_devices AS (
                SELECT DISTINCT d."Id" AS device_id, r."SourceSystemId", MAX(r."ObservedAt") AS observed_at
                FROM "RawDeviceObservations" r
                JOIN "Devices" d
                  ON d."TenantId" = r."TenantId"
                 AND d."SourceSystemId" = r."SourceSystemId"
                 AND d."ExternalId" = r."ExternalId"
                WHERE r."TenantId" = @tenantId
                  AND r."IngestionRunId" = @runId
                GROUP BY d."Id", r."SourceSystemId"
            ),
            current_exposures AS (
                SELECT DISTINCT d."Id" AS device_id, v."Id" AS vulnerability_id
                FROM "RawExposureObservations" r
                JOIN "Devices" d
                  ON d."TenantId" = r."TenantId"
                 AND d."SourceSystemId" = r."SourceSystemId"
                 AND d."ExternalId" = r."DeviceExternalId"
                JOIN "Vulnerabilities" v
                  ON v."ExternalId" = r."VulnerabilityExternalId"
                WHERE r."TenantId" = @tenantId
                  AND r."IngestionRunId" = @runId
            ),
            resolved AS (
                UPDATE "DeviceVulnerabilityExposures" e
                SET "Status" = 'Resolved',
                    "ResolvedAt" = td.observed_at
                FROM touched_devices td
                JOIN "SourceSystems" ss
                  ON ss."Id" = td."SourceSystemId"
                JOIN "Vulnerabilities" v
                  ON v."Source" = ss."Key"
                WHERE e."TenantId" = @tenantId
                  AND e."DeviceId" = td.device_id
                  AND e."VulnerabilityId" = v."Id"
                  AND e."Status" = 'Open'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM current_exposures ce
                      WHERE ce.device_id = e."DeviceId"
                        AND ce.vulnerability_id = e."VulnerabilityId")
                RETURNING 1
            )
            SELECT COUNT(*) FROM resolved;
            """, tenantId, runId, ct);
    }

    private static async Task<int> ExecuteScalarAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        string sql,
        Guid tenantId,
        Guid runId,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("tenantId", NpgsqlDbType.Uuid, tenantId);
        cmd.Parameters.AddWithValue("runId", NpgsqlDbType.Uuid, runId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }
}
