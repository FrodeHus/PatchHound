using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlternateMitigationVulnIdView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE MATERIALIZED VIEW mv_alternate_mitigation_vuln_ids AS
                SELECT DISTINCT "TenantId", "VulnerabilityId"
                FROM "ApprovedVulnerabilityRemediations"
                WHERE "Outcome" = 'AlternateMitigation';

                CREATE UNIQUE INDEX ix_mv_amvi_tenant_vuln
                    ON mv_alternate_mitigation_vuln_ids ("TenantId", "VulnerabilityId");
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS mv_alternate_mitigation_vuln_ids;");
        }
    }
}
