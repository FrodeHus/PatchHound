using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironmentalSeverityProfilesAndAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SecurityProfileId",
                table: "Assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetSecurityProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    EnvironmentClass = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InternetReachability = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfidentialityRequirement = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IntegrityRequirement = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AvailabilityRequirement = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetSecurityProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityAssetAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetSecurityProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaseSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BaseScore = table.Column<decimal>(type: "numeric", nullable: true),
                    BaseVector = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EffectiveSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EffectiveScore = table.Column<decimal>(type: "numeric", nullable: true),
                    EffectiveVector = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    ReasonSummary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityAssetAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VulnerabilityAssetAssessments_AssetSecurityProfiles_AssetSe~",
                        column: x => x.AssetSecurityProfileId,
                        principalTable: "AssetSecurityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VulnerabilityAssetAssessments_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VulnerabilityAssetAssessments_Vulnerabilities_Vulnerability~",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_SecurityProfileId",
                table: "Assets",
                column: "SecurityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetSecurityProfiles_TenantId",
                table: "AssetSecurityProfiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetSecurityProfiles_TenantId_Name",
                table: "AssetSecurityProfiles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_AssetId",
                table: "VulnerabilityAssetAssessments",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_AssetSecurityProfileId",
                table: "VulnerabilityAssetAssessments",
                column: "AssetSecurityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_TenantId",
                table: "VulnerabilityAssetAssessments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_TenantId_VulnerabilityId_Asse~",
                table: "VulnerabilityAssetAssessments",
                columns: new[] { "TenantId", "VulnerabilityId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_VulnerabilityId",
                table: "VulnerabilityAssetAssessments",
                column: "VulnerabilityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_AssetSecurityProfiles_SecurityProfileId",
                table: "Assets",
                column: "SecurityProfileId",
                principalTable: "AssetSecurityProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_AssetSecurityProfiles_SecurityProfileId",
                table: "Assets");

            migrationBuilder.DropTable(
                name: "VulnerabilityAssetAssessments");

            migrationBuilder.DropTable(
                name: "AssetSecurityProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Assets_SecurityProfileId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "SecurityProfileId",
                table: "Assets");
        }
    }
}
