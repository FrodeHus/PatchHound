using Microsoft.EntityFrameworkCore;
using Npgsql;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Bulk;

/// <summary>
/// Postgres-native bulk writer for the normalized software projection.
/// Replaces per-row <c>AddAsync</c> + dual <c>SaveChangesAsync</c> loops with
/// set-based SQL that reads directly from the already-persisted
/// <c>InstalledSoftware</c> table.
///
/// Because the unique indexes include the nullable <c>SnapshotId</c> column —
/// and PostgreSQL treats NULL values in unique indexes as distinct — we cannot
/// use <c>INSERT ... ON CONFLICT</c> when <c>SnapshotId IS NULL</c>. Instead,
/// each sync runs as a deterministic UPDATE-then-INSERT-NOT-EXISTS pair inside
/// a single transaction, using <c>IS NOT DISTINCT FROM</c> to match the
/// snapshot column.
/// </summary>
public sealed class PostgresBulkSoftwareProjectionWriter(PatchHoundDbContext db)
    : IBulkSoftwareProjectionWriter
{
    public async Task SyncTenantSoftwareAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen)
                await connection.OpenAsync(ct);

            try
            {
                await using var tx = await connection.BeginTransactionAsync(ct);

                // 1. Extend observation window for existing rows whose product still has installs.
                await using (var update = new NpgsqlCommand("""
                    UPDATE "SoftwareTenantRecords" r
                    SET "FirstSeenAt" = LEAST(r."FirstSeenAt", agg.first_seen),
                        "LastSeenAt"  = GREATEST(r."LastSeenAt", agg.last_seen),
                        "UpdatedAt"   = GREATEST(r."UpdatedAt", agg.last_seen)
                    FROM (
                        SELECT i."SoftwareProductId",
                               MIN(i."FirstSeenAt") AS first_seen,
                               MAX(i."LastSeenAt")  AS last_seen
                        FROM "InstalledSoftware" i
                        WHERE i."TenantId" = @tenantId
                        GROUP BY i."SoftwareProductId"
                    ) agg
                    WHERE r."TenantId" = @tenantId
                      AND r."SnapshotId" IS NOT DISTINCT FROM @snapshotId
                      AND r."SoftwareProductId" = agg."SoftwareProductId";
                    """, connection, tx))
                {
                    update.Parameters.AddWithValue("tenantId", tenantId);
                    AddNullableUuid(update, "snapshotId", snapshotId);
                    await update.ExecuteNonQueryAsync(ct);
                }

                // 2. Insert new rows for products that don't yet have a SoftwareTenantRecord
                //    for this (tenant, snapshot) pair.
                await using (var insert = new NpgsqlCommand("""
                    INSERT INTO "SoftwareTenantRecords" (
                        "Id", "TenantId", "SnapshotId", "SoftwareProductId",
                        "FirstSeenAt", "LastSeenAt",
                        "RemediationAiSummaryContent", "RemediationAiSummaryInputHash",
                        "RemediationAiSummaryProviderType", "RemediationAiSummaryProfileName",
                        "RemediationAiSummaryModel",
                        "RemediationAiOwnerRecommendationContent",
                        "RemediationAiAnalystAssessmentContent",
                        "RemediationAiExceptionRecommendationContent",
                        "RemediationAiRecommendedOutcome",
                        "RemediationAiRecommendedPriority",
                        "RemediationAiReviewStatus",
                        "CreatedAt", "UpdatedAt")
                    SELECT gen_random_uuid(), @tenantId, @snapshotId, agg."SoftwareProductId",
                           agg.first_seen, agg.last_seen,
                           '', '', '', '', '', '', '', '', '', '', '',
                           agg.first_seen, agg.last_seen
                    FROM (
                        SELECT i."SoftwareProductId",
                               MIN(i."FirstSeenAt") AS first_seen,
                               MAX(i."LastSeenAt")  AS last_seen
                        FROM "InstalledSoftware" i
                        WHERE i."TenantId" = @tenantId
                        GROUP BY i."SoftwareProductId"
                    ) agg
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "SoftwareTenantRecords" r
                        WHERE r."TenantId" = @tenantId
                          AND r."SnapshotId" IS NOT DISTINCT FROM @snapshotId
                          AND r."SoftwareProductId" = agg."SoftwareProductId"
                    );
                    """, connection, tx))
                {
                    insert.Parameters.AddWithValue("tenantId", tenantId);
                    AddNullableUuid(insert, "snapshotId", snapshotId);
                    await insert.ExecuteNonQueryAsync(ct);
                }

                // 3. Delete stale rows: tenant records whose product no longer has any install.
                await using (var delete = new NpgsqlCommand("""
                    DELETE FROM "SoftwareTenantRecords" r
                    WHERE r."TenantId" = @tenantId
                      AND r."SnapshotId" IS NOT DISTINCT FROM @snapshotId
                      AND NOT EXISTS (
                        SELECT 1 FROM "InstalledSoftware" i
                        WHERE i."TenantId" = r."TenantId"
                          AND i."SoftwareProductId" = r."SoftwareProductId"
                      );
                    """, connection, tx))
                {
                    delete.Parameters.AddWithValue("tenantId", tenantId);
                    AddNullableUuid(delete, "snapshotId", snapshotId);
                    await delete.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }
            finally
            {
                if (!wasOpen)
                    await connection.CloseAsync();
            }
        });
    }

    public async Task SyncSoftwareInstallationsAsync(Guid tenantId, Guid? snapshotId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen)
                await connection.OpenAsync(ct);

            try
            {
                await using var tx = await connection.BeginTransactionAsync(ct);

                // 1. Update existing projection rows for installs that still exist.
                //    SourceSystem mapping mirrors NormalizedSoftwareProjectionService.ResolveSourceSystem.
                await using (var update = new NpgsqlCommand("""
                    UPDATE "SoftwareProductInstallations" p
                    SET "TenantSoftwareId"     = src."TenantSoftwareId",
                        "SourceSystem"         = src."SourceSystem",
                        "DetectedVersion"      = src."DetectedVersion",
                        "FirstSeenAt"          = LEAST(p."FirstSeenAt", src."FirstSeenAt"),
                        "LastSeenAt"           = GREATEST(p."LastSeenAt", src."LastSeenAt"),
                        "RemovedAt"            = NULL,
                        "IsActive"             = TRUE,
                        "CurrentEpisodeNumber" = GREATEST(1, p."CurrentEpisodeNumber")
                    FROM (
                        SELECT i."Id" AS "SoftwareAssetId",
                               i."DeviceId",
                               tr."Id" AS "TenantSoftwareId",
                               CASE WHEN LOWER(COALESCE(s."Key", '')) = 'authenticated-scan'
                                    THEN 'AuthenticatedScan'
                                    ELSE 'Defender' END AS "SourceSystem",
                               NULLIF(BTRIM(COALESCE(i."Version", '')), '') AS "DetectedVersion",
                               i."FirstSeenAt",
                               i."LastSeenAt"
                        FROM "InstalledSoftware" i
                        JOIN "SoftwareTenantRecords" tr
                          ON tr."TenantId" = i."TenantId"
                         AND tr."SoftwareProductId" = i."SoftwareProductId"
                         AND tr."SnapshotId" IS NOT DISTINCT FROM @snapshotId
                        LEFT JOIN "SourceSystems" s ON s."Id" = i."SourceSystemId"
                        WHERE i."TenantId" = @tenantId
                    ) src
                    WHERE p."TenantId" = @tenantId
                      AND p."SnapshotId" IS NOT DISTINCT FROM @snapshotId
                      AND p."SoftwareAssetId" = src."SoftwareAssetId";
                    """, connection, tx))
                {
                    update.Parameters.AddWithValue("tenantId", tenantId);
                    AddNullableUuid(update, "snapshotId", snapshotId);
                    await update.ExecuteNonQueryAsync(ct);
                }

                // 2. Insert new projection rows for installs that don't yet have one.
                await using (var insert = new NpgsqlCommand("""
                    INSERT INTO "SoftwareProductInstallations" (
                        "Id", "TenantId", "SnapshotId", "TenantSoftwareId",
                        "SoftwareAssetId", "DeviceAssetId", "SourceSystem",
                        "DetectedVersion", "FirstSeenAt", "LastSeenAt",
                        "RemovedAt", "IsActive", "CurrentEpisodeNumber")
                    SELECT gen_random_uuid(), @tenantId, @snapshotId, src."TenantSoftwareId",
                           src."SoftwareAssetId", src."DeviceId", src."SourceSystem",
                           src."DetectedVersion", src."FirstSeenAt", src."LastSeenAt",
                           NULL, TRUE, 1
                    FROM (
                        SELECT i."Id" AS "SoftwareAssetId",
                               i."DeviceId",
                               tr."Id" AS "TenantSoftwareId",
                               CASE WHEN LOWER(COALESCE(s."Key", '')) = 'authenticated-scan'
                                    THEN 'AuthenticatedScan'
                                    ELSE 'Defender' END AS "SourceSystem",
                               NULLIF(BTRIM(COALESCE(i."Version", '')), '') AS "DetectedVersion",
                               i."FirstSeenAt",
                               i."LastSeenAt"
                        FROM "InstalledSoftware" i
                        JOIN "SoftwareTenantRecords" tr
                          ON tr."TenantId" = i."TenantId"
                         AND tr."SoftwareProductId" = i."SoftwareProductId"
                         AND tr."SnapshotId" IS NOT DISTINCT FROM @snapshotId
                        LEFT JOIN "SourceSystems" s ON s."Id" = i."SourceSystemId"
                        WHERE i."TenantId" = @tenantId
                    ) src
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "SoftwareProductInstallations" p
                        WHERE p."TenantId" = @tenantId
                          AND p."SnapshotId" IS NOT DISTINCT FROM @snapshotId
                          AND p."SoftwareAssetId" = src."SoftwareAssetId"
                    );
                    """, connection, tx))
                {
                    insert.Parameters.AddWithValue("tenantId", tenantId);
                    AddNullableUuid(insert, "snapshotId", snapshotId);
                    await insert.ExecuteNonQueryAsync(ct);
                }

                // 3. Mark stale projection rows inactive — installs that no longer exist
                //    in the canonical InstalledSoftware table.
                await using (var deactivate = new NpgsqlCommand("""
                    UPDATE "SoftwareProductInstallations" p
                    SET "IsActive"  = FALSE,
                        "RemovedAt" = COALESCE(p."RemovedAt", now())
                    WHERE p."TenantId" = @tenantId
                      AND p."SnapshotId" IS NOT DISTINCT FROM @snapshotId
                      AND p."IsActive" = TRUE
                      AND NOT EXISTS (
                        SELECT 1 FROM "InstalledSoftware" i
                        WHERE i."Id" = p."SoftwareAssetId"
                      );
                    """, connection, tx))
                {
                    deactivate.Parameters.AddWithValue("tenantId", tenantId);
                    AddNullableUuid(deactivate, "snapshotId", snapshotId);
                    await deactivate.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }
            finally
            {
                if (!wasOpen)
                    await connection.CloseAsync();
            }
        });
    }

    private static void AddNullableUuid(NpgsqlCommand cmd, string name, Guid? value)
    {
        var p = cmd.Parameters.Add(name, NpgsqlTypes.NpgsqlDbType.Uuid);
        p.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}
