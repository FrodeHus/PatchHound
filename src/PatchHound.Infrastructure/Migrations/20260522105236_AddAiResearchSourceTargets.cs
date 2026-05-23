using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiResearchSourceTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResearchSourceKey",
                table: "TenantAiProfiles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OptionsJson",
                table: "EnrichmentSourceConfigurations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Targets",
                table: "EnrichmentSourceConfigurations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "Scheduled");

            migrationBuilder.Sql(
                """
                UPDATE "EnrichmentSourceConfigurations"
                SET "Targets" = 'Scheduled'
                WHERE "Targets" IS NULL OR btrim("Targets") = ''
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResearchSourceKey",
                table: "TenantAiProfiles");

            migrationBuilder.DropColumn(
                name: "OptionsJson",
                table: "EnrichmentSourceConfigurations");

            migrationBuilder.DropColumn(
                name: "Targets",
                table: "EnrichmentSourceConfigurations");
        }
    }
}
