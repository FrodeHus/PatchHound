using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSecureScoreSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantSecureScoreSnapshots");
        }
    }
}
