using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedSoftware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NormalizedSoftware",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CanonicalVendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CanonicalProductKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PrimaryCpe23Uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    NormalizationMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Confidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastEvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedSoftware", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NormalizedSoftwareAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalSoftwareId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RawName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RawVendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RawVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AliasConfidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MatchReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedSoftwareAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NormalizedSoftwareAliases_NormalizedSoftware_NormalizedSoft~",
                        column: x => x.NormalizedSoftwareId,
                        principalTable: "NormalizedSoftware",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NormalizedSoftwareInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DetectedVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentEpisodeNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedSoftwareInstallations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NormalizedSoftwareInstallations_Assets_DeviceAssetId",
                        column: x => x.DeviceAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NormalizedSoftwareInstallations_Assets_SoftwareAssetId",
                        column: x => x.SoftwareAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NormalizedSoftwareInstallations_NormalizedSoftware_Normaliz~",
                        column: x => x.NormalizedSoftwareId,
                        principalTable: "NormalizedSoftware",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NormalizedSoftwareVulnerabilityProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BestMatchMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BestConfidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AffectedInstallCount = table.Column<int>(type: "integer", nullable: false),
                    AffectedDeviceCount = table.Column<int>(type: "integer", nullable: false),
                    AffectedVersionCount = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedSoftwareVulnerabilityProjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NormalizedSoftwareVulnerabilityProjections_NormalizedSoftwa~",
                        column: x => x.NormalizedSoftwareId,
                        principalTable: "NormalizedSoftware",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NormalizedSoftwareVulnerabilityProjections_Vulnerabilities_~",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftware_TenantId",
                table: "NormalizedSoftware",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftware_TenantId_CanonicalProductKey",
                table: "NormalizedSoftware",
                columns: new[] { "TenantId", "CanonicalProductKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareAliases_NormalizedSoftwareId",
                table: "NormalizedSoftwareAliases",
                column: "NormalizedSoftwareId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareAliases_TenantId",
                table: "NormalizedSoftwareAliases",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareAliases_TenantId_SourceSystem_ExternalSof~",
                table: "NormalizedSoftwareAliases",
                columns: new[] { "TenantId", "SourceSystem", "ExternalSoftwareId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_DeviceAssetId",
                table: "NormalizedSoftwareInstallations",
                column: "DeviceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_NormalizedSoftwareId",
                table: "NormalizedSoftwareInstallations",
                column: "NormalizedSoftwareId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_SoftwareAssetId",
                table: "NormalizedSoftwareInstallations",
                column: "SoftwareAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_TenantId",
                table: "NormalizedSoftwareInstallations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_TenantId_NormalizedSoftware~",
                table: "NormalizedSoftwareInstallations",
                columns: new[] { "TenantId", "NormalizedSoftwareId", "DetectedVersion", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_TenantId_SoftwareAssetId_De~",
                table: "NormalizedSoftwareInstallations",
                columns: new[] { "TenantId", "SoftwareAssetId", "DeviceAssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareVulnerabilityProjections_NormalizedSoftwa~",
                table: "NormalizedSoftwareVulnerabilityProjections",
                column: "NormalizedSoftwareId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareVulnerabilityProjections_TenantId",
                table: "NormalizedSoftwareVulnerabilityProjections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareVulnerabilityProjections_TenantId_Normali~",
                table: "NormalizedSoftwareVulnerabilityProjections",
                columns: new[] { "TenantId", "NormalizedSoftwareId", "VulnerabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareVulnerabilityProjections_VulnerabilityId",
                table: "NormalizedSoftwareVulnerabilityProjections",
                column: "VulnerabilityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NormalizedSoftwareAliases");

            migrationBuilder.DropTable(
                name: "NormalizedSoftwareInstallations");

            migrationBuilder.DropTable(
                name: "NormalizedSoftwareVulnerabilityProjections");

            migrationBuilder.DropTable(
                name: "NormalizedSoftware");
        }
    }
}
