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

            migrationBuilder.CreateIndex(
                name: "IX_DeviceVulnerabilityExposures_TenantId_LastSeenRunId",
                table: "DeviceVulnerabilityExposures",
                columns: new[] { "TenantId", "LastSeenRunId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceVulnerabilityExposures_TenantId_LastSeenRunId",
                table: "DeviceVulnerabilityExposures");

            migrationBuilder.DropColumn(
                name: "LastSeenRunId",
                table: "DeviceVulnerabilityExposures");
        }
    }
}
