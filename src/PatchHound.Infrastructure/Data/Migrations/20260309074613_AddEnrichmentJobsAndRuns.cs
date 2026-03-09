using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichmentJobsAndRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveEnrichmentRunId",
                table: "EnrichmentSourceConfigurations",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseAcquiredAt",
                table: "EnrichmentSourceConfigurations",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "EnrichmentSourceConfigurations",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "EnrichmentJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    TargetModel = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalKey = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastStartedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastCompletedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LeaseOwner = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    LastError = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrichmentJobs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EnrichmentRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    StartedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CompletedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    Status = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    JobsClaimed = table.Column<int>(type: "integer", nullable: false),
                    JobsSucceeded = table.Column<int>(type: "integer", nullable: false),
                    JobsNoData = table.Column<int>(type: "integer", nullable: false),
                    JobsFailed = table.Column<int>(type: "integer", nullable: false),
                    JobsRetried = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(
                        type: "character varying(1024)",
                        maxLength: 1024,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrichmentRun", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentJobs_SourceKey_Status_NextAttemptAt",
                table: "EnrichmentJobs",
                columns: new[] { "SourceKey", "Status", "NextAttemptAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentJobs_SourceKey_TargetModel_TargetId",
                table: "EnrichmentJobs",
                columns: new[] { "SourceKey", "TargetModel", "TargetId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentJobs_TenantId",
                table: "EnrichmentJobs",
                column: "TenantId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentRun_SourceKey",
                table: "EnrichmentRun",
                column: "SourceKey"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentRun_StartedAt",
                table: "EnrichmentRun",
                column: "StartedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentSourceConfigurations_ActiveEnrichmentRunId",
                table: "EnrichmentSourceConfigurations",
                column: "ActiveEnrichmentRunId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EnrichmentJobs");

            migrationBuilder.DropTable(name: "EnrichmentRun");

            migrationBuilder.DropIndex(
                name: "IX_EnrichmentSourceConfigurations_ActiveEnrichmentRunId",
                table: "EnrichmentSourceConfigurations"
            );

            migrationBuilder.DropColumn(
                name: "ActiveEnrichmentRunId",
                table: "EnrichmentSourceConfigurations"
            );

            migrationBuilder.DropColumn(
                name: "LeaseAcquiredAt",
                table: "EnrichmentSourceConfigurations"
            );

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "EnrichmentSourceConfigurations"
            );
        }
    }
}
