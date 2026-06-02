using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAiProfileOperationalContextFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowOperationalContext",
                table: "TenantAiProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeDeviceNamesInContext",
                table: "TenantAiProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeUserNamesInContext",
                table: "TenantAiProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxOperationalContextTokens",
                table: "TenantAiProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 3000);

            migrationBuilder.AddColumn<string>(
                name: "OperationalContextMode",
                table: "TenantAiProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "StructuredOnly");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowOperationalContext",
                table: "TenantAiProfiles");

            migrationBuilder.DropColumn(
                name: "IncludeDeviceNamesInContext",
                table: "TenantAiProfiles");

            migrationBuilder.DropColumn(
                name: "IncludeUserNamesInContext",
                table: "TenantAiProfiles");

            migrationBuilder.DropColumn(
                name: "MaxOperationalContextTokens",
                table: "TenantAiProfiles");

            migrationBuilder.DropColumn(
                name: "OperationalContextMode",
                table: "TenantAiProfiles");
        }
    }
}
