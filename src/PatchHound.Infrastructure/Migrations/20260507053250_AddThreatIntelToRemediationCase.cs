using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddThreatIntelToRemediationCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ThreatIntelGeneratedAt",
                table: "RemediationCases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThreatIntelProfileName",
                table: "RemediationCases",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThreatIntelSummary",
                table: "RemediationCases",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThreatIntelGeneratedAt",
                table: "RemediationCases");

            migrationBuilder.DropColumn(
                name: "ThreatIntelProfileName",
                table: "RemediationCases");

            migrationBuilder.DropColumn(
                name: "ThreatIntelSummary",
                table: "RemediationCases");
        }
    }
}
