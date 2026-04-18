using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOneActiveWorkflowPerCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deactivate all but the most recently updated active workflow per (TenantId, RemediationCaseId).
            migrationBuilder.Sql("""
                UPDATE "RemediationWorkflows" w
                SET "Status" = 'Cancelled', "UpdatedAt" = NOW()
                WHERE "Status" = 'Active'
                  AND "Id" NOT IN (
                      SELECT DISTINCT ON ("TenantId", "RemediationCaseId") "Id"
                      FROM "RemediationWorkflows"
                      WHERE "Status" = 'Active'
                      ORDER BY "TenantId", "RemediationCaseId", "UpdatedAt" DESC
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflows_ActivePerCase",
                table: "RemediationWorkflows",
                columns: new[] { "TenantId", "RemediationCaseId" },
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RemediationWorkflows_ActivePerCase",
                table: "RemediationWorkflows");
        }
    }
}
