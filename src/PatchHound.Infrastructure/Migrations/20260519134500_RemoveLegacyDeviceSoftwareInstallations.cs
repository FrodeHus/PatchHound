using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260519134500_RemoveLegacyDeviceSoftwareInstallations")]
public partial class RemoveLegacyDeviceSoftwareInstallations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DeviceSoftwareInstallations");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DeviceSoftwareInstallations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DeviceAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                MissingSyncCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeviceSoftwareInstallations", x => x.Id);
                table.ForeignKey(
                    name: "FK_DeviceSoftwareInstallations_Devices_DeviceAssetId",
                    column: x => x.DeviceAssetId,
                    principalTable: "Devices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DeviceSoftwareInstallations_DeviceAssetId_SoftwareAssetId",
            table: "DeviceSoftwareInstallations",
            columns: new[] { "DeviceAssetId", "SoftwareAssetId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DeviceSoftwareInstallations_TenantId",
            table: "DeviceSoftwareInstallations",
            column: "TenantId");
    }
}
