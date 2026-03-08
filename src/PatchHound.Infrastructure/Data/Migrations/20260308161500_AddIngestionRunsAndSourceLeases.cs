using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260308161500_AddIngestionRunsAndSourceLeases")]
    public partial class AddIngestionRunsAndSourceLeases : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveIngestionRunId",
                table: "TenantSourceConfigurations",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseAcquiredAt",
                table: "TenantSourceConfigurations",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "TenantSourceConfigurations",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "IngestionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    StartedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CompletedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    Status = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    FetchedVulnerabilityCount = table.Column<int>(type: "integer", nullable: false),
                    FetchedAssetCount = table.Column<int>(type: "integer", nullable: false),
                    FetchedSoftwareInstallationCount = table.Column<int>(
                        type: "integer",
                        nullable: false
                    ),
                    Error = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionRuns", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_IngestionRuns_TenantId_SourceKey_StartedAt",
                table: "IngestionRuns",
                columns: new[] { "TenantId", "SourceKey", "StartedAt" }
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IngestionRuns");

            migrationBuilder.DropColumn(
                name: "ActiveIngestionRunId",
                table: "TenantSourceConfigurations"
            );

            migrationBuilder.DropColumn(
                name: "LeaseAcquiredAt",
                table: "TenantSourceConfigurations"
            );

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "TenantSourceConfigurations"
            );
        }
    }
}
