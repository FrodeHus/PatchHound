using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.IngestionV2;

public sealed class PostgresIngestionStateMerger(PatchHoundDbContext db) : IIngestionStateMerger
{
    public async Task<SoftwareStateMergeResult> MergeSoftwareAsync(
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
                var products = await UpsertSoftwareProductsAsync(connection, tx, tenantId, runId, ct);
                var releases = await UpsertSoftwareReleasesAsync(connection, tx, tenantId, runId, ct);
                var sourceIdentities = await UpsertSoftwareSourceIdentitiesAsync(connection, tx, tenantId, runId, ct);
                var productDeltas = await InsertProductDeltasAsync(connection, tx, tenantId, runId, ct);
                await tx.CommitAsync(ct);
                return new SoftwareStateMergeResult(products, releases, sourceIdentities, productDeltas);
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

    private static Task<int> UpsertSoftwareProductsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid runId,
        CancellationToken ct)
    {
        return ExecuteScalarAsync(connection, tx, """
            WITH source_rows AS (
                SELECT DISTINCT ON ("CanonicalProductKey")
                       "CanonicalProductKey",
                       "Vendor",
                       "Name",
                       "ObservedAt"
                FROM "RawSoftwareObservations"
                WHERE "TenantId" = @tenantId
                  AND "IngestionRunId" = @runId
                ORDER BY "CanonicalProductKey", "ObservedAt" DESC
            ),
            upserted AS (
                INSERT INTO "SoftwareProducts" (
                    "Id",
                    "CanonicalProductKey",
                    "Vendor",
                    "Name",
                    "CreatedAt",
                    "UpdatedAt",
                    "NormalizationMethod",
                    "Confidence",
                    "LastEvaluatedAt",
                    "SupplyChainRemediationPath",
                    "SupplyChainInsightConfidence")
                SELECT gen_random_uuid(),
                       source_rows."CanonicalProductKey",
                       source_rows."Vendor",
                       source_rows."Name",
                       source_rows."ObservedAt",
                       source_rows."ObservedAt",
                       0,
                       1,
                       source_rows."ObservedAt",
                       0,
                       0
                FROM source_rows
                ON CONFLICT ("CanonicalProductKey") DO UPDATE SET
                    "Vendor" = EXCLUDED."Vendor",
                    "Name" = EXCLUDED."Name",
                    "UpdatedAt" = GREATEST("SoftwareProducts"."UpdatedAt", EXCLUDED."UpdatedAt"),
                    "LastEvaluatedAt" = GREATEST("SoftwareProducts"."LastEvaluatedAt", EXCLUDED."LastEvaluatedAt")
                RETURNING 1
            )
            SELECT COUNT(*) FROM upserted;
            """, tenantId, runId, ct);
    }

    private static Task<int> UpsertSoftwareReleasesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid runId,
        CancellationToken ct)
    {
        return ExecuteScalarAsync(connection, tx, """
            WITH source_rows AS (
                SELECT DISTINCT ON (p."Id", COALESCE(r."Version", ''))
                       p."Id" AS software_product_id,
                       COALESCE(r."Version", '') AS raw_version,
                       lower(COALESCE(r."Version", '')) AS normalized_version,
                       r."ObservedAt"
                FROM "RawSoftwareObservations" r
                JOIN "SoftwareProducts" p
                  ON p."CanonicalProductKey" = r."CanonicalProductKey"
                WHERE r."TenantId" = @tenantId
                  AND r."IngestionRunId" = @runId
                ORDER BY p."Id", COALESCE(r."Version", ''), r."ObservedAt" DESC
            ),
            upserted AS (
                INSERT INTO "SoftwareReleases" (
                    "Id",
                    "SoftwareProductId",
                    "NormalizedVersion",
                    "RawVersion",
                    "FirstSeenAt",
                    "LastSeenAt")
                SELECT gen_random_uuid(),
                       software_product_id,
                       normalized_version,
                       raw_version,
                       "ObservedAt",
                       "ObservedAt"
                FROM source_rows
                ON CONFLICT ("SoftwareProductId", "NormalizedVersion") DO UPDATE SET
                    "RawVersion" = EXCLUDED."RawVersion",
                    "FirstSeenAt" = LEAST("SoftwareReleases"."FirstSeenAt", EXCLUDED."FirstSeenAt"),
                    "LastSeenAt" = GREATEST("SoftwareReleases"."LastSeenAt", EXCLUDED."LastSeenAt")
                RETURNING 1
            )
            SELECT COUNT(*) FROM upserted;
            """, tenantId, runId, ct);
    }

    private static Task<int> UpsertSoftwareSourceIdentitiesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid runId,
        CancellationToken ct)
    {
        return ExecuteScalarAsync(connection, tx, """
            WITH source_rows AS (
                SELECT DISTINCT ON (r."SourceSystemId", r."ExternalId")
                       r."SourceSystemId",
                       r."ExternalId",
                       r."Vendor",
                       r."Name",
                       r."Version",
                       r."CanonicalProductKey",
                       p."Id" AS software_product_id,
                       sr."Id" AS software_release_id,
                       r."ObservedAt"
                FROM "RawSoftwareObservations" r
                JOIN "SoftwareProducts" p
                  ON p."CanonicalProductKey" = r."CanonicalProductKey"
                JOIN "SoftwareReleases" sr
                  ON sr."SoftwareProductId" = p."Id"
                 AND sr."NormalizedVersion" = lower(COALESCE(r."Version", ''))
                WHERE r."TenantId" = @tenantId
                  AND r."IngestionRunId" = @runId
                ORDER BY r."SourceSystemId", r."ExternalId", r."ObservedAt" DESC
            ),
            upserted AS (
                INSERT INTO "SoftwareSourceIdentities" (
                    "Id",
                    "SourceSystemId",
                    "ExternalId",
                    "ObservedVendor",
                    "ObservedName",
                    "ObservedVersion",
                    "CanonicalProductKey",
                    "SoftwareProductId",
                    "SoftwareReleaseId",
                    "FirstSeenAt",
                    "LastSeenAt")
                SELECT gen_random_uuid(),
                       "SourceSystemId",
                       "ExternalId",
                       "Vendor",
                       "Name",
                       "Version",
                       "CanonicalProductKey",
                       software_product_id,
                       software_release_id,
                       "ObservedAt",
                       "ObservedAt"
                FROM source_rows
                ON CONFLICT ("SourceSystemId", "ExternalId") DO UPDATE SET
                    "ObservedVendor" = EXCLUDED."ObservedVendor",
                    "ObservedName" = EXCLUDED."ObservedName",
                    "ObservedVersion" = EXCLUDED."ObservedVersion",
                    "CanonicalProductKey" = EXCLUDED."CanonicalProductKey",
                    "SoftwareProductId" = EXCLUDED."SoftwareProductId",
                    "SoftwareReleaseId" = EXCLUDED."SoftwareReleaseId",
                    "FirstSeenAt" = LEAST("SoftwareSourceIdentities"."FirstSeenAt", EXCLUDED."FirstSeenAt"),
                    "LastSeenAt" = GREATEST("SoftwareSourceIdentities"."LastSeenAt", EXCLUDED."LastSeenAt")
                RETURNING 1
            )
            SELECT COUNT(*) FROM upserted;
            """, tenantId, runId, ct);
    }

    private static Task<int> InsertProductDeltasAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid runId,
        CancellationToken ct)
    {
        return ExecuteScalarAsync(connection, tx, """
            WITH source_rows AS (
                SELECT DISTINCT p."Id" AS entity_id, MAX(r."ObservedAt") AS observed_at
                FROM "RawSoftwareObservations" r
                JOIN "SoftwareProducts" p
                  ON p."CanonicalProductKey" = r."CanonicalProductKey"
                WHERE r."TenantId" = @tenantId
                  AND r."IngestionRunId" = @runId
                GROUP BY p."Id"
            ),
            inserted AS (
                INSERT INTO "IngestionRunDeltas" ("Id", "RunId", "TenantId", "Kind", "EntityId", "CreatedAt")
                SELECT gen_random_uuid(), @runId, @tenantId, 'SoftwareProduct', entity_id, observed_at
                FROM source_rows
                ON CONFLICT ("RunId", "Kind", "EntityId") DO NOTHING
                RETURNING 1
            )
            SELECT COUNT(*) FROM inserted;
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
