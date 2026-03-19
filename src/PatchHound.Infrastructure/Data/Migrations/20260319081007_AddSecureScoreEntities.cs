using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSecureScoreEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetSecureScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    VulnerabilityScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    ConfigurationScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    DeviceValueWeight = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    ActiveVulnerabilityCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetSecureScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetSecureScores_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantSecureScoreTargets",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSecureScoreTargets", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetSecureScores_AssetId",
                table: "AssetSecureScores",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetSecureScores_TenantId",
                table: "AssetSecureScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetSecureScores_TenantId_AssetId",
                table: "AssetSecureScores",
                columns: new[] { "TenantId", "AssetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetSecureScores");

            migrationBuilder.DropTable(
                name: "TenantSecureScoreTargets");
        }
    }
}
