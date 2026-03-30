using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRemediationAiRecommendedOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemediationAiRecommendedOutcome",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemediationAiReviewStatus",
                table: "TenantSoftware",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemediationAiReviewedAt",
                table: "TenantSoftware",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RemediationAiReviewedBy",
                table: "TenantSoftware",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemediationAiRecommendedOutcome",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiReviewStatus",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiReviewedAt",
                table: "TenantSoftware");

            migrationBuilder.DropColumn(
                name: "RemediationAiReviewedBy",
                table: "TenantSoftware");
        }
    }
}
