using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260322194500_AddRefreshTtlHoursToEnrichmentSources")]
public partial class AddRefreshTtlHoursToEnrichmentSources : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "EnrichmentSourceConfigurations"
            ADD COLUMN IF NOT EXISTS "RefreshTtlHours" integer;
            """
        );

        migrationBuilder.Sql(
            $"""
            UPDATE "EnrichmentSourceConfigurations"
            SET "RefreshTtlHours" = {EnrichmentSourceCatalog.DefaultDefenderRefreshTtlHours}
            WHERE "SourceKey" = '{EnrichmentSourceCatalog.DefenderSourceKey}'
              AND "RefreshTtlHours" IS NULL;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "EnrichmentSourceConfigurations"
            DROP COLUMN IF EXISTS "RefreshTtlHours";
            """
        );
    }
}
