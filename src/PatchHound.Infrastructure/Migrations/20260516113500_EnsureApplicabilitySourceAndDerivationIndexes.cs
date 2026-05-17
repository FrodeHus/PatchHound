using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260516113500_EnsureApplicabilitySourceAndDerivationIndexes")]
    public partial class EnsureApplicabilitySourceAndDerivationIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "VulnerabilityApplicabilities"
                ADD COLUMN IF NOT EXISTS "Source" character varying(64) NOT NULL DEFAULT 'Unknown';
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_VulnerabilityApplicabilities_VulnerabilityId_Source"
                ON "VulnerabilityApplicabilities" ("VulnerabilityId", "Source");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_InstalledSoftware_TenantId_LastSeenRunId_SoftwareProductId"
                ON "InstalledSoftware" ("TenantId", "LastSeenRunId", "SoftwareProductId");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_VulnerabilityApplicabilities_Vulnerable_SoftwareProductId"
                ON "VulnerabilityApplicabilities" ("Vulnerable", "SoftwareProductId");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_VulnerabilityApplicabilities_Vulnerable_CpeCriteria_Lower"
                ON "VulnerabilityApplicabilities" (lower("CpeCriteria"))
                WHERE "Vulnerable" = TRUE
                  AND "SoftwareProductId" IS NULL
                  AND "CpeCriteria" IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_VulnerabilityApplicabilities_Vulnerable_CpeCriteria_Lower";
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_VulnerabilityApplicabilities_Vulnerable_SoftwareProductId";
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_InstalledSoftware_TenantId_LastSeenRunId_SoftwareProductId";
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_VulnerabilityApplicabilities_VulnerabilityId_Source";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "VulnerabilityApplicabilities"
                DROP COLUMN IF EXISTS "Source";
                """);
        }
    }
}
