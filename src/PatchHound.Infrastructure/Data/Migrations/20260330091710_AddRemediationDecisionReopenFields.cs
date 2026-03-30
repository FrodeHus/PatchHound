using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRemediationDecisionReopenFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReopenCount",
                table: "RemediationDecisions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReopenedAt",
                table: "RemediationDecisions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReopenCount",
                table: "RemediationDecisions");

            migrationBuilder.DropColumn(
                name: "ReopenedAt",
                table: "RemediationDecisions");
        }
    }
}
