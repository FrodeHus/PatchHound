using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedDeviceFieldsAndDeviceSoftwareInstallations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceAadDeviceId",
                table: "Assets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DeviceHealthStatus",
                table: "Assets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DeviceLastIpAddress",
                table: "Assets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeviceLastSeenAt",
                table: "Assets",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DeviceOsPlatform",
                table: "Assets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DeviceOsVersion",
                table: "Assets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "DeviceRiskScore",
                table: "Assets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "DeviceSoftwareInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSoftwareInstallations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceSoftwareInstallations_Assets_DeviceAssetId",
                        column: x => x.DeviceAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_DeviceSoftwareInstallations_Assets_SoftwareAssetId",
                        column: x => x.SoftwareAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallations_DeviceAssetId_SoftwareAssetId",
                table: "DeviceSoftwareInstallations",
                columns: new[] { "DeviceAssetId", "SoftwareAssetId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallations_SoftwareAssetId",
                table: "DeviceSoftwareInstallations",
                column: "SoftwareAssetId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallations_TenantId",
                table: "DeviceSoftwareInstallations",
                column: "TenantId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DeviceSoftwareInstallations");

            migrationBuilder.DropColumn(name: "DeviceAadDeviceId", table: "Assets");

            migrationBuilder.DropColumn(name: "DeviceHealthStatus", table: "Assets");

            migrationBuilder.DropColumn(name: "DeviceLastIpAddress", table: "Assets");

            migrationBuilder.DropColumn(name: "DeviceLastSeenAt", table: "Assets");

            migrationBuilder.DropColumn(name: "DeviceOsPlatform", table: "Assets");

            migrationBuilder.DropColumn(name: "DeviceOsVersion", table: "Assets");

            migrationBuilder.DropColumn(name: "DeviceRiskScore", table: "Assets");
        }
    }
}
