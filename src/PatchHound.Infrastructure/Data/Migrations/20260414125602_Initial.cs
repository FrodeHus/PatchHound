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
                name: "AdvancedTools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SupportedAssetTypesJson = table.Column<string>(type: "text", nullable: false),
                    KqlQuery = table.Column<string>(type: "text", nullable: false),
                    AiPrompt = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvancedTools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetRules",
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
                    table.PrimaryKey("PK_AssetRules", x => x.Id);
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
                name: "BusinessLabels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessLabels", x => x.Id);
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
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
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
                name: "DeviceGroupRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeviceGroupId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceGroupName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    MaxAssetRiskScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CriticalEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    HighEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    MediumEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    LowEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    OpenEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceGroupRiskScores", x => x.Id);
                });

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
                    RefreshTtlHours = table.Column<int>(type: "integer", nullable: true),
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
                name: "IngestionCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Phase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
                    CursorJson = table.Column<string>(type: "text", nullable: false),
                    RecordsCommitted = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastCommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionCheckpoints", x => x.Id);
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
                    AbortRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StagedMachineCount = table.Column<int>(type: "integer", nullable: false),
                    StagedSoftwareCount = table.Column<int>(type: "integer", nullable: false),
                    StagedVulnerabilityCount = table.Column<int>(type: "integer", nullable: false),
                    PersistedMachineCount = table.Column<int>(type: "integer", nullable: false),
                    DeactivatedMachineCount = table.Column<int>(type: "integer", nullable: false),
                    PersistedSoftwareCount = table.Column<int>(type: "integer", nullable: false),
                    PersistedVulnerabilityCount = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NormalizedSoftware",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CanonicalVendor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CanonicalProductKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PrimaryCpe23Uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DescriptionGeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DescriptionProviderType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DescriptionProfileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DescriptionModel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizationMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Confidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastEvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EolProductSlug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EolDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EolLatestVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EolIsLts = table.Column<bool>(type: "boolean", nullable: true),
                    EolSupportEndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EolIsDiscontinued = table.Column<bool>(type: "boolean", nullable: true),
                    EolEnrichedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupplyChainRemediationPath = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SupplyChainInsightConfidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SupplyChainSourceFormat = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SupplyChainPrimaryComponentName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SupplyChainPrimaryComponentVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SupplyChainFixedVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SupplyChainAffectedVulnerabilityCount = table.Column<int>(type: "integer", nullable: true),
                    SupplyChainSummary = table.Column<string>(type: "text", nullable: true),
                    SupplyChainEnrichedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedSoftware", x => x.Id);
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
                name: "RemediationAiJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InputHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationAiJobs", x => x.Id);
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
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "SentinelConnectorConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DceEndpoint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DcrImmutableId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StreamName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelConnectorConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareDescriptionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantAiProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareDescriptionJobs", x => x.Id);
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
                name: "StagedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
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
                name: "StagedDeviceSoftwareInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
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
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
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
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
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
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsDynamic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantAiProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    Temperature = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    TopP = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DeploymentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ApiVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeepAlive = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AllowExternalResearch = table.Column<bool>(type: "boolean", nullable: false),
                    WebResearchMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IncludeCitations = table.Column<bool>(type: "boolean", nullable: false),
                    MaxResearchSources = table.Column<int>(type: "integer", nullable: false),
                    AllowedDomains = table.Column<string>(type: "text", nullable: false),
                    LastValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastValidationStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastValidationError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantAiProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantRiskScoreSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    CriticalAssetCount = table.Column<int>(type: "integer", nullable: false),
                    HighAssetCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantRiskScoreSnapshots", x => x.Id);
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
                    LowDays = table.Column<int>(type: "integer", nullable: false),
                    ApprovalExpiryHours = table.Column<int>(type: "integer", nullable: false)
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
                    ActiveSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuildingSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    EntraObjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Company = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    AccessScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Internal")
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
                name: "WorkflowDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GraphJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "defender"),
                    AssetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    DeviceComputerDnsName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DeviceHealthStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceOsPlatform = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceOsVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceRiskScore = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceLastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeviceActiveInTenant = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DeviceLastIpAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceAadDeviceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceGroupId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceGroupName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DeviceExposureLevel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceIsAadJoined = table.Column<bool>(type: "boolean", nullable: true),
                    DeviceOnboardingStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DeviceValue = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExposureImpactScore = table.Column<decimal>(type: "numeric", nullable: true),
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
                name: "NormalizedSoftwareAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "SoftwareCpeBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
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
                        name: "FK_SoftwareCpeBindings_NormalizedSoftware_NormalizedSoftwareId",
                        column: x => x.NormalizedSoftwareId,
                        principalTable: "NormalizedSoftware",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantSoftware",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    NormalizedSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemediationAiSummaryContent = table.Column<string>(type: "text", nullable: false),
                    RemediationAiSummaryInputHash = table.Column<string>(type: "text", nullable: false),
                    RemediationAiSummaryProviderType = table.Column<string>(type: "text", nullable: false),
                    RemediationAiSummaryProfileName = table.Column<string>(type: "text", nullable: false),
                    RemediationAiSummaryModel = table.Column<string>(type: "text", nullable: false),
                    RemediationAiOwnerRecommendationContent = table.Column<string>(type: "text", nullable: false),
                    RemediationAiAnalystAssessmentContent = table.Column<string>(type: "text", nullable: false),
                    RemediationAiExceptionRecommendationContent = table.Column<string>(type: "text", nullable: false),
                    RemediationAiRecommendedOutcome = table.Column<string>(type: "text", nullable: false),
                    RemediationAiRecommendedPriority = table.Column<string>(type: "text", nullable: false),
                    RemediationAiReviewStatus = table.Column<string>(type: "text", nullable: false),
                    RemediationAiReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RemediationAiReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemediationAiSummaryGeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSoftware", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSoftware_NormalizedSoftware_NormalizedSoftwareId",
                        column: x => x.NormalizedSoftwareId,
                        principalTable: "NormalizedSoftware",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemediationCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemediationCases_SoftwareProducts_SoftwareProductId",
                        column: x => x.SoftwareProductId,
                        principalTable: "SoftwareProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    MaxExposureScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CriticalExposureCount = table.Column<int>(type: "integer", nullable: false),
                    HighExposureCount = table.Column<int>(type: "integer", nullable: false),
                    MediumExposureCount = table.Column<int>(type: "integer", nullable: false),
                    LowExposureCount = table.Column<int>(type: "integer", nullable: false),
                    AffectedDeviceCount = table.Column<int>(type: "integer", nullable: false),
                    OpenExposureCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareRiskScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoftwareRiskScores_SoftwareProducts_SoftwareProductId",
                        column: x => x.SoftwareProductId,
                        principalTable: "SoftwareProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "TeamMembershipRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    FilterDefinition = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastMatchCount = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembershipRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMembershipRules_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    MaxAssetRiskScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CriticalEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    HighEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    MediumEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    LowEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    OpenEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamRiskScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamRiskScores_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.ForeignKey(
                        name: "FK_OrganizationalSeverities_Vulnerabilities_VulnerabilityId",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "WorkflowInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionVersion = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggerType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowInstances_WorkflowDefinitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetBusinessLabels",
                columns: table => new
                {
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessLabelId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedByRuleId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetBusinessLabels", x => new { x.AssetId, x.BusinessLabelId, x.SourceKey });
                    table.ForeignKey(
                        name: "FK_AssetBusinessLabels_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetBusinessLabels_BusinessLabels_BusinessLabelId",
                        column: x => x.BusinessLabelId,
                        principalTable: "BusinessLabels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_AssetRiskScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetRiskScores_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetTags_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
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
                name: "NormalizedSoftwareInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
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
                        name: "FK_NormalizedSoftwareInstallations_TenantSoftware_TenantSoftwa~",
                        column: x => x.TenantSoftwareId,
                        principalTable: "TenantSoftware",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantSoftwareRiskScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    OverallScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    MaxEpisodeRiskScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CriticalEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    HighEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    MediumEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    LowEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    AffectedDeviceCount = table.Column<int>(type: "integer", nullable: false),
                    OpenEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    FactorsJson = table.Column<string>(type: "text", nullable: false),
                    CalculationVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSoftwareRiskScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSoftwareRiskScores_TenantSoftware_TenantSoftwareId",
                        column: x => x.TenantSoftwareId,
                        principalTable: "TenantSoftware",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AIReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantAiProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ProviderType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SystemPromptHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Temperature = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GeneratedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIReports_RemediationCases_RemediationCaseId",
                        column: x => x.RemediationCaseId,
                        principalTable: "RemediationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIReports_TenantAiProfiles_TenantAiProfileId",
                        column: x => x.TenantAiProfileId,
                        principalTable: "TenantAiProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AIReports_Vulnerabilities_VulnerabilityId",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RemediationWorkflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareOwnerTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecurrenceSourceWorkflowId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProposedOutcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ApprovalMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentStageStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationWorkflows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemediationWorkflows_RemediationCases_RemediationCaseId",
                        column: x => x.RemediationCaseId,
                        principalTable: "RemediationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskAcceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.ForeignKey(
                        name: "FK_RiskAcceptances_RemediationCases_RemediationCaseId",
                        column: x => x.RemediationCaseId,
                        principalTable: "RemediationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "WorkflowNodeExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NodeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InputJson = table.Column<string>(type: "jsonb", nullable: true),
                    OutputJson = table.Column<string>(type: "jsonb", nullable: true),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssignedTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowNodeExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowNodeExecutions_WorkflowInstances_WorkflowInstanceId",
                        column: x => x.WorkflowInstanceId,
                        principalTable: "WorkflowInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalystRecommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationWorkflowId = table.Column<Guid>(type: "uuid", nullable: true),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecommendedOutcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Rationale = table.Column<string>(type: "text", nullable: false),
                    PriorityOverride = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AnalystId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalystRecommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalystRecommendations_RemediationCases_RemediationCaseId",
                        column: x => x.RemediationCaseId,
                        principalTable: "RemediationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnalystRecommendations_RemediationWorkflows_RemediationWork~",
                        column: x => x.RemediationWorkflowId,
                        principalTable: "RemediationWorkflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RemediationDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationWorkflowId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApprovalStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Justification = table.Column<string>(type: "text", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaintenanceWindowDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiryDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReEvaluationDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSlaNotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemediationDecisions_RemediationCases_RemediationCaseId",
                        column: x => x.RemediationCaseId,
                        principalTable: "RemediationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RemediationDecisions_RemediationWorkflows_RemediationWorkfl~",
                        column: x => x.RemediationWorkflowId,
                        principalTable: "RemediationWorkflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RemediationWorkflowStageRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationWorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AssignedTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationWorkflowStageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemediationWorkflowStageRecords_RemediationWorkflows_Remedi~",
                        column: x => x.RemediationWorkflowId,
                        principalTable: "RemediationWorkflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceVulnerabilityExposures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    InstalledSoftwareId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchedVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MatchSource = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FirstObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceVulnerabilityExposures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceVulnerabilityExposures_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceVulnerabilityExposures_InstalledSoftware_InstalledSof~",
                        column: x => x.InstalledSoftwareId,
                        principalTable: "InstalledSoftware",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DeviceVulnerabilityExposures_SoftwareProducts_SoftwareProdu~",
                        column: x => x.SoftwareProductId,
                        principalTable: "SoftwareProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceVulnerabilityExposures_Vulnerabilities_VulnerabilityId",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowActions_WorkflowInstances_WorkflowInstanceId",
                        column: x => x.WorkflowInstanceId,
                        principalTable: "WorkflowInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowActions_WorkflowNodeExecutions_NodeExecutionId",
                        column: x => x.NodeExecutionId,
                        principalTable: "WorkflowNodeExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationWorkflowId = table.Column<Guid>(type: "uuid", nullable: true),
                    RemediationDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequiresJustification = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolutionJustification = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalTasks_RemediationCases_RemediationCaseId",
                        column: x => x.RemediationCaseId,
                        principalTable: "RemediationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalTasks_RemediationDecisions_RemediationDecisionId",
                        column: x => x.RemediationDecisionId,
                        principalTable: "RemediationDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalTasks_RemediationWorkflows_RemediationWorkflowId",
                        column: x => x.RemediationWorkflowId,
                        principalTable: "RemediationWorkflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PatchingTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationWorkflowId = table.Column<Guid>(type: "uuid", nullable: true),
                    RemediationDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatchingTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatchingTasks_RemediationCases_RemediationCaseId",
                        column: x => x.RemediationCaseId,
                        principalTable: "RemediationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatchingTasks_RemediationDecisions_RemediationDecisionId",
                        column: x => x.RemediationDecisionId,
                        principalTable: "RemediationDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PatchingTasks_RemediationWorkflows_RemediationWorkflowId",
                        column: x => x.RemediationWorkflowId,
                        principalTable: "RemediationWorkflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RemediationDecisionVulnerabilityOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Justification = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationDecisionVulnerabilityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemediationDecisionVulnerabilityOverrides_RemediationDecisi~",
                        column: x => x.RemediationDecisionId,
                        principalTable: "RemediationDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RemediationDecisionVulnerabilityOverrides_Vulnerabilities_V~",
                        column: x => x.VulnerabilityId,
                        principalTable: "Vulnerabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExposureAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceVulnerabilityExposureId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecurityProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaseCvss = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    EnvironmentalCvss = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExposureAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExposureAssessments_DeviceVulnerabilityExposures_DeviceVuln~",
                        column: x => x.DeviceVulnerabilityExposureId,
                        principalTable: "DeviceVulnerabilityExposures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExposureAssessments_SecurityProfiles_SecurityProfileId",
                        column: x => x.SecurityProfileId,
                        principalTable: "SecurityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ExposureEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceVulnerabilityExposureId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExposureEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExposureEpisodes_DeviceVulnerabilityExposures_DeviceVulnera~",
                        column: x => x.DeviceVulnerabilityExposureId,
                        principalTable: "DeviceVulnerabilityExposures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalTaskVisibleRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalTaskVisibleRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalTaskVisibleRoles_ApprovalTasks_ApprovalTaskId",
                        column: x => x.ApprovalTaskId,
                        principalTable: "ApprovalTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvancedTools_Name",
                table: "AdvancedTools",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIReports_RemediationCaseId",
                table: "AIReports",
                column: "RemediationCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_AIReports_TenantAiProfileId",
                table: "AIReports",
                column: "TenantAiProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AIReports_TenantId",
                table: "AIReports",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AIReports_TenantId_RemediationCaseId_GeneratedAt",
                table: "AIReports",
                columns: new[] { "TenantId", "RemediationCaseId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AIReports_VulnerabilityId",
                table: "AIReports",
                column: "VulnerabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalystRecommendations_RemediationCaseId",
                table: "AnalystRecommendations",
                column: "RemediationCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalystRecommendations_RemediationWorkflowId",
                table: "AnalystRecommendations",
                column: "RemediationWorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalystRecommendations_TenantId",
                table: "AnalystRecommendations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalystRecommendations_TenantId_RemediationCaseId",
                table: "AnalystRecommendations",
                columns: new[] { "TenantId", "RemediationCaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_ExpiresAt",
                table: "ApprovalTasks",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_RemediationCaseId",
                table: "ApprovalTasks",
                column: "RemediationCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_RemediationDecisionId",
                table: "ApprovalTasks",
                column: "RemediationDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_RemediationWorkflowId",
                table: "ApprovalTasks",
                column: "RemediationWorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_Status",
                table: "ApprovalTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_TenantId",
                table: "ApprovalTasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_TenantId_RemediationCaseId_Status",
                table: "ApprovalTasks",
                columns: new[] { "TenantId", "RemediationCaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_TenantId_RemediationDecisionId",
                table: "ApprovalTasks",
                columns: new[] { "TenantId", "RemediationDecisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTaskVisibleRoles_ApprovalTaskId_Role",
                table: "ApprovalTaskVisibleRoles",
                columns: new[] { "ApprovalTaskId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTaskVisibleRoles_Role_ApprovalTaskId",
                table: "ApprovalTaskVisibleRoles",
                columns: new[] { "Role", "ApprovalTaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetBusinessLabels_AssignedByRuleId",
                table: "AssetBusinessLabels",
                column: "AssignedByRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetBusinessLabels_BusinessLabelId",
                table: "AssetBusinessLabels",
                column: "BusinessLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetRiskScores_AssetId",
                table: "AssetRiskScores",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetRiskScores_TenantId",
                table: "AssetRiskScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetRiskScores_TenantId_AssetId",
                table: "AssetRiskScores",
                columns: new[] { "TenantId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetRules_TenantId",
                table: "AssetRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetRules_TenantId_Priority",
                table: "AssetRules",
                columns: new[] { "TenantId", "Priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_SecurityProfileId",
                table: "Assets",
                column: "SecurityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId",
                table: "Assets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_AssetType_DeviceActiveInTenant",
                table: "Assets",
                columns: new[] { "TenantId", "AssetType", "DeviceActiveInTenant" });

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
                name: "IX_AssetTags_AssetId_Tag",
                table: "AssetTags",
                columns: new[] { "AssetId", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetTags_TenantId_Tag",
                table: "AssetTags",
                columns: new[] { "TenantId", "Tag" });

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
                name: "IX_AuthenticatedScanRuns_TenantId_ScanProfileId_StartedAt",
                table: "AuthenticatedScanRuns",
                columns: new[] { "TenantId", "ScanProfileId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLabels_TenantId",
                table: "BusinessLabels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLabels_TenantId_Name",
                table: "BusinessLabels",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Comments_EntityType_EntityId",
                table: "Comments",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_TenantId",
                table: "Comments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionProfiles_TenantId_Name",
                table: "ConnectionProfiles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

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
                name: "IX_DeviceGroupRiskScores_TenantId",
                table: "DeviceGroupRiskScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceGroupRiskScores_TenantId_GroupKey",
                table: "DeviceGroupRiskScores",
                columns: new[] { "TenantId", "GroupKey" },
                unique: true);

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
                name: "IX_DeviceVulnerabilityExposures_DeviceId",
                table: "DeviceVulnerabilityExposures",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceVulnerabilityExposures_InstalledSoftwareId",
                table: "DeviceVulnerabilityExposures",
                column: "InstalledSoftwareId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceVulnerabilityExposures_SoftwareProductId",
                table: "DeviceVulnerabilityExposures",
                column: "SoftwareProductId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceVulnerabilityExposures_TenantId",
                table: "DeviceVulnerabilityExposures",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceVulnerabilityExposures_TenantId_DeviceId_Vulnerabilit~",
                table: "DeviceVulnerabilityExposures",
                columns: new[] { "TenantId", "DeviceId", "VulnerabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceVulnerabilityExposures_TenantId_Status",
                table: "DeviceVulnerabilityExposures",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceVulnerabilityExposures_TenantId_VulnerabilityId",
                table: "DeviceVulnerabilityExposures",
                columns: new[] { "TenantId", "VulnerabilityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceVulnerabilityExposures_VulnerabilityId",
                table: "DeviceVulnerabilityExposures",
                column: "VulnerabilityId");

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
                name: "IX_ExposureAssessments_DeviceVulnerabilityExposureId",
                table: "ExposureAssessments",
                column: "DeviceVulnerabilityExposureId");

            migrationBuilder.CreateIndex(
                name: "IX_ExposureAssessments_SecurityProfileId",
                table: "ExposureAssessments",
                column: "SecurityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ExposureAssessments_TenantId_DeviceVulnerabilityExposureId",
                table: "ExposureAssessments",
                columns: new[] { "TenantId", "DeviceVulnerabilityExposureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExposureEpisodes_DeviceVulnerabilityExposureId",
                table: "ExposureEpisodes",
                column: "DeviceVulnerabilityExposureId");

            migrationBuilder.CreateIndex(
                name: "IX_ExposureEpisodes_TenantId",
                table: "ExposureEpisodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ExposureEpisodes_TenantId_DeviceVulnerabilityExposureId_Epi~",
                table: "ExposureEpisodes",
                columns: new[] { "TenantId", "DeviceVulnerabilityExposureId", "EpisodeNumber" },
                unique: true);

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
                name: "IX_IngestionCheckpoints_IngestionRunId",
                table: "IngestionCheckpoints",
                column: "IngestionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionCheckpoints_IngestionRunId_Phase",
                table: "IngestionCheckpoints",
                columns: new[] { "IngestionRunId", "Phase" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngestionCheckpoints_TenantId_SourceKey_Phase",
                table: "IngestionCheckpoints",
                columns: new[] { "TenantId", "SourceKey", "Phase" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionRuns_TenantId_SourceKey_StartedAt",
                table: "IngestionRuns",
                columns: new[] { "TenantId", "SourceKey", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionSnapshots_TenantId_SourceKey_Status",
                table: "IngestionSnapshots",
                columns: new[] { "TenantId", "SourceKey", "Status" });

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
                name: "IX_NormalizedSoftware_CanonicalProductKey",
                table: "NormalizedSoftware",
                column: "CanonicalProductKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareAliases_NormalizedSoftwareId",
                table: "NormalizedSoftwareAliases",
                column: "NormalizedSoftwareId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareAliases_SourceSystem_ExternalSoftwareId",
                table: "NormalizedSoftwareAliases",
                columns: new[] { "SourceSystem", "ExternalSoftwareId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_DeviceAssetId",
                table: "NormalizedSoftwareInstallations",
                column: "DeviceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_SnapshotId",
                table: "NormalizedSoftwareInstallations",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_SoftwareAssetId",
                table: "NormalizedSoftwareInstallations",
                column: "SoftwareAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_TenantId",
                table: "NormalizedSoftwareInstallations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_TenantId_SnapshotId_Softwar~",
                table: "NormalizedSoftwareInstallations",
                columns: new[] { "TenantId", "SnapshotId", "SoftwareAssetId", "DeviceAssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_TenantId_SnapshotId_TenantS~",
                table: "NormalizedSoftwareInstallations",
                columns: new[] { "TenantId", "SnapshotId", "TenantSoftwareId", "DetectedVersion", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedSoftwareInstallations_TenantSoftwareId",
                table: "NormalizedSoftwareInstallations",
                column: "TenantSoftwareId");

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
                name: "IX_OrganizationalSeverities_VulnerabilityId",
                table: "OrganizationalSeverities",
                column: "VulnerabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_OwnerTeamId",
                table: "PatchingTasks",
                column: "OwnerTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_RemediationCaseId",
                table: "PatchingTasks",
                column: "RemediationCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_RemediationDecisionId",
                table: "PatchingTasks",
                column: "RemediationDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_RemediationWorkflowId",
                table: "PatchingTasks",
                column: "RemediationWorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_Status",
                table: "PatchingTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_TenantId",
                table: "PatchingTasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_TenantId_RemediationCaseId_Status",
                table: "PatchingTasks",
                columns: new[] { "TenantId", "RemediationCaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationAiJobs_TenantId",
                table: "RemediationAiJobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationAiJobs_TenantId_RemediationCaseId_RequestedAt",
                table: "RemediationAiJobs",
                columns: new[] { "TenantId", "RemediationCaseId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationAiJobs_TenantId_RemediationCaseId_Status",
                table: "RemediationAiJobs",
                columns: new[] { "TenantId", "RemediationCaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationCases_SoftwareProductId",
                table: "RemediationCases",
                column: "SoftwareProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationCases_TenantId",
                table: "RemediationCases",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationCases_TenantId_SoftwareProductId",
                table: "RemediationCases",
                columns: new[] { "TenantId", "SoftwareProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisions_ApprovalStatus",
                table: "RemediationDecisions",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisions_RemediationCaseId",
                table: "RemediationDecisions",
                column: "RemediationCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisions_RemediationWorkflowId",
                table: "RemediationDecisions",
                column: "RemediationWorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisions_TenantId",
                table: "RemediationDecisions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisions_TenantId_RemediationCaseId_ApprovalSta~",
                table: "RemediationDecisions",
                columns: new[] { "TenantId", "RemediationCaseId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisionVulnerabilityOverrides_RemediationDecisi~",
                table: "RemediationDecisionVulnerabilityOverrides",
                columns: new[] { "RemediationDecisionId", "VulnerabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisionVulnerabilityOverrides_VulnerabilityId",
                table: "RemediationDecisionVulnerabilityOverrides",
                column: "VulnerabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflows_RecurrenceSourceWorkflowId",
                table: "RemediationWorkflows",
                column: "RecurrenceSourceWorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflows_RemediationCaseId",
                table: "RemediationWorkflows",
                column: "RemediationCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflows_TenantId",
                table: "RemediationWorkflows",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflows_TenantId_RemediationCaseId_Status",
                table: "RemediationWorkflows",
                columns: new[] { "TenantId", "RemediationCaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflowStageRecords_RemediationWorkflowId_Stage",
                table: "RemediationWorkflowStageRecords",
                columns: new[] { "RemediationWorkflowId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflowStageRecords_TenantId",
                table: "RemediationWorkflowStageRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflowStageRecords_TenantId_RemediationWorkflo~",
                table: "RemediationWorkflowStageRecords",
                columns: new[] { "TenantId", "RemediationWorkflowId" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskAcceptances_RemediationCaseId",
                table: "RiskAcceptances",
                column: "RemediationCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAcceptances_Status",
                table: "RiskAcceptances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAcceptances_TenantId",
                table: "RiskAcceptances",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAcceptances_TenantId_RemediationCaseId_Status",
                table: "RiskAcceptances",
                columns: new[] { "TenantId", "RemediationCaseId", "Status" });

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
                name: "IX_SoftwareCpeBindings_NormalizedSoftwareId",
                table: "SoftwareCpeBindings",
                column: "NormalizedSoftwareId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareDescriptionJobs_TenantId",
                table: "SoftwareDescriptionJobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareDescriptionJobs_TenantId_SoftwareProductId_Status",
                table: "SoftwareDescriptionJobs",
                columns: new[] { "TenantId", "SoftwareProductId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareProducts_CanonicalProductKey",
                table: "SoftwareProducts",
                column: "CanonicalProductKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareRiskScores_SoftwareProductId",
                table: "SoftwareRiskScores",
                column: "SoftwareProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareRiskScores_TenantId",
                table: "SoftwareRiskScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareRiskScores_TenantId_SoftwareProductId",
                table: "SoftwareRiskScores",
                columns: new[] { "TenantId", "SoftwareProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceSystems_Key",
                table: "SourceSystems",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StagedAssets_IngestionRunId",
                table: "StagedAssets",
                column: "IngestionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedAssets_IngestionRunId_BatchNumber",
                table: "StagedAssets",
                columns: new[] { "IngestionRunId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedAssets_TenantId_SourceKey_ExternalId",
                table: "StagedAssets",
                columns: new[] { "TenantId", "SourceKey", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedDetectedSoftware_ScanJobId",
                table: "StagedDetectedSoftware",
                column: "ScanJobId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedDeviceSoftwareInstallations_IngestionRunId",
                table: "StagedDeviceSoftwareInstallations",
                column: "IngestionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedDeviceSoftwareInstallations_IngestionRunId_BatchNumber",
                table: "StagedDeviceSoftwareInstallations",
                columns: new[] { "IngestionRunId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedDeviceSoftwareInstallations_TenantId_SourceKey_Device~",
                table: "StagedDeviceSoftwareInstallations",
                columns: new[] { "TenantId", "SourceKey", "DeviceExternalId", "SoftwareExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilities_IngestionRunId",
                table: "StagedVulnerabilities",
                column: "IngestionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilities_IngestionRunId_BatchNumber",
                table: "StagedVulnerabilities",
                columns: new[] { "IngestionRunId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilities_IngestionRunId_TenantId_SourceKey_Id",
                table: "StagedVulnerabilities",
                columns: new[] { "IngestionRunId", "TenantId", "SourceKey", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilities_TenantId_SourceKey_ExternalId",
                table: "StagedVulnerabilities",
                columns: new[] { "TenantId", "SourceKey", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilityExposures_IngestionRunId",
                table: "StagedVulnerabilityExposures",
                column: "IngestionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilityExposures_IngestionRunId_BatchNumber",
                table: "StagedVulnerabilityExposures",
                columns: new[] { "IngestionRunId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedVulnerabilityExposures_IngestionRunId_TenantId_Source~",
                table: "StagedVulnerabilityExposures",
                columns: new[] { "IngestionRunId", "TenantId", "SourceKey", "VulnerabilityExternalId", "AssetExternalId" });

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
                name: "IX_TeamMembershipRules_TeamId",
                table: "TeamMembershipRules",
                column: "TeamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembershipRules_TenantId",
                table: "TeamMembershipRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamRiskScores_TeamId",
                table: "TeamRiskScores",
                column: "TeamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamRiskScores_TenantId",
                table: "TeamRiskScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_TenantId_Name",
                table: "Teams",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantAiProfiles_TenantId",
                table: "TenantAiProfiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantAiProfiles_TenantId_Name",
                table: "TenantAiProfiles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantRiskScoreSnapshots_TenantId",
                table: "TenantRiskScoreSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantRiskScoreSnapshots_TenantId_Date",
                table: "TenantRiskScoreSnapshots",
                columns: new[] { "TenantId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_EntraTenantId",
                table: "Tenants",
                column: "EntraTenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftware_NormalizedSoftwareId",
                table: "TenantSoftware",
                column: "NormalizedSoftwareId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftware_SnapshotId",
                table: "TenantSoftware",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftware_TenantId",
                table: "TenantSoftware",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftware_TenantId_SnapshotId_NormalizedSoftwareId",
                table: "TenantSoftware",
                columns: new[] { "TenantId", "SnapshotId", "NormalizedSoftwareId" },
                unique: true);

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
                name: "IX_TenantSoftwareRiskScores_TenantId",
                table: "TenantSoftwareRiskScores",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftwareRiskScores_TenantId_SnapshotId",
                table: "TenantSoftwareRiskScores",
                columns: new[] { "TenantId", "SnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSoftwareRiskScores_TenantSoftwareId",
                table: "TenantSoftwareRiskScores",
                column: "TenantSoftwareId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSourceConfigurations_ActiveSnapshotId",
                table: "TenantSourceConfigurations",
                column: "ActiveSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSourceConfigurations_BuildingSnapshotId",
                table: "TenantSourceConfigurations",
                column: "BuildingSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSourceConfigurations_TenantId_SourceKey",
                table: "TenantSourceConfigurations",
                columns: new[] { "TenantId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ThreatAssessments_VulnerabilityId",
                table: "ThreatAssessments",
                column: "VulnerabilityId",
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

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActions_NodeExecutionId",
                table: "WorkflowActions",
                column: "NodeExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActions_TenantId_TeamId_Status",
                table: "WorkflowActions",
                columns: new[] { "TenantId", "TeamId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowActions_WorkflowInstanceId",
                table: "WorkflowActions",
                column: "WorkflowInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_Scope_Status",
                table: "WorkflowDefinitions",
                columns: new[] { "Scope", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_TenantId_Scope_TriggerType",
                table: "WorkflowDefinitions",
                columns: new[] { "TenantId", "Scope", "TriggerType" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_TenantId_Status",
                table: "WorkflowInstances",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_WorkflowDefinitionId_Status",
                table: "WorkflowInstances",
                columns: new[] { "WorkflowDefinitionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNodeExecutions_WorkflowInstanceId_NodeId",
                table: "WorkflowNodeExecutions",
                columns: new[] { "WorkflowInstanceId", "NodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowNodeExecutions_WorkflowInstanceId_Status",
                table: "WorkflowNodeExecutions",
                columns: new[] { "WorkflowInstanceId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvancedTools");

            migrationBuilder.DropTable(
                name: "AIReports");

            migrationBuilder.DropTable(
                name: "AnalystRecommendations");

            migrationBuilder.DropTable(
                name: "ApprovalTaskVisibleRoles");

            migrationBuilder.DropTable(
                name: "AssetBusinessLabels");

            migrationBuilder.DropTable(
                name: "AssetRiskScores");

            migrationBuilder.DropTable(
                name: "AssetRules");

            migrationBuilder.DropTable(
                name: "AssetTags");

            migrationBuilder.DropTable(
                name: "AuditLogEntries");

            migrationBuilder.DropTable(
                name: "AuthenticatedScanRuns");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "ConnectionProfiles");

            migrationBuilder.DropTable(
                name: "DeviceBusinessLabels");

            migrationBuilder.DropTable(
                name: "DeviceGroupRiskScores");

            migrationBuilder.DropTable(
                name: "DeviceRiskScores");

            migrationBuilder.DropTable(
                name: "DeviceRules");

            migrationBuilder.DropTable(
                name: "DeviceScanProfileAssignments");

            migrationBuilder.DropTable(
                name: "DeviceSoftwareInstallationEpisodes");

            migrationBuilder.DropTable(
                name: "DeviceSoftwareInstallations");

            migrationBuilder.DropTable(
                name: "DeviceTags");

            migrationBuilder.DropTable(
                name: "EnrichmentJobs");

            migrationBuilder.DropTable(
                name: "EnrichmentRun");

            migrationBuilder.DropTable(
                name: "EnrichmentSourceConfigurations");

            migrationBuilder.DropTable(
                name: "ExposureAssessments");

            migrationBuilder.DropTable(
                name: "ExposureEpisodes");

            migrationBuilder.DropTable(
                name: "FeatureFlagOverrides");

            migrationBuilder.DropTable(
                name: "IngestionCheckpoints");

            migrationBuilder.DropTable(
                name: "IngestionRuns");

            migrationBuilder.DropTable(
                name: "IngestionSnapshots");

            migrationBuilder.DropTable(
                name: "NormalizedSoftwareAliases");

            migrationBuilder.DropTable(
                name: "NormalizedSoftwareInstallations");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OrganizationalSeverities");

            migrationBuilder.DropTable(
                name: "PatchingTasks");

            migrationBuilder.DropTable(
                name: "RemediationAiJobs");

            migrationBuilder.DropTable(
                name: "RemediationDecisionVulnerabilityOverrides");

            migrationBuilder.DropTable(
                name: "RemediationWorkflowStageRecords");

            migrationBuilder.DropTable(
                name: "RiskAcceptances");

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
                name: "SentinelConnectorConfigurations");

            migrationBuilder.DropTable(
                name: "SoftwareAliases");

            migrationBuilder.DropTable(
                name: "SoftwareCpeBindings");

            migrationBuilder.DropTable(
                name: "SoftwareDescriptionJobs");

            migrationBuilder.DropTable(
                name: "SoftwareRiskScores");

            migrationBuilder.DropTable(
                name: "StagedAssets");

            migrationBuilder.DropTable(
                name: "StagedDetectedSoftware");

            migrationBuilder.DropTable(
                name: "StagedDeviceSoftwareInstallations");

            migrationBuilder.DropTable(
                name: "StagedVulnerabilities");

            migrationBuilder.DropTable(
                name: "StagedVulnerabilityExposures");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "TeamMembershipRules");

            migrationBuilder.DropTable(
                name: "TeamRiskScores");

            migrationBuilder.DropTable(
                name: "TenantRiskScoreSnapshots");

            migrationBuilder.DropTable(
                name: "TenantSlaConfigurations");

            migrationBuilder.DropTable(
                name: "TenantSoftwareProductInsights");

            migrationBuilder.DropTable(
                name: "TenantSoftwareRiskScores");

            migrationBuilder.DropTable(
                name: "TenantSourceConfigurations");

            migrationBuilder.DropTable(
                name: "ThreatAssessments");

            migrationBuilder.DropTable(
                name: "UserTenantRoles");

            migrationBuilder.DropTable(
                name: "VulnerabilityApplicabilities");

            migrationBuilder.DropTable(
                name: "VulnerabilityReferences");

            migrationBuilder.DropTable(
                name: "WorkflowActions");

            migrationBuilder.DropTable(
                name: "TenantAiProfiles");

            migrationBuilder.DropTable(
                name: "ApprovalTasks");

            migrationBuilder.DropTable(
                name: "BusinessLabels");

            migrationBuilder.DropTable(
                name: "SecurityProfiles");

            migrationBuilder.DropTable(
                name: "DeviceVulnerabilityExposures");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "TenantSoftware");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WorkflowNodeExecutions");

            migrationBuilder.DropTable(
                name: "RemediationDecisions");

            migrationBuilder.DropTable(
                name: "InstalledSoftware");

            migrationBuilder.DropTable(
                name: "Vulnerabilities");

            migrationBuilder.DropTable(
                name: "AssetSecurityProfiles");

            migrationBuilder.DropTable(
                name: "NormalizedSoftware");

            migrationBuilder.DropTable(
                name: "WorkflowInstances");

            migrationBuilder.DropTable(
                name: "RemediationWorkflows");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "WorkflowDefinitions");

            migrationBuilder.DropTable(
                name: "RemediationCases");

            migrationBuilder.DropTable(
                name: "SourceSystems");

            migrationBuilder.DropTable(
                name: "SoftwareProducts");
        }
    }
}
