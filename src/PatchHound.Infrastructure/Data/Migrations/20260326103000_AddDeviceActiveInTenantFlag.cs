using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260326103000_AddDeviceActiveInTenantFlag")]
public partial class AddDeviceActiveInTenantFlag : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "DeviceActiveInTenant",
            table: "Assets",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.CreateIndex(
            name: "IX_Assets_TenantId_AssetType_DeviceActiveInTenant",
            table: "Assets",
            columns: new[] { "TenantId", "AssetType", "DeviceActiveInTenant" });

        migrationBuilder.Sql("""
            UPDATE "Assets"
            SET "DeviceActiveInTenant" = CASE
                WHEN "AssetType" <> 'Device' THEN TRUE
                WHEN "DeviceLastSeenAt" IS NULL THEN FALSE
                WHEN "DeviceLastSeenAt" < CURRENT_TIMESTAMP - INTERVAL '30 days' THEN FALSE
                ELSE TRUE
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Assets_TenantId_AssetType_DeviceActiveInTenant",
            table: "Assets");

        migrationBuilder.DropColumn(
            name: "DeviceActiveInTenant",
            table: "Assets");
    }
}
