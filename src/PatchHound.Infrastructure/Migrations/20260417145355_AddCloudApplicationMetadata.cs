using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudApplicationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppId",
                table: "CloudApplications",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFallbackPublicClient",
                table: "CloudApplications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerTeamId",
                table: "CloudApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedirectUris",
                table: "CloudApplications",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppId",
                table: "CloudApplications");

            migrationBuilder.DropColumn(
                name: "IsFallbackPublicClient",
                table: "CloudApplications");

            migrationBuilder.DropColumn(
                name: "OwnerTeamId",
                table: "CloudApplications");

            migrationBuilder.DropColumn(
                name: "RedirectUris",
                table: "CloudApplications");
        }
    }
}
