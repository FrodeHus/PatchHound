using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260322180000_RemoveSecureScoreArtifacts")]
public partial class RemoveSecureScoreArtifacts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AssetSecureScores");

        migrationBuilder.DropTable(
            name: "TenantSecureScoreSnapshots");

        migrationBuilder.DropTable(
            name: "TenantSecureScoreTargets");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantSecureScoreSnapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                OverallScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                AssetCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantSecureScoreSnapshots", x => x.Id);
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

        migrationBuilder.CreateIndex(
            name: "IX_TenantSecureScoreSnapshots_TenantId",
            table: "TenantSecureScoreSnapshots",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_TenantSecureScoreSnapshots_TenantId_Date",
            table: "TenantSecureScoreSnapshots",
            columns: new[] { "TenantId", "Date" },
            unique: true);
    }
}
