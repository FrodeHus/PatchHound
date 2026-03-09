using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GeneratedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIReports", x => x.Id);
                });

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
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OldValues = table.Column<string>(type: "text", nullable: true),
                    NewValues = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnrichmentJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetModel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrichmentJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnrichmentRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    JobsClaimed = table.Column<int>(type: "integer", nullable: false),
                    JobsSucceeded = table.Column<int>(type: "integer", nullable: false),
                    JobsNoData = table.Column<int>(type: "integer", nullable: false),
                    JobsFailed = table.Column<int>(type: "integer", nullable: false),
                    JobsRetried = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrichmentRun", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnrichmentSourceConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ActiveEnrichmentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseAcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSucceededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrichmentSourceConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FetchedVulnerabilityCount = table.Column<int>(type: "integer", nullable: false),
                    FetchedAssetCount = table.Column<int>(type: "integer", nullable: false),
                    FetchedSoftwareInstallationCount = table.Column<int>(type: "integer", nullable: false),
                    StagedVulnerabilityCount = table.Column<int>(type: "integer", nullable: false),
                    StagedExposureCount = table.Column<int>(type: "integer", nullable: false),
                    MergedExposureCount = table.Column<int>(type: "integer", nullable: false),
                    OpenedProjectionCount = table.Column<int>(type: "integer", nullable: false),
                    ResolvedProjectionCount = table.Column<int>(type: "integer", nullable: false),
                    StagedAssetCount = table.Column<int>(type: "integer", nullable: false),
                    MergedAssetCount = table.Column<int>(type: "integer", nullable: false),
                    StagedSoftwareLinkCount = table.Column<int>(type: "integer", nullable: false),
                    ResolvedSoftwareLinkCount = table.Column<int>(type: "integer", nullable: false),
                    InstallationsCreated = table.Column<int>(type: "integer", nullable: false),
                    InstallationsTouched = table.Column<int>(type: "integer", nullable: false),
                    InstallationEpisodesOpened = table.Column<int>(type: "integer", nullable: false),
                    InstallationEpisodesSeen = table.Column<int>(type: "integer", nullable: false),
                    StaleInstallationsMarked = table.Column<int>(type: "integer", nullable: false),
                    InstallationsRemoved = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    RelatedEntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationalSeverities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdjustedSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Justification = table.Column<string>(type: "text", nullable: false),
                    AssetCriticalityFactor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExposureFactor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CompensatingControls = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AdjustedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AdjustedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationalSeverities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemediationTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Justification = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSlaNotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskAcceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Justification = table.Column<string>(type: "text", nullable: false),
                    Conditions = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ExpiryDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewFrequency = table.Column<int>(type: "integer", nullable: true),
                    NextReviewDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskAcceptances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StagedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StagedDeviceSoftwareInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeviceExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SoftwareExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedDeviceSoftwareInstallations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StagedVulnerabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    VendorSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedVulnerabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StagedVulnerabilityExposures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VulnerabilityExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssetExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssetName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedVulnerabilityExposures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EntraTenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSlaConfigurations",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriticalDays = table.Column<int>(type: "integer", nullable: false),
                    HighDays = table.Column<int>(type: "integer", nullable: false),
                    MediumDays = table.Column<int>(type: "integer", nullable: false),
                    LowDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSlaConfigurations", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "TenantSourceConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    SyncSchedule = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CredentialTenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TokenScope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ManualRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSucceededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActiveIngestionRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseAcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSourceConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EntraObjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vulnerabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    VendorSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CvssScore = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    CvssVector = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PublishedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProductVendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProductName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ProductVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vulnerabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Criticality = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    FallbackTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    SecurityProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceComputerDnsName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DeviceHealthStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceOsPlatform = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceOsVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceRiskScore = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceLastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeviceLastIpAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceAadDeviceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assets_AssetSecurityProfiles_SecurityProfileId",
                        column: x => x.SecurityProfileId,
                        principalTable: "AssetSecurityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTenantRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTenantRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTenantRoles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserTenantRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityAffectedSoftware",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vulnerable = table.Column<bool>(type: "boolean", nullable: false),
                    Criteria = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    VersionStartIncluding = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    VersionStartExcluding = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    VersionEndIncluding = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    VersionEndExcluding = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityAffectedSoftware", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VulnerabilityAffectedSoftware_Vulnerabilities_Vulnerability~",
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
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: false)
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
                name: "DeviceSoftwareInstallationEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MissingSyncCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSoftwareInstallationEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceSoftwareInstallationEpisodes_Assets_DeviceAssetId",
                        column: x => x.DeviceAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceSoftwareInstallationEpisodes_Assets_SoftwareAssetId",
                        column: x => x.SoftwareAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceSoftwareInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MissingSyncCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSoftwareInstallations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceSoftwareInstallations_Assets_DeviceAssetId",
                        column: x => x.DeviceAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceSoftwareInstallations_Assets_SoftwareAssetId",
                        column: x => x.SoftwareAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareCpeBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cpe23Uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    BindingMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Confidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MatchedVendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MatchedProduct = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MatchedVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareCpeBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoftwareCpeBindings_Assets_SoftwareAssetId",
                        column: x => x.SoftwareAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareVulnerabilityMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Confidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                        name: "FK_SoftwareVulnerabilityMatches_Vulnerabilities_VulnerabilityId",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "VulnerabilityAssetEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MissingSyncCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
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
                        name: "FK_VulnerabilityAssetEpisodes_Vulnerabilities_VulnerabilityId",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    DetectedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                        name: "FK_VulnerabilityAssets_Vulnerabilities_VulnerabilityId",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIReports_TenantId",
                table: "AIReports",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AIReports_VulnerabilityId",
                table: "AIReports",
                column: "VulnerabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_SecurityProfileId",
                table: "Assets",
                column: "SecurityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId",
                table: "Assets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_ExternalId",
                table: "Assets",
                columns: new[] { "TenantId", "ExternalId" },
                unique: true);

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
                name: "IX_AuditLogEntries_EntityType_EntityId",
                table: "AuditLogEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_TenantId",
                table: "AuditLogEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Timestamp",
                table: "AuditLogEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_EntityType_EntityId",
                table: "Comments",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_TenantId",
                table: "Comments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallationEpisodes_DeviceAssetId_SoftwareAs~",
                table: "DeviceSoftwareInstallationEpisodes",
                columns: new[] { "DeviceAssetId", "SoftwareAssetId", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallationEpisodes_SoftwareAssetId",
                table: "DeviceSoftwareInstallationEpisodes",
                column: "SoftwareAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallationEpisodes_TenantId",
                table: "DeviceSoftwareInstallationEpisodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallations_DeviceAssetId_SoftwareAssetId",
                table: "DeviceSoftwareInstallations",
                columns: new[] { "DeviceAssetId", "SoftwareAssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallations_SoftwareAssetId",
                table: "DeviceSoftwareInstallations",
                column: "SoftwareAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSoftwareInstallations_TenantId",
                table: "DeviceSoftwareInstallations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentJobs_SourceKey_Status_NextAttemptAt",
                table: "EnrichmentJobs",
                columns: new[] { "SourceKey", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentJobs_SourceKey_TargetModel_TargetId",
                table: "EnrichmentJobs",
                columns: new[] { "SourceKey", "TargetModel", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentJobs_TenantId",
                table: "EnrichmentJobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentRun_SourceKey",
                table: "EnrichmentRun",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentRun_StartedAt",
                table: "EnrichmentRun",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentSourceConfigurations_ActiveEnrichmentRunId",
                table: "EnrichmentSourceConfigurations",
                column: "ActiveEnrichmentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentSourceConfigurations_SourceKey",
                table: "EnrichmentSourceConfigurations",
                column: "SourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngestionRuns_TenantId_SourceKey_StartedAt",
                table: "IngestionRuns",
                columns: new[] { "TenantId", "SourceKey", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId",
                table: "Notifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationalSeverities_TenantId_VulnerabilityId",
                table: "OrganizationalSeverities",
                columns: new[] { "TenantId", "VulnerabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemediationTasks_AssigneeId",
                table: "RemediationTasks",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationTasks_Status",
                table: "RemediationTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationTasks_TenantId",
                table: "RemediationTasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAcceptances_Status",
                table: "RiskAcceptances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAcceptances_TenantId",
                table: "RiskAcceptances",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareCpeBindings_SoftwareAssetId",
                table: "SoftwareCpeBindings",
                column: "SoftwareAssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareCpeBindings_TenantId",
                table: "SoftwareCpeBindings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareVulnerabilityMatches_SoftwareAssetId",
                table: "SoftwareVulnerabilityMatches",
                column: "SoftwareAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareVulnerabilityMatches_TenantId",
                table: "SoftwareVulnerabilityMatches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareVulnerabilityMatches_TenantId_SoftwareAssetId_Vulne~",
                table: "SoftwareVulnerabilityMatches",
                columns: new[] { "TenantId", "SoftwareAssetId", "VulnerabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareVulnerabilityMatches_VulnerabilityId",
                table: "SoftwareVulnerabilityMatches",
                column: "VulnerabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedAssets_IngestionRunId",
                table: "StagedAssets",
                column: "IngestionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedAssets_TenantId_SourceKey_ExternalId",
                table: "StagedAssets",
                columns: new[] { "TenantId", "SourceKey", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedDeviceSoftwareInstallations_IngestionRunId",
                table: "StagedDeviceSoftwareInstallations",
                column: "IngestionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedDeviceSoftwareInstallations_TenantId_SourceKey_Device~",
                table: "StagedDeviceSoftwareInstallations",
                columns: new[] { "TenantId", "SourceKey", "DeviceExternalId", "SoftwareExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilities_IngestionRunId",
                table: "StagedVulnerabilities",
                column: "IngestionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilities_TenantId_SourceKey_ExternalId",
                table: "StagedVulnerabilities",
                columns: new[] { "TenantId", "SourceKey", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilityExposures_IngestionRunId",
                table: "StagedVulnerabilityExposures",
                column: "IngestionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilityExposures_TenantId_SourceKey_Vulnerabili~",
                table: "StagedVulnerabilityExposures",
                columns: new[] { "TenantId", "SourceKey", "VulnerabilityExternalId", "AssetExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_UserId",
                table: "TeamMembers",
                columns: new[] { "TeamId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_UserId",
                table: "TeamMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_TenantId_Name",
                table: "Teams",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_EntraTenantId",
                table: "Tenants",
                column: "EntraTenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSourceConfigurations_TenantId_SourceKey",
                table: "TenantSourceConfigurations",
                columns: new[] { "TenantId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_EntraObjectId",
                table: "Users",
                column: "EntraObjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTenantRoles_TenantId",
                table: "UserTenantRoles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenantRoles_UserId_TenantId_Role",
                table: "UserTenantRoles",
                columns: new[] { "UserId", "TenantId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vulnerabilities_Status",
                table: "Vulnerabilities",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Vulnerabilities_TenantId",
                table: "Vulnerabilities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Vulnerabilities_TenantId_ExternalId",
                table: "Vulnerabilities",
                columns: new[] { "TenantId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAffectedSoftware_VulnerabilityId",
                table: "VulnerabilityAffectedSoftware",
                column: "VulnerabilityId");

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

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetEpisodes_AssetId",
                table: "VulnerabilityAssetEpisodes",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetEpisodes_TenantId",
                table: "VulnerabilityAssetEpisodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssetEpisodes_VulnerabilityId_AssetId_EpisodeN~",
                table: "VulnerabilityAssetEpisodes",
                columns: new[] { "VulnerabilityId", "AssetId", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssets_AssetId",
                table: "VulnerabilityAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAssets_VulnerabilityId_AssetId",
                table: "VulnerabilityAssets",
                columns: new[] { "VulnerabilityId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityReferences_VulnerabilityId",
                table: "VulnerabilityReferences",
                column: "VulnerabilityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIReports");

            migrationBuilder.DropTable(
                name: "AuditLogEntries");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "DeviceSoftwareInstallationEpisodes");

            migrationBuilder.DropTable(
                name: "DeviceSoftwareInstallations");

            migrationBuilder.DropTable(
                name: "EnrichmentJobs");

            migrationBuilder.DropTable(
                name: "EnrichmentRun");

            migrationBuilder.DropTable(
                name: "EnrichmentSourceConfigurations");

            migrationBuilder.DropTable(
                name: "IngestionRuns");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OrganizationalSeverities");

            migrationBuilder.DropTable(
                name: "RemediationTasks");

            migrationBuilder.DropTable(
                name: "RiskAcceptances");

            migrationBuilder.DropTable(
                name: "SoftwareCpeBindings");

            migrationBuilder.DropTable(
                name: "SoftwareVulnerabilityMatches");

            migrationBuilder.DropTable(
                name: "StagedAssets");

            migrationBuilder.DropTable(
                name: "StagedDeviceSoftwareInstallations");

            migrationBuilder.DropTable(
                name: "StagedVulnerabilities");

            migrationBuilder.DropTable(
                name: "StagedVulnerabilityExposures");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "TenantSlaConfigurations");

            migrationBuilder.DropTable(
                name: "TenantSourceConfigurations");

            migrationBuilder.DropTable(
                name: "UserTenantRoles");

            migrationBuilder.DropTable(
                name: "VulnerabilityAffectedSoftware");

            migrationBuilder.DropTable(
                name: "VulnerabilityAssetAssessments");

            migrationBuilder.DropTable(
                name: "VulnerabilityAssetEpisodes");

            migrationBuilder.DropTable(
                name: "VulnerabilityAssets");

            migrationBuilder.DropTable(
                name: "VulnerabilityReferences");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "Vulnerabilities");

            migrationBuilder.DropTable(
                name: "AssetSecurityProfiles");
        }
    }
}
