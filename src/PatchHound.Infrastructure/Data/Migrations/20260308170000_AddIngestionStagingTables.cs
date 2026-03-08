using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260308170000_AddIngestionStagingTables")]
    public partial class AddIngestionStagingTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StagedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ExternalId = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    Name = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                    AssetType = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedAssets", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "StagedDeviceSoftwareInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DeviceExternalId = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    SoftwareExternalId = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    ObservedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedDeviceSoftwareInstallations", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "StagedVulnerabilityExposures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    VulnerabilityExternalId = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    AssetExternalId = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    AssetName = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                    AssetType = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedVulnerabilityExposures", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "StagedVulnerabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    ExternalId = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                    VendorSeverity = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedVulnerabilities", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_StagedAssets_IngestionRunId",
                table: "StagedAssets",
                column: "IngestionRunId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_StagedAssets_TenantId_SourceKey_ExternalId",
                table: "StagedAssets",
                columns: new[] { "TenantId", "SourceKey", "ExternalId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_StagedDeviceSoftwareInstallations_IngestionRunId",
                table: "StagedDeviceSoftwareInstallations",
                column: "IngestionRunId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_StagedDeviceSoftwareInstalls_Key",
                table: "StagedDeviceSoftwareInstallations",
                columns: new[] { "TenantId", "SourceKey", "DeviceExternalId", "SoftwareExternalId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilityExposures_IngestionRunId",
                table: "StagedVulnerabilityExposures",
                column: "IngestionRunId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilityExposures_TenantId_SourceKey_VulnAsset",
                table: "StagedVulnerabilityExposures",
                columns: new[]
                {
                    "TenantId",
                    "SourceKey",
                    "VulnerabilityExternalId",
                    "AssetExternalId",
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilities_IngestionRunId",
                table: "StagedVulnerabilities",
                column: "IngestionRunId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilities_TenantId_SourceKey_ExternalId",
                table: "StagedVulnerabilities",
                columns: new[] { "TenantId", "SourceKey", "ExternalId" }
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StagedAssets");
            migrationBuilder.DropTable(name: "StagedDeviceSoftwareInstallations");
            migrationBuilder.DropTable(name: "StagedVulnerabilityExposures");
            migrationBuilder.DropTable(name: "StagedVulnerabilities");
        }
    }
}
