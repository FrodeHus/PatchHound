using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenExposureVulnSummaryView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE MATERIALIZED VIEW mv_open_exposure_vuln_summary AS
                SELECT
                    dve."TenantId",
                    dve."VulnerabilityId",
                    v."VendorSeverity",
                    COUNT(DISTINCT dve."DeviceId")::integer    AS "AffectedDeviceCount",
                    MAX(dve."LastObservedAt")                  AS "LatestSeenAt",
                    MAX(v."CvssScore")                         AS "MaxCvss",
                    MIN(v."PublishedDate")                     AS "PublishedDate"
                FROM "DeviceVulnerabilityExposures" dve
                JOIN "Vulnerabilities" v ON v."Id" = dve."VulnerabilityId"
                WHERE dve."Status" = 'Open'
                GROUP BY dve."TenantId", dve."VulnerabilityId", v."VendorSeverity";

                CREATE UNIQUE INDEX ix_mv_oevs_tenant_vuln
                    ON mv_open_exposure_vuln_summary ("TenantId", "VulnerabilityId");

                CREATE INDEX ix_mv_oevs_tenant_severity_count
                    ON mv_open_exposure_vuln_summary ("TenantId", "VendorSeverity", "AffectedDeviceCount" DESC);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS mv_open_exposure_vuln_summary;");
        }
    }
}
