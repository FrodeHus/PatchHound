using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExposureLatestAssessmentView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE MATERIALIZED VIEW mv_exposure_latest_assessment AS
                SELECT
                    "TenantId",
                    "DeviceVulnerabilityExposureId",
                    "EnvironmentalCvss"
                FROM "ExposureAssessments";

                CREATE UNIQUE INDEX ix_mv_ela_exposure_id
                    ON mv_exposure_latest_assessment ("DeviceVulnerabilityExposureId");

                CREATE INDEX ix_mv_ela_tenant_id
                    ON mv_exposure_latest_assessment ("TenantId");
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS mv_exposure_latest_assessment;");
        }
    }
}
