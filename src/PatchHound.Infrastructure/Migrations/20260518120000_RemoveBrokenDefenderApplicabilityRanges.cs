using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260518120000_RemoveBrokenDefenderApplicabilityRanges")]
    public partial class RemoveBrokenDefenderApplicabilityRanges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "VulnerabilityApplicabilities"
                WHERE (
                    "Source" = 'microsoft-defender'
                    OR (
                        "Source" = 'Unknown'
                        AND EXISTS (
                            SELECT 1
                            FROM "Vulnerabilities" v
                            WHERE v."Id" = "VulnerabilityApplicabilities"."VulnerabilityId"
                              AND v."Source" = 'microsoft-defender'
                        )
                    )
                  )
                  AND "VersionStartIncluding" IS NULL
                  AND "VersionStartExcluding" IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
