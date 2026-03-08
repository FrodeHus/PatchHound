using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveEnrichmentSourcesToGlobalConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnrichmentSourceConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_EnrichmentSourceConfigurations", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentSourceConfigurations_SourceKey",
                table: "EnrichmentSourceConfigurations",
                column: "SourceKey",
                unique: true
            );

            migrationBuilder.Sql(
                $"""
                INSERT INTO "EnrichmentSourceConfigurations"
                    ("Id", "SourceKey", "DisplayName", "Enabled", "SecretRef", "ApiBaseUrl",
                     "LastStartedAt", "LastCompletedAt", "LastSucceededAt", "LastStatus", "LastError")
                SELECT
                    '{Guid.NewGuid()}',
                    'nvd',
                    COALESCE(source."DisplayName", 'NVD API'),
                    COALESCE(source."Enabled", FALSE),
                    COALESCE(source."SecretRef", ''),
                    COALESCE(source."ApiBaseUrl", 'https://services.nvd.nist.gov'),
                    source."LastStartedAt",
                    source."LastCompletedAt",
                    source."LastSucceededAt",
                    COALESCE(source."LastStatus", ''),
                    COALESCE(source."LastError", '')
                FROM (
                    SELECT *
                    FROM "TenantSourceConfigurations"
                    WHERE "SourceKey" = 'nvd'
                    ORDER BY
                        CASE WHEN "Enabled" THEN 0 ELSE 1 END,
                        CASE WHEN "SecretRef" <> '' THEN 0 ELSE 1 END,
                        "LastSucceededAt" DESC NULLS LAST
                    LIMIT 1
                ) AS source
                ON CONFLICT ("SourceKey") DO NOTHING;
                """
            );

            migrationBuilder.Sql(
                $"""
                INSERT INTO "EnrichmentSourceConfigurations"
                    ("Id", "SourceKey", "DisplayName", "Enabled", "SecretRef", "ApiBaseUrl",
                     "LastStartedAt", "LastCompletedAt", "LastSucceededAt", "LastStatus", "LastError")
                SELECT
                    '{Guid.NewGuid()}',
                    'nvd',
                    'NVD API',
                    FALSE,
                    '',
                    'https://services.nvd.nist.gov',
                    NULL,
                    NULL,
                    NULL,
                    '',
                    ''
                WHERE NOT EXISTS (
                    SELECT 1 FROM "EnrichmentSourceConfigurations" WHERE "SourceKey" = 'nvd'
                );
                """
            );

            migrationBuilder.Sql(
                """
                DELETE FROM "TenantSourceConfigurations"
                WHERE "SourceKey" = 'nvd';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                INSERT INTO "TenantSourceConfigurations"
                    ("Id", "TenantId", "SourceKey", "DisplayName", "Enabled", "SyncSchedule",
                     "CredentialTenantId", "ClientId", "SecretRef", "ApiBaseUrl", "TokenScope",
                     "ManualRequestedAt", "LastStartedAt", "LastCompletedAt", "LastSucceededAt", "LastStatus", "LastError")
                SELECT
                    tenant."Id",
                    tenant."Id",
                    'nvd',
                    COALESCE(source."DisplayName", 'NVD API'),
                    COALESCE(source."Enabled", FALSE),
                    '',
                    '',
                    '',
                    COALESCE(source."SecretRef", ''),
                    COALESCE(source."ApiBaseUrl", 'https://services.nvd.nist.gov'),
                    '',
                    NULL,
                    source."LastStartedAt",
                    source."LastCompletedAt",
                    source."LastSucceededAt",
                    COALESCE(source."LastStatus", ''),
                    COALESCE(source."LastError", '')
                FROM "Tenants" tenant
                CROSS JOIN LATERAL (
                    SELECT *
                    FROM "EnrichmentSourceConfigurations"
                    WHERE "SourceKey" = 'nvd'
                    LIMIT 1
                ) AS source
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "TenantSourceConfigurations" existing
                    WHERE existing."TenantId" = tenant."Id"
                      AND existing."SourceKey" = 'nvd'
                );
                """
            );

            migrationBuilder.DropTable(name: "EnrichmentSourceConfigurations");
        }
    }
}
