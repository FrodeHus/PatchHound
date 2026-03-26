using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260326140500_BackfillApprovalExpiryHours")]
    public partial class BackfillApprovalExpiryHours : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "TenantSlaConfigurations"
                SET "ApprovalExpiryHours" = 24
                WHERE "ApprovalExpiryHours" <= 0;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
