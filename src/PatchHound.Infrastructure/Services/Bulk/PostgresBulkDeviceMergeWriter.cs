using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Bulk;

/// <summary>
/// Postgres-native bulk writer for the canonical <c>Devices</c> and
/// <c>InstalledSoftware</c> tables used by <c>StagedDeviceMergeService</c>.
/// Uses temp-table + COPY BINARY + INSERT ... ON CONFLICT to avoid per-row
/// SELECT/UPDATE round-trips and EF change-tracking overhead.
/// </summary>
public sealed class PostgresBulkDeviceMergeWriter(PatchHoundDbContext db) : IBulkDeviceMergeWriter
{
    public async Task<IReadOnlyDictionary<(Guid SourceSystemId, string ExternalId), Guid>>
        UpsertDevicesAsync(IReadOnlyCollection<DeviceMergeRow> rows, CancellationToken ct)
    {
        ValidateDeviceLengths(rows);

        var map = new Dictionary<(Guid, string), Guid>(rows.Count);
        if (rows.Count == 0) return map;

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var result = new Dictionary<(Guid, string), Guid>(rows.Count);
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen)
                await connection.OpenAsync(ct);

            try
            {
                await using var tx = await connection.BeginTransactionAsync(ct);

                await using (var create = new NpgsqlCommand("""
                    CREATE TEMP TABLE IF NOT EXISTS _device_upsert (
                        id uuid,
                        tenant_id uuid,
                        source_system_id uuid,
                        external_id text,
                        name text,
                        computer_dns_name text,
                        health_status text,
                        os_platform text,
                        os_version text,
                        external_risk_label text,
                        last_seen_at timestamptz,
                        last_ip_address text,
                        aad_device_id text,
                        group_id text,
                        group_name text,
                        exposure_level text,
                        is_aad_joined boolean,
                        onboarding_status text,
                        device_value text,
                        is_active boolean
                    ) ON COMMIT DROP;
                    TRUNCATE _device_upsert;
                    """, connection, tx))
                {
                    await create.ExecuteNonQueryAsync(ct);
                }

                await using (var copy = await connection.BeginBinaryImportAsync(
                    "COPY _device_upsert (id, tenant_id, source_system_id, external_id, name, computer_dns_name, health_status, os_platform, os_version, external_risk_label, last_seen_at, last_ip_address, aad_device_id, group_id, group_name, exposure_level, is_aad_joined, onboarding_status, device_value, is_active) FROM STDIN (FORMAT BINARY)", ct))
                {
                    foreach (var r in rows)
                    {
                        await copy.StartRowAsync(ct);
                        // Pre-allocate a new id; ON CONFLICT will reuse the existing row's id.
                        await copy.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.TenantId, NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.SourceSystemId, NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.ExternalId, NpgsqlDbType.Text, ct);
                        await copy.WriteAsync(r.Name, NpgsqlDbType.Text, ct);
                        await WriteNullable(copy, r.ComputerDnsName, ct);
                        await WriteNullable(copy, r.HealthStatus, ct);
                        await WriteNullable(copy, r.OsPlatform, ct);
                        await WriteNullable(copy, r.OsVersion, ct);
                        await WriteNullable(copy, r.ExternalRiskLabel, ct);
                        if (r.LastSeenAt is { } ls) await copy.WriteAsync(ls, NpgsqlDbType.TimestampTz, ct); else await copy.WriteNullAsync(ct);
                        await WriteNullable(copy, r.LastIpAddress, ct);
                        await WriteNullable(copy, r.AadDeviceId, ct);
                        await WriteNullable(copy, r.GroupId, ct);
                        await WriteNullable(copy, r.GroupName, ct);
                        await WriteNullable(copy, r.ExposureLevel, ct);
                        if (r.IsAadJoined is { } aj) await copy.WriteAsync(aj, NpgsqlDbType.Boolean, ct); else await copy.WriteNullAsync(ct);
                        await WriteNullable(copy, r.OnboardingStatus, ct);
                        await WriteNullable(copy, r.DeviceValue, ct);
                        await copy.WriteAsync(r.IsActive, NpgsqlDbType.Boolean, ct);
                    }
                    await copy.CompleteAsync(ct);
                }

