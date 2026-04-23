using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetRuleOwnershipToSoftwareTenantRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerTeamId",
                table: "SoftwareTenantRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerTeamRuleId",
                table: "SoftwareTenantRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerTeamRuleId",
                table: "Devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareTenantRecords_OwnerTeamId",
                table: "SoftwareTenantRecords",
                column: "OwnerTeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SoftwareTenantRecords_OwnerTeamId",
                table: "SoftwareTenantRecords");

            migrationBuilder.DropColumn(
                name: "OwnerTeamId",
                table: "SoftwareTenantRecords");

            migrationBuilder.DropColumn(
                name: "OwnerTeamRuleId",
                table: "SoftwareTenantRecords");

            migrationBuilder.DropColumn(
                name: "OwnerTeamRuleId",
                table: "Devices");
        }
    }
}
