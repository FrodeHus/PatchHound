using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceTenantSettingsWithTenantSourceConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantSlaConfigurations",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriticalDays = table.Column<int>(type: "integer", nullable: false),
                    HighDays = table.Column<int>(type: "integer", nullable: false),
                    MediumDays = table.Column<int>(type: "integer", nullable: false),
                    LowDays = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSlaConfigurations", x => x.TenantId);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantSourceConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SyncSchedule = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    CredentialTenantId = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    ClientId = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    SecretRef = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                    ApiBaseUrl = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                    TokenScope = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                    ManualRequestedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastStartedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastCompletedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastSucceededAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastStatus = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    LastError = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSourceConfigurations", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantSourceConfigurations_TenantId_SourceKey",
                table: "TenantSourceConfigurations",
                columns: new[] { "TenantId", "SourceKey" },
                unique: true
            );

            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS pgcrypto;
                """
            );

            migrationBuilder.Sql(
                """
                INSERT INTO "TenantSourceConfigurations" (
                    "Id",
                    "TenantId",
                    "SourceKey",
                    "DisplayName",
                    "Enabled",
                    "SyncSchedule",
                    "CredentialTenantId",
                    "ClientId",
                    "SecretRef",
                    "ApiBaseUrl",
                    "TokenScope",
                    "ManualRequestedAt",
                    "LastStartedAt",
                    "LastCompletedAt",
                    "LastSucceededAt",
                    "LastStatus",
                    "LastError"
                )
                SELECT
                    gen_random_uuid(),
                    tenant."Id",
                    source ->> 'key',
                    COALESCE(NULLIF(source ->> 'displayName', ''), source ->> 'key'),
                    COALESCE((source ->> 'enabled')::boolean, false),
                    COALESCE(NULLIF(source ->> 'syncSchedule', ''), ''),
                    COALESCE(source -> 'credentials' ->> 'tenantId', ''),
                    COALESCE(source -> 'credentials' ->> 'clientId', ''),
                    COALESCE(source -> 'credentials' ->> 'secretRef', ''),
                    COALESCE(source -> 'credentials' ->> 'apiBaseUrl', ''),
                    COALESCE(source -> 'credentials' ->> 'tokenScope', ''),
                    NULLIF(source -> 'runtime' ->> 'manualRequestedAt', '')::timestamp with time zone,
                    NULLIF(source -> 'runtime' ->> 'lastStartedAt', '')::timestamp with time zone,
                    NULLIF(source -> 'runtime' ->> 'lastCompletedAt', '')::timestamp with time zone,
                    NULLIF(source -> 'runtime' ->> 'lastSucceededAt', '')::timestamp with time zone,
                    COALESCE(source -> 'runtime' ->> 'lastStatus', ''),
                    COALESCE(source -> 'runtime' ->> 'lastError', '')
                FROM "Tenants" AS tenant
                CROSS JOIN LATERAL jsonb_array_elements(
                    CASE
                        WHEN jsonb_typeof(COALESCE(tenant."Settings", '{}')::jsonb -> 'ingestionSources') = 'array'
                            THEN COALESCE(tenant."Settings", '{}')::jsonb -> 'ingestionSources'
                        ELSE '[]'::jsonb
                    END
                ) AS source
                WHERE source ->> 'key' IN ('microsoft-defender', 'nvd');
                """
            );

            migrationBuilder.Sql(
                """
                INSERT INTO "TenantSourceConfigurations" (
                    "Id",
                    "TenantId",
                    "SourceKey",
                    "DisplayName",
                    "Enabled",
                    "SyncSchedule",
                    "CredentialTenantId",
                    "ClientId",
                    "SecretRef",
                    "ApiBaseUrl",
                    "TokenScope",
                    "LastStatus",
                    "LastError"
                )
                SELECT
                    gen_random_uuid(),
                    tenant."Id",
                    'microsoft-defender',
                    'Microsoft Defender',
                    false,
                    '0 */6 * * *',
                    '',
                    '',
                    '',
                    'https://api.securitycenter.microsoft.com',
                    'https://api.securitycenter.microsoft.com/.default',
                    '',
                    ''
                FROM "Tenants" AS tenant
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "TenantSourceConfigurations" AS source
                    WHERE source."TenantId" = tenant."Id"
                      AND source."SourceKey" = 'microsoft-defender'
                );
                """
            );

            migrationBuilder.Sql(
                """
                INSERT INTO "TenantSourceConfigurations" (
                    "Id",
                    "TenantId",
                    "SourceKey",
                    "DisplayName",
                    "Enabled",
                    "SyncSchedule",
                    "CredentialTenantId",
                    "ClientId",
                    "SecretRef",
                    "ApiBaseUrl",
                    "TokenScope",
                    "LastStatus",
                    "LastError"
                )
                SELECT
                    gen_random_uuid(),
                    tenant."Id",
                    'nvd',
                    'NVD API',
                    false,
                    '',
                    '',
                    '',
                    '',
                    'https://services.nvd.nist.gov',
                    '',
                    '',
                    ''
                FROM "Tenants" AS tenant
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "TenantSourceConfigurations" AS source
                    WHERE source."TenantId" = tenant."Id"
                      AND source."SourceKey" = 'nvd'
                );
                """
            );

            migrationBuilder.Sql(
                """
                INSERT INTO "TenantSlaConfigurations" (
                    "TenantId",
                    "CriticalDays",
                    "HighDays",
                    "MediumDays",
                    "LowDays"
                )
                SELECT
                    tenant."Id",
                    COALESCE(NULLIF(COALESCE(tenant."Settings", '{}')::jsonb -> 'slaDays' ->> 'Critical', '')::integer, 7),
                    COALESCE(NULLIF(COALESCE(tenant."Settings", '{}')::jsonb -> 'slaDays' ->> 'High', '')::integer, 30),
                    COALESCE(NULLIF(COALESCE(tenant."Settings", '{}')::jsonb -> 'slaDays' ->> 'Medium', '')::integer, 90),
                    COALESCE(NULLIF(COALESCE(tenant."Settings", '{}')::jsonb -> 'slaDays' ->> 'Low', '')::integer, 180)
                FROM "Tenants" AS tenant;
                """
            );

            migrationBuilder.DropColumn(name: "Settings", table: "Tenants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantSlaConfigurations");

            migrationBuilder.DropTable(name: "TenantSourceConfigurations");

            migrationBuilder.AddColumn<string>(
                name: "Settings",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.Sql(
                """
                UPDATE "Tenants" AS tenant
                SET "Settings" = jsonb_build_object(
                    'slaDays',
                    jsonb_build_object(
                        'Critical', COALESCE(sla."CriticalDays", 7),
                        'High', COALESCE(sla."HighDays", 30),
                        'Medium', COALESCE(sla."MediumDays", 90),
                        'Low', COALESCE(sla."LowDays", 180)
                    ),
                    'ingestionSources',
                    COALESCE((
                        SELECT jsonb_agg(
                            jsonb_build_object(
                                'key', source."SourceKey",
                                'displayName', source."DisplayName",
                                'enabled', source."Enabled",
                                'syncSchedule', source."SyncSchedule",
                                'credentials', jsonb_build_object(
                                    'tenantId', source."CredentialTenantId",
                                    'clientId', source."ClientId",
                                    'secretRef', source."SecretRef",
                                    'apiBaseUrl', source."ApiBaseUrl",
                                    'tokenScope', source."TokenScope"
                                ),
                                'runtime', jsonb_build_object(
                                    'manualRequestedAt', source."ManualRequestedAt",
                                    'lastStartedAt', source."LastStartedAt",
                                    'lastCompletedAt', source."LastCompletedAt",
                                    'lastSucceededAt', source."LastSucceededAt",
                                    'lastStatus', source."LastStatus",
                                    'lastError', source."LastError"
                                )
                            )
                        )
                        FROM "TenantSourceConfigurations" AS source
                        WHERE source."TenantId" = tenant."Id"
                    ), '[]'::jsonb)
                )::text
                FROM "TenantSlaConfigurations" AS sla
                WHERE sla."TenantId" = tenant."Id";
                """
            );
        }
    }
}
