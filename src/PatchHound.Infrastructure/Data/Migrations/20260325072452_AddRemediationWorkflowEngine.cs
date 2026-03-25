using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRemediationWorkflowEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RemediationWorkflowId",
                table: "RemediationDecisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RemediationWorkflowId",
                table: "PatchingTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RemediationWorkflowId",
                table: "ApprovalTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RemediationWorkflowId",
                table: "AnalystRecommendations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RemediationWorkflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantSoftwareId = table.Column<Guid>(type: "uuid", nullable: false),
                    SoftwareOwnerTeamId = table.Column<Guid>(type: "uuid", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_RemediationDecisions_RemediationWorkflowId",
                table: "RemediationDecisions",
                column: "RemediationWorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_PatchingTasks_RemediationWorkflowId",
                table: "PatchingTasks",
                column: "RemediationWorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_RemediationWorkflowId",
                table: "ApprovalTasks",
                column: "RemediationWorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalystRecommendations_RemediationWorkflowId",
                table: "AnalystRecommendations",
                column: "RemediationWorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflows_TenantId",
                table: "RemediationWorkflows",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflows_TenantId_Status",
                table: "RemediationWorkflows",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationWorkflows_TenantId_TenantSoftwareId",
                table: "RemediationWorkflows",
                columns: new[] { "TenantId", "TenantSoftwareId" });

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

            migrationBuilder.AddForeignKey(
                name: "FK_AnalystRecommendations_RemediationWorkflows_RemediationWork~",
                table: "AnalystRecommendations",
                column: "RemediationWorkflowId",
                principalTable: "RemediationWorkflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalTasks_RemediationWorkflows_RemediationWorkflowId",
                table: "ApprovalTasks",
                column: "RemediationWorkflowId",
                principalTable: "RemediationWorkflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PatchingTasks_RemediationWorkflows_RemediationWorkflowId",
                table: "PatchingTasks",
                column: "RemediationWorkflowId",
                principalTable: "RemediationWorkflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RemediationDecisions_RemediationWorkflows_RemediationWorkfl~",
                table: "RemediationDecisions",
                column: "RemediationWorkflowId",
                principalTable: "RemediationWorkflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnalystRecommendations_RemediationWorkflows_RemediationWork~",
                table: "AnalystRecommendations");

            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalTasks_RemediationWorkflows_RemediationWorkflowId",
                table: "ApprovalTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_PatchingTasks_RemediationWorkflows_RemediationWorkflowId",
                table: "PatchingTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_RemediationDecisions_RemediationWorkflows_RemediationWorkfl~",
                table: "RemediationDecisions");

            migrationBuilder.DropTable(
                name: "RemediationWorkflowStageRecords");

            migrationBuilder.DropTable(
                name: "RemediationWorkflows");

            migrationBuilder.DropIndex(
                name: "IX_RemediationDecisions_RemediationWorkflowId",
                table: "RemediationDecisions");

            migrationBuilder.DropIndex(
                name: "IX_PatchingTasks_RemediationWorkflowId",
                table: "PatchingTasks");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalTasks_RemediationWorkflowId",
                table: "ApprovalTasks");

            migrationBuilder.DropIndex(
                name: "IX_AnalystRecommendations_RemediationWorkflowId",
                table: "AnalystRecommendations");

            migrationBuilder.DropColumn(
                name: "RemediationWorkflowId",
                table: "RemediationDecisions");

            migrationBuilder.DropColumn(
                name: "RemediationWorkflowId",
                table: "PatchingTasks");

            migrationBuilder.DropColumn(
                name: "RemediationWorkflowId",
                table: "ApprovalTasks");

            migrationBuilder.DropColumn(
                name: "RemediationWorkflowId",
                table: "AnalystRecommendations");
        }
    }
}
