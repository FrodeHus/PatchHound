using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichmentChangeLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnrichmentChangeLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EnrichmentRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    EnrichmentJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    FieldPath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OldValueJson = table.Column<string>(type: "text", nullable: true),
                    NewValueJson = table.Column<string>(type: "text", nullable: true),
                    ValueKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangeReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Confidence = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrichmentChangeLog", x => x.Id);
                    table.CheckConstraint("CK_EnrichmentChangeLog_Scope_TenantId", "(\"Scope\" = 'Global' AND \"TenantId\" IS NULL) OR (\"Scope\" = 'Tenant' AND \"TenantId\" IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentChangeLog_EnrichmentJobId",
                table: "EnrichmentChangeLog",
                column: "EnrichmentJobId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentChangeLog_EnrichmentRunId",
                table: "EnrichmentChangeLog",
                column: "EnrichmentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentChangeLog_Scope_TenantId_EntityType_EntityId_Chan~",
                table: "EnrichmentChangeLog",
                columns: new[] { "Scope", "TenantId", "EntityType", "EntityId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentChangeLog_Scope_TenantId_SourceKey_ChangedAt",
                table: "EnrichmentChangeLog",
                columns: new[] { "Scope", "TenantId", "SourceKey", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnrichmentChangeLog");
        }
    }
}
