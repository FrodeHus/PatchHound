using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260312153000_AddSoftwareDescriptionJobs")]
    public partial class AddSoftwareDescriptionJobs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SoftwareDescriptionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantAiProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareDescriptionJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareDescriptionJobs_TenantId",
                table: "SoftwareDescriptionJobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareDescriptionJobs_TenantId_TenantSoftwareId_RequestedAt",
                table: "SoftwareDescriptionJobs",
                columns: new[] { "TenantId", "TenantSoftwareId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareDescriptionJobs_TenantId_TenantSoftwareId_Status",
                table: "SoftwareDescriptionJobs",
                columns: new[] { "TenantId", "TenantSoftwareId", "Status" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SoftwareDescriptionJobs");
        }
    }
}
