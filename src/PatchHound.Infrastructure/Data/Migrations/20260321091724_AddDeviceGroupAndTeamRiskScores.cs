using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceGroupAndTeamRiskScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceGroupRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeviceGroupId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceGroupName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    MaxAssetRiskScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CriticalEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    HighEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    MediumEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    LowEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    OpenEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceGroupRiskScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    MaxAssetRiskScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CriticalEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    HighEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    MediumEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    LowEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    OpenEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamRiskScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamRiskScores_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceGroupRiskScores_TenantId",
                table: "DeviceGroupRiskScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceGroupRiskScores_TenantId_GroupKey",
                table: "DeviceGroupRiskScores",
                columns: new[] { "TenantId", "GroupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamRiskScores_TeamId",
                table: "TeamRiskScores",
                column: "TeamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamRiskScores_TenantId",
                table: "TeamRiskScores",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceGroupRiskScores");

            migrationBuilder.DropTable(
                name: "TeamRiskScores");
        }
    }
}
