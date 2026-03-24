using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Data.Migrations;

[DbContext(typeof(PatchHoundDbContext))]
[Migration("20260324093000_NormalizeApprovalTaskVisibleRoles")]
public partial class NormalizeApprovalTaskVisibleRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ApprovalTaskVisibleRoles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ApprovalTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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

        migrationBuilder.Sql(
            """
            INSERT INTO "ApprovalTaskVisibleRoles" ("Id", "ApprovalTaskId", "Role")
            SELECT
                (
                    substr(md5("ApprovalTasks"."Id"::text || ':' || trimmed_roles."Role"), 1, 8) || '-' ||
                    substr(md5("ApprovalTasks"."Id"::text || ':' || trimmed_roles."Role"), 9, 4) || '-' ||
                    substr(md5("ApprovalTasks"."Id"::text || ':' || trimmed_roles."Role"), 13, 4) || '-' ||
                    substr(md5("ApprovalTasks"."Id"::text || ':' || trimmed_roles."Role"), 17, 4) || '-' ||
                    substr(md5("ApprovalTasks"."Id"::text || ':' || trimmed_roles."Role"), 21, 12)
                )::uuid,
                "ApprovalTasks"."Id",
                trimmed_roles."Role"
            FROM "ApprovalTasks"
            CROSS JOIN LATERAL (
                SELECT trim(role_value) AS "Role"
                FROM unnest(string_to_array("ApprovalTasks"."VisibleToRoles", ',')) AS role_value
                WHERE trim(role_value) <> ''
            ) AS trimmed_roles;
            """);

        migrationBuilder.DropColumn(
            name: "VisibleToRoles",
            table: "ApprovalTasks");

        migrationBuilder.CreateIndex(
            name: "IX_ApprovalTaskVisibleRoles_ApprovalTaskId_Role",
            table: "ApprovalTaskVisibleRoles",
            columns: new[] { "ApprovalTaskId", "Role" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ApprovalTaskVisibleRoles_Role_ApprovalTaskId",
            table: "ApprovalTaskVisibleRoles",
            columns: new[] { "Role", "ApprovalTaskId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "VisibleToRoles",
            table: "ApprovalTasks",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.Sql(
            """
            UPDATE "ApprovalTasks" AS at
            SET "VisibleToRoles" = COALESCE(role_values."VisibleToRoles", '')
            FROM (
                SELECT
                    "ApprovalTaskId",
                    string_agg("Role", ',' ORDER BY "Role") AS "VisibleToRoles"
                FROM "ApprovalTaskVisibleRoles"
                GROUP BY "ApprovalTaskId"
            ) AS role_values
            WHERE at."Id" = role_values."ApprovalTaskId";
            """);

        migrationBuilder.DropTable(
            name: "ApprovalTaskVisibleRoles");
    }
}
