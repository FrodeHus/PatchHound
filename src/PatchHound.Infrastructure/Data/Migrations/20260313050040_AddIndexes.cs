using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetEpisodes_TenantId_Status_TenantVulnerabil~",
                table: "VulnerabilityAssetEpisodes",
                columns: new[] { "TenantId", "Status", "TenantVulnerabilityId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetEpisodes_TenantId_TenantVulnerabilityId_A~",
                table: "VulnerabilityAssetEpisodes",
                columns: new[] { "TenantId", "TenantVulnerabilityId", "AssetId", "EpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilityExposures_IngestionRunId_TenantId_Source~",
                table: "StagedVulnerabilityExposures",
                columns: new[] { "IngestionRunId", "TenantId", "SourceKey", "VulnerabilityExternalId", "AssetExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilities_IngestionRunId_TenantId_SourceKey_Id",
                table: "StagedVulnerabilities",
                columns: new[] { "IngestionRunId", "TenantId", "SourceKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationTasks_TenantId_TenantVulnerabilityId_AssetId_Sta~",
                table: "RemediationTasks",
                columns: new[] { "TenantId", "TenantVulnerabilityId", "AssetId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VulnerabilityAssetEpisodes_TenantId_Status_TenantVulnerabil~",
                table: "VulnerabilityAssetEpisodes");

            migrationBuilder.DropIndex(
                name: "IX_VulnerabilityAssetEpisodes_TenantId_TenantVulnerabilityId_A~",
                table: "VulnerabilityAssetEpisodes");

            migrationBuilder.DropIndex(
                name: "IX_StagedVulnerabilityExposures_IngestionRunId_TenantId_Source~",
                table: "StagedVulnerabilityExposures");

            migrationBuilder.DropIndex(
                name: "IX_StagedVulnerabilities_IngestionRunId_TenantId_SourceKey_Id",
                table: "StagedVulnerabilities");

            migrationBuilder.DropIndex(
                name: "IX_RemediationTasks_TenantId_TenantVulnerabilityId_AssetId_Sta~",
                table: "RemediationTasks");
        }
    }
}
