using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260323124500_RemoveLegacyRemediationTasks")]
public partial class RemoveLegacyRemediationTasks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RemediationTasks");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RemediationTasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                AssignedBy = table.Column<Guid>(type: "uuid", nullable: false),
                AssigneeId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Justification = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                LastSlaNotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantVulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RemediationTasks", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RemediationTasks_AssigneeId",
            table: "RemediationTasks",
            column: "AssigneeId");

        migrationBuilder.CreateIndex(
            name: "IX_RemediationTasks_Status",
            table: "RemediationTasks",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_RemediationTasks_TenantId",
            table: "RemediationTasks",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_RemediationTasks_TenantId_TenantVulnerabilityId_AssetId_Status",
            table: "RemediationTasks",
            columns: new[] { "TenantId", "TenantVulnerabilityId", "AssetId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_RemediationTasks_TenantVulnerabilityId",
            table: "RemediationTasks",
            column: "TenantVulnerabilityId");
    }
}
