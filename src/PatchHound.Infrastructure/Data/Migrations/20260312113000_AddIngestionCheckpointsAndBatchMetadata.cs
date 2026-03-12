using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260312113000_AddIngestionCheckpointsAndBatchMetadata")]
public partial class AddIngestionCheckpointsAndBatchMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "BatchNumber",
            table: "StagedAssets",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "BatchNumber",
            table: "StagedDeviceSoftwareInstallations",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "BatchNumber",
            table: "StagedVulnerabilities",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "BatchNumber",
            table: "StagedVulnerabilityExposures",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "IngestionCheckpoints",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Phase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                BatchNumber = table.Column<int>(type: "integer", nullable: false),
                CursorJson = table.Column<string>(type: "text", nullable: false),
                RecordsCommitted = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                LastCommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IngestionCheckpoints", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IngestionCheckpoints_IngestionRunId",
            table: "IngestionCheckpoints",
            column: "IngestionRunId");

        migrationBuilder.CreateIndex(
            name: "IX_IngestionCheckpoints_IngestionRunId_Phase",
            table: "IngestionCheckpoints",
            columns: new[] { "IngestionRunId", "Phase" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IngestionCheckpoints_TenantId_SourceKey_Phase",
            table: "IngestionCheckpoints",
            columns: new[] { "TenantId", "SourceKey", "Phase" });

        migrationBuilder.CreateIndex(
            name: "IX_StagedAssets_IngestionRunId_BatchNumber",
            table: "StagedAssets",
            columns: new[] { "IngestionRunId", "BatchNumber" });

        migrationBuilder.CreateIndex(
            name: "IX_StagedDeviceSoftwareInstallations_IngestionRunId_BatchNumber",
            table: "StagedDeviceSoftwareInstallations",
            columns: new[] { "IngestionRunId", "BatchNumber" });

        migrationBuilder.CreateIndex(
            name: "IX_StagedVulnerabilities_IngestionRunId_BatchNumber",
            table: "StagedVulnerabilities",
            columns: new[] { "IngestionRunId", "BatchNumber" });

        migrationBuilder.CreateIndex(
            name: "IX_StagedVulnerabilityExposures_IngestionRunId_BatchNumber",
            table: "StagedVulnerabilityExposures",
            columns: new[] { "IngestionRunId", "BatchNumber" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "IngestionCheckpoints");

        migrationBuilder.DropIndex(
            name: "IX_StagedAssets_IngestionRunId_BatchNumber",
            table: "StagedAssets");

        migrationBuilder.DropIndex(
            name: "IX_StagedDeviceSoftwareInstallations_IngestionRunId_BatchNumber",
            table: "StagedDeviceSoftwareInstallations");

        migrationBuilder.DropIndex(
            name: "IX_StagedVulnerabilities_IngestionRunId_BatchNumber",
            table: "StagedVulnerabilities");

        migrationBuilder.DropIndex(
            name: "IX_StagedVulnerabilityExposures_IngestionRunId_BatchNumber",
            table: "StagedVulnerabilityExposures");

        migrationBuilder.DropColumn(
            name: "BatchNumber",
            table: "StagedAssets");

        migrationBuilder.DropColumn(
            name: "BatchNumber",
            table: "StagedDeviceSoftwareInstallations");

        migrationBuilder.DropColumn(
            name: "BatchNumber",
            table: "StagedVulnerabilities");

        migrationBuilder.DropColumn(
            name: "BatchNumber",
            table: "StagedVulnerabilityExposures");
    }
}
