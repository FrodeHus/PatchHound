using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceKeyAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "TenantVulnerabilities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "TenantVulnerabilities" tv
                SET "SourceKey" = vd."Source"
                FROM "VulnerabilityDefinitions" vd
                WHERE tv."VulnerabilityDefinitionId" = vd."Id"
                  AND tv."SourceKey" IS NULL
            """);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityDefinitions_Source",
                table: "VulnerabilityDefinitions",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_TenantVulnerabilities_TenantId_SourceKey_Status",
                table: "TenantVulnerabilities",
                columns: new[] { "TenantId", "SourceKey", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VulnerabilityDefinitions_Source",
                table: "VulnerabilityDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_TenantVulnerabilities_TenantId_SourceKey_Status",
                table: "TenantVulnerabilities");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "TenantVulnerabilities");
        }
    }
}
