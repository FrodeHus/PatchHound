using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5SoftwareRiskScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SoftwareRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    MaxExposureScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CriticalExposureCount = table.Column<int>(type: "integer", nullable: false),
                    HighExposureCount = table.Column<int>(type: "integer", nullable: false),
                    MediumExposureCount = table.Column<int>(type: "integer", nullable: false),
                    LowExposureCount = table.Column<int>(type: "integer", nullable: false),
                    AffectedDeviceCount = table.Column<int>(type: "integer", nullable: false),
                    OpenExposureCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareRiskScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoftwareRiskScores_SoftwareProducts_SoftwareProductId",
                        column: x => x.SoftwareProductId,
                        principalTable: "SoftwareProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareRiskScores_SoftwareProductId",
                table: "SoftwareRiskScores",
                column: "SoftwareProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareRiskScores_TenantId",
                table: "SoftwareRiskScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareRiskScores_TenantId_SoftwareProductId",
                table: "SoftwareRiskScores",
                columns: new[] { "TenantId", "SoftwareProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SoftwareRiskScores");
        }
    }
}
