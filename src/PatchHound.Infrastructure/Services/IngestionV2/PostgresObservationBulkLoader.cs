using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.IngestionV2;

public sealed class PostgresObservationBulkLoader(PatchHoundDbContext db) : IObservationBulkLoader
{
    public async Task<ObservationLoadSummary> LoadAsync(IngestionObservationBatch batch, CancellationToken ct)
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
                await CreateTempTablesAsync(connection, tx, ct);
                await CopyDevicesAsync(connection, batch, ct);
                await CopySoftwareAsync(connection, batch, ct);
                await CopyInstallationsAsync(connection, batch, ct);
                await CopyVulnerabilitiesAsync(connection, batch, ct);
                await CopyExposuresAsync(connection, batch, ct);
                var summary = await MergeTempTablesAsync(connection, tx, ct);
                await tx.CommitAsync(ct);
                return summary;
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

    private static async Task CreateTempTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        CancellationToken ct)
    {
        await using var create = new NpgsqlCommand("""
            CREATE TEMP TABLE IF NOT EXISTS _raw_device_observation (
                id uuid,
                ingestion_run_id uuid,
                batch_number integer,
                tenant_id uuid,
                source_system_id uuid,
                external_id text,
                name text,
                computer_dns_name text,
                health_status text,
                os_platform text,
                os_version text,
                source_last_seen_at timestamptz,
                observed_at timestamptz
            ) ON COMMIT DROP;
            TRUNCATE _raw_device_observation;

            CREATE TEMP TABLE IF NOT EXISTS _raw_software_observation (
                id uuid,
                ingestion_run_id uuid,
                batch_number integer,
                tenant_id uuid,
                source_system_id uuid,
                external_id text,
                vendor text,
                name text,
                version text,
                canonical_product_key text,
                observed_at timestamptz
            ) ON COMMIT DROP;
            TRUNCATE _raw_software_observation;

            CREATE TEMP TABLE IF NOT EXISTS _raw_installation_observation (
                id uuid,
                ingestion_run_id uuid,
                batch_number integer,
                tenant_id uuid,
                source_system_id uuid,
                device_external_id text,
                software_external_id text,
                version text,
                observed_at timestamptz
            ) ON COMMIT DROP;
            TRUNCATE _raw_installation_observation;

            CREATE TEMP TABLE IF NOT EXISTS _raw_vulnerability_observation (
                id uuid,
                ingestion_run_id uuid,
                batch_number integer,
                tenant_id uuid,
                source_system_id uuid,
                external_id text,
                title text,
                description text,
                vendor_severity text,
                cvss_score numeric,
                cvss_vector text,
                published_date timestamptz,
                observed_at timestamptz
            ) ON COMMIT DROP;
            TRUNCATE _raw_vulnerability_observation;

            CREATE TEMP TABLE IF NOT EXISTS _raw_exposure_observation (
                id uuid,
                ingestion_run_id uuid,
                batch_number integer,
                tenant_id uuid,
                source_system_id uuid,
                device_external_id text,
                vulnerability_external_id text,
                software_external_id text,
                software_version text,
                observed_at timestamptz
            ) ON COMMIT DROP;
            TRUNCATE _raw_exposure_observation;
            """, connection, tx);
        await create.ExecuteNonQueryAsync(ct);
    }

    private static async Task CopyDevicesAsync(
        NpgsqlConnection connection,
        IngestionObservationBatch batch,
        CancellationToken ct)
    {
        if (batch.Devices.Count == 0) return;

        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY _raw_device_observation (id, ingestion_run_id, batch_number, tenant_id, source_system_id, external_id, name, computer_dns_name, health_status, os_platform, os_version, source_last_seen_at, observed_at) FROM STDIN (FORMAT BINARY)",
            ct);
        foreach (var item in batch.Devices)
        {
            await copy.StartRowAsync(ct);
            await copy.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(batch.IngestionRunId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(item.BatchNumber, NpgsqlDbType.Integer, ct);
            await copy.WriteAsync(batch.TenantId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(batch.SourceSystemId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(item.ExternalId, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.Name, NpgsqlDbType.Text, ct);
            await WriteNullableTextAsync(copy, item.ComputerDnsName, ct);
            await WriteNullableTextAsync(copy, item.HealthStatus, ct);
            await WriteNullableTextAsync(copy, item.OsPlatform, ct);
            await WriteNullableTextAsync(copy, item.OsVersion, ct);
            await WriteNullableTimestampAsync(copy, item.SourceLastSeenAt, ct);
            await copy.WriteAsync(item.ObservedAt, NpgsqlDbType.TimestampTz, ct);
        }
        await copy.CompleteAsync(ct);
    }

    private static async Task CopySoftwareAsync(
        NpgsqlConnection connection,
        IngestionObservationBatch batch,
        CancellationToken ct)
    {
        if (batch.Software.Count == 0) return;

        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY _raw_software_observation (id, ingestion_run_id, batch_number, tenant_id, source_system_id, external_id, vendor, name, version, canonical_product_key, observed_at) FROM STDIN (FORMAT BINARY)",
            ct);
        foreach (var item in batch.Software)
        {
            await copy.StartRowAsync(ct);
            await copy.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(batch.IngestionRunId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(item.BatchNumber, NpgsqlDbType.Integer, ct);
            await copy.WriteAsync(batch.TenantId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(batch.SourceSystemId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(item.ExternalId, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.Vendor, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.Name, NpgsqlDbType.Text, ct);
            await WriteNullableTextAsync(copy, item.Version, ct);
            await copy.WriteAsync(item.CanonicalProductKey, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.ObservedAt, NpgsqlDbType.TimestampTz, ct);
        }
        await copy.CompleteAsync(ct);
    }

    private static async Task CopyInstallationsAsync(
        NpgsqlConnection connection,
        IngestionObservationBatch batch,
        CancellationToken ct)
    {
        if (batch.Installations.Count == 0) return;

        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY _raw_installation_observation (id, ingestion_run_id, batch_number, tenant_id, source_system_id, device_external_id, software_external_id, version, observed_at) FROM STDIN (FORMAT BINARY)",
            ct);
        foreach (var item in batch.Installations)
        {
            await copy.StartRowAsync(ct);
            await copy.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(batch.IngestionRunId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(item.BatchNumber, NpgsqlDbType.Integer, ct);
            await copy.WriteAsync(batch.TenantId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(batch.SourceSystemId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(item.DeviceExternalId, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.SoftwareExternalId, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.Version, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.ObservedAt, NpgsqlDbType.TimestampTz, ct);
        }
        await copy.CompleteAsync(ct);
    }

    private static async Task CopyVulnerabilitiesAsync(
        NpgsqlConnection connection,
        IngestionObservationBatch batch,
        CancellationToken ct)
    {
        if (batch.Vulnerabilities.Count == 0) return;

        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY _raw_vulnerability_observation (id, ingestion_run_id, batch_number, tenant_id, source_system_id, external_id, title, description, vendor_severity, cvss_score, cvss_vector, published_date, observed_at) FROM STDIN (FORMAT BINARY)",
            ct);
        foreach (var item in batch.Vulnerabilities)
        {
            await copy.StartRowAsync(ct);
            await copy.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(batch.IngestionRunId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(item.BatchNumber, NpgsqlDbType.Integer, ct);
            await copy.WriteAsync(batch.TenantId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(batch.SourceSystemId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(item.ExternalId, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.Title, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.Description, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.VendorSeverity.ToString(), NpgsqlDbType.Text, ct);
            if (item.CvssScore is { } cvss) await copy.WriteAsync(cvss, NpgsqlDbType.Numeric, ct); else await copy.WriteNullAsync(ct);
            await WriteNullableTextAsync(copy, item.CvssVector, ct);
            await WriteNullableTimestampAsync(copy, item.PublishedDate, ct);
            await copy.WriteAsync(item.ObservedAt, NpgsqlDbType.TimestampTz, ct);
        }
        await copy.CompleteAsync(ct);
    }

    private static async Task CopyExposuresAsync(
        NpgsqlConnection connection,
        IngestionObservationBatch batch,
        CancellationToken ct)
    {
        if (batch.Exposures.Count == 0) return;

        await using var copy = await connection.BeginBinaryImportAsync(
            "COPY _raw_exposure_observation (id, ingestion_run_id, batch_number, tenant_id, source_system_id, device_external_id, vulnerability_external_id, software_external_id, software_version, observed_at) FROM STDIN (FORMAT BINARY)",
            ct);
        foreach (var item in batch.Exposures)
        {
            await copy.StartRowAsync(ct);
            await copy.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(batch.IngestionRunId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(item.BatchNumber, NpgsqlDbType.Integer, ct);
            await copy.WriteAsync(batch.TenantId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(batch.SourceSystemId, NpgsqlDbType.Uuid, ct);
            await copy.WriteAsync(item.DeviceExternalId, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.VulnerabilityExternalId, NpgsqlDbType.Text, ct);
            await copy.WriteAsync(item.SoftwareExternalId, NpgsqlDbType.Text, ct);
            await WriteNullableTextAsync(copy, item.SoftwareVersion, ct);
            await copy.WriteAsync(item.ObservedAt, NpgsqlDbType.TimestampTz, ct);
        }
        await copy.CompleteAsync(ct);
    }

    private static async Task<ObservationLoadSummary> MergeTempTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        CancellationToken ct)
    {
        var devices = await ExecuteScalarAsync(connection, tx, """
            INSERT INTO "RawDeviceObservations"
                ("Id", "IngestionRunId", "BatchNumber", "TenantId", "SourceSystemId",
                 "ExternalId", "Name", "ComputerDnsName", "HealthStatus", "OsPlatform",
                 "OsVersion", "SourceLastSeenAt", "ObservedAt")
            SELECT id, ingestion_run_id, batch_number, tenant_id, source_system_id,
                   external_id, name, computer_dns_name, health_status, os_platform,
                   os_version, source_last_seen_at, observed_at
            FROM _raw_device_observation
            ON CONFLICT ("IngestionRunId", "SourceSystemId", "ExternalId") DO UPDATE SET
                "BatchNumber" = EXCLUDED."BatchNumber",
                "Name" = EXCLUDED."Name",
                "ComputerDnsName" = EXCLUDED."ComputerDnsName",
                "HealthStatus" = EXCLUDED."HealthStatus",
                "OsPlatform" = EXCLUDED."OsPlatform",
                "OsVersion" = EXCLUDED."OsVersion",
                "SourceLastSeenAt" = EXCLUDED."SourceLastSeenAt",
                "ObservedAt" = GREATEST("RawDeviceObservations"."ObservedAt", EXCLUDED."ObservedAt")
            RETURNING 1;
            """, ct);

        var software = await ExecuteScalarAsync(connection, tx, """
            INSERT INTO "RawSoftwareObservations"
                ("Id", "IngestionRunId", "BatchNumber", "TenantId", "SourceSystemId",
                 "ExternalId", "Vendor", "Name", "Version", "CanonicalProductKey", "ObservedAt")
            SELECT id, ingestion_run_id, batch_number, tenant_id, source_system_id,
                   external_id, vendor, name, version, canonical_product_key, observed_at
            FROM _raw_software_observation
            ON CONFLICT ("IngestionRunId", "SourceSystemId", "ExternalId") DO UPDATE SET
                "BatchNumber" = EXCLUDED."BatchNumber",
                "Vendor" = EXCLUDED."Vendor",
                "Name" = EXCLUDED."Name",
                "Version" = EXCLUDED."Version",
                "CanonicalProductKey" = EXCLUDED."CanonicalProductKey",
                "ObservedAt" = GREATEST("RawSoftwareObservations"."ObservedAt", EXCLUDED."ObservedAt")
            RETURNING 1;
            """, ct);

        var installations = await ExecuteScalarAsync(connection, tx, """
            INSERT INTO "RawInstallationObservations"
                ("Id", "IngestionRunId", "BatchNumber", "TenantId", "SourceSystemId",
                 "DeviceExternalId", "SoftwareExternalId", "Version", "ObservedAt")
            SELECT id, ingestion_run_id, batch_number, tenant_id, source_system_id,
                   device_external_id, software_external_id, version, observed_at
            FROM _raw_installation_observation
            ON CONFLICT ("IngestionRunId", "SourceSystemId", "DeviceExternalId", "SoftwareExternalId") DO UPDATE SET
                "BatchNumber" = EXCLUDED."BatchNumber",
                "Version" = EXCLUDED."Version",
                "ObservedAt" = GREATEST("RawInstallationObservations"."ObservedAt", EXCLUDED."ObservedAt")
            RETURNING 1;
            """, ct);

        var vulnerabilities = await ExecuteScalarAsync(connection, tx, """
            INSERT INTO "RawVulnerabilityObservations"
                ("Id", "IngestionRunId", "BatchNumber", "TenantId", "SourceSystemId",
                 "ExternalId", "Title", "Description", "VendorSeverity", "CvssScore",
                 "CvssVector", "PublishedDate", "ObservedAt")
            SELECT id, ingestion_run_id, batch_number, tenant_id, source_system_id,
                   external_id, title, description, vendor_severity, cvss_score,
                   cvss_vector, published_date, observed_at
            FROM _raw_vulnerability_observation
            ON CONFLICT ("IngestionRunId", "SourceSystemId", "ExternalId") DO UPDATE SET
                "BatchNumber" = EXCLUDED."BatchNumber",
                "Title" = EXCLUDED."Title",
                "Description" = EXCLUDED."Description",
                "VendorSeverity" = EXCLUDED."VendorSeverity",
                "CvssScore" = EXCLUDED."CvssScore",
                "CvssVector" = EXCLUDED."CvssVector",
                "PublishedDate" = EXCLUDED."PublishedDate",
                "ObservedAt" = GREATEST("RawVulnerabilityObservations"."ObservedAt", EXCLUDED."ObservedAt")
            RETURNING 1;
            """, ct);

        var exposures = await ExecuteScalarAsync(connection, tx, """
            INSERT INTO "RawExposureObservations"
                ("Id", "IngestionRunId", "BatchNumber", "TenantId", "SourceSystemId",
                 "DeviceExternalId", "VulnerabilityExternalId", "SoftwareExternalId",
                 "SoftwareVersion", "ObservedAt")
            SELECT id, ingestion_run_id, batch_number, tenant_id, source_system_id,
                   device_external_id, vulnerability_external_id, software_external_id,
                   software_version, observed_at
            FROM _raw_exposure_observation
            ON CONFLICT ("IngestionRunId", "SourceSystemId", "DeviceExternalId", "VulnerabilityExternalId", "SoftwareExternalId") DO UPDATE SET
                "BatchNumber" = EXCLUDED."BatchNumber",
                "SoftwareVersion" = EXCLUDED."SoftwareVersion",
                "ObservedAt" = GREATEST("RawExposureObservations"."ObservedAt", EXCLUDED."ObservedAt")
            RETURNING 1;
            """, ct);

        return new ObservationLoadSummary(devices, software, installations, vulnerabilities, exposures);
    }

    private static async Task<int> ExecuteScalarAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        string sql,
        CancellationToken ct)
    {
        var trimmed = sql.Trim().TrimEnd(';');
        await using var cmd = new NpgsqlCommand($"WITH _rows AS ({trimmed}) SELECT COUNT(*) FROM _rows;", connection, tx);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private static async Task WriteNullableTextAsync(
        NpgsqlBinaryImporter copy,
        string? value,
        CancellationToken ct)
    {
        if (value is null) await copy.WriteNullAsync(ct);
        else await copy.WriteAsync(value, NpgsqlDbType.Text, ct);
    }

    private static async Task WriteNullableTimestampAsync(
        NpgsqlBinaryImporter copy,
        DateTimeOffset? value,
        CancellationToken ct)
    {
        if (value is null) await copy.WriteNullAsync(ct);
        else await copy.WriteAsync(value.Value, NpgsqlDbType.TimestampTz, ct);
    }
}
