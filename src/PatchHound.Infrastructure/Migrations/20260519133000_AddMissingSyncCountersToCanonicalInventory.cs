using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260519133000_AddMissingSyncCountersToCanonicalInventory")]
    public partial class AddMissingSyncCountersToCanonicalInventory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastMissedRunId",
                table: "InstalledSoftware",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissingSyncCount",
                table: "InstalledSoftware",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "LastMissedRunId",
                table: "DeviceVulnerabilityExposures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissingSyncCount",
                table: "DeviceVulnerabilityExposures",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MissingSyncCount",
                table: "InstalledSoftware");

            migrationBuilder.DropColumn(
                name: "LastMissedRunId",
                table: "InstalledSoftware");

            migrationBuilder.DropColumn(
                name: "MissingSyncCount",
                table: "DeviceVulnerabilityExposures");

            migrationBuilder.DropColumn(
                name: "LastMissedRunId",
                table: "DeviceVulnerabilityExposures");
        }
    }
}
