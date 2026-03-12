using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260312093000_AddSecurityProfileCvssEnvironmentalMetrics")]
    public partial class AddSecurityProfileCvssEnvironmentalMetrics : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModifiedAttackComplexity",
                table: "AssetSecurityProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotDefined");

            migrationBuilder.AddColumn<string>(
                name: "ModifiedAttackVector",
                table: "AssetSecurityProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Network");

            migrationBuilder.AddColumn<string>(
                name: "ModifiedAvailabilityImpact",
                table: "AssetSecurityProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotDefined");

            migrationBuilder.AddColumn<string>(
                name: "ModifiedConfidentialityImpact",
                table: "AssetSecurityProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotDefined");

            migrationBuilder.AddColumn<string>(
                name: "ModifiedIntegrityImpact",
                table: "AssetSecurityProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotDefined");

            migrationBuilder.AddColumn<string>(
                name: "ModifiedPrivilegesRequired",
                table: "AssetSecurityProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotDefined");

            migrationBuilder.AddColumn<string>(
                name: "ModifiedScope",
                table: "AssetSecurityProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotDefined");

            migrationBuilder.AddColumn<string>(
                name: "ModifiedUserInteraction",
                table: "AssetSecurityProfiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotDefined");

            migrationBuilder.Sql(
                """
                UPDATE "AssetSecurityProfiles"
                SET "ModifiedAttackVector" = CASE
                    WHEN "InternetReachability" = 'AdjacentOnly' THEN 'Adjacent'
                    WHEN "InternetReachability" = 'LocalOnly' THEN 'Local'
                    ELSE 'Network'
                END
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModifiedAttackComplexity",
                table: "AssetSecurityProfiles");

            migrationBuilder.DropColumn(
                name: "ModifiedAttackVector",
                table: "AssetSecurityProfiles");

            migrationBuilder.DropColumn(
                name: "ModifiedAvailabilityImpact",
                table: "AssetSecurityProfiles");

            migrationBuilder.DropColumn(
                name: "ModifiedConfidentialityImpact",
                table: "AssetSecurityProfiles");

            migrationBuilder.DropColumn(
                name: "ModifiedIntegrityImpact",
                table: "AssetSecurityProfiles");

            migrationBuilder.DropColumn(
                name: "ModifiedPrivilegesRequired",
                table: "AssetSecurityProfiles");

            migrationBuilder.DropColumn(
                name: "ModifiedScope",
                table: "AssetSecurityProfiles");

            migrationBuilder.DropColumn(
                name: "ModifiedUserInteraction",
                table: "AssetSecurityProfiles");
        }
    }
}
