using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260323111500_AddAssetCriticalityProvenance")]
public partial class AddAssetCriticalityProvenance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "Assets"
            ADD COLUMN IF NOT EXISTS "BaselineCriticality" character varying(32);
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE "Assets"
            ADD COLUMN IF NOT EXISTS "CriticalitySource" character varying(32);
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE "Assets"
            ADD COLUMN IF NOT EXISTS "CriticalityReason" character varying(512);
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE "Assets"
            ADD COLUMN IF NOT EXISTS "CriticalityRuleId" uuid;
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE "Assets"
            ADD COLUMN IF NOT EXISTS "CriticalityUpdatedAt" timestamp with time zone;
            """
        );

        migrationBuilder.Sql(
            """
            UPDATE "Assets"
            SET "BaselineCriticality" = COALESCE("BaselineCriticality", "Criticality"),
                "CriticalitySource" = COALESCE("CriticalitySource", 'Default'),
                "CriticalityUpdatedAt" = COALESCE("CriticalityUpdatedAt", NOW())
            WHERE "BaselineCriticality" IS NULL
               OR "CriticalitySource" IS NULL
               OR "CriticalityUpdatedAt" IS NULL;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "Assets"
            DROP COLUMN IF EXISTS "CriticalityUpdatedAt";
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE "Assets"
            DROP COLUMN IF EXISTS "CriticalityRuleId";
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE "Assets"
            DROP COLUMN IF EXISTS "CriticalityReason";
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE "Assets"
            DROP COLUMN IF EXISTS "CriticalitySource";
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE "Assets"
            DROP COLUMN IF EXISTS "BaselineCriticality";
            """
        );
    }
}