                // INSERT ... ON CONFLICT on the unique index (TenantId, SourceSystemId, ExternalId).
                // For new rows we set canonical defaults (BaselineCriticality=Medium, OwnerType=User, Metadata="{}").
                // For conflicts we update only the mutable inventory-detail columns and ActiveInTenant.
                // We deliberately do NOT touch Criticality / Owner / SecurityProfile / Description on update
                // — those are owned by separate services.
                await using var merge = new NpgsqlCommand(transaction: tx, connection: connection, cmdText: """
                    INSERT INTO "Devices"
                        ("Id", "TenantId", "SourceSystemId", "ExternalId", "Name",
                         "BaselineCriticality", "Criticality", "CriticalitySource", "CriticalityUpdatedAt",
                         "OwnerType", "ActiveInTenant", "Metadata",
                         "ComputerDnsName", "HealthStatus", "OsPlatform", "OsVersion",
                         "ExternalRiskLabel", "LastSeenAt", "LastIpAddress", "AadDeviceId",
                         "GroupId", "GroupName", "ExposureLevel", "IsAadJoined",
                         "OnboardingStatus", "DeviceValue")
                    SELECT
                        id, tenant_id, source_system_id, external_id, name,
                        'Medium', 'Medium', 'Default', NOW(),
                        'User', is_active, '{}',
                        computer_dns_name, health_status, os_platform, os_version,
                        external_risk_label, last_seen_at, last_ip_address, aad_device_id,
                        group_id, group_name, exposure_level, is_aad_joined,
                        onboarding_status, device_value
                    FROM _device_upsert
                    ON CONFLICT ("TenantId", "SourceSystemId", "ExternalId") DO UPDATE SET
                        "Name"              = EXCLUDED."Name",
                        "ComputerDnsName"   = EXCLUDED."ComputerDnsName",
                        "HealthStatus"      = EXCLUDED."HealthStatus",
                        "OsPlatform"        = EXCLUDED."OsPlatform",
                        "OsVersion"         = EXCLUDED."OsVersion",
                        "ExternalRiskLabel" = EXCLUDED."ExternalRiskLabel",
                        "LastSeenAt"        = EXCLUDED."LastSeenAt",
                        "LastIpAddress"     = EXCLUDED."LastIpAddress",
                        "AadDeviceId"       = EXCLUDED."AadDeviceId",
                        "GroupId"           = EXCLUDED."GroupId",
                        "GroupName"         = EXCLUDED."GroupName",
                        "ExposureLevel"     = EXCLUDED."ExposureLevel",
                        "IsAadJoined"       = EXCLUDED."IsAadJoined",
                        "OnboardingStatus"  = EXCLUDED."OnboardingStatus",
                        "DeviceValue"       = EXCLUDED."DeviceValue",
                        "ActiveInTenant"    = EXCLUDED."ActiveInTenant"
                    RETURNING "Id", "SourceSystemId", "ExternalId";
                    """);

