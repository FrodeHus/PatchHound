using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ActiveInTenant = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StagedCloudApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngestionRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    StagedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedCloudApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CloudApplicationCredentialMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CloudApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudApplicationCredentialMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudApplicationCredentialMetadata_CloudApplications_CloudA~",
                        column: x => x.CloudApplicationId,
                        principalTable: "CloudApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudApplicationCredentialMetadata_CloudApplicationId",
                table: "CloudApplicationCredentialMetadata",
                column: "CloudApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudApplicationCredentialMetadata_ExpiresAt",
                table: "CloudApplicationCredentialMetadata",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_CloudApplicationCredentialMetadata_TenantId",
                table: "CloudApplicationCredentialMetadata",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudApplications_TenantId",
                table: "CloudApplications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudApplications_TenantId_SourceSystemId_ExternalId",
                table: "CloudApplications",
                columns: new[] { "TenantId", "SourceSystemId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StagedCloudApplications_IngestionRunId",
                table: "StagedCloudApplications",
                column: "IngestionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedCloudApplications_TenantId_SourceKey_ExternalId",
                table: "StagedCloudApplications",
                columns: new[] { "TenantId", "SourceKey", "ExternalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudApplicationCredentialMetadata");

            migrationBuilder.DropTable(
                name: "StagedCloudApplications");

            migrationBuilder.DropTable(
                name: "CloudApplications");
        }
    }
}
