using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRemediationDecisionSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalystRecommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantVulnerabilityId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecommendedOutcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Rationale = table.Column<string>(type: "text", nullable: false),
                    PriorityOverride = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AnalystId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalystRecommendations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemediationDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApprovalStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Justification = table.Column<string>(type: "text", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                        name: "FK_RemediationDecisions_Assets_SoftwareAssetId",
                        column: x => x.SoftwareAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatchingTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareAssetId = table.Column<Guid>(type: "uuid", nullable: false),
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
                        name: "FK_PatchingTasks_RemediationDecisions_RemediationDecisionId",
                        column: x => x.RemediationDecisionId,
                        principalTable: "RemediationDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemediationDecisionVulnerabilityOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantVulnerabilityId = table.Column<Guid>(type: "uuid", nullable: false),
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalystRecommendations_TenantId",
                table: "AnalystRecommendations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalystRecommendations_TenantId_SoftwareAssetId",
                table: "AnalystRecommendations",
                columns: new[] { "TenantId", "SoftwareAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_OwnerTeamId",
                table: "PatchingTasks",
                column: "OwnerTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_RemediationDecisionId",
                table: "PatchingTasks",
                column: "RemediationDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_Status",
                table: "PatchingTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_TenantId",
                table: "PatchingTasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_TenantId_SoftwareAssetId",
                table: "PatchingTasks",
                columns: new[] { "TenantId", "SoftwareAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisions_ApprovalStatus",
                table: "RemediationDecisions",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisions_SoftwareAssetId",
                table: "RemediationDecisions",
                column: "SoftwareAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisions_TenantId",
                table: "RemediationDecisions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisions_TenantId_SoftwareAssetId",
                table: "RemediationDecisions",
                columns: new[] { "TenantId", "SoftwareAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisionVulnerabilityOverrides_RemediationDecisi~",
                table: "RemediationDecisionVulnerabilityOverrides",
                columns: new[] { "RemediationDecisionId", "TenantVulnerabilityId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalystRecommendations");

            migrationBuilder.DropTable(
                name: "PatchingTasks");

            migrationBuilder.DropTable(
                name: "RemediationDecisionVulnerabilityOverrides");

            migrationBuilder.DropTable(
                name: "RemediationDecisions");
        }
    }
}
