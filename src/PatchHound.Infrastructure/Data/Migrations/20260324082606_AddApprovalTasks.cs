using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovalExpiryHours",
                table: "TenantSlaConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ApprovalTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemediationDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VisibleToRoles = table.Column<string>(type: "text", nullable: false),
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
                        name: "FK_ApprovalTasks_RemediationDecisions_RemediationDecisionId",
                        column: x => x.RemediationDecisionId,
                        principalTable: "RemediationDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_ExpiresAt",
                table: "ApprovalTasks",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_RemediationDecisionId",
                table: "ApprovalTasks",
                column: "RemediationDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_Status",
                table: "ApprovalTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_TenantId",
                table: "ApprovalTasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalTasks_TenantId_RemediationDecisionId",
                table: "ApprovalTasks",
                columns: new[] { "TenantId", "RemediationDecisionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalTasks");

            migrationBuilder.DropColumn(
                name: "ApprovalExpiryHours",
                table: "TenantSlaConfigurations");
        }
    }
}
