using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260324113000_ScopeRemediationToTenantSoftware")]
public partial class ScopeRemediationToTenantSoftware : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TenantSoftwareId",
            table: "RemediationDecisions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "TenantSoftwareId",
            table: "PatchingTasks",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "RemediationDecisions" AS rd
            SET "TenantSoftwareId" = scope."TenantSoftwareId"
            FROM (
                SELECT DISTINCT ON ("SoftwareAssetId")
                    "SoftwareAssetId",
                    "TenantSoftwareId"
                FROM "NormalizedSoftwareInstallations"
                ORDER BY "SoftwareAssetId", "LastSeenAt" DESC
            ) AS scope
            WHERE rd."TenantSoftwareId" IS NULL
              AND rd."SoftwareAssetId" = scope."SoftwareAssetId";
            """);

        migrationBuilder.Sql(
            """
            UPDATE "PatchingTasks" AS pt
            SET "TenantSoftwareId" = rd."TenantSoftwareId"
            FROM "RemediationDecisions" AS rd
            WHERE pt."TenantSoftwareId" IS NULL
              AND pt."RemediationDecisionId" = rd."Id";
            """);

        migrationBuilder.Sql(
            """
            UPDATE "PatchingTasks" AS pt
            SET "TenantSoftwareId" = scope."TenantSoftwareId"
            FROM (
                SELECT DISTINCT ON ("SoftwareAssetId")
                    "SoftwareAssetId",
                    "TenantSoftwareId"
                FROM "NormalizedSoftwareInstallations"
                ORDER BY "SoftwareAssetId", "LastSeenAt" DESC
            ) AS scope
            WHERE pt."TenantSoftwareId" IS NULL
              AND pt."SoftwareAssetId" = scope."SoftwareAssetId";
            """);

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM "RemediationDecisions" WHERE "TenantSoftwareId" IS NULL
                ) THEN
                    RAISE EXCEPTION 'Unable to backfill RemediationDecisions.TenantSoftwareId for all rows';
                END IF;

                IF EXISTS (
                    SELECT 1 FROM "PatchingTasks" WHERE "TenantSoftwareId" IS NULL
                ) THEN
                    RAISE EXCEPTION 'Unable to backfill PatchingTasks.TenantSoftwareId for all rows';
                END IF;
            END $$;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "TenantSoftwareId",
            table: "RemediationDecisions",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "TenantSoftwareId",
            table: "PatchingTasks",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.DropIndex(
            name: "IX_RemediationDecisions_TenantId_SoftwareAssetId",
            table: "RemediationDecisions");

        migrationBuilder.DropIndex(
            name: "IX_PatchingTasks_TenantId_SoftwareAssetId",
            table: "PatchingTasks");

        migrationBuilder.CreateIndex(
            name: "IX_RemediationDecisions_TenantId_TenantSoftwareId",
            table: "RemediationDecisions",
            columns: new[] { "TenantId", "TenantSoftwareId" });

        migrationBuilder.CreateIndex(
            name: "IX_PatchingTasks_TenantId_TenantSoftwareId",
            table: "PatchingTasks",
            columns: new[] { "TenantId", "TenantSoftwareId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RemediationDecisions_TenantId_TenantSoftwareId",
            table: "RemediationDecisions");

        migrationBuilder.DropIndex(
            name: "IX_PatchingTasks_TenantId_TenantSoftwareId",
            table: "PatchingTasks");

        migrationBuilder.CreateIndex(
            name: "IX_RemediationDecisions_TenantId_SoftwareAssetId",
            table: "RemediationDecisions",
            columns: new[] { "TenantId", "SoftwareAssetId" });

        migrationBuilder.CreateIndex(
            name: "IX_PatchingTasks_TenantId_SoftwareAssetId",
            table: "PatchingTasks",
            columns: new[] { "TenantId", "SoftwareAssetId" });

        migrationBuilder.DropColumn(
            name: "TenantSoftwareId",
            table: "RemediationDecisions");

        migrationBuilder.DropColumn(
            name: "TenantSoftwareId",
            table: "PatchingTasks");
    }
}
