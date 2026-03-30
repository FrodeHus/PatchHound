using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FallbackTeamRuleId",
                table: "Assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityProfileRuleId",
                table: "Assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessLabels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessLabels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetBusinessLabels",
                columns: table => new
                {
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessLabelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedByRuleId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetBusinessLabels", x => new { x.AssetId, x.BusinessLabelId, x.SourceKey });
                    table.ForeignKey(
                        name: "FK_AssetBusinessLabels_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetBusinessLabels_BusinessLabels_BusinessLabelId",
                        column: x => x.BusinessLabelId,
                        principalTable: "BusinessLabels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetBusinessLabels_BusinessLabelId",
                table: "AssetBusinessLabels",
                column: "BusinessLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetBusinessLabels_AssignedByRuleId",
                table: "AssetBusinessLabels",
                column: "AssignedByRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLabels_TenantId",
                table: "BusinessLabels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLabels_TenantId_Name",
                table: "BusinessLabels",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetBusinessLabels");

            migrationBuilder.DropTable(
                name: "BusinessLabels");

            migrationBuilder.DropColumn(
                name: "FallbackTeamRuleId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "SecurityProfileRuleId",
                table: "Assets");
        }
    }
}
