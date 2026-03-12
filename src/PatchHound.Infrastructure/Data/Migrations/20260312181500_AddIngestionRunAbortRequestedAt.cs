using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260312181500_AddIngestionRunAbortRequestedAt")]
public partial class AddIngestionRunAbortRequestedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AbortRequestedAt",
            table: "IngestionRuns",
            type: "timestamp with time zone",
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AbortRequestedAt", table: "IngestionRuns");
    }
}
