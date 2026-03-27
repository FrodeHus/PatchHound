using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftwareEndOfLifeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EolDate",
                table: "NormalizedSoftware",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EolEnrichedAt",
                table: "NormalizedSoftware",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EolIsDiscontinued",
                table: "NormalizedSoftware",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EolIsLts",
                table: "NormalizedSoftware",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EolLatestVersion",
                table: "NormalizedSoftware",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EolProductSlug",
                table: "NormalizedSoftware",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EolSupportEndDate",
                table: "NormalizedSoftware",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EolDate",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "EolEnrichedAt",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "EolIsDiscontinued",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "EolIsLts",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "EolLatestVersion",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "EolProductSlug",
                table: "NormalizedSoftware");

            migrationBuilder.DropColumn(
                name: "EolSupportEndDate",
                table: "NormalizedSoftware");
        }
    }
}
