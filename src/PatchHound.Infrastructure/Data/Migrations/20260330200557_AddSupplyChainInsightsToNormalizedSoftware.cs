using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplyChainInsightsToNormalizedSoftware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupplyChainAffectedVulnerabilityCount",
                table: "NormalizedSoftware",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SupplyChainEnrichedAt",
                table: "NormalizedSoftware",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplyChainFixedVersion",
                table: "NormalizedSoftware",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplyChainInsightConfidence",
                table: "NormalizedSoftware",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SupplyChainPrimaryComponentName",
                table: "NormalizedSoftware",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplyChainPrimaryComponentVersion",
                table: "NormalizedSoftware",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplyChainRemediationPath",
                table: "NormalizedSoftware",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SupplyChainSourceFormat",
                table: "NormalizedSoftware",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplyChainSummary",
                table: "NormalizedSoftware",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupplyChainAffectedVulnerabilityCount",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "SupplyChainEnrichedAt",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "SupplyChainFixedVersion",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "SupplyChainInsightConfidence",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "SupplyChainPrimaryComponentName",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "SupplyChainPrimaryComponentVersion",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "SupplyChainRemediationPath",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "SupplyChainSourceFormat",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "SupplyChainSummary",
                table: "NormalizedSoftware");
        }
    }
}
