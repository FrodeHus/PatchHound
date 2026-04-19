using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNvdCveCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NvdCveCache",
                columns: table => new
                {
                    CveId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    CvssScore = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    CvssVector = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PublishedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FeedLastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReferencesJson = table.Column<string>(type: "text", nullable: false),
                    ConfigurationsJson = table.Column<string>(type: "text", nullable: false),
                    CachedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NvdCveCache", x => x.CveId);
                });

            migrationBuilder.CreateTable(
                name: "NvdFeedCheckpoints",
                columns: table => new
                {
                    FeedName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FeedLastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NvdFeedCheckpoints", x => x.FeedName);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NvdCveCache_FeedLastModified",
                table: "NvdCveCache",
                column: "FeedLastModified");

            migrationBuilder.CreateIndex(
                name: "IX_NvdCveCache_PublishedDate",
                table: "NvdCveCache",
                column: "PublishedDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NvdCveCache");

            migrationBuilder.DropTable(
                name: "NvdFeedCheckpoints");
        }
    }
}