                await using var reader = await merge.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetGuid(0);
                    var sourceSystemId = reader.GetGuid(1);
                    var externalId = reader.GetString(2);
                    result[(sourceSystemId, externalId)] = id;
                }
                await reader.CloseAsync();
                await tx.CommitAsync(ct);
                return (IReadOnlyDictionary<(Guid, string), Guid>)result;
            }
            finally
            {
                if (!wasOpen)
                    await connection.CloseAsync();
            }
        });
    }

    public async Task<int> UpsertInstalledSoftwareAsync(
        IReadOnlyCollection<InstalledSoftwareMergeRow> rows,
        CancellationToken ct)
    {
        ValidateInstalledSoftwareLengths(rows);

        if (rows.Count == 0) return 0;

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

                await using (var create = new NpgsqlCommand("""
                    CREATE TEMP TABLE IF NOT EXISTS _installed_software_upsert (
                        id uuid,
                        tenant_id uuid,
                        device_id uuid,
                        software_product_id uuid,
                        source_system_id uuid,
                        version text,
                        observed_at timestamptz,
                        run_id uuid
                    ) ON COMMIT DROP;
                    TRUNCATE _installed_software_upsert;
                    """, connection, tx))
                {
                    await create.ExecuteNonQueryAsync(ct);
                }

                await using (var copy = await connection.BeginBinaryImportAsync(
                    "COPY _installed_software_upsert (id, tenant_id, device_id, software_product_id, source_system_id, version, observed_at, run_id) FROM STDIN (FORMAT BINARY)", ct))
                {
                    foreach (var r in rows)
                    {
                        await copy.StartRowAsync(ct);
                        await copy.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.TenantId, NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.DeviceId, NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.SoftwareProductId, NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.SourceSystemId, NpgsqlDbType.Uuid, ct);
                        await copy.WriteAsync(r.Version ?? string.Empty, NpgsqlDbType.Text, ct);
                        await copy.WriteAsync(r.ObservedAt, NpgsqlDbType.TimestampTz, ct);
                        await copy.WriteAsync(r.RunId, NpgsqlDbType.Uuid, ct);
                    }
                    await copy.CompleteAsync(ct);
                }

                // Unique index: (TenantId, DeviceId, SoftwareProductId, SourceSystemId, Version)
                await using var merge = new NpgsqlCommand(transaction: tx, connection: connection, cmdText: """
                    WITH upsert AS (
                        INSERT INTO "InstalledSoftware"
                            ("Id", "TenantId", "DeviceId", "SoftwareProductId", "SourceSystemId",
                             "Version", "FirstSeenAt", "LastSeenAt", "LastSeenRunId")
                        SELECT id, tenant_id, device_id, software_product_id, source_system_id,
                               version, observed_at, observed_at, run_id
                        FROM _installed_software_upsert
                        ON CONFLICT ("TenantId", "DeviceId", "SoftwareProductId", "SourceSystemId", "Version")
                        DO UPDATE SET
                            "LastSeenAt" = GREATEST(EXCLUDED."LastSeenAt", "InstalledSoftware"."LastSeenAt"),
                            "LastSeenRunId" = EXCLUDED."LastSeenRunId"
                        RETURNING 1
                    )
                    SELECT COUNT(*) FROM upsert;
                    """);

                var touched = Convert.ToInt32(await merge.ExecuteScalarAsync(ct));
                await tx.CommitAsync(ct);
                return touched;
            }
            finally
            {
                if (!wasOpen)
                    await connection.CloseAsync();
            }
        });
    }

    private static async Task WriteNullable(NpgsqlBinaryImporter copy, string? value, CancellationToken ct)
    {
        if (value is null)
            await copy.WriteNullAsync(ct);
        else
            await copy.WriteAsync(value, NpgsqlDbType.Text, ct);
    }

    private static void ValidateDeviceLengths(IReadOnlyCollection<DeviceMergeRow> rows)
    {
        foreach (var r in rows)
        {
            Check(nameof(r.ExternalId), r.ExternalId, 256);
            Check(nameof(r.Name), r.Name, 256);
            Check(nameof(r.ComputerDnsName), r.ComputerDnsName, 256);
            Check(nameof(r.HealthStatus), r.HealthStatus, 64);
            Check(nameof(r.OsPlatform), r.OsPlatform, 128);
            Check(nameof(r.OsVersion), r.OsVersion, 128);
            Check(nameof(r.ExternalRiskLabel), r.ExternalRiskLabel, 64);
            Check(nameof(r.LastIpAddress), r.LastIpAddress, 128);
            Check(nameof(r.AadDeviceId), r.AadDeviceId, 128);
            Check(nameof(r.GroupId), r.GroupId, 128);
            Check(nameof(r.GroupName), r.GroupName, 256);
            Check(nameof(r.ExposureLevel), r.ExposureLevel, 64);
            Check(nameof(r.OnboardingStatus), r.OnboardingStatus, 64);
            Check(nameof(r.DeviceValue), r.DeviceValue, 64);
        }

        static void Check(string column, string? value, int max)
        {
            if (value is not null && value.Length > max)
                throw new ArgumentException(
                    $"Device.{column} exceeds {max} chars ({value.Length}).",
                    nameof(rows));
        }
    }

    private static void ValidateInstalledSoftwareLengths(IReadOnlyCollection<InstalledSoftwareMergeRow> rows)
    {
        foreach (var r in rows)
        {
            if (r.Version?.Length > 128)
                throw new ArgumentException(
                    $"InstalledSoftware.Version exceeds 128 chars ({r.Version.Length}) for device {r.DeviceId}.",
                    nameof(rows));
            if (r.RunId == Guid.Empty)
                throw new ArgumentException(
                    $"InstalledSoftware.RunId is required for device {r.DeviceId}.",
                    nameof(rows));
        }
    }
}
