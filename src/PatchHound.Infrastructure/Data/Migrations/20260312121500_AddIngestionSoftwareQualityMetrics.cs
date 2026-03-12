using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260312121500_AddIngestionSoftwareQualityMetrics")]
public partial class AddIngestionSoftwareQualityMetrics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "FetchedSoftwareCount",
            table: "IngestionRuns",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "SoftwareWithoutMachineReferencesCount",
            table: "IngestionRuns",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "FetchedSoftwareCount",
            table: "IngestionRuns");

        migrationBuilder.DropColumn(
            name: "SoftwareWithoutMachineReferencesCount",
            table: "IngestionRuns");
    }
}
