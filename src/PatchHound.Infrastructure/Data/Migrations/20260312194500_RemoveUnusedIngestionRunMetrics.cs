using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260312194500_RemoveUnusedIngestionRunMetrics")]
public partial class RemoveUnusedIngestionRunMetrics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "FetchedAssetCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "FetchedSoftwareCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "FetchedSoftwareInstallationCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "FetchedVulnerabilityCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "InstallationEpisodesOpened", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "InstallationEpisodesSeen", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "InstallationsCreated", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "InstallationsRemoved", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "InstallationsTouched", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "MergedAssetCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "MergedExposureCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "OpenedProjectionCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "ResolvedProjectionCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "ResolvedSoftwareLinkCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "StagedAssetCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "StagedExposureCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "StagedSoftwareLinkCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "SoftwareWithoutMachineReferencesCount", table: "IngestionRuns");
        migrationBuilder.DropColumn(name: "StaleInstallationsMarked", table: "IngestionRuns");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "FetchedAssetCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "FetchedSoftwareCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "FetchedSoftwareInstallationCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "FetchedVulnerabilityCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "InstallationEpisodesOpened", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "InstallationEpisodesSeen", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "InstallationsCreated", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "InstallationsRemoved", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "InstallationsTouched", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "MergedAssetCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "MergedExposureCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "OpenedProjectionCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "ResolvedProjectionCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "ResolvedSoftwareLinkCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "StagedAssetCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "StagedExposureCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "StagedSoftwareLinkCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "SoftwareWithoutMachineReferencesCount", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "StaleInstallationsMarked", table: "IngestionRuns", type: "integer", nullable: false, defaultValue: 0);
    }
}
