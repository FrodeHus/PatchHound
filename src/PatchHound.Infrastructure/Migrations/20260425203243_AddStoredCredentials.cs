using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "SentinelConnectorConfigurations");

            migrationBuilder.DropColumn(
                name: "SecretRef",
                table: "SentinelConnectorConfigurations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SentinelConnectorConfigurations");

            migrationBuilder.AddColumn<Guid>(
                name: "StoredCredentialId",
                table: "TenantSourceConfigurations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StoredCredentialId",
                table: "SentinelConnectorConfigurations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StoredCredentialId",
                table: "EnrichmentSourceConfigurations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoredCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsGlobal = table.Column<bool>(type: "boolean", nullable: false),
                    CredentialTenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoredCredentialTenants",
                columns: table => new
                {
                    StoredCredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredCredentialTenants", x => new { x.StoredCredentialId, x.TenantId });
                    table.ForeignKey(
                        name: "FK_StoredCredentialTenants_StoredCredentials_StoredCredentialId",
                        column: x => x.StoredCredentialId,
                        principalTable: "StoredCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoredCredentialTenants_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSourceConfigurations_StoredCredentialId",
                table: "TenantSourceConfigurations",
                column: "StoredCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelConnectorConfigurations_StoredCredentialId",
                table: "SentinelConnectorConfigurations",
                column: "StoredCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentSourceConfigurations_StoredCredentialId",
                table: "EnrichmentSourceConfigurations",
                column: "StoredCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredCredentials_IsGlobal",
                table: "StoredCredentials",
                column: "IsGlobal");

            migrationBuilder.CreateIndex(
                name: "IX_StoredCredentials_Type",
                table: "StoredCredentials",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_StoredCredentialTenants_TenantId",
                table: "StoredCredentialTenants",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_EnrichmentSourceConfigurations_StoredCredentials_StoredCred~",
                table: "EnrichmentSourceConfigurations",
                column: "StoredCredentialId",
                principalTable: "StoredCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SentinelConnectorConfigurations_StoredCredentials_StoredCre~",
                table: "SentinelConnectorConfigurations",
                column: "StoredCredentialId",
                principalTable: "StoredCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantSourceConfigurations_StoredCredentials_StoredCredenti~",
                table: "TenantSourceConfigurations",
                column: "StoredCredentialId",
                principalTable: "StoredCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EnrichmentSourceConfigurations_StoredCredentials_StoredCred~",
                table: "EnrichmentSourceConfigurations");

            migrationBuilder.DropForeignKey(
                name: "FK_SentinelConnectorConfigurations_StoredCredentials_StoredCre~",
                table: "SentinelConnectorConfigurations");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantSourceConfigurations_StoredCredentials_StoredCredenti~",
                table: "TenantSourceConfigurations");

            migrationBuilder.DropTable(
                name: "StoredCredentialTenants");

            migrationBuilder.DropTable(
                name: "StoredCredentials");

            migrationBuilder.DropIndex(
                name: "IX_TenantSourceConfigurations_StoredCredentialId",
                table: "TenantSourceConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_SentinelConnectorConfigurations_StoredCredentialId",
                table: "SentinelConnectorConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_EnrichmentSourceConfigurations_StoredCredentialId",
                table: "EnrichmentSourceConfigurations");

            migrationBuilder.DropColumn(
                name: "StoredCredentialId",
                table: "TenantSourceConfigurations");

            migrationBuilder.DropColumn(
                name: "StoredCredentialId",
                table: "SentinelConnectorConfigurations");

            migrationBuilder.DropColumn(
                name: "StoredCredentialId",
                table: "EnrichmentSourceConfigurations");

            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "SentinelConnectorConfigurations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SecretRef",
                table: "SentinelConnectorConfigurations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SentinelConnectorConfigurations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");
        }
    }
}
