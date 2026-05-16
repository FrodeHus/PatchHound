using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLastSeenRunIdToExposures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastSeenRunId",
                table: "DeviceVulnerabilityExposures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastSeenRunId",
                table: "InstalledSoftware",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "VulnerabilityApplicabilities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceVulnerabilityExposures_TenantId_LastSeenRunId",
                table: "DeviceVulnerabilityExposures",
                columns: new[] { "TenantId", "LastSeenRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityApplicabilities_VulnerabilityId_Source",
                table: "VulnerabilityApplicabilities",
                columns: new[] { "VulnerabilityId", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_InstalledSoftware_TenantId_LastSeenRunId",
                table: "InstalledSoftware",
                columns: new[] { "TenantId", "LastSeenRunId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceVulnerabilityExposures_TenantId_LastSeenRunId",
                table: "DeviceVulnerabilityExposures");

            migrationBuilder.DropIndex(
                name: "IX_InstalledSoftware_TenantId_LastSeenRunId",
                table: "InstalledSoftware");

            migrationBuilder.DropIndex(
                name: "IX_VulnerabilityApplicabilities_VulnerabilityId_Source",
                table: "VulnerabilityApplicabilities");

            migrationBuilder.DropColumn(
                name: "LastSeenRunId",
                table: "DeviceVulnerabilityExposures");

            migrationBuilder.DropColumn(
                name: "LastSeenRunId",
                table: "InstalledSoftware");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "VulnerabilityApplicabilities");
        }
    }
}
