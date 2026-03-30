using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRemediationAiDraftJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemediationAiAnalystAssessmentContent",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemediationAiExceptionRecommendationContent",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemediationAiOwnerRecommendationContent",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemediationAiRecommendedPriority",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RemediationAiJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InputHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationAiJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationAiJobs_TenantId",
                table: "RemediationAiJobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationAiJobs_TenantId_TenantSoftwareId_RequestedAt",
                table: "RemediationAiJobs",
                columns: new[] { "TenantId", "TenantSoftwareId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationAiJobs_TenantId_TenantSoftwareId_Status",
                table: "RemediationAiJobs",
                columns: new[] { "TenantId", "TenantSoftwareId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemediationAiJobs");

            migrationBuilder.DropColumn(
                name: "RemediationAiAnalystAssessmentContent",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiExceptionRecommendationContent",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiOwnerRecommendationContent",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiRecommendedPriority",
                table: "TenantSoftware");
        }
    }
}
