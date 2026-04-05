using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticatedScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetScanProfileAssignments",
                columns: table => new
                {
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetScanProfileAssignments", x => new { x.AssetId, x.ScanProfileId });
                });

            migrationBuilder.CreateTable(
                name: "AuthenticatedScanRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggerKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TriggeredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalDevices = table.Column<int>(type: "integer", nullable: false),
                    SucceededCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    EntriesIngested = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticatedScanRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConnectionProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SshHost = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SshPort = table.Column<int>(type: "integer", nullable: false),
                    SshUsername = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AuthMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    HostKeyFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanJobResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawStdout = table.Column<string>(type: "text", nullable: false),
                    RawStderr = table.Column<string>(type: "text", nullable: false),
                    ParsedJson = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanJobResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanRunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanningToolVersionIdsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: false),
                    StdoutBytes = table.Column<int>(type: "integer", nullable: false),
                    StderrBytes = table.Column<int>(type: "integer", nullable: false),
                    EntriesIngested = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanJobValidationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldPath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    EntryIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanJobValidationIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanningTools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ScriptType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InterpreterPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    OutputModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanningTools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanningToolVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanningToolId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ScriptContent = table.Column<string>(type: "text", nullable: false),
                    EditedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanningToolVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CronSchedule = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConnectionProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanRunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ManualRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanProfileTools",
                columns: table => new
                {
                    ScanProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanningToolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanProfileTools", x => new { x.ScanProfileId, x.ScanningToolId });
                });

            migrationBuilder.CreateTable(
                name: "ScanRunners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanRunners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StagedAuthenticatedScanSoftware",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CanonicalProductKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CanonicalVendor = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Category = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PrimaryCpe23Uri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DetectedVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedAuthenticatedScanSoftware", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetScanProfileAssignments_TenantId_ScanProfileId",
                table: "AssetScanProfileAssignments",
                columns: new[] { "TenantId", "ScanProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticatedScanRuns_TenantId_ScanProfileId_StartedAt",
                table: "AuthenticatedScanRuns",
                columns: new[] { "TenantId", "ScanProfileId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionProfiles_TenantId_Name",
                table: "ConnectionProfiles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobResults_ScanJobId",
                table: "ScanJobResults",
                column: "ScanJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobs_RunId",
                table: "ScanJobs",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobs_ScanRunnerId_Status",
                table: "ScanJobs",
                columns: new[] { "ScanRunnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobValidationIssues_ScanJobId",
                table: "ScanJobValidationIssues",
                column: "ScanJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanningTools_TenantId_Name",
                table: "ScanningTools",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanningToolVersions_ScanningToolId_VersionNumber",
                table: "ScanningToolVersions",
                columns: new[] { "ScanningToolId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanProfiles_TenantId_Name",
                table: "ScanProfiles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanRunners_TenantId_Name",
                table: "ScanRunners",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StagedAuthenticatedScanSoftware_ScanJobId",
                table: "StagedAuthenticatedScanSoftware",
                column: "ScanJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetScanProfileAssignments");

            migrationBuilder.DropTable(
                name: "AuthenticatedScanRuns");

            migrationBuilder.DropTable(
                name: "ConnectionProfiles");

            migrationBuilder.DropTable(
                name: "ScanJobResults");

            migrationBuilder.DropTable(
                name: "ScanJobs");

            migrationBuilder.DropTable(
                name: "ScanJobValidationIssues");

            migrationBuilder.DropTable(
                name: "ScanningTools");

            migrationBuilder.DropTable(
                name: "ScanningToolVersions");

            migrationBuilder.DropTable(
                name: "ScanProfiles");

            migrationBuilder.DropTable(
                name: "ScanProfileTools");

            migrationBuilder.DropTable(
                name: "ScanRunners");

            migrationBuilder.DropTable(
                name: "StagedAuthenticatedScanSoftware");
        }
    }
}
