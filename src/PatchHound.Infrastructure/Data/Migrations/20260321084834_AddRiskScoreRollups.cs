using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskScoreRollups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    MaxEpisodeRiskScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CriticalCount = table.Column<int>(type: "integer", nullable: false),
                    HighCount = table.Column<int>(type: "integer", nullable: false),
                    MediumCount = table.Column<int>(type: "integer", nullable: false),
                    LowCount = table.Column<int>(type: "integer", nullable: false),
                    OpenEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetRiskScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetRiskScores_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantRiskScoreSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    CriticalAssetCount = table.Column<int>(type: "integer", nullable: false),
                    HighAssetCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantRiskScoreSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetRiskScores_AssetId",
                table: "AssetRiskScores",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetRiskScores_TenantId",
                table: "AssetRiskScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetRiskScores_TenantId_AssetId",
                table: "AssetRiskScores",
                columns: new[] { "TenantId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantRiskScoreSnapshots_TenantId",
                table: "TenantRiskScoreSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantRiskScoreSnapshots_TenantId_Date",
                table: "TenantRiskScoreSnapshots",
                columns: new[] { "TenantId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetRiskScores");

            migrationBuilder.DropTable(
                name: "TenantRiskScoreSnapshots");
        }
    }
}
