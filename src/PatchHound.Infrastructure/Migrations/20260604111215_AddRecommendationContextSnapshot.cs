using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationContextSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContextSnapshotId",
                table: "AnalystRecommendations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecommendationContextSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextJson = table.Column<string>(type: "text", nullable: false),
                    ContextHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CitationsJson = table.Column<string>(type: "text", nullable: false),
                    GeneratedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationContextSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationContextSnapshots_TenantId_RemediationCaseId",
                table: "RecommendationContextSnapshots",
                columns: new[] { "TenantId", "RemediationCaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationContextSnapshots");

            migrationBuilder.DropColumn(
                name: "ContextSnapshotId",
                table: "AnalystRecommendations");
        }
    }
}
