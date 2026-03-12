using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260312103000_AddTenantAiWebResearchSettings")]
public partial class AddTenantAiWebResearchSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AllowedDomains",
            table: "TenantAiProfiles",
            type: "text",
            nullable: false,
            defaultValue: string.Empty);

        migrationBuilder.AddColumn<bool>(
            name: "AllowExternalResearch",
            table: "TenantAiProfiles",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IncludeCitations",
            table: "TenantAiProfiles",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<int>(
            name: "MaxResearchSources",
            table: "TenantAiProfiles",
            type: "integer",
            nullable: false,
            defaultValue: 5);

        migrationBuilder.AddColumn<string>(
            name: "WebResearchMode",
            table: "TenantAiProfiles",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Disabled");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AllowedDomains",
            table: "TenantAiProfiles");

        migrationBuilder.DropColumn(
            name: "AllowExternalResearch",
            table: "TenantAiProfiles");

        migrationBuilder.DropColumn(
            name: "IncludeCitations",
            table: "TenantAiProfiles");

        migrationBuilder.DropColumn(
            name: "MaxResearchSources",
            table: "TenantAiProfiles");

        migrationBuilder.DropColumn(
            name: "WebResearchMode",
            table: "TenantAiProfiles");
    }
}
