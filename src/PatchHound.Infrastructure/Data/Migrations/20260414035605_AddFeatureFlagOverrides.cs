using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureFlagOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AIReports_TenantVulnerabilities_TenantVulnerabilityId",
                table: "AIReports");

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationalSeverities_TenantVulnerabilities_TenantVulner~",
                table: "OrganizationalSeverities");

            migrationBuilder.DropTable(
                name: "AssetScanProfileAssignments");

            migrationBuilder.DropTable(
                name: "NormalizedSoftwareVulnerabilityProjections");

            migrationBuilder.DropTable(
                name: "SoftwareVulnerabilityMatches");

            migrationBuilder.DropTable(
                name: "StagedAuthenticatedScanSoftware");

            migrationBuilder.DropTable(
                name: "VulnerabilityAssetAssessments");

            migrationBuilder.DropTable(
                name: "VulnerabilityAssets");

            migrationBuilder.DropTable(
                name: "VulnerabilityDefinitionAffectedSoftware");

            migrationBuilder.DropTable(
                name: "VulnerabilityDefinitionReferences");

            migrationBuilder.DropTable(
                name: "VulnerabilityEpisodeRiskAssessments");

            migrationBuilder.DropTable(
                name: "VulnerabilityThreatAssessments");

            migrationBuilder.DropTable(
                name: "VulnerabilityAssetEpisodes");

            migrationBuilder.DropTable(
                name: "TenantVulnerabilities");

            migrationBuilder.DropTable(
                name: "VulnerabilityDefinitions");

            migrationBuilder.RenameColumn(
                name: "AssetId",
                table: "ScanJobs",
                newName: "DeviceId");

            migrationBuilder.RenameColumn(
                name: "TenantVulnerabilityId",
                table: "OrganizationalSeverities",
                newName: "VulnerabilityId");

            migrationBuilder.RenameIndex(
                name: "IX_OrganizationalSeverities_TenantVulnerabilityId",
                table: "OrganizationalSeverities",
                newName: "IX_OrganizationalSeverities_VulnerabilityId");

            migrationBuilder.RenameIndex(
                name: "IX_OrganizationalSeverities_TenantId_TenantVulnerabilityId",
                table: "OrganizationalSeverities",
                newName: "IX_OrganizationalSeverities_TenantId_VulnerabilityId");

            migrationBuilder.RenameColumn(
                name: "TenantVulnerabilityId",
                table: "AIReports",
                newName: "VulnerabilityId");

            migrationBuilder.RenameIndex(
                name: "IX_AIReports_TenantVulnerabilityId",
                table: "AIReports",
                newName: "IX_AIReports_VulnerabilityId");

            migrationBuilder.CreateTable(
                name: "DeviceRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    FilterDefinition = table.Column<string>(type: "text", nullable: false),
                    Operations = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastMatchCount = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceScanProfileAssignments",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceScanProfileAssignments", x => new { x.DeviceId, x.ScanProfileId });
                });

            migrationBuilder.CreateTable(
                name: "FeatureFlagOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlagName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlagOverrides", x => x.Id);
                    table.CheckConstraint("CK_FeatureFlagOverrides_OneTarget", "(\"TenantId\" IS NOT NULL AND \"UserId\" IS NULL) OR (\"TenantId\" IS NULL AND \"UserId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_FeatureFlagOverrides_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FeatureFlagOverrides_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityProfiles",
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
                    ModifiedAttackVector = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModifiedAttackComplexity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModifiedPrivilegesRequired = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModifiedUserInteraction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModifiedScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModifiedConfidentialityImpact = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModifiedIntegrityImpact = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModifiedAvailabilityImpact = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalProductKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Vendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PrimaryCpe23Uri = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EndOfLifeAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceSystems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceSystems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StagedDetectedSoftware",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_StagedDetectedSoftware", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vulnerabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    VendorSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CvssScore = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    CvssVector = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PublishedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vulnerabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSoftwareProductInsights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SupplyChainEvidenceJson = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSoftwareProductInsights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSoftwareProductInsights_SoftwareProducts_SoftwareProd~",
                        column: x => x.SoftwareProductId,
                        principalTable: "SoftwareProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BaselineCriticality = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Criticality = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CriticalitySource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CriticalityReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CriticalityRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriticalityUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    FallbackTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    FallbackTeamRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    SecurityProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    SecurityProfileRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ComputerDnsName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    HealthStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OsPlatform = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OsVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExternalRiskLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastIpAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AadDeviceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GroupId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GroupName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExposureLevel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsAadJoined = table.Column<bool>(type: "boolean", nullable: true),
                    OnboardingStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceValue = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExposureImpactScore = table.Column<decimal>(type: "numeric", nullable: true),
                    ActiveInTenant = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Metadata = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_SourceSystems_SourceSystemId",
                        column: x => x.SourceSystemId,
                        principalTable: "SourceSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ObservedVendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ObservedName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoftwareAliases_SoftwareProducts_SoftwareProductId",
                        column: x => x.SoftwareProductId,
                        principalTable: "SoftwareProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SoftwareAliases_SourceSystems_SourceSystemId",
                        column: x => x.SourceSystemId,
                        principalTable: "SourceSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ThreatAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThreatScore = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    TechnicalScore = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    ExploitLikelihoodScore = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    ThreatActivityScore = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    EpssScore = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    KnownExploited = table.Column<bool>(type: "boolean", nullable: false),
                    PublicExploit = table.Column<bool>(type: "boolean", nullable: false),
                    ActiveAlert = table.Column<bool>(type: "boolean", nullable: false),
                    HasRansomwareAssociation = table.Column<bool>(type: "boolean", nullable: false),
                    HasMalwareAssociation = table.Column<bool>(type: "boolean", nullable: false),
                    DefenderLastRefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThreatAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThreatAssessments_Vulnerabilities_VulnerabilityId",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityApplicabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    CpeCriteria = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Vulnerable = table.Column<bool>(type: "boolean", nullable: false),
                    VersionStartIncluding = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    VersionStartExcluding = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    VersionEndIncluding = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    VersionEndExcluding = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityApplicabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VulnerabilityApplicabilities_SoftwareProducts_SoftwareProdu~",
                        column: x => x.SoftwareProductId,
                        principalTable: "SoftwareProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VulnerabilityApplicabilities_Vulnerabilities_VulnerabilityId",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Tags = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VulnerabilityReferences_Vulnerabilities_VulnerabilityId",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceBusinessLabels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessLabelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedByRuleId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceBusinessLabels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceBusinessLabels_BusinessLabels_BusinessLabelId",
                        column: x => x.BusinessLabelId,
                        principalTable: "BusinessLabels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceBusinessLabels_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    MaxEpisodeRiskScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CriticalCount = table.Column<int>(type: "integer", nullable: false),
                    HighCount = table.Column<int>(type: "integer", nullable: false),
                    MediumCount = table.Column<int>(type: "integer", nullable: false),
                    LowCount = table.Column<int>(type: "integer", nullable: false),
                    OpenEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceRiskScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceRiskScores_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceTags_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstalledSoftware",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstalledSoftware", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstalledSoftware_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstalledSoftware_SoftwareProducts_SoftwareProductId",
                        column: x => x.SoftwareProductId,
                        principalTable: "SoftwareProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InstalledSoftware_SourceSystems_SourceSystemId",
                        column: x => x.SourceSystemId,
                        principalTable: "SourceSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceBusinessLabels_AssignedByRuleId",
                table: "DeviceBusinessLabels",
                column: "AssignedByRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceBusinessLabels_BusinessLabelId",
                table: "DeviceBusinessLabels",
                column: "BusinessLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceBusinessLabels_DeviceId_BusinessLabelId_SourceKey",
                table: "DeviceBusinessLabels",
                columns: new[] { "DeviceId", "BusinessLabelId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceBusinessLabels_TenantId",
                table: "DeviceBusinessLabels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRiskScores_DeviceId",
                table: "DeviceRiskScores",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRiskScores_TenantId",
                table: "DeviceRiskScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRiskScores_TenantId_DeviceId",
                table: "DeviceRiskScores",
                columns: new[] { "TenantId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRules_TenantId",
                table: "DeviceRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceRules_TenantId_Priority",
                table: "DeviceRules",
                columns: new[] { "TenantId", "Priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_SecurityProfileId",
                table: "Devices",
                column: "SecurityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_SourceSystemId",
                table: "Devices",
                column: "SourceSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId",
                table: "Devices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_ActiveInTenant",
                table: "Devices",
                columns: new[] { "TenantId", "ActiveInTenant" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_SourceSystemId_ExternalId",
                table: "Devices",
                columns: new[] { "TenantId", "SourceSystemId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceScanProfileAssignments_TenantId_ScanProfileId",
                table: "DeviceScanProfileAssignments",
                columns: new[] { "TenantId", "ScanProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTags_DeviceId_Key",
                table: "DeviceTags",
                columns: new[] { "DeviceId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTags_TenantId",
                table: "DeviceTags",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTags_TenantId_Key",
                table: "DeviceTags",
                columns: new[] { "TenantId", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagOverrides_FlagName_TenantId",
                table: "FeatureFlagOverrides",
                columns: new[] { "FlagName", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagOverrides_FlagName_UserId",
                table: "FeatureFlagOverrides",
                columns: new[] { "FlagName", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagOverrides_TenantId",
                table: "FeatureFlagOverrides",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagOverrides_UserId",
                table: "FeatureFlagOverrides",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledSoftware_DeviceId",
                table: "InstalledSoftware",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledSoftware_SoftwareProductId",
                table: "InstalledSoftware",
                column: "SoftwareProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledSoftware_SourceSystemId",
                table: "InstalledSoftware",
                column: "SourceSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledSoftware_TenantId",
                table: "InstalledSoftware",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledSoftware_TenantId_DeviceId_SoftwareProductId_Sourc~",
                table: "InstalledSoftware",
                columns: new[] { "TenantId", "DeviceId", "SoftwareProductId", "SourceSystemId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstalledSoftware_TenantId_SoftwareProductId",
                table: "InstalledSoftware",
                columns: new[] { "TenantId", "SoftwareProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityProfiles_TenantId",
                table: "SecurityProfiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityProfiles_TenantId_Name",
                table: "SecurityProfiles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareAliases_SoftwareProductId",
                table: "SoftwareAliases",
                column: "SoftwareProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareAliases_SourceSystemId_ExternalId",
                table: "SoftwareAliases",
                columns: new[] { "SourceSystemId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareProducts_CanonicalProductKey",
                table: "SoftwareProducts",
                column: "CanonicalProductKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceSystems_Key",
                table: "SourceSystems",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StagedDetectedSoftware_ScanJobId",
                table: "StagedDetectedSoftware",
                column: "ScanJobId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftwareProductInsights_SoftwareProductId",
                table: "TenantSoftwareProductInsights",
                column: "SoftwareProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftwareProductInsights_TenantId_SoftwareProductId",
                table: "TenantSoftwareProductInsights",
                columns: new[] { "TenantId", "SoftwareProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThreatAssessments_VulnerabilityId",
                table: "ThreatAssessments",
                column: "VulnerabilityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vulnerabilities_ExternalId",
                table: "Vulnerabilities",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Vulnerabilities_Source_ExternalId",
                table: "Vulnerabilities",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityApplicabilities_SoftwareProductId",
                table: "VulnerabilityApplicabilities",
                column: "SoftwareProductId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityApplicabilities_VulnerabilityId_CpeCriteria",
                table: "VulnerabilityApplicabilities",
                columns: new[] { "VulnerabilityId", "CpeCriteria" });

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityApplicabilities_VulnerabilityId_SoftwareProduc~",
                table: "VulnerabilityApplicabilities",
                columns: new[] { "VulnerabilityId", "SoftwareProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityReferences_VulnerabilityId_Url",
                table: "VulnerabilityReferences",
                columns: new[] { "VulnerabilityId", "Url" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AIReports_Vulnerabilities_VulnerabilityId",
                table: "AIReports",
                column: "VulnerabilityId",
                principalTable: "Vulnerabilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationalSeverities_Vulnerabilities_VulnerabilityId",
                table: "OrganizationalSeverities",
                column: "VulnerabilityId",
                principalTable: "Vulnerabilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AIReports_Vulnerabilities_VulnerabilityId",
                table: "AIReports");

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationalSeverities_Vulnerabilities_VulnerabilityId",
                table: "OrganizationalSeverities");

            migrationBuilder.DropTable(
                name: "DeviceBusinessLabels");

            migrationBuilder.DropTable(
                name: "DeviceRiskScores");

            migrationBuilder.DropTable(
                name: "DeviceRules");

            migrationBuilder.DropTable(
                name: "DeviceScanProfileAssignments");

            migrationBuilder.DropTable(
                name: "DeviceTags");

            migrationBuilder.DropTable(
                name: "FeatureFlagOverrides");

            migrationBuilder.DropTable(
                name: "InstalledSoftware");

            migrationBuilder.DropTable(
                name: "SecurityProfiles");

            migrationBuilder.DropTable(
                name: "SoftwareAliases");

            migrationBuilder.DropTable(
                name: "StagedDetectedSoftware");

            migrationBuilder.DropTable(
                name: "TenantSoftwareProductInsights");

            migrationBuilder.DropTable(
                name: "ThreatAssessments");

            migrationBuilder.DropTable(
                name: "VulnerabilityApplicabilities");

            migrationBuilder.DropTable(
                name: "VulnerabilityReferences");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "SoftwareProducts");

            migrationBuilder.DropTable(
                name: "Vulnerabilities");

            migrationBuilder.DropTable(
                name: "SourceSystems");

            migrationBuilder.RenameColumn(
                name: "DeviceId",
                table: "ScanJobs",
                newName: "AssetId");

            migrationBuilder.RenameColumn(
                name: "VulnerabilityId",
                table: "OrganizationalSeverities",
                newName: "TenantVulnerabilityId");

            migrationBuilder.RenameIndex(
                name: "IX_OrganizationalSeverities_VulnerabilityId",
                table: "OrganizationalSeverities",
                newName: "IX_OrganizationalSeverities_TenantVulnerabilityId");

            migrationBuilder.RenameIndex(
                name: "IX_OrganizationalSeverities_TenantId_VulnerabilityId",
                table: "OrganizationalSeverities",
                newName: "IX_OrganizationalSeverities_TenantId_TenantVulnerabilityId");

            migrationBuilder.RenameColumn(
                name: "VulnerabilityId",
                table: "AIReports",
                newName: "TenantVulnerabilityId");

            migrationBuilder.RenameIndex(
                name: "IX_AIReports_VulnerabilityId",
                table: "AIReports",
                newName: "IX_AIReports_TenantVulnerabilityId");

            migrationBuilder.CreateTable(
                name: "AssetScanProfileAssignments",
                columns: table => new
                {
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedByRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetScanProfileAssignments", x => new { x.AssetId, x.ScanProfileId });
                });

            migrationBuilder.CreateTable(
                name: "StagedAuthenticatedScanSoftware",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CanonicalProductKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CanonicalVendor = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Category = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DetectedVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DeviceAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryCpe23Uri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ScanJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedAuthenticatedScanSoftware", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CvssScore = table.Column<decimal>(type: "numeric", nullable: true),
                    CvssVector = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProductVendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProductVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PublishedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    VendorSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NormalizedSoftwareVulnerabilityProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffectedDeviceCount = table.Column<int>(type: "integer", nullable: false),
                    AffectedInstallCount = table.Column<int>(type: "integer", nullable: false),
                    AffectedVersionCount = table.Column<int>(type: "integer", nullable: false),
                    BestConfidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BestMatchMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedSoftwareVulnerabilityProjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NormalizedSoftwareVulnerabilityProjections_TenantSoftware_T~",
                        column: x => x.TenantSoftwareId,
                        principalTable: "TenantSoftware",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NormalizedSoftwareVulnerabilityProjections_VulnerabilityDef~",
                        column: x => x.VulnerabilityDefinitionId,
                        principalTable: "VulnerabilityDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareVulnerabilityMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Confidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MatchMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareVulnerabilityMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoftwareVulnerabilityMatches_Assets_SoftwareAssetId",
                        column: x => x.SoftwareAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SoftwareVulnerabilityMatches_VulnerabilityDefinitions_Vulne~",
                        column: x => x.VulnerabilityDefinitionId,
                        principalTable: "VulnerabilityDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantVulnerabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantVulnerabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantVulnerabilities_VulnerabilityDefinitions_Vulnerabilit~",
                        column: x => x.VulnerabilityDefinitionId,
                        principalTable: "VulnerabilityDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityDefinitionAffectedSoftware",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Criteria = table.Column<string>(type: "text", nullable: false),
                    VersionEndExcluding = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    VersionEndIncluding = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    VersionStartExcluding = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    VersionStartIncluding = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Vulnerable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityDefinitionAffectedSoftware", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VulnerabilityDefinitionAffectedSoftware_VulnerabilityDefini~",
                        column: x => x.VulnerabilityDefinitionId,
                        principalTable: "VulnerabilityDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityDefinitionReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityDefinitionReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VulnerabilityDefinitionReferences_VulnerabilityDefinitions_~",
                        column: x => x.VulnerabilityDefinitionId,
                        principalTable: "VulnerabilityDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityThreatAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveAlert = table.Column<bool>(type: "boolean", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DefenderLastRefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EpssScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    ExploitLikelihoodScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    HasMalwareAssociation = table.Column<bool>(type: "boolean", nullable: false),
                    HasRansomwareAssociation = table.Column<bool>(type: "boolean", nullable: false),
                    KnownExploited = table.Column<bool>(type: "boolean", nullable: false),
                    PublicExploit = table.Column<bool>(type: "boolean", nullable: false),
                    TechnicalScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ThreatActivityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ThreatScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityThreatAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VulnerabilityThreatAssessments_VulnerabilityDefinitions_Vul~",
                        column: x => x.VulnerabilityDefinitionId,
                        principalTable: "VulnerabilityDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityAssetAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetSecurityProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantVulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseScore = table.Column<decimal>(type: "numeric", nullable: true),
                    BaseSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BaseVector = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EffectiveScore = table.Column<decimal>(type: "numeric", nullable: true),
                    EffectiveSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EffectiveVector = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    ReasonSummary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
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
                        name: "FK_VulnerabilityAssetAssessments_TenantVulnerabilities_TenantV~",
                        column: x => x.TenantVulnerabilityId,
                        principalTable: "TenantVulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityAssetEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantVulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MissingSyncCount = table.Column<int>(type: "integer", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityAssetEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VulnerabilityAssetEpisodes_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VulnerabilityAssetEpisodes_TenantVulnerabilities_TenantVuln~",
                        column: x => x.TenantVulnerabilityId,
                        principalTable: "TenantVulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantVulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DetectedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProductVendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProductVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResolvedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VulnerabilityAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VulnerabilityAssets_TenantVulnerabilities_TenantVulnerabili~",
                        column: x => x.TenantVulnerabilityId,
                        principalTable: "TenantVulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityEpisodeRiskAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantVulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityAssetEpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContextScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    EpisodeRiskScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    OperationalScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RiskBand = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThreatScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityEpisodeRiskAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VulnerabilityEpisodeRiskAssessments_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VulnerabilityEpisodeRiskAssessments_TenantVulnerabilities_T~",
                        column: x => x.TenantVulnerabilityId,
                        principalTable: "TenantVulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VulnerabilityEpisodeRiskAssessments_VulnerabilityAssetEpiso~",
                        column: x => x.VulnerabilityAssetEpisodeId,
                        principalTable: "VulnerabilityAssetEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetScanProfileAssignments_TenantId_ScanProfileId",
                table: "AssetScanProfileAssignments",
                columns: new[] { "TenantId", "ScanProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareVulnerabilityProjections_SnapshotId",
                table: "NormalizedSoftwareVulnerabilityProjections",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareVulnerabilityProjections_TenantId",
                table: "NormalizedSoftwareVulnerabilityProjections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareVulnerabilityProjections_TenantId_Snapsho~",
                table: "NormalizedSoftwareVulnerabilityProjections",
                columns: new[] { "TenantId", "SnapshotId", "TenantSoftwareId", "VulnerabilityDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareVulnerabilityProjections_TenantSoftwareId",
                table: "NormalizedSoftwareVulnerabilityProjections",
                column: "TenantSoftwareId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareVulnerabilityProjections_VulnerabilityDef~",
                table: "NormalizedSoftwareVulnerabilityProjections",
                column: "VulnerabilityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareVulnerabilityMatches_SnapshotId",
                table: "SoftwareVulnerabilityMatches",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareVulnerabilityMatches_SoftwareAssetId",
                table: "SoftwareVulnerabilityMatches",
                column: "SoftwareAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareVulnerabilityMatches_TenantId",
                table: "SoftwareVulnerabilityMatches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareVulnerabilityMatches_TenantId_SnapshotId_SoftwareAs~",
                table: "SoftwareVulnerabilityMatches",
                columns: new[] { "TenantId", "SnapshotId", "SoftwareAssetId", "VulnerabilityDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareVulnerabilityMatches_VulnerabilityDefinitionId",
                table: "SoftwareVulnerabilityMatches",
                column: "VulnerabilityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedAuthenticatedScanSoftware_ScanJobId",
                table: "StagedAuthenticatedScanSoftware",
                column: "ScanJobId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantVulnerabilities_TenantId",
                table: "TenantVulnerabilities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantVulnerabilities_TenantId_SourceKey_Status",
                table: "TenantVulnerabilities",
                columns: new[] { "TenantId", "SourceKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantVulnerabilities_TenantId_VulnerabilityDefinitionId",
                table: "TenantVulnerabilities",
                columns: new[] { "TenantId", "VulnerabilityDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantVulnerabilities_VulnerabilityDefinitionId",
                table: "TenantVulnerabilities",
                column: "VulnerabilityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_AssetId",
                table: "VulnerabilityAssetAssessments",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_AssetSecurityProfileId",
                table: "VulnerabilityAssetAssessments",
                column: "AssetSecurityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_SnapshotId",
                table: "VulnerabilityAssetAssessments",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_TenantId",
                table: "VulnerabilityAssetAssessments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_TenantId_SnapshotId_TenantVul~",
                table: "VulnerabilityAssetAssessments",
                columns: new[] { "TenantId", "SnapshotId", "TenantVulnerabilityId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetAssessments_TenantVulnerabilityId",
                table: "VulnerabilityAssetAssessments",
                column: "TenantVulnerabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetEpisodes_AssetId",
                table: "VulnerabilityAssetEpisodes",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetEpisodes_TenantId",
                table: "VulnerabilityAssetEpisodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetEpisodes_TenantId_Status_TenantVulnerabil~",
                table: "VulnerabilityAssetEpisodes",
                columns: new[] { "TenantId", "Status", "TenantVulnerabilityId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetEpisodes_TenantId_TenantVulnerabilityId_A~",
                table: "VulnerabilityAssetEpisodes",
                columns: new[] { "TenantId", "TenantVulnerabilityId", "AssetId", "EpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetEpisodes_TenantVulnerabilityId_AssetId_Ep~",
                table: "VulnerabilityAssetEpisodes",
                columns: new[] { "TenantVulnerabilityId", "AssetId", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssets_AssetId",
                table: "VulnerabilityAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssets_SnapshotId",
                table: "VulnerabilityAssets",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssets_TenantVulnerabilityId_AssetId_SnapshotId",
                table: "VulnerabilityAssets",
                columns: new[] { "TenantVulnerabilityId", "AssetId", "SnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityDefinitionAffectedSoftware_VulnerabilityDefini~",
                table: "VulnerabilityDefinitionAffectedSoftware",
                column: "VulnerabilityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityDefinitionReferences_VulnerabilityDefinitionId",
                table: "VulnerabilityDefinitionReferences",
                column: "VulnerabilityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityDefinitions_ExternalId",
                table: "VulnerabilityDefinitions",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityDefinitions_Source",
                table: "VulnerabilityDefinitions",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityEpisodeRiskAssessments_AssetId",
                table: "VulnerabilityEpisodeRiskAssessments",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityEpisodeRiskAssessments_TenantId",
                table: "VulnerabilityEpisodeRiskAssessments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityEpisodeRiskAssessments_TenantId_ResolvedAt",
                table: "VulnerabilityEpisodeRiskAssessments",
                columns: new[] { "TenantId", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityEpisodeRiskAssessments_TenantId_TenantVulnerab~",
                table: "VulnerabilityEpisodeRiskAssessments",
                columns: new[] { "TenantId", "TenantVulnerabilityId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityEpisodeRiskAssessments_TenantVulnerabilityId",
                table: "VulnerabilityEpisodeRiskAssessments",
                column: "TenantVulnerabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityEpisodeRiskAssessments_VulnerabilityAssetEpiso~",
                table: "VulnerabilityEpisodeRiskAssessments",
                column: "VulnerabilityAssetEpisodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityThreatAssessments_VulnerabilityDefinitionId",
                table: "VulnerabilityThreatAssessments",
                column: "VulnerabilityDefinitionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AIReports_TenantVulnerabilities_TenantVulnerabilityId",
                table: "AIReports",
                column: "TenantVulnerabilityId",
                principalTable: "TenantVulnerabilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationalSeverities_TenantVulnerabilities_TenantVulner~",
                table: "OrganizationalSeverities",
                column: "TenantVulnerabilityId",
                principalTable: "TenantVulnerabilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
