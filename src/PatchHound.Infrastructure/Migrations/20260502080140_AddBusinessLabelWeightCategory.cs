using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessLabelWeightCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WeightCategory",
                table: "BusinessLabels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Normal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeightCategory",
                table: "BusinessLabels");
        }
    }
}
