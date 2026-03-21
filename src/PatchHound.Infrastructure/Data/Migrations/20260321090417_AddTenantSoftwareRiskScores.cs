using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSoftwareRiskScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantSoftwareRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    MaxEpisodeRiskScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CriticalEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    HighEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    MediumEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    LowEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    AffectedDeviceCount = table.Column<int>(type: "integer", nullable: false),
                    OpenEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSoftwareRiskScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSoftwareRiskScores_TenantSoftware_TenantSoftwareId",
                        column: x => x.TenantSoftwareId,
                        principalTable: "TenantSoftware",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftwareRiskScores_TenantId",
                table: "TenantSoftwareRiskScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftwareRiskScores_TenantId_SnapshotId",
                table: "TenantSoftwareRiskScores",
                columns: new[] { "TenantId", "SnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftwareRiskScores_TenantSoftwareId",
                table: "TenantSoftwareRiskScores",
                column: "TenantSoftwareId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantSoftwareRiskScores");
        }
    }
}
