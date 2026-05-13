using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropRemediationAiJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemediationAiJobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemediationAiJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    InputHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RemediationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "IX_RemediationAiJobs_TenantId_RemediationCaseId_RequestedAt",
                table: "RemediationAiJobs",
                columns: new[] { "TenantId", "RemediationCaseId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationAiJobs_TenantId_RemediationCaseId_Status",
                table: "RemediationAiJobs",
                columns: new[] { "TenantId", "RemediationCaseId", "Status" });
        }
    }
}
