using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetTagAndDeviceExposureFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceExposureLevel",
                table: "Assets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeviceIsAadJoined",
                table: "Assets",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetTags_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetTags_AssetId_Tag",
                table: "AssetTags",
                columns: new[] { "AssetId", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetTags_TenantId_Tag",
                table: "AssetTags",
                columns: new[] { "TenantId", "Tag" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetTags");

            migrationBuilder.DropColumn(
                name: "DeviceExposureLevel",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "DeviceIsAadJoined",
                table: "Assets");
        }
    }
}
