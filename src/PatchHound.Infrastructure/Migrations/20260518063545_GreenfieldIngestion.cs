using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GreenfieldIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IngestionRunDeltas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionRunDeltas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngestionRunDeltas_IngestionRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "IngestionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RawDeviceObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ComputerDnsName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    HealthStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OsPlatform = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OsVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SourceLastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawDeviceObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawDeviceObservations_IngestionRuns_IngestionRunId",
                        column: x => x.IngestionRunId,
                        principalTable: "IngestionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RawDeviceObservations_SourceSystems_SourceSystemId",
                        column: x => x.SourceSystemId,
                        principalTable: "SourceSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawExposureObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    VulnerabilityExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SoftwareExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SoftwareVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawExposureObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawExposureObservations_IngestionRuns_IngestionRunId",
                        column: x => x.IngestionRunId,
                        principalTable: "IngestionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RawExposureObservations_SourceSystems_SourceSystemId",
                        column: x => x.SourceSystemId,
                        principalTable: "SourceSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawInstallationObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SoftwareExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawInstallationObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawInstallationObservations_IngestionRuns_IngestionRunId",
                        column: x => x.IngestionRunId,
                        principalTable: "IngestionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RawInstallationObservations_SourceSystems_SourceSystemId",
                        column: x => x.SourceSystemId,
                        principalTable: "SourceSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawSoftwareObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Vendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CanonicalProductKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawSoftwareObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawSoftwareObservations_IngestionRuns_IngestionRunId",
                        column: x => x.IngestionRunId,
                        principalTable: "IngestionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RawSoftwareObservations_SourceSystems_SourceSystemId",
                        column: x => x.SourceSystemId,
                        principalTable: "SourceSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawVulnerabilityObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    VendorSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CvssScore = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    CvssVector = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PublishedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawVulnerabilityObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawVulnerabilityObservations_IngestionRuns_IngestionRunId",
                        column: x => x.IngestionRunId,
                        principalTable: "IngestionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RawVulnerabilityObservations_SourceSystems_SourceSystemId",
                        column: x => x.SourceSystemId,
                        principalTable: "SourceSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoftwareReleases_SoftwareProducts_SoftwareProductId",
                        column: x => x.SoftwareProductId,
                        principalTable: "SoftwareProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareSourceIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ObservedVendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ObservedName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ObservedVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CanonicalProductKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    SoftwareReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareSourceIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoftwareSourceIdentities_SoftwareProducts_SoftwareProductId",
                        column: x => x.SoftwareProductId,
                        principalTable: "SoftwareProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SoftwareSourceIdentities_SoftwareReleases_SoftwareReleaseId",
                        column: x => x.SoftwareReleaseId,
                        principalTable: "SoftwareReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SoftwareSourceIdentities_SourceSystems_SourceSystemId",
                        column: x => x.SourceSystemId,
                        principalTable: "SourceSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionRunDeltas_TenantId_Kind",
                table: "IngestionRunDeltas",
                columns: new[] { "TenantId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "UX_IngestionRunDeltas_Run_Kind_Id",
                table: "IngestionRunDeltas",
                columns: new[] { "RunId", "Kind", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawDeviceObservations_IngestionRunId_BatchNumber",
                table: "RawDeviceObservations",
                columns: new[] { "IngestionRunId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RawDeviceObservations_SourceSystemId",
                table: "RawDeviceObservations",
                column: "SourceSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_RawDeviceObservations_TenantId_SourceSystemId_ExternalId",
                table: "RawDeviceObservations",
                columns: new[] { "TenantId", "SourceSystemId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "UX_RawDeviceObservations_Run_Source_ExternalId",
                table: "RawDeviceObservations",
                columns: new[] { "IngestionRunId", "SourceSystemId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawExposureObservations_IngestionRunId_BatchNumber",
                table: "RawExposureObservations",
                columns: new[] { "IngestionRunId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RawExposureObservations_IngestionRunId_TenantId_DeviceExter~",
                table: "RawExposureObservations",
                columns: new[] { "IngestionRunId", "TenantId", "DeviceExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_RawExposureObservations_IngestionRunId_TenantId_Vulnerabili~",
                table: "RawExposureObservations",
                columns: new[] { "IngestionRunId", "TenantId", "VulnerabilityExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_RawExposureObservations_SourceSystemId",
                table: "RawExposureObservations",
                column: "SourceSystemId");

            migrationBuilder.CreateIndex(
                name: "UX_RawExposureObservations_Run_Device_Vulnerability_Software",
                table: "RawExposureObservations",
                columns: new[] { "IngestionRunId", "SourceSystemId", "DeviceExternalId", "VulnerabilityExternalId", "SoftwareExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawInstallationObservations_IngestionRunId_BatchNumber",
                table: "RawInstallationObservations",
                columns: new[] { "IngestionRunId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RawInstallationObservations_IngestionRunId_TenantId_DeviceE~",
                table: "RawInstallationObservations",
                columns: new[] { "IngestionRunId", "TenantId", "DeviceExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_RawInstallationObservations_IngestionRunId_TenantId_Softwar~",
                table: "RawInstallationObservations",
                columns: new[] { "IngestionRunId", "TenantId", "SoftwareExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_RawInstallationObservations_SourceSystemId",
                table: "RawInstallationObservations",
                column: "SourceSystemId");

            migrationBuilder.CreateIndex(
                name: "UX_RawInstallationObservations_Run_Device_Software",
                table: "RawInstallationObservations",
                columns: new[] { "IngestionRunId", "SourceSystemId", "DeviceExternalId", "SoftwareExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawSoftwareObservations_IngestionRunId_BatchNumber",
                table: "RawSoftwareObservations",
                columns: new[] { "IngestionRunId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RawSoftwareObservations_IngestionRunId_CanonicalProductKey",
                table: "RawSoftwareObservations",
                columns: new[] { "IngestionRunId", "CanonicalProductKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RawSoftwareObservations_SourceSystemId",
                table: "RawSoftwareObservations",
                column: "SourceSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_RawSoftwareObservations_TenantId_SourceSystemId_ExternalId",
                table: "RawSoftwareObservations",
                columns: new[] { "TenantId", "SourceSystemId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "UX_RawSoftwareObservations_Run_Source_ExternalId",
                table: "RawSoftwareObservations",
                columns: new[] { "IngestionRunId", "SourceSystemId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawVulnerabilityObservations_IngestionRunId_BatchNumber",
                table: "RawVulnerabilityObservations",
                columns: new[] { "IngestionRunId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RawVulnerabilityObservations_SourceSystemId",
                table: "RawVulnerabilityObservations",
                column: "SourceSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_RawVulnerabilityObservations_TenantId_SourceSystemId_Extern~",
                table: "RawVulnerabilityObservations",
                columns: new[] { "TenantId", "SourceSystemId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "UX_RawVulnerabilityObservations_Run_Source_ExternalId",
                table: "RawVulnerabilityObservations",
                columns: new[] { "IngestionRunId", "SourceSystemId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SoftwareReleases_Product_Version",
                table: "SoftwareReleases",
                columns: new[] { "SoftwareProductId", "NormalizedVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareSourceIdentities_SoftwareProductId",
                table: "SoftwareSourceIdentities",
                column: "SoftwareProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareSourceIdentities_SoftwareReleaseId",
                table: "SoftwareSourceIdentities",
                column: "SoftwareReleaseId");

            migrationBuilder.CreateIndex(
                name: "UX_SoftwareSourceIdentities_Source_ExternalId",
                table: "SoftwareSourceIdentities",
                columns: new[] { "SourceSystemId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngestionRunDeltas");

            migrationBuilder.DropTable(
                name: "RawDeviceObservations");

            migrationBuilder.DropTable(
                name: "RawExposureObservations");

            migrationBuilder.DropTable(
                name: "RawInstallationObservations");

            migrationBuilder.DropTable(
                name: "RawSoftwareObservations");

            migrationBuilder.DropTable(
                name: "RawVulnerabilityObservations");

            migrationBuilder.DropTable(
                name: "SoftwareSourceIdentities");

            migrationBuilder.DropTable(
                name: "SoftwareReleases");
        }
    }
}
