using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260308161500_AddIngestionRunsAndSourceLeases")]
    public partial class AddIngestionRunsAndSourceLeases : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "TenantSourceConfigurations"
                ADD COLUMN IF NOT EXISTS "ActiveIngestionRunId" uuid;
                """
            );

            migrationBuilder.Sql(
                """
                ALTER TABLE "TenantSourceConfigurations"
                ADD COLUMN IF NOT EXISTS "LeaseAcquiredAt" timestamp with time zone;
                """
            );

            migrationBuilder.Sql(
                """
                ALTER TABLE "TenantSourceConfigurations"
                ADD COLUMN IF NOT EXISTS "LeaseExpiresAt" timestamp with time zone;
                """
            );

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "IngestionRuns" (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "SourceKey" character varying(128) NOT NULL,
                    "StartedAt" timestamp with time zone NOT NULL,
                    "CompletedAt" timestamp with time zone NULL,
                    "Status" character varying(64) NOT NULL,
                    "FetchedVulnerabilityCount" integer NOT NULL,
                    "FetchedAssetCount" integer NOT NULL,
                    "FetchedSoftwareInstallationCount" integer NOT NULL,
                    "Error" character varying(512) NOT NULL,
                    CONSTRAINT "PK_IngestionRuns" PRIMARY KEY ("Id")
                );
                """
            );

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_IngestionRuns_TenantId_SourceKey_StartedAt"
                ON "IngestionRuns" ("TenantId", "SourceKey", "StartedAt");
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "IngestionRuns";""");

            migrationBuilder.Sql(
                """
                ALTER TABLE "TenantSourceConfigurations"
                DROP COLUMN IF EXISTS "ActiveIngestionRunId";
                """
            );

            migrationBuilder.Sql(
                """
                ALTER TABLE "TenantSourceConfigurations"
                DROP COLUMN IF EXISTS "LeaseAcquiredAt";
                """
            );

            migrationBuilder.Sql(
                """
                ALTER TABLE "TenantSourceConfigurations"
                DROP COLUMN IF EXISTS "LeaseExpiresAt";
                """
            );
        }
    }
}
