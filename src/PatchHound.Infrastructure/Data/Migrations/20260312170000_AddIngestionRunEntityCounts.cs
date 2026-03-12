using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260312170000_AddIngestionRunEntityCounts")]
public partial class AddIngestionRunEntityCounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "PersistedMachineCount",
            table: "IngestionRuns",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<int>(
            name: "PersistedSoftwareCount",
            table: "IngestionRuns",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<int>(
            name: "PersistedVulnerabilityCount",
            table: "IngestionRuns",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<int>(
            name: "StagedMachineCount",
            table: "IngestionRuns",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<int>(
            name: "StagedSoftwareCount",
            table: "IngestionRuns",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PersistedMachineCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "PersistedSoftwareCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "PersistedVulnerabilityCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "StagedMachineCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "StagedSoftwareCount", table: "IngestionRuns");
    }
}
