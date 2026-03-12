using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260312133000_AddNormalizedSoftwareDescriptions")]
public partial class AddNormalizedSoftwareDescriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Description",
            table: "NormalizedSoftware",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DescriptionGeneratedAt",
            table: "NormalizedSoftware",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DescriptionModel",
            table: "NormalizedSoftware",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DescriptionProfileName",
            table: "NormalizedSoftware",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DescriptionProviderType",
            table: "NormalizedSoftware",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Description",
            table: "NormalizedSoftware");

        migrationBuilder.DropColumn(
            name: "DescriptionGeneratedAt",
            table: "NormalizedSoftware");

        migrationBuilder.DropColumn(
            name: "DescriptionModel",
            table: "NormalizedSoftware");

        migrationBuilder.DropColumn(
            name: "DescriptionProfileName",
            table: "NormalizedSoftware");

        migrationBuilder.DropColumn(
            name: "DescriptionProviderType",
            table: "NormalizedSoftware");
    }
}
