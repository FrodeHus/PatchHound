using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceSoftwareInstallationEpisodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MissingSyncCount",
                table: "DeviceSoftwareInstallations",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.CreateTable(
                name: "DeviceSoftwareInstallationEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastSeenAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    RemovedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    MissingSyncCount = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSoftwareInstallationEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceSoftwareInstallationEpisodes_Assets_DeviceAssetId",
                        column: x => x.DeviceAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_DeviceSoftwareInstallationEpisodes_Assets_SoftwareAssetId",
                        column: x => x.SoftwareAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallationEpisodes_DeviceAssetId_SoftwareAs~",
                table: "DeviceSoftwareInstallationEpisodes",
                columns: new[] { "DeviceAssetId", "SoftwareAssetId", "EpisodeNumber" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallationEpisodes_SoftwareAssetId",
                table: "DeviceSoftwareInstallationEpisodes",
                column: "SoftwareAssetId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallationEpisodes_TenantId",
                table: "DeviceSoftwareInstallationEpisodes",
                column: "TenantId"
            );

            migrationBuilder.Sql(
                """
                INSERT INTO "DeviceSoftwareInstallationEpisodes"
                    ("Id", "TenantId", "DeviceAssetId", "SoftwareAssetId", "EpisodeNumber", "FirstSeenAt", "LastSeenAt", "RemovedAt", "MissingSyncCount")
                SELECT
                    gen_random_uuid(),
                    dsi."TenantId",
                    dsi."DeviceAssetId",
                    dsi."SoftwareAssetId",
                    1,
                    dsi."LastSeenAt",
                    dsi."LastSeenAt",
                    NULL,
                    0
                FROM "DeviceSoftwareInstallations" dsi;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DeviceSoftwareInstallationEpisodes");

            migrationBuilder.DropColumn(
                name: "MissingSyncCount",
                table: "DeviceSoftwareInstallations"
            );
        }
    }
}
