using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260330152000_AddTenantSoftwareRemediationAiSummary")]
    /// <inheritdoc />
    public partial class AddTenantSoftwareRemediationAiSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemediationAiSummaryContent",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemediationAiSummaryGeneratedAt",
                table: "TenantSoftware",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemediationAiSummaryInputHash",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemediationAiSummaryModel",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemediationAiSummaryProfileName",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemediationAiSummaryProviderType",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemediationAiSummaryContent",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiSummaryGeneratedAt",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiSummaryInputHash",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiSummaryModel",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiSummaryProfileName",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiSummaryProviderType",
                table: "TenantSoftware");
        }
    }
}
