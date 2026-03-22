using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260322193000_AddDefenderRefreshTimestampToThreatAssessments")]
public partial class AddDefenderRefreshTimestampToThreatAssessments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "VulnerabilityThreatAssessments"
            ADD COLUMN IF NOT EXISTS "DefenderLastRefreshedAt" timestamp with time zone;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "VulnerabilityThreatAssessments"
            DROP COLUMN IF EXISTS "DefenderLastRefreshedAt";
            """
        );
    }
}
