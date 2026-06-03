using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOperationalContextFromAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextHash",
                table: "VulnerabilityPatchAssessments");

            migrationBuilder.DropColumn(
                name: "ContextJson",
                table: "VulnerabilityPatchAssessments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContextHash",
                table: "VulnerabilityPatchAssessments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContextJson",
                table: "VulnerabilityPatchAssessments",
                type: "text",
                nullable: true);
        }
    }
}
