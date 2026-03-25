using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260325113000_AddRemediationWorkflowRecurrenceLineage")]
public partial class AddRemediationWorkflowRecurrenceLineage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "RecurrenceSourceWorkflowId",
            table: "RemediationWorkflows",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RemediationWorkflows_RecurrenceSourceWorkflowId",
            table: "RemediationWorkflows",
            column: "RecurrenceSourceWorkflowId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RemediationWorkflows_RecurrenceSourceWorkflowId",
            table: "RemediationWorkflows");

        migrationBuilder.DropColumn(
            name: "RecurrenceSourceWorkflowId",
            table: "RemediationWorkflows");
    }
}
